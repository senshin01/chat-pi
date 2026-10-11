# 音声テスト素材

すべてこのリポジトリ用に生成した合成音と画像です。市販の音源は含みません。

- `reference16.wav`: 44.1 kHz / 16 bit / stereo、0.5 秒の正弦波。
- `reference24.wav`: 96 kHz / 24 bit / stereo、0.5 秒。下位 8 bit に変化を加え、16 bit への切り捨てを検出します。
- `reference8.wav`: 48 kHz / unsigned 8 bit / mono、0.5 秒。
- FLAC / ALAC / AIFF: 対応する WAV をエンコード。全サンプルとシーク後の音声を元の WAV と比較します。
- AAC / WMA / Vorbis: 対応する WAV から非可逆圧縮。元のレート、デコード、シーク、巻き戻しを確認します。
- `vorbis-seek.ogg`: seed を固定した 3 秒のノイズ。複数ページにわたる前後のシークと終端を、通常デコード時のサンプルと比較します。
- `cover.png`: 自作の 16 × 16 ピクセル画像。24 bit FLAC と ALAC に埋め込み、タグ・画像の読み込みを確認します。

コンテナーが対応する場合、タイトルは `Fixture Song`、アーティストは `Fixture Artist`、アルバムは `Fixture Album` です。

Python 3 と FFmpeg 7.1.5 でリポジトリのルートから再生成できます。

```sh
python3 scripts/generate-audio-fixtures.py
```

FFmpeg はこの開発用スクリプトのみで使用し、アプリには同梱しません。
