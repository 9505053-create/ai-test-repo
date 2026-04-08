# utils/hash.py - dHash 感知雜湊模組

from PIL import Image
import numpy as np


def dhash(img: Image.Image, hash_size: int = 8) -> int:
    """
    計算 dHash（差異雜湊）
    hash_size=8 → 64-bit hash
    """
    resized = img.convert("L").resize((hash_size + 1, hash_size), Image.LANCZOS)
    arr = np.array(resized)
    diff = arr[:, 1:] > arr[:, :-1]
    # 轉為整數
    result = 0
    for bit in diff.flatten():
        result = (result << 1) | int(bit)
    return result


def hamming_distance(hash_a: int, hash_b: int) -> int:
    """計算兩個 hash 的漢明距離（差異 bit 數）"""
    xor = hash_a ^ hash_b
    return bin(xor).count("1")


def is_page_changed(img_a: Image.Image, img_b: Image.Image, threshold: int = 5) -> bool:
    """
    判斷頁面是否已翻頁
    threshold：漢明距離門檻，越小越嚴格
    """
    h_a = dhash(img_a)
    h_b = dhash(img_b)
    dist = hamming_distance(h_a, h_b)
    return dist >= threshold
