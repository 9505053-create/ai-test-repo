# main.py - BookCapture v4.0 GUI 主程式
# 使用 tkinter 標準庫，不依賴第三方 UI 框架

import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import threading
import os
import sys
import subprocess

# ── 確保 book_capture 目錄在 path 內（PyInstaller 相容）─────────
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, BASE_DIR)

from config import OUTPUT_DIR, MODE_SCROLL, MODE_PAGE, PAGE_MAX, CAPTURE_DELAY
from capturer import CaptureSession

VERSION = "v4.0 Beta"
APP_NAME = "BookCapture"


class BookCaptureApp(tk.Tk):
    def __init__(self):
        super().__init__()

        self.title(f"{APP_NAME} {VERSION}")
        self.resizable(False, False)
        self.configure(bg="#1e1e2e")

        # ── 狀態變數 ─────────────────────────────────────────
        self._session: CaptureSession | None = None

        # ── 建立 UI ──────────────────────────────────────────
        self._build_ui()
        self._apply_style()

        # ── 視窗置中 ─────────────────────────────────────────
        self.update_idletasks()
        w, h = 560, 620
        x = (self.winfo_screenwidth()  - w) // 2
        y = (self.winfo_screenheight() - h) // 2
        self.geometry(f"{w}x{h}+{x}+{y}")

    # ══════════════════════════════════════════════════════════
    # UI 建構
    # ══════════════════════════════════════════════════════════

    def _build_ui(self):
        pad = {"padx": 14, "pady": 6}

        # ── 標題列 ────────────────────────────────────────────
        title_frame = tk.Frame(self, bg="#181825")
        title_frame.pack(fill="x")
        tk.Label(
            title_frame,
            text=f"📸  {APP_NAME}  {VERSION}",
            font=("Segoe UI", 13, "bold"),
            fg="#cdd6f4", bg="#181825"
        ).pack(pady=10)

        # ── 書名輸入 ──────────────────────────────────────────
        section = self._section("書本設定")
        row = tk.Frame(section, bg="#313244")
        row.pack(fill="x", **pad)
        tk.Label(row, text="書名：", font=("Segoe UI", 10),
                 fg="#cdd6f4", bg="#313244", width=8, anchor="w").pack(side="left")
        self.book_name_var = tk.StringVar(value="我的書本")
        tk.Entry(row, textvariable=self.book_name_var,
                 font=("Segoe UI", 10), bg="#45475a", fg="#cdd6f4",
                 insertbackground="#cdd6f4", relief="flat", width=30).pack(side="left", padx=4)

        # ── 輸出目錄 ──────────────────────────────────────────
        row2 = tk.Frame(section, bg="#313244")
        row2.pack(fill="x", **pad)
        tk.Label(row2, text="輸出目錄：", font=("Segoe UI", 10),
                 fg="#cdd6f4", bg="#313244", width=8, anchor="w").pack(side="left")
        self.output_dir_var = tk.StringVar(value=OUTPUT_DIR)
        tk.Entry(row2, textvariable=self.output_dir_var,
                 font=("Segoe UI", 9), bg="#45475a", fg="#a6adc8",
                 insertbackground="#cdd6f4", relief="flat", width=26).pack(side="left", padx=4)
        tk.Button(row2, text="瀏覽", command=self._browse_dir,
                  font=("Segoe UI", 9), bg="#585b70", fg="#cdd6f4",
                  relief="flat", padx=6, cursor="hand2").pack(side="left")

        # ── 擷取模式 ──────────────────────────────────────────
        section2 = self._section("擷取模式")
        self.mode_var = tk.StringVar(value=MODE_SCROLL)
        mode_frame = tk.Frame(section2, bg="#313244")
        mode_frame.pack(fill="x", **pad)

        for text, val, desc in [
            ("Scroll 模式", MODE_SCROLL, "滾輪翻頁 + 像素差異偵測"),
            ("Page 模式",   MODE_PAGE,   "方向鍵翻頁 + dHash 偵測"),
        ]:
            rb_frame = tk.Frame(mode_frame, bg="#313244")
            rb_frame.pack(side="left", padx=12)
            tk.Radiobutton(
                rb_frame, text=text, variable=self.mode_var, value=val,
                font=("Segoe UI", 10, "bold"), fg="#89dceb", bg="#313244",
                selectcolor="#1e1e2e", activebackground="#313244",
                cursor="hand2"
            ).pack(anchor="w")
            tk.Label(rb_frame, text=desc, font=("Segoe UI", 8),
                     fg="#6c7086", bg="#313244").pack(anchor="w")

        # ── 參數設定 ──────────────────────────────────────────
        section3 = self._section("擷取參數")
        param_frame = tk.Frame(section3, bg="#313244")
        param_frame.pack(fill="x", **pad)

        # 最大頁數
        self._param_row(param_frame, "最大頁數：", 0)
        self.max_pages_var = tk.IntVar(value=200)
        tk.Spinbox(
            param_frame, from_=1, to=PAGE_MAX,
            textvariable=self.max_pages_var,
            font=("Segoe UI", 10), bg="#45475a", fg="#cdd6f4",
            buttonbackground="#585b70", relief="flat", width=6
        ).grid(row=0, column=1, padx=8, pady=3, sticky="w")

        # 翻頁延遲
        self._param_row(param_frame, "翻頁延遲(秒)：", 1)
        self.delay_var = tk.DoubleVar(value=CAPTURE_DELAY)
        tk.Spinbox(
            param_frame, from_=0.5, to=5.0, increment=0.1,
            textvariable=self.delay_var,
            font=("Segoe UI", 10), bg="#45475a", fg="#cdd6f4",
            buttonbackground="#585b70", relief="flat", width=6,
            format="%.1f"
        ).grid(row=1, column=1, padx=8, pady=3, sticky="w")

        # ── 進度條 ────────────────────────────────────────────
        section4 = self._section("執行狀態")
        self.progress_var = tk.IntVar(value=0)
        self.progress_bar = ttk.Progressbar(
            section4, variable=self.progress_var,
            maximum=100, length=500, mode="determinate"
        )
        self.progress_bar.pack(padx=14, pady=(4, 2))
        self.status_label = tk.Label(
            section4, text="準備就緒", font=("Segoe UI", 9),
            fg="#a6e3a1", bg="#313244"
        )
        self.status_label.pack(pady=(0, 6))

        # ── Log 輸出區 ────────────────────────────────────────
        log_frame = tk.Frame(self, bg="#1e1e2e")
        log_frame.pack(fill="both", expand=True, padx=14, pady=(0, 8))
        tk.Label(log_frame, text="執行記錄", font=("Segoe UI", 9, "bold"),
                 fg="#6c7086", bg="#1e1e2e").pack(anchor="w")
        self.log_text = tk.Text(
            log_frame, height=8, state="disabled",
            font=("Consolas", 9), bg="#11111b", fg="#a6adc8",
            relief="flat", wrap="word", insertbackground="#cdd6f4"
        )
        scroll_bar = tk.Scrollbar(log_frame, command=self.log_text.yview)
        self.log_text.configure(yscrollcommand=scroll_bar.set)
        scroll_bar.pack(side="right", fill="y")
        self.log_text.pack(fill="both", expand=True)

        # ── 控制按鈕 ──────────────────────────────────────────
        btn_frame = tk.Frame(self, bg="#1e1e2e")
        btn_frame.pack(pady=10)

        self.start_btn = tk.Button(
            btn_frame, text="▶  開始擷取", command=self._on_start,
            font=("Segoe UI", 11, "bold"),
            bg="#a6e3a1", fg="#1e1e2e",
            relief="flat", padx=20, pady=8, cursor="hand2",
            activebackground="#94d3a2"
        )
        self.start_btn.pack(side="left", padx=8)

        self.stop_btn = tk.Button(
            btn_frame, text="⏹  停止", command=self._on_stop,
            font=("Segoe UI", 11, "bold"),
            bg="#f38ba8", fg="#1e1e2e",
            relief="flat", padx=20, pady=8, cursor="hand2",
            state="disabled", activebackground="#e37898"
        )
        self.stop_btn.pack(side="left", padx=8)

        self.open_btn = tk.Button(
            btn_frame, text="📁  開啟資料夾", command=self._open_output_dir,
            font=("Segoe UI", 10),
            bg="#585b70", fg="#cdd6f4",
            relief="flat", padx=12, pady=8, cursor="hand2"
        )
        self.open_btn.pack(side="left", padx=8)

    def _section(self, title: str) -> tk.Frame:
        """建立帶標題的區塊 Frame"""
        outer = tk.Frame(self, bg="#1e1e2e")
        outer.pack(fill="x", padx=14, pady=4)
        tk.Label(outer, text=title, font=("Segoe UI", 9, "bold"),
                 fg="#89b4fa", bg="#1e1e2e").pack(anchor="w", pady=(2, 0))
        inner = tk.Frame(outer, bg="#313244", relief="flat", bd=0)
        inner.pack(fill="x")
        return inner

    def _param_row(self, parent, label: str, row: int):
        tk.Label(parent, text=label, font=("Segoe UI", 10),
                 fg="#cdd6f4", bg="#313244", anchor="w", width=14
                 ).grid(row=row, column=0, pady=3, sticky="w")

    def _apply_style(self):
        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure(
            "Horizontal.TProgressbar",
            troughcolor="#313244",
            background="#89b4fa",
            bordercolor="#313244",
            lightcolor="#89b4fa",
            darkcolor="#89b4fa"
        )

    # ══════════════════════════════════════════════════════════
    # 事件處理
    # ══════════════════════════════════════════════════════════

    def _browse_dir(self):
        path = filedialog.askdirectory(title="選擇輸出目錄")
        if path:
            self.output_dir_var.set(path)

    def _on_start(self):
        if self._session and self._session.is_running():
            return

        book_name = self.book_name_var.get().strip() or "output"
        out_dir   = self.output_dir_var.get().strip() or OUTPUT_DIR
        mode      = self.mode_var.get()
        max_pages = self.max_pages_var.get()
        delay     = self.delay_var.get()

        self._clear_log()
        self.progress_var.set(0)
        self._set_status("擷取中…", "#f9e2af")
        self.start_btn.config(state="disabled")
        self.stop_btn.config(state="normal")

        self._session = CaptureSession(
            mode=mode,
            output_dir=out_dir,
            book_name=book_name,
            max_pages=max_pages,
            delay=delay,
            log_callback=self._log,
            progress_callback=self._on_progress,
            done_callback=self._on_done,
        )
        self._session.start()

    def _on_stop(self):
        if self._session:
            self._session.stop()
        self.stop_btn.config(state="disabled")

    def _on_progress(self, current: int, total: int):
        pct = int(current / total * 100) if total > 0 else 0
        self.after(0, lambda: self.progress_var.set(pct))
        self.after(0, lambda: self._set_status(f"擷取中… {current} / {total} 頁", "#f9e2af"))

    def _on_done(self, pdf_path: str):
        def _ui():
            self.start_btn.config(state="normal")
            self.stop_btn.config(state="disabled")
            self.progress_var.set(100)
            if pdf_path:
                self._set_status(f"✔ 完成！{os.path.basename(pdf_path)}", "#a6e3a1")
                if messagebox.askyesno("完成", f"擷取完成！\n\n{pdf_path}\n\n是否立即開啟 PDF？"):
                    os.startfile(pdf_path)
            else:
                self._set_status("已停止（無輸出）", "#f38ba8")
        self.after(0, _ui)

    def _open_output_dir(self):
        path = self.output_dir_var.get().strip() or OUTPUT_DIR
        os.makedirs(path, exist_ok=True)
        subprocess.Popen(["explorer", path])

    # ══════════════════════════════════════════════════════════
    # Log 工具
    # ══════════════════════════════════════════════════════════

    def _log(self, msg: str):
        def _append():
            self.log_text.config(state="normal")
            self.log_text.insert("end", msg + "\n")
            self.log_text.see("end")
            self.log_text.config(state="disabled")
        self.after(0, _append)

    def _clear_log(self):
        self.log_text.config(state="normal")
        self.log_text.delete("1.0", "end")
        self.log_text.config(state="disabled")

    def _set_status(self, text: str, color: str = "#a6adc8"):
        self.status_label.config(text=text, fg=color)


# ── 入口點 ────────────────────────────────────────────────────────
if __name__ == "__main__":
    app = BookCaptureApp()
    app.mainloop()
