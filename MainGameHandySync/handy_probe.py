#!/usr/bin/env python3
"""
Handy2 (H02_pro / fw4.2.1) を手元から直接叩いて、滑らかに動く送り方を詰めるための道具。

ゲームに入れる前にここで完成させる。プラグインと同じ v3 HDSP 経路を使うので、
ここで滑らかに動いた組み合わせは、そのままプラグインへ移植できる。

鍵は BepInEx の設定ファイルから読む（ハードコードしない）。

使い方:
    python handy_probe.py latency            往復遅延だけ測る（デバイスは動かない）
    python handy_probe.py slam               今のプラグインと同じ t=1ms 全力往復（カクカクの再現）
    python handy_probe.py timed              端から端へ、往復にかける時間を t で指定
    python handy_probe.py sine               正弦で中間位置も送る（分割送信）
    python handy_probe.py xpva               速度指定で往復
    python handy_probe.py stop               停止（中央へゆっくり戻す）

主なオプション:
    --period 1.2    1往復の秒数
    --strokes 8     往復回数
    --min 0.1 --max 0.9   ストローク範囲
    --hz 10         sine のときの送信レート
    --va 300        xpva のときの速度
"""

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.request

CFG = r"F:\kks\BepInEx\config\com.kks.maingame.handysync.cfg"
BASE = "https://www.handyfeeling.com/api/handy-rest/v3"


def read_cfg():
    """ConnectionKey と V3Token を設定ファイルから拾う。"""
    try:
        text = open(CFG, encoding="utf-8-sig").read()
    except OSError as exc:
        sys.exit(f"設定ファイルが読めない: {exc}")

    def grab(key):
        m = re.search(rf"^{key}\s*=\s*(.+)$", text, re.M)
        return m.group(1).strip() if m else ""

    ck, ak = grab("ConnectionKey"), grab("V3Token")
    if not ck or not ak:
        sys.exit("ConnectionKey か V3Token が設定ファイルに無い")
    return ck, ak


def call(method, path, token=None, ck=None, body=None, apikey=None, timeout=15):
    """(status, json_or_text, 経過ms) を返す。例外は投げない。"""
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{BASE}{path}", data=data, method=method)
    if apikey:
        req.add_header("X-Api-Key", apikey)
    if token:
        req.add_header("Authorization", f"Bearer {token}")
        req.add_header("X-Connection-Key", ck)
    if data:
        req.add_header("Content-Type", "application/json")

    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw, status = r.read().decode(), r.getcode()
    except urllib.error.HTTPError as e:
        raw, status = e.read().decode(), e.code
    except Exception as e:
        return 0, str(e), (time.perf_counter() - t0) * 1000
    ms = (time.perf_counter() - t0) * 1000
    try:
        return status, json.loads(raw), ms
    except ValueError:
        return status, raw, ms


def connect():
    ck, ak = read_cfg()
    st, res, _ = call("GET", f"/auth/token/issue?ttl=3600&to={ck}", apikey=ak)
    if st != 200 or not isinstance(res, dict):
        sys.exit(f"トークン発行に失敗: {st} {res}")
    token = res["result"]["token"]

    st, info, _ = call("GET", "/info", token, ck)
    st2, conn, _ = call("GET", "/connected", token, ck)
    r = info.get("result", {}) if isinstance(info, dict) else {}
    online = conn.get("result", {}).get("connected") if isinstance(conn, dict) else None
    print(f"接続: {r.get('hw_model_name')} fw={r.get('fw_version')} online={online}")
    if not online:
        sys.exit("デバイスがオフライン。電源とWiFiを確認してくれ")

    st, _, _ = call("PUT", "/mode2", token, ck, {"mode": 2})
    print(f"HDSPモード設定: status={st}")
    return token, ck


def xpt(token, ck, xp, t_ms, stop=True):
    body = {"xp": round(xp, 4), "t": int(t_ms)}
    if stop:
        body["stop_on_target"] = True
    return call("PUT", "/hdsp/xpt", token, ck, body)


def xpva(token, ck, xp, va, stop=True):
    body = {"xp": round(xp, 4), "va": int(va)}
    if stop:
        body["stop_on_target"] = True
    return call("PUT", "/hdsp/xpva", token, ck, body)


def report(sent, fails, lat):
    if not lat:
        return
    lat.sort()
    n = len(lat)
    print(f"\n送信={sent} 失敗={fails}")
    print(f"往復遅延 中央値={lat[n//2]:.0f}ms 最小={lat[0]:.0f}ms "
          f"最大={lat[-1]:.0f}ms p90={lat[int(n*0.9)-1 if n>1 else 0]:.0f}ms")
    print(f"→ この遅延だと現実的な送信レートの上限は約 {1000/lat[n//2]:.1f}Hz")


# HAMP の velocity と実測ストローク数(回/分)の対応。
# 実機H02_pro で 0.8→118, 0.92→129.3 を計測（定常待ちあり30秒計測）。
# 上端ほど鈍るので、0.95超の領域は外挿であり要再計測。
BPM_SLOPE = 94.2
BPM_OFFSET = 42.6


def bpm_to_velocity(bpm):
    v = (bpm - BPM_OFFSET) / BPM_SLOPE
    return max(0.0, min(1.0, v))


def velocity_to_bpm(v):
    return BPM_SLOPE * v + BPM_OFFSET


def run_bpm(token, ck, bpm, seconds):
    """HAMPで自走させ、指定BPMのストローク数に合わせる。遅延の影響を受けない。"""
    v = bpm_to_velocity(bpm)
    print(f"\nBPM{bpm} -> velocity={v:.3f} (換算式の想定 {velocity_to_bpm(v):.1f}BPM)")
    if v >= 0.999:
        print("  ※ velocityが上限に張り付いている。これ以上速くできない")

    call("PUT", "/mode2", token, ck, {"mode": 0})
    call("PUT", "/hamp/start", token, ck, {})
    call("PUT", "/hamp/velocity", token, ck, {"velocity": v})

    print("  定常待ち5秒", flush=True)
    time.sleep(5)
    print(f"--- 計測開始（{seconds}秒） ---", flush=True)
    t0 = time.time()
    while time.time() - t0 < seconds:
        time.sleep(5)
        print(f"    {int(time.time()-t0)}秒", flush=True)
    print("--- 計測終了 ---")
    print(f"BPM{bpm}なら {seconds}秒で約 {bpm*seconds/60:.1f} 往復")

    call("PUT", "/hamp/velocity", token, ck, {"velocity": 0})
    call("PUT", "/hamp/stop", token, ck, {})
    print("停止した")


def make_funscript(period_s, lo, hi, cycles, points_per_cycle=40):
    """三角波ではなく正弦の funscript を作る。pos は 0-100 の整数、at はミリ秒。"""
    import math
    actions = []
    step_ms = period_s * 1000.0 / points_per_cycle
    for c in range(cycles):
        for i in range(points_per_cycle):
            phase = i / points_per_cycle
            v = lo + (hi - lo) * (1 - math.cos(2 * math.pi * phase)) / 2
            actions.append({"at": int(round((c * points_per_cycle + i) * step_ms)),
                            "pos": int(round(v * 100))})
    # 終端を1点足してループ境界を閉じる
    actions.append({"at": int(round(cycles * points_per_cycle * step_ms)),
                    "pos": int(round(lo * 100))})
    return {"version": "1.0", "inverted": False, "range": 100, "actions": actions}


def hssp_main(a, token, ck):
    """HSSP: スクリプトを本体へ渡し、時刻同期してループ再生させる。"""
    if a.mode == "hsspstop":
        st, res, _ = call("PUT", "/hssp/stop", token, ck, {})
        print(f"hssp/stop: {st} {res}")
        return

    if a.mode == "probe":
        for path, body in [("/hssp/setup", {}), ("/hssp/play", {}), ("/hssp/stop", {})]:
            st, res, ms = call("PUT", path, token, ck, body)
            print(f"PUT {path:<14} {st} {ms:6.0f}ms {str(res)[:220]}")
        return

    # モードをHSSP(1)へ
    st, res, _ = call("PUT", "/mode2", token, ck, {"mode": 1})
    print(f"HSSPモードへ切替: {st} {res}")

    fs = make_funscript(a.period, a.min, a.max, cycles=1)
    print(f"funscript: {len(fs['actions'])}点 / 1周期 {a.period}s")

    st, res, ms = call("PUT", "/hssp/setup", token, ck,
                       {"script": fs, "loop": True}, timeout=40)
    print(f"hssp/setup: {st} {ms:.0f}ms {str(res)[:300]}")
    if st != 200:
        print("→ setup が通らない。受け取り方が違う（URL渡しの可能性）ので probe の出力を見る")
        return

    st, srv, _ = call("GET", "/servertime", token, ck)
    now = srv.get("server_time") if isinstance(srv, dict) else None
    st, res, ms = call("PUT", "/hssp/play", token, ck,
                       {"start_time": 0, "server_time": now, "loop": True})
    print(f"hssp/play: {st} {ms:.0f}ms {str(res)[:300]}")

    for _ in range(int(a.period * a.strokes) + 2):
        time.sleep(1.0)
        st, s, _ = call("GET", "/hssp/state", token, ck)
        r = s.get("result", {}) if isinstance(s, dict) else {}
        print(f"  state play={r.get('play_state')} point={r.get('current_point')}"
              f"/{r.get('points')} t={r.get('current_time')}")

    call("PUT", "/hssp/stop", token, ck, {})
    print("停止した")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("mode", choices=["latency", "slam", "timed", "sine", "xpva", "stop",
                                    "hssp", "hsspstop", "probe"])
    p.add_argument("--period", type=float, default=1.2, help="1往復の秒数")
    p.add_argument("--strokes", type=int, default=8, help="往復回数")
    p.add_argument("--min", type=float, default=0.1)
    p.add_argument("--max", type=float, default=0.9)
    p.add_argument("--hz", type=float, default=10.0, help="sine の送信レート")
    p.add_argument("--va", type=int, default=300)
    p.add_argument("--no-stop", action="store_true", help="stop_on_target を付けない")
    a = p.parse_args()

    # HSSP系はモード2(HDSP)ではなくモード1(HSSP)が要るので、connect後に切り替える。
    token, ck = connect()
    stop = not a.no_stop
    lat, fails, sent = [], 0, 0

    if a.mode in ("hssp", "hsspstop", "probe"):
        return hssp_main(a, token, ck)

    def track(st, res, ms):
        nonlocal fails, sent
        sent += 1
        lat.append(ms)
        if st != 200:
            fails += 1
            print(f"  失敗 {st}: {res}")

    if a.mode == "latency":
        print("\n往復遅延を測る（デバイスは動かない）")
        for _ in range(20):
            track(*call("GET", "/connected", token, ck))
            time.sleep(0.05)

    elif a.mode == "stop":
        print("\n中央へゆっくり戻す")
        track(*xpt(token, ck, 0.5, 1500, stop))

    elif a.mode == "slam":
        print(f"\n今のプラグインと同じ t=1ms 全力往復 x{a.strokes}（カクカクの再現）")
        half = a.period / 2
        for i in range(a.strokes * 2):
            track(*xpt(token, ck, a.max if i % 2 == 0 else a.min, 1, stop))
            time.sleep(half)

    elif a.mode == "timed":
        print(f"\n端から端へ t={a.period/2*1000:.0f}ms かけて往復 x{a.strokes}")
        half_ms = a.period / 2 * 1000
        for i in range(a.strokes * 2):
            track(*xpt(token, ck, a.max if i % 2 == 0 else a.min, half_ms, stop))
            time.sleep(a.period / 2)

    elif a.mode == "sine":
        import math
        interval = 1.0 / a.hz
        print(f"\n正弦で中間位置も送る {a.hz}Hz x{a.strokes}往復 "
              f"(1送信あたり t={interval*1000:.0f}ms)")
        steps = int(a.period * a.hz)
        for s in range(steps * a.strokes):
            phase = (s % steps) / steps
            xp = a.min + (a.max - a.min) * (1 - math.cos(2 * math.pi * phase)) / 2
            track(*xpt(token, ck, xp, interval * 1000, stop))
            time.sleep(interval)

    elif a.mode == "xpva":
        print(f"\n速度指定 va={a.va} で往復 x{a.strokes}")
        half = a.period / 2
        for i in range(a.strokes * 2):
            track(*xpva(token, ck, a.max if i % 2 == 0 else a.min, a.va, stop))
            time.sleep(half)

    report(sent, fails, lat)


if __name__ == "__main__":
    main()
