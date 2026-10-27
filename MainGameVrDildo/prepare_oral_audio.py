"""Copy audition clips and extract the c00 fellatio breathing clips for the plugin."""
from pathlib import Path
import csv
import json
import shutil
import sys
import wave

sys.path.insert(0, r"F:\kks\work\clothing_setup\pydeps")
import UnityPy
from UnityPy.export.AudioClipConverter import extract_audioclip_samples

SOURCE = Path(r"F:\kks\voice_extract\voice_wave\c00\oral_review_20261026\audio")
BUNDLE = Path(r"F:\kks\abdata\sound\data\pcm\c00\h\01.unity3d")
DEST = Path(r"F:\kks\BepInEx\plugins\canon_plugins\MainGameVoiceFaceEventBridge\ImpactCueAudio\Oral")
REVIEW_MANIFEST = SOURCE.parent / "manifest.csv"
BREATHS = [25, 26, 27, 28, 33, 34, 35, 36, 45, 46, 47, 48]


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    for path in SOURCE.glob("*.wav"):
        shutil.copy2(path, DEST / path.name)
    needed = {f"h_ko_00_00_{number:03}" for number in BREATHS}
    env = UnityPy.load(str(BUNDLE))
    found = set()
    for obj in env.objects:
        if obj.type.name != "AudioClip":
            continue
        clip = obj.read()
        if clip.m_Name not in needed:
            continue
        samples = extract_audioclip_samples(clip)
        data = samples.get(clip.m_Name + ".wav")
        if data is None:
            data = samples.get(clip.m_Name)
        if not data or not data.startswith(b"RIFF"):
            raise RuntimeError(f"Invalid WAV for {clip.m_Name}: {list(samples)}")
        target = DEST / (clip.m_Name + ".wav")
        target.write_bytes(data)
        with wave.open(str(target), "rb") as reader:
            if reader.getsampwidth() != 2 or reader.getnchannels() not in (1, 2):
                raise RuntimeError(f"Unexpected format: {target}")
        found.add(clip.m_Name)
    if found != needed:
        raise RuntimeError(f"Missing breaths: {sorted(needed - found)}")
    with REVIEW_MANIFEST.open(encoding="utf-8-sig", newline="") as stream:
        rows = [row for row in csv.DictReader(stream) if row["group"] == "口・フェラ"]
    for row in rows:
        if not (DEST / row["filename"]).is_file():
            raise RuntimeError(f"Missing oral WAV: {row['filename']}")
    manifest = {"items": [{"file": row["filename"], "text": row["caption"]} for row in rows]}
    (DEST / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Ready: {len(rows)} selected oral clips, {len(found)} breaths in {DEST}")


if __name__ == "__main__":
    main()
