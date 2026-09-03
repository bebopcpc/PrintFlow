# PrintFlow — سكربت بناء نسخة التجربة
#
# بيعمل أربع حاجات:
#   1) يشغّل كل التستات — لو واحد وقع، مفيش بيلد.
#   2) ينشر نسخة self-contained (مش محتاجة .NET متسطّب على جهاز المطبعة).
#   3) يطلّع نسختين: مجلد portable مضغوط + مدخلات جاهزة للـ Installer.
#   4) (اختياري) يبني حزمة MSIX لمتجر مايكروسوفت.
#
# الاستخدام:
#   .\build.ps1              → تستات + نشر + zip
#   .\build.ps1 -SkipTests   → نشر بس (للتجارب السريعة)
#   .\build.ps1 -Installer   → يشغّل Inno Setup كمان لو متسطّب
#   .\build.ps1 -Msix        → يطلّع كمان حزمة .msix للمتجر

param(
    [switch]$SkipTests,
    [switch]$Installer,
    [switch]$Msix
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
#
# (بيان الـ MSIX مالوش الفحص ده لأننا بنحقن فيه النسخة أوتوماتيك تحت —
#  الحقن أأمن من الفحص، لأنه بيمنع الاختلاف بدل ما يمسكه بعد ما يحصل.)
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

# ومش بس اسمه صح — لازم يكون **موقّع من الناشر الحقيقي**.
#
# الفحص اللي فوق بيقرا خانة ProductName وبس، وأي حد يقدر يكتب فيها أي
# كلام. الملف المزيّف اللي عدّى علينا قبل كده اتمسك بالصدفة لأن خانته
# كانت فاضية — لو كان كاتب فيها SumatraPDF كان عدّى وطبعنا بيه.
#
# التوقيع الرقمي هو اللي مايتزوّرش. النسخة الرسمية موقّعة بشهادة
# Certum باسم Krzysztof Kowalczyk (مطوّر SumatraPDF).
$sig = Get-AuthenticodeSignature $sumatra

if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Kowalczyk') {
    throw @"
tools\SumatraPDF.exe مش موقّع من الناشر الحقيقي.
  حالة التوقيع = $($sig.Status)
  الموقّع       = $($sig.SignerCertificate.Subject)
المفروض: Valid + CN=Krzysztof Kowalczyk (شهادة Certum Code Signing).
البيلد وقف. نزّل النسخة الرسمية 64-bit من sumatrapdfreader.org.
"@
}

Write-Host "  SumatraPDF $($sumatraInfo.ProductVersion) - OK (موقّع)" -ForegroundColor DarkGray

New-Item -ItemType Directory -Force -Path (Join-Path $publish 'tools') | Out-Null
Copy-Item $sumatra (Join-Path $publish 'tools') -Force

# ملفات رخصة SumatraPDF — لازم تتوزّع جنب الملف التنفيذي.
#
# مطوّر SumatraPDF قال إن التوزيع التجاري مسموح **بشرط** إرفاق
# AUTHORS و COPYING و COPYING.BSD. من غيرهم التوزيع مخالف للرخصة،
# ومراجعة متجر مايكروسوفت بتفحص ده.
#
# throw مش warning عن قصد: ده شرط قانوني مش تحسين.
foreach ($lic in 'AUTHORS', 'COPYING', 'COPYING.BSD') {
    $src = Join-Path $root "tools\$lic"

    if (-not (Test-Path $src)) {
        throw "ملف رخصة SumatraPDF ناقص: tools\$lic"
    }

    Copy-Item $src (Join-Path $publish 'tools') -Force
}

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
    # بندوّر على 7 الأول وبعدين 6. كان مكتوب 6 بس، فلما اتسطّبت 7 السكربت
    # قال "مش متسطّب" وعدّى من غير Installer — والمستخدم فاكر إنه اتعمل.
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($iscc) {
        & $iscc "$root\PrintFlow.iss"

        # الفشل الصامت أخطر من الصريح: من غير الرمي ده، لو الـ .iss وقع
        # السكربت كان بيكمّل ويقول "خلص" والـ Setup مش موجود أصلًا.
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup وقع (كود $LASTEXITCODE). شوف الرسايل فوق."
        }

        Write-Host "  ✓ Installer: $output\PrintFlow_Setup_$version.exe" -ForegroundColor Green
    }
    else {
        Write-Warning "Inno Setup مش متسطّب — اتخطّينا الـ Installer. نزّله من jrsoftware.org"
    }
}

# ── حزمة المتجر (اختيارية)
#
# ليه حزمة تانية غير الـ Installer أصلًا:
#
# Smart App Control في ويندوز ١١ بتفحص كل ملف .dll قبل ما يتحمّل، ولو
# مش موقّع رقميًا بتمنعه — **ومفيش زرار "Run anyway"** زي SmartScreen.
# يعني على جهاز ويندوز ١١ متنصّب نضيف، البرنامج بيفتح والـ DLL بتاعنا
# بيتمنع، والنتيجة برنامج نصّه شغال.
#
# حزمة المتجر بتعدّي الفحص ده لأن **مايكروسوفت** هي اللي بتوقّعها بعد
# المراجعة — من غير ما نشتري شهادة توقيع.
#
# بنبني في مجلد منفصل عن publish عشان نسخة الـ Inno والـ portable تفضل
# نضيفة من ملفات الحزمة (البيان والأيقونات مالهمش لازمة فيهم).
$msixMissing = $null

if ($Msix) {
    Write-Host "`n[إضافي] بناء حزمة MSIX للمتجر..." -ForegroundColor Yellow

    $msixSource = Join-Path $root 'msix'
    $appxManifest = Join-Path $msixSource 'AppxManifest.xml'

    if (-not (Test-Path $appxManifest)) {
        throw "msix\AppxManifest.xml مش موجود."
    }
    if (-not (Test-Path (Join-Path $msixSource 'Assets'))) {
        throw "مجلد msix\Assets مش موجود (الأيقونات)."
    }

    $makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -EA SilentlyContinue |
                Sort-Object FullName |
                Select-Object -Last 1 -ExpandProperty FullName

    if (-not $makeappx) { throw "makeappx.exe مش موجود — نزّل Windows SDK." }

    $stage = Join-Path $root 'publish-msix'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

    Copy-Item $publish $stage -Recurse -Force
    Copy-Item (Join-Path $msixSource 'Assets') $stage -Recurse -Force

    # بنحقن رقم النسخة من الـ csproj بدل ما يفضل مكتوب بالإيد في البيان.
    # ده نفس الفخ بتاع ملف .iss: نسخة قديمة نايمة في ملف ومحدش واخد باله.
    # الـ regex محصور جوه <Identity> عن قصد عشان مايلمسش MinVersion
    # و MaxVersionTested اللي في <Dependencies>.
    #
    # المتجر بيطلب أربع خانات والأخيرة **صفر** (محجوزة ليه).
    $manifest = Get-Content $appxManifest -Raw
    $manifest = $manifest -replace '(<Identity[\s\S]*?Version=")[\d.]+(")', "`${1}$version.0`${2}"
    Set-Content (Join-Path $stage 'AppxManifest.xml') $manifest -Encoding UTF8

    $msixPath = Join-Path $output "PrintFlow_$version.msix"
    if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

    & $makeappx pack /d $stage /p $msixPath /o
    if ($LASTEXITCODE -ne 0) { throw "بناء MSIX فشل (كود $LASTEXITCODE)." }

    $msixMb = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
    Write-Host "  ✓ حزمة المتجر: $msixPath ($msixMb ميجا)" -ForegroundColor Green
}

Write-Host "`nخلص. اللي المطبعة محتاجاه:" -ForegroundColor Cyan
Write-Host "  • نسخة portable  → فك الضغط وشغّل PrintFlow.UI.exe (مش محتاجة تثبيت ولا .NET)"
Write-Host "  • أو الـ Installer → لو عايز اختصارات وإلغاء تثبيت منظّم"

if ($Msix) {
    Write-Host "  • أو حزمة .msix  → ارفعها في Partner Center **من غير توقيع**؛" -ForegroundColor Cyan
    Write-Host "                     المتجر بيوقّعها بنفسه بعد المراجعة." -ForegroundColor DarkGray
}

Write-Host "`nسجل التشغيل بيتكتب في: %AppData%\PrintFlow\logs" -ForegroundColor DarkGray
