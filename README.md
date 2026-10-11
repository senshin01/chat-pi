# Chat Pi

Windows 向けの、とてもシンプルな WASAPI / ASIO 音楽プレイヤーです。
**v0.3.0** は、1 ファイルを開いて再生する基本機能に絞っています。

## できること

- MP3 / WAV / FLAC / ALAC / AAC / AIFF / Ogg Vorbis / WMA を開いて自動再生。ドラッグ＆ドロップにも対応。
- 再生、一時停止、停止、シーク。
- WASAPI 排他（既定）、WASAPI 共有、ASIO の選択とデバイス設定の保存。
- タイトル、アーティスト、アルバム、埋め込みジャケットの表示。
- 対応する音楽ファイルの既定アプリ候補へのユーザー単位の登録・解除。
- 別のファイルを開くと、既に起動中のウィンドウで再生を切り替え。
- 同じ曲を繰り返すリピートボタン。オン／オフを保存。
- 設定からライト／ダークテーマを選択。選択を保存。

プレイリスト、ストリーミング連携、音量調整、音量正規化、アップサンプリングはありません。

## Windows で使う

対象は **Windows 10 / 11 の x64** です。ASIO を使う場合は、機器メーカーの
**64 bit ASIO ドライバー**が必要です。

1. `ChatPi-win-x64.zip` を固定のフォルダーに**すべて展開**します。
2. `ChatPi.exe` を起動します。配布フォルダーには .NET ランタイムが含まれているため、別途 .NET の導入は不要です。
3. 右上の設定から出力モードとデバイスを選び、保存します。
4. 「ファイルを開く」またはドラッグ＆ドロップで音楽ファイルを開きます。

再生操作の左側にある矢印のリピートボタンは、色付きの状態でオンです。
曲が自然に終了すると先頭から再生し、停止ボタンやエラー時には再開しません。
曲間を完全につなぐギャップレス再生は対象外です。
設定の「テーマ」でライト／ダークを選び、保存すると画面に適用されます。
リピートとテーマは再生を中断せずに切り替えられ、アプリを終了しても保持します。
既存の設定ファイルも引き継ぎ、初回はリピートオフ・ダークテーマです。

Windows の音楽ファイルのダブルクリックで起動するには、設定の
「登録して Windows の設定を開く」を押し、「既定のアプリ」で Chat Pi を選び、
使いたい拡張子を割り当てます。登録だけでは現在の既定アプリは変わりません。
以前のバージョンで登録済みの場合も、このボタンから再登録すると追加形式が候補に入ります。
実行ファイルの場所を移した場合は、移動先で再登録してください。
アンインストールは設定画面で登録を解除し、展開したフォルダーを削除します。
保存済み設定も消す場合は `%LOCALAPPDATA%\ChatPi` を削除します。

コマンドライン起動も可能です。

```powershell
.\ChatPi.exe "C:\Music\音楽ファイル.wav"
```

複数ファイルを渡した場合は最初の 1 ファイルだけを使用します。
ショートカットは `Ctrl+O`（開く）、`Space`（再生／一時停止）、`Esc`（停止）です。
シークバーはクリック、ドラッグ、キーボードで操作できます。
ノブをドラッグすると選択中の時刻を表示し、離した位置から再生します。
バーのクリックでもその位置へ移動できます。一時停止中は、移動後も一時停止を維持します。

## 対応する音楽ファイル

| 形式 | 拡張子 | デコード |
| --- | --- | --- |
| WAV | `.wav` | 非圧縮 PCM 8/16/24/32 bit、IEEE float 32 bit |
| MP3 | `.mp3` | Windows ACM |
| FLAC | `.flac` | Windows Media Foundation、8〜24 bit のロスレス音源 |
| ALAC | `.m4a` | Windows Media Foundation、16/24 bit のロスレス音源 |
| AAC | `.m4a`, `.aac` | Windows Media Foundation（AAC-LC、ADTS を検証） |
| AIFF | `.aiff`, `.aif` | 非圧縮 PCM 8/16/24/32 bit、整数値を保ってバイト順を変換 |
| Ogg Vorbis | `.ogg`, `.oga` | NVorbis、float 32 bit |
| WMA | `.wma` | Windows Media Foundation |

モノラル／ステレオに対応します。読み込みながらデコードするため、曲全体の事前変換は不要です。
FLAC / ALAC は整数 PCM で展開し、24 bit 音源の下位ビットも保持します。
出力用の PCM のビット幅が音源より広くなる場合がありますが、元のサンプルレートは維持します。
AAC / MP3 / Vorbis / 通常の WMA は非可逆圧縮なので、展開後のビット幅は原音の精度を表すものではありません。

DSD（`.dsf` / `.dff`）、DoP、Opus、APE、圧縮 AIFF-C、DRM 付きファイルは今回の対象外です。
同じ拡張子でも未対応のコーデックを収録したファイルはエラーになります。
Windows N で Media Foundation を利用するには Media Feature Pack が必要です。

## 音声出力の方針

| モード | 挙動 |
| --- | --- |
| WASAPI 排他 | 元のサンプルレートと PCM / float 表現でデバイスを開きます。未対応の形式はエラーにし、共有モードや別レートに自動切り替えしません。 |
| WASAPI 共有 | Windows ミキサー経由です。Windows のデバイス設定に合わせて形式変換される場合があります。 |
| ASIO | 元のサンプルレートでドライバーへ直接出力します。対応しないレート、チャンネル、精度を落とす整数変換はエラーにします。占有・共有はドライバーの実装に依存します。 |

- WAV はモノラル／ステレオの PCM 8/16/24/32 bit と IEEE float 32 bit に対応。
- ASIO は little-endian の Int16 / Int24 / Int32 / Float32、および Int32 コンテナー内の 16/18/20/24 bit 表現に対応。big-endian、float64、DSD は初期版の対象外です。
- モノラルは左右に同じサンプルを出力。8 bit PCM は正確な整数変換で 16 bit に拡張します。
- ASIO の整数幅の拡張は整数演算で行います。24 bit PCM を float32 に渡す場合も数値精度を保ちます。PCM32 → float32 は精度が落ちるため拒否します。
- float → 整数の出力時はフルスケールに基づいて丸めます。±1 を超える値は整数の範囲に制限されます。初期版にはディザリングがありません。
- MP3 は Windows の ACM デコーダーで PCM に展開します。MP3 自体は非可逆圧縮です。
- デジタル音量は固定です。アプリは EQ、DSP、音量正規化を行いません。音量は機器側で調整してください。
- 停止・再生終了・曲切り替えで出力を解放します。一時停止中は出力を保持する場合があります。

物理デバイス、ドライバー、DAC 内部の処理まで含む bit-perfect や音質の保証はしていません。
Windows 実機での初回確認手順は [docs/windows-testing.md](docs/windows-testing.md) を参照してください。

## 開発

C# / .NET 10 LTS / WPF / NAudio 2.3 / TagLibSharp 2.3 / NVorbis を使用しています。
SDK バージョンは `global.json`、依存関係は各 `packages.lock.json` に固定しています。
NVorbis は終端付近のシークと巻き戻しの修正を含む `1.0.0-rc.2` に固定しています。
シーク時には最大 1 ブロック分を先行デコードし、重ね合わせに必要な状態を復元します。

```powershell
dotnet restore ChatPi.sln --locked-mode
dotnet build ChatPi.sln -c Release --no-restore
dotnet test tests/ChatPi.Core.Tests/ChatPi.Core.Tests.csproj -c Release --no-build --no-restore
```

Windows ではさらに実ファイルの MP3 / FLAC / ALAC / AAC / WMA デコード、単一起動、WPF のテーマと設定切り替えのテスト、アプリの起動を行います。

```powershell
dotnet test tests/ChatPi.Windows.Tests/ChatPi.Windows.Tests.csproj -c Release --no-build --no-restore
dotnet run --project src/ChatPi.App
./scripts/publish-windows.ps1
```

Linux でも Windows 向けのビルドと Core のテストは可能です。
WPF の起動、Windows の ACM / Media Foundation、レジストリ登録、WASAPI / ASIO の実デバイス確認は Windows が必要です。
GitHub Actions には Linux の Core テストと Windows のビルド・統合テスト・ZIP 作成を用意しています。
音声テスト素材は自作の合成音です。再生成方法は [tests/Fixtures/README.md](tests/Fixtures/README.md) を参照してください。

## 構成と次の段階

- `src/ChatPi.Core`: 設定、メタデータ、再生状態、ネイティブレートの PCM パイプライン、デバイス用サンプル変換。
- `src/ChatPi.App`: WPF 画面、Windows デコーダー、WASAPI / ASIO、単一起動、ファイル関連付け。
- `tests/ChatPi.Core.Tests`: WAV / AIFF / Vorbis、サンプル精度とシーク、再生状態、設定・メタデータ、ファイル起動メッセージ転送のテスト。
- `tests/ChatPi.Windows.Tests`: Windows デコード、ロスレス形式の全サンプル比較、形式別のシークとリピート、名前付きパイプ転送、WPF の統合テスト。

アップサンプリングは、実機で基本再生を確認した後に追加します。
`NativePcmPipeline` とデバイス出力の間に、明示設定の DSP ステージを追加できる構成です。
次段階では出力レート、フィルター、CPU 負荷、遅延、クリッピング対策、ディザリングを設計し、
周波数応答・エイリアス除去・数値精度の測定を行います。
