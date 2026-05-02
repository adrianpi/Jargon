; Jargon Installer Script for Inno Setup 6+
; https://jrsoftware.org/isinfo.php
;
; Build this installer:
;   1. Install Inno Setup 6 from https://jrsoftware.org/isdl.php
;   2. Build the Jargon solution in Release|x64 configuration
;   3. Open this script in Inno Setup Compiler and click Build

#define MyAppName "Jargon"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Adrian Pi"
#define MyAppURL "https://github.com/adrianpi/Jargon"

; Root of the Jargon repository (parent of this Installer folder)
#define RepoRoot ".."
#define BinDir RepoRoot + "\x64\Release"

[Setup]
AppId={{E8A3F2B1-7C4D-4A5E-9B6F-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile={#RepoRoot}\LICENSE.txt
OutputDir=Output
OutputBaseFilename=JargonSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation (compiler only)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "compiler"; Description: "Jargon Compiler (jlc1.exe)"; Types: full compact custom; Flags: fixed
Name: "runtime"; Description: "Jargon Runtime Library"; Types: full compact custom; Flags: fixed
Name: "stdlib"; Description: "Standard Library Sources (.jr files)"; Types: full custom
Name: "templates"; Description: "Template Instantiation Files (.jrt)"; Types: full custom

[Tasks]
Name: "addtopath"; Description: "Add Jargon to the system PATH"; GroupDescription: "Environment:"; Flags: checkedonce

[Files]
; Compiler
Source: "{#BinDir}\jlc1.exe"; DestDir: "{app}\bin"; Components: compiler; Flags: ignoreversion

; Runtime
Source: "{#BinDir}\Jargon.dll"; DestDir: "{app}\bin"; Components: runtime; Flags: ignoreversion
Source: "{#BinDir}\Jargon.lib"; DestDir: "{app}\lib"; Components: runtime; Flags: ignoreversion
Source: "{#BinDir}\Startup.lib"; DestDir: "{app}\lib"; Components: runtime; Flags: ignoreversion

; Standard library sources
Source: "{#RepoRoot}\Jargon\jargon.jr"; DestDir: "{app}\lib"; Components: stdlib; Flags: ignoreversion
Source: "{#RepoRoot}\Startup\startup.jr"; DestDir: "{app}\lib"; Components: stdlib; Flags: ignoreversion

; Template instantiation files
Source: "{#BinDir}\*.jrt"; DestDir: "{app}\lib"; Components: templates; Flags: ignoreversion

; License
Source: "{#RepoRoot}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Jargon Documentation"; Filename: "{#MyAppURL}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Registry]
; Set JARGON_LIB environment variable to point to the lib folder
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: string; ValueName: "JARGON_LIB"; ValueData: "{app}\lib"; \
    Flags: preservestringtype uninsdeletevalue

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

procedure AddToPath();
var
  CurrentPath: string;
  BinPath: string;
begin
  BinPath := ExpandConstant('{app}\bin');
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath) then
  begin
    // Check if already in PATH
    if Pos(Uppercase(BinPath), Uppercase(CurrentPath)) = 0 then
    begin
      // Append to PATH
      if (CurrentPath <> '') and (CurrentPath[Length(CurrentPath)] <> ';') then
        CurrentPath := CurrentPath + ';';
      CurrentPath := CurrentPath + BinPath;
      RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath);
    end;
  end;
end;

procedure RemoveFromPath();
var
  CurrentPath: string;
  BinPath: string;
  P: Integer;
begin
  BinPath := ExpandConstant('{app}\bin');
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath) then
  begin
    P := Pos(Uppercase(BinPath), Uppercase(CurrentPath));
    if P > 0 then
    begin
      Delete(CurrentPath, P, Length(BinPath));
      // Clean up extra semicolons
      if (P > 1) and (P <= Length(CurrentPath)) and (CurrentPath[P] = ';') then
        Delete(CurrentPath, P, 1)
      else if (P > 1) and (CurrentPath[P - 1] = ';') then
        Delete(CurrentPath, P - 1, 1);
      RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if IsTaskSelected('addtopath') then
      AddToPath();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath();
end;
