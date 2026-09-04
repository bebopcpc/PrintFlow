# ═══════════════════════════════════════════════════════════════════
#  تحذير: الكتيّب من غير طباعة على الوجهين
#
#  بيضيف حتّتين بس:
#    1) src\PrintFlow.Presentation\MainViewModel.cs
#         • خاصيتين جداد: BookletDuplexWarning و BookletDuplexIsActive
#         • تحديثهم لما الكتيّب أو الوجهين يتغيّر
#    2) src\PrintFlow.UI\MainWindow.xaml
#         • سطر أحمر تحت خيار الكتيّب
#
#  بيحسب كل حاجة الأول، وبيكتب في آخر خطوة. لو أي مكان مش زي ما هو
#  متوقّع، بيقف من غير ما يلمس أي ملف خالص.
#
#  بياخد نسخة احتياطية من كل ملف بيغيّره، وبيشتغل مرة واحدة بس —
#  لو اتشغّل تاني بيقول "متعمول قبل كده" ويسيبه.
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

function Add-Snippet([string]$text, [string]$anchor, [string]$addition, [string]$what) {
    $n = ([regex]::Matches($text, [regex]::Escape($anchor))).Count
    if ($n -ne 1) {
        throw "توقّفت عند «$what»: المكان المتوقّع اتلقى $n مرة، المفروض مرة واحدة بالظبط. مافيش أي ملف اتغيّر."
    }
    return $text.Replace($anchor, $anchor + $addition)
}

Write-Host ''
Write-Host '  اقفل التابين دول في VS Code الأول (قفل، مش بس تسيبهم مفتوحين):' -ForegroundColor Yellow
Write-Host '     MainViewModel.cs'  -ForegroundColor Yellow
Write-Host '     MainWindow.xaml'   -ForegroundColor Yellow
Write-Host ''
Read-Host  '  لما تقفلهم اضغط Enter'

$vmPath   = Join-Path $root 'src\PrintFlow.Presentation\MainViewModel.cs'
$xamlPath = Join-Path $root 'src\PrintFlow.UI\MainWindow.xaml'

# ═══════════ 1) MainViewModel.cs — الحساب ═══════════

$vm      = Read-Src $vmPath
$vmCrLf  = $vm.Text.Contains("`r`n")
$vmText  = $vm.Text -replace "`r`n", "`n"
$vmDone  = $vmText.Contains('BookletDuplexWarning')

if (-not $vmDone) {

    $anchor1 = @'
            if (e.PropertyName is nameof(PrintSettings.BookletMode)
                or nameof(PrintSettings.BookletStart))
            {
                RefreshBookletSummary();
            }
'@ -replace "`r`n", "`n"

    $add1 = @'


            // الكتيّب والوجهين قرار واحد متقسّم على مربعين في مجموعتين
            // مختلفتين. أي واحد فيهم يتغيّر، السطر الأحمر لازم يتحدّث.
            if (e.PropertyName is nameof(PrintSettings.BookletMode)
                or nameof(PrintSettings.Duplex))
            {
                OnPropertyChanged(nameof(BookletDuplexWarning));
                OnPropertyChanged(nameof(BookletDuplexIsActive));
            }
'@ -replace "`r`n", "`n"

    $anchor2 = '    public bool PageRangeIsActive => PageRange.IsSubset(Settings.PageFrom, Settings.PageTo);'

    $add2 = @'


    /// <summary>
    /// تحذير الكتيّب من غير وجهين.
    ///
    /// الكتيّب بيعيد ترتيب الصفحات على أساس إن الورقة هتتطبع من الوجهين
    /// وتتطوي. لو الوجهين مقفول، كل وش بيروح على ورقة لوحده — الورق
    /// ضِعف اللازم ونُصّه فاضي، والطي مابيدّيش كتيّب.
    ///
    /// ⚠ والاتنين في مجموعتين مختلفتين في الواجهة: الكتيّب في "خيارات
    /// البوكليت" والوجهين في "خيارات الطباعة" تحتها. فاللي بيفتح الكتيّب
    /// مش شايف حالة الوجهين قدامه أصلًا.
    ///
    /// القرار نفسه في <see cref="BookletRules"/> — مش مكتوب هنا — عشان
    /// يفضل مصدر واحد لو احتجناه في مكان تاني.
    /// </summary>
    public string BookletDuplexWarning => BookletRules.Describe(Settings);

    /// <summary>في مشكلة؟ ده اللي بيظهّر السطر ويخفيه.</summary>
    public bool BookletDuplexIsActive => BookletRules.NeedsDuplex(Settings);
'@ -replace "`r`n", "`n"

    $vmText = Add-Snippet $vmText $anchor1 $add1 'مكان تحديث الإشعار في MainViewModel.cs'
    $vmText = Add-Snippet $vmText $anchor2 $add2 'مكان الخاصيتين الجداد في MainViewModel.cs'

    if ($vmCrLf) { $vmText = $vmText -replace "`n", "`r`n" }
}

# ═══════════ 2) MainWindow.xaml — الحساب ═══════════

$xaml     = Read-Src $xamlPath
$xamlCrLf = $xaml.Text.Contains("`r`n")
$xamlText = $xaml.Text -replace "`r`n", "`n"
$xamlDone = $xamlText.Contains('BookletDuplexWarning')

if (-not $xamlDone) {

    $key = '{Binding BookletSummary}'
    $n   = ([regex]::Matches($xamlText, [regex]::Escape($key))).Count
    if ($n -ne 1) {
        throw "توقّفت: «$key» في MainWindow.xaml اتلقى $n مرة، المفروض مرة واحدة. مافيش أي ملف اتغيّر."
    }

    # بندوّر على نهاية العنصر نفسه — أول "/>" بعد السطر ده
    $i   = $xamlText.IndexOf($key)
    $end = $xamlText.IndexOf('/>', $i)
    if ($end -lt 0) { throw 'توقّفت: مالقيتش نهاية عنصر BookletSummary في MainWindow.xaml. مافيش أي ملف اتغيّر.' }
    $end = $end + 2

    $block = @'


                                        <!-- الكتيّب بيعيد ترتيب الصفحات على أساس إن الورقة
                                             هتتطبع من الوجهين وتتطوي. لو الوجهين مقفول، كل وش
                                             بيروح على ورقة لوحده — ورق ضِعف اللازم ونُصّه فاضي.
                                             والاتنين في مجموعتين مختلفتين، فاللي بيفتح الكتيّب
                                             مش شايف حالة الوجهين قدامه أصلًا. -->
                                        <TextBlock Text="{Binding BookletDuplexWarning}" TextWrapping="Wrap"
                                                   FontSize="11" FontWeight="SemiBold" Margin="0,6,0,0"
                                                   Foreground="{Binding BookletDuplexIsActive,
                                                                Converter={StaticResource WarningBrush}}"
                                                   Visibility="{Binding BookletDuplexIsActive,
                                                                Converter={StaticResource BoolToVis}}"/>
'@ -replace "`r`n", "`n"

    $xamlText = $xamlText.Substring(0, $end) + $block + $xamlText.Substring($end)

    if ($xamlCrLf) { $xamlText = $xamlText -replace "`n", "`r`n" }
}

# ═══════════ الكتابة — كل الحسابات نجحت خلاص ═══════════

Write-Host ''

if ($vmDone) {
    Write-Host '  MainViewModel.cs  →  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
}
else {
    Copy-Item $vmPath "$vmPath.$stamp.bak"
    Write-Src $vmPath $vmText $vm.Bom
    Write-Host '  MainViewModel.cs  →  اتعدّل (خاصيتين + تحديث الإشعار).' -ForegroundColor Green
}

if ($xamlDone) {
    Write-Host '  MainWindow.xaml   →  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
}
else {
    Copy-Item $xamlPath "$xamlPath.$stamp.bak"
    Write-Src $xamlPath $xamlText $xaml.Bom
    Write-Host '  MainWindow.xaml   →  اتعدّل (السطر الأحمر تحت خيار الكتيّب).' -ForegroundColor Green
}

# ═══════════ التأكيد ═══════════

Write-Host ''
Write-Host '  ═══ تأكيد ═══' -ForegroundColor Cyan

$checks = @(
    @{ Name = 'الخاصية في الـ ViewModel';       Path = $vmPath;   Needle = 'BookletRules.Describe(Settings)' },
    @{ Name = 'تحديث الإشعار';                  Path = $vmPath;   Needle = 'nameof(BookletDuplexIsActive)' },
    @{ Name = 'السطر الأحمر في الواجهة';        Path = $xamlPath; Needle = '{Binding BookletDuplexWarning}' },
    @{ Name = 'ملف القاعدة BookletRules.cs';    Path = (Join-Path $root 'src\PrintFlow.Domain\BookletRules.cs');       Needle = 'NeedsDuplex' },
    @{ Name = 'ملف التستات BookletRulesTests.cs'; Path = (Join-Path $root 'tests\PrintFlow.Tests\BookletRulesTests.cs'); Needle = 'NeedsDuplex' }
)

$allOk = $true

foreach ($c in $checks) {
    $ok = $false
    if (Test-Path $c.Path) {
        $ok = (Read-Src $c.Path).Text.Contains($c.Needle)
    }

    if ($ok) {
        Write-Host ('    [تمام]  ' + $c.Name) -ForegroundColor Green
    }
    else {
        Write-Host ('    [ناقص]  ' + $c.Name) -ForegroundColor Red
        $allOk = $false
    }
}

Write-Host ''

if ($allOk) {
    Write-Host '  كله تمام. شغّل دلوقتي:  .\build.ps1'      -ForegroundColor Green
    Write-Host '  المتوقّع: 976 تست، صفر فشل.'              -ForegroundColor Green
    Write-Host ''
    Write-Host "  النسخ الاحتياطية اسمها *.$stamp.bak — امسحها بعد ما البناء ينجح." -ForegroundColor DarkGray
}
else {
    Write-Host '  في حاجة ناقصة فوق — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
