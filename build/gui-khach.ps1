# Tạo file zip gửi khách từ bản phát hành đã đóng gói (dist\release-<phiên bản>\, do build\package.ps1 tạo).
# Mỗi zip có đúng một thư mục gốc MultiDepLao\ chứa bộ app theo bản đã chọn:
#   cai-dat : MultiDepLao\{MultiDepLao-<v>-setup-win-x64.exe, HUONG-DAN.txt, MIEN-TRU-TRACH-NHIEM.txt, QUYEN-RIENG-TU.txt}
#   portable: MultiDepLao\{MultiDepLao.App.exe, DLL, portable.txt, HUONG-DAN.txt, MIEN-TRU-TRACH-NHIEM.txt, QUYEN-RIENG-TU.txt}
# Kết quả: dist\gui-khach\MultiDepLao-<v>-<cai-dat|portable>.zip + SHA256SUMS.txt.
# Chạy: bấm đúp build\Tao-goi-gui-khach.cmd (hỏi chọn), hoặc
#   powershell -ExecutionPolicy Bypass -File build\gui-khach.ps1 -Ban cai-dat|portable|ca-hai [-Version 0.3.1] [-NoOpen]
param(
    [ValidateSet('cai-dat', 'portable', 'ca-hai')][string]$Ban,
    [string]$Version,
    [switch]$NoOpen
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Get-Ver([string]$name) { try { [version]($name -replace '^release-', '') } catch { [version]'0.0' } }

# --- Chọn phiên bản ---
$releases = @(Get-ChildItem -LiteralPath $dist -Directory -Filter 'release-*' -ErrorAction SilentlyContinue |
    Where-Object { (Test-Path (Join-Path $_.FullName 'ban-cai-dat')) -or (Test-Path (Join-Path $_.FullName 'ban-portable')) } |
    Sort-Object { Get-Ver $_.Name } -Descending)
if ($releases.Count -eq 0) { throw 'Chưa có bản phát hành nào trong dist. Chạy build\package.ps1 trước.' }
if ($Version) {
    $rel = $releases | Where-Object { $_.Name -eq "release-$Version" } | Select-Object -First 1
    if (-not $rel) { throw "Không có dist\release-$Version." }
} elseif ($releases.Count -eq 1 -or $Ban) {
    $rel = $releases[0]
} else {
    Write-Host 'Chọn phiên bản:'
    for ($i = 0; $i -lt $releases.Count; $i++) { Write-Host ("  {0} = {1}{2}" -f ($i + 1), ($releases[$i].Name -replace '^release-', ''), $(if ($i -eq 0) { ' (mới nhất)' } else { '' })) }
    $a = Read-Host 'Nhập số [1]'
    if (-not $a) { $a = '1' }
    $n = 0
    if (-not [int]::TryParse($a, [ref]$n) -or $n -lt 1 -or $n -gt $releases.Count) { throw "Lựa chọn không hợp lệ: $a" }
    $rel = $releases[$n - 1]
}
$ver = $rel.Name -replace '^release-', ''

# --- Chọn bản ---
if (-not $Ban) {
    Write-Host ''
    Write-Host "Multi DépLào $ver – tạo zip gửi khách:"
    Write-Host '  1 = Bản cài đặt (khách chạy file setup để cài)'
    Write-Host '  2 = Bản portable (khách giải nén là chạy, không cần cài)'
    Write-Host '  3 = Cả hai'
    $a = Read-Host 'Nhập số'
    $Ban = switch ($a) { '1' { 'cai-dat' } '2' { 'portable' } '3' { 'ca-hai' } default { throw "Lựa chọn không hợp lệ: $a" } }
}
$kinds = if ($Ban -eq 'ca-hai') { @('cai-dat', 'portable') } else { @($Ban) }

$outDir = Join-Path $dist 'gui-khach'
New-Item -ItemType Directory -Force $outDir | Out-Null
$guide = Join-Path $PSScriptRoot 'HUONG-DAN.txt'
$disclaimer = Join-Path $PSScriptRoot 'MIEN-TRU-TRACH-NHIEM.txt'
$privacy = Join-Path $PSScriptRoot 'QUYEN-RIENG-TU.txt'

# Ghi zip: mọi mục nằm dưới MultiDepLao/ (dấu '/'), không có gì khác ở gốc.
function Write-ClientZip([string]$stageDir, [string]$zipPath) {
    $tmp = "$zipPath.tmp"
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    $za = [IO.Compression.ZipFile]::Open($tmp, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in Get-ChildItem -LiteralPath $stageDir -Recurse -File -Force) {
            $name = 'MultiDepLao/' + $f.FullName.Substring($stageDir.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $za.Dispose() }
    Move-Item -LiteralPath $tmp -Destination $zipPath -Force
}

$made = @()
foreach ($k in $kinds) {
    $stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("MultiDepLao-gui-khach-" + [guid]::NewGuid().ToString('N'))
    $stage = Join-Path $stageRoot 'MultiDepLao'
    New-Item -ItemType Directory -Force $stage | Out-Null
    try {
        if ($k -eq 'cai-dat') {
            $src = Join-Path $rel.FullName 'ban-cai-dat'
            $setup = Get-ChildItem -LiteralPath $src -Filter 'MultiDepLao-*-setup-win-x64.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $setup) { throw "Không thấy file setup trong $src" }
            Copy-Item -LiteralPath $setup.FullName -Destination $stage
            foreach ($t in @((Join-Path $src 'HUONG-DAN.txt'), $guide)) { if (Test-Path -LiteralPath $t) { Copy-Item -LiteralPath $t -Destination $stage; break } }
            foreach ($t in @((Join-Path $src 'MIEN-TRU-TRACH-NHIEM.txt'), $disclaimer)) { if (Test-Path -LiteralPath $t) { Copy-Item -LiteralPath $t -Destination $stage; break } }
            foreach ($t in @((Join-Path $src 'QUYEN-RIENG-TU.txt'), $privacy)) { if (Test-Path -LiteralPath $t) { Copy-Item -LiteralPath $t -Destination $stage; break } }
        } else {
            $src = Join-Path $rel.FullName 'ban-portable'
            $pz = Get-ChildItem -LiteralPath $src -Filter 'MultiDepLao-*-portable-win-x64.zip' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $pz) { throw "Không thấy zip portable trong $src" }
            # Giải nén vào thư mục tạm, lấy đúng thư mục MultiDepLao\ bên trong
            [IO.Compression.ZipFile]::ExtractToDirectory($pz.FullName, $stageRoot)
            if (-not (Test-Path (Join-Path $stage 'MultiDepLao.App.exe'))) { throw "Zip portable không có MultiDepLao\MultiDepLao.App.exe: $($pz.FullName)" }
            if (-not (Test-Path (Join-Path $stage 'portable.txt'))) { throw "Zip portable thiếu portable.txt: $($pz.FullName)" }
            if (-not (Test-Path (Join-Path $stage 'MIEN-TRU-TRACH-NHIEM.txt')) -and (Test-Path -LiteralPath $disclaimer)) { Copy-Item -LiteralPath $disclaimer -Destination $stage }
            if (-not (Test-Path (Join-Path $stage 'QUYEN-RIENG-TU.txt')) -and (Test-Path -LiteralPath $privacy)) { Copy-Item -LiteralPath $privacy -Destination $stage }
        }
        # Không để dữ liệu cá nhân lọt vào
        $bad = @(Get-ChildItem -LiteralPath $stage -Recurse -Force | Where-Object {
            ($_.PSIsContainer -and $_.Name -in @('data', 'profiles', 'avatars', 'logs')) -or
            (-not $_.PSIsContainer -and ($_.Name -in @('config.json', 'mz.pid', 'app.lock') -or $_.Name -like '*.log')) })
        if ($bad.Count) { throw ("Gói có dữ liệu cá nhân, dừng lại: " + (($bad | ForEach-Object Name) -join ', ')) }

        $zip = Join-Path $outDir "MultiDepLao-$ver-$k.zip"
        Write-ClientZip $stage $zip
        $made += $zip
    } finally {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# --- Kiểm tra cấu trúc + SHA256 ---
$sumsFile = Join-Path $outDir 'SHA256SUMS.txt'
$sums = [ordered]@{}
if (Test-Path -LiteralPath $sumsFile) {
    foreach ($l in Get-Content -LiteralPath $sumsFile) { if ($l -match '^([0-9a-f]{64}) \*(.+)$' -and (Test-Path -LiteralPath (Join-Path $outDir $Matches[2]))) { $sums[$Matches[2]] = $Matches[1] } }
}
Write-Host ''
foreach ($z in $made) {
    $a = [IO.Compression.ZipFile]::OpenRead($z)
    try {
        $roots = @($a.Entries | ForEach-Object { ($_.FullName -split '/')[0] } | Sort-Object -Unique)
        $files = @($a.Entries | ForEach-Object { $_.FullName.Substring('MultiDepLao/'.Length) })
    } finally { $a.Dispose() }
    if ($roots.Count -ne 1 -or $roots[0] -ne 'MultiDepLao') { throw "Zip sai cấu trúc (gốc: $($roots -join ', ')): $z" }
    $h = (Get-FileHash -LiteralPath $z -Algorithm SHA256).Hash.ToLowerInvariant()
    $sums[(Split-Path $z -Leaf)] = $h
    Write-Host ("Đã tạo: {0}  ({1:N1} MB)" -f $z, ((Get-Item -LiteralPath $z).Length / 1MB))
    Write-Host ("  Bên trong: MultiDepLao\{0}" -f ($files -join ', '))
    Write-Host "  SHA256 $h"
}
$lines = foreach ($k in $sums.Keys) { "$($sums[$k]) *$k" }
[IO.File]::WriteAllText($sumsFile, (($lines -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding $false))

if (-not $NoOpen -and $made.Count) { Start-Process explorer.exe -ArgumentList "/select,`"$($made[-1])`"" }
