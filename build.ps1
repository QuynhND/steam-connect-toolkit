<#
.SYNOPSIS
  Biên dịch Steam Connect Toolkit bằng csc.exe có sẵn trong Windows (.NET Framework 4.8), không cần cài SDK.

.PARAMETER SmokeTest
  Biên dịch thêm tests\SmokeTest (console kiểm thử lõi, không đổi DNS/VPN thật).
.PARAMETER RunSmokeTest
  Chạy bộ tự kiểm thử rồi kiểm tra mạng thật sau khi biên dịch.
.PARAMETER Installer
  Biên dịch bộ cài bằng Inno Setup (nếu máy có ISCC.exe).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -SmokeTest -RunSmokeTest
#>
param(
    [switch]$SmokeTest,
    [switch]$RunSmokeTest,
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src\SteamConnectToolkit"
$dist = Join-Path $root "dist"
$icon = Join-Path $root "assets\app.ico"

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "Không tìm thấy csc.exe của .NET Framework 4.x. Hãy cài .NET Framework 4.8." }

New-Item -ItemType Directory -Force $dist | Out-Null

if (-not (Test-Path $icon)) {
    Write-Host "Tạo icon..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\make-icon.ps1") -OutFile $icon
}

function Invoke-Csc([string[]]$Options, [string[]]$Sources, [string]$Label) {
    $rsp = Join-Path $env:TEMP ("sct-" + $Label + ".rsp")
    $lines = @()
    $lines += $Options
    $lines += ($Sources | ForEach-Object { '"' + $_ + '"' })
    Set-Content -Path $rsp -Value $lines -Encoding Unicode
    Write-Host "Biên dịch $Label ..."
    & $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "Biên dịch $Label thất bại (mã $LASTEXITCODE)" }
    Remove-Item $rsp -ErrorAction SilentlyContinue
}

$commonRefs = @(
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Web.Extensions.dll", "/r:Microsoft.CSharp.dll", "/r:System.Xml.dll", "/r:System.Security.dll", "/r:System.IO.Compression.dll"
)

# ---- Ứng dụng chính ----
$appSources = Get-ChildItem -Path $src -Recurse -Filter *.cs | Select-Object -ExpandProperty FullName
$appOptions = @(
    "/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/debug-", "/warn:4", "/nowarn:1591",
    "/out:""$dist\SteamConnectToolkit.exe""",
    "/win32manifest:""$src\app.manifest""",
    "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll"
) + $commonRefs
if (Test-Path $icon) { $appOptions += "/win32icon:""$icon""" }
Invoke-Csc -Options $appOptions -Sources $appSources -Label "SteamConnectToolkit"
$exe = Join-Path $dist "SteamConnectToolkit.exe"
Write-Host ("OK: {0} ({1:N0} KB)" -f $exe, ((Get-Item $exe).Length / 1KB))

# ---- SmokeTest (tuỳ chọn) ----
if ($SmokeTest -or $RunSmokeTest) {
    $testSources = @((Join-Path $root "tests\SmokeTest\Program.cs"))
    $testSources += Get-ChildItem -Path (Join-Path $src "Models"), (Join-Path $src "Services") -Recurse -Filter *.cs | Select-Object -ExpandProperty FullName
    $testOptions = @("/nologo", "/target:exe", "/platform:anycpu", "/optimize+", "/out:""$dist\SmokeTest.exe""") + $commonRefs
    Invoke-Csc -Options $testOptions -Sources $testSources -Label "SmokeTest"
    if ($RunSmokeTest) {
        Write-Host ""
        Write-Host "=== Tự kiểm thử (không đổi DNS/VPN thật, không cần admin) ==="
        & (Join-Path $dist "SmokeTest.exe") selftest
        $selfTestExit = $LASTEXITCODE
        Write-Host ""
        Write-Host "=== Kiểm tra mạng thật ==="
        & (Join-Path $dist "SmokeTest.exe") google cloudflare quad9 --no-discovery
        if ($selfTestExit -ne 0) { throw "Tự kiểm thử thất bại (mã $selfTestExit)" }
    }
}

# ---- Installer (tuỳ chọn, cần Inno Setup 6) ----
if ($Installer) {
    $iscc = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        Write-Warning "Không tìm thấy Inno Setup 6 (ISCC.exe). Cài từ https://jrsoftware.org/isinfo.php rồi chạy lại với -Installer."
    } else {
        Write-Host "Tạo bộ cài bằng Inno Setup..."
        & $iscc (Join-Path $root "installer\SteamConnectToolkit.iss")
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup thất bại (mã $LASTEXITCODE)" }
    }
}

Write-Host "Hoàn tất."
