# config.py - BookCapture v4.0 設定檔

import os

# ── 截圖設定 ────────────────────────────────────────────────
CAPTURE_DELAY      = 1.2    # 翻頁後等待截圖的秒數
SCROLL_AMOUNT      = -3     # scroll mode 滾輪量（負 = 向下）
WAKEUP_SCROLL      = -50    # retry 喚醒用滾輪量
PAGE_MAX           = 500    # 最大擷取頁數上限（防止無限迴圈）

# ── 相似度判斷 ───────────────────────────────────────────────
PIXEL_DIFF_THRESHOLD = 0.005   # scroll mode：畫面變化比例門檻
DHASH_THRESHOLD      = 5       # page mode：dHash 差異門檻

# ── 輸出設定 ────────────────────────────────────────────────
OUTPUT_DIR         = os.path.join(os.path.expanduser("~"), "BookCapture_Output")
JPG_QUALITY        = 85        # JPG 壓縮品質（1-95）
PDF_DPI            = 150       # PDF 嵌入 DPI

# ── 智慧裁切 ────────────────────────────────────────────────
CROP_ENABLED       = True      # 是否啟用裁切
CROP_WHITE_MARGIN  = 10        # 白底裁切容忍像素
CROP_DARK_THRESHOLD = 30       # 黑底裁切門檻

# ── 模式常數 ────────────────────────────────────────────────
MODE_SCROLL = "scroll"
MODE_PAGE   = "page"
