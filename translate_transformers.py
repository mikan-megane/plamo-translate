"""PLaMo 3 Translate 0.8B を使った日⇔英翻訳CLI.

使い方:
    python translate.py "翻訳したいテキスト"
    python translate.py --file input.txt
    python translate.py --source en --target ja "Hello, world."
    echo テキスト | python translate.py
"""

import argparse
import sys

# Windows でパイプ/stdin 使い時に出力が CP932 になって文字化けするのを防ぐ
if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

import torch
from transformers import AutoModelForCausalLM, AutoTokenizer

MODEL_NAME = "pfnet/plamo-3-translate-0.8b"

# 対応する翻訳方向: 日本語 <-> 英語
SUPPORTED_LANGS = {"ja": "日本語", "en": "英語"}

# モデルとトークナイザを 1 回だけ読み込むためのキャッシュ
_model = None
_tokenizer = None


def load_model():
    """モデルとトークナイザをロードする(初回のみGPUに載せる)."""
    global _model, _tokenizer
    if _model is None:
        print("モデルをロード中... (初回はダウンロードが発生します)", file=sys.stderr)
        _tokenizer = AutoTokenizer.from_pretrained(MODEL_NAME, trust_remote_code=True)
        _model = AutoModelForCausalLM.from_pretrained(
            MODEL_NAME,
            trust_remote_code=True,
            dtype="bfloat16",
            device_map="cuda" if torch.cuda.is_available() else "cpu",
        )
        _model.eval()
    return _model, _tokenizer


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


def translate(text: str, source_lang: str | None, target_lang: str) -> str:
    """テキストを target_lang へ翻訳する."""
    model, tokenizer = load_model()
    if source_lang is None:
        source_lang = detect_lang(text)

    inputs = tokenizer.apply_chat_template(
        [{"role": "user", "content": text}],
        source_lang=source_lang,
        target_lang=target_lang,
        add_generation_prompt=True,
        return_dict=True,
        return_tensors="pt",
    ).to(model.device)

    with torch.inference_mode():
        output = model.generate(**inputs, do_sample=False, max_new_tokens=512)

    return tokenizer.decode(
        output[0, inputs["input_ids"].shape[1]:], skip_special_tokens=True
    ).strip()


def main() -> None:
    parser = argparse.ArgumentParser(description="PLaMo 3 Translate 0.8B 日⇔英翻訳CLI")
    parser.add_argument("text", nargs="?", help="翻訳するテキスト(未指定なら --file または標準入力)")
    parser.add_argument("--file", help="翻訳するテキストファイル")
    parser.add_argument("--source", choices=sorted(SUPPORTED_LANGS), default=None,
                        help="原文の言語(未指定なら自動判定)")
    parser.add_argument("--target", choices=sorted(SUPPORTED_LANGS), default=None,
                        help="翻訳先の言語(デフォルトは原文の反対側)")
    args = parser.parse_args()

    if args.text is not None and not sys.stdin.isatty():
        # `echo 何か | python translate.py -` のような形では両方入力に使えないのでテキストを優先
        pass
    text = args.text
    if text is None:
        if args.file is not None:
            with open(args.file, encoding="utf-8") as f:
                text = f.read()
        elif not sys.stdin.isatty():
            text = sys.stdin.read()
        else:
            parser.error("翻訳するテキスト、--file、または標準入力を指定してください")

    text = text.strip()
    if not text:
        print("エラー: 翻訳するテキストが空です", file=sys.stderr)
        sys.exit(1)

    source_lang = args.source or detect_lang(text)
    target_lang = args.target or ("en" if source_lang == "ja" else "ja")
    if source_lang == target_lang:
        print(f"エラー: 原文と翻訳先が同じ言語です ({source_lang})", file=sys.stderr)
        sys.exit(1)

    result = translate(text, source_lang, target_lang)
    print(result)


if __name__ == "__main__":
    main()
