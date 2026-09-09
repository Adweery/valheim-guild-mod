param([string]$GamePath)
$ErrorActionPreference = 'Stop'
$BaseUrl = 'https://github.com/Adweery/valheim-guild-mod'
$TempDir = Join-Path ([IO.Path]::GetTempPath()) ('ValheimGuild-' + [guid]::NewGuid())
$Changed = New-Object 'System.Collections.Generic.List[string]'
$Backup = $null
$Committing = $false

function Download-File([string]$Url, [string]$Path) {
    if (-not $Url.StartsWith('https://')) { throw 'Neplatna adresa stahovania.' }
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Path -TimeoutSec 180
}
function Assert-Hash([string]$Path, [string]$Expected) {
    if ($Expected -notmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Expected) {
        throw 'Subor nepresiel kontrolou. Instalacia zastavena.'
    }
}
function Assert-GameStopped {
    if (Get-Process -Name valheim -ErrorAction SilentlyContinue) { throw 'Najprv uplne vypni Valheim a spusti instalator znova.' }
}
function Expand-SafeZip([string]$Archive, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        if ($zip.Entries.Count -gt 500) { throw 'Prilis vela suborov v baliku.' }
        $size = 0L
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.StartsWith('/') -or $name.Contains(':') -or $name -match '(^|/)\.\.(/|$)' -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw 'Nebezpecna cesta v baliku.'
            }
            $size += $entry.Length
        }
        if ($size -gt 100MB) { throw 'Prilis velky balik.' }
    } finally { $zip.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $Destination)
}
function Assert-NoLink([string]$Path) {
    while ($Path -and $Path -ne $GamePath) {
        if (Test-Path -LiteralPath $Path) {
            if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Ciel obsahuje odkaz na iny priecinok. Instalacia zastavena.'
            }
        }
        $Path = Split-Path -Parent $Path
    }
}
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Assert-GameStopped
    if (-not $GamePath) {
        $Roots = @()
        foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
            $info = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
            if ($info.SteamPath) { $Roots += $info.SteamPath }
            if ($info.InstallPath) { $Roots += $info.InstallPath }
        }
        if (${env:ProgramFiles(x86)}) { $Roots += (Join-Path ${env:ProgramFiles(x86)} 'Steam') }
        foreach ($root in @($Roots)) {
            $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
            if (Test-Path -LiteralPath $vdf) {
                $content = Get-Content -LiteralPath $vdf -Raw
                foreach ($match in [regex]::Matches($content, '"path"\s+"([^"]+)"')) { $Roots += $match.Groups[1].Value.Replace('\\', '\') }
            }
        }
        $Candidates = @($Roots | Select-Object -Unique | ForEach-Object { Join-Path $_ 'steamapps\common\Valheim' } | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'valheim.exe') })
        if ($Candidates.Count -eq 1) { $GamePath = $Candidates[0] }
        else {
            Add-Type -AssemblyName System.Windows.Forms
            $picker = New-Object System.Windows.Forms.FolderBrowserDialog
            $picker.Description = 'Vyber priecinok Valheim, v ktorom je valheim.exe'
            if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Instalacia zrusena.' }
            $GamePath = $picker.SelectedPath
            $picker.Dispose()
        }
    }
    $GamePath = [IO.Path]::GetFullPath($GamePath).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath (Join-Path $GamePath 'valheim.exe'))) { throw 'V tomto priecinku nie je Valheim.' }
    New-Item -ItemType Directory -Path $TempDir | Out-Null
    Write-Host 'Stahujem aktualnu verziu Valheim Guild modu...'
    $manifestPath = Join-Path $TempDir 'latest.json'
    Download-File "$BaseUrl/releases/latest/download/latest.json" $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1) { throw 'Je potrebna nova verzia instalatora.' }
    $version = [string]$manifest.version
    if ($version -notmatch '^\d+\.\d+\.\d+$' -or $manifest.mod_url -ne "$BaseUrl/releases/download/v$version/ValheimGuildTelemetry.zip") { throw 'Neocakavane miesto stahovania.' }
    $modZip = Join-Path $TempDir 'mod.zip'
    Download-File $manifest.mod_url $modZip
    Assert-Hash $modZip $manifest.mod_sha256
    $modDir = Join-Path $TempDir 'mod'
    Expand-SafeZip $modZip $modDir
    $modFiles = @(Get-ChildItem -LiteralPath $modDir -Recurse -File)
    if ($modFiles.Count -ne 1 -or $modFiles[0].FullName -ne (Join-Path $modDir 'ValheimGuildTelemetry.dll')) { throw 'Neocakavany obsah modu.' }
    Assert-Hash $modFiles[0].FullName $manifest.dll_sha256
    $stage = Join-Path $TempDir 'stage'
    New-Item -ItemType Directory -Path $stage | Out-Null
    $core = Join-Path $GamePath 'BepInEx\core\BepInEx.dll'
    if (Test-Path -LiteralPath $core) {
        Assert-Hash $core $manifest.loader_core_sha256
        foreach ($name in @('winhttp.dll', 'doorstop_config.ini')) {
            if (-not (Test-Path -LiteralPath (Join-Path $GamePath $name))) { throw 'BepInEx nie je kompletne nastaveny pre Windows. Ozvi sa Adamovi.' }
        }
    } else {
        foreach ($name in @('BepInEx', 'winhttp.dll', 'doorstop_config.ini')) {
            if (Test-Path -LiteralPath (Join-Path $GamePath $name)) { throw 'Nasiel som inu alebo nekompletnu instalaciu modov. Ozvi sa Adamovi.' }
        }
        if ($manifest.loader_url -ne 'https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2350/') { throw 'Neocakavany zdroj BepInEx.' }
        $loaderZip = Join-Path $TempDir 'loader.zip'
        Download-File $manifest.loader_url $loaderZip
        Assert-Hash $loaderZip $manifest.loader_sha256
        $loaderDir = Join-Path $TempDir 'loader'
        Expand-SafeZip $loaderZip $loaderDir
        $pack = Join-Path $loaderDir 'BepInExPack_Valheim'
        foreach ($name in @('BepInEx', 'winhttp.dll', 'doorstop_config.ini')) { Copy-Item -LiteralPath (Join-Path $pack $name) -Destination $stage -Recurse }
    }
    $pluginDir = Join-Path $stage 'BepInEx\plugins\ValheimGuildTelemetry'
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    Copy-Item -LiteralPath $modFiles[0].FullName -Destination $pluginDir
    Set-Content -LiteralPath (Join-Path $stage 'valheim-guild-version.txt') -Value $version -Encoding ASCII
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    foreach ($file in $files) { Assert-NoLink (Join-Path $GamePath $file.FullName.Substring($stage.Length + 1)) }
    Assert-NoLink (Join-Path $GamePath 'ValheimGuildBackups')
    Assert-GameStopped
    $Backup = Join-Path $GamePath ('ValheimGuildBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $Backup 'files') -Force | Out-Null
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $target = Join-Path $GamePath $relative
        if (Test-Path -LiteralPath $target) {
            $saved = Join-Path (Join-Path $Backup 'files') $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved
        }
    }
    $Committing = $true
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $target = Join-Path $GamePath $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        $Changed.Add($relative)
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    $Changed | Set-Content -LiteralPath (Join-Path $Backup 'changed.txt')
    $Committing = $false
    Write-Host "Hotovo. Nainstalovana verzia: $version"
    Write-Host 'Valheim teraz spusti normalne cez Steam. XP a prepojenie uctu zostali zachovane.'
    exit 0
} catch {
    if ($Committing) {
        foreach ($relative in $Changed) {
            $saved = Join-Path (Join-Path $Backup 'files') $relative
            $target = Join-Path $GamePath $relative
            if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $target -Force -ErrorAction Continue }
            else { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue }
        }
        Write-Host 'Povodne subory boli obnovene zo zalohy.'
    }
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'Posli Adamovi tuto chybu. Instalacia sa nedokoncila.'
    exit 1
} finally {
    if (Test-Path -LiteralPath $TempDir) { Remove-Item -LiteralPath $TempDir -Recurse -Force -ErrorAction SilentlyContinue }
}
