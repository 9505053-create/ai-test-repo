# utils/capture.py - 截圖與裁切模組

import mss
import mss.tools
import numpy as np
from PIL import Image
import ctypes
import io

# ── DPI Awareness（4K 必要）───────────────────────────────────
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass


def capture_screen() -> Image.Image:
    """擷取全螢幕，回傳 PIL Image（RGB）"""
    with mss.mss() as sct:
        monitor = sct.monitors[1]
        raw = sct.grab(monitor)
        img = Image.frombytes("RGB", raw.size, raw.bgra, "raw", "BGRX")
    return img


def pixel_diff_ratio(img_a: Image.Image, img_b: Image.Image) -> float:
    """計算兩張圖的像素差異比例（0.0 ~ 1.0）"""
    arr_a = np.array(img_a.convert("L"), dtype=np.int32)
    arr_b = np.array(img_b.convert("L"), dtype=np.int32)
    diff = np.abs(arr_a - arr_b)
    changed = np.sum(diff > 10)
    return changed / diff.size


def smart_crop(img: Image.Image, dark_threshold: int = 30, white_margin: int = 10) -> Image.Image:
    """
    智慧裁切：
    - 白底：裁掉四周白邊
    - 黑底：裁掉四周暗邊
    """
    arr = np.array(img.convert("L"))
    mean_brightness = arr.mean()

    if mean_brightness > 200:
        # 白底模式
        mask = arr < (255 - white_margin)
    else:
        # 黑底模式
        mask = arr > dark_threshold

    rows = np.any(mask, axis=1)
    cols = np.any(mask, axis=0)

    if not rows.any() or not cols.any():
        return img  # 無法裁切，原圖回傳

    rmin, rmax = np.where(rows)[0][[0, -1]]
    cmin, cmax = np.where(cols)[0][[0, -1]]

    padding = 5
    rmin = max(0, rmin - padding)
    rmax = min(arr.shape[0] - 1, rmax + padding)
    cmin = max(0, cmin - padding)
    cmax = min(arr.shape[1] - 1, cmax + padding)

    return img.crop((cmin, rmin, cmax + 1, rmax + 1))


def image_to_jpg_bytes(img: Image.Image, quality: int = 85) -> bytes:
    """PIL Image 轉 JPG bytes"""
    buf = io.BytesIO()
    img.convert("RGB").save(buf, format="JPEG", quality=quality, optimize=True)
    return buf.getvalue()
