<#
    pxd Toolkit from PowerShell:  irm pxdtek.com/tk|iex   (also irm pxdtek.com/i|iex)

    Shows the pxd apps that have a PowerShell edition, whether each is installed on this PC, and runs them straight
    from pxdtek.com. It is temporary: it installs nothing, starts nothing in the tray and writes nothing to the PC.
    The tools it runs live in memory for as long as they run.

    To install the pxd Toolkit app itself (download, SHA-256 check, %USERPROFILE%\Apps\pxdToolkit, Start menu
    shortcut, started in the tray; its window opens only if you say so):
      irm pxdtek.com/tk/install|iex
    (the same as: & ([scriptblock]::Create((irm pxdtek.com/tk))) -Install)
    or download it from https://github.com/pxdtek/pxdToolkit/releases. Other apps install from the Toolkit's window.

    On a network that blocks pxdtek.com, run it from GitHub instead; everything then comes from GitHub:
      irm https://raw.githubusercontent.com/pxdtek/pxdToolkit/main/install.ps1 | iex

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
    $installCmd = 'irm pxdtek.com/tk/install|iex'

    function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

    # ---- PD header ---------------------------------------------------------------------------------------------------
    # The pxd logo beside the tool's name, shown when a pxdtek CLI tool starts. The master copy is cli/PdHeader.ps1 in the
    # pxdtek repo; each tool carries a copy (they run through irm | iex, so they can't load it). tools/sync-header.ps1
    # copies it into them.
    # Ten drawings of the logo, from tools/pdlogos.mjs: solid blocks, braille dots, shades and plain ASCII, tilted like the
    # logo or upright, the pxd in white and the ring in cyan. They're stored as gzip + base64 so this stays ASCII and
    # irm | iex never trips on encoding. Each time, one is picked at random from those that fit the window (braille only
    # in Windows Terminal or VS Code, whose fonts have the dots); a window too narrow for any gets a one-line header.
    # Nothing is kept between runs: under irm | iex the script scope is the caller's own session.

    # A random logo no wider than $Room (@{ Style; Width; Text = lines; Color = lines }), or $null if none fits.
    function Get-PdLogo([int]$Room = [int]::MaxValue) {
        $logos = @(
            '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'
        ) -join ''
        $all = @()
        try {
            $gz = New-Object IO.Compression.GZipStream((New-Object IO.MemoryStream(, [Convert]::FromBase64String($logos))), ([IO.Compression.CompressionMode]::Decompress))
            $body = (New-Object IO.StreamReader($gz, [Text.Encoding]::UTF8)).ReadToEnd()
            foreach ($chunk in ($body -split "`n~`n")) {
                $l = $chunk -split "`n"
                $head = $l[0] -split ' '
                $n = [int]$head[2]
                $all += @{ Style = $head[0]; Width = [int]$head[1]; Text = $l[1..$n]; Color = $l[($n + 1)..(2 * $n)] }
            }
        } catch { return $null }
        $dots = [bool]($env:WT_SESSION -or $env:TERM_PROGRAM -eq 'vscode')
        $fit = @($all | Where-Object { ($dots -or $_.Style -ne 'd') -and $_.Width -le $Room })
        if (-not $fit.Count) { return $null }
        $fit[(Get-Random -Maximum $fit.Count)]
    }

    # Line $Row of a logo in its colours (W the pxd, C the ring, D its faint edge), no newline; -Pad fills it to the
    # logo's width. A row past the logo's end writes only the padding.
    function Write-PdLogoLine($Logo, [int]$Row, [switch]$Pad) {
        $line = ''; $mask = ''
        if ($Row -ge 0 -and $Row -lt $Logo.Text.Count) { $line = [string]$Logo.Text[$Row]; $mask = [string]$Logo.Color[$Row] }
        $colors = @{ 'W' = 'White'; 'C' = 'Cyan'; 'D' = 'DarkCyan' }
        # one colour run at a time; spaces join whichever run they're in
        $run = ''; $col = 'Cyan'
        for ($k = 0; $k -lt $line.Length; $k++) {
            $c = $col
            if ($line[$k] -ne ' ' -and $k -lt $mask.Length -and $colors.ContainsKey([string]$mask[$k])) { $c = $colors[[string]$mask[$k]] }
            if ($c -ne $col -and $run) { Write-Host $run -NoNewline -ForegroundColor $col; $run = '' }
            $col = $c; $run += $line[$k]
        }
        if ($run) { Write-Host $run -NoNewline -ForegroundColor $col }
        if ($Pad -and $line.Length -lt $Logo.Width) { Write-Host (' ' * ($Logo.Width - $line.Length)) -NoNewline }
    }

    function Show-PdHeader {
        param([string]$Name, [string]$Version = '', [string[]]$About = @(), [string]$Command = '')
        $text = New-Object System.Collections.Generic.List[object]
        $text.Add(@($Name, 'White'))
        $text.Add(@(((@($Version, 'pxdtek.com') | Where-Object { $_ }) -join '  -  '), 'DarkGray'))
        if ($About) { $text.Add(@('', 'Gray')); foreach ($a in $About) { $text.Add(@($a, 'Gray')) } }
        if ($Command) { $text.Add(@('', 'Gray')); $text.Add(@($Command, 'DarkCyan')) }
        $textWidth = ($text | ForEach-Object { $_[0].Length } | Measure-Object -Maximum).Maximum

        $w = 0
        try { $w = $Host.UI.RawUI.WindowSize.Width } catch {}
        $logo = Get-PdLogo $(if ($w -gt 0) { $w - $textWidth - 7 } else { [int]::MaxValue })

        Write-Host ''
        if (-not $logo) {
            Write-Host '  (pd) ' -NoNewline -ForegroundColor Cyan
            Write-Host $Name -NoNewline -ForegroundColor White
            Write-Host $(if ($Version) { "  $Version" } else { '' }) -ForegroundColor DarkGray
            Write-Host ''
            return
        }
        $rows = $logo.Text.Count
        $top = [Math]::Max(0, [Math]::Floor(($rows - $text.Count) / 2))
        for ($i = 0; $i -lt [Math]::Max($rows, $top + $text.Count); $i++) {
            $j = $i - $top
            $say = $j -ge 0 -and $j -lt $text.Count -and $text[$j][0]
            Write-Host '  ' -NoNewline
            Write-PdLogoLine $logo $i -Pad:$say
            if ($say) { Write-Host ('    ' + $text[$j][0]) -NoNewline -ForegroundColor $text[$j][1] }
            Write-Host ''
        }
        Write-Host ''
    }
    # ---- end of PD header --------------------------------------------------------------------------------------------

    # ---- where things come from: pxdtek.com first, GitHub (github.com/pxdtek) when a network blocks pxdtek.com ----------
    # pxdtek.com fetches from GitHub on its side, so normally this PC only ever talks to pxdtek.com.
    $script:PxdBlocked = $false
    $script:ProbeFailed = $false
    # HTML, not a script: a PowerShell script may well start with a '<#' comment, so look for real HTML tags.
    function Test-BlockPage($Body) { ($Body -is [string]) -and ($Body -match '^\s*<(!doctype|html|head|body|meta|title|script|div|\?xml)') }
    function Note-Blocked {
        if (-not $script:PxdBlocked) {
            $script:PxdBlocked = $true
            Say '  pxdtek.com is blocked or unreachable on this network; using GitHub instead.' 'DarkYellow'
        }
    }
    # pxdtek.com's copy of a release download or repo file -> the same thing on GitHub.
    function Get-GitHubCopy([string]$Url) {
        if ($Url -match '^https://pxdtek\.com/dl/([^/]+)/([^/]+)/([^/]+)$') { return "https://github.com/pxdtek/$($Matches[1])/releases/download/$($Matches[2])/$($Matches[3])" }
        if ($Url -match '^https://pxdtek\.com/raw/([^/]+)/(.+)$') { return "https://raw.githubusercontent.com/pxdtek/$($Matches[1])/HEAD/$($Matches[2])" }
        $null
    }
    # Text or JSON from pxdtek.com, else its GitHub copy. A filter's block page (HTML) counts as blocked.
    function Get-Remote([string]$Url, [int]$Timeout = 20) {
        $h = @{ 'User-Agent' = 'WindowsPowerShell pxdToolkit-cli' }
        if (-not $script:PxdBlocked -or -not (Get-GitHubCopy $Url)) {
            try {
                $r = Invoke-RestMethod -Uri $Url -Headers $h -TimeoutSec $Timeout
                if (-not (Test-BlockPage $r)) { return $r }
            } catch { if (-not (Get-GitHubCopy $Url)) { throw } }
            Note-Blocked
        }
        Invoke-RestMethod -Uri (Get-GitHubCopy $Url) -Headers $h -TimeoutSec $Timeout
    }
    # A release download to a file, from pxdtek.com or (blocked) GitHub.
    function Save-Remote([string]$Url, [string]$OutFile) {
        $h = @{ 'User-Agent' = 'pxdToolkit-installer' }
        if (-not $script:PxdBlocked) {
            try {
                Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing -Headers $h
                $head = [IO.File]::ReadAllBytes($OutFile) | Select-Object -First 1
                if ($head -ne 0x3C) { return }   # not '<': a real download, not a block page
            } catch { }
            Note-Blocked
        }
        $alt = Get-GitHubCopy $Url
        if (-not $alt) { throw "Couldn't download $Url" }
        Invoke-WebRequest -Uri $alt -OutFile $OutFile -UseBasicParsing -Headers $h
    }

    # ---- releases: asked once per run and remembered (in memory only) ---------------------------------------------------
    $releaseCache = @{}
    $script:LastGitHubError = ''
    function Get-LatestRelease([string]$Repo) {
        if ($releaseCache.ContainsKey($Repo)) { return $releaseCache[$Repo] }
        $rel = $null
        $bases = if ($script:PxdBlocked) { @('https://api.github.com') } else { @('https://pxdtek.com/gh', 'https://api.github.com') }
        foreach ($b in $bases) {
            try {
                $r = Invoke-RestMethod -Uri "$b/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'pxdToolkit-cli'; 'Accept' = 'application/vnd.github+json' } -TimeoutSec 20
                if ($r -and -not (Test-BlockPage $r) -and $r.tag_name) { $rel = $r; break }
                if ($b -like 'https://pxdtek.com*') { Note-Blocked }
            } catch {
                $code = 0
                try { $code = [int]$_.Exception.Response.StatusCode } catch { }
                if ($b -like 'https://pxdtek.com*') {
                    if ($code -eq 404) { $script:LastGitHubError = 'there is no release yet.'; break }
                    Note-Blocked; continue
                }
                $script:LastGitHubError = if ($code -eq 403 -or $code -eq 429) { "GitHub's hourly limit for this internet connection is used up. Try again in a while." }
                                          elseif ($code -eq 404) { 'there is no release yet.' }
                                          else { $_.Exception.Message }
            }
        }
        $releaseCache[$Repo] = $rel
        $rel
    }

    # ---- installing the pxd Toolkit app (only when asked: -Install, /tk/install, or yes in the list) --------------------
    function Install-Toolkit {
        if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitOperatingSystem) { Say '  pxd Toolkit needs 64-bit Windows 10 or 11.' 'Red'; return }
        $home2 = if ($env:PXDTK_HOME) { $env:PXDTK_HOME } else { Join-Path $env:USERPROFILE 'Apps\pxdToolkit' }
        $exe = Join-Path $home2 'pxdToolkit.exe'
        $headers = @{ 'User-Agent' = 'pxdToolkit-installer'; 'Accept' = 'application/vnd.github+json' }
    
        Say '  Finding the latest release...'
        $rel = Get-LatestRelease 'pxdtek/pxdToolkit'
        if (-not $rel) {
            Say "  Can't get the release from GitHub: $script:LastGitHubError" 'Red'
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
            Save-Remote $asset.browser_download_url $zip

            if ($sum) {
                $expected = ([string](Get-Remote $sum.browser_download_url)).Trim().Split(' ')[0].ToLowerInvariant()
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
    }

    if ($Install) {
        Show-PdHeader -Name 'pxd Toolkit' -Version 'installer' -Command $installCmd
        Install-Toolkit
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
        if ($wan -notmatch '^[0-9a-fA-F:.]{3,45}$') { $wan = ''; $script:ProbeFailed = $true }   # no answer, or a filter's block page: use GitHub from the start
        if ($wan -and $wan -ne 'unknown') { Add 'WAN IP' $wan }
        $L
    }

    # The info on the left, a PD logo on the right: one of the header's ten at random, the biggest that fits.
    function Show-SysHeader([string]$Version) {
        $text = New-Object System.Collections.Generic.List[object]
        $text.Add(@('pxd Toolkit', 'White'))
        $text.Add(@("$Version  -  pxdtek.com", 'DarkGray'))
        $text.Add(@('', 'Gray'))
        foreach ($l in (Get-SysLines)) { $text.Add($l) }
        $leftWidth = [Math]::Max(40, ($text | ForEach-Object { $_[0].Length } | Measure-Object -Maximum).Maximum + 4)

        $w = 0
        try { $w = $Host.UI.RawUI.WindowSize.Width } catch { }
        Write-Host ''
        $logo = Get-PdLogo $(if ($w -gt 0) { $w - 3 - $leftWidth } else { [int]::MaxValue })
        $logoRows = if ($logo) { $logo.Text.Count } else { 0 }
        $rows = [Math]::Max($text.Count, $logoRows)
        $top = [Math]::Max(0, [Math]::Floor(($logoRows - $text.Count) / 2))
        $logoTop = [Math]::Max(0, [Math]::Floor(($text.Count - $logoRows) / 2))
        for ($i = 0; $i -lt $rows; $i++) {
            $j = $i - $top
            $line = if ($j -ge 0 -and $j -lt $text.Count) { $text[$j] } else { @('', 'Gray') }
            Write-Host ('  ' + $line[0].PadRight($leftWidth)) -NoNewline -ForegroundColor $line[1]
            if ($logo) { Write-PdLogoLine $logo ($i - $logoTop) }
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
        if ($script:ProbeFailed) { Note-Blocked }
        # PXDTK_CATALOG: a local catalog.json, for testing.
        try {
            $catalog = if ($env:PXDTK_CATALOG) { Get-Content -LiteralPath $env:PXDTK_CATALOG -Raw | ConvertFrom-Json }
                       else { Get-Remote 'https://pxdtek.com/raw/pxdToolkit/catalog.json' }
            if ($catalog -is [string]) { $catalog = $catalog | ConvertFrom-Json }
        } catch { Say "  Can't reach pxdtek.com or GitHub: $($_.Exception.Message)" 'Red'; return $null }
        $root = Get-AppsRoot
        $apps = @([pscustomobject]@{ id = 'pxdToolkit'; name = 'pxd Toolkit'; repo = 'pxdtek/pxdToolkit'; exe = 'pxdToolkit.exe'; folder = 'pxdToolkit'; installCommand = ''; tagline = 'The app that installs, updates and opens every pxd app' })
        $apps += @($catalog.apps | Where-Object { $_.installCommand })
        $rows = @()
        foreach ($a in $apps) {
            $path = Find-Installed $a $root
            $have = if ($path) { Get-LocalVersion $path } else { '' }
            $latest = ''
            $rel = Get-LatestRelease $a.repo
            if ($rel) { $latest = $rel.tag_name.TrimStart('v') }
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
            Write-Host ($status + "  ").PadRight(32) -NoNewline -ForegroundColor $color
            if ($r.Code) { Write-Host "irm pxdtek.com/$($r.Code)|iex" -ForegroundColor DarkCyan }
            elseif ($r.Path -and $r.Latest -and $r.Have -and (Test-Newer $r.Latest $r.Have)) { Write-Host 'the app: pick it to update' -ForegroundColor DarkGray }
            elseif ($r.Path) { Write-Host 'the app: up to date' -ForegroundColor DarkGray }
            else { Write-Host 'the app: pick it to install' -ForegroundColor DarkGray }
        }
        Write-Host ''
        Say '  Tools run in this window. Nothing is installed or saved unless you pick the Toolkit and say yes.' 'DarkGray'
        Say '  Other apps install from the Toolkit window.' 'DarkGray'
        Write-Host ''
        $rows
    }

    # Runs a tool's script in memory, the same thing its one-liner does; this list comes back when it ends.
    function Invoke-Tool($Row) {
        Say "  Starting $($Row.App.name)..." 'DarkGray'
        $code = $null
        if (-not $script:PxdBlocked) {
            try {
                $code = Invoke-RestMethod -Uri ('https://pxdtek.com/' + $Row.Code) -Headers @{ 'User-Agent' = 'WindowsPowerShell pxdToolkit-cli' } -TimeoutSec 30
                if (Test-BlockPage $code) { $code = $null }
            } catch { }
            if (-not $code) { Note-Blocked }
        }
        # Never run a web filter's block page: the script itself, from GitHub, when pxdtek.com can't be used.
        if (-not $code) {
            if (-not $Row.App.script) { throw "pxdtek.com is blocked here and there's no GitHub copy of this tool's script." }
            $code = Invoke-RestMethod -Uri ("https://raw.githubusercontent.com/$($Row.App.repo)/HEAD/$($Row.App.script)") -Headers @{ 'User-Agent' = 'pxdToolkit-cli' } -TimeoutSec 30
            if (Test-BlockPage $code) { throw 'GitHub is blocked on this network too.' }
        }
        & ([scriptblock]::Create([string]$code))
    }

    $headers = @{ 'User-Agent' = 'pxdToolkit-cli'; 'Accept' = 'application/vnd.github+json' }
    Show-SysHeader $managerVersion
    while ($true) {
        $rows = @(Show-List)
        if (-not $rows -or -not $rows[0]) { break }
        $pick = ''
        try { $pick = (Read-Host '  Pick a number (Enter to quit)').Trim() } catch { break }
        if (-not $pick) { break }
        $n = 0
        if (-not [int]::TryParse($pick, [ref]$n) -or $n -lt 1 -or $n -gt $rows.Count) { Say "  There's no [$pick]." 'Yellow'; continue }
        $r = $rows[$n - 1]
        if (-not $r.Code) {
            # The Toolkit is an app, not a tool: installing it is the one thing this list writes, and only on a yes.
            $what = if (-not $r.Path) { 'Install' } elseif ($r.Latest -and $r.Have -and (Test-Newer $r.Latest $r.Have)) { 'Update' } else { 'Reinstall' }
            $yes = ''
            try { $yes = (Read-Host "  $what pxd Toolkit on this PC (%USERPROFILE%\Apps\pxdToolkit)? (y/N)").Trim().ToLower() } catch { }
            if ($yes -eq 'y' -or $yes -eq 'yes') { Write-Host ''; try { Install-Toolkit } catch { Say "  That didn't work: $($_.Exception.Message)" 'Red' } }
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
