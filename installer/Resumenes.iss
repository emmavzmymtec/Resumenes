#define MyAppName "Resúmenes de Estudio"
#define MyAppExe "Resumenes.Ui.exe"
#define MyAppVersion "1.3.0"

[Setup]
AppId={{8F3A2C7E-5B1D-4E9A-9C2F-RES0MENES001}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\ResumenesApp
DefaultGroupName=Resúmenes de Estudio
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ResumenesSetup
Compression=lzma2/max
SolidCompression=yes
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile=..\src\Resumenes.Ui\Recursos\app.ico

[Files]
; App publicada (framework-dependent)
Source: "..\publish\app\*"; DestDir: "{app}"; Excludes: "workspace\*,*.sqlite,*.sqlite-shm,*.sqlite-wal"; Flags: recursesubdirs createallsubdirs ignoreversion
; Scripts y fuentes (livianos, van en el instalador)
Source: "..\runtime\scripts\*"; DestDir: "{app}\runtime\scripts"; Flags: recursesubdirs ignoreversion
Source: "..\runtime\fonts\*"; DestDir: "{app}\runtime\fonts"; Flags: recursesubdirs ignoreversion
; settings de instalación -> config\settings.json
Source: "..\config\settings.instalacion.json"; DestDir: "{app}\config"; DestName: "settings.json"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Adicional:"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Iniciar {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  NetUrl = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe';

function NetRuntimePresente(): Boolean;
var
  ResultCode: Integer;
  Salida: AnsiString;
  TmpFile: String;
begin
  // Corre 'dotnet --list-runtimes' y busca 'Microsoft.WindowsDesktop.App 9.'
  TmpFile := ExpandConstant('{tmp}\runtimes.txt');
  Result := False;
  if Exec(ExpandConstant('{cmd}'), '/C dotnet --list-runtimes > "' + TmpFile + '" 2>&1', '',
          SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(TmpFile, Salida) then
      Result := Pos('Microsoft.WindowsDesktop.App 9.', Salida) > 0;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  DownloadPage: TDownloadWizardPage;
  ResultCode: Integer;
  Instalador: String;
begin
  if CurStep = ssInstall then
  begin
    if not NetRuntimePresente() then
    begin
      DownloadPage := CreateDownloadPage('Runtime .NET 9', 'Descargando el runtime necesario…', nil);
      DownloadPage.Clear;
      DownloadPage.Add(NetUrl, 'windowsdesktop-runtime-9-win-x64.exe', '');
      DownloadPage.Show;
      try
        DownloadPage.Download;
        Instalador := ExpandConstant('{tmp}\windowsdesktop-runtime-9-win-x64.exe');
        if (not Exec(Instalador, '/install /quiet /norestore', '', SW_SHOW, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
          MsgBox('No se pudo instalar el runtime .NET 9 (código ' + IntToStr(ResultCode) + '). La aplicación podría no iniciarse. Instalá .NET 9 Desktop Runtime manualmente desde https://dotnet.microsoft.com', mbError, MB_OK);
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;
