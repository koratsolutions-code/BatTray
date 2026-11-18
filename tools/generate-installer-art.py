"""Generates the branded WiX installer art and license from the repository assets.

Outputs (in installer/assets):
  Dialog.bmp   493x312  welcome/finish dialog background (brand panel on the left)
  Banner.bmp   493x58   top banner of the inner dialogs
  License.rtf           the MIT license shown by the installer

Requires Pillow. Run from the repository root:  python tools/generate-installer-art.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "BatTray.App" / "Assets"
OUT = ROOT / "installer" / "assets"

BLUE = (0x2E, 0x6F, 0xD0)
NAVY = (0x16, 0x28, 0x4C)
WHITE = (0xFF, 0xFF, 0xFF)
SOFT = (0xC9, 0xD8, 0xF2)
SCALE = 2  # draw at 2x, then downsample for smooth edges


def font(name, size):
    try:
        return ImageFont.truetype(name, size * SCALE)
    except OSError:
        return ImageFont.load_default()


def gradient(width, height):
    img = Image.new("RGB", (width, height))
    d = ImageDraw.Draw(img)
    for y in range(height):
        t = y / max(1, height - 1)
        d.line([(0, y), (width, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(BLUE, NAVY)))
    return img


def centered_text(d, cx, y, text, f, fill):
    w = d.textlength(text, font=f)
    d.text((cx - w / 2, y), text, font=f, fill=fill)


def dialog():
    w, h, panel = 493 * SCALE, 312 * SCALE, 164 * SCALE
    img = Image.new("RGB", (w, h), WHITE)
    img.paste(gradient(panel, h), (0, 0))
    icon = Image.open(ASSETS / "BatTray-512.png").convert("RGBA").resize((96 * SCALE, 96 * SCALE), Image.LANCZOS)
    img.paste(icon, ((panel - icon.width) // 2, 52 * SCALE), icon)
    d = ImageDraw.Draw(img)
    cx = panel // 2
    centered_text(d, cx, 160 * SCALE, "BatTray", font("segoeuib.ttf", 20), WHITE)
    centered_text(d, cx, 188 * SCALE, "Your devices. One glance.", font("segoeui.ttf", 9), SOFT)
    centered_text(d, cx, 278 * SCALE, "by Korat Solutions", font("segoeui.ttf", 9), SOFT)
    img.resize((493, 312), Image.LANCZOS).save(OUT / "Dialog.bmp")


def banner():
    w, h = 493 * SCALE, 58 * SCALE
    img = Image.new("RGB", (w, h), WHITE)
    d = ImageDraw.Draw(img)
    d.rectangle([0, h - 3 * SCALE, w, h], fill=BLUE)  # brand accent line
    icon = Image.open(ASSETS / "BatTray-512.png").convert("RGBA").resize((40 * SCALE, 40 * SCALE), Image.LANCZOS)
    img.paste(icon, (w - icon.width - 12 * SCALE, 7 * SCALE), icon)
    img.resize((493, 58), Image.LANCZOS).save(OUT / "Banner.bmp")


def license_rtf():
    text = (ROOT / "LICENSE").read_text(encoding="utf-8").strip()
    paragraphs = [" ".join(p.split("\n")) for p in text.split("\n\n")]
    body = "\\par\\par\n".join(p.replace("\\", "\\\\").replace("{", "\\{").replace("}", "\\}") for p in paragraphs)
    rtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}\\f0\\fs18\n" + body + "\\par\n}"
    (OUT / "License.rtf").write_text(rtf, encoding="ascii")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    dialog()
    banner()
    license_rtf()
    print(f"Wrote installer art to {OUT}")
