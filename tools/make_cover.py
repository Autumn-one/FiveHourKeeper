"""生成 B 站视频封面（1920x1080，16:9）。

设计：深色科技风渐变背景 + 时钟环光晕 + 左侧大字标题 + 右侧真实软件界面卡片。
文案与视频标题对应：解决 AI 订阅被「5 小时限额」卡住的问题。

用法：python tools/make_cover.py
输出：docs/bilibili-cover.png / docs/bilibili-cover.jpg
"""
from __future__ import annotations

import math
import os
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1920, 1080

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "docs"
SHOT = Path(os.environ.get("TEMP", r"C:\Users\mgtx6\AppData\Local\Temp")) / "fh-app-clean.png"

# 品牌色
BG_TOP = (18, 16, 42)
BG_MID = (32, 20, 68)
BG_BOT = (9, 8, 20)
VIOLET = (124, 92, 255)
CYAN = (86, 214, 255)
ACCENT = (255, 216, 77)
LAVENDER = (185, 178, 238)
WHITE = (255, 255, 255)

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyhbd.ttc",
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\Deng.ttf",
    r"C:\Windows\Fonts\simhei.ttf",
]
FONT_REG_CANDIDATES = [
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\Deng.ttf",
    r"C:\Windows\Fonts\simhei.ttf",
]


def pick_font(candidates: list[str]) -> str:
    for p in candidates:
        if os.path.exists(p):
            return p
    raise SystemExit("找不到可用中文字体")


FONT_BOLD = pick_font(FONT_CANDIDATES)
FONT_REG = pick_font(FONT_REG_CANDIDATES)


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    try:
        return ImageFont.truetype(path, size, index=0)
    except Exception:
        return ImageFont.truetype(path, size)


def lerp(c1, c2, t):
    return tuple(int(a + (b - a) * t) for a, b in zip(c1, c2))


def make_background() -> Image.Image:
    """三段纵向渐变底。"""
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    for y in range(H):
        t = y / (H - 1)
        if t < 0.55:
            c = lerp(BG_TOP, BG_MID, t / 0.55)
        else:
            c = lerp(BG_MID, BG_BOT, (t - 0.55) / 0.45)
        d.line([(0, y), (W, y)], fill=c)
    return img


def add_radial_glow(img: Image.Image, center, radius, color, alpha=90) -> Image.Image:
    """在指定位置叠加径向光晕。"""
    layer = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(layer)
    steps = 48
    for i in range(steps, 0, -1):
        r = int(radius * i / steps)
        a = int(alpha * (1 - i / steps) ** 2.2)
        d.ellipse([center[0] - r, center[1] - r, center[0] + r, center[1] + r], fill=a)
    layer = layer.filter(ImageFilter.GaussianBlur(radius * 0.14))
    glow = Image.new("RGB", (W, H), color)
    return Image.composite(Image.blend(img, glow, 0.85), img, layer)


def add_grid(img: Image.Image, step=78, alpha=13) -> Image.Image:
    layer = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(layer)
    for x in range(0, W, step):
        d.line([(x, 0), (x, H)], fill=alpha)
    for y in range(0, H, step):
        d.line([(0, y), (W, y)], fill=alpha)
    white = Image.new("RGB", (W, H), (255, 255, 255))
    return Image.composite(white, img, layer)


def add_clock_halo(img: Image.Image, center, radius) -> Image.Image:
    """时钟环：暗环 + 高亮 5/12 弧（代表 5 小时窗口）。"""
    glow_layer = Image.new("L", (W, H), 0)
    dg = ImageDraw.Draw(glow_layer)
    box = [center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius]
    dg.ellipse(box, outline=255, width=34)
    glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(26))

    solid = Image.new("L", (W, H), 0)
    ds = ImageDraw.Draw(solid)
    ds.ellipse(box, outline=110, width=18)

    arc = Image.new("L", (W, H), 0)
    da = ImageDraw.Draw(arc)
    start, sweep = -86, 150  # 5/12 圈 ≈ 150°
    da.arc(box, start=start, end=start + sweep, fill=255, width=22)
    arc_glow = arc.filter(ImageFilter.GaussianBlur(22))

    dim_ring = Image.new("RGB", (W, H), (120, 110, 200))
    bright = Image.new("RGB", (W, H), CYAN)
    out = Image.composite(bright, img, solid)
    out = Image.composite(bright, out, arc)
    violet = Image.new("RGB", (W, H), VIOLET)
    out = Image.composite(violet, out, glow_layer)
    cyan_img = Image.new("RGB", (W, H), CYAN)
    out = Image.composite(cyan_img, out, arc_glow)
    return out


def add_ticks(img: Image.Image, center, radius) -> Image.Image:
    layer = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(layer)
    for i in range(60):
        ang = math.radians(i * 6)
        major = i % 5 == 0
        r1 = radius + 30
        r2 = radius + (52 if major else 40)
        x1, y1 = center[0] + r1 * math.cos(ang), center[1] + r1 * math.sin(ang)
        x2, y2 = center[0] + r2 * math.cos(ang), center[1] + r2 * math.sin(ang)
        d.line([(x1, y1), (x2, y2)], fill=90 if major else 45, width=5 if major else 3)
    layer = layer.filter(ImageFilter.GaussianBlur(1.2))
    white = Image.new("RGB", (W, H), (200, 195, 255))
    return Image.composite(white, img, layer)


def add_vignette(img: Image.Image) -> Image.Image:
    layer = Image.new("L", (W, H), 255)
    d = ImageDraw.Draw(layer)
    d.ellipse([-W * 0.30, -H * 0.42, W * 1.30, H * 1.42], fill=0)
    layer = layer.filter(ImageFilter.GaussianBlur(190))
    dark = Image.new("RGB", (W, H), (4, 3, 10))
    return Image.composite(dark, img, layer)


def rounded_card(shot: Image.Image, target_w: int, radius=20) -> Image.Image:
    """把界面截图做成圆角卡片（带白边）。"""
    ratio = target_w / shot.width
    shot = shot.resize((target_w, int(shot.height * ratio)), Image.LANCZOS)
    w, h = shot.size

    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h - 1], radius=radius, fill=255)

    card = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    card.paste(shot.convert("RGBA"), (0, 0), mask)

    border = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(border).rounded_rectangle(
        [1, 1, w - 2, h - 2], radius=radius, outline=(190, 175, 255, 200), width=2)
    return Image.alpha_composite(card, border)


def paste_with_shadow(canvas: Image.Image, card: Image.Image, pos, glow_color=VIOLET) -> Image.Image:
    """带投影与外发光的粘贴。"""
    x, y = pos
    w, h = card.size

    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle(
        [x - 16, y - 16, x + w + 16, y + h + 16], radius=40, fill=(*glow_color, 150))
    glow = glow.filter(ImageFilter.GaussianBlur(44))
    canvas = Image.alpha_composite(canvas.convert("RGBA"), glow)

    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [x - 4, y + 14, x + w + 4, y + h + 30], radius=30, fill=(0, 0, 0, 190))
    shadow = shadow.filter(ImageFilter.GaussianBlur(30))
    canvas = Image.alpha_composite(canvas, shadow)

    canvas.paste(card, (x, y), card)
    return canvas


def gradient_text(canvas: Image.Image, xy, text, fnt, c1, c2) -> None:
    """渐变填充文字。"""
    tmp = Image.new("L", (W, H), 0)
    ImageDraw.Draw(tmp).text(xy, text, font=fnt, fill=255)
    grad = Image.new("RGB", (W, H))
    dg = ImageDraw.Draw(grad)
    for i in range(W):
        dg.line([(i, 0), (i, H)], fill=lerp(c1, c2, i / (W - 1)))
    canvas.paste(grad, (0, 0), tmp)


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    img = make_background()
    img = add_grid(img)
    img = add_radial_glow(img, (300, 120), 900, VIOLET, alpha=70)
    img = add_radial_glow(img, (1750, 980), 820, (40, 90, 160), alpha=80)

    # 时钟环放在界面卡片正后方，形成光晕
    card_x, card_y = 985, 292
    shot = Image.open(SHOT).convert("RGB")
    card = rounded_card(shot, 858)
    cw, ch = card.size
    ring_center = (card_x + cw // 2, card_y + ch // 2)
    img = add_clock_halo(img, ring_center, 585)
    img = add_ticks(img, ring_center, 585)
    img = add_vignette(img)

    img = img.convert("RGBA")
    img = paste_with_shadow(img, card, (card_x, card_y))

    draw = ImageDraw.Draw(img)

    # ---- 左上角标签 ----
    badge_font = font(FONT_REG, 30)
    badge_text = "AI 订阅 · Windows 免费工具"
    bb = draw.textbbox((0, 0), badge_text, font=badge_font)
    bw, bh = bb[2] - bb[0], bb[3] - bb[1]
    pad_x, pad_y = 26, 16
    pill = [96, 96, 96 + bw + pad_x * 2, 96 + bh + pad_y * 2]
    pill_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(pill_layer).rounded_rectangle(pill, radius=(pill[3] - pill[1]) // 2,
                                                 fill=(*VIOLET, 70), outline=(*VIOLET, 220), width=2)
    img = Image.alpha_composite(img, pill_layer)
    draw = ImageDraw.Draw(img)
    draw.text((pill[0] + pad_x, pill[1] + pad_y - bb[1]), badge_text, font=badge_font, fill=(230, 226, 255))

    # ---- 主标题 ----
    f_small = font(FONT_BOLD, 62)
    f_big = font(FONT_BOLD, 112)
    f_sub = font(FONT_REG, 38)

    draw.text((96, 232), "解决 AI 订阅被", font=f_small, fill=LAVENDER)
    gradient_text(img, (96, 314), "「5 小时限额」", f_big, (255, 255, 255), (168, 226, 255))
    draw = ImageDraw.Draw(img)
    draw.text((96, 452), "卡住的问题", font=f_big, fill=ACCENT)

    # 副标题
    draw.text((100, 620), "自动卡点刷新 · 窗口永远用满", font=f_sub, fill=(168, 160, 220))

    # 高亮下划线
    draw.rounded_rectangle([100, 596, 100 + 560, 602], radius=3, fill=(*VIOLET, 220))

    # ---- 功能亮点 ----
    f_feat = font(FONT_REG, 33)
    features = [
        "按目标时刻自动倒推发送时间",
        "多个 AI 模型同时托管",
        "常驻托盘，静默运行不打扰",
    ]
    for i, text in enumerate(features):
        y = 690 + i * 54
        draw.ellipse([102, y + 13, 116, y + 27], fill=CYAN)
        draw.text((132, y), text, font=f_feat, fill=(197, 191, 240))

    # ---- 左下角品牌区 ----
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle([100, 900, 186, 986], radius=22, fill=(124, 92, 255))
    draw.text((100 + 15, 916), "5h", font=font(FONT_BOLD, 40), fill=WHITE)

    draw.text((210, 906), "五小时窗口管家", font=font(FONT_BOLD, 40), fill=WHITE)
    draw.text((212, 958), "FiveHourKeeper · Windows 免费工具", font=font(FONT_REG, 26), fill=(158, 150, 214))

    img = img.convert("RGB")
    png = OUT_DIR / "bilibili-cover.png"
    jpg = OUT_DIR / "bilibili-cover.jpg"
    img.save(png)
    img.save(jpg, quality=92, optimize=True)
    print("saved:", png, png.stat().st_size // 1024, "KB")
    print("saved:", jpg, jpg.stat().st_size // 1024, "KB")


if __name__ == "__main__":
    main()
