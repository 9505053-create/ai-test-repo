# capturer.py - BookCapture v4.0 核心引擎
# GUI 透過 threading 呼叫此模組，不直接操作 UI

import time
import os
import threading
from typing import Callable, Optional
from datetime import datetime

import pyautogui

from config import (
    CAPTURE_DELAY, SCROLL_AMOUNT, WAKEUP_SCROLL,
    PAGE_MAX, PIXEL_DIFF_THRESHOLD, DHASH_THRESHOLD,
    OUTPUT_DIR, JPG_QUALITY, CROP_ENABLED,
    CROP_WHITE_MARGIN, CROP_DARK_THRESHOLD,
    MODE_SCROLL, MODE_PAGE
)
from utils.capture import capture_screen, pixel_diff_ratio, smart_crop, image_to_jpg_bytes
from utils.hash import is_page_changed
from utils.pdf import jpg_bytes_list_to_pdf


class CaptureSession:
    """
    一次擷取工作的狀態容器與執行引擎。
    由 GUI 建立，透過 callback 回報進度與 log。
    """

    def __init__(
        self,
        mode: str,
        output_dir: str,
        book_name: str,
        max_pages: int,
        delay: float,
        log_callback:      Optional[Callable[[str], None]] = None,
        progress_callback: Optional[Callable[[int, int], None]] = None,
        done_callback:     Optional[Callable[[str], None]] = None,
    ):
        self.mode       = mode
        self.output_dir = output_dir
        self.book_name  = book_name
        self.max_pages  = max_pages
        self.delay      = delay

        self._log      = log_callback      or (lambda msg: print(msg))
        self._progress = progress_callback or (lambda cur, tot: None)
        self._done     = done_callback     or (lambda path: None)

        self._stop_event = threading.Event()
        self._thread: Optional[threading.Thread] = None

    # ── 公開控制介面 ──────────────────────────────────────────

    def start(self):
        """在背景執行緒啟動擷取"""
        self._stop_event.clear()
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self):
        """通知擷取迴圈停止"""
        self._stop_event.set()
        self._log("⏹ 停止訊號已發送，等待當前頁完成…")

    def is_running(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    # ── 內部執行邏輯 ──────────────────────────────────────────

    def _run(self):
        self._log(f"▶ 開始擷取（模式：{self.mode}，上限：{self.max_pages} 頁）")
        self._log(f"  3 秒後開始，請切換到書本視窗…")
        time.sleep(3)

        jpg_data_list = []
        prev_img = capture_screen()
        retry_count = 0
        page_num = 0

        while not self._stop_event.is_set() and page_num < self.max_pages:
            # 翻頁
            if self.mode == MODE_SCROLL:
                pyautogui.scroll(SCROLL_AMOUNT)
            else:
                pyautogui.press("right")

            time.sleep(self.delay)
            curr_img = capture_screen()

            # 判斷是否有翻頁
            page_changed = self._detect_change(prev_img, curr_img)

            if not page_changed:
                # 重試邏輯：喚醒
                retry_count += 1
                self._log(f"  ⚠ 第 {page_num + 1} 頁：偵測到未翻頁，重試 ({retry_count})…")
                pyautogui.scroll(WAKEUP_SCROLL)
                time.sleep(0.5)
                if retry_count >= 3:
                    self._log("  ✋ 連續 3 次未翻頁，判斷已到最後一頁，自動停止。")
                    break
                continue

            retry_count = 0
            page_num += 1

            # 裁切 + 壓縮
            processed = smart_crop(curr_img, CROP_DARK_THRESHOLD, CROP_WHITE_MARGIN) \
                        if CROP_ENABLED else curr_img
            jpg_bytes = image_to_jpg_bytes(processed, JPG_QUALITY)
            jpg_data_list.append(jpg_bytes)

            self._log(f"  ✅ 第 {page_num} 頁擷取完成")
            self._progress(page_num, self.max_pages)
            prev_img = curr_img

        # 輸出 PDF
        if jpg_data_list:
            self._log(f"\n📄 正在合併 {len(jpg_data_list)} 頁為 PDF…")
            pdf_path = self._export_pdf(jpg_data_list)
            self._log(f"✔ PDF 輸出完成：{pdf_path}")
            self._done(pdf_path)
        else:
            self._log("⚠ 沒有擷取到任何頁面。")
            self._done("")

    def _detect_change(self, img_a, img_b) -> bool:
        if self.mode == MODE_SCROLL:
            ratio = pixel_diff_ratio(img_a, img_b)
            return ratio >= PIXEL_DIFF_THRESHOLD
        else:
            return is_page_changed(img_a, img_b, DHASH_THRESHOLD)

    def _export_pdf(self, jpg_data_list) -> str:
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        safe_name = self.book_name.strip() or "output"
        filename  = f"{safe_name}_{timestamp}.pdf"
        out_dir   = self.output_dir or OUTPUT_DIR
        os.makedirs(out_dir, exist_ok=True)
        output_path = os.path.join(out_dir, filename)

        return jpg_bytes_list_to_pdf(
            jpg_data_list,
            output_path,
            progress_callback=lambda c, t: self._log(f"  合併中 {c}/{t}…")
        )
