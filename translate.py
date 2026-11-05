"""PLaMo 3 Translate 2B (GGUF / llama-server) を使った日⇔英翻訳CLI.

使い方:
    python translate.py "翻訳したいテキスト"
    python translate.py --file input.txt
    python translate.py --source en --target ja "Hello, world."
    echo テキスト | python translate.py

サーバー管理:
    python translate.py --server start    # llama-server を起動(未起動の場合)
    python translate.py --server stop     # llama-server を停止
    python translate.py --server status   # 動作状態を確認

llama-server (http://127.0.0.1:8080) が未起動のときは翻訳前に自動で起動します。
"""

import argparse
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

# Windows でパイプ/stdin 使用時に出力が CP932 になって文字化けするのを防ぐ
if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

BASE_DIR = Path(__file__).resolve().parent
LLAMA_SERVER_EXE = BASE_DIR / "llama-bin" / "llama-server.exe"
MODEL_GGUF = BASE_DIR / "models" / "plamo-3-translate-2b-Q4_K_M.gguf"
HOST = "127.0.0.1"
PORT = 8080
BASE_URL = f"http://{HOST}:{PORT}"
CONTEXT_SIZE = 8192

# 対応する翻訳方向: 日本語 <-> 英語
SUPPORTED_LANGS = {"ja": "日本語", "en": "英語"}

PFML_TAG = "<|plamo:tag|>"
PFML_MSG = "<|plamo:msg|>"
PFML_KEY = "<|plamo:key|>"
PFML_VAL = "<|plamo:val|>"
STOP_STRINGS = [PFML_TAG, "<|plamo:bos|>"]


def http_json(path: str, payload: dict | None = None, timeout: float = 120.0) -> dict | None:
    """JSONをPOST/GETし、レスポンスをdictで返す(失敗時はNone)."""
    url = BASE_URL + path
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST" if data is not None else "GET",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as res:
            return json.loads(res.read().decode("utf-8"))
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError):
        return None


def is_server_up() -> bool:
    resp = http_json("/health", timeout=3.0)
    return resp is not None and resp.get("status") == "ok"


def start_server(wait_seconds: float = 30.0) -> bool:
    """llama-server を起動し、healthチェックが通るまで待つ."""
    if is_server_up():
        return True
    if not LLAMA_SERVER_EXE.exists():
        print(f"エラー: llama-server が見つかりません: {LLAMA_SERVER_EXE}", file=sys.stderr)
        return False

    print("llama-server を起動中...", file=sys.stderr)
    subprocess.Popen(
        [str(LLAMA_SERVER_EXE), "-m", str(MODEL_GGUF), "-c", str(CONTEXT_SIZE),
         "--port", str(PORT), "--host", HOST],
        cwd=str(BASE_DIR),
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    deadline = time.time() + wait_seconds
    while time.time() < deadline:
        if is_server_up():
            return True
        time.sleep(0.5)
    print("エラー: llama-server の起動がタイムアウトしました", file=sys.stderr)
    return False


def stop_server() -> None:
    if not is_server_up():
        print("llama-server は起動していません")
        return
    # /shutdown は応答が返らず切断されることがあるため結果は確認しない
    http_json("/shutdown", timeout=10.0)
    time.sleep(1.0)
    if not is_server_up():
        print("llama-server を停止しました")
    else:
        # /shutdown が効かない場合は直接プロセスを終了
        subprocess.run(["taskkill", "/IM", "llama-server.exe", "/F"],
                       capture_output=True, check=False)
        print("llama-server を強制終了しました")


def build_prompt(text: str, source_lang: str, target_lang: str) -> str:
    """PFML3 形式の翻訳プロンプトを組み立てる."""
    return (
        f"{PFML_TAG}dataset{PFML_MSG}translation"
        f"{PFML_TAG}input{PFML_KEY}lang{PFML_VAL}{source_lang}{PFML_MSG}{text}"
        f"{PFML_TAG}output{PFML_KEY}lang{PFML_VAL}{target_lang}{PFML_MSG}"
    )


def detect_lang(text: str) -> str:
    """ひらがな・カタカナ・漢字を含むテキストは日本語とみなす."""
    for ch in text:
        if (
            "\u3040" <= ch <= "\u309f"  # ひらがな
            or "\u30a0" <= ch <= "\u30ff"  # カタカナ
            or "\u4e00" <= ch <= "\u9fff"  # 漢字
        ):
            return "ja"
    return "en"


def translate(text: str, source_lang: str, target_lang: str, timeout: float) -> str:
    """llama-server に翻訳を依頼して結果を返す."""
    resp = http_json("/v1/completions", {
        "prompt": build_prompt(text, source_lang, target_lang),
        "temperature": 0,
        "max_tokens": 512,
        "stop": STOP_STRINGS,
        "cache_prompt": True,  # 直前の入力と似ている場合の再計算を省略
    }, timeout=timeout)
    if resp is None:
        raise RuntimeError("llama-server へのリクエストが失敗しました")
    choices = resp.get("choices")
    if not choices:
        raise RuntimeError("llama-server から予期しない応答が返りました")
    return str(choices[0].get("text", "")).strip()


def main() -> None:
    parser = argparse.ArgumentParser(description="PLaMo 3 Translate 2B 日⇔英翻訳CLI")
    parser.add_argument("text", nargs="*", help="翻訳するテキスト(未指定なら --file または標準入力)")
    parser.add_argument("--file", help="翻訳するテキストファイル")
    parser.add_argument("--source", choices=sorted(SUPPORTED_LANGS), default=None,
                        help="原文の言語(未指定なら自動判定)")
    parser.add_argument("--target", choices=sorted(SUPPORTED_LANGS), default=None,
                        help="翻訳先の言語(デフォルトは原文の反対側)")
    parser.add_argument("--server", choices=["start", "stop", "status"], metavar="{start,stop,status}",
                        help="llama-server を管理する")
    parser.add_argument("--timeout", type=float, default=120.0,
                        help="翻訳1回のタイムアウト秒数(デフォルト: 120)")
    args = parser.parse_args()

    # --- サーバー管理モード ---
    if args.server == "status":
        print("llama-server: 起動中" if is_server_up() else "llama-server: 停止中")
        return
    if args.server == "stop":
        stop_server()
        return
    if args.server == "start":
        if is_server_up():
            print("llama-server は既に起動しています")
        elif start_server():
            print("llama-server を起動しました")
        return

    # --- 翻訳モード ---
    text = " ".join(args.text).strip()
    if not text and args.file is not None:
        text = Path(args.file).read_text(encoding="utf-8").strip()
    if not text and not sys.stdin.isatty():
        text = sys.stdin.read().strip()
    if not text:
        parser.error("翻訳するテキスト、--file、または標準入力を指定してください")

    source_lang = args.source or detect_lang(text)
    target_lang = args.target or ("en" if source_lang == "ja" else "ja")
    if source_lang == target_lang:
        print(f"エラー: 原文と翻訳先が同じ言語です ({source_lang})", file=sys.stderr)
        sys.exit(1)

    if not start_server():
        sys.exit(1)

    try:
        print(translate(text, source_lang, target_lang, args.timeout))
    except RuntimeError as e:
        print(f"エラー: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
