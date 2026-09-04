# ═══════════════════════════════════════════════════════════════════
#  الأعمدة تكبر مع الشاشة
#
#  بيعدّل ملف واحد: src\PrintFlow.UI\MainWindow.xaml
#    • تاب الرئيسية:        250 | 250 | *   ←   نسبي بحد أدنى وأقصى
#    • تاب الإعدادات العامة: 290 | 320 | *   ←   نفس القاعدة
#
#  مفيش أي كود بيتلمس — XAML بس. عدد التستات مايتغيّرش (1016).
# ═══════════════════════════════════════════════════════════════════

$ErrorActionPreference = 'Stop'
$root  = 'C:\Projects\PrintFlow'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

function Read-Src([string]$path) {
    if (-not (Test-Path $path)) { throw "مالقيتش الملف: $path" }
    $bytes  = [System.IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text   = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) { $text = $text.Substring(1) }
    [pscustomobject]@{ Text = $text; Bom = $hasBom }
}

function Write-Src([string]$path, [string]$text, [bool]$bom) {
    $enc = New-Object System.Text.UTF8Encoding($bom)
    [System.IO.File]::WriteAllText($path, $text, $enc)
}

function Set-Snippet([string]$text, [string]$anchor, [string]$replacement, [string]$what) {
    $n = ([regex]::Matches($text, [regex]::Escape($anchor))).Count
    if ($n -ne 1) {
        throw "توقّفت عند «$what»: المكان المتوقّع اتلقى $n مرة، المفروض مرة واحدة بالظبط. الملف ماتغيّرش."
    }
    return $text.Replace($anchor, $replacement)
}

Write-Host ''
Write-Host '  اقفل MainWindow.xaml في VS Code الأول (قفل، مش بس تسيبه مفتوح).' -ForegroundColor Yellow
Write-Host ''
Read-Host  '  لما تقفله اضغط Enter'

$xamlPath = Join-Path $root 'src\PrintFlow.UI\MainWindow.xaml'

$xaml = Read-Src $xamlPath
$crlf = $xaml.Text.Contains("`r`n")
$text = $xaml.Text -replace "`r`n", "`n"

if ($text.Contains('MaxWidth="420"')) {
    Write-Host ''
    Write-Host '  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
    Write-Host ''
    return
}

    $mainA = @'
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="250"/>
                            <ColumnDefinition Width="250"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
'@ -replace "`r`n", "`n"
    $mainB = @'
                        <!-- ═══ الأعمدة نسبية بحد أدنى وأقصى، مش مقاس ثابت ═══

                             كانت 250 و250 ثابتين. النافذة اتصممت على عرض 1080،
                             فلما المستخدم بيكبّرها لـ 1920 العمودين دول بيفضلوا
                             250 زي ما هما، وكل الزيادة بتروح لقايمة الملفات —
                             اللي ممكن يكون فيها ملف واحد.

                             النتيجة كانت: "كل مكنة تطبع العدد كامل (من غير تق..."
                             مقصوصة، وسطرَي الورق والتكلفة تحت الطي محتاجين scroll
                             عشان توصلهم — وهما بالظبط الرقمين اللي المفروض تبصلهم
                             قبل ما تضغط طباعة.

                             MinWidth = مايبقاش أوحش من اللي كان.
                             MaxWidth = مايبقاش سخيف على شاشة عريضة (خانة اختيار
                             بعرض نص شاشة مش أحسن، هي بس فاضية أكتر). -->
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" MinWidth="250" MaxWidth="420"/>
                            <ColumnDefinition Width="*" MinWidth="250" MaxWidth="420"/>
                            <ColumnDefinition Width="1.4*" MinWidth="300"/>
                        </Grid.ColumnDefinitions>
'@ -replace "`r`n", "`n"
    $setA = @'
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="290"/>
                        <ColumnDefinition Width="320"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
'@ -replace "`r`n", "`n"
    $setB = @'
                    <!-- نفس قاعدة تاب الرئيسية: نسبي بحد أدنى وأقصى.
                         العمود التالت فيه المعاينة، ومقاسها ثابت 300×424 —
                         فأي زيادة فوق كده بتبقى بياض. -->
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" MinWidth="290" MaxWidth="460"/>
                        <ColumnDefinition Width="*" MinWidth="320" MaxWidth="480"/>
                        <ColumnDefinition Width="*" MinWidth="330"/>
                    </Grid.ColumnDefinitions>
'@ -replace "`r`n", "`n"

$text = Set-Snippet $text $mainA $mainB 'أعمدة تاب الرئيسية'
$text = Set-Snippet $text $setA  $setB  'أعمدة تاب الإعدادات العامة'

if ($crlf) { $text = $text -replace "`n", "`r`n" }

Copy-Item $xamlPath "$xamlPath.$stamp.bak"
Write-Src $xamlPath $text $xaml.Bom

Write-Host ''
Write-Host '  MainWindow.xaml  →  اتعدّل.' -ForegroundColor Green
Write-Host ''

$after = (Read-Src $xamlPath).Text
$ok = $after.Contains('MaxWidth="420"') -and $after.Contains('MaxWidth="480"')

if ($ok) {
    Write-Host '    [تمام]  أعمدة الرئيسية'        -ForegroundColor Green
    Write-Host '    [تمام]  أعمدة الإعدادات العامة' -ForegroundColor Green
    Write-Host ''
    Write-Host '  شغّل دلوقتي:  .\build.ps1'  -ForegroundColor Green
    Write-Host '  المتوقّع: 1016 تست، صفر فشل (مفيش تستات جديدة — ده تعديل شكل بس).' -ForegroundColor Green
    Write-Host ''
    Write-Host "  النسخة الاحتياطية: MainWindow.xaml.$stamp.bak — امسحها بعد ما البناء ينجح." -ForegroundColor DarkGray
}
else {
    Write-Host '  في حاجة ناقصة — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
