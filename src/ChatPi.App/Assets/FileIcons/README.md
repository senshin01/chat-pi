# Format file icons

Original document and music-note artwork with format labels. M4A identifies the
container, which can contain either AAC or ALAC; AIF uses the AIFF icon, and OGA
uses the OGG icon. Application/window icons retain the existing Chat Pi icon.

Each ICO includes individually rendered 16, 20, 24, 32, 40, 48, 64, 128 and
256 pixel images. The two smallest sizes use hand-drawn pixel glyphs for clarity.
Larger labels are rendered using DejaVu Sans Bold; the font is not distributed.

Regenerate with Python 3, Pillow and DejaVuSans-Bold.ttf:

```sh
python3 scripts/generate-file-icons.py --font /path/to/DejaVuSans-Bold.ttf
```

The checked-in ICO files are copied to build and publish outputs automatically;
regeneration dependencies are not required to build or run Chat Pi.
