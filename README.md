# plamo-translate

[PLaMo 3 Translate 2B](https://huggingface.co/pfnet/plamo-3-translate-2b-GGUF) (GGUF Q4_K_M) + llama.cpp を使った日⇔英翻訳ポップアップアプリ。

## 構成

- `popup/` — 選択テキスト翻訳・画面OCR翻訳ポップアップアプリ(WPF、exe 直接実行)
- `llama-bin/` — llama.cpp CUDA ビルド(b12121)
- `models/` — モデルファイル(GGUF)

## 翻訳ポップアップアプリ (`popup/`)

他ソフトで選択中のテキストを **Ctrl+F7** で翻訳し、カーソルの近くに訳を表示する
Windows 常駐アプリ(.NET 8 WPF、シングルファイル自己完結 exe)。
**Ctrl+F9** では画面の一部をドラッグで範囲選択し、OCR(Windows 内蔵エンジン)で
テキスト抽出→翻訳できます(画像やコピーできない画面の文字向け)。

### 使い方

1. `popup\dist\PlamoTranslatePopup.exe` をダブルクリックして起動(タスクトレイに常駐)
2. 任意のアプリでテキストを選択した状態で `Ctrl+F7`
   - または `Ctrl+F9` → ドラッグで画面の範囲を選択(Esc でキャンセル)
3. カーソル付近にポップアップ表示(既定 30秒で自動クローズ)

### ポップアップの操作

- **外をクリック**すると閉じる
- **タイトル部分(言語方向ラベル)をドラッグ**して移動できる
- **マウスを乗せると不透明**になり読みやすくなる(離すと設定の透明度に戻る)
- ホバー中は自動クローズしない
- 翻訳は**ストリーミング表示**(生成され次第逐次表示され、完了時に位置も動かない)
- OCR では翻訳前の文字列(OCR 結果)もポップアップに小さく表示される

トレイアイコン(青地に「訳」)が見つからない場合は、タスクバー右端の
「^」(**隠れたアイコン**)の中を確認してください。そこからドラッグすると
タスクバーに常時表示できます。

選択テキストは Ctrl+C 送信で一時取得し、翻訳後にクリップボードへ復元します。
llama-server が未起動なら自動起動します(ポップアップ表示に数秒かかります)。
トレイアイコンの右クリックメニューから「設定...」「サーバーを起動」「サーバーを停止」
「終了」が実行できます。翻訳に使われないまま設定秒数(既定 300秒)経過すると
llama-server を自動停止して VRAM を解放します。
**アプリの終了時(常駐・単発モード問わず)に llama-server を停止して VRAM を解放します。**

OCR は Windows 内蔵の `Windows.Media.Ocr` を使用します(外部依存なし。
日本語 OCR 言語パックが必要: 設定 → 時計と言語 → 言語と地域)。

### 設定画面

トレイアイコンの右クリック → 「設定...」で開きます。保存すると
`popup.json` に書き出され、ホットキーは即時再登録されます。

- 翻訳エンジン: PLaMo (ローカル llama-server) / Google 翻訳 (オンライン・高速)
- ショートカットキー(選択翻訳 / OCR翻訳): ボックスをクリックしてキーを押すと記録
- 自動で閉じる秒数: 0 で自動的に閉じなくなる
- 不透明度: 30〜100%(通常時の値。ホバー中は常に不透明)
- 放置したら自動終了: llama-server を自動停止するまでの秒数(0 = 自動停止しない)

**翻訳エンジンについて**

| エンジン | 特徴 |
|---|---|
| PLaMo (既定) | ローカル推論。オフラインで動作しテキストを送信しない。コールド起動 ~7秒、翻訳 0.4〜0.8秒 |
| Google 翻訳 | 非公式 API (`translate.googleapis.com`) を使用。llama-server 不要で高速(短文 ~0.1秒)。**テキストは Google に送信され、予告なく使えなくなる可能性があります** |

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
| `serverIdleTimeoutSeconds` | `300` | 未使用の llama-server を自動停止する秒数(0 = 無効) |
| `translationEngine` | `"plamo"` | 翻訳エンジン (`"plamo"` / `"google"`)。`google` は非公式 API |

キーの書式: `Ctrl+Alt+Shift+Win+キー名`(例: `Ctrl+Shift+T`, `Alt+F9`, `Ctrl+1`)。
F12 と単独の修飾キーは登録できません。他アプリと競合するキーは保存時に拒否されます。

## 性能 (RTX 3070 実測)

| 指標 | 値 |
|---|---|
| 翻訳1文の応答 | 0.4〜0.8 秒 |
| 生成速度 | 約60〜95 tok/s |
| VRAM消費(常駐時) | 約2.1 GB |

## テスト用の起動モード

```bash
PlamoTranslatePopup.exe --settings       # 設定画面だけを開いて終了
PlamoTranslatePopup.exe --trigger        # クリップボードの内容を翻訳して表示し終了
PlamoTranslatePopup.exe --ocr 300,300,900,200  # 指定領域(物理ピクセル)をOCR翻訳して表示し終了
```

動作ログは `%TEMP%\plamo-popup.log` に出力されます。

## llama-server を手動で起動する場合

通常はホットキー実行時に自動起動・終了時に自動停止します。

```bash
llama-bin\llama-server.exe -m models\plamo-3-translate-2b-Q4_K_M.gguf -c 8192 --port 8080 --host 127.0.0.1
```

## ビルド

```bash
dotnet publish popup/PlamoTranslatePopup/PlamoTranslatePopup.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o popup/dist
```

約68MBの単一exe(`popup/dist/PlamoTranslatePopup.exe`)が生成されます。

## ライセンス

モデルは [PLaMo Community License](https://plamo.preferredai.jp/info/plamo-community-license-ja)。
個人利用・非商用(学術利用含む)は無料。商用利用は年収など条件あり。
