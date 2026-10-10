# Raf'ı bu kullanıcıdan kaldırır: program dosyaları, Başlat menüsü kısayolu, açılışta başlatma
# ve "Yüklü uygulamalar" kaydı. Dosyalarına (%USERPROFILE%\Raf) ve ayarlarına (%APPDATA%\Raf) dokunmaz.
# Windows'un "Yüklü uygulamalar" listesindeki "Kaldır" da bu betiği çalıştırır.
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\Raf'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Raf'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Raf.lnk'

Get-Process Raf -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$target\*" } |
    ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }

Remove-ItemProperty $runKey -Name Raf -ErrorAction SilentlyContinue
if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }
if (Test-Path $uninstallKey) { Remove-Item $uninstallKey -Recurse -Force }

# Yalnızca gerçekten Raf'ın kurulum klasörüyse silinir. Bu betik o klasörün içinden çalışıyor
# olabileceği için silme, betik bittikten sonra çalışacak ayrı bir komuta bırakılır.
if ((Test-Path (Join-Path $target 'Raf.exe')) -and $target.EndsWith('\Programs\Raf')) {
    Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c', "timeout /t 2 /nobreak >nul & rmdir /s /q `"$target`""
}

Write-Host "Raf kaldırıldı. Dosyaların ($env:USERPROFILE\Raf) ve ayarların ($env:APPDATA\Raf) yerinde duruyor."
