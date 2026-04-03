#Requires -Version 5.1
<#
.SYNOPSIS
    Windows Development Environment Snapshot Tool v1.2.0
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

$TOOL_VERSION = "1.2.0"
$startTime = Get-Date

# ── Self-unblock (avoid digital signature prompts) ──
try { Unblock-File -Path $PSCommandPath -ErrorAction SilentlyContinue } catch { }

# ── ExecutionPolicy check ──
$currentPolicy = try { (Get-ExecutionPolicy -Scope CurrentUser).ToString() } catch { "Unknown" }
if ($currentPolicy -in @("Restricted", "AllSigned", "Undefined")) {
    $machinePolicy = try { (Get-ExecutionPolicy -Scope LocalMachine).ToString() } catch { "Unknown" }
    if ($machinePolicy -in @("Restricted", "AllSigned", "Undefined")) {
        Write-Host ""
        Write-Host "[!] ExecutionPolicy may block scripts. If you see errors, run:" -ForegroundColor Yellow
        Write-Host "    Set-ExecutionPolicy -Scope CurrentUser RemoteSigned" -ForegroundColor Cyan
        Write-Host ""
    }
}

# ── Output folder ──
$timestamp = $startTime.ToString("yyyy-MM-dd_HH-mm-ss")
$hostName = $env:COMPUTERNAME ?? "UNKNOWN"
$outDir = Join-Path $PSScriptRoot "snapshots" "${timestamp}_${hostName}"
if (Test-Path $outDir) { $outDir = "${outDir}_$(Get-Random -Maximum 9999)" }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# ── Tracking ──
$collectionStatus = @{ success = [System.Collections.ArrayList]::new(); partial = [System.Collections.ArrayList]::new(); failed = [System.Collections.ArrayList]::new() }
function Add-Status($module, $status) { $collectionStatus[$status].Add($module) | Out-Null }

# ── Security: Scrub sensitive env values ──
function Test-SensitiveKey([string]$keyName) {
    foreach ($p in @('TOKEN','KEY','SECRET','PASSWORD','PASSWD','CREDENTIAL','API_KEY','APIKEY')) {
        if ($keyName -match $p) { return $true }
    }
    return $false
}
function Get-ScrubbedValue([string]$keyName, [string]$value) {
    if (Test-SensitiveKey $keyName) { return "***SCRUBBED***" }
    return $value
}

# ── Null-safe accessor ──
function Get-SafeValue($obj, [string]$prop, $default = $null) {
    if ($null -eq $obj) { return $default }
    if ($obj -is [hashtable]) { if ($obj.ContainsKey($prop)) { return $obj[$prop] }; return $default }
    try { $v = $obj.$prop; if ($null -eq $v) { return $default }; return $v } catch { return $default }
}

# ════════════════════════════════════════════════════════════════
# MODULES (each returns guaranteed schema on failure)
# ════════════════════════════════════════════════════════════════

function Get-SnapshotMeta {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        Add-Status "meta" "success"
        return @{ tool_version = $TOOL_VERSION; timestamp = $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); hostname = $hostName; current_user = $env:USERNAME; is_admin = $isAdmin }
    } catch {
        Add-Status "meta" "partial"
        return @{ tool_version = $TOOL_VERSION; timestamp = $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); hostname = $hostName; current_user = $env:USERNAME; is_admin = $null }
    }
}

function Get-SystemInfo {
    try {
        $os = Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 3
        $execPolicy = try { (Get-ExecutionPolicy).ToString() } catch { "unknown" }
        $codePage = try { $o = & chcp 2>$null; if ($o -match '(\d+)') { [int]$Matches[1] } else { $null } } catch { $null }
        $longPaths = try { $v = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -EA Stop; [bool]$v.LongPathsEnabled } catch { $null }
        $pendingReboot = $false
        try {
            if (Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") { $pendingReboot = $true }
            if (Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") { $pendingReboot = $true }
            $pfu = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager" -Name "PendingFileRenameOperations" -EA SilentlyContinue
            if ($pfu -and $pfu.PendingFileRenameOperations) { $pendingReboot = $true }
        } catch { }
        Add-Status "system" "success"
        return @{ os_name = $os.Caption; os_version = $os.Version; os_build = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -EA SilentlyContinue).DisplayVersion ?? "unknown"; architecture = $env:PROCESSOR_ARCHITECTURE; timezone = (Get-TimeZone).Id; powershell_version = $PSVersionTable.PSVersion.ToString(); powershell_execution_policy = $execPolicy; active_code_page = $codePage; long_paths_enabled = $longPaths; pending_reboot = $pendingReboot }
    } catch { Add-Status "system" "failed"; return @{ error = $_.Exception.Message } }
}

function Get-HardwareInfo {
    try {
        $cpu = Get-CimInstance Win32_Processor -OperationTimeoutSec 3 | Select-Object -First 1
        $cs = Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 3 | Select-Object -First 1
        $osI = Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 3
        Add-Status "hardware" "success"
        return @{ cpu_model = $cpu.Name.Trim(); physical_cores = $cpu.NumberOfCores; logical_cores = $cpu.NumberOfLogicalProcessors; total_ram_gb = [math]::Round($cs.TotalPhysicalMemory/1GB,1); available_ram_gb = [math]::Round($osI.FreePhysicalMemory/1MB,1) }
    } catch { Add-Status "hardware" "partial"; return @{ cpu_model = $null; physical_cores = $null; logical_cores = $null; total_ram_gb = $null; available_ram_gb = $null; error = $_.Exception.Message } }
}

function Get-StorageInfo {
    $r = @{ system_drive = $null; disk_total_gb = $null; disk_free_gb = $null; filesystem = $null; is_ssd = $null }
    try {
        $dl = $env:SystemDrive[0]; $r.system_drive = $env:SystemDrive
        $vol = Get-Volume -DriveLetter $dl -EA Stop
        $r.disk_total_gb = [math]::Round($vol.Size/1GB,1); $r.disk_free_gb = [math]::Round($vol.SizeRemaining/1GB,1); $r.filesystem = $vol.FileSystemType
        try { $p = Get-Partition -DriveLetter $dl -EA Stop; $d = $p | Get-Disk -EA Stop; if ($d -and ($d | Get-Member -Name 'MediaType' -EA SilentlyContinue)) { $r.is_ssd = ($d.MediaType -eq 'SSD') } } catch { $r.is_ssd = $null }
        Add-Status "storage" "success"
    } catch { $r["error"] = $_.Exception.Message; Add-Status "storage" "partial" }
    return $r
}

function Get-PythonInfo {
    try {
        $pyCmd = Get-Command python -EA SilentlyContinue
        if (-not $pyCmd) { Add-Status "python" "partial"; return @{ python_version = "not found"; python_path = $null; pip_version = $null; pip_path = $null; is_venv = $null; virtual_env_type = "none"; sys_executable = $null; sys_path = @(); site_packages_path = @(); user_site_enabled = $null } }
        $pyPath = $pyCmd.Source; $pyVer = try { & python --version 2>&1 | ForEach-Object { $_ -replace 'Python\s*','' } } catch { "unknown" }
        $pipVersion = $null; $pipPath = $null
        try { $pipRaw = & pip --version 2>&1 | Select-Object -First 1; if ($pipRaw -match '^pip\s+([\d.]+)\s+from\s+(.+?)\s') { $pipVersion = $Matches[1]; $pipPath = $Matches[2] } } catch { }
        $pyInfo = try { $raw = & python -c "import sys,json,site; print(json.dumps({'executable':sys.executable,'sys_path':sys.path,'prefix':sys.prefix,'base_prefix':sys.base_prefix,'site_packages':[p for p in site.getsitepackages()],'user_site_enabled':site.ENABLE_USER_SITE}))" 2>&1; $raw | ConvertFrom-Json } catch { $null }
        $isVenv = if ($pyInfo) { $pyInfo.prefix -ne $pyInfo.base_prefix } else { $null }
        $venvType = if ($env:VIRTUAL_ENV) { "venv" } elseif ($env:CONDA_DEFAULT_ENV) { "conda" } elseif ($isVenv) { "venv" } else { "none" }
        Add-Status "python" "success"
        return @{ python_version = $pyVer; python_path = $pyPath; pip_version = $pipVersion; pip_path = $pipPath; sys_executable = if($pyInfo){$pyInfo.executable}else{$null}; sys_path = if($pyInfo){$pyInfo.sys_path}else{@()}; site_packages_path = if($pyInfo){$pyInfo.site_packages}else{@()}; is_venv = $isVenv; virtual_env_type = $venvType; user_site_enabled = if($pyInfo){$pyInfo.user_site_enabled}else{$null} }
    } catch { Add-Status "python" "failed"; return @{ python_version = $null; python_path = $null; pip_version = $null; pip_path = $null; is_venv = $null; virtual_env_type = "none"; error = $_.Exception.Message } }
}

function Get-DevToolsInfo {
    $dt = @{}
    foreach ($tool in @("python","pip","node","npm","git","java","docker")) {
        try {
            $cmds = @(Get-Command $tool -All -EA SilentlyContinue)
            if ($cmds.Count -gt 0) {
                $ver = try { switch ($tool) { "java" { $raw = & java -version 2>&1; ($raw|Select-Object -First 1) -replace '.*"(.+)".*','$1' }; default { $raw = & $tool --version 2>&1; ($raw|Select-Object -First 1).ToString().Trim() } } } catch { "unknown" }
                $dt[$tool] = @{ exists = $true; version = $ver; resolved_path = $cmds[0].Source; all_resolved_paths = @($cmds|ForEach-Object{$_.Source}) }
            } else { $dt[$tool] = @{ exists = $false; version = $null; resolved_path = $null; all_resolved_paths = @() } }
        } catch { $dt[$tool] = @{ exists = $false; version = $null; resolved_path = $null; all_resolved_paths = @() } }
    }
    Add-Status "dev_tools" "success"; return $dt
}

function Get-ServiceStatus {
    $svc = @{}
    try { $d = Get-Service -Name "com.docker.service" -EA SilentlyContinue; if(-not $d){$d = Get-Service -Name "docker" -EA SilentlyContinue}; $svc["docker"] = if($d){@{exists=$true;status=$d.Status.ToString()}}else{@{exists=$false;status=$null}} } catch { $svc["docker"] = @{exists=$false;status=$null} }
    try { $n = Get-Service -Name "n8n" -EA SilentlyContinue; $np = try{[bool](Get-NetTCPConnection -LocalPort 5678 -EA SilentlyContinue|Select-Object -First 1)}catch{$false}; $svc["n8n"] = if($n){@{exists=$true;status=$n.Status.ToString();port_5678_active=$np}}else{@{exists=$false;status=$null;port_5678_active=$np}} } catch { $svc["n8n"] = @{exists=$false;status=$null;port_5678_active=$false} }
    Add-Status "services" "success"; return $svc
}

function Get-EnvironmentInfo {
    try {
        $pathRaw = $env:PATH; $pe = @($pathRaw -split ';'|Where-Object{$_ -ne ''})
        $dup = @($pe|Group-Object|Where-Object{$_.Count -gt 1}|ForEach-Object{$_.Name})
        $inv = @($pe|Where-Object{-not(Test-Path $_ -EA SilentlyContinue)})
        $ke = @{}; foreach($k in @("PATH","PYTHONPATH","JAVA_HOME","NODE_PATH","VIRTUAL_ENV")){$v=[System.Environment]::GetEnvironmentVariable($k);$ke[$k]=Get-ScrubbedValue $k $v}
        Add-Status "environment" "success"
        return @{ path_raw=$pathRaw; path_entries=$pe; path_entry_count=$pe.Count; duplicate_path_entries=$dup; invalid_path_entries=$inv; key_env=$ke }
    } catch { Add-Status "environment" "failed"; return @{ path_raw=$null; path_entries=@(); path_entry_count=0; duplicate_path_entries=@(); invalid_path_entries=@(); key_env=@(); error=$_.Exception.Message } }
}

function Get-CommandResolution {
    $cr = @{}
    foreach ($cmd in @("python","pip","node","git")) {
        try {
            $all = @(); $all += @(Get-Command $cmd -All -EA SilentlyContinue|ForEach-Object{$_.Source}); $all += try{@(& where.exe $cmd 2>$null)}catch{@()}; $all = @($all|Select-Object -Unique)
            $msStub = $false; foreach($p in $all){ if($p -match 'WindowsApps'){$fi=Get-Item $p -EA SilentlyContinue; if($fi -and $fi.Length -lt 1024){$msStub=$true;break}} }
            $cr[$cmd] = @{ resolved_paths=$all; primary_path=if($all.Count -gt 0){$all[0]}else{$null}; shadowed=($all.Count -gt 1); ms_store_stub_detected=$msStub }
        } catch { $cr[$cmd] = @{ resolved_paths=@(); primary_path=$null; shadowed=$false; ms_store_stub_detected=$false } }
    }
    Add-Status "command_resolution" "success"; return $cr
}

function Get-StartupInfo {
    $su = @()
    try {
        foreach ($rp in @("HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run","HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run")) {
            try { $items = Get-ItemProperty -Path $rp -EA SilentlyContinue; if($items){ $items.PSObject.Properties|Where-Object{$_.Name -notin @('PSPath','PSParentPath','PSChildName','PSDrive','PSProvider')}|ForEach-Object{ $su += @{name=$_.Name;command=$_.Value;location=$rp} } } } catch { }
        }
        Add-Status "startup" "success"
    } catch { Add-Status "startup" "failed" }
    return $su
}

function Get-NetworkInfo {
    $r = @{ hostname=$null; local_ip=$null; default_gateway=$null; dns=@(); active_dev_ports=@() }
    try {
        $r.hostname = [System.Net.Dns]::GetHostName()
        $ad = try { Get-NetIPConfiguration -EA SilentlyContinue|Where-Object{$_.IPv4DefaultGateway}|Select-Object -First 1 } catch { $null }
        if ($ad) { $r.local_ip=($ad.IPv4Address|Select-Object -First 1).IPAddress; $r.default_gateway=($ad.IPv4DefaultGateway|Select-Object -First 1).NextHop; $r.dns=@($ad.DNSServer|ForEach-Object{$_.ServerAddresses}) }
        $pc = @(); foreach($port in @(3000,5000,5678,8000,8080,8443,9090)){ try{$c=Get-NetTCPConnection -LocalPort $port -EA SilentlyContinue|Select-Object -First 1; if($c){$pn=try{(Get-Process -Id $c.OwningProcess -EA SilentlyContinue).ProcessName}catch{"unknown"}; $pc+=@{port=$port;process=$pn;pid=$c.OwningProcess}}}catch{} }
        $r.active_dev_ports = $pc
        Add-Status "network" "success"
    } catch { $r["error"]=$_.Exception.Message; Add-Status "network" "partial" }
    return $r
}

# ════════════════════════════════════════
# COLLECT ALL MODULES (with progress)
# ════════════════════════════════════════
Write-Host ""
Write-Host "=== Env Snapshot v$TOOL_VERSION ===" -ForegroundColor Cyan
Write-Host ""

$modules = @(
    @{n=" 1/11"; label="meta";               fn={Get-SnapshotMeta}},
    @{n=" 2/11"; label="system";             fn={Get-SystemInfo}},
    @{n=" 3/11"; label="hardware";           fn={Get-HardwareInfo}},
    @{n=" 4/11"; label="storage";            fn={Get-StorageInfo}},
    @{n=" 5/11"; label="python";             fn={Get-PythonInfo}},
    @{n=" 6/11"; label="dev tools";          fn={Get-DevToolsInfo}},
    @{n=" 7/11"; label="services";           fn={Get-ServiceStatus}},
    @{n=" 8/11"; label="environment";        fn={Get-EnvironmentInfo}},
    @{n=" 9/11"; label="command resolution"; fn={Get-CommandResolution}},
    @{n="10/11"; label="startup";            fn={Get-StartupInfo}},
    @{n="11/11"; label="network";            fn={Get-NetworkInfo}}
)

$results = @{}
foreach ($m in $modules) {
    Write-Host "[$($m.n)] Collecting $($m.label)..." -NoNewline
    $ms = Get-Date
    $results[$m.label] = & $m.fn
    $dur = [math]::Round(((Get-Date)-$ms).TotalSeconds,1)
    Write-Host " OK (${dur}s)" -ForegroundColor Green
}

$meta=$results["meta"]; $system=$results["system"]; $hardware=$results["hardware"]; $storage=$results["storage"]
$python=$results["python"]; $devTools=$results["dev tools"]; $services=$results["services"]; $environment=$results["environment"]
$cmdRes=$results["command resolution"]; $startup=$results["startup"]; $network=$results["network"]

Write-Host ""

# ════════════════════════════════════════
# RISK FLAGS
# ════════════════════════════════════════
Write-Host "Analyzing risk flags..." -NoNewline
$riskFlags = [System.Collections.ArrayList]::new()
$resolveCmds = @("python","pip","node","git")

if (-not (Get-SafeValue $devTools["python"] "exists" $false)) { $riskFlags.Add(@{id="python_not_in_path";severity="high";hint="Python not found in PATH."}) | Out-Null }

$pyPaths = Get-SafeValue $cmdRes["python"] "resolved_paths" @()
if ($pyPaths.Count -gt 1) { $riskFlags.Add(@{id="multiple_python_versions";severity="medium";hint="Found $($pyPaths.Count) python executables: $($pyPaths -join ', ')"}) | Out-Null }

# pip_mismatch (fixed: Scripts\ subdirectory is normal)
$pyR = Get-SafeValue $devTools["python"] "resolved_path"; $pipR = Get-SafeValue $devTools["pip"] "resolved_path"
if ($pyR -and $pipR) { $pyB = Split-Path $pyR; $pipB = Split-Path $pipR; $pipGP = Split-Path $pipB; if ($pipB -ne $pyB -and $pipGP -ne $pyB) { $riskFlags.Add(@{id="pip_mismatch";severity="high";hint="pip ($pipB) and python ($pyB) appear from different installations."}) | Out-Null } }

$df = Get-SafeValue $storage "disk_free_gb"; if ($null -ne $df -and $df -lt 10) { $riskFlags.Add(@{id="low_disk_space";severity="high";hint="System drive has only $df GB free."}) | Out-Null }
$ie = Get-SafeValue $environment "invalid_path_entries" @(); if ($ie.Count -gt 0) { $riskFlags.Add(@{id="invalid_path_entries";severity="medium";hint="$($ie.Count) PATH entries point to non-existent dirs."}) | Out-Null }
$ep = Get-SafeValue $system "powershell_execution_policy"; if ($ep -in @("Restricted","AllSigned")) { $riskFlags.Add(@{id="execution_policy_restricted";severity="high";hint="ExecutionPolicy is $ep."}) | Out-Null }
$cp = Get-SafeValue $system "active_code_page"; if ($null -ne $cp -and $cp -ne 65001) { $riskFlags.Add(@{id="encoding_mismatch";severity="medium";hint="Code page is $cp, not UTF-8 (65001)."}) | Out-Null }
foreach ($cmd in $resolveCmds) { if (Get-SafeValue $cmdRes[$cmd] "shadowed" $false) { $riskFlags.Add(@{id="shadowed_commands";severity="medium";hint="'$cmd' resolves to multiple paths."}) | Out-Null } }
foreach ($cmd in $resolveCmds) { if (Get-SafeValue $cmdRes[$cmd] "ms_store_stub_detected" $false) { $riskFlags.Add(@{id="ms_store_stub_detected";severity="high";hint="MS Store stub for '$cmd'."}) | Out-Null } }
if ((Get-SafeValue $system "long_paths_enabled") -eq $false) { $riskFlags.Add(@{id="path_length_warning";severity="medium";hint="Long paths not enabled."}) | Out-Null }
$ap = Get-SafeValue $network "active_dev_ports" @(); if ($ap.Count -gt 0) { $pl=($ap|ForEach-Object{"$($_.port)($($_.process))"})-join', '; $riskFlags.Add(@{id="port_conflict_risk";severity="low";hint="Dev ports in use: $pl"}) | Out-Null }
if ((Get-SafeValue $system "pending_reboot") -eq $true) { $riskFlags.Add(@{id="pending_reboot";severity="medium";hint="System has pending reboot."}) | Out-Null }

Write-Host " $($riskFlags.Count) flags" -ForegroundColor $(if($riskFlags.Count -gt 0){"Yellow"}else{"Green"})

# ════════════════════════════════════════
# ASSEMBLE JSON + MARKDOWN
# ════════════════════════════════════════
Write-Host "Generating output..." -NoNewline

$snapshot = [ordered]@{
    metadata = [ordered]@{ tool_version=$TOOL_VERSION; timestamp=Get-SafeValue $meta "timestamp"; hostname=Get-SafeValue $meta "hostname"; current_user=Get-SafeValue $meta "current_user"; is_admin=Get-SafeValue $meta "is_admin" }
    risk_flags = @($riskFlags)
    details = [ordered]@{ system=$system; hardware=$hardware; storage=$storage; python=$python; dev_tools=$devTools; services=$services; environment=$environment; command_resolution=$cmdRes; startup=$startup; network=$network }
    collection_status = $collectionStatus
}
$jsonPath = Join-Path $outDir "snapshot.json"
$snapshot | ConvertTo-Json -Depth 10 | Out-File -FilePath $jsonPath -Encoding utf8

# ── Markdown ──
$elapsed = ((Get-Date)-$startTime).TotalSeconds
$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("# Dev Environment Snapshot")
[void]$md.AppendLine("")
[void]$md.AppendLine("**Generated:** $(Get-SafeValue $meta 'timestamp') | **Host:** $(Get-SafeValue $meta 'hostname') | **User:** $(Get-SafeValue $meta 'current_user') | **Admin:** $(Get-SafeValue $meta 'is_admin')")
[void]$md.AppendLine("**Tool Version:** $TOOL_VERSION | **Duration:** $([math]::Round($elapsed,2))s")
[void]$md.AppendLine("")
[void]$md.AppendLine("## 1. Summary"); [void]$md.AppendLine("")
[void]$md.AppendLine("| Module | Status |"); [void]$md.AppendLine("|--------|--------|")
foreach ($mod in @("meta","system","hardware","storage","python","dev_tools","services","environment","command_resolution","startup","network")) {
    $st = if($mod -in $collectionStatus.success){"OK"}elseif($mod -in $collectionStatus.partial){"PARTIAL"}else{"FAILED"}
    [void]$md.AppendLine("| $mod | $st |")
}
[void]$md.AppendLine("")
[void]$md.AppendLine("## 2. Risk Flags"); [void]$md.AppendLine("")
if ($riskFlags.Count -eq 0) { [void]$md.AppendLine("No risks detected.") } else {
    [void]$md.AppendLine("| ID | Severity | Hint |"); [void]$md.AppendLine("|----|----------|------|")
    foreach ($rf in $riskFlags) { [void]$md.AppendLine("| $($rf.id) | $($rf.severity) | $($rf.hint) |") }
}
[void]$md.AppendLine("")
[void]$md.AppendLine("## 3. Python"); [void]$md.AppendLine("")
[void]$md.AppendLine("| Field | Value |"); [void]$md.AppendLine("|-------|-------|")
foreach ($k in @("python_version","python_path","pip_version","pip_path","is_venv","virtual_env_type","user_site_enabled")) { [void]$md.AppendLine("| $k | $(Get-SafeValue $python $k 'N/A') |") }
[void]$md.AppendLine("")
[void]$md.AppendLine("## 4. Dev Tools"); [void]$md.AppendLine("")
[void]$md.AppendLine("| Tool | Exists | Version | Path |"); [void]$md.AppendLine("|------|--------|---------|------|")
foreach ($tool in @("python","pip","node","npm","git","java","docker")) { $t=$devTools[$tool]; [void]$md.AppendLine("| $tool | $(if(Get-SafeValue $t 'exists' $false){'Yes'}else{'No'}) | $(Get-SafeValue $t 'version' '-') | $(Get-SafeValue $t 'resolved_path' '-') |") }
[void]$md.AppendLine("")
[void]$md.AppendLine("## 5. Services"); [void]$md.AppendLine("")
[void]$md.AppendLine("| Service | Exists | Status |"); [void]$md.AppendLine("|---------|--------|--------|")
foreach ($svc in @("docker","n8n")) { $s=$services[$svc]; [void]$md.AppendLine("| $svc | $(if(Get-SafeValue $s 'exists' $false){'Yes'}else{'No'}) | $(Get-SafeValue $s 'status' '-') |") }
[void]$md.AppendLine("")
[void]$md.AppendLine("## 6. PATH / ENV"); [void]$md.AppendLine("")
$dupE = Get-SafeValue $environment "duplicate_path_entries" @(); if($dupE.Count -gt 0){[void]$md.AppendLine("**Duplicate PATH:** $($dupE -join ', ')"); [void]$md.AppendLine("")}
$invE = Get-SafeValue $environment "invalid_path_entries" @(); if($invE.Count -gt 0){[void]$md.AppendLine("**Invalid PATH ($($invE.Count)):**"); foreach($ip in $invE){[void]$md.AppendLine("- ``$ip``")}; [void]$md.AppendLine("")}
[void]$md.AppendLine("**Key Environment Variables:**"); [void]$md.AppendLine("")
[void]$md.AppendLine("| Variable | Value |"); [void]$md.AppendLine("|----------|-------|")
$keD = Get-SafeValue $environment "key_env" @{}
foreach ($k in @("PYTHONPATH","JAVA_HOME","NODE_PATH","VIRTUAL_ENV")) { $v = if($keD -is [hashtable] -and $keD.ContainsKey($k) -and $keD[$k]){$keD[$k]}else{"(not set)"}; [void]$md.AppendLine("| $k | $v |") }
[void]$md.AppendLine("")
[void]$md.AppendLine("## 7. System / Hardware / Storage"); [void]$md.AppendLine("")
[void]$md.AppendLine("| Field | Value |"); [void]$md.AppendLine("|-------|-------|")
[void]$md.AppendLine("| OS | $(Get-SafeValue $system 'os_name' 'N/A') $(Get-SafeValue $system 'os_build' '') |")
[void]$md.AppendLine("| Build | $(Get-SafeValue $system 'os_version' 'N/A') |")
[void]$md.AppendLine("| Arch | $(Get-SafeValue $system 'architecture' 'N/A') |")
[void]$md.AppendLine("| Timezone | $(Get-SafeValue $system 'timezone' 'N/A') |")
[void]$md.AppendLine("| PowerShell | $(Get-SafeValue $system 'powershell_version' 'N/A') |")
[void]$md.AppendLine("| ExecutionPolicy | $(Get-SafeValue $system 'powershell_execution_policy' 'N/A') |")
[void]$md.AppendLine("| CodePage | $(Get-SafeValue $system 'active_code_page' 'N/A') |")
[void]$md.AppendLine("| LongPaths | $(Get-SafeValue $system 'long_paths_enabled' 'N/A') |")
[void]$md.AppendLine("| PendingReboot | $(Get-SafeValue $system 'pending_reboot' 'N/A') |")
[void]$md.AppendLine("| CPU | $(Get-SafeValue $hardware 'cpu_model' 'N/A') |")
[void]$md.AppendLine("| Cores | $(Get-SafeValue $hardware 'physical_cores' 'N/A')P / $(Get-SafeValue $hardware 'logical_cores' 'N/A')L |")
[void]$md.AppendLine("| RAM | $(Get-SafeValue $hardware 'total_ram_gb' 'N/A') GB (free: $(Get-SafeValue $hardware 'available_ram_gb' 'N/A') GB) |")
[void]$md.AppendLine("| System Drive | $(Get-SafeValue $storage 'system_drive' 'N/A') |")
[void]$md.AppendLine("| Disk | $(Get-SafeValue $storage 'disk_total_gb' 'N/A') GB (free: $(Get-SafeValue $storage 'disk_free_gb' 'N/A') GB) |")
[void]$md.AppendLine("| Filesystem | $(Get-SafeValue $storage 'filesystem' 'N/A') |")
[void]$md.AppendLine("| SSD | $(Get-SafeValue $storage 'is_ssd' 'N/A') |")
[void]$md.AppendLine("")
$portAct = Get-SafeValue $network "active_dev_ports" @()
if ($portAct.Count -gt 0) { [void]$md.AppendLine("## 8. Active Dev Ports"); [void]$md.AppendLine(""); [void]$md.AppendLine("| Port | Process | PID |"); [void]$md.AppendLine("|------|---------|-----|"); foreach($p in $portAct){[void]$md.AppendLine("| $($p.port) | $($p.process) | $($p.pid) |")}; [void]$md.AppendLine("") }
[void]$md.AppendLine("## 9. Full JSON"); [void]$md.AppendLine(""); [void]$md.AppendLine("``$jsonPath``")

$mdPath = Join-Path $outDir "snapshot.md"
$md.ToString() | Out-File -FilePath $mdPath -Encoding utf8
Write-Host " done" -ForegroundColor Green

# ── Done ──
$elapsed = ((Get-Date)-$startTime).TotalSeconds
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Snapshot complete in $([math]::Round($elapsed,2))s" -ForegroundColor Green
Write-Host "  JSON: $jsonPath"
Write-Host "  MD:   $mdPath"
Write-Host "  Risk flags: $($riskFlags.Count)" -ForegroundColor $(if($riskFlags.Count -gt 0){"Yellow"}else{"Green"})
Write-Host "========================================" -ForegroundColor Cyan
