# Dong goi Multi DepLao de gui nguoi dung thu: ban portable (.zip) + ban cai dat (Inno Setup 6).
# Khong can admin. Chay: powershell -ExecutionPolicy Bypass -File build\package.ps1 [-Force] [-Iscc <duong dan ISCC.exe>]
#   -Force: xoa dist\release-<version> neu da ton tai (that bai neu co app dang chay tu do).
# Ket qua (moi thu muc con = dung thu can gui, kem SHA256SUMS.txt rieng):
#   dist\release-<v>\ban-cai-dat\   MultiDepLao-<v>-setup-win-x64.exe + HUONG-DAN, MIEN-TRU, QUYEN-RIENG-TU
#   dist\release-<v>\ban-portable\  MultiDepLao-<v>-portable-win-x64.zip
#   dist\release-<v>\_build\app\    ban publish trung gian, KHONG gui
param([switch]$Force, [string]$Iscc)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\MultiDepLao.App\MultiDepLao.App.csproj'
[xml]$xml = Get-Content -LiteralPath $csproj -Raw
$version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Khong tim thay <Version> trong csproj.' }

# --- Tim ISCC.exe truoc khi lam gi (khong tu cai) ---
if (-not $Iscc) {
    $cands = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $cands = @($cmd.Source) + $cands }
    $Iscc = $cands | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) {
    throw 'Chua co Inno Setup 6 (ISCC.exe). Cai (khong can admin): winget install JRSoftware.InnoSetup --scope user, hoac truyen -Iscc <duong dan>.'
}

# --- Thu muc phat hanh MOI (khong ghi de thu muc app dang chay) ---
$rel = Join-Path $root "dist\release-$version"
if (Test-Path $rel) {
    if (-not $Force) { throw "Da co $rel. Xoa tay hoac chay lai voi -Force (dong app neu dang chay tu do)." }
    Remove-Item -Recurse -Force $rel
}
New-Item -ItemType Directory -Force $rel | Out-Null
$app = Join-Path $rel '_build\app'
$outSetup = Join-Path $rel 'ban-cai-dat'
$outPortable = Join-Path $rel 'ban-portable'
New-Item -ItemType Directory -Force $outSetup, $outPortable | Out-Null

& (Join-Path $PSScriptRoot 'publish.ps1') -OutDir $app
if (-not (Test-Path (Join-Path $app 'MultiDepLao.App.exe'))) { throw 'Publish khong tao ra MultiDepLao.App.exe' }

# --- Chan du lieu ca nhan lot vao goi ---
function Assert-NoPersonalData([string]$dir) {
    $bad = @()
    $bad += Get-ChildItem -LiteralPath $dir -Recurse -Directory -Force |
        Where-Object { $_.Name -in @('profiles', 'avatars', 'logs', 'zalo-nocap', 'zalo-cap', 'data') }
    $bad += Get-ChildItem -LiteralPath $dir -Recurse -File -Force |
        Where-Object { $_.Name -in @('config.json', 'mz.pid', 'app.lock', 'portable.txt') -or $_.Name -like '*.log' -or $_.Name -like 'config*.json*' }
    if ($bad.Count -gt 0) {
        $list = ($bad | ForEach-Object { '  ' + $_.FullName.Substring($dir.Length).TrimStart('\') }) -join "`n"
        throw "Thu muc publish co du lieu ca nhan / tep khong duoc phep, dung lai:`n$list"
    }
}
Assert-NoPersonalData $app
Write-Host 'Kiem tra du lieu ca nhan: sach.'

$guide = Join-Path $PSScriptRoot 'HUONG-DAN.txt'
$disclaimer = Join-Path $PSScriptRoot 'MIEN-TRU-TRACH-NHIEM.txt'
$privacy = Join-Path $PSScriptRoot 'QUYEN-RIENG-TU.txt'
$ico = Join-Path $root 'src\MultiDepLao.App\Assets\app.ico'

# --- Ban portable ---
$stage = Join-Path $rel '_build\portable-stage'
$pdir = Join-Path $stage 'MultiDepLao'
New-Item -ItemType Directory -Force $pdir | Out-Null
Copy-Item -Path (Join-Path $app '*') -Destination $pdir -Recurse
Copy-Item -LiteralPath $guide -Destination $pdir
Copy-Item -LiteralPath $disclaimer -Destination $pdir
Copy-Item -LiteralPath $privacy -Destination $pdir
Set-Content -LiteralPath (Join-Path $pdir 'portable.txt') -Encoding ASCII -Value @(
    'File nay bat che do portable: du lieu luu trong thu muc "data" canh MultiDepLao.App.exe.',
    'Xoa file nay thi app dung %LOCALAPPDATA%\MultiDepLao nhu ban cai dat.')
$zip = Join-Path $outPortable "MultiDepLao-$version-portable-win-x64.zip"
# Tu ghi tung muc voi dau '/' (ZipFile.CreateFromDirectory cua .NET Framework/PS 5.1 ghi '\' -> mot so trinh giai nen loi).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$za = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in Get-ChildItem -LiteralPath $pdir -Recurse -File -Force) {
        $name = 'MultiDepLao/' + $f.FullName.Substring($pdir.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $za.Dispose() }
Remove-Item -Recurse -Force $stage

# --- Ban cai dat ---
$iss = Join-Path $PSScriptRoot 'installer.iss'
$base = "MultiDepLao-$version-setup-win-x64"
& $Iscc /Qp "/DAppVersion=$version" "/DSourceDir=$app" "/DGuideFile=$guide" "/DIconFile=$ico" "/DDisclaimerFile=$disclaimer" "/DPrivacyFile=$privacy" "/DOutDir=$outSetup" "/DOutBase=$base" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC loi (exit $LASTEXITCODE)" }
$setup = Join-Path $outSetup "$base.exe"
Copy-Item -LiteralPath $guide -Destination $outSetup   # gui kem bo cai de nguoi nhan doc truoc khi cai
Copy-Item -LiteralPath $disclaimer -Destination $outSetup
Copy-Item -LiteralPath $privacy -Destination $outSetup

# --- Kich thuoc + SHA256 (moi thu muc gui co SHA256SUMS.txt rieng) ---
$utf8 = New-Object Text.UTF8Encoding $false
foreach ($f in @($setup, $zip)) {
    $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant()
    $len = (Get-Item -LiteralPath $f).Length
    Write-Host ("{0}`n  {1:N1} MB ({2} byte)`n  SHA256 {3}" -f $f, ($len / 1MB), $len, $h)
    [IO.File]::WriteAllText((Join-Path (Split-Path $f -Parent) 'SHA256SUMS.txt'), "$h *$(Split-Path $f -Leaf)`r`n", $utf8)
}

# Don thu muc trung gian (ban publish da nam trong setup va zip). Truoc day xoa 1 lan + SilentlyContinue: file exe vua tao
# hay bi Defender/bo lap chi muc giu vai giay -> xoa that bai im lang, _build (~220 MB) con sot. Nay: thu lai + canh bao.
$buildDir = Join-Path $rel '_build'
for ($i = 1; $i -le 10 -and (Test-Path -LiteralPath $buildDir); $i++) {
    try { Remove-Item -LiteralPath $buildDir -Recurse -Force -ErrorAction Stop } catch { Start-Sleep -Seconds 2 }
}
if (Test-Path -LiteralPath $buildDir) { Write-Warning "Chua xoa duoc $buildDir (file dang bi giu) - xoa tay sau." }

# Loi tat tao goi gui khach (bam dup) ngay trong dist
#   (chi khi co script gui khach - ban ma nguon cong khai khong kem)
$cmd = Join-Path $root 'dist\Tao-goi-gui-khach.cmd'
$hasGuiKhach = Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Tao-goi-gui-khach.cmd')
if ($hasGuiKhach) {
    [IO.File]::WriteAllText($cmd, "@echo off`r`ncall `"%~dp0..\build\Tao-goi-gui-khach.cmd`" %*`r`n", (New-Object Text.UTF8Encoding $false))
}

Write-Host "Gui ban cai dat : $outSetup"
Write-Host "Gui ban portable: $outPortable"
if ($hasGuiKhach) { Write-Host "Tao zip gui khach: bam dup $cmd" }
