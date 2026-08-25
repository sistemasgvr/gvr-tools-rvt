; GVR Tools — instalador comercial (Inno Setup 6+)
; Empaqueta build/2021…2027 en %ProgramData%\GVR\GvrTools\<año>\ y escribe .addin.
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
SolidCompression=no
; PDF24 ya viene comprimido: no recomprimir (ahorra minutos en cada build de prueba).
; Las DLLs sí usan lzma2.
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
Name: "revit2026"; Description: "Revit 2026"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2027"; Description: "Revit 2027"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked

[Files]
; Cada carpeta build/<año> se copia solo si el task correspondiente está marcado.
Source: "{#PayloadRoot}\2021\*"; DestDir: "{app}\2021"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2021
Source: "{#PayloadRoot}\2022\*"; DestDir: "{app}\2022"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2022
Source: "{#PayloadRoot}\2023\*"; DestDir: "{app}\2023"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2023
Source: "{#PayloadRoot}\2024\*"; DestDir: "{app}\2024"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2024
Source: "{#PayloadRoot}\2025\*"; DestDir: "{app}\2025"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2025
Source: "{#PayloadRoot}\2026\*"; DestDir: "{app}\2026"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2026
Source: "{#PayloadRoot}\2027\*"; DestDir: "{app}\2027"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2027
; PDF24 se incluye para que la instalación sea reproducible y no dependa de una descarga.
Source: "prereqs\pdf24-creator-installer.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall nocompression

[Run]
; Flags oficiales PDF24 (Inno): silent + sin auto-update + solo impresora PDF (suficiente para Revit 2021).
Filename: "{tmp}\pdf24-creator-installer.exe"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOUPDATE /COMPONENTS=pdfPrinter"; StatusMsg: "Instalando PDF24 Creator..."; Flags: waituntilterminated; Check: ShouldInstallPdf24

[Code]
var
  PrereqPage: TWizardPage;
  PrereqList: TNewMemo;
  InstallPdf24Check: TNewCheckBox;
  Pdf24InstalledAtStart: Boolean;

function RevitExeExists(Year: Integer): Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\Autodesk\Revit ' + IntToStr(Year) + '\Revit.exe'));
end;

function IsPdf24Installed: Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'SOFTWARE\PDF24') or
    RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\PDF24') or
    FileExists(ExpandConstant('{pf}\PDF24\pdf24-DocTool.exe')) or
    FileExists(ExpandConstant('{pf32}\PDF24\pdf24-DocTool.exe')) or
    DirExists(ExpandConstant('{pf}\PDF24')) or
    DirExists(ExpandConstant('{pf32}\PDF24'));
end;

function IsSpanish: Boolean;
begin
  Result := ActiveLanguage = 'spanish';
end;

procedure UpdatePrerequisitePage;
var
  RequiredText, FoundText, ActionText: string;
  Pdf24Required: Boolean;
begin
  Pdf24Required := WizardIsTaskSelected('revit2021');
  Pdf24InstalledAtStart := IsPdf24Installed;

  if Pdf24Required then
    RequiredText := 'Revit 2021'
  else if IsSpanish then
    RequiredText := 'Opcional'
  else
    RequiredText := 'Optional';

  if Pdf24InstalledAtStart then
  begin
    if IsSpanish then
    begin
      FoundText := 'Instalado';
      ActionText := 'Ya instalado';
    end
    else
    begin
      FoundText := 'Installed';
      ActionText := 'Already installed';
    end;
  end
  else
  begin
    FoundText := '';
    if Pdf24Required then
    begin
      if IsSpanish then
        ActionText := 'Debe instalarse'
      else
        ActionText := 'Must install';
    end
    else
    begin
      if IsSpanish then
        ActionText := 'Disponible'
      else
        ActionText := 'Available';
    end;
  end;

  PrereqList.Lines.Clear;
  PrereqList.Lines.Add('Name                    Required          Found             Action');
  PrereqList.Lines.Add('PDF24 Creator      ' + RequiredText + '      ' + FoundText + '      ' + ActionText);

  InstallPdf24Check.Visible := not Pdf24InstalledAtStart;
  if not Pdf24InstalledAtStart then
  begin
    InstallPdf24Check.Checked := Pdf24Required;
    InstallPdf24Check.Enabled := not Pdf24Required;
    if IsSpanish then
    begin
      if Pdf24Required then
        InstallPdf24Check.Caption := 'PDF24 es obligatorio para Revit 2021 y se instalará automáticamente.'
      else
        InstallPdf24Check.Caption := 'Instalar PDF24 Creator (opcional para Revit 2022 o posterior).';
    end
    else
    begin
      if Pdf24Required then
        InstallPdf24Check.Caption := 'PDF24 is required for Revit 2021 and will be installed automatically.'
      else
        InstallPdf24Check.Caption := 'Install PDF24 Creator (optional for Revit 2022 or later).';
    end;
  end;
end;

procedure InitializeWizard;
var
  Y: Integer;
  SelectedTasks: string;
begin
  for Y := 2021 to 2027 do
  begin
    if RevitExeExists(Y) then
    begin
      if SelectedTasks <> '' then
        SelectedTasks := SelectedTasks + ',';
      case Y of
        2021: SelectedTasks := SelectedTasks + 'revit2021';
        2022: SelectedTasks := SelectedTasks + 'revit2022';
        2023: SelectedTasks := SelectedTasks + 'revit2023';
        2024: SelectedTasks := SelectedTasks + 'revit2024';
        2025: SelectedTasks := SelectedTasks + 'revit2025';
        2026: SelectedTasks := SelectedTasks + 'revit2026';
        2027: SelectedTasks := SelectedTasks + 'revit2027';
      end;
    end;
  end;
  if SelectedTasks <> '' then
    WizardSelectTasks(SelectedTasks);

  if IsSpanish then
    PrereqPage := CreateCustomPage(
      wpSelectTasks, 'Prerequisitos', 'Comprueba los componentes necesarios antes de instalar.')
  else
    PrereqPage := CreateCustomPage(
      wpSelectTasks, 'Prerequisites', 'Review the components needed before installation.');

  PrereqList := TNewMemo.Create(PrereqPage);
  PrereqList.Parent := PrereqPage.Surface;
  PrereqList.Left := 0;
  PrereqList.Top := 0;
  PrereqList.Width := PrereqPage.SurfaceWidth;
  PrereqList.Height := ScaleY(112);
  PrereqList.ReadOnly := True;

  InstallPdf24Check := TNewCheckBox.Create(PrereqPage);
  InstallPdf24Check.Parent := PrereqPage.Surface;
  InstallPdf24Check.Left := 0;
  InstallPdf24Check.Top := PrereqList.Top + PrereqList.Height + ScaleY(16);
  InstallPdf24Check.Width := PrereqPage.SurfaceWidth;
  InstallPdf24Check.Height := ScaleY(42);

  Pdf24InstalledAtStart := IsPdf24Installed;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (PrereqPage <> nil) and (CurPageID = PrereqPage.ID) then
    UpdatePrerequisitePage;
end;

function ShouldInstallPdf24: Boolean;
begin
  Result :=
    (not Pdf24InstalledAtStart) and
    (WizardIsTaskSelected('revit2021') or InstallPdf24Check.Checked);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectTasks then
  begin
    if (not WizardIsTaskSelected('revit2021')) and
       (not WizardIsTaskSelected('revit2022')) and
       (not WizardIsTaskSelected('revit2023')) and
       (not WizardIsTaskSelected('revit2024')) and
       (not WizardIsTaskSelected('revit2025')) and
       (not WizardIsTaskSelected('revit2026')) and
       (not WizardIsTaskSelected('revit2027')) then
    begin
      MsgBox(
        'Selecciona al menos una versión de Revit.' + #13#10 +
        'Select at least one Revit version.',
        mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function WriteAddin(Year: Integer): Boolean;
var
  AddinDir, AddinPath, AssemblyPath, Content: string;
begin
  if Year >= 2027 then
    AddinDir := ExpandConstant('{pf}\Autodesk\Revit\Addins\' + IntToStr(Year))
  else
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
    if WizardIsTaskSelected('revit2026') then WriteAddin(2026);
    if WizardIsTaskSelected('revit2027') then WriteAddin(2027);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Y: Integer;
  AddinPath: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    for Y := 2021 to 2027 do
    begin
      if Y >= 2027 then
        AddinPath := ExpandConstant('{pf}\Autodesk\Revit\Addins\' + IntToStr(Y) + '\GvrTools.addin')
      else
        AddinPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + IntToStr(Y) + '\GvrTools.addin');
      if FileExists(AddinPath) then
        DeleteFile(AddinPath);
    end;
  end;
end;
