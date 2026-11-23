"""Renders the plugin thumbnail (16:9) shown in Jellyfin's plugin catalogue.

Original artwork: "DVB-I" word mark with broadcast waves, in Jellyfin's purple-to-blue accent gradient.
It deliberately does not reproduce the official DVB-I or Jellyfin logos (both are trademarks).

Usage: python3 tools/make_logo.py <NotoSans-Bold.ttf> <NotoSans-Regular.ttf> <output.png>
Fonts: Noto Sans (SIL Open Font License), https://github.com/notofonts
Requires Pillow.
"""
import math
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

W, H = 1280, 720
S = 4  # supersampling factor
PURPLE = (170, 92, 195)   # Jellyfin accent #AA5CC3
BLUE = (0, 164, 220)      # Jellyfin accent #00A4DC


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def gradient(size, c1, c2, angle_deg=25):
    """Linear gradient image from c1 to c2 along angle_deg."""
    w, h = size
    small = Image.new("RGB", (256, 1))
    for x in range(256):
        small.putpixel((x, 0), lerp(c1, c2, x / 255))
    diag = int(math.hypot(w, h)) + 2
    band = small.resize((diag, diag), Image.BILINEAR).rotate(angle_deg, resample=Image.BILINEAR)
    left, top = (diag - w) // 2, (diag - h) // 2
    return band.crop((left, top, left + w, top + h))


def main(bold_path, regular_path, out_path):
    w, h = W * S, H * S

    # Background: deep blue-black with a soft accent glow behind the word mark.
    bg = gradient((w, h), (9, 9, 16), (20, 20, 38), angle_deg=-60)
    glow = Image.new("L", (w, h), 0)
    ImageDraw.Draw(glow).ellipse((w * 0.18, h * 0.12, w * 0.82, h * 0.98), fill=110)
    glow = glow.filter(ImageFilter.GaussianBlur(160 * S))
    bg = Image.composite(gradient((w, h), PURPLE, BLUE), bg, glow.point(lambda v: v * 0.55))

    font = ImageFont.truetype(bold_path, 260 * S)
    small = ImageFont.truetype(regular_path, 44 * S)

    left_text, right_text = "DVB-", "I"
    lw = font.getlength(left_text)
    rw = font.getlength(right_text)
    total = lw + rw
    x0 = (w - total) / 2
    _, top, _, bottom = font.getbbox("DVB-I")
    text_h = bottom - top
    y0 = h * 0.58 - text_h / 2 - top

    # Word mark: "DVB-" white, "I" in the accent gradient.
    white_mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(white_mask).text((x0, y0), left_text, font=font, fill=255)
    accent_mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(accent_mask).text((x0 + lw, y0), right_text, font=font, fill=255)

    # Broadcast waves above the "I": a dot with three arcs on each side.
    i_left, i_top, i_right, _ = font.getbbox(right_text)
    cx = x0 + lw + (i_left + i_right) / 2
    cy = y0 + i_top - 70 * S
    draw = ImageDraw.Draw(accent_mask)
    r_dot = 17 * S
    draw.ellipse((cx - r_dot, cy - r_dot, cx + r_dot, cy + r_dot), fill=255)
    for i, radius in enumerate((52, 92, 132)):
        r = radius * S
        width = 14 * S
        alpha = 255 - i * 55
        box = (cx - r, cy - r, cx + r, cy + r)
        draw.arc(box, start=-40, end=40, fill=alpha, width=width)    # right
        draw.arc(box, start=140, end=220, fill=alpha, width=width)   # left

    # Subtle drop shadow for depth.
    shadow = ImageChops.lighter(white_mask, accent_mask).filter(ImageFilter.GaussianBlur(10 * S))
    img = Image.composite(Image.new("RGB", (w, h), (0, 0, 0)), bg, shadow.point(lambda v: v * 0.6))
    img = Image.composite(Image.new("RGB", (w, h), (245, 245, 250)), img, white_mask)
    # Accent gradient spans only the "I" and its waves, so both colours are visible.
    ax0, ay0, ax1, ay1 = accent_mask.getbbox()
    accent = Image.new("RGB", (w, h), BLUE)
    accent.paste(gradient((ax1 - ax0, ay1 - ay0), PURPLE, BLUE, angle_deg=-60), (ax0, ay0))
    img = Image.composite(accent, img, accent_mask)

    # Tagline.
    tagline = "L I V E   T V   ·   I N T E R N E T"
    tw = small.getlength(tagline)
    ImageDraw.Draw(img).text(((w - tw) / 2, y0 + bottom + 40 * S), tagline, font=small, fill=(170, 172, 190))

    img.resize((W, H), Image.LANCZOS).save(out_path, optimize=True)
    print(f"wrote {out_path} ({W}x{H})")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
