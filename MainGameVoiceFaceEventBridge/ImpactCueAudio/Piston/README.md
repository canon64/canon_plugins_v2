# Continuous piston voices

Place WAV files and `manifest.json` in this directory. Each `items[]` entry needs only `file` (a WAV filename in this directory) and `text` (its subtitle). Only listed WAV files are played. Restart the game after changing the files. No registration in `selection.json` is needed.

Example `manifest.json`:

```json
{
  "items": [
    { "file": "001.wav", "text": "あっ" },
    { "file": "002.wav", "text": "んっ" }
  ]
}
```

The existing full SBV2 manifest also works; extra fields are ignored. An older entry with `id` and `text` is still accepted and maps `1` to `001.wav`. The loader accepts PCM WAV files up to 30 seconds and skips unsupported files. If the manifest is missing or cannot be parsed, the loader discovers all WAV files as before, without subtitles.

The game installation path is `BepInEx/plugins/canon_plugins/MainGameVoiceFaceEventBridge/ImpactCueAudio/Piston/`.
