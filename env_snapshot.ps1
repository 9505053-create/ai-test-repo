#Requires -Version 5.1
<#
.SYNOPSIS
    Windows Development Environment Snapshot Tool v0.2.0
.DESCRIPTION
    Produces an AI-readable snapshot of the development environment.
    Output: snapshot.json + snapshot.md in snapshots/<timestamp>_<hostname>/
.NOTES
    No admin required (best-effort for some fields).
    Will not abort on partial failures.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$TOOL_VERSION = "0.2.0"
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

# ════════════════════════════════════════
# 1. META
# ════════════════════════════════════════
$meta = @{}
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    $meta = @{
        tool_version  = $TOOL_VERSION
        generated_at  = $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        computer_name = $hostName
        current_user  = $env:USERNAME
        is_admin      = $isAdmin
    }
    Add-Status "meta" "success"
} catch {
    $meta = @{ tool_version = $TOOL_VERSION; generated_at = $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); error = $_.Exception.Message }
    Add-Status "meta" "partial"
}

# ════════════════════════════════════════
# 2. SYSTEM
# ════════════════════════════════════════
$system = @{}
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $psVer = $PSVersionTable.PSVersion.ToString()

    # ExecutionPolicy
    $execPolicy = try { (Get-ExecutionPolicy).ToString() } catch { "unknown" }

    # Code page
    $codePage = try {
        $chcpOut = & chcp 2>$null
        if ($chcpOut -match '(\d+)') { [int]$Matches[1] } else { $null }
    } catch { $null }

    # Long paths
    $longPaths = try {
        $val = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -ErrorAction Stop
        [bool]$val.LongPathsEnabled
    } catch { $null }

    $system = @{
        os_name                    = $os.Caption
        os_version                 = $os.Version
        os_build                   = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction SilentlyContinue).DisplayVersion ?? "unknown"
        architecture               = $env:PROCESSOR_ARCHITECTURE
        timezone                   = (Get-TimeZone).Id
        powershell_version         = $psVer
        powershell_execution_policy = $execPolicy
        active_code_page           = $codePage
        long_paths_enabled         = $longPaths
    }
    Add-Status "system" "success"
} catch {
    $system = @{ error = $_.Exception.Message }
    Add-Status "system" "failed"
}

# ════════════════════════════════════════
# 3. HARDWARE
# ════════════════════════════════════════
$hardware = @{}
try {
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $cs = Get-CimInstance Win32_ComputerSystem

    $hardware = @{
        cpu_model      = $cpu.Name.Trim()
        physical_cores = $cpu.NumberOfCores
        logical_cores  = $cpu.NumberOfLogicalProcessors
        total_ram_gb   = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
        available_ram_gb = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
    }
    Add-Status "hardware" "success"
} catch {
    $hardware = @{ error = $_.Exception.Message }
    Add-Status "hardware" "failed"
}

# ════════════════════════════════════════
# 4. STORAGE
# ════════════════════════════════════════
$storage = @{}
try {
    $sysDrive = $env:SystemDrive + "\"
    $vol = Get-Volume -DriveLetter $env:SystemDrive[0] -ErrorAction Stop
    $partition = Get-Partition -DriveLetter $env:SystemDrive[0] -ErrorAction SilentlyContinue
    $physDisk = if ($partition) {
        try { $partition | Get-Disk -ErrorAction SilentlyContinue } catch { $null }
    } else { $null }
    $isSsd = if ($physDisk) { $physDisk.MediaType -eq 'SSD' } else { $null }

    $storage = @{
        system_drive  = $env:SystemDrive
        disk_total_gb = [math]::Round($vol.Size / 1GB, 1)
        disk_free_gb  = [math]::Round($vol.SizeRemaining / 1GB, 1)
        filesystem    = $vol.FileSystemType
        is_ssd        = $isSsd
    }
    Add-Status "storage" "success"
} catch {
    $storage = @{ error = $_.Exception.Message }
    Add-Status "storage" "partial"
}

# ════════════════════════════════════════
# 5. PYTHON
# ════════════════════════════════════════
$python = @{}
try {
    $pyCmd = Get-Command python -ErrorAction SilentlyContinue
    if ($pyCmd) {
        $pyPath = $pyCmd.Source
        $pyVer = try { & python --version 2>&1 | ForEach-Object { $_ -replace 'Python\s*', '' } } catch { "unknown" }

        # pip
        $pipVer = try { & pip --version 2>&1 | ForEach-Object { if ($_ -match '^pip\s+([\d.]+)\s+from\s+(.+?)\s') { @{ version = $Matches[1]; path = $Matches[2] } } else { @{ version = "unknown"; path = "unknown" } } } } catch { @{ version = "not found"; path = $null } }

        # Python internals via one-liner
        $pyInfo = try {
            $raw = & python -c "import sys,json,site; print(json.dumps({'executable':sys.executable,'sys_path':sys.path,'prefix':sys.prefix,'base_prefix':sys.base_prefix,'site_packages':[p for p in site.getsitepackages()],'user_site_enabled':site.ENABLE_USER_SITE}))" 2>&1
            $raw | ConvertFrom-Json
        } catch { $null }

        $isVenv = if ($pyInfo) { $pyInfo.prefix -ne $pyInfo.base_prefix } else { $null }
        $venvType = if ($env:VIRTUAL_ENV) { "venv" } elseif ($env:CONDA_DEFAULT_ENV) { "conda" } elseif ($isVenv) { "venv" } else { "none" }

        $python = @{
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
    } else {
        $python = @{ python_version = "not found"; python_path = $null }
        Add-Status "python" "partial"
    }
} catch {
    $python = @{ error = $_.Exception.Message }
    Add-Status "python" "failed"
}

# ════════════════════════════════════════
# 6. DEV_TOOLS
# ════════════════════════════════════════
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

# ════════════════════════════════════════
# 7. ENVIRONMENT
# ════════════════════════════════════════
$environment = @{}
try {
    $pathRaw = $env:PATH
    $pathEntries = @($pathRaw -split ';' | Where-Object { $_ -ne '' })
    $duplicates = @($pathEntries | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    $invalid = @($pathEntries | Where-Object { -not (Test-Path $_ -ErrorAction SilentlyContinue) })

    $keyEnvNames = @("PATH", "PYTHONPATH", "JAVA_HOME", "NODE_PATH", "VIRTUAL_ENV")
    $keyEnv = @{}
    foreach ($k in $keyEnvNames) {
        $keyEnv[$k] = [System.Environment]::GetEnvironmentVariable($k)
    }

    $environment = @{
        path_raw              = $pathRaw
        path_entries          = $pathEntries
        duplicate_path_entries = $duplicates
        invalid_path_entries  = $invalid
        key_env               = $keyEnv
    }
    Add-Status "environment" "success"
} catch {
    $environment = @{ error = $_.Exception.Message }
    Add-Status "environment" "failed"
}

# ════════════════════════════════════════
# 8. COMMAND_RESOLUTION
# ════════════════════════════════════════
$commandResolution = @{}
$resolveCmds = @("python", "pip", "node", "git")

foreach ($cmd in $resolveCmds) {
    try {
        $allPaths = @()
        # Get-Command
        $gcPaths = @(Get-Command $cmd -All -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
        $allPaths += $gcPaths

        # where.exe
        $wherePaths = try { @(& where.exe $cmd 2>$null) } catch { @() }
        $allPaths += $wherePaths
        $allPaths = @($allPaths | Select-Object -Unique)

        $primary = if ($allPaths.Count -gt 0) { $allPaths[0] } else { $null }
        $shadowed = $allPaths.Count -gt 1

        # MS Store stub detection
        $msStub = $false
        foreach ($p in $allPaths) {
            if ($p -match 'WindowsApps') {
                $fileInfo = Get-Item $p -ErrorAction SilentlyContinue
                if ($fileInfo -and $fileInfo.Length -lt 1024) {
                    $msStub = $true
                    break
                }
            }
        }

        $commandResolution[$cmd] = @{
            resolved_paths          = $allPaths
            primary_path            = $primary
            shadowed                = $shadowed
            ms_store_stub_detected  = $msStub
        }
    } catch {
        $commandResolution[$cmd] = @{ resolved_paths = @(); primary_path = $null; shadowed = $false; ms_store_stub_detected = $false; error = $_.Exception.Message }
    }
}
Add-Status "command_resolution" "success"

# ════════════════════════════════════════
# 9. STARTUP
# ════════════════════════════════════════
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
                    $startup += @{
                        name     = $_.Name
                        command  = $_.Value
                        location = $rp
                    }
                }
            }
        } catch { }
    }
    Add-Status "startup" "success"
} catch {
    Add-Status "startup" "failed"
}

# ════════════════════════════════════════
# 10. NETWORK
# ════════════════════════════════════════
$network = @{}
try {
    $adapters = Get-NetIPConfiguration -ErrorAction SilentlyContinue | Where-Object { $_.IPv4DefaultGateway }
    $adapter = $adapters | Select-Object -First 1

    $network = @{
        hostname        = [System.Net.Dns]::GetHostName()
        local_ip        = if ($adapter) { ($adapter.IPv4Address | Select-Object -First 1).IPAddress } else { $null }
        default_gateway = if ($adapter) { ($adapter.IPv4DefaultGateway | Select-Object -First 1).NextHop } else { $null }
        dns             = if ($adapter) { @($adapter.DNSServer | ForEach-Object { $_.ServerAddresses }) } else { @() }
    }
    Add-Status "network" "success"
} catch {
    $network = @{ error = $_.Exception.Message }
    Add-Status "network" "partial"
}

# ════════════════════════════════════════
# RISK FLAGS
# ════════════════════════════════════════
$riskFlags = [System.Collections.ArrayList]::new()

# python_not_in_path
if (-not $devTools["python"].exists) {
    $riskFlags.Add(@{ id = "python_not_in_path"; severity = "high"; detail = "Python not found in PATH" }) | Out-Null
}

# multiple_python_versions
$pyPaths = $commandResolution["python"].resolved_paths
if ($pyPaths.Count -gt 1) {
    $riskFlags.Add(@{ id = "multiple_python_versions"; severity = "medium"; detail = "Found $($pyPaths.Count) python executables: $($pyPaths -join ', ')" }) | Out-Null
}

# pip_mismatch
if ($devTools["python"].exists -and $devTools["pip"].exists) {
    $pipResolvedDir = if ($devTools["pip"].resolved_path) { Split-Path $devTools["pip"].resolved_path } else { "" }
    $pyResolvedDir = if ($devTools["python"].resolved_path) { Split-Path $devTools["python"].resolved_path } else { "" }
    if ($pipResolvedDir -and $pyResolvedDir -and ($pipResolvedDir -ne $pyResolvedDir)) {
        $riskFlags.Add(@{ id = "pip_mismatch"; severity = "high"; detail = "pip ($pipResolvedDir) and python ($pyResolvedDir) are in different directories" }) | Out-Null
    }
}

# low_disk_space
if ($storage.disk_free_gb -and $storage.disk_free_gb -lt 10) {
    $riskFlags.Add(@{ id = "low_disk_space"; severity = "high"; detail = "System drive has only $($storage.disk_free_gb) GB free" }) | Out-Null
}

# invalid_path_entries
if ($environment.invalid_path_entries -and $environment.invalid_path_entries.Count -gt 0) {
    $riskFlags.Add(@{ id = "invalid_path_entries"; severity = "medium"; detail = "$($environment.invalid_path_entries.Count) invalid PATH entries found" }) | Out-Null
}

# execution_policy_restricted
if ($system.powershell_execution_policy -in @("Restricted", "AllSigned")) {
    $riskFlags.Add(@{ id = "execution_policy_restricted"; severity = "high"; detail = "ExecutionPolicy is $($system.powershell_execution_policy)" }) | Out-Null
}

# encoding_mismatch
if ($system.active_code_page -and $system.active_code_page -ne 65001) {
    $riskFlags.Add(@{ id = "encoding_mismatch"; severity = "medium"; detail = "Active code page is $($system.active_code_page), not UTF-8 (65001)" }) | Out-Null
}

# shadowed_commands
foreach ($cmd in $resolveCmds) {
    if ($commandResolution[$cmd].shadowed) {
        $riskFlags.Add(@{ id = "shadowed_commands"; severity = "medium"; detail = "Command '$cmd' has multiple resolved paths (shadowed)" }) | Out-Null
    }
}

# ms_store_stub_detected
foreach ($cmd in $resolveCmds) {
    if ($commandResolution[$cmd].ms_store_stub_detected) {
        $riskFlags.Add(@{ id = "ms_store_stub_detected"; severity = "high"; detail = "MS Store stub detected for '$cmd'" }) | Out-Null
    }
}

# ════════════════════════════════════════
# ASSEMBLE & OUTPUT
# ════════════════════════════════════════
$snapshot = [ordered]@{
    meta               = $meta
    system             = $system
    hardware           = $hardware
    storage            = $storage
    python             = $python
    dev_tools          = $devTools
    environment        = $environment
    command_resolution = $commandResolution
    startup            = $startup
    network            = $network
    risk_flags         = @($riskFlags)
    collection_status  = $collectionStatus
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
[void]$md.AppendLine("**Generated:** $($meta.generated_at) | **Host:** $($meta.computer_name) | **User:** $($meta.current_user) | **Admin:** $($meta.is_admin)")
[void]$md.AppendLine("**Tool Version:** $TOOL_VERSION | **Duration:** $([math]::Round($elapsed, 2))s")
[void]$md.AppendLine("")

# Summary
[void]$md.AppendLine("## 1. Summary")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Category | Status |")
[void]$md.AppendLine("|----------|--------|")
foreach ($mod in @("meta","system","hardware","storage","python","dev_tools","environment","command_resolution","startup","network")) {
    $st = if ($mod -in $collectionStatus.success) { "OK" } elseif ($mod -in $collectionStatus.partial) { "PARTIAL" } else { "FAILED" }
    [void]$md.AppendLine("| $mod | $st |")
}
[void]$md.AppendLine("")

# Risk Flags
[void]$md.AppendLine("## 2. Risk Flags")
[void]$md.AppendLine("")
if ($riskFlags.Count -eq 0) {
    [void]$md.AppendLine("No risks detected.")
} else {
    [void]$md.AppendLine("| ID | Severity | Detail |")
    [void]$md.AppendLine("|----|----------|--------|")
    foreach ($rf in $riskFlags) {
        [void]$md.AppendLine("| $($rf.id) | $($rf.severity) | $($rf.detail) |")
    }
}
[void]$md.AppendLine("")

# Python
[void]$md.AppendLine("## 3. Python")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Field | Value |")
[void]$md.AppendLine("|-------|-------|")
foreach ($k in @("python_version","python_path","pip_version","pip_path","is_venv","virtual_env_type","user_site_enabled")) {
    $val = $python[$k]
    if ($null -eq $val) { $val = "N/A" }
    [void]$md.AppendLine("| $k | $val |")
}
[void]$md.AppendLine("")

# Dev Tools
[void]$md.AppendLine("## 4. Dev Tools")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Tool | Exists | Version | Path |")
[void]$md.AppendLine("|------|--------|---------|------|")
foreach ($tool in $toolList) {
    $t = $devTools[$tool]
    $e = if ($t.exists) { "Yes" } else { "No" }
    $v = if ($t.version) { $t.version } else { "-" }
    $p = if ($t.resolved_path) { $t.resolved_path } else { "-" }
    [void]$md.AppendLine("| $tool | $e | $v | $p |")
}
[void]$md.AppendLine("")

# PATH / ENV
[void]$md.AppendLine("## 5. PATH / ENV")
[void]$md.AppendLine("")
if ($environment.duplicate_path_entries -and $environment.duplicate_path_entries.Count -gt 0) {
    [void]$md.AppendLine("**Duplicate PATH entries:** $($environment.duplicate_path_entries -join ', ')")
    [void]$md.AppendLine("")
}
if ($environment.invalid_path_entries -and $environment.invalid_path_entries.Count -gt 0) {
    [void]$md.AppendLine("**Invalid PATH entries ($($environment.invalid_path_entries.Count)):**")
    foreach ($ip in $environment.invalid_path_entries) {
        [void]$md.AppendLine("- ``$ip``")
    }
    [void]$md.AppendLine("")
}
[void]$md.AppendLine("**Key Environment Variables:**")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Variable | Value |")
[void]$md.AppendLine("|----------|-------|")
foreach ($k in @("PYTHONPATH","JAVA_HOME","NODE_PATH","VIRTUAL_ENV")) {
    $val = $environment.key_env[$k]
    if (-not $val) { $val = "(not set)" }
    [void]$md.AppendLine("| $k | $val |")
}
[void]$md.AppendLine("")

# System / Hardware / Storage
[void]$md.AppendLine("## 6. System / Hardware / Storage")
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
[void]$md.AppendLine("| CPU | $($hardware.cpu_model) |")
[void]$md.AppendLine("| Cores | $($hardware.physical_cores)P / $($hardware.logical_cores)L |")
[void]$md.AppendLine("| RAM | $($hardware.total_ram_gb) GB (free: $($hardware.available_ram_gb) GB) |")
[void]$md.AppendLine("| System Drive | $($storage.system_drive) |")
[void]$md.AppendLine("| Disk | $($storage.disk_total_gb) GB (free: $($storage.disk_free_gb) GB) |")
[void]$md.AppendLine("| Filesystem | $($storage.filesystem) |")
[void]$md.AppendLine("| SSD | $($storage.is_ssd) |")
[void]$md.AppendLine("")

# JSON path
[void]$md.AppendLine("## 7. Full JSON")
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
