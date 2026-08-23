[Setup]
AppName=PrintFlow
AppVersion=1.0
DefaultDirName={autopf}\PrintFlow
DefaultGroupName=PrintFlow
OutputDir=Output
OutputBaseFilename=PrintFlow_Setup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PrintFlow"; Filename: "{app}\PrintFlow.UI.exe"
Name: "{commondesktop}\PrintFlow"; Filename: "{app}\PrintFlow.UI.exe"

[Run]
Filename: "{app}\PrintFlow.UI.exe"; Description: " ‘€Ì· PrintFlow «·¬‰"; Flags: nowait postinstall skipifsilent