# plamo-translate

[PLaMo 3 Translate 2B](https://huggingface.co/pfnet/plamo-3-translate-2b-GGUF) (GGUF Q4_K_M) + llama.cpp を使った日⇔英翻訳ツール集。

## 構成

- `translate.py` — 翻訳CLI(llama-server と通信、標準ライブラリのみで動作)
- `popup/` — 選択テキスト翻訳・画面OCR翻訳ポップアップアプリ(WPF、exe 直接実行)
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

## 翻訳ポップアップアプリ (`popup/`)

他ソフトで選択中のテキストを **Ctrl+F7** で翻訳し、カーソルの近くに訳を表示する
Windows 常駐アプリ(.NET 8 WPF、シングルファイル自己完結 exe)。
**Ctrl+F9** では画面の一部をドラッグで範囲選択し、OCR(Windows 内蔵エンジン)で
テキスト抽出→翻訳できます(画像やコピーできない画面の文字向け)。

### 使い方

1. `popup\dist\PlamoTranslatePopup.exe` をダブルクリックして起動(タスクトレイに常駐)
2. 任意のアプリでテキストを選択した状態で `Ctrl+F7`
   - または `Ctrl+F9` → ドラッグで画面の範囲を選択(Esc でキャンセル)
3. カーソル付近にポップアップ表示(既定 30秒で自動クローズ。ホバー中は閉じない)

選択テキストは Ctrl+C 送信で一時取得し、翻訳後にクリップボードへ復元します。
llama-server が未起動なら自動起動します(ポップアップ表示に数秒かかります)。
トレイアイコン(青地に「訳」)が見つからない場合は、タスクバー右端の
「^」(**隠れたアイコン**)の中を確認してください。そこからドラッグすると
タスクバーに常時表示できます。
トレイアイコンの右クリック → 「設定...」で設定画面、「終了」で終了します。
**アプリの終了時(常駐・単発モード問わず)に llama-server を停止して VRAM を解放します。**

OCR は Windows 内蔵の `Windows.Media.Ocr` を使用します(外部依存なし。
日本語 OCR 言語パックが必要: 設定 → 時計と言語 → 言語と地域)。

### 設定画面

トレイアイコンの右クリック → 「設定...」で開きます。保存すると
`popup.json` に書き出され、ホットキーは即時再登録されます。

- ショートカットキー(選択翻訳 / OCR翻訳): ボックスをクリックしてキーを押すと記録
- 自動で閉じる秒数: 0 で自動的に閉じなくなる
- 不透明度: 30〜100%

### 設定 (`popup.json`)

exe と同じフォルダに置くと既定値を上書きできます(dist 版に同梱。
設定画面からの保存もこのファイルに行われます)。

| キー | 既定値 | 説明 |
|---|---|---|
| `serverUrl` | `http://127.0.0.1:8080` | llama-server の URL |
| `modelRoot` | リポジトリルート | `llama-bin/`・`models/` の探索ルート |
| `autoCloseSeconds` | `30` | ポップアップ自動クローズ秒数(0 = 閉じない) |
| `popupOpacity` | `0.95` | ポップアップの不透明度(0.3〜1.0) |
| `hotkeyTranslate` | `Ctrl+F7` | 選択テキスト翻訳のホットキー |
| `hotkeyOcr` | `Ctrl+F9` | 画面OCR翻訳のホットキー |

キーの書式: `Ctrl+Alt+Shift+Win+キー名`(例: `Ctrl+Shift+T`, `Alt+F9`, `Ctrl+1`)。
F12 と単独の修飾キーは登録できません。他アプリと競合するキーは保存時に拒否されます。

動作ログは `%TEMP%\plamo-popup.log` に出力されます。

CLI/テスト用の起動モード:

```bash
PlamoTranslatePopup.exe --settings       # 設定画面だけを開いて終了
PlamoTranslatePopup.exe --trigger        # クリップボードの内容を翻訳して表示し終了
PlamoTranslatePopup.exe --ocr 300,300,900,200  # 指定領域(物理ピクセル)をOCR翻訳して表示し終了
```

### ビルド

```bash
dotnet publish popup/PlamoTranslatePopup/PlamoTranslatePopup.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o popup/dist
```

約68MBの単一exe(`popup/dist/PlamoTranslatePopup.exe`)が生成されます。

## ライセンス

モデルは [PLaMo Community License](https://plamo.preferredai.jp/info/plamo-community-license-ja)。
個人利用・非商用(学術利用含む)は無料。商用利用は年収など条件あり。
