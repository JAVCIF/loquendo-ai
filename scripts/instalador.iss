; Instalador de Loquendo AI (Inno Setup 6). Lo compila scripts\publicar.ps1 -Instalador a partir de la carpeta portable.
; A mano: ISCC.exe /DAppVersion=1.3.0 /DSourceDir=..\artifacts\LoquendoAI_v1.3.0_portable /DOutputDir=..\artifacts instalador.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\LoquendoAI_v" + AppVersion + "_portable"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{7C0E6B3A-4F1D-4B8E-9C52-1D0A5E4C3B21}
AppName=Loquendo AI
AppVersion={#AppVersion}
AppPublisher=JAVCIF
DefaultDirName={localappdata}\Programs\Loquendo AI
DefaultGroupName=Loquendo AI
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
; «x64» works in every Inno Setup 6 (6.3+ calls it x64compatible and only warns).
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutputDir}
OutputBaseFilename=LoquendoAI_v{#AppVersion}_setup
SetupIconFile={#SourceDir}\loquendo-ai.ico
UninstallDisplayIcon={app}\LoquendoAI.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Everything from the portable folder except «portable.txt»: the installed app keeps its settings in %LOCALAPPDATA%.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "portable.txt,datos\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Loquendo AI"; Filename: "{app}\LoquendoAI.exe"
#if !FileExists(AddBackslash(SourceDir) + "worker\python\python.exe")
; Built with -SinSTT: the app installs the local transcription itself when needed; this shortcut does it by hand.
Name: "{group}\Preparar transcripción local (Python)"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\stt-setup.ps1"""
#endif
Name: "{autodesktop}\Loquendo AI"; Filename: "{app}\LoquendoAI.exe"; Tasks: desktopicon

[UninstallDelete]
; The local transcription: the embedded Python (plus the byte-code it compiles while running) and the environment
; scripts\stt-setup.ps1 creates when the package was built with -SinSTT.
Type: filesandordirs; Name: "{app}\worker\python"
Type: filesandordirs; Name: "{app}\worker\python.nuevo"
Type: filesandordirs; Name: "{app}\worker\.venv"

[Run]
Filename: "{app}\LoquendoAI.exe"; Description: "{cm:LaunchProgram,Loquendo AI}"; Flags: nowait postinstall skipifsilent
