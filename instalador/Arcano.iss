; Instalador de Arcano (Inno Setup 6).
; Se compila desde scripts/publicar.ps1, que pasa la versión y la carpeta publicada:
;   ISCC /DVersion=0.2.0 /DOrigen=..\publicar\Arcano /O..\publicar instalador\Arcano.iss
;
; Instala por usuario (sin permisos de administrador) en %LOCALAPPDATA%\Programs\Arcano.
; Quien tenga permisos puede elegir instalar para todos los usuarios.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Origen
  #define Origen "..\publicar\Arcano"
#endif

[Setup]
AppId={{3E9B7C21-6A4D-4F58-B2E1-9D0C5A7F8B36}
AppName=Arcano
AppVersion={#Version}
AppVerName=Arcano {#Version}
AppPublisher=Luis Pablo Marmolejo Ramírez
AppPublisherURL=https://github.com/PabloMarmolejo/Arcano
AppSupportURL=https://github.com/PabloMarmolejo/Arcano/issues
AppComments=Generador de versiones públicas: testado irreversible de datos personales.
VersionInfoVersion={#Version}
VersionInfoDescription=Instalador de Arcano
DefaultDirName={autopf}\Arcano
DefaultGroupName=Arcano
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=Arcano-{#Version}-instalador
SetupIconFile=..\recursos\marca\arcano.ico
UninstallDisplayIcon={app}\Arcano.exe
UninstallDisplayName=Arcano
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
ChangesAssociations=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked
Name: "abrircon"; Description: "Agregar «Abrir con → Arcano» para archivos PDF y de Word"

[InstallDelete]
; La interfaz cambia de nombres de archivo en cada versión: se reemplaza completa.
Type: filesandordirs; Name: "{app}\wwwroot"

[Files]
Source: "{#Origen}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Arcano"; Filename: "{app}\Arcano.exe"; Comment: "Generador de versiones públicas"
Name: "{autodesktop}\Arcano"; Filename: "{app}\Arcano.exe"; Tasks: escritorio

[Registry]
; Trabajos .arcano (y los .testado de la versión anterior): se abren con Arcano, que abre el documento al que pertenecen.
Root: HKA; Subkey: "Software\Classes\.arcano"; ValueType: string; ValueData: "Arcano.Proyecto"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.testado"; ValueType: string; ValueData: "Arcano.Proyecto"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Arcano.Proyecto"; ValueType: string; ValueData: "Trabajo de Arcano"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Arcano.Proyecto\DefaultIcon"; ValueType: string; ValueData: """{app}\Arcano.exe"",0"
Root: HKA; Subkey: "Software\Classes\Arcano.Proyecto\shell\open\command"; ValueType: string; ValueData: """{app}\Arcano.exe"" ""%1"""

; «Abrir con»: se agrega como opción sin cambiar el programa predeterminado de PDF ni de Word.
Root: HKA; Subkey: "Software\Classes\Arcano.Documento"; ValueType: string; ValueData: "Documento para testar"; Flags: uninsdeletekey; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Arcano.Documento\DefaultIcon"; ValueType: string; ValueData: """{app}\Arcano.exe"",0"; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Arcano.Documento\shell\open\command"; ValueType: string; ValueData: """{app}\Arcano.exe"" ""%1"""; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Applications\Arcano.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Arcano"; Flags: uninsdeletekey; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Applications\Arcano.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Arcano.exe"" ""%1"""; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Applications\Arcano.exe\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Applications\Arcano.exe\SupportedTypes"; ValueType: string; ValueName: ".docx"; ValueData: ""; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Applications\Arcano.exe\SupportedTypes"; ValueType: string; ValueName: ".doc"; ValueData: ""; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Applications\Arcano.exe\SupportedTypes"; ValueType: string; ValueName: ".rtf"; ValueData: ""; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\Applications\Arcano.exe\SupportedTypes"; ValueType: string; ValueName: ".odt"; ValueData: ""; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "Arcano.Documento"; ValueData: ""; Flags: uninsdeletevalue; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\.docx\OpenWithProgids"; ValueType: string; ValueName: "Arcano.Documento"; ValueData: ""; Flags: uninsdeletevalue; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\.doc\OpenWithProgids"; ValueType: string; ValueName: "Arcano.Documento"; ValueData: ""; Flags: uninsdeletevalue; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\.rtf\OpenWithProgids"; ValueType: string; ValueName: "Arcano.Documento"; ValueData: ""; Flags: uninsdeletevalue; Tasks: abrircon
Root: HKA; Subkey: "Software\Classes\.odt\OpenWithProgids"; ValueType: string; ValueName: "Arcano.Documento"; ValueData: ""; Flags: uninsdeletevalue; Tasks: abrircon

[Run]
Filename: "{app}\Arcano.exe"; Description: "Abrir Arcano"; Flags: nowait postinstall skipifsilent
; Cuando el propio programa se actualiza (instalación silenciosa con /REABRIR), se vuelve a abrir.
Filename: "{app}\Arcano.exe"; Flags: nowait; Check: DebeReabrir

[UninstallDelete]
; El catálogo y las preferencias del usuario (%LOCALAPPDATA%\Arcano) se conservan a propósito.
Type: filesandordirs; Name: "{app}\wwwroot"

[Code]
const
  { Instalador de la versión anterior, cuando el programa se llamaba Testado. }
  AppIdTestado = '{8C5D1E5A-4F7B-4E6B-9C1E-7A3B2D9F4E10}_is1';
  WebView2Id ='{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  UrlWebView2 = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  UrlVcRedist = 'https://aka.ms/vs/17/release/vc_redist.x64.exe';

function TieneWebView2(): Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2Id, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2Id, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function TieneVcRedist(): Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\vcruntime140.dll')) and
            FileExists(ExpandConstant('{sys}\vcruntime140_1.dll')) and
            FileExists(ExpandConstant('{sys}\msvcp140.dll'));
end;

procedure Abrir(Url: String);
var
  Codigo: Integer;
begin
  ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, Codigo);
end;

function DebeReabrir(): Boolean;
begin
  Result := Pos('/REABRIR', Uppercase(GetCmdTail)) > 0;
end;

function DesinstaladorDeTestado(var Comando: String): Boolean;
var
  Clave: String;
begin
  Clave := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppIdTestado;
  Result := RegQueryStringValue(HKCU, Clave, 'UninstallString', Comando) or
            RegQueryStringValue(HKLM, Clave, 'UninstallString', Comando);
end;

{ Testado y Arcano son el mismo programa: se quita el anterior para no dejar dos accesos. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Comando: String;
  Codigo: Integer;
begin
  Result := '';
  if DesinstaladorDeTestado(Comando) then
    Exec(RemoveQuotes(Comando), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not TieneWebView2() then
  begin
    if SuppressibleMsgBox('Este equipo no tiene Microsoft Edge WebView2 Runtime, que Arcano necesita para mostrar su interfaz.' + #13#10#13#10 +
         '¿Abrir la página de descarga? Puede continuar la instalación e instalarlo después.',
         mbConfirmation, MB_YESNO, IDNO) = IDYES then
      Abrir(UrlWebView2);
  end;
  if not TieneVcRedist() then
  begin
    if SuppressibleMsgBox('Falta Microsoft Visual C++ 2015-2022 (x64). Sin él no funciona el reconocimiento de texto (OCR) de páginas escaneadas.' + #13#10#13#10 +
         '¿Descargarlo ahora? Puede continuar la instalación e instalarlo después.',
         mbConfirmation, MB_YESNO, IDNO) = IDYES then
      Abrir(UrlVcRedist);
  end;
end;
