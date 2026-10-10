# Raf'ı başka bilgisayarlara kurulabilir tek bir paket hâline getirir:
#   paket\Raf-Kurulum-<sürüm>.exe   kurulum programı (.NET dahil; hedef bilgisayarda başka bir şey gerekmez)
#   paket\Raf-<sürüm>.zip           kurulum programı + OKUBENİ.txt (gönderilecek dosya)
#
# Gerekenler: .NET 9 SDK ve Inno Setup 6 (https://jrsoftware.org/isinfo.php).
#
# Kullanım:
#   powershell -ExecutionPolicy Bypass -File paketle.ps1
$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
$out = Join-Path $repo 'paket'
$app = Join-Path $out 'app'
$project = Join-Path $repo 'src\Raf.App\Raf.App.csproj'

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 bulunamadı. https://jrsoftware.org/isinfo.php adresinden kurup yeniden dene.' }

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

# Kendi kendine yeten derleme: .NET çalışma zamanı da klasöre kopyalanır. Hata ayıklama bilgisi
# eklenmez; yoksa derleyen kişinin bilgisayarındaki klasör yolları dosyaların içine yazılırdı.
# Baştan derlenir (--no-incremental): kur.ps1'in daha önce derlediği dosyalar o yolları taşır ve
# yeniden kullanılırsa pakete girer.
Write-Host "Derleniyor (Raf $version, .NET dahil)..."
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--nologo', '-v', 'q',
    '-p:DebugType=none', '-p:DebugSymbols=false')
$buildOutput = dotnet build $project @common --no-incremental 2>&1
if ($LASTEXITCODE -eq 0) { $buildOutput = dotnet publish $project @common --no-build -o $app 2>&1 }
if ($LASTEXITCODE -ne 0) {
    $buildOutput | Write-Host
    throw 'Derleme başarısız oldu; paket hazırlanmadı.'
}
Get-ChildItem $app -Recurse -Filter *.pdb | Remove-Item -Force

# Son denetim: paketteki hiçbir dosyada bu bilgisayarın kullanıcı klasörünün yolu geçmemeli.
$latin = [Text.Encoding]::GetEncoding(28591)
$needles = @(
    $latin.GetString([Text.Encoding]::UTF8.GetBytes($env:USERPROFILE)),
    $latin.GetString([Text.Encoding]::Unicode.GetBytes($env:USERPROFILE))
)
$leaks = @(Get-ChildItem $app -Recurse -File | Where-Object {
    $text = $latin.GetString([IO.File]::ReadAllBytes($_.FullName))
    $needles | Where-Object { $text.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
})
if ($leaks.Count -gt 0) {
    throw "Şu dosyalarda bu bilgisayarın klasör yolu geçiyor, paket hazırlanmadı: $($leaks.Name -join ', ')"
}

Write-Host 'Kurulum programı hazırlanıyor...'
$setupOutput = & $iscc "/DAppVersion=$version" /Q (Join-Path $repo 'tools\kurulum\Raf.iss') 2>&1
if ($LASTEXITCODE -ne 0) {
    $setupOutput | Write-Host
    throw 'Kurulum programı derlenemedi.'
}

$setup = Join-Path $out "Raf-Kurulum-$version.exe"
$zip = Join-Path $out "Raf-$version.zip"
$stage = Join-Path $out 'zip'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $setup $stage
Copy-Item (Join-Path $repo 'tools\kurulum\OKUBENI.txt') (Join-Path $stage 'OKUBENİ.txt')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

"Hazır:"
Get-Item $setup, $zip | ForEach-Object { "  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }
