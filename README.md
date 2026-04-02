# Windows 開發環境快照器 (env_snapshot.ps1)

**Version:** 1.1.0 | **Upgrade from:** 0.2.0

產出一份 AI 可讀的開發環境快照，用於 debug 與環境問題判斷。
核心價值：`risk_flags` 作為 AI 的第一入口，快速定位環境地雷。

---

## 使用方式

```powershell
# 直接執行（建議在專案根目錄）
.\env_snapshot.ps1
```

執行後會在當前目錄建立：

```
snapshots/YYYY-MM-DD_HH-mm-ss_HOSTNAME/
  snapshot.json    ← AI 讀取用（結構化資料）
  snapshot.md      ← 人類快速瀏覽用
```

---

## Data Flow

```
System → Modules → risk_flags → JSON → Markdown
                                  ↓
                            AI Analysis
```

---

## AI 使用方式

```
Input:  snapshot.json
Output: AI 可直接進行：
  1. 讀取 risk_flags → 快速定位問題
  2. 深入 details → 分析具體原因
  3. 提供修復建議
```

---

## JSON 結構 (v1.1)

```json
{
  "metadata": {
    "tool_version": "1.1.0",
    "timestamp": "2026-04-03T12:00:00Z",
    "hostname": "SCOTT-PC",
    "current_user": "Scott",
    "is_admin": false
  },
  "risk_flags": [
    {
      "id": "pip_mismatch",
      "severity": "high",
      "hint": "pip and python are in different directories. Packages may install to wrong environment."
    },
    {
      "id": "encoding_mismatch",
      "severity": "medium",
      "hint": "Active code page is 950, not UTF-8 (65001). Non-ASCII characters may corrupt."
    },
    {
      "id": "port_conflict_risk",
      "severity": "low",
      "hint": "Common dev ports already in use: 8080(node). New services on these ports will fail to bind."
    }
  ],
  "details": {
    "system": {},
    "hardware": {},
    "storage": {},
    "python": {},
    "dev_tools": {},
    "services": {},
    "environment": {},
    "command_resolution": {},
    "startup": [],
    "network": {}
  },
  "collection_status": {
    "success": ["meta", "system", "hardware", "..."],
    "partial": [],
    "failed": []
  }
}
```

---

## 權限說明

| 需求 | 說明 |
|------|------|
| Admin | **不需要**，所有功能 best-effort 執行 |
| ExecutionPolicy | 需允許腳本執行。若為 Restricted，先執行：`Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned` |
| 網路 | 不需要，完全本機操作 |

---

## v1.1 升級重點（vs v0.2）

| 項目 | v0.2 | v1.1 |
|------|------|------|
| JSON 結構 | 扁平 12 keys | `metadata` → `risk_flags` → `details`（AI-first） |
| risk_flags 格式 | id + severity + detail | id + severity + **hint**（人類可讀說明） |
| 新增 risk_flags | 9 項 | 12 項（+path_length_warning, port_conflict_risk, pending_reboot） |
| 服務偵測 | 無 | Docker + n8n 服務狀態 |
| 安全過濾 | 無 | env 變數含 TOKEN/KEY/SECRET/PASSWORD 自動 scrub |
| 防卡死 | 無 | hardware/network/CIM 加 timeout wrapper |
| Pending reboot | 無 | 偵測 Registry 3 處 reboot pending |
| Port 掃描 | 無 | 7 個常用 dev port 佔用偵測 |
| 程式架構 | 線性腳本 | 模組化 function 分離 |
| 向下相容 | - | 所有 v0.2 欄位保留 |

---

## Risk Flags 完整清單

| Flag ID | Severity | 觸發條件 |
|---------|----------|----------|
| `python_not_in_path` | high | PATH 中找不到 Python |
| `multiple_python_versions` | medium | 多個 Python 執行檔 |
| `pip_mismatch` | high | pip 和 python 不在同一目錄 |
| `low_disk_space` | high | 系統磁碟剩餘 < 10GB |
| `invalid_path_entries` | medium | PATH 中有不存在的路徑 |
| `execution_policy_restricted` | high | ExecutionPolicy 為 Restricted 或 AllSigned |
| `encoding_mismatch` | medium | Code page 非 65001 (UTF-8) |
| `shadowed_commands` | medium | 命令被多個路徑遮蔽 |
| `ms_store_stub_detected` | high | Microsoft Store 假 Python stub |
| `path_length_warning` | medium | 長路徑未啟用（>260 字元可能失敗） |
| `port_conflict_risk` | low | 常用 dev port 已被佔用 |
| `pending_reboot` | medium | 系統有待重開機的更新 |

---

## 安全性

- 環境變數名稱含 TOKEN / KEY / SECRET / PASSWORD / CREDENTIAL 時，值自動替換為 `***SCRUBBED***`
- 不收集密碼、SSH key、API token
- 不修改任何系統設定
- 不安裝任何東西

---

## 限制

- **僅支援 Windows**（PowerShell 5.1+）
- SSD 偵測為 best-effort，部分虛擬化環境可能回傳 `null`
- 僅掃描系統磁碟（C:），不掃其他磁碟
- startup 項目僅從 Registry Run key 讀取，不含排程工作或服務
- 非 admin 時部分 Registry / CIM 查詢可能回傳 null
- Port 掃描僅檢查 7 個常用 dev port，非完整掃描
- n8n 偵測依賴服務名稱或 port 5678，Docker 內的 n8n 可能不被偵測
