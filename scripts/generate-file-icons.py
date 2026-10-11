"""Generate original format-labelled document icons (Python 3 + Pillow).

Use --font to specify DejaVuSans-Bold.ttf on other operating systems.
Neither Python nor the font file is shipped with the application.
"""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

FORMATS = {
    "MP3": "#235E4B", "WAV": "#285B83", "FLAC": "#176570",
    "M4A": "#655384", "AAC": "#815B24", "AIFF": "#37577B",
    "OGG": "#735078", "WMA": "#415F75",
}
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
# Hand-drawn 3x5 glyphs keep labels crisp at Explorer's smallest icon sizes.
GLYPHS = {
    "A": ("010", "101", "111", "101", "101"),
    "C": ("111", "100", "100", "100", "111"),
    "F": ("111", "100", "110", "100", "100"),
    "G": ("111", "100", "101", "101", "111"),
    "I": ("111", "010", "010", "010", "111"),
    "L": ("100", "100", "100", "100", "111"),
    "M": ("101", "111", "111", "101", "101"),
    "O": ("111", "101", "101", "101", "111"),
    "P": ("110", "101", "110", "100", "100"),
    "V": ("101", "101", "101", "101", "010"),
    "W": ("101", "101", "111", "111", "101"),
    "3": ("110", "001", "010", "001", "110"),
    "4": ("101", "101", "111", "001", "001"),
}


def render(label, color, size, font_path):
    scale = 4
    image = Image.new("RGBA", (size * scale, size * scale))
    draw = ImageDraw.Draw(image)
    unit = size * scale / 32
    def points(values):
        return [(x * unit, y * unit) for x, y in values]
    draw.polygon(points([(5, 1), (21, 1), (27, 7), (27, 29), (5, 29)]), fill="#F1F4F8")
    draw.line(points([(5, 29), (5, 1), (21, 1), (27, 7), (27, 29)]), fill="#738397", width=max(2, round(unit)))
    draw.polygon(points([(21, 1), (21, 7), (27, 7)]), fill="#CAD4DF")
    # A single music note retains the player's visual identity.
    draw.line(points([(16, 16), (16, 9), (21, 8)]), fill="#344C62", width=max(3, round(1.7 * unit)))
    draw.ellipse((11 * unit, 14 * unit, 17 * unit, 18 * unit), fill="#344C62")
    badge_top = round(size * 0.59) * scale
    draw.rounded_rectangle((0, badge_top, size * scale - 1, size * scale - 1), radius=2 * unit, fill=color)
    if size > 20:
        font_size = round(size * 0.31 * scale)
        font = ImageFont.truetype(str(font_path), font_size)
        while draw.textbbox((0, 0), label, font=font)[2] > (size - 3) * scale:
            font_size -= 1
            font = ImageFont.truetype(str(font_path), font_size)
        left, top, right, bottom = draw.textbbox((0, 0), label, font=font)
        x = (size * scale - (right - left)) / 2 - left
        y = (badge_top + size * scale - (bottom - top)) / 2 - top
        draw.text((x, y), label, font=font, fill="white")
    image = image.resize((size, size), Image.Resampling.LANCZOS)
    if size <= 20:
        draw = ImageDraw.Draw(image)
        x = (size - (len(label) * 4 - 1)) // 2
        y = (round(size * 0.59) + size - 5) // 2
        for index, char in enumerate(label):
            for row, glyph in enumerate(GLYPHS[char]):
                for col, bit in enumerate(glyph):
                    if bit == "1": draw.point((x + index * 4 + col, y + row), fill="white")
    return image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--font", type=Path, default=Path("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"))
    parser.add_argument("--preview", type=Path, help="Optional contact sheet outside the application assets")
    args = parser.parse_args()
    output = Path(__file__).resolve().parents[1] / "src/ChatPi.App/Assets/FileIcons"
    output.mkdir(parents=True, exist_ok=True)
    for label, color in FORMATS.items():
        images = [render(label, color, size, args.font) for size in SIZES]
        images[-1].save(output / f"{label}.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    if args.preview:
        preview = Image.new("RGB", (640, 320), "#10151D")
        draw = ImageDraw.Draw(preview)
        draw.rectangle((0, 160, 640, 320), fill="#F5F6F8")
        for index, (label, color) in enumerate(FORMATS.items()):
            for row in range(2):
                for size, y in [(48, 15), (24, 83), (16, 127)]:
                    icon = render(label, color, size, args.font)
                    preview.paste(icon, (index * 80 + (80 - size) // 2, row * 160 + y), icon)
        preview.save(args.preview)


if __name__ == "__main__":
    main()
