; GVR Tools — instalador comercial (Inno Setup 6+)
; Empaqueta build/2021…2025 en %ProgramData%\GVR\GvrTools\<año>\ y escribe .addin.
; Desarrollo interno: seguir usando scripts/install-addin.ps1

#define MyAppName "GVR Tools"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "GVR"
#define MyAppURL "https://github.com/sistemasgvr"
#define PayloadRoot "..\build"

[Setup]
AppId={{A7C3E2F1-9B4D-4E8A-9C1F-6D2B0A5E8F31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={commonappdata}\GVR\GvrTools
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=GvrTools-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
LicenseFile=assets\TOS.txt
ShowLanguageDialog=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "revit2021"; Description: "Revit 2021"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2022"; Description: "Revit 2022"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2023"; Description: "Revit 2023"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2024"; Description: "Revit 2024"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2025"; Description: "Revit 2025"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked

[Files]
; Cada carpeta build/<año> se copia solo si el task correspondiente está marcado.
Source: "{#PayloadRoot}\2021\*"; DestDir: "{app}\2021"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2021
Source: "{#PayloadRoot}\2022\*"; DestDir: "{app}\2022"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2022
Source: "{#PayloadRoot}\2023\*"; DestDir: "{app}\2023"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2023
Source: "{#PayloadRoot}\2024\*"; DestDir: "{app}\2024"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2024
Source: "{#PayloadRoot}\2025\*"; DestDir: "{app}\2025"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2025

[Code]
var
  Pdf24Page: TOutputMsgMemoWizardPage;

function RevitExeExists(Year: Integer): Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\Autodesk\Revit ' + IntToStr(Year) + '\Revit.exe'));
end;

function IsPdf24Installed: Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'SOFTWARE\PDF24') or
    RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\PDF24') or
    DirExists(ExpandConstant('{pf}\PDF24')) or
    DirExists(ExpandConstant('{pf32}\PDF24'));
end;

procedure InitializeWizard;
var
  Y: Integer;
begin
  for Y := 2021 to 2025 do
  begin
    if RevitExeExists(Y) then
    begin
      case Y of
        2021: WizardSelectTasks('revit2021');
        2022: WizardSelectTasks('revit2022');
        2023: WizardSelectTasks('revit2023');
        2024: WizardSelectTasks('revit2024');
        2025: WizardSelectTasks('revit2025');
      end;
    end;
  end;

  Pdf24Page := CreateOutputMsgMemoPage(
    wpSelectTasks,
    'Prerequisito PDF (Revit 2021)',
    'Revit 2021 no tiene PDF nativo',
    'Si instalas para Revit 2021 necesitas una impresora PDF silenciosa (recomendado: PDF24 Creator).',
    'Instala PDF24 desde https://www.pdf24.org y vuelve a ejecutar este instalador si falta.' + #13#10 + #13#10 +
    'Para Revit 2022+ el PDF es nativo y no hace falta PDF24.');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (Pdf24Page <> nil) and (PageID = Pdf24Page.ID) then
    Result := not WizardIsTaskSelected('revit2021');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (Pdf24Page <> nil) and (CurPageID = Pdf24Page.ID) then
  begin
    if WizardIsTaskSelected('revit2021') and (not IsPdf24Installed) then
    begin
      MsgBox(
        'PDF24 no se detectó en este PC. Instálalo antes de continuar con Revit 2021, ' +
        'o desmarca Revit 2021 en la lista de versiones.',
        mbError, MB_OK);
      Result := False;
    end;
  end;

  if CurPageID = wpSelectTasks then
  begin
    if (not WizardIsTaskSelected('revit2021')) and
       (not WizardIsTaskSelected('revit2022')) and
       (not WizardIsTaskSelected('revit2023')) and
       (not WizardIsTaskSelected('revit2024')) and
       (not WizardIsTaskSelected('revit2025')) then
    begin
      MsgBox('Selecciona al menos una versión de Revit.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function WriteAddin(Year: Integer): Boolean;
var
  AddinDir, AddinPath, AssemblyPath, Content: string;
begin
  AddinDir := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + IntToStr(Year));
  ForceDirectories(AddinDir);
  AssemblyPath := ExpandConstant('{app}\' + IntToStr(Year) + '\GvrTools.App.dll');
  AddinPath := AddinDir + '\GvrTools.addin';
  Content :=
    '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
    '<RevitAddIns>' + #13#10 +
    '  <AddIn Type="Application">' + #13#10 +
    '    <Name>GVR Tools</Name>' + #13#10 +
    '    <Assembly>' + AssemblyPath + '</Assembly>' + #13#10 +
    '    <AddInId>87c18a7a-dc0c-47af-a20d-86d2bcd59a91</AddInId>' + #13#10 +
    '    <FullClassName>GvrTools.App.GvrApplication</FullClassName>' + #13#10 +
    '    <VendorId>GVR</VendorId>' + #13#10 +
    '    <VendorDescription>GVR</VendorDescription>' + #13#10 +
    '  </AddIn>' + #13#10 +
    '</RevitAddIns>';
  Result := SaveStringToFile(AddinPath, Content, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('revit2021') then WriteAddin(2021);
    if WizardIsTaskSelected('revit2022') then WriteAddin(2022);
    if WizardIsTaskSelected('revit2023') then WriteAddin(2023);
    if WizardIsTaskSelected('revit2024') then WriteAddin(2024);
    if WizardIsTaskSelected('revit2025') then WriteAddin(2025);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Y: Integer;
  AddinPath: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    for Y := 2021 to 2025 do
    begin
      AddinPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + IntToStr(Y) + '\GvrTools.addin');
      if FileExists(AddinPath) then
        DeleteFile(AddinPath);
    end;
  end;
end;
