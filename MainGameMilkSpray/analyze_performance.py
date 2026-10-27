"""Summarize MilkSpray CSV. Only frame rows are totals; camera rows are their breakdown."""
import argparse
import collections
import csv
import datetime
import pathlib
import statistics


def utc(value):
    # .NET's O format includes seven fractional digits; Python accepts six reliably.
    return datetime.datetime.strptime(value[:26], "%Y-%m-%dT%H:%M:%S.%f")


def number(row, key):
    return float(row.get(key) or 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", nargs="?", default=r"F:\kks\BepInEx\plugins\canon_plugins\MainGameMilkSpray\_logs")
    parser.add_argument("--last-seconds", type=float, default=0)
    args = parser.parse_args()
    target = pathlib.Path(args.path)
    files = list(target.glob("milk-performance*.csv")) if target.is_dir() else [target]
    rows = []
    for path in files:
        if not path.is_file():
            continue
        with path.open(encoding="utf-8-sig", newline="") as stream:
            # The last row can be incomplete while the writer is flushing.
            rows.extend(r for r in csv.DictReader(stream) if r.get("upload_ms") is not None and r.get("schema") == "1")
    if not rows:
        print("No samples yet. Restart KKS with the instrumented DLL; PerformanceLogEnabled must be true.")
        return
    session = max(rows, key=lambda r: r["utc"])["session"]
    rows = [r for r in rows if r["session"] == session]
    if args.last_seconds > 0:
        cutoff = max(utc(r["utc"]) for r in rows) - datetime.timedelta(seconds=args.last_seconds)
        rows = [r for r in rows if utc(r["utc"]) >= cutoff]
    frames = sorted((r for r in rows if r["kind"] == "frame" and r["complete"] == "1" and r["frame_gap"] == "1"), key=lambda r: int(r["frame"]))
    cameras = [r for r in rows if r["kind"] == "camera"]
    print("Session:", session, "complete frames:", len(frames), "camera calls:", len(cameras))
    print("Time UTC:", min(r["utc"] for r in rows), "to", max(r["utc"] for r in rows))
    print("Dropped rows (cumulative):", int(max(number(r, "dropped_rows") for r in rows)))
    if not frames:
        print("No complete single-frame samples yet.")
        return
    print("CPU-side elapsed milliseconds, NOT GPU duration. Nested stages must not be added together.")
    print("GC counts are process-wide. Frame span includes all game work between observer calls.")
    groups = {
        "all": frames,
        "particles present": [r for r in frames if number(r, "draw_particles") > 0 or number(r, "camera_calls") > 0],
        "no particles": [r for r in frames if number(r, "draw_particles") == 0 and number(r, "camera_calls") == 0],
        "plugin OFF": [r for r in frames if r["enabled"] == "0"],
    }
    keys = ["frame_span_ms", "late_ms", "detect_ms", "init_ms", "simulation_ms", "ghost_update_ms",
            "fire_ms", "prepare_ms", "clear_ms", "camera_ms", "sort_ms", "geometry_ms", "upload_ms"]
    for name, samples in groups.items():
        if not samples:
            continue
        print("\n[{}: {} frames] mean_ms / max_ms".format(name, len(samples)))
        for key in keys:
            values = [number(r, key) for r in samples]
            print("  {:18s} {:9.4f} / {:9.4f}".format(key, statistics.mean(values), max(values)))
    by_camera = collections.defaultdict(list)
    for row in cameras:
        by_camera[(row["camera_id"], row["camera_name"])].append(row)
    print("\n[Cameras: per-call mean_ms / max_ms]")
    for (identity, name), samples in sorted(by_camera.items()):
        print("  {} id={} calls={}".format(name, identity, len(samples)))
        for key in ["camera_ms", "sort_ms", "geometry_ms", "upload_ms"]:
            values = [number(r, key) for r in samples]
            print("    {:12s} {:9.4f} / {:9.4f}".format(key, statistics.mean(values), max(values)))
    print("\n[Slowest frames: frame span | LateUpdate | camera | geometry | upload | particles | camera calls]")
    for row in sorted(frames, key=lambda r: number(r, "frame_span_ms"), reverse=True)[:10]:
        print("  frame={} UTC={}  {}".format(row["frame"], row["utc"], " | ".join(row[k] for k in
            ["frame_span_ms", "late_ms", "camera_ms", "geometry_ms", "upload_ms", "draw_particles", "camera_calls"])))


if __name__ == "__main__":
    main()
