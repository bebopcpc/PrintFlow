# ═══════════════════════════════════════════════════════════════════
#  عدّاد الورق — كام ورقة هتخرج من الأوردر قبل ما تضغط طباعة
#
#  بيعدّل ملفين:
#    1) src\PrintFlow.Presentation\MainViewModel.cs
#         • خاصيتين: PaperSummary و PaperSummaryIsVisible
#         • RefreshPaperSummary + توصيلها بالمحفّزات
#    2) src\PrintFlow.UI\MainWindow.xaml
#         • سطر "الورق المتوقع" تحت خيارات الطباعة
#
#  بيحسب كل حاجة الأول، وبيكتب في آخر خطوة. لو أي مكان مش زي ما هو
#  متوقّع، بيقف من غير ما يلمس أي ملف خالص.
#
#  بياخد نسخة احتياطية من كل ملف بيغيّره، وبيشتغل مرة واحدة بس.
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

function Assert-Once([string]$text, [string]$anchor, [string]$what) {
    $n = ([regex]::Matches($text, [regex]::Escape($anchor))).Count
    if ($n -ne 1) {
        throw "توقّفت عند «$what»: المكان المتوقّع اتلقى $n مرة، المفروض مرة واحدة بالظبط. مافيش أي ملف اتغيّر."
    }
}

# بيحط الإضافة بعد المرساة، والمرساة بتفضل مكانها
function Add-Snippet([string]$text, [string]$anchor, [string]$addition, [string]$what) {
    Assert-Once $text $anchor $what
    return $text.Replace($anchor, $anchor + $addition)
}

# بيبدّل المرساة بالكامل
function Set-Snippet([string]$text, [string]$anchor, [string]$replacement, [string]$what) {
    Assert-Once $text $anchor $what
    return $text.Replace($anchor, $replacement)
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

$vm     = Read-Src $vmPath
$vmCrLf = $vm.Text.Contains("`r`n")
$vmText = $vm.Text -replace "`r`n", "`n"
$vmDone = $vmText.Contains('PaperSummaryIsVisible')

if (-not $vmDone) {

    # ── أ) نداء غير مشروط في أول مستمع الإعدادات
    $anchorA = @'
        Settings.PropertyChanged += (_, e) =>
        {
'@ -replace "`r`n", "`n"

    $addA = @'

            // ⚠ من غير شرط عن قصد.
            //
            // عدّاد الورق بيقرا تسع خصايص (النسخ، التوزيع، الوجهين،
            // الكتيّب، الشرائح، الحذف، نص الحذف، وأول وآخر صفحة). لستة
            // بالأسامي هنا معناها إن أي خيار جديد يتضاف بعد كده لازم حد
            // يفتكر يضيفه هنا كمان — وأول مرة حد ينسى، الرقم بيفضل
            // معروض غلط من غير أي علامة.
            //
            // والحسبة نفسها جمع على كام رقم صحيح، فمفيش أي تمن لتشغيلها
            // مع كل تغيير.
            RefreshPaperSummary();

'@ -replace "`r`n", "`n"

    # ── ب) نفس محفّزات سطر اختيار المكن
    $anchorB = @'
    /// <summary>بيحدّث السطر بعد أي تغيير في التعليم أو عدد النسخ أو الملفات.</summary>
    private void RefreshPrinterChoiceSummary() => OnPropertyChanged(nameof(PrinterChoiceSummary));
'@ -replace "`r`n", "`n"

    $newB = @'
    /// <summary>بيحدّث السطر بعد أي تغيير في التعليم أو عدد النسخ أو الملفات.</summary>
    private void RefreshPrinterChoiceSummary()
    {
        OnPropertyChanged(nameof(PrinterChoiceSummary));

        // عدّاد الورق بيتغيّر مع نفس الحاجات بالظبط: التعليم على المكن،
        // الملفات، عدد النسخ، وبداية ونهاية الأوردر. محفّز واحد للاتنين
        // بدل ما نفتكر نضيف نداء في ست أماكن.
        RefreshPaperSummary();
    }

    /// <summary>بيحدّث سطر الورق المتوقع وظهوره.</summary>
    private void RefreshPaperSummary()
    {
        OnPropertyChanged(nameof(PaperSummary));
        OnPropertyChanged(nameof(PaperSummaryIsVisible));
    }
'@ -replace "`r`n", "`n"

    # ── ج) لما أعداد الصفحات توصل
    $anchorC = @'
        // والتوزيع محتاجه عشان يحسب "حوالي كام صفحة لكل مكنة"
        RefreshDistributionSummary();
'@ -replace "`r`n", "`n"

    $addC = @'


        // وعدّاد الورق كان بيقول "" طول ما الأعداد لسه بتتقرا
        RefreshPaperSummary();
'@ -replace "`r`n", "`n"

    # ── د) الخاصيتين الجداد
    $anchorD = @'
    /// <summary>في مشكلة؟ ده اللي بيظهّر السطر ويخفيه.</summary>
    public bool BookletDuplexIsActive => BookletRules.NeedsDuplex(Settings);
'@ -replace "`r`n", "`n"

    $addD = @'


    /// <summary>
    /// الورق المتوقع من الأوردر ده — قبل ما حد يضغط طباعة.
    ///
    /// ═══ ليه ورق مش صفحات ═══
    ///
    /// اللي بيتحضّر وبيتسعّر في المطبعة ورق. "٢٤٠ صفحة" على الوجهين
    /// واتنين في الورقة = ٦٠ ورقة — واللي حضّر ٢٤٠ حضّر أربع أضعاف.
    ///
    /// ═══ الملف اللي اتعالج بيتحسب بطريقة تانية ═══
    ///
    /// بعد المعالجة، الحذف والتجميع اتنفّذوا على الملف خلاص. لو حسبناهم
    /// تاني الرقم بيطلع نُصّه. عشان كده بنقول لـ <see cref="PaperCount"/>
    /// إحنا في أنهي مرحلة بدل ما نخمّن.
    /// </summary>
    public string PaperSummary
    {
        get
        {
            bool processed = _output.Count > 0;

            var pages = processed
                ? _output.Select(d => d.Pages).ToList()
                : Files.Select(f => f.PageCount ?? 0).ToList();

            // نفس شرط الأهلية اللي الطباعة بتستخدمه. من غير توزيع كل
            // مكنة بتطلّع العدد كامل، فالعدد ده بيضرب الورق.
            int machines = Math.Max(1, Printers.Count(p => p.IsSelected && p.IsEligible));

            return PaperCount.Describe(pages, Settings, machines, processed);
        }
    }

    /// <summary>فيه رقم نعرضه أصلًا؟ بيرجّع false لما الأعداد لسه مجهولة.</summary>
    public bool PaperSummaryIsVisible => PaperSummary.Length > 0;
'@ -replace "`r`n", "`n"

    $vmText = Add-Snippet $vmText $anchorA $addA 'مستمع الإعدادات في MainViewModel.cs'
    $vmText = Set-Snippet $vmText $anchorB $newB 'دالة RefreshPrinterChoiceSummary'
    $vmText = Add-Snippet $vmText $anchorC $addC 'نهاية قراءة أعداد الصفحات'
    $vmText = Add-Snippet $vmText $anchorD $addD 'مكان الخاصيتين الجداد'

    if ($vmCrLf) { $vmText = $vmText -replace "`n", "`r`n" }
}

# ═══════════ 2) MainWindow.xaml — الحساب ═══════════

$xaml     = Read-Src $xamlPath
$xamlCrLf = $xaml.Text.Contains("`r`n")
$xamlText = $xaml.Text -replace "`r`n", "`n"
$xamlDone = $xamlText.Contains('PaperSummaryIsVisible')

if (-not $xamlDone) {

    $key = '{Binding DistributionSummary}'
    Assert-Once $xamlText $key 'سطر ملخّص التوزيع في MainWindow.xaml'

    $i   = $xamlText.IndexOf($key)
    $end = $xamlText.IndexOf('/>', $i)
    if ($end -lt 0) { throw 'توقّفت: مالقيتش نهاية عنصر DistributionSummary. مافيش أي ملف اتغيّر.' }
    $end = $end + 2

    $block = @'


                                        <!-- الورق المتوقع. ده الرقم اللي المطبعة بتحضّر
                                             وبتسعّر عليه — مش عدد الصفحات. أوردر ٢٤٠ صفحة
                                             على الوجهين واتنين في الورقة = ٦٠ ورقة بس. -->
                                        <TextBlock Text="{Binding PaperSummary}" TextWrapping="Wrap"
                                                   FontSize="12" FontWeight="SemiBold" Margin="0,10,0,0"
                                                   Foreground="#1B2A4A"
                                                   Visibility="{Binding PaperSummaryIsVisible,
                                                                Converter={StaticResource BoolToVis}}"/>
'@ -replace "`r`n", "`n"

    $xamlText = $xamlText.Substring(0, $end) + $block + $xamlText.Substring($end)

    if ($xamlCrLf) { $xamlText = $xamlText -replace "`n", "`r`n" }
}

# ═══════════ الكتابة ═══════════

Write-Host ''

if ($vmDone) {
    Write-Host '  MainViewModel.cs  →  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
}
else {
    Copy-Item $vmPath "$vmPath.$stamp.bak"
    Write-Src $vmPath $vmText $vm.Bom
    Write-Host '  MainViewModel.cs  →  اتعدّل (خاصيتين + ٣ محفّزات).' -ForegroundColor Green
}

if ($xamlDone) {
    Write-Host '  MainWindow.xaml   →  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
}
else {
    Copy-Item $xamlPath "$xamlPath.$stamp.bak"
    Write-Src $xamlPath $xamlText $xaml.Bom
    Write-Host '  MainWindow.xaml   →  اتعدّل (سطر الورق المتوقع).' -ForegroundColor Green
}

# ═══════════ التأكيد ═══════════

Write-Host ''
Write-Host '  ═══ تأكيد ═══' -ForegroundColor Cyan

$checks = @(
    @{ Name = 'خاصية الورق في الـ ViewModel'; Path = $vmPath;   Needle = 'PaperCount.Describe(pages, Settings, machines, processed)' },
    @{ Name = 'دالة التحديث';                  Path = $vmPath;   Needle = 'private void RefreshPaperSummary()' },
    @{ Name = 'المحفّز غير المشروط';           Path = $vmPath;   Needle = 'RefreshPaperSummary();' },
    @{ Name = 'السطر في الواجهة';              Path = $xamlPath; Needle = '{Binding PaperSummary}' },
    @{ Name = 'ملف الحساب PaperCount.cs';      Path = (Join-Path $root 'src\PrintFlow.Domain\PaperCount.cs');        Needle = 'SheetsFrom' },
    @{ Name = 'ملف التستات PaperCountTests.cs'; Path = (Join-Path $root 'tests\PrintFlow.Tests\PaperCountTests.cs'); Needle = 'SheetsFrom' }
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
    Write-Host '  كله تمام. شغّل دلوقتي:  .\build.ps1'  -ForegroundColor Green
    Write-Host '  المتوقّع: 1004 تست، صفر فشل.'         -ForegroundColor Green
    Write-Host ''
    Write-Host "  النسخ الاحتياطية اسمها *.$stamp.bak — امسحها بعد ما البناء ينجح." -ForegroundColor DarkGray
}
else {
    Write-Host '  في حاجة ناقصة فوق — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
