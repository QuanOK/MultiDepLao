# Publish MultiDepLao (self-contained, single-file, win-x64) ra dist\MultiDepLao-<version>\ va nen zip.
# Khong can admin. Chay: powershell -File build\publish.ps1 [-NoZip] [-OutDir <thu muc>]
# -OutDir: publish vao thu muc chi dinh (phai chua ton tai hoac rong), khong xoa gi, khong nen zip (package.ps1 dung).
param([switch]$NoZip, [string]$OutDir)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\MultiDepLao.App\MultiDepLao.App.csproj'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }

[xml]$xml = Get-Content -LiteralPath $csproj -Raw
$version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Khong tim thay <Version> trong csproj.' }

$dist = Join-Path $root 'dist'
if ($OutDir) {
    $out = [IO.Path]::GetFullPath($OutDir)
    if ((Test-Path $out) -and (Get-ChildItem $out -Force | Select-Object -First 1)) { throw "Thu muc dich da co du lieu: $out" }
    $NoZip = $true
} else {
    $out = Join-Path $dist "MultiDepLao-$version"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
}
New-Item -ItemType Directory -Force $out | Out-Null

# Khong trimming (WPF khong ho tro). Single-file (tu 0.3.1): exe gop toan bo .NET + assembly; DLL native cua WPF
# de canh exe (khong tu giai nen ra %TEMP%); pdb nhung vao exe; khong nen bundle (khoi dong nhanh hon, zip da nen).
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=false `
    -p:DebugType=embedded -p:PublishTrimmed=false -p:PublishReadyToRun=true `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish loi (exit $LASTEXITCODE)" }

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Thu muc: {0} ({1:N1} MB)" -f $out, ($size / 1MB))

if (-not $NoZip) {
    $zip = Join-Path $dist "MultiDepLao-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Host ("Zip: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}
