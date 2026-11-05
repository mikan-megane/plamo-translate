# plamo-translate

[PLaMo 3 Translate 2B](https://huggingface.co/pfnet/plamo-3-translate-2b-GGUF) (GGUF Q4_K_M) + llama.cpp を使った日⇔英翻訳CLI。

## 構成

- `translate.py` — メインCLI(llama-server と通信、標準ライブラリのみで動作)
- `translate_transformers.py` — transformers 版(0.8B BF16、比較・退避用)
- `llama-bin/` — llama.cpp CUDA ビルド(b12121)
- `models/` — モデルファイル(GGUF)

## 使い方

```bash
# 日本語 → 英語 (言語は自動判定)
python translate.py "会議は来週の月曜日に変更されました。"

# 英語 → 日本語
python translate.py "The meeting has been rescheduled for next Monday."

# 言語を明示的に指定
python translate.py --source ja --target en "こんにちは"

# ファイルを翻訳
python translate.py --file input.txt

# 標準入力から
echo "こんにちは" | python translate.py
```

llama-server が未起動のときは自動で起動します(初回のみ数秒待ち)。

### サーバー管理

```bash
python translate.py --server start    # 明示的に起動
python translate.py --server stop     # 停止
python translate.py --server status   # 状態確認
```

## 性能 (RTX 3070 実測)

| 指標 | 値 |
|---|---|
| 翻訳1文の応答 | 0.4〜0.8 秒 |
| 生成速度 | 約60〜95 tok/s |
| VRAM消費(常駐時) | 約2.1 GB |

## transformers 版を使う場合

```bash
pip install -U torch --index-url https://download.pytorch.org/whl/cu126
pip install -U transformers accelerate numba
python translate_transformers.py "会議は来週の月曜日に変更されました。"
```

## ライセンス

モデルは [PLaMo Community License](https://plamo.preferredai.jp/info/plamo-community-license-ja)。
個人利用・非商用(学術利用含む)は無料。商用利用は年収など条件あり。
