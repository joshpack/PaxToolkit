<#
    PAX Toolkit one-line installer:  irm paxdigi.com/toolkit | iex
    (or: irm https://raw.githubusercontent.com/joshpack/PaxToolkit/main/install.ps1 | iex)

    Downloads the latest PAX Toolkit release from GitHub, checks its SHA-256, puts PaxToolkit.exe in
    %USERPROFILE%\Apps\PaxToolkit, adds a Start menu shortcut and starts it. Running it again updates it.
    No admin rights needed. Works in Windows PowerShell 5.1 and PowerShell 7. ASCII only, so irm | iex never trips.

    Options (environment variables, since irm | iex can't take parameters):
      $env:PAXTK_HOME     = 'D:\Tools\PaxToolkit'   install somewhere else
      $env:PAXTK_NOSTART  = '1'                      install without starting it
      $env:PAXTK_NOSHORTCUT = '1'                    no Start menu shortcut
#>
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is many times slower with the progress bar
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

    function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

    Write-Host ''
    Say '  PAX TOOLKIT' 'Green'
    Say '  Install, update and open your PAX apps from one place.' 'DarkGray'
    Write-Host ''

    if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitOperatingSystem) {
        Say '  PAX Toolkit needs 64-bit Windows 10 or 11.' 'Red'
        return
    }

    $home2 = if ($env:PAXTK_HOME) { $env:PAXTK_HOME } else { Join-Path $env:USERPROFILE 'Apps\PaxToolkit' }
    $exe = Join-Path $home2 'PaxToolkit.exe'
    $headers = @{ 'User-Agent' = 'PaxToolkit-installer'; 'Accept' = 'application/vnd.github+json' }

    Say '  Finding the latest release...'
    try {
        $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/joshpack/PaxToolkit/releases/latest' -Headers $headers
    } catch {
        Say "  Can't reach GitHub: $($_.Exception.Message)" 'Red'
        return
    }

    $asset = $rel.assets | Where-Object { $_.name -match '^PaxToolkit-.*-win-x64\.zip$' } | Select-Object -First 1
    if (-not $asset) { Say "  The $($rel.tag_name) release has no Windows download." 'Red'; return }
    $sum = $rel.assets | Where-Object { $_.name -eq ($asset.name + '.sha256') } | Select-Object -First 1
    $version = $rel.tag_name.TrimStart('v')

    if (Test-Path -LiteralPath $exe) {
        $have = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
        if ($have) { $have = ($have -split '\+')[0] }
        if ($have -eq $version) { Say "  PAX Toolkit $version is already installed; reinstalling it." 'DarkGray' }
        else { Say "  Updating PAX Toolkit $have to $version." }
    } else {
        Say "  Installing PAX Toolkit $version ($([math]::Round($asset.size / 1MB)) MB)."
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('paxtoolkit-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        $zip = Join-Path $tmp $asset.name
        Say '  Downloading...'
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers @{ 'User-Agent' = 'PaxToolkit-installer' }

        if ($sum) {
            $expected = ([string](Invoke-RestMethod -Uri $sum.browser_download_url -Headers @{ 'User-Agent' = 'PaxToolkit-installer' })).Trim().Split(' ')[0].ToLowerInvariant()
            $sha = [Security.Cryptography.SHA256]::Create()
            $fs = [IO.File]::OpenRead($zip)
            try { $actual = -join ($sha.ComputeHash($fs) | ForEach-Object { $_.ToString('x2') }) } finally { $fs.Dispose(); $sha.Dispose() }
            if ($expected -ne $actual) { Say '  The download is damaged (checksum does not match). Nothing was changed; try again.' 'Red'; return }
            Say '  Checksum OK.' 'DarkGray'
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $unz = Join-Path $tmp 'x'
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unz)
        $fresh = Get-ChildItem -LiteralPath $unz -Recurse -Filter 'PaxToolkit.exe' | Select-Object -First 1
        if (-not $fresh) { Say '  The download has no PaxToolkit.exe in it.' 'Red'; return }

        # A running copy holds the exe: ask it to close, then make sure.
        $running = @(Get-Process -Name PaxToolkit -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq [IO.Path]::GetFullPath($exe)) } catch { $false } })
        if ($running.Count -gt 0) {
            Say '  Closing the running PAX Toolkit...' 'DarkGray'
            foreach ($p in $running) { try { $p.Kill(); $p.WaitForExit(5000) | Out-Null } catch { } }
        }

        New-Item -ItemType Directory -Path $home2 -Force | Out-Null
        $done = $false
        for ($i = 0; $i -lt 10 -and -not $done; $i++) {
            try { Copy-Item -LiteralPath $fresh.FullName -Destination $exe -Force; $done = $true } catch { Start-Sleep -Milliseconds 400 }
        }
        if (-not $done) { Say "  Couldn't replace $exe (still in use). Close PAX Toolkit and run this again." 'Red'; return }
        Say "  Installed to $home2" 'Green'

        if ($env:PAXTK_NOSHORTCUT -ne '1') {
            try {
                $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PAX Toolkit'
                New-Item -ItemType Directory -Path $menu -Force | Out-Null
                $ws = New-Object -ComObject WScript.Shell
                $lnk = $ws.CreateShortcut((Join-Path $menu 'PAX Toolkit.lnk'))
                $lnk.TargetPath = $exe
                $lnk.WorkingDirectory = $home2
                $lnk.Description = 'Install, update and open your PAX apps'
                $lnk.Save()
                Say '  Start menu shortcut: Start > PAX Toolkit' 'DarkGray'
            } catch {
                Say "  (Couldn't add the Start menu shortcut: $($_.Exception.Message))" 'DarkYellow'
            }
        }
    } finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($env:PAXTK_NOSTART -ne '1') {
        Start-Process -FilePath $exe -WorkingDirectory $home2
        Say '  PAX Toolkit is starting.' 'Green'
    }
    Write-Host ''
}
