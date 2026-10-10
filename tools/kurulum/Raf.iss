; Raf kurulum programı (Inno Setup 6). paketle.ps1 tarafından derlenir; elle derlemek için:
;   ISCC.exe /DAppVersion=1.0.0 tools\kurulum\Raf.iss
;
; Kurulum yönetici izni istemez: her şey o kullanıcının kendi alanına kurulur. Program dosyaları
; paket\app klasöründen alınır (.NET dahil, kendi kendine yeten derleme).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{8C2B6F0E-5D0B-4B55-9B7E-52A1F3C7D9A1}
AppName=Raf
AppVersion={#AppVersion}
AppPublisher=Raf
DefaultDirName={localappdata}\Programs\Raf
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\paket
OutputBaseFilename=Raf-Kurulum-{#AppVersion}
SetupIconFile=..\..\src\Raf.App\Assets\raf.ico
UninstallDisplayIcon={app}\Raf.exe
UninstallDisplayName=Raf
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Raf tepside penceresiz çalışır; kullanıcıdan "kapatın" diye istemek yerine aşağıdaki kod kapatır.
CloseApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "autostart"; Description: "Windows açılınca Raf da başlasın (önerilir)"; GroupDescription: "Ek ayarlar:"

[Files]
Source: "..\..\paket\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Raf"; Filename: "{app}\Raf.exe"

[Registry]
; Raf'ın Ayarlar sayfasındaki "Windows açılınca başlasın" anahtarı da aynı değeri okur ve yazar.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Raf"; ValueData: """{app}\Raf.exe"" --tray"; Tasks: autostart

[Run]
Filename: "{app}\Raf.exe"; Description: "Raf'ı şimdi başlat"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure StopRaf;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Raf.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Güncelleme sırasında çalışan Raf dosyalarını kilitler; kopyalamadan önce kapatılır. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRaf;
  Result := '';
end;

{ Kaldırırken Raf kapatılır ve açılışta başlatma kaydı silinir (kayıt kurulumda değil, sonradan
  Ayarlar'dan açılmış olabilir). Kullanıcının dosyalarına ve ayarlarına dokunulmaz. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRaf;
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'Raf');
  end;
end;
