<#
.SYNOPSIS
  Đóng gói phát hành Steam Connect Toolkit: build → kiểm thử → quét bí mật → gói portable (zip) → checksum → manifest latest.json (+ bộ cài nếu có Inno Setup).
  Mặc định URL trong manifest trỏ tới GitHub Releases của kho (tag v<phiên bản>); -Publish tải gói lên GitHub Release bằng tools\publish-release.ps1.

.PARAMETER BaseUrl
  Địa chỉ gốc nơi sẽ đặt các file phát hành khi KHÔNG dùng GitHub Releases (ghi URL vào latest.json), ví dụ https://example.com/sct/releases/1.0.0
.PARAMETER Publish
  Sau khi đóng gói, tạo GitHub Release tag v<phiên bản> và tải gói lên (tag phải đã push; token từ GITHUB_TOKEN hoặc Git Credential Manager).
.PARAMETER SkipTests
  Bỏ qua bộ tự kiểm thử (không khuyến nghị).
.PARAMETER Installer
  Biên dịch bộ cài bằng Inno Setup (cần ISCC.exe).
.PARAMETER Notes
  Ghi chú phát hành ngắn ghi vào manifest (mặc định lấy mục đầu của CHANGELOG.md).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\release.ps1            # gói + manifest trỏ GitHub Releases
  powershell -ExecutionPolicy Bypass -File .\release.ps1 -Publish   # ... rồi tạo Release và tải lên
  powershell -ExecutionPolicy Bypass -File .\release.ps1 -BaseUrl "https://example.com/sct/releases/1.0.0" -Installer
#>
param(
    [string]$BaseUrl = "",
    [string]$GitHubRepo = "QuynhND/steam-connect-toolkit",
    [switch]$SkipTests,
    [switch]$Installer,
    [switch]$Publish,
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
# Mặc định phát hành lên GitHub Releases của kho $GitHubRepo (gói KHÔNG nằm trong kho nguồn); -BaseUrl để tự host nơi khác.
$useGitHub = -not $BaseUrl

# 1. Build (+ SmokeTest, + Installer)
$buildArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "build.ps1"), "-SmokeTest")
if ($Installer) { $buildArgs += "-Installer" }
& powershell @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build thất bại" }

# 2. Kiểm thử
if (-not $SkipTests) {
    Write-Host "Chạy bộ tự kiểm thử (offline)..."
    & (Join-Path $dist "SmokeTest.exe") selftest --no-network | Select-Object -Last 3
    if ($LASTEXITCODE -ne 0) { throw "Tự kiểm thử thất bại – không phát hành" }
}

# 2b. Quét thông tin nhạy cảm trong toàn bộ file được theo dõi (chặn nếu có khoá/token/mật khẩu)
if (Test-Path (Join-Path $root "tools\check-secrets.ps1")) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\check-secrets.ps1") -All
    if ($LASTEXITCODE -ne 0) { throw "Phát hiện thông tin nhạy cảm – không phát hành" }
}

# 3. Thông tin phiên bản
$exe = Join-Path $dist "SteamConnectToolkit.exe"
$version = (Get-Item $exe).VersionInfo.ProductVersion.Trim()
if (-not $version) { $version = (Get-Item $exe).VersionInfo.FileVersion }
$rel = Join-Path $dist "release\$version"
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Force $rel | Out-Null
Write-Host "Phiên bản: $version → $rel"

# 4. Gói portable
$portableDir = Join-Path $rel "portable"
New-Item -ItemType Directory -Force $portableDir | Out-Null
Copy-Item $exe $portableDir
foreach ($f in @("README.md", "LICENSE", "CHANGELOG.md")) { if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $portableDir } }
if (Test-Path (Join-Path $root "docs")) { Copy-Item (Join-Path $root "docs") (Join-Path $portableDir "docs") -Recurse }
$zip = Join-Path $rel "SteamConnectToolkit-$version-portable.zip"
Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $zip -Force
Copy-Item $exe (Join-Path $rel "SteamConnectToolkit.exe")   # file lẻ để cập nhật tại chỗ (portable)
Remove-Item $portableDir -Recurse -Force

# 5. Bộ cài (nếu đã build)
$setup = Join-Path $dist "SteamConnectToolkit-Setup-$version.exe"
$hasSetup = Test-Path $setup
if ($hasSetup) { Copy-Item $setup $rel } else { Write-Warning "Không có bộ cài (cần Inno Setup 6 và -Installer); manifest chỉ có gói portable." }

# 6. Checksum
function Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant() }
$sums = @()
foreach ($f in Get-ChildItem $rel -File | Where-Object { $_.Extension -in ".exe", ".zip" }) { $sums += ("{0}  {1}" -f (Sha256 $f.FullName), $f.Name) }
Set-Content -Path (Join-Path $rel "SHA256SUMS.txt") -Value $sums -Encoding ASCII

# 7. Ghi chú phát hành
if (-not $Notes) {
    $cl = Join-Path $root "CHANGELOG.md"
    if (Test-Path $cl) {
        $lines = Get-Content $cl -Encoding UTF8
        $start = ($lines | Select-String -Pattern "^## " | Select-Object -First 1).LineNumber
        if ($start) {
            $body = @()
            for ($i = $start; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^## ") { break }; if ($lines[$i].Trim()) { $body += $lines[$i].Trim() } }
            $Notes = ($body -join " ")
        }
    }
    if (-not $Notes) { $Notes = "Phiên bản $version" }
}

# 8. Manifest latest.json (tên trường PascalCase khớp lớp UpdateManifest)
if ($useGitHub) {
    # Gói đính kèm GitHub Release tag v<phiên bản>; địa chỉ manifest cố định .../releases/latest/download/latest.json (GitHub tự chuyển hướng tới Release mới nhất).
    $base = "https://github.com/$GitHubRepo/releases/download/v$version"
    $notesUrl = "https://github.com/$GitHubRepo/releases/tag/v$version"
    $manifestUrl = "https://github.com/$GitHubRepo/releases/latest/download/latest.json"
} else {
    $base = $BaseUrl.TrimEnd("/")
    $notesUrl = "$base/CHANGELOG.md"
    $manifestUrl = "$base/latest.json"
}
$manifest = [ordered]@{
    Version    = $version
    Published  = (Get-Date -Format "yyyy-MM-dd")
    MinVersion = "1.0.0"
    Mandatory  = $false
    Notes      = $Notes
    NotesUrl   = $notesUrl
    Portable   = [ordered]@{ Url = "$base/SteamConnectToolkit.exe"; Sha256 = (Sha256 (Join-Path $rel "SteamConnectToolkit.exe")); Size = (Get-Item (Join-Path $rel "SteamConnectToolkit.exe")).Length }
}
if ($hasSetup) {
    $manifest["Installer"] = [ordered]@{ Url = "$base/SteamConnectToolkit-Setup-$version.exe"; Sha256 = (Sha256 (Join-Path $rel "SteamConnectToolkit-Setup-$version.exe")); Size = (Get-Item (Join-Path $rel "SteamConnectToolkit-Setup-$version.exe")).Length }
}
$json = $manifest | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $rel "latest.json"), $json, (New-Object Text.UTF8Encoding($false)))
if (Test-Path (Join-Path $root "CHANGELOG.md")) { Copy-Item (Join-Path $root "CHANGELOG.md") $rel }

Write-Host ""
Write-Host "=== Gói phát hành $version ==="
Get-ChildItem $rel | ForEach-Object { "{0,-50} {1,12:N0} B" -f $_.Name, $_.Length }
Write-Host ""
if ($useGitHub) {
    Write-Host "Bước tiếp theo (GitHub Releases – gói không commit vào kho):"
    Write-Host "  1. git add -A; git commit -m `"release: v$version`"; git tag v$version; git push; git push --tags"
    Write-Host "  2. powershell -File tools\publish-release.ps1 -Version $version   (hoặc tải tay các file trong $rel lên https://github.com/$GitHubRepo/releases/new?tag=v$version)"
    Write-Host "Địa chỉ manifest (đã đặt mặc định trong app): $manifestUrl"
    if ($Publish) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\publish-release.ps1") -Version $version -Dir $rel -Repo $GitHubRepo
        if ($LASTEXITCODE -ne 0) { throw "Tải gói lên GitHub Release thất bại" }
    }
} else {
    Write-Host "Tải toàn bộ thư mục trên lên: $base"
    Write-Host "Địa chỉ manifest để nhập vào app (Cài đặt → Cập nhật từ xa): $manifestUrl"
}
