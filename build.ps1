# PrintFlow — سكربت بناء نسخة التجربة
#
# بيعمل تلات حاجات:
#   1) يشغّل كل التستات — لو واحد وقع، مفيش بيلد.
#   2) ينشر نسخة self-contained (مش محتاجة .NET متسطّب على جهاز المطبعة).
#   3) يطلّع نسختين: مجلد portable مضغوط + مدخلات جاهزة للـ Installer.
#
# الاستخدام:
#   .\build.ps1              → تستات + نشر + zip
#   .\build.ps1 -SkipTests   → نشر بس (للتجارب السريعة)
#   .\build.ps1 -Installer   → يشغّل Inno Setup كمان لو متسطّب

param(
    [switch]$SkipTests,
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'
$output = Join-Path $root 'Output'

# النسخة بتتقرا من الـ csproj مش مكتوبة هنا بالإيد.
# كانت مكتوبة، وفعلًا قدمت: الـ csproj بقى 1.2.1 والسكربت لسه بيقول 1.1.0،
# يعني الـ zip كان هيطلع باسم نسخة غلط.
#
# بنقراها بـ regex مش بـ [xml] عن قصد: ملفات المشروع محفوظة UTF-8 with BOM،
# وتحويلها لـ [xml] على طول بيقع أحيانًا بـ "Data at the root level is invalid".
$csproj = Join-Path $root 'src\PrintFlow.UI\PrintFlow.UI.csproj'
$version = [regex]::Match((Get-Content $csproj -Raw), '<Version>\s*([^<]+?)\s*</Version>').Groups[1].Value

if (-not $version) {
    throw "مالقيناش <Version> في $csproj"
}

# الـ .iss بيمسك نسخته بنفسه (Inno مابيعرفش يقرا csproj). بنتأكد إن الاتنين
# متطابقين هنا بدل ما الـ Installer يطلع برقم نسخة قديم من غير ما حد ياخد باله.
$iss = Join-Path $root 'PrintFlow.iss'
if (Test-Path $iss) {
    $issVersion = [regex]::Match((Get-Content $iss -Raw), '#define\s+AppVersion\s+"([^"]+)"').Groups[1].Value

    if ($issVersion -and $issVersion -ne $version) {
        throw "النسخة مش متطابقة: الـ csproj بيقول $version والـ .iss بيقول $issVersion. ظبّط الاتنين الأول."
    }
}

Write-Host "══ PrintFlow $version ══" -ForegroundColor Cyan

# ── 1) التستات
if (-not $SkipTests) {
    Write-Host "`n[1/3] تشغيل التستات..." -ForegroundColor Yellow
    dotnet test "$root\PrintFlow.sln" --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "في تستات وقعت — مفيش بيلد قبل ما تتصلّح."
    }
}
else {
    Write-Host "`n[1/3] تخطّي التستات (-SkipTests)" -ForegroundColor DarkYellow
}

# ── 2) النشر
Write-Host "`n[2/3] نشر نسخة self-contained..." -ForegroundColor Yellow

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

# self-contained عشان جهاز المطبعة مايحتاجش .NET 10 متسطّب.
# الحجم بيكبر (~150 ميجا) بس ده أهون بكتير من "البرنامج مش راضي يفتح".
dotnet publish "$root\src\PrintFlow.UI\PrintFlow.UI.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publish `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "النشر فشل." }

# SumatraPDF لازم يبقى جنب البرنامج — من غيره الطباعة مش هتشتغل
$sumatra = Join-Path $root 'tools\SumatraPDF.exe'
if (-not (Test-Path $sumatra)) {
    throw "SumatraPDF.exe مش موجود في مجلد tools. حمّله وحطه هناك الأول."
}

# ولازم يكون SumatraPDF **حقيقي**، مش أي ملف اتسمّى بالاسم ده.
#
# ليه الفحص ده موجود: فعلًا اتشحن ملف اسمه SumatraPDF.exe وهو مش هو —
# برنامج Delphi حجمه 533 ك.ب من غير أي معلومات نسخة. مكانش بيفهم -print-to
# خالص، بيرجّع كود 0 وميطبعش حاجة. الطباعة كانت "شغالة" في اللوج وميطلعش ورق.
$sumatraInfo = (Get-Item $sumatra).VersionInfo

if ($sumatraInfo.ProductName -notmatch 'SumatraPDF') {
    $size = [math]::Round((Get-Item $sumatra).Length / 1MB, 1)
    throw @"
tools\SumatraPDF.exe مش SumatraPDF حقيقي.
  ProductName = '$($sumatraInfo.ProductName)'   الحجم = $size م.ب
النسخة السليمة معلوماتها ProductName = SumatraPDF وحجمها حوالي 20 م.ب.
نزّل النسخة المحمولة 64-bit من sumatrapdfreader.org وحطها مكانه.
"@
}

Write-Host "  SumatraPDF $($sumatraInfo.ProductVersion) - OK" -ForegroundColor DarkGray

New-Item -ItemType Directory -Force -Path (Join-Path $publish 'tools') | Out-Null
Copy-Item $sumatra (Join-Path $publish 'tools') -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $publish -Force

# ── 3) الحزم
Write-Host "`n[3/3] تجهيز الحزم..." -ForegroundColor Yellow

New-Item -ItemType Directory -Force -Path $output | Out-Null

$zip = Join-Path $output "PrintFlow_Portable_$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$publish\*" -DestinationPath $zip

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "  ✓ نسخة portable: $zip ($sizeMb ميجا)" -ForegroundColor Green

if ($Installer) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($iscc) {
        & $iscc "$root\PrintFlow.iss"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ Installer: $output\PrintFlow_Setup_$version.exe" -ForegroundColor Green
        }
    }
    else {
        Write-Warning "Inno Setup 6 مش متسطّب — اتخطّينا الـ Installer. نزّله من jrsoftware.org"
    }
}

Write-Host "`nخلص. اللي المطبعة محتاجاه:" -ForegroundColor Cyan
Write-Host "  • نسخة portable  → فك الضغط وشغّل PrintFlow.UI.exe (مش محتاجة تثبيت ولا .NET)"
Write-Host "  • أو الـ Installer → لو عايز اختصارات وإلغاء تثبيت منظّم"
Write-Host "`nسجل التشغيل بيتكتب في: %AppData%\PrintFlow\logs" -ForegroundColor DarkGray
