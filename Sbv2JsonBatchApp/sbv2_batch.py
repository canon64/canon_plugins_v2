"""Local JSON/CSV-to-SBV2 batch synthesizer. Standard library only."""

from __future__ import annotations

import csv
import io
import json
import os
import queue
import re
import shutil
import subprocess
import threading
import time
import urllib.parse
import urllib.request
import wave
from datetime import datetime
from pathlib import Path
from tkinter import END, BOTH, LEFT, RIGHT, X, BooleanVar, StringVar, Tk, filedialog, messagebox, ttk
from tkinter.scrolledtext import ScrolledText


TEXT_KEYS = ("text", "moan", "line", "voice", "dialogue", "phrase", "utterance", "セリフ", "台詞", "喘ぎ", "音声", "本文", "内容", "発話")
METADATA_KEYS = ("id", "index", "no", "number", "intensity", "score", "weight", "file", "filename", "duration", "強度", "番号")
DEFAULT_PARAMS = {
    "speaker_id": 0,
    "style": "Neutral",
    "style_weight": 1,
    "sdp_ratio": 0.2,
    "noise": 0.6,
    "noisew": 0.8,
    "length": 1,
    "language": "JP",
    "auto_split": "false",
}
AUDIO_ROOT = Path(r"F:\kks\BepInEx\plugins\canon_plugins\MainGameVoiceFaceEventBridge\ImpactCueAudio")
SUB_PC_URL = "http://192.168.11.30:5000"
MAIN_PC_URL = "http://127.0.0.1:5000"
SERVER_PRESETS = {"サブPC": SUB_PC_URL, "メインPC": MAIN_PC_URL}
LOCAL_SBV2_ROOT = Path(r"J:\ai_tools\voice\sbv2\app")
LOCAL_MODEL_SOURCE = LOCAL_SBV2_ROOT / "model_assets"
LOCAL_MODEL_SERVING = LOCAL_SBV2_ROOT / "model_assets_batch_app"
LOCAL_SERVER_LOG = Path(__file__).with_name("main_pc_server.log")


def _checkpoint_order(path):
    match = re.search(r"_s(\d+)(?:\.[^.]+)$", path.name)
    return (int(match.group(1)) if match else -1, path.stat().st_mtime_ns)


def prepare_local_model_assets(source=LOCAL_MODEL_SOURCE, serving=LOCAL_MODEL_SERVING):
    """Expose one, latest checkpoint per local model to server_fastapi.py."""
    if not source.is_dir():
        raise FileNotFoundError(f"メインPCのSBV2モデルフォルダがありません: {source}")
    serving.mkdir(parents=True, exist_ok=True)
    names = []
    for model_dir in sorted((p for p in source.iterdir() if p.is_dir()), key=lambda p: p.name.casefold()):
        required = (model_dir / "config.json", model_dir / "style_vectors.npy")
        candidates = list(model_dir.glob("*.safetensors"))
        if not candidates or not all(p.is_file() for p in required):
            continue
        latest = max(candidates, key=_checkpoint_order)
        target = serving / model_dir.name
        target.mkdir(exist_ok=True)
        for old in target.glob("*.safetensors"):
            if old.name != latest.name or not os.path.samefile(old, latest):
                old.unlink()
        linked = target / latest.name
        if not linked.exists():
            os.link(latest, linked)
        for item in required:
            shutil.copy2(item, target / item.name)
        names.append(model_dir.name)
    if not names:
        raise RuntimeError(f"メインPCに利用可能なSBV2モデルがありません: {source}")
    return names


def start_local_server(timeout=90):
    """Start the main PC's SBV2 server if it is not already answering."""
    try:
        return get_models(MAIN_PC_URL, timeout=2)
    except Exception:
        pass
    prepare_local_model_assets()
    python = LOCAL_SBV2_ROOT / "venv" / "Scripts" / "python.exe"
    script = LOCAL_SBV2_ROOT / "server_fastapi.py"
    if not python.is_file() or not script.is_file():
        raise FileNotFoundError("メインPCのSBV2サーバー実行ファイルが見つかりません")
    with LOCAL_SERVER_LOG.open("ab") as log:
        subprocess.Popen(
            [str(python), str(script), "--dir", str(LOCAL_MODEL_SERVING)],
            cwd=LOCAL_SBV2_ROOT, stdout=log, stderr=subprocess.STDOUT,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            return get_models(MAIN_PC_URL, timeout=2)
        except Exception:
            time.sleep(1)
    raise TimeoutError(f"メインPCサーバーが応答しません。ログ: {LOCAL_SERVER_LOG}")


def extract_lines(value):
    """Return text plus source location; ignore numeric metadata such as intensity."""
    lines = []

    def visit(node, path):
        if isinstance(node, str):
            if node.strip():
                lines.append({"text": node.strip(), "source": path})
        elif isinstance(node, list):
            for index, item in enumerate(node):
                visit(item, f"{path}[{index}]")
        elif isinstance(node, dict):
            for key in TEXT_KEYS:
                if isinstance(node.get(key), str):
                    visit(node[key], f"{path}.{key}")
                    return
            for key, item in node.items():
                if isinstance(item, list):
                    visit(item, f"{path}.{key}")

    visit(value, "$")
    if not lines:
        raise ValueError("音声化できる文字列がありません。文字列配列、または text / moan を含む配列を入力してください。")
    return lines


def _csv_rows(raw):
    reader = csv.reader(io.StringIO(raw, newline=""))
    return [(reader.line_num, row) for row in reader if any(cell.strip() for cell in row)]


def csv_columns(raw):
    rows = _csv_rows(raw.lstrip("\ufeff"))
    return [cell.strip() for cell in rows[0][1]] if rows else []


def _looks_numeric(value):
    try:
        float(value.strip())
        return True
    except ValueError:
        return False


def parse_input_text(raw, csv_column=None):
    """Accept JSON or CSV; csv_column is a zero-based selection for unknown headers."""
    raw = raw.lstrip("\ufeff").strip()
    if not raw:
        raise ValueError("JSONまたはCSVを入力してください")
    try:
        return extract_lines(json.loads(raw))
    except json.JSONDecodeError as exc:
        if raw[0] in "[{":
            raise ValueError(f"JSONの形式が正しくありません: {exc}") from exc

    rows = _csv_rows(raw)
    if not rows:
        raise ValueError("CSVに音声化できる行がありません")
    header = [cell.strip() for cell in rows[0][1]]
    lowered = [cell.casefold() for cell in header]
    column = csv_column
    has_header = False
    if column is not None:
        if column < 0 or column >= len(header):
            raise ValueError("選択したCSV列が見つかりません")
        has_header = not any(_looks_numeric(cell) for cell in header)
    else:
        column = next((index for index, name in enumerate(lowered) if name in TEXT_KEYS), None)
        if column is not None:
            has_header = True
        elif len(header) == 1:
            column = 0
            has_header = lowered[0] in TEXT_KEYS
        else:
            candidates = []
            for index, name in enumerate(lowered):
                values = [row[index].strip() for _, row in rows[1:] if len(row) > index and row[index].strip()]
                if name not in METADATA_KEYS and values and any(not _looks_numeric(value) for value in values):
                    candidates.append(index)
            if len(candidates) == 1:
                column = candidates[0]
                has_header = not any(_looks_numeric(cell) for cell in header)
            else:
                first_text_columns = [index for index, value in enumerate(header) if value and not _looks_numeric(value)]
                if len(first_text_columns) == 1 and all(
                    len(row) > first_text_columns[0] and row[first_text_columns[0]].strip()
                    for _, row in rows
                ):
                    column = first_text_columns[0]
                else:
                    names = ", ".join(f"{index + 1}:{name or '(空欄)'}" for index, name in enumerate(header))
                    raise ValueError("音声にするCSV列を画面の「CSV音声列」で選んでください: " + names)
    if has_header:
        rows = rows[1:]
    lines = [
        {"text": row[column].strip(), "source": f"csv:{line_number}"}
        for line_number, row in rows
        if len(row) > column and row[column].strip()
    ]
    if not lines:
        raise ValueError("CSVに音声化できる文字列がありません")
    return lines


def get_models(base_url, timeout=8):
    url = base_url.rstrip("/") + "/models/info"
    with urllib.request.urlopen(url, timeout=timeout) as response:
        data = json.load(response)
    models = {}
    for model in data.values():
        name = Path(str(model["config_path"]).replace("\\", "/")).parent.name
        models[name] = model
    return models


def check_local_model_version(base_url, model_name, models):
    if base_url != MAIN_PC_URL or model_name != "c00_fera_chestfera_200":
        return
    source = LOCAL_MODEL_SOURCE / model_name
    latest = max(source.glob("*.safetensors"), key=_checkpoint_order)
    served = Path(str(models[model_name]["model_path"]).replace("\\", "/"))
    if served.name != latest.name:
        raise ValueError(
            f"メインPCサーバーは古いモデル {served.name} を使用中です。"
            "既存のSBV2サーバーを終了し、接続先で「メインPC」を選び直してください。"
        )


def synthesize(base_url, model_name, text, params=None, timeout=120):
    arguments = dict(DEFAULT_PARAMS if params is None else params)
    arguments.update(model_name=model_name, text=text)
    url = base_url.rstrip("/") + "/voice?" + urllib.parse.urlencode(arguments)
    request = urllib.request.Request(url, data=b"", method="POST")
    with urllib.request.urlopen(request, timeout=timeout) as response:
        data = response.read()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError("SBV2がWAV以外を返しました")
    with wave.open(io.BytesIO(data), "rb") as audio:
        info = {
            "duration": round(audio.getnframes() / audio.getframerate(), 3),
            "sample_rate": audio.getframerate(),
            "channels": audio.getnchannels(),
        }
    return data, info


def generate_batch(lines, base_url, model_name, output_root, progress=None, stop=None, direct_piston=False):
    output_root = Path(output_root)
    output_root.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    if direct_piston:
        output_dir = output_root
        kind = "".join(c.lower() if c.isascii() and c.isalnum() else "_" for c in output_root.name).strip("_") or "audio"
        prefix = f"{kind}_{stamp}"
        suffix = 2
        while any(output_dir.glob(prefix + "_*.wav")):
            prefix = f"{kind}_{stamp}_{suffix}"
            suffix += 1
    else:
        output_dir = output_root / f"sbv2_batch_{stamp}"
        suffix = 2
        while output_dir.exists():
            output_dir = output_root / f"sbv2_batch_{stamp}_{suffix}"
            suffix += 1
        output_dir.mkdir()
        prefix = ""
    results = []
    for index, item in enumerate(lines, 1):
        if stop is not None and stop.is_set():
            break
        filename = f"{prefix}_{index:03d}.wav" if direct_piston else f"{index:03d}.wav"
        row = {"id": index, **item, "file": filename}
        try:
            audio, info = synthesize(base_url, model_name, item["text"])
            (output_dir / row["file"]).write_bytes(audio)
            row.update(status="ready", **info)
        except Exception as exc:
            row.update(status="error", error=str(exc))
        results.append(row)
        if progress:
            progress(index, len(lines), row)
    playback_manifest = {"items": [
        {"file": row["file"], "text": row["text"]}
        for row in results if row["status"] == "ready" and (output_dir / row["file"]).is_file()
    ]}
    if playback_manifest["items"]:
        live_path = output_dir / "manifest.json"
        temp_path = output_dir / ("manifest.json.tmp")
        temp_path.write_text(json.dumps(playback_manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        os.replace(temp_path, live_path)
    return output_dir, results


class App:
    def __init__(self, root):
        self.root = root
        root.title("SBV2 JSON・CSV 一括音声化")
        root.geometry("850x730")
        self.server_choice = StringVar(value="サブPC")
        self.server = StringVar(value=SUB_PC_URL)
        self.model = StringVar(value="セクシー")
        self.output = StringVar(value=str(AUDIO_ROOT))
        self.direct_piston = BooleanVar(value=True)
        self.csv_column = StringVar(value="自動判定")
        self.status = StringVar(value="JSONまたはCSVを貼り付け、モデルを確認してください")
        self.events = queue.Queue()
        self.stop_event = threading.Event()
        self.running = False

        frame = ttk.Frame(root, padding=12)
        frame.pack(fill=BOTH, expand=True)
        ttk.Label(frame, text="SBV2サーバー").pack(anchor="w")
        source_row = ttk.Frame(frame)
        source_row.pack(fill=X, pady=(2, 4))
        self.server_choice_box = ttk.Combobox(
            source_row, textvariable=self.server_choice,
            values=["サブPC", "メインPC", "手入力"], state="readonly", width=12)
        self.server_choice_box.pack(side=LEFT)
        self.server_choice_box.bind("<<ComboboxSelected>>", self.select_server)
        ttk.Label(source_row, text="メインPCはローカルの学習済みモデルを使用").pack(side=LEFT, padx=(9, 0))
        server_row = ttk.Frame(frame)
        server_row.pack(fill=X, pady=(0, 9))
        ttk.Entry(server_row, textvariable=self.server).pack(side=LEFT, fill=X, expand=True)
        ttk.Button(server_row, text="モデル取得", command=self.load_models).pack(side=RIGHT, padx=(8, 0))
        ttk.Label(frame, text="モデル").pack(anchor="w")
        self.model_box = ttk.Combobox(frame, textvariable=self.model)
        self.model_box.pack(fill=X, pady=(2, 9))
        ttk.Label(frame, text="JSON・CSV（text / moan 列、または1列だけのCSV）").pack(anchor="w")
        self.input = ScrolledText(frame, height=19, wrap="word", font=("Yu Gothic UI", 10))
        self.input.pack(fill=BOTH, expand=True, pady=(2, 9))
        ttk.Button(frame, text="JSON・CSVファイルを開く", command=self.open_input_file).pack(anchor="w", pady=(0, 9))
        ttk.Label(frame, text="CSV音声列（自動判定で違う列を選ぶ場合に指定）").pack(anchor="w")
        self.csv_column_box = ttk.Combobox(frame, textvariable=self.csv_column, values=["自動判定"], state="readonly")
        self.csv_column_box.pack(fill=X, pady=(2, 9))
        ttk.Label(frame, text="保存先").pack(anchor="w")
        output_row = ttk.Frame(frame)
        output_row.pack(fill=X, pady=(2, 9))
        ttk.Entry(output_row, textvariable=self.output).pack(side=LEFT, fill=X, expand=True)
        ttk.Button(output_row, text="選択", command=self.choose_output).pack(side=RIGHT, padx=(8, 0))
        targets = ttk.Frame(frame)
        targets.pack(fill=X, pady=(0, 4))
        ttk.Label(targets, text="直下のフォルダ").pack(side=LEFT)
        self.folder_buttons = ttk.Frame(frame)
        self.folder_buttons.pack(fill=X, pady=(0, 4))
        ttk.Button(targets, text="更新", command=self.refresh_folder_buttons).pack(side=LEFT, padx=(8, 0))
        self.refresh_folder_buttons()
        ttk.Checkbutton(frame, text="指定フォルダへ直接保存（再生用 manifest.json を更新）",
                        variable=self.direct_piston).pack(anchor="w", pady=(0, 9))
        actions = ttk.Frame(frame)
        actions.pack(fill=X)
        self.start_button = ttk.Button(actions, text="全件を音声化", command=self.start)
        self.start_button.pack(side=LEFT)
        ttk.Button(actions, text="停止", command=self.stop_event.set).pack(side=LEFT, padx=8)
        self.progress = ttk.Progressbar(frame, mode="determinate")
        self.progress.pack(fill=X, pady=(10, 4))
        ttk.Label(frame, textvariable=self.status, wraplength=790).pack(anchor="w")
        root.after(100, self.drain_events)

    def open_input_file(self):
        path = filedialog.askopenfilename(filetypes=[("JSON・CSV", "*.json *.csv"), ("JSON", "*.json"), ("CSV", "*.csv"), ("すべて", "*.*")])
        if path:
            self.input.delete("1.0", END)
            try:
                content = Path(path).read_text(encoding="utf-8-sig")
            except UnicodeDecodeError:
                content = Path(path).read_text(encoding="cp932")
            self.input.insert("1.0", content)
            self.refresh_csv_columns(content)

    def refresh_csv_columns(self, raw):
        try:
            headers = csv_columns(raw) if raw.lstrip().lstrip("\ufeff").strip()[:1] not in ("[", "{") else []
        except csv.Error:
            headers = []
        choices = ["自動判定"] + [f"{index + 1}: {name or '(空欄)'}" for index, name in enumerate(headers)]
        self.csv_column_box["values"] = choices
        if self.csv_column.get() not in choices:
            self.csv_column.set("自動判定")

    def choose_output(self):
        path = filedialog.askdirectory()
        if path:
            self.output.set(path)

    def refresh_folder_buttons(self):
        for child in self.folder_buttons.winfo_children():
            child.destroy()
        if not AUDIO_ROOT.is_dir():
            return
        for folder in sorted((p for p in AUDIO_ROOT.iterdir() if p.is_dir()), key=lambda p: p.name.casefold()):
            ttk.Button(self.folder_buttons, text=folder.name,
                       command=lambda path=folder: self.output.set(str(path))).pack(side=LEFT, padx=(0, 6), pady=2)

    def load_models(self):
        base = self.server.get().strip()
        self.model_box["values"] = ()
        self.model.set("")
        self.status.set(f"モデル取得中: {base}")
        def work():
            try:
                models = start_local_server() if base == MAIN_PC_URL else get_models(base)
                self.events.put(("models", base, models))
            except Exception as exc:
                self.events.put(("model_error", base, f"モデル取得失敗: {exc}"))
        threading.Thread(target=work, daemon=True).start()

    def select_server(self, _event=None):
        choice = self.server_choice.get()
        if choice in SERVER_PRESETS:
            self.server.set(SERVER_PRESETS[choice])
            self.load_models()
        else:
            self.model_box["values"] = ()
            self.model.set("")
            self.status.set("接続先URLを入力して「モデル取得」を押してください")

    def start(self):
        if self.running:
            return
        try:
            raw = self.input.get("1.0", END)
            self.refresh_csv_columns(raw)
            selection = self.csv_column.get()
            column = None if selection == "自動判定" else int(selection.split(":", 1)[0]) - 1
            lines = parse_input_text(raw, column)
            if not self.server.get().strip() or not self.model.get().strip():
                raise ValueError("SBV2サーバーとモデルを指定してください")
            if not self.output.get().strip():
                raise ValueError("保存先を指定してください")
            if Path(self.output.get().strip()) == AUDIO_ROOT:
                raise ValueError("ImpactCueAudio 直下の保存フォルダを選んでください")
        except ValueError as exc:
            messagebox.showerror("入力エラー", str(exc))
            return
        self.running = True
        self.stop_event.clear()
        self.start_button.state(["disabled"])
        self.progress.configure(maximum=len(lines), value=0)
        self.status.set(f"{len(lines)}件を生成します")
        base, model, output = self.server.get().strip(), self.model.get().strip(), self.output.get().strip()
        direct_piston = self.direct_piston.get()
        def work():
            try:
                models = start_local_server() if base == MAIN_PC_URL else get_models(base)
                if model not in models:
                    raise ValueError(f"モデル「{model}」がサーバーにありません")
                check_local_model_version(base, model, models)
                result = generate_batch(
                    lines, base, model, output,
                    progress=lambda i, total, row: self.events.put(("progress", i, total, row)),
                    stop=self.stop_event,
                    direct_piston=direct_piston,
                )
                self.events.put(("done", result))
            except Exception as exc:
                self.events.put(("error", str(exc)))
        threading.Thread(target=work, daemon=True).start()

    def drain_events(self):
        while True:
            try:
                event = self.events.get_nowait()
            except queue.Empty:
                break
            kind = event[0]
            if kind == "models":
                _, base, models = event
                if base != self.server.get().strip():
                    continue
                names = sorted(models, key=str.casefold)
                self.model_box["values"] = names
                if self.model.get() not in names and names:
                    preferred = "c00_fera_chestfera_200" if base == MAIN_PC_URL else "セクシー"
                    self.model.set(preferred if preferred in names else names[0])
                self.status.set(f"{base} のモデル {len(names)}件: " + ", ".join(names))
            elif kind == "model_error":
                _, base, detail = event
                if base == self.server.get().strip():
                    self.status.set(detail)
            elif kind == "progress":
                _, index, total, row = event
                self.progress.configure(value=index)
                self.status.set(f"{index}/{total}  {row['status']}  {row['file']}")
            elif kind == "done":
                self.running = False
                self.start_button.state(["!disabled"])
                output_dir, results = event[1]
                ready = sum(row["status"] == "ready" for row in results)
                self.status.set(f"完了: {ready}/{len(results)}件  {output_dir / 'manifest.json'}")
                messagebox.showinfo("生成結果", self.status.get())
            elif kind == "error":
                self.running = False
                self.start_button.state(["!disabled"])
                self.status.set(event[1])
                messagebox.showerror("エラー", event[1])
        self.root.after(100, self.drain_events)


if __name__ == "__main__":
    App(Tk()).root.mainloop()
