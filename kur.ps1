# Raf'ı derler ve bu kullanıcı için kurar (yönetici izni gerekmez):
#   - program dosyaları:   %LOCALAPPDATA%\Programs\Raf
#   - Başlat menüsü kısayolu
#   - Windows açılışında başlatma (ilk kurulumda açılır; sonraki kurulumlarda Ayarlar'daki seçimin korunur)
#   - Ayarlar > Uygulamalar > Yüklü uygulamalar listesinde "Raf" kaydı
# Dosyalarına (%USERPROFILE%\Raf) ve ayarlarına (%APPDATA%\Raf) dokunmaz.
#
# Kullanım (kodda değişiklik yaptıktan sonra da aynı komut):
#   powershell -ExecutionPolicy Bypass -File kur.ps1
$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
$dist = Join-Path $repo 'dist'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Raf'
$exe = Join-Path $target 'Raf.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Raf'

function Stop-Raf([string]$folder) {
    Get-Process Raf -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$folder\*" } |
        ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
}

# Derleme çıktısı klasöründen çalışan bir kopya varsa (geliştirme sırasında açılmış) dosyaları kilitler.
Stop-Raf $dist

Write-Host 'Derleniyor...'
$buildOutput = dotnet publish (Join-Path $repo 'src\Raf.App\Raf.App.csproj') -c Release -o $dist --nologo -v q 2>&1
if ($LASTEXITCODE -ne 0) {
    $buildOutput | Write-Host
    throw 'Derleme başarısız oldu; kurulum yapılmadı.'
}

$wasInstalled = Test-Path $exe
$hadAutostart = $null -ne (Get-ItemProperty $runKey -Name Raf -ErrorAction SilentlyContinue)

# Kurulu Raf çalışırken dosyaları kilitlidir; derleme başarılı olduktan sonra, kopyalamadan hemen önce kapatılır.
Stop-Raf $target

Write-Host "Kopyalanıyor: $target"
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $dist '*') $target -Recurse -Force
Copy-Item (Join-Path $repo 'kaldir.ps1') $target -Force

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Raf.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'Raf'
$shortcut.Save()

# Açılışta başlatma Raf'ın Ayarlar sayfasından kapatıldıysa yeniden açılmaz; açıksa yolu yeni yere çevrilir.
if ($hadAutostart -or -not $wasInstalled) {
    Set-ItemProperty $runKey -Name Raf -Value "`"$exe`" --tray"
}

$version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
$sizeKb = [int]((Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
$uninstall = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $target 'kaldir.ps1')`""
New-Item $uninstallKey -Force | Out-Null
Set-ItemProperty $uninstallKey -Name DisplayName -Value 'Raf'
Set-ItemProperty $uninstallKey -Name DisplayVersion -Value $version
Set-ItemProperty $uninstallKey -Name Publisher -Value 'Raf'
Set-ItemProperty $uninstallKey -Name DisplayIcon -Value $exe
Set-ItemProperty $uninstallKey -Name InstallLocation -Value $target
Set-ItemProperty $uninstallKey -Name UninstallString -Value $uninstall
Set-ItemProperty $uninstallKey -Name EstimatedSize -Value $sizeKb -Type DWord
Set-ItemProperty $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty $uninstallKey -Name NoRepair -Value 1 -Type DWord

Start-Process $exe -ArgumentList '--tray'
Write-Host "Raf $version kuruldu ve arka planda çalışıyor."
