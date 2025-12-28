; KaiROS AI Installer Script for Inno Setup
; This creates a professional Windows installer with silent install support

#define MyAppName "KaiROS AI"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "KaiROS"
#define MyAppURL "https://github.com/YOUR_USERNAME/KaiROS.AI"
#define MyAppExeName "KaiROS.AI.exe"
#define MyAppDescription "Local AI Assistant - Run LLMs privately on your own hardware"

[Setup]
; Unique App ID - DO NOT CHANGE after first release
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Output settings
OutputDir=..\artifacts
OutputBaseFilename=KaiROS.AI-{#MyAppVersion}-Setup
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
; UI settings
WizardStyle=modern
SetupIconFile=..\KaiROS.AI\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
; Privileges
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; Silent install support (MS Store requirement)
; Use: /VERYSILENT /SUPPRESSMSGBOXES for silent install

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application files
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Custom code for uninstall cleanup
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  LocalAppData: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean up app data folder
    LocalAppData := ExpandConstant('{localappdata}\KaiROS.AI');
    if DirExists(LocalAppData) then
    begin
      if MsgBox('Do you want to remove your chat history and settings?', mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(LocalAppData, True, True, True);
      end;
    end;
  end;
end;
