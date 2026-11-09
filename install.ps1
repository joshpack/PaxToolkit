<#
    pxd Toolkit from PowerShell:  irm pxdtek.com/tk|iex   (also irm pxdtek.com/i|iex)

    Shows the pxd apps that have a PowerShell edition, whether each is installed on this PC, and runs them straight
    from pxdtek.com. It is temporary: it installs nothing, starts nothing in the tray and writes nothing to the PC.
    The tools it runs live in memory for as long as they run.

    To install the pxd Toolkit app itself (download, SHA-256 check, %USERPROFILE%\Apps\pxdToolkit, Start menu
    shortcut, started in the tray; its window opens only if you say so):
      & ([scriptblock]::Create((irm pxdtek.com/tk))) -Install
    or download it from https://github.com/joshpack/pxdToolkit/releases. Other apps install from the Toolkit's window.

    -Install options (environment variables):
      $env:PXDTK_HOME     = 'D:\Tools\pxdToolkit'   install somewhere else
      $env:PXDTK_NOSTART  = '1'                      install without starting it
      $env:PXDTK_NOSHORTCUT = '1'                    no Start menu shortcut

    Windows PowerShell 5.1 and PowerShell 7. ASCII only, so irm | iex never trips.
#>
param([switch]$Install)

& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is many times slower with the progress bar
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

    $managerVersion = '1.2.0'
    $installCmd = '& ([scriptblock]::Create((irm pxdtek.com/tk))) -Install'

    function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

    # ---- PD header ---------------------------------------------------------------------------------------------------
    # The PD logo beside the tool's name, shown once when a pxdtek CLI tool starts. The master copy is cli/PdHeader.ps1
    # in the pxdtek repo; each tool carries a copy (they run through irm | iex, so they can't load it). Keep them in sync.
    # ASCII-only on purpose, so irm | iex never trips on encoding: the solid-block logo is stored as letters
    # (U = upper half block, D = lower half block, F = full block) and turned into blocks when it's shown.
    # Each run picks one of the two logos at random; a window too narrow for the logo gets a one-line header.
    function Show-PdHeader {
        param([string]$Name, [string]$Version = '', [string[]]$About = @(), [string]$Command = '')
        $ascii = @(
            '      -++++++++++-'
            '   :++-          -++:'
            '  ++        +#-     ++'
            ' #-         -+@%-    -#'
            '++    =##*%#%=-=##.   ++'
            '%    +%. .@:+%   *#    %'
            '%    #*   %+:@. .%+    %'
            '++   .##=-=%#%*##=    ++'
            ' #-    -%@+-         -#'
            '  ++     -#+        ++'
            '   :++-          -++:'
            '      -++++++++++-'
        )
        $blocks = @(
            '      DDDUUUUUUDDD'
            '    DUU          UUD'
            '  DU        UFD     UD'
            ' FU         DDFFD    UF'
            'DU    DFUUFFFU UUF    UD'
            'F    DF   F UF   FF    F'
            'F    FF   FD F   FU    F'
            'UD    FDD DFFFDDFU    DU'
            ' FD    UFFUU         DF'
            '  UD     UFD        DU'
            '    UDD          DDU'
            '      UUUDDDDDDUUU'
        )
        $text = New-Object System.Collections.Generic.List[object]
        $text.Add(@($Name, 'White'))
        $text.Add(@(((@($Version, 'pxdtek.com') | Where-Object { $_ }) -join '  -  '), 'DarkGray'))
        if ($About) { $text.Add(@('', 'Gray')); foreach ($a in $About) { $text.Add(@($a, 'Gray')) } }
        if ($Command) { $text.Add(@('', 'Gray')); $text.Add(@($Command, 'DarkCyan')) }
        $textWidth = ($text | ForEach-Object { $_[0].Length } | Measure-Object -Maximum).Maximum

        $w = 0
        try { $w = $Host.UI.RawUI.WindowSize.Width } catch {}
        Write-Host ''
        if ($w -gt 0 -and $w -lt 30 + $textWidth) {
            Write-Host '  (pd) ' -NoNewline -ForegroundColor Cyan
            Write-Host $Name -NoNewline -ForegroundColor White
            Write-Host $(if ($Version) { "  $Version" } else { '' }) -ForegroundColor DarkGray
            Write-Host ''
            return
        }

        if ($null -eq (Get-Variable -Name PdHeaderStyle -Scope Script -ValueOnly -ErrorAction SilentlyContinue)) { $script:PdHeaderStyle = Get-Random -Minimum 0 -Maximum 2 }
        $useBlocks = $script:PdHeaderStyle -eq 1
        $top = [Math]::Max(0, [Math]::Floor(($ascii.Count - $text.Count) / 2))
        for ($i = 0; $i -lt $ascii.Count; $i++) {
            Write-Host '  ' -NoNewline
            if ($useBlocks) {
                $l = $blocks[$i].Replace('U', [string][char]0x2580).Replace('D', [string][char]0x2584).Replace('F', [string][char]0x2588)
                Write-Host $l.PadRight(24) -NoNewline -ForegroundColor Cyan
            } else {
                # dense strokes white, the middle cyan, the edges dark cyan: one colour run at a time
                $run = ''; $col = ''
                foreach ($c in $ascii[$i].PadRight(24).ToCharArray()) {
                    $k = if ('@%#'.Contains([string]$c)) { 'White' } elseif ('*+='.Contains([string]$c)) { 'Cyan' } else { 'DarkCyan' }
                    if ($k -ne $col -and $run) { Write-Host $run -NoNewline -ForegroundColor $col; $run = '' }
                    $col = $k; $run += $c
                }
                if ($run) { Write-Host $run -NoNewline -ForegroundColor $col }
            }
            $j = $i - $top
            if ($j -ge 0 -and $j -lt $text.Count -and $text[$j][0]) { Write-Host ('    ' + $text[$j][0]) -NoNewline -ForegroundColor $text[$j][1] }
            Write-Host ''
        }
        Write-Host ''
    }
    # ---- end of PD header --------------------------------------------------------------------------------------------

    # ---- -Install: the pxd Toolkit app itself -------------------------------------------------------------------------
    if ($Install) {
        Show-PdHeader -Name 'pxd Toolkit' -Version 'installer' -Command $installCmd
        if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitOperatingSystem) { Say '  pxd Toolkit needs 64-bit Windows 10 or 11.' 'Red'; return }
        $home2 = if ($env:PXDTK_HOME) { $env:PXDTK_HOME } else { Join-Path $env:USERPROFILE 'Apps\pxdToolkit' }
        $exe = Join-Path $home2 'pxdToolkit.exe'
        $headers = @{ 'User-Agent' = 'pxdToolkit-installer'; 'Accept' = 'application/vnd.github+json' }
    
        Say '  Finding the latest release...'
        try {
            $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/joshpack/pxdToolkit/releases/latest' -Headers $headers
        } catch {
            Say "  Can't reach GitHub: $($_.Exception.Message)" 'Red'
            return
        }
    
        $asset = $rel.assets | Where-Object { $_.name -match '^pxdToolkit-.*-win-x64\.zip$' } | Select-Object -First 1
        if (-not $asset) { Say "  The $($rel.tag_name) release has no Windows download." 'Red'; return }
        $sum = $rel.assets | Where-Object { $_.name -eq ($asset.name + '.sha256') } | Select-Object -First 1
        $version = $rel.tag_name.TrimStart('v')
    
        if (Test-Path -LiteralPath $exe) {
            $have = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
            if ($have) { $have = ($have -split '\+')[0] }
            if ($have -eq $version) { Say "  pxd Toolkit $version is already installed; reinstalling it." 'DarkGray' }
            else { Say "  Updating pxd Toolkit $have to $version." }
        } else {
            Say "  Installing pxd Toolkit $version ($([math]::Round($asset.size / 1MB)) MB)."
        }
    
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ('pxdtoolkit-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tmp -Force | Out-Null
        try {
            $zip = Join-Path $tmp $asset.name
            Say '  Downloading...'
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers @{ 'User-Agent' = 'pxdToolkit-installer' }
    
            if ($sum) {
                $expected = ([string](Invoke-RestMethod -Uri $sum.browser_download_url -Headers @{ 'User-Agent' = 'pxdToolkit-installer' })).Trim().Split(' ')[0].ToLowerInvariant()
                $sha = [Security.Cryptography.SHA256]::Create()
                $fs = [IO.File]::OpenRead($zip)
                try { $actual = -join ($sha.ComputeHash($fs) | ForEach-Object { $_.ToString('x2') }) } finally { $fs.Dispose(); $sha.Dispose() }
                if ($expected -ne $actual) { Say '  The download is damaged (checksum does not match). Nothing was changed; try again.' 'Red'; return }
                Say '  Checksum OK.' 'DarkGray'
            }
    
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $unz = Join-Path $tmp 'x'
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unz)
            $fresh = Get-ChildItem -LiteralPath $unz -Recurse -Filter 'pxdToolkit.exe' | Select-Object -First 1
            if (-not $fresh) { Say '  The download has no pxdToolkit.exe in it.' 'Red'; return }
    
            # A running copy holds the exe: ask it to close, then make sure.
            $running = @(Get-Process -Name pxdToolkit -ErrorAction SilentlyContinue | Where-Object {
                try { $_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq [IO.Path]::GetFullPath($exe)) } catch { $false } })
            if ($running.Count -gt 0) {
                Say '  Closing the running pxd Toolkit...' 'DarkGray'
                foreach ($p in $running) { try { $p.Kill(); $p.WaitForExit(5000) | Out-Null } catch { } }
            }
    
            New-Item -ItemType Directory -Path $home2 -Force | Out-Null
            $done = $false
            for ($i = 0; $i -lt 10 -and -not $done; $i++) {
                try { Copy-Item -LiteralPath $fresh.FullName -Destination $exe -Force; $done = $true } catch { Start-Sleep -Milliseconds 400 }
            }
            if (-not $done) { Say "  Couldn't replace $exe (still in use). Close pxd Toolkit and run this again." 'Red'; return }
            Say "  Installed to $home2" 'Green'
    
            if ($env:PXDTK_NOSHORTCUT -ne '1') {
                try {
                    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'pxd Toolkit'
                    New-Item -ItemType Directory -Path $menu -Force | Out-Null
                    $ws = New-Object -ComObject WScript.Shell
                    $lnk = $ws.CreateShortcut((Join-Path $menu 'pxd Toolkit.lnk'))
                    $lnk.TargetPath = $exe
                    $lnk.WorkingDirectory = $home2
                    $lnk.Description = 'Install, update and open your pxd apps'
                    $lnk.Save()
                    Say '  Start menu shortcut: Start > pxd Toolkit' 'DarkGray'
                } catch {
                    Say "  (Couldn't add the Start menu shortcut: $($_.Exception.Message))" 'DarkYellow'
                }
            }
        } finally {
            Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
        }
    
        # Started in the tray only (as at sign-in): the window opens only when asked for.
        if ($env:PXDTK_NOSTART -ne '1') {
            Start-Process -FilePath $exe -ArgumentList '--minimized' -WorkingDirectory $home2
            Say '  pxd Toolkit is running in the tray.' 'Green'
            $open = ''
            try { $open = (Read-Host '  Open its window now? (y/N)').Trim().ToLower() } catch { }
            if ($open -eq 'y' -or $open -eq 'yes') {
                Start-Sleep -Milliseconds 800
                Start-Process -FilePath $exe -ArgumentList '--show' -WorkingDirectory $home2
            }
        }
        Write-Host ''
        return
    }


    # ---- system info beside the logo ---------------------------------------------------------------------------------
    # Read-only: CIM, the network adapters and pxdtek.com/ip (the address this PC is seen from). Nothing is saved.
    function Get-SysLines {
        $L = New-Object System.Collections.Generic.List[object]
        function Add([string]$k, [string]$v) { if ($v) { $L.Add(@(($k.PadRight(10) + $v), 'Gray')) } }
        $os = $cs = $cpu = $null
        try { $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop } catch { }
        try { $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop } catch { }
        try { $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1 } catch { }
        Add 'PC' $env:COMPUTERNAME
        Add 'User' $env:USERNAME
        if ($os) {
            $build = $os.BuildNumber
            try { $ubr = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop); if ($ubr.DisplayVersion) { $build = "$($ubr.DisplayVersion), build $($os.BuildNumber).$($ubr.UBR)" } } catch { }
            Add 'Windows' (($os.Caption -replace '^Microsoft\s+', '') + " ($build)")
            $up = (Get-Date) - $os.LastBootUpTime
            Add 'Uptime' $(if ($up.TotalDays -ge 1) { '{0} d {1} h' -f [int][math]::Floor($up.TotalDays), $up.Hours } else { '{0} h {1} min' -f $up.Hours, $up.Minutes })
        }
        if ($cpu) { Add 'CPU' (($cpu.Name -replace '\s+', ' ').Trim() + " ($($cpu.NumberOfCores) cores)") }
        if ($cs -and $os) { Add 'Memory' ('{0:0} GB ({1:0.#} GB free)' -f ($cs.TotalPhysicalMemory / 1GB), ($os.FreePhysicalMemory * 1KB / 1GB)) }
        try {
            $gpu = @(Get-CimInstance Win32_VideoController -ErrorAction Stop | Where-Object { $_.Name -notmatch 'Basic Display|Remote|Virtual|Parsec|Meta' } | ForEach-Object { $_.Name.Trim() })
            if ($gpu) { Add 'GPU' ($gpu -join ', ') }
        } catch { }
        try {
            $d = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($env:SystemDrive)'" -ErrorAction Stop
            Add "Disk $($env:SystemDrive)" ('{0:0} GB free of {1:0} GB' -f ($d.FreeSpace / 1GB), ($d.Size / 1GB))
        } catch { }
        try {
            $lan = @(Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = TRUE' -ErrorAction Stop |
                Where-Object { $_.DefaultIPGateway } | ForEach-Object { $_.IPAddress | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } })
            if ($lan) { Add 'Local IP' ($lan -join ', ') }
        } catch { }
        $wan = ''
        try { $wan = ([string](Invoke-RestMethod -Uri 'https://pxdtek.com/ip' -UserAgent 'pxdToolkit-cli' -TimeoutSec 5)).Trim() } catch { }
        if ($wan -and $wan -ne 'unknown') { Add 'WAN IP' $wan }
        $L
    }

    # The info on the left, the PD logo on the right (the logo is the same ASCII art as Show-PdHeader's; keep them in step).
    function Show-SysHeader([string]$Version) {
        $logo = @(
            '      -++++++++++-'
            '   :++-          -++:'
            '  ++        +#-     ++'
            ' #-         -+@%-    -#'
            '++    =##*%#%=-=##.   ++'
            '%    +%. .@:+%   *#    %'
            '%    #*   %+:@. .%+    %'
            '++   .##=-=%#%*##=    ++'
            ' #-    -%@+-         -#'
            '  ++     -#+        ++'
            '   :++-          -++:'
            '      -++++++++++-'
        )
        $text = New-Object System.Collections.Generic.List[object]
        $text.Add(@('pxd Toolkit', 'White'))
        $text.Add(@("$Version  -  pxdtek.com", 'DarkGray'))
        $text.Add(@('', 'Gray'))
        foreach ($l in (Get-SysLines)) { $text.Add($l) }
        $leftWidth = [Math]::Max(40, ($text | ForEach-Object { $_[0].Length } | Measure-Object -Maximum).Maximum + 4)

        $w = 0
        try { $w = $Host.UI.RawUI.WindowSize.Width } catch { }
        Write-Host ''
        $showLogo = -not ($w -gt 0 -and $w -lt 2 + $leftWidth + 24)
        $rows = [Math]::Max($text.Count, $(if ($showLogo) { $logo.Count } else { 0 }))
        $top = if ($showLogo) { [Math]::Max(0, [Math]::Floor(($logo.Count - $text.Count) / 2)) } else { 0 }
        $logoTop = if ($showLogo) { [Math]::Max(0, [Math]::Floor(($text.Count - $logo.Count) / 2)) } else { 0 }
        for ($i = 0; $i -lt $rows; $i++) {
            $j = $i - $top
            $line = if ($j -ge 0 -and $j -lt $text.Count) { $text[$j] } else { @('', 'Gray') }
            Write-Host ('  ' + $line[0].PadRight($leftWidth)) -NoNewline -ForegroundColor $line[1]
            $k = $i - $logoTop
            if ($showLogo -and $k -ge 0 -and $k -lt $logo.Count) {
                # dense strokes white, the middle cyan, the edges dark cyan: one colour run at a time
                $run = ''; $col = ''
                foreach ($c in $logo[$k].ToCharArray()) {
                    $kind = if ('@%#'.Contains([string]$c)) { 'White' } elseif ('*+='.Contains([string]$c)) { 'Cyan' } else { 'DarkCyan' }
                    if ($kind -ne $col -and $run) { Write-Host $run -NoNewline -ForegroundColor $col; $run = '' }
                    $col = $kind; $run += $c
                }
                if ($run) { Write-Host $run -NoNewline -ForegroundColor $col }
            }
            Write-Host ''
        }
        Write-Host ''
        Say '  irm pxdtek.com/tk|iex' 'DarkCyan'
    }

    # ---- the temporary list (default) --------------------------------------------------------------------------------
    # Reads only: the Toolkit's app list from GitHub, what's in the Apps folder, and each app's latest release.
    function Get-AppsRoot {
        try {
            $s = Join-Path $env:LOCALAPPDATA 'pxdToolkit\settings.json'
            if (Test-Path -LiteralPath $s) {
                $j = Get-Content -LiteralPath $s -Raw | ConvertFrom-Json
                if ($j.effectiveAppsRoot) { return [string]$j.effectiveAppsRoot }
            }
        } catch { }
        Join-Path $env:USERPROFILE 'Apps'
    }

    function Find-Installed($App, [string]$Root) {
        $dir = Join-Path $Root $App.folder
        $direct = Join-Path $dir $App.exe
        if (Test-Path -LiteralPath $direct) { return $direct }
        if (Test-Path -LiteralPath $dir) {
            $hit = Get-ChildItem -LiteralPath $dir -Recurse -Depth 2 -Filter $App.exe -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
        $null
    }

    function Get-LocalVersion([string]$Path) {
        try {
            if ($Path -like '*.ps1') {
                foreach ($line in (Get-Content -LiteralPath $Path -TotalCount 400)) {
                    if ($line -match "Version\s*=\s*['""](\d+\.\d+(\.\d+)?)['""]") { return $Matches[1] }
                }
                return ''
            }
            $v = (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
            if ($v) { return ($v -split '\+')[0] }
        } catch { }
        ''
    }

    function Test-Newer([string]$Latest, [string]$Have) {
        try { return ([version]$Latest) -gt ([version]$Have) } catch { return $false }
    }

    # The code after pxdtek.com/ in a one-liner: "irm pxdtek.com/ss | iex" -> "ss".
    function Get-Code([string]$Command) {
        ($Command -replace '^\s*irm\s+(https?://)?pxdtek\.com/?', '') -replace '\s*\|\s*iex\s*$', ''
    }

    function Show-List {
        Say '  Checking this PC...' 'DarkGray'
        # PXDTK_CATALOG: a local catalog.json, for testing.
        try {
            $catalog = if ($env:PXDTK_CATALOG) { Get-Content -LiteralPath $env:PXDTK_CATALOG -Raw | ConvertFrom-Json }
                       else { Invoke-RestMethod -Uri 'https://raw.githubusercontent.com/joshpack/pxdToolkit/main/catalog.json' -Headers @{ 'User-Agent' = 'pxdToolkit-cli' } -TimeoutSec 20 }
        } catch { Say "  Can't reach GitHub: $($_.Exception.Message)" 'Red'; return $null }
        $root = Get-AppsRoot
        $apps = @([pscustomobject]@{ id = 'pxdToolkit'; name = 'pxd Toolkit'; repo = 'joshpack/pxdToolkit'; exe = 'pxdToolkit.exe'; folder = 'pxdToolkit'; installCommand = ''; tagline = 'The app that installs, updates and opens every pxd app' })
        $apps += @($catalog.apps | Where-Object { $_.installCommand })
        $rows = @()
        foreach ($a in $apps) {
            $path = Find-Installed $a $root
            $have = if ($path) { Get-LocalVersion $path } else { '' }
            $latest = ''
            try { $latest = (Invoke-RestMethod -Uri "https://api.github.com/repos/$($a.repo)/releases/latest" -Headers $headers -TimeoutSec 20).tag_name.TrimStart('v') } catch { }
            $rows += [pscustomobject]@{ App = $a; Path = $path; Have = $have; Latest = $latest; Code = $(if ($a.installCommand) { Get-Code $a.installCommand } else { '' }) }
        }

        Write-Host ''
        for ($i = 0; $i -lt $rows.Count; $i++) {
            $r = $rows[$i]
            Write-Host ('   [' + ($i + 1) + '] ') -NoNewline -ForegroundColor Cyan
            Write-Host ($r.App.name.PadRight(22)) -NoNewline -ForegroundColor White
            $status = if (-not $r.Path) { 'not installed' } elseif ($r.Have) { "installed v$($r.Have)" } else { 'installed' }
            $color = if (-not $r.Path) { 'DarkGray' } else { 'Green' }
            if ($r.Path -and $r.Latest -and $r.Have -and (Test-Newer $r.Latest $r.Have)) { $status += ", v$($r.Latest) out"; $color = 'Yellow' }
            Write-Host $status.PadRight(28) -NoNewline -ForegroundColor $color
            if ($r.Code) { Write-Host "irm pxdtek.com/$($r.Code)|iex" -ForegroundColor DarkCyan }
            else { Write-Host 'the app (see below to install it)' -ForegroundColor DarkGray }
        }
        Write-Host ''
        Say '  Pick a tool to run it here; nothing is installed or left behind.' 'DarkGray'
        Say "  To install the pxd Toolkit app: $installCmd" 'DarkGray'
        Say '  (or download it from github.com/joshpack/pxdToolkit/releases; other apps install from its window)' 'DarkGray'
        Write-Host ''
        $rows
    }

    # Runs a tool's script in memory, the same thing its one-liner does; this list comes back when it ends.
    function Invoke-Tool($Row) {
        Say "  Starting $($Row.App.name)..." 'DarkGray'
        $code = Invoke-RestMethod -Uri ('https://pxdtek.com/' + $Row.Code) -Headers @{ 'User-Agent' = 'WindowsPowerShell pxdToolkit-cli' } -TimeoutSec 30
        & ([scriptblock]::Create([string]$code))
    }

    $headers = @{ 'User-Agent' = 'pxdToolkit-cli'; 'Accept' = 'application/vnd.github+json' }
    $script:PdHeaderStyle = 0   # the ASCII logo
    Show-SysHeader $managerVersion
    while ($true) {
        $rows = @(Show-List)
        if (-not $rows -or -not $rows[0]) { break }
        $pick = ''
        try { $pick = (Read-Host '  Number to run (Enter to quit)').Trim() } catch { break }
        if (-not $pick) { break }
        $n = 0
        if (-not [int]::TryParse($pick, [ref]$n) -or $n -lt 1 -or $n -gt $rows.Count) { Say "  There's no [$pick]." 'Yellow'; continue }
        $r = $rows[$n - 1]
        if (-not $r.Code) {
            Say "  $($r.App.name) is an app, not a PowerShell tool. To install it: $installCmd" 'Yellow'
            continue
        }
        # The tool runs in memory and is told so: it saves nothing on this PC (PXD_INMEMORY lives only in this process).
        $before = $env:PXD_INMEMORY
        $env:PXD_INMEMORY = '1'
        try { Invoke-Tool $r } catch { Say "  $($r.App.name) stopped: $($_.Exception.Message)" 'Red' }
        finally { $env:PXD_INMEMORY = $before }
        Write-Host ''
    }
    Write-Host ''
}
