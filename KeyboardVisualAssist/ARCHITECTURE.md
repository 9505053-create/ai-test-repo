# Keyboard Visual Assist Tool — 架構設計文件
Version: MVP 1.0 | Status: Beta

---

## 一、專案目錄結構

```
KeyboardVisualAssist/
├── KeyboardVisualAssist.csproj
├── App.xaml
├── App.xaml.cs
│
├── InputCapture/
│   ├── KeyboardHook.cs          # Win32 WH_KEYBOARD_LL 封裝
│   └── KeyEventArgs.cs          # 標準化按鍵事件 DTO
│
├── KeyMap/
│   ├── KeyMapModel.cs           # 單鍵資料模型
│   ├── KeyMapRepository.cs      # 載入 JSON keymap
│   └── KeyMapper.cs             # VirtualKey → 顯示資料轉換
│
├── Overlay/
│   ├── OverlayWindow.xaml       # WPF 透明視窗
│   ├── OverlayWindow.xaml.cs    # Code-behind + WS_EX_NOACTIVATE
│   └── OverlayViewModel.cs      # UI State 管理
│
├── Monitor/
│   └── ForegroundAppMonitor.cs  # 前景程式偵測
│
├── Config/
│   └── ConfigService.cs         # config.json 載入/儲存
│
├── Logger/
│   └── AppLogger.cs             # 簡易 file logger（不記錄輸入內容）
│
├── assets/
│   ├── config.json
│   ├── keymap.standard.json
│   └── keymap.hsu.json
│
└── KeyEventQueue.cs             # ConcurrentQueue 緩衝層
```

---

## 二、類別架構（Class Diagram 概念）

```
┌─────────────────────────────────────────────────────────┐
│                      App Entry                          │
│                      App.xaml.cs                        │
│   - 初始化 ConfigService                                 │
│   - 初始化 KeyMapRepository                              │
│   - 初始化 OverlayWindow                                 │
│   - 啟動 KeyboardHook                                    │
│   - 啟動 ForegroundAppMonitor                           │
└───────────────────┬─────────────────────────────────────┘
                    │
        ┌───────────┴───────────┐
        ▼                       ▼
┌───────────────┐    ┌──────────────────────┐
│ KeyboardHook  │    │ ForegroundAppMonitor │
│               │    │                      │
│ WH_KEYBOARD_LL│    │ GetForegroundWindow()│
│ KeyDown/Up    │    │ GetWindowInfo()      │
│ 發送標準化事件 │    │ → 判斷是否顯示 Overlay│
└───────┬───────┘    └──────────┬───────────┘
        │                       │
        ▼                       ▼
┌───────────────┐    ┌──────────────────────┐
│ KeyEventQueue │    │   OverlayViewModel   │
│               │    │                      │
│ ConcurrentQ   │───▶│ RecentKeys (list)    │
│ Timer 消化    │    │ HighlightedKey       │
└───────┬───────┘    │ FadeOut Timer        │
        │            │ IsVisible            │
        ▼            └──────────┬───────────┘
┌───────────────┐               │
│   KeyMapper   │               ▼
│               │    ┌──────────────────────┐
│ VK → Display  │    │   OverlayWindow      │
│ Standard/Hsu  │    │                      │
└───────┬───────┘    │ TopMost              │
        │            │ AllowsTransparency   │
        ▼            │ WS_EX_NOACTIVATE     │
┌───────────────┐    │ Click-through        │
│KeyMapRepository│   └──────────────────────┘
│               │
│ 載入 JSON     │
│ Standard/Hsu  │
└───────────────┘
```

---

## 三、各模組責任分離

| 模組 | 責任 | 不做的事 |
|------|------|----------|
| KeyboardHook | Win32 Hook 封裝、發送事件 | 不操作 UI、不做映射 |
| KeyEventQueue | 緩衝按鍵事件、保護 Hook callback | 不做業務邏輯 |
| KeyMapper | VK code → 顯示字串 | 不做 UI |
| KeyMapRepository | 讀 JSON、管理 layout | 不做轉換邏輯 |
| OverlayViewModel | UI 狀態管理、高亮/淡出 | 不做 Hook |
| OverlayWindow | 純 View，綁 ViewModel | 不做邏輯 |
| ForegroundAppMonitor | 前景 App 偵測 | 不做鍵盤相關 |
| ConfigService | JSON 讀寫 | 不做業務邏輯 |
| AppLogger | 記錄錯誤/狀態 | 絕對不記錄輸入內容 |

---

## 四、MVP 事件流

```
使用者按下實體鍵盤
        │
        ▼
[Win32 WH_KEYBOARD_LL callback]
  KeyboardHook.HookCallback()
  ┌─ 解析 VirtualKey code
  └─ 封裝 KeyEventArgs（vk, isKeyDown, timestamp）
        │
        ▼
[KeyEventQueue]
  queue.Enqueue(keyEvent)     ← Hook callback 立即返回，不阻塞
        │
  DispatcherTimer（UI thread）
  queue.TryDequeue()
        │
        ▼
[KeyMapper]
  GetDisplayInfo(vk, currentLayout)
  → DisplayLabel（顯示字串）
  → KeyId（對應鍵盤圖 key id）
  → IsModifier（是否修飾鍵）
        │
        ▼
[OverlayViewModel]
  UpdateRecentKeys(displayInfo)   → 最近 N 鍵清單
  SetHighlight(keyId)             → 鍵盤高亮
  StartFadeTimer()                → 淡出計時
        │
        ▼
[ForegroundAppMonitor]
  IsTargetApp() → true/false
        │
        ▼
[OverlayWindow]
  顯示/隱藏 Overlay
  更新最近按鍵列表
  更新鍵盤高亮狀態
  執行淡出動畫
```
