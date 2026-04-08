# BookCapture v4.0

Google Play Books 螢幕自動截圖工具，具備 tkinter GUI 介面。

## 版本狀態

**v4.0 Beta** — GUI 版本，tkinter + PyInstaller

## 功能

- 🖥 Scroll 模式：滾輪翻頁 + 像素差異偵測
- 📖 Page 模式：方向鍵翻頁 + dHash 感知雜湊偵測
- 🔁 Retry fallback：連續未翻頁自動停止
- 🖼 智慧裁切：白底 / 黑底自動裁切
- 📄 PDF 輸出：PNG → JPG (q85) 壓縮合併
- 📊 GUI 進度條 + 即時 log
- 🏗 PyInstaller 單一 .exe 打包

## 目錄結構

```
BookCapture/
├── book_capture/
│   ├── main.py         # tkinter GUI 主程式
│   ├── capturer.py     # 核心擷取引擎
│   ├── config.py       # 全域設定
│   └── utils/
│       ├── capture.py  # 截圖 / 裁切
│       ├── hash.py     # dHash 比對
│       └── pdf.py      # PDF 合併輸出
├── requirements.txt
└── build.bat           # PyInstaller 打包腳本
```

## 安裝與執行

```powershell
# 1. 建立目錄並切換
New-Item -ItemType Directory -Force "C:\claude\BookCapture"
cd C:\claude\BookCapture

# 2. 安裝相依套件
pip install -r requirements.txt

# 3. 執行 GUI
cd book_capture
python main.py
```

## 打包 .exe

```powershell
cd C:\claude\BookCapture
.\build.bat
# 輸出：book_capture\dist\BookCapture.exe
```

## 使用說明

1. 開啟 Google Play Books 並準備好書本頁面
2. 設定書名、輸出目錄、模式、頁數上限
3. 點擊「開始擷取」，3 秒內切換到書本視窗
4. 等待完成，PDF 自動儲存到指定目錄

## 變更記錄

| 版本 | 說明 |
|------|------|
| v4.0 Beta | tkinter GUI + PyInstaller 打包 |
| v3.1 Beta | 雙模式穩定版（CLI） |
| v3.0 | 4K DPI Awareness 修正 |
