# Windows 開發環境快照器 (env_snapshot.ps1)

**Version:** 0.2.0

產出一份 AI 可讀的開發環境快照，用於 debug 與環境問題判斷。

---

## 使用方式

```powershell
# 直接執行（建議在專案根目錄）
.\env_snapshot.ps1
```

執行後會在當前目錄建立：

```
snapshots/YYYY-MM-DD_HH-mm-ss_HOSTNAME/
  snapshot.json    ← AI 讀取用（完整結構化資料）
  snapshot.md      ← 人類快速瀏覽用
```

---

## 權限說明

| 需求 | 說明 |
|------|------|
| Admin | **不需要**，所有功能 best-effort 執行 |
| ExecutionPolicy | 需允許腳本執行。若為 Restricted，先執行：`Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned` |
| 網路 | 不需要，完全本機操作 |

---

## 輸出範例

### snapshot.md 結構

1. **Summary** — 各模組收集狀態（OK / PARTIAL / FAILED）
2. **Risk Flags** — 偵測到的風險（嚴重度 + 說明）
3. **Python** — 版本、路徑、venv 狀態
4. **Dev Tools** — python/pip/node/npm/git/java/docker 表格
5. **PATH / ENV** — 重複/無效 PATH、關鍵環境變數
6. **System / Hardware / Storage** — OS、CPU、RAM、磁碟
7. **Full JSON** — JSON 檔案路徑

### Risk Flags 偵測項目

| Flag ID | 說明 |
|---------|------|
| `python_not_in_path` | PATH 中找不到 Python |
| `multiple_python_versions` | 多個 Python 執行檔（版本衝突風險） |
| `pip_mismatch` | pip 和 python 不在同一目錄 |
| `low_disk_space` | 系統磁碟剩餘 < 10GB |
| `invalid_path_entries` | PATH 中有不存在的路徑 |
| `execution_policy_restricted` | ExecutionPolicy 為 Restricted 或 AllSigned |
| `encoding_mismatch` | Code page 非 65001 (UTF-8) |
| `shadowed_commands` | 命令被多個路徑遮蔽 |
| `ms_store_stub_detected` | 偵測到 Microsoft Store 的假 Python stub |

---

## 限制

- **僅支援 Windows**（PowerShell 5.1+）
- SSD 偵測為 best-effort，部分虛擬化環境可能回傳 `null`
- 不掃描所有磁碟，僅系統磁碟（C:）
- 不安裝任何東西，不修改系統設定
- 不收集敏感資訊（密碼、token、SSH key 等）
- startup 項目僅從 Registry Run key 讀取，不含排程工作或服務

---

## JSON 結構

```
{
  "meta": {},              // 工具版本、時間戳、主機名
  "system": {},            // OS、PowerShell、編碼、長路徑
  "hardware": {},          // CPU、RAM
  "storage": {},           // 系統磁碟容量
  "python": {},            // Python/pip 版本與路徑
  "dev_tools": {},         // 7 個關鍵工具的存在性與版本
  "environment": {},       // PATH 分析、關鍵環境變數
  "command_resolution": {},// 命令解析、遮蔽偵測、MS Store stub
  "startup": [],           // 開機啟動項目
  "network": {},           // 主機名、IP、閘道、DNS
  "risk_flags": [],        // 風險旗標（核心價值）
  "collection_status": {}  // 各模組收集結果
}
```
