; Inno Setup script for CodeSearch.
; Build with build-installer.ps1 (it publishes the app and passes AppVersion / PublishDir).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

#define AppName "CodeSearch"
#define AppExe "CodeSearch.exe"
#define AppUrl "https://github.com/torunf/CodeSearch"

[Setup]
AppId={{536D2A8E-D68D-4D12-B207-FBD3C7270D90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=torunf
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default (no UAC prompt); the user may still choose "all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=Output
OutputBaseFilename=CodeSearch-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.DeleteIndex=Also delete the search index and settings (%1)?%n%nThe index can be several gigabytes. Choose No to keep it for a later reinstall.
turkish.DeleteIndex=Arama indeksi ve ayarlar da silinsin mi (%1)?%n%nİndeks birkaç gigabayt olabilir. Yeniden kurulumda kullanmak için saklamak isterseniz Hayır'ı seçin.
german.DeleteIndex=Suchindex und Einstellungen ebenfalls löschen (%1)?%n%nDer Index kann mehrere Gigabyte groß sein. Wählen Sie Nein, um ihn für eine spätere Neuinstallation zu behalten.
spanish.DeleteIndex=¿Eliminar también el índice de búsqueda y los ajustes (%1)?%n%nEl índice puede ocupar varios gigabytes. Elija No para conservarlo para una reinstalación.
french.DeleteIndex=Supprimer aussi l'index de recherche et les paramètres (%1) ?%n%nL'index peut occuper plusieurs gigaoctets. Choisissez Non pour le conserver en vue d'une réinstallation.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The app keeps its index and settings in %LOCALAPPDATA%\CodeSearch of the user who ran it. }
    DataDir := ExpandConstant('{localappdata}\CodeSearch');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox(FmtMessage(CustomMessage('DeleteIndex'), [DataDir]), mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
