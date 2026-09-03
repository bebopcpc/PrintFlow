; PrintFlow — سكربت التثبيت
; مهم: الملف ده لازم يفضل محفوظ UTF-8 **مع BOM**، وإلا Inno Setup هيقرا
; العربي غلط ويطلع رموز زي ����� (ده اللي كان حاصل في النسخة القديمة).

#define AppName "PrintFlow"
#define AppVersion "1.9.9"
; غيّر الناشر لاسمك أو اسم المطبعة — SwiftByte شركة تانية
#define AppPublisher "PrintFlow"
#define AppExe "PrintFlow.UI.exe"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=PrintFlow_Setup_{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
DisableProgramGroupPage=yes

; معلومات النسخة اللي بتظهر في Properties → Details على ملف الـ Setup نفسه.
;
; ليه اتضافت: ملف تثبيت من Inno من غير معلومات نسخة خانته كلها فاضية،
; ومحركات فحص الفيروسات بتقرا الخانات دي. الملف المجهول تمامًا بياخد
; درجة شك أعلى من الملف اللي مكتوب فيه مين عمله وإيه هو.
;
; بالإنجليزي عن قصد — دي بتتقرا بأدوات وناس من كل الدنيا، والعربي في
; مورد النسخة بيطلع مقروء غلط في بعض الأدوات القديمة.
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup - print shop management
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "اختصارات:"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\إلغاء تثبيت {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "تشغيل {#AppName} الآن"; Flags: nowait postinstall skipifsilent

; ملاحظة: إعدادات المستخدم والإعدادات المسبقة وسجل التشغيل بيعيشوا في
; %AppData%\PrintFlow وعن قصد **مش** بيتمسحوا عند إلغاء التثبيت،
; عشان لو المستخدم عمل تحديث مايفقدش شغله.
;
; ملاحظة تانية: ملفات رخصة SumatraPDF (AUTHORS و COPYING و COPYING.BSD)
; بتيجي لوحدها جوه publish\tools — build.ps1 بيحطها هناك، والسطر
; "publish\*" مع recursesubdirs بياخدها. مش محتاجة سطر منفصل هنا.
