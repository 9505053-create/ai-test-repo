# utils/pdf.py - PDF 組合輸出模組

from PIL import Image
from typing import List, Callable, Optional
import os


def images_to_pdf(
    image_paths: List[str],
    output_path: str,
    quality: int = 85,
    progress_callback: Optional[Callable[[int, int], None]] = None
) -> str:
    """
    將多張 JPG/PNG 圖片合併輸出為 PDF

    Args:
        image_paths: 圖片路徑清單（已排序）
        output_path: 輸出 PDF 路徑
        quality: JPG 品質（若來源是 PNG 需轉換）
        progress_callback: fn(current, total) 進度回呼

    Returns:
        輸出檔案路徑
    """
    if not image_paths:
        raise ValueError("沒有圖片可合併")

    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    imgs = []
    total = len(image_paths)

    for i, path in enumerate(image_paths):
        img = Image.open(path).convert("RGB")
        imgs.append(img)
        if progress_callback:
            progress_callback(i + 1, total)

    first = imgs[0]
    rest  = imgs[1:]

    first.save(
        output_path,
        format="PDF",
        save_all=True,
        append_images=rest,
        resolution=150
    )

    return output_path


def jpg_bytes_list_to_pdf(
    jpg_bytes_list: List[bytes],
    output_path: str,
    progress_callback: Optional[Callable[[int, int], None]] = None
) -> str:
    """
    直接從 JPG bytes list 合併 PDF（不寫暫存圖片）
    """
    if not jpg_bytes_list:
        raise ValueError("沒有圖片資料")

    from io import BytesIO

    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    imgs = []
    total = len(jpg_bytes_list)
    for i, data in enumerate(jpg_bytes_list):
        img = Image.open(BytesIO(data)).convert("RGB")
        imgs.append(img)
        if progress_callback:
            progress_callback(i + 1, total)

    first = imgs[0]
    rest  = imgs[1:]
    first.save(output_path, format="PDF", save_all=True, append_images=rest, resolution=150)

    return output_path
