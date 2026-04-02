#Requires -Version 5.1
<#
.SYNOPSIS
    Windows Development Environment Snapshot Tool v1.1.0
.DESCRIPTION
    Produces an AI-readable snapshot of the development environment.
    Output: snapshot.json + snapshot.md in snapshots/<timestamp>_<hostname>/
    Core value: risk_flags as AI's first entry point for environment diagnosis.
.NOTES
    No admin required (best-effort for some fields).
    Will not abort on partial failures.
    Sensitive env vars (TOKEN/KEY/SECRET/PASSWORD) are automatically scrubbed.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$TOOL_VERSION = "1.1.0"
$TIMEOUT_SEC = 5
$startTime = Get-Date

# ── Output folder ──
$timestamp = $startTime.ToString("yyyy-MM-dd_HH-mm-ss")
$hostName = $env:COMPUTERNAME ?? "UNKNOWN"
$outDir = Join-Path $PSScriptRoot "snapshots" "${timestamp}_${hostName}"

if (Test-Path $outDir) {
    $outDir = "${outDir}_$(Get-Random -Maximum 9999)"
}
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# ── Tracking ──
$collectionStatus = @{ success = [System.Collections.ArrayList]::new(); partial = [System.Collections.ArrayList]::new(); failed = [System.Collections.ArrayList]::new() }

function Add-Status($module, $status) {
    $collectionStatus[$status].Add($module) | Out-Null
}

# ── Security: Scrub sensitive env values ──
function Test-SensitiveKey([string]$keyName) {
    $patterns = @('TOKEN', 'KEY', 'SECRET', 'PASSWORD', 'PASSWD', 'CREDENTIAL', 'API_KEY', 'APIKEY')
    foreach ($p in $patterns) {
        if ($keyName -match $p) { return $true }
    }
    return $false
}

function Get-ScrubbedValue([string]$keyName, [string]$value) {
    if (Test-SensitiveKey $keyName) { return "***SCRUBBED***" }
    return $value
}

# ── Timeout wrapper for potentially blocking calls ──
function Invoke-WithTimeout {
    param(
        [scriptblock]$ScriptBlock,
        [int]$TimeoutSeconds = $TIMEOUT_SEC,
        $Default = $null
    )
    try {
        $job = Start-Job -ScriptBlock $ScriptBlock
        $completed = Wait-Job $job -Timeout $TimeoutSeconds
        if ($completed) {
            $result = Receive-Job $job -ErrorAction SilentlyContinue
            Remove-Job $job -Force -ErrorAction SilentlyContinue
            return $result
        } else {
            Stop-Job $job -ErrorAction SilentlyContinue
            Remove-Job $job -Force -ErrorAction SilentlyContinue
            return $Default
        }
    } catch {
        return $Default
    }
}

# ════════════════════════════════════════
# MODULE: META
# ════════════════════════════════════════
function Get-SnapshotMeta {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

        $result = @{
            tool_version  = $TOOL_VERSION
            timestamp     = $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            hostname      = $hostName
            current_user  = $env:USERNAME
            is_admin      = $isAdmin
        }
        Add-Status "meta" "success"
        return $result
    } catch {
        Add-Status "meta" "partial"
        return @{ tool_version = $TOOL_VERSION; timestamp = $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); error = $_.Exception.Message }
    }
}

# ════════════════════════════════════════
# MODULE: SYSTEM
# ════════════════════════════════════════
function Get-SystemInfo {
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        $psVer = $PSVersionTable.PSVersion.ToString()
        $execPolicy = try { (Get-ExecutionPolicy).ToString() } catch { "unknown" }

        $codePage = try {
            $chcpOut = & chcp 2>$null
            if ($chcpOut -match '(\d+)') { [int]$Matches[1] } else { $null }
        } catch { $null }

        $longPaths = try {
            $val = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -ErrorAction Stop
            [bool]$val.LongPathsEnabled
        } catch { $null }

        # Pending reboot detection
        $pendingReboot = $false
        try {
            if (Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") { $pendingReboot = $true }
            if (Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") { $pendingReboot = $true }
            $pfu = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager" -Name "PendingFileRenameOperations" -ErrorAction SilentlyContinue
            if ($pfu -and $pfu.PendingFileRenameOperations) { $pendingReboot = $true }
        } catch { }

        $result = @{
            os_name                     = $os.Caption
            os_version                  = $os.Version
            os_build                    = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction SilentlyContinue).DisplayVersion ?? "unknown"
            architecture                = $env:PROCESSOR_ARCHITECTURE
            timezone                    = (Get-TimeZone).Id
            powershell_version          = $psVer
            powershell_execution_policy = $execPolicy
            active_code_page            = $codePage
            long_paths_enabled          = $longPaths
            pending_reboot              = $pendingReboot
        }
        Add-Status "system" "success"
        return $result
    } catch {
        Add-Status "system" "failed"
        return @{ error = $_.Exception.Message }
    }
}

# ════════════════════════════════════════
# MODULE: HARDWARE (with timeout)
# ════════════════════════════════════════
function Get-HardwareInfo {
    try {
        $cpu = Invoke-WithTimeout -ScriptBlock { Get-CimInstance Win32_Processor | Select-Object -First 1 -Property Name, NumberOfCores, NumberOfLogicalProcessors }
        $cs = Invoke-WithTimeout -ScriptBlock { Get-CimInstance Win32_ComputerSystem | Select-Object -First 1 -Property TotalPhysicalMemory }
        $freeRam = Invoke-WithTimeout -ScriptBlock { (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory }

        if (-not $cpu -or -not $cs) {
            Add-Status "hardware" "partial"
            return @{ error = "CIM query timed out or returned null" }
        }

        $result = @{
            cpu_model        = $cpu.Name.Trim()
            physical_cores   = $cpu.NumberOfCores
            logical_cores    = $cpu.NumberOfLogicalProcessors
            total_ram_gb     = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
            available_ram_gb = if ($freeRam) { [math]::Round($freeRam / 1MB, 1) } else { $null }
        }
        Add-Status "hardware" "success"
        return $result
    } catch {
        Add-Status "hardware" "failed"
        return @{ error = $_.Exception.Message }
    }
}

# ════════════════════════════════════════
# MODULE: STORAGE
# ════════════════════════════════════════
function Get-StorageInfo {
    try {
        $vol = Get-Volume -DriveLetter $env:SystemDrive[0] -ErrorAction Stop
        $partition = Get-Partition -DriveLetter $env:SystemDrive[0] -ErrorAction SilentlyContinue
        $physDisk = if ($partition) { try { $partition | Get-Disk -ErrorAction SilentlyContinue } catch { $null } } else { $null }
        $isSsd = if ($physDisk) { $physDisk.MediaType -eq 'SSD' } else { $null }

        $result = @{
            system_drive  = $env:SystemDrive
            disk_total_gb = [math]::Round($vol.Size / 1GB, 1)
            disk_free_gb  = [math]::Round($vol.SizeRemaining / 1GB, 1)
            filesystem    = $vol.FileSystemType
            is_ssd        = $isSsd
        }
        Add-Status "storage" "success"
        return $result
    } catch {
        Add-Status "storage" "partial"
        return @{ error = $_.Exception.Message }
    }
}

# ════════════════════════════════════════
# MODULE: PYTHON
# ════════════════════════════════════════
function Get-PythonInfo {
    try {
        $pyCmd = Get-Command python -ErrorAction SilentlyContinue
        if (-not $pyCmd) {
            Add-Status "python" "partial"
            return @{ python_version = "not found"; python_path = $null; is_venv = $null; virtual_env_type = "none" }
        }

        $pyPath = $pyCmd.Source
        $pyVer = try { & python --version 2>&1 | ForEach-Object { $_ -replace 'Python\s*', '' } } catch { "unknown" }

        $pipVer = try {
            & pip --version 2>&1 | ForEach-Object {
                if ($_ -match '^pip\s+([\d.]+)\s+from\s+(.+?)\s') { @{ version = $Matches[1]; path = $Matches[2] } }
                else { @{ version = "unknown"; path = "unknown" } }
            }
        } catch { @{ version = "not found"; path = $null } }

        $pyInfo = try {
            $raw = & python -c "import sys,json,site; print(json.dumps({'executable':sys.executable,'sys_path':sys.path,'prefix':sys.prefix,'base_prefix':sys.base_prefix,'site_packages':[p for p in site.getsitepackages()],'user_site_enabled':site.ENABLE_USER_SITE}))" 2>&1
            $raw | ConvertFrom-Json
        } catch { $null }

        $isVenv = if ($pyInfo) { $pyInfo.prefix -ne $pyInfo.base_prefix } else { $null }
        $venvType = if ($env:VIRTUAL_ENV) { "venv" } elseif ($env:CONDA_DEFAULT_ENV) { "conda" } elseif ($isVenv) { "venv" } else { "none" }

        $result = @{
            python_version     = $pyVer
            python_path        = $pyPath
            pip_version        = $pipVer.version
            pip_path           = $pipVer.path
            sys_executable     = if ($pyInfo) { $pyInfo.executable } else { $null }
            sys_path           = if ($pyInfo) { $pyInfo.sys_path } else { @() }
            site_packages_path = if ($pyInfo) { $pyInfo.site_packages } else { @() }
            is_venv            = $isVenv
            virtual_env_type   = $venvType
            user_site_enabled  = if ($pyInfo) { $pyInfo.user_site_enabled } else { $null }
        }
        Add-Status "python" "success"
        return $result
    } catch {
        Add-Status "python" "failed"
        return @{ error = $_.Exception.Message }
    }
}

# ════════════════════════════════════════
# MODULE: DEV_TOOLS
# ════════════════════════════════════════
function Get-DevToolsInfo {
    $devTools = @{}
    $toolList = @("python", "pip", "node", "npm", "git", "java", "docker")

    foreach ($tool in $toolList) {
        try {
            $cmds = @(Get-Command $tool -All -ErrorAction SilentlyContinue)
            if ($cmds.Count -gt 0) {
                $ver = try {
                    switch ($tool) {
                        "java" { $raw = & java -version 2>&1; ($raw | Select-Object -First 1) -replace '.*"(.+)".*', '$1' }
                        default { $raw = & $tool --version 2>&1; ($raw | Select-Object -First 1).ToString().Trim() }
                    }
                } catch { "unknown" }

                $devTools[$tool] = @{
                    exists             = $true
                    version            = $ver
                    resolved_path      = $cmds[0].Source
                    all_resolved_paths = @($cmds | ForEach-Object { $_.Source })
                }
            } else {
                $devTools[$tool] = @{ exists = $false; version = $null; resolved_path = $null; all_resolved_paths = @() }
            }
        } catch {
            $devTools[$tool] = @{ exists = $false; version = $null; resolved_path = $null; all_resolved_paths = @(); error = $_.Exception.Message }
        }
    }
    Add-Status "dev_tools" "success"
    return $devTools
}

# ════════════════════════════════════════
# MODULE: SERVICES (Docker / n8n)
# ════════════════════════════════════════
function Get-ServiceStatus {
    $services = @{}

    # Docker
    try {
        $dockerSvc = Get-Service -Name "com.docker.service" -ErrorAction SilentlyContinue
        if (-not $dockerSvc) { $dockerSvc = Get-Service -Name "docker" -ErrorAction SilentlyContinue }
        $services["docker"] = if ($dockerSvc) {
            @{ exists = $true; status = $dockerSvc.Status.ToString() }
        } else {
            @{ exists = $false; status = $null }
        }
    } catch {
        $services["docker"] = @{ exists = $false; status = $null; error = $_.Exception.Message }
    }

    # n8n
    try {
        $n8nSvc = Get-Service -Name "n8n" -ErrorAction SilentlyContinue
        $n8nPort = try {
            $conn = Get-NetTCPConnection -LocalPort 5678 -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($conn) { $true } else { $false }
        } catch { $false }

        $services["n8n"] = if ($n8nSvc) {
            @{ exists = $true; status = $n8nSvc.Status.ToString(); port_5678_active = $n8nPort }
        } else {
            @{ exists = $false; status = $null; port_5678_active = $n8nPort }
        }
    } catch {
        $services["n8n"] = @{ exists = $false; status = $null; error = $_.Exception.Message }
    }

    Add-Status "services" "success"
    return $services
}

# ════════════════════════════════════════
# MODULE: ENVIRONMENT (with security scrubbing)
# ════════════════════════════════════════
function Get-EnvironmentInfo {
    try {
        $pathRaw = $env:PATH
        $pathEntries = @($pathRaw -split ';' | Where-Object { $_ -ne '' })
        $duplicates = @($pathEntries | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
        $invalid = @($pathEntries | Where-Object { -not (Test-Path $_ -ErrorAction SilentlyContinue) })

        $keyEnvNames = @("PATH", "PYTHONPATH", "JAVA_HOME", "NODE_PATH", "VIRTUAL_ENV")
        $keyEnv = @{}
        foreach ($k in $keyEnvNames) {
            $val = [System.Environment]::GetEnvironmentVariable($k)
            $keyEnv[$k] = Get-ScrubbedValue $k $val
        }

        $result = @{
            path_raw               = $pathRaw
            path_entries           = $pathEntries
            path_entry_count       = $pathEntries.Count
            duplicate_path_entries = $duplicates
            invalid_path_entries   = $invalid
            key_env                = $keyEnv
        }
        Add-Status "environment" "success"
        return $result
    } catch {
        Add-Status "environment" "failed"
        return @{ error = $_.Exception.Message }
    }
}

# ════════════════════════════════════════
# MODULE: COMMAND_RESOLUTION
# ════════════════════════════════════════
function Get-CommandResolution {
    $commandResolution = @{}
    $resolveCmds = @("python", "pip", "node", "git")

    foreach ($cmd in $resolveCmds) {
        try {
            $allPaths = @()
            $gcPaths = @(Get-Command $cmd -All -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
            $allPaths += $gcPaths
            $wherePaths = try { @(& where.exe $cmd 2>$null) } catch { @() }
            $allPaths += $wherePaths
            $allPaths = @($allPaths | Select-Object -Unique)

            $primary = if ($allPaths.Count -gt 0) { $allPaths[0] } else { $null }
            $shadowed = $allPaths.Count -gt 1

            $msStub = $false
            foreach ($p in $allPaths) {
                if ($p -match 'WindowsApps') {
                    $fileInfo = Get-Item $p -ErrorAction SilentlyContinue
                    if ($fileInfo -and $fileInfo.Length -lt 1024) { $msStub = $true; break }
                }
            }

            $commandResolution[$cmd] = @{
                resolved_paths         = $allPaths
                primary_path           = $primary
                shadowed               = $shadowed
                ms_store_stub_detected = $msStub
            }
        } catch {
            $commandResolution[$cmd] = @{ resolved_paths = @(); primary_path = $null; shadowed = $false; ms_store_stub_detected = $false; error = $_.Exception.Message }
        }
    }
    Add-Status "command_resolution" "success"
    return $commandResolution
}

# ════════════════════════════════════════
# MODULE: STARTUP
# ════════════════════════════════════════
function Get-StartupInfo {
    $startup = @()
    try {
        $regPaths = @(
            "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
        )
        foreach ($rp in $regPaths) {
            try {
                $items = Get-ItemProperty -Path $rp -ErrorAction SilentlyContinue
                if ($items) {
                    $items.PSObject.Properties | Where-Object { $_.Name -notin @('PSPath','PSParentPath','PSChildName','PSDrive','PSProvider') } | ForEach-Object {
                        $startup += @{ name = $_.Name; command = $_.Value; location = $rp }
                    }
                }
            } catch { }
        }
        Add-Status "startup" "success"
    } catch {
        Add-Status "startup" "failed"
    }
    return $startup
}

# ════════════════════════════════════════
# MODULE: NETWORK (with timeout + port scan)
# ════════════════════════════════════════
function Get-NetworkInfo {
    try {
        $adapter = Invoke-WithTimeout -ScriptBlock {
            Get-NetIPConfiguration -ErrorAction SilentlyContinue | Where-Object { $_.IPv4DefaultGateway } | Select-Object -First 1
        }

        # Port conflict check (common dev ports)
        $portConflicts = @()
        $checkPorts = @(3000, 5000, 5678, 8000, 8080, 8443, 9090)
        foreach ($port in $checkPorts) {
            try {
                $conn = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($conn) {
                    $proc = try { (Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue).ProcessName } catch { "unknown" }
                    $portConflicts += @{ port = $port; process = $proc; pid = $conn.OwningProcess }
                }
            } catch { }
        }

        $result = @{
            hostname         = [System.Net.Dns]::GetHostName()
            local_ip         = if ($adapter) { ($adapter.IPv4Address | Select-Object -First 1).IPAddress } else { $null }
            default_gateway  = if ($adapter) { ($adapter.IPv4DefaultGateway | Select-Object -First 1).NextHop } else { $null }
            dns              = if ($adapter) { @($adapter.DNSServer | ForEach-Object { $_.ServerAddresses }) } else { @() }
            active_dev_ports = $portConflicts
        }
        Add-Status "network" "success"
        return $result
    } catch {
        Add-Status "network" "partial"
        return @{ error = $_.Exception.Message }
    }
}

# ════════════════════════════════════════
# COLLECT ALL MODULES
# ════════════════════════════════════════
$meta        = Get-SnapshotMeta
$system      = Get-SystemInfo
$hardware    = Get-HardwareInfo
$storage     = Get-StorageInfo
$python      = Get-PythonInfo
$devTools    = Get-DevToolsInfo
$services    = Get-ServiceStatus
$environment = Get-EnvironmentInfo
$cmdRes      = Get-CommandResolution
$startup     = Get-StartupInfo
$network     = Get-NetworkInfo

# ════════════════════════════════════════
# RISK FLAGS (AI's first entry point)
# ════════════════════════════════════════
$riskFlags = [System.Collections.ArrayList]::new()

# python_not_in_path
if (-not $devTools["python"].exists) {
    $riskFlags.Add(@{ id = "python_not_in_path"; severity = "high"; hint = "Python not found in PATH. Most Python-based tools will fail." }) | Out-Null
}

# multiple_python_versions
$pyPaths = $cmdRes["python"].resolved_paths
if ($pyPaths.Count -gt 1) {
    $riskFlags.Add(@{ id = "multiple_python_versions"; severity = "medium"; hint = "Found $($pyPaths.Count) python executables. pip install may target wrong version. Paths: $($pyPaths -join ', ')" }) | Out-Null
}

# pip_mismatch
if ($devTools["python"].exists -and $devTools["pip"].exists) {
    $pipDir = if ($devTools["pip"].resolved_path) { Split-Path $devTools["pip"].resolved_path } else { "" }
    $pyDir = if ($devTools["python"].resolved_path) { Split-Path $devTools["python"].resolved_path } else { "" }
    if ($pipDir -and $pyDir -and ($pipDir -ne $pyDir)) {
        $riskFlags.Add(@{ id = "pip_mismatch"; severity = "high"; hint = "pip ($pipDir) and python ($pyDir) are in different directories. Packages may install to wrong environment." }) | Out-Null
    }
}

# low_disk_space
if ($storage.disk_free_gb -and $storage.disk_free_gb -lt 10) {
    $riskFlags.Add(@{ id = "low_disk_space"; severity = "high"; hint = "System drive has only $($storage.disk_free_gb) GB free. Builds and Docker may fail." }) | Out-Null
}

# invalid_path_entries
if ($environment.invalid_path_entries -and $environment.invalid_path_entries.Count -gt 0) {
    $riskFlags.Add(@{ id = "invalid_path_entries"; severity = "medium"; hint = "$($environment.invalid_path_entries.Count) PATH entries point to non-existent directories." }) | Out-Null
}

# execution_policy_restricted
if ($system.powershell_execution_policy -in @("Restricted", "AllSigned")) {
    $riskFlags.Add(@{ id = "execution_policy_restricted"; severity = "high"; hint = "ExecutionPolicy is $($system.powershell_execution_policy). Scripts cannot run without policy change." }) | Out-Null
}

# encoding_mismatch
if ($system.active_code_page -and $system.active_code_page -ne 65001) {
    $riskFlags.Add(@{ id = "encoding_mismatch"; severity = "medium"; hint = "Active code page is $($system.active_code_page), not UTF-8 (65001). Non-ASCII characters may corrupt." }) | Out-Null
}

# shadowed_commands
$resolveCmds = @("python", "pip", "node", "git")
foreach ($cmd in $resolveCmds) {
    if ($cmdRes[$cmd].shadowed) {
        $riskFlags.Add(@{ id = "shadowed_commands"; severity = "medium"; hint = "Command '$cmd' resolves to multiple paths. First match wins but may not be intended version." }) | Out-Null
    }
}

# ms_store_stub_detected
foreach ($cmd in $resolveCmds) {
    if ($cmdRes[$cmd].ms_store_stub_detected) {
        $riskFlags.Add(@{ id = "ms_store_stub_detected"; severity = "high"; hint = "MS Store stub detected for '$cmd'. This is a fake executable that redirects to Store." }) | Out-Null
    }
}

# path_length_warning (v1.1)
if ($system.long_paths_enabled -eq $false) {
    $riskFlags.Add(@{ id = "path_length_warning"; severity = "medium"; hint = "Long paths not enabled. Deep node_modules or Python packages may fail with path >260 chars." }) | Out-Null
}

# port_conflict_risk (v1.1)
if ($network.active_dev_ports -and $network.active_dev_ports.Count -gt 0) {
    $portList = ($network.active_dev_ports | ForEach-Object { "$($_.port)($($_.process))" }) -join ', '
    $riskFlags.Add(@{ id = "port_conflict_risk"; severity = "low"; hint = "Common dev ports already in use: $portList. New services on these ports will fail to bind." }) | Out-Null
}

# pending_reboot (v1.1)
if ($system.pending_reboot -eq $true) {
    $riskFlags.Add(@{ id = "pending_reboot"; severity = "medium"; hint = "System has pending reboot. Some updates or driver changes may not take effect until restart." }) | Out-Null
}

# ════════════════════════════════════════
# ASSEMBLE JSON (v1.1 restructured)
# ════════════════════════════════════════
$snapshot = [ordered]@{
    metadata = [ordered]@{
        tool_version = $TOOL_VERSION
        timestamp    = $meta.timestamp
        hostname     = $meta.hostname
        current_user = $meta.current_user
        is_admin     = $meta.is_admin
    }
    risk_flags = @($riskFlags)
    details = [ordered]@{
        system             = $system
        hardware           = $hardware
        storage            = $storage
        python             = $python
        dev_tools          = $devTools
        services           = $services
        environment        = $environment
        command_resolution = $cmdRes
        startup            = $startup
        network            = $network
    }
    collection_status = $collectionStatus
}

$jsonPath = Join-Path $outDir "snapshot.json"
$snapshot | ConvertTo-Json -Depth 10 | Out-File -FilePath $jsonPath -Encoding utf8

# ════════════════════════════════════════
# GENERATE MARKDOWN
# ════════════════════════════════════════
$elapsed = ((Get-Date) - $startTime).TotalSeconds
$md = [System.Text.StringBuilder]::new()

[void]$md.AppendLine("# Dev Environment Snapshot")
[void]$md.AppendLine("")
[void]$md.AppendLine("**Generated:** $($meta.timestamp) | **Host:** $($meta.hostname) | **User:** $($meta.current_user) | **Admin:** $($meta.is_admin)")
[void]$md.AppendLine("**Tool Version:** $TOOL_VERSION | **Duration:** $([math]::Round($elapsed, 2))s")
[void]$md.AppendLine("")

# 1. Summary
[void]$md.AppendLine("## 1. Summary")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Module | Status |")
[void]$md.AppendLine("|--------|--------|")
foreach ($mod in @("meta","system","hardware","storage","python","dev_tools","services","environment","command_resolution","startup","network")) {
    $st = if ($mod -in $collectionStatus.success) { "OK" } elseif ($mod -in $collectionStatus.partial) { "PARTIAL" } else { "FAILED" }
    [void]$md.AppendLine("| $mod | $st |")
}
[void]$md.AppendLine("")

# 2. Risk Flags
[void]$md.AppendLine("## 2. Risk Flags")
[void]$md.AppendLine("")
if ($riskFlags.Count -eq 0) {
    [void]$md.AppendLine("No risks detected.")
} else {
    [void]$md.AppendLine("| ID | Severity | Hint |")
    [void]$md.AppendLine("|----|----------|------|")
    foreach ($rf in $riskFlags) {
        [void]$md.AppendLine("| $($rf.id) | $($rf.severity) | $($rf.hint) |")
    }
}
[void]$md.AppendLine("")

# 3. Python
[void]$md.AppendLine("## 3. Python")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Field | Value |")
[void]$md.AppendLine("|-------|-------|")
foreach ($k in @("python_version","python_path","pip_version","pip_path","is_venv","virtual_env_type","user_site_enabled")) {
    $val = $python[$k]; if ($null -eq $val) { $val = "N/A" }
    [void]$md.AppendLine("| $k | $val |")
}
[void]$md.AppendLine("")

# 4. Dev Tools
[void]$md.AppendLine("## 4. Dev Tools")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Tool | Exists | Version | Path |")
[void]$md.AppendLine("|------|--------|---------|------|")
$toolList = @("python", "pip", "node", "npm", "git", "java", "docker")
foreach ($tool in $toolList) {
    $t = $devTools[$tool]
    $e = if ($t.exists) { "Yes" } else { "No" }
    $v = if ($t.version) { $t.version } else { "-" }
    $p = if ($t.resolved_path) { $t.resolved_path } else { "-" }
    [void]$md.AppendLine("| $tool | $e | $v | $p |")
}
[void]$md.AppendLine("")

# 5. Services
[void]$md.AppendLine("## 5. Services")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Service | Exists | Status |")
[void]$md.AppendLine("|---------|--------|--------|")
foreach ($svc in @("docker", "n8n")) {
    $s = $services[$svc]
    $e = if ($s.exists) { "Yes" } else { "No" }
    $st = if ($s.status) { $s.status } else { "-" }
    [void]$md.AppendLine("| $svc | $e | $st |")
}
[void]$md.AppendLine("")

# 6. PATH / ENV
[void]$md.AppendLine("## 6. PATH / ENV")
[void]$md.AppendLine("")
if ($environment.duplicate_path_entries -and $environment.duplicate_path_entries.Count -gt 0) {
    [void]$md.AppendLine("**Duplicate PATH entries:** $($environment.duplicate_path_entries -join ', ')")
    [void]$md.AppendLine("")
}
if ($environment.invalid_path_entries -and $environment.invalid_path_entries.Count -gt 0) {
    [void]$md.AppendLine("**Invalid PATH entries ($($environment.invalid_path_entries.Count)):**")
    foreach ($ip in $environment.invalid_path_entries) { [void]$md.AppendLine("- ``$ip``") }
    [void]$md.AppendLine("")
}
[void]$md.AppendLine("**Key Environment Variables:**")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Variable | Value |")
[void]$md.AppendLine("|----------|-------|")
foreach ($k in @("PYTHONPATH","JAVA_HOME","NODE_PATH","VIRTUAL_ENV")) {
    $val = $environment.key_env[$k]; if (-not $val) { $val = "(not set)" }
    [void]$md.AppendLine("| $k | $val |")
}
[void]$md.AppendLine("")

# 7. System / Hardware / Storage
[void]$md.AppendLine("## 7. System / Hardware / Storage")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Field | Value |")
[void]$md.AppendLine("|-------|-------|")
[void]$md.AppendLine("| OS | $($system.os_name) $($system.os_build) |")
[void]$md.AppendLine("| Build | $($system.os_version) |")
[void]$md.AppendLine("| Arch | $($system.architecture) |")
[void]$md.AppendLine("| Timezone | $($system.timezone) |")
[void]$md.AppendLine("| PowerShell | $($system.powershell_version) |")
[void]$md.AppendLine("| ExecutionPolicy | $($system.powershell_execution_policy) |")
[void]$md.AppendLine("| CodePage | $($system.active_code_page) |")
[void]$md.AppendLine("| LongPaths | $($system.long_paths_enabled) |")
[void]$md.AppendLine("| PendingReboot | $($system.pending_reboot) |")
[void]$md.AppendLine("| CPU | $($hardware.cpu_model) |")
[void]$md.AppendLine("| Cores | $($hardware.physical_cores)P / $($hardware.logical_cores)L |")
[void]$md.AppendLine("| RAM | $($hardware.total_ram_gb) GB (free: $($hardware.available_ram_gb) GB) |")
[void]$md.AppendLine("| System Drive | $($storage.system_drive) |")
[void]$md.AppendLine("| Disk | $($storage.disk_total_gb) GB (free: $($storage.disk_free_gb) GB) |")
[void]$md.AppendLine("| Filesystem | $($storage.filesystem) |")
[void]$md.AppendLine("| SSD | $($storage.is_ssd) |")
[void]$md.AppendLine("")

# 8. Port Activity
if ($network.active_dev_ports -and $network.active_dev_ports.Count -gt 0) {
    [void]$md.AppendLine("## 8. Active Dev Ports")
    [void]$md.AppendLine("")
    [void]$md.AppendLine("| Port | Process | PID |")
    [void]$md.AppendLine("|------|---------|-----|")
    foreach ($p in $network.active_dev_ports) {
        [void]$md.AppendLine("| $($p.port) | $($p.process) | $($p.pid) |")
    }
    [void]$md.AppendLine("")
}

# 9. JSON path
[void]$md.AppendLine("## 9. Full JSON")
[void]$md.AppendLine("")
[void]$md.AppendLine("``$jsonPath``")

$mdPath = Join-Path $outDir "snapshot.md"
$md.ToString() | Out-File -FilePath $mdPath -Encoding utf8

# ── Done ──
Write-Host ""
Write-Host "Snapshot complete in $([math]::Round($elapsed, 2))s" -ForegroundColor Green
Write-Host "  JSON: $jsonPath"
Write-Host "  MD:   $mdPath"
if ($riskFlags.Count -gt 0) {
    Write-Host "  Risk flags: $($riskFlags.Count)" -ForegroundColor Yellow
} else {
    Write-Host "  No risk flags detected." -ForegroundColor Green
}
