# KeyboardVisualAssist — 建置與發布說明

## 環境需求

| 項目 | 版本 |
|------|------|
| .NET SDK | 8.0+ |
| 作業系統 | Windows 10/11 x64 |
| 目標環境 | 無需 Admin，無需安裝 Runtime |

---

## 建置指令

### 開發測試（需本機 .NET 8）
```bash
dotnet build
dotnet run
```

### 發布 — Self-contained Single-file EXE（目標機器不需安裝 Runtime）
```bash
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish
```

發布完成後，`publish/` 目錄會包含：
```
publish/
├── KeyboardVisualAssist.exe   ← 單一可攜式 EXE
└── assets/
    ├── config.json
    ├── keymap.standard.json
    └── keymap.hsu.json
```

> **注意**：`assets/` 資料夾必須與 EXE 同層。設定異動只需修改 JSON，不需重新編譯。

---

## 目標機器部署

1. 將 `publish/` 整個資料夾複製到目標機器任意位置（例如 `D:\Tools\KeyboardVisualAssist\`）
2. 直接雙擊 `KeyboardVisualAssist.exe`
3. 如果防毒軟體攔截：
   - 確認執行畫面是可見的 Overlay，不是背景隱匿程式
   - 向 IT 申報例外：程式不寫出輸入內容，只記錄錯誤狀態

---

## 設定調整（不需重新編譯）

### config.json 主要選項

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `ActiveLayout` | `Standard` 或 `Hsu` | `Standard` |
| `HighlightDurationMs` | 高亮淡出時間（ms） | `500` |
| `RecentKeyCount` | 最近按鍵顯示數 | `5` |
| `OverlayOpacity` | Overlay 透明度 0~1 | `0.88` |
| `ShowKeyboardMap` | 是否顯示鍵盤圖 | `true` |
| `TargetApps` | 觸發顯示的 App 清單 | Word/Excel 等 |

---

## 注意事項

- **不儲存輸入內容**：Logger 只記錄錯誤與狀態，不記錄任何打字內容
- **不修改輸入法**：純視覺輔助，不影響任何 IME 狀態
- **不需 Admin 權限**：WH_KEYBOARD_LL 在一般使用者權限下即可運作
- **建議放 D: 槽**：避免 C: 槽權限問題

---

## 版本歷史

| 版本 | 日期 | 狀態 |
|------|------|------|
| 1.0.0 Beta | 2026-04 | MVP，基礎 Hook + Overlay + 許式鍵盤圖 |
