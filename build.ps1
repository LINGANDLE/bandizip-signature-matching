# Bandizip 7.47 专业版解锁补丁 —— 编译脚本
# 用法: powershell -ExecutionPolicy Bypass -File .\build.ps1

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "未找到 csc.exe：$csc（需要 .NET Framework 4.x）"
}

$outDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$target = Join-Path $outDir 'Bandizip747ProPatch.exe'

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    "/out:$target"
    '/r:System.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    'src\BandizipProPatch.cs'
)

Write-Host '[*] 正在编译 ...' -ForegroundColor Cyan
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$hash = (Get-FileHash $target -Algorithm SHA256).Hash
Write-Host "[OK] $target" -ForegroundColor Green
Write-Host "     SHA256: $hash"
