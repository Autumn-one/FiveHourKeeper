"""生成 FiveHourKeeper 的应用图标与托盘三态图标。

图形语义：圆角底板 + 白色钟环 + 从 12 点顺时针 150°（12 小时盘上的 5 小时）扇形 + 中心轴点。
每个尺寸单独绘制并按 4 倍超采样下采样，保证 16px 托盘图标依然清晰；
最终手工封装为多尺寸 ICO（PNG 负载），避免 Pillow 单张缩放导致小尺寸糊掉。

用法： python tools/make_icons.py
"""

from __future__ import annotations

import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "src" / "FiveHourKeeper" / "Assets"
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

PURPLE = (83, 74, 183, 255)
GRAY = (95, 94, 90, 255)
RED = (163, 45, 45, 255)
AMBER = (250, 199, 117, 255)
WHITE = (255, 255, 255, 255)

WEDGE_START = -90.0
WEDGE_SWEEP = 150.0  # 5 / 12 * 360


def draw_icon(size: int, background: tuple[int, int, int, int]) -> Image.Image:
    ss = 4
    s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(0.22 * s), fill=background)

    cx = cy = s / 2
    ring_r = 0.33 * s
    ring_w = max(ss, int(0.055 * s))
    d.ellipse(
        [cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
        outline=WHITE,
        width=ring_w,
    )

    wedge_r = ring_r - ring_w * 1.35
    d.pieslice(
        [cx - wedge_r, cy - wedge_r, cx + wedge_r, cy + wedge_r],
        start=WEDGE_START,
        end=WEDGE_START + WEDGE_SWEEP,
        fill=AMBER,
    )

    dot_r = max(ss, 0.038 * s)
    d.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r], fill=WHITE)

    return img.resize((size, size), Image.LANCZOS)


def write_ico(path: Path, images: list[Image.Image]) -> None:
    payloads = []
    for im in images:
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        payloads.append(buf.getvalue())

    out = io.BytesIO()
    out.write(struct.pack("<HHH", 0, 1, len(images)))
    offset = 6 + 16 * len(images)
    for im, data in zip(images, payloads):
        w = 0 if im.width >= 256 else im.width
        h = 0 if im.height >= 256 else im.height
        out.write(struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    for data in payloads:
        out.write(data)

    path.write_bytes(out.getvalue())


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    targets = {
        "app.ico": PURPLE,
        "tray-normal.ico": PURPLE,
        "tray-paused.ico": GRAY,
        "tray-error.ico": RED,
    }
    for name, color in targets.items():
        sizes = SIZES if name == "app.ico" else [16, 20, 24, 32, 40, 48, 64]
        write_ico(ASSETS / name, [draw_icon(s, color) for s in sizes])
        print(f"wrote {name}")

    draw_icon(512, PURPLE).save(ASSETS / "logo-512.png")
    print("wrote logo-512.png")


if __name__ == "__main__":
    main()
