# ═══════════════════════════════════════════════════════════════════
#  التكلفة المتوقعة — سعر الأوردر قبل ما تضغط طباعة
#
#  بيعدّل ٥ ملفات:
#    src\PrintFlow.Domain\AppSettings.cs              → خانة UnitPrice
#    src\PrintFlow.Presentation\MainViewModel.cs      → سطر التكلفة + المحفّزات
#    src\PrintFlow.UI\MainWindow.xaml                 → خانة السعر + السطر
#    tests\PrintFlow.Tests\AppSettingsPersistenceTests.cs  → حالة decimal
#    tests\PrintFlow.Tests\MainViewModelTests.cs           → حالة decimal
#
#  آخر ملفين تستات موجودة عندك خلاص: فيهم مساعد بيولّد "قيمة مختلفة"
#  لكل نوع، ومكانش فيه حالة للـ decimal. من غيرها التستات بتقع بسبب
#  المساعد نفسه مش بسبب باج.
#
#  بيحسب كل حاجة الأول، وبيكتب في آخر خطوة. لو أي مكان مش زي ما هو
#  متوقّع، بيقف من غير ما يلمس أي ملف خالص.
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
        throw "توقّفت عند «$what»: المكان المتوقّع اتلقى $n مرة، المفروض مرة واحدة بالظبط. مافيش أي ملف اتغيّر."
    }
    return $text.Replace($anchor, $replacement)
}

Write-Host ''
Write-Host '  اقفل الملفات دي في VS Code الأول (قفل، مش بس تسيبهم مفتوحين):' -ForegroundColor Yellow
Write-Host '     AppSettings.cs   MainViewModel.cs   MainWindow.xaml'        -ForegroundColor Yellow
Write-Host '     AppSettingsPersistenceTests.cs      MainViewModelTests.cs'  -ForegroundColor Yellow
Write-Host ''
Read-Host  '  لما تقفلهم اضغط Enter'

$appPath   = Join-Path $root 'src\PrintFlow.Domain\AppSettings.cs'
$vmPath    = Join-Path $root 'src\PrintFlow.Presentation\MainViewModel.cs'
$xamlPath  = Join-Path $root 'src\PrintFlow.UI\MainWindow.xaml'
$persPath  = Join-Path $root 'tests\PrintFlow.Tests\AppSettingsPersistenceTests.cs'
$vmtPath   = Join-Path $root 'tests\PrintFlow.Tests\MainViewModelTests.cs'

# ═══════════ 1) AppSettings.cs ═══════════

$app     = Read-Src $appPath
$appCrLf = $app.Text.Contains("`r`n")
$appText = $app.Text -replace "`r`n", "`n"
$appDone = $appText.Contains('UnitPrice')

if (-not $appDone) {
    $appA = @'
    private CountingMethod _countingMethod = CountingMethod.ByPage;
    public CountingMethod CountingMethod
    {
        get => _countingMethod;
        set => SetProperty(ref _countingMethod, value);
    }
'@ -replace "`r`n", "`n"
    $appB = @'
    private CountingMethod _countingMethod = CountingMethod.ByPage;
    /// <summary>
    /// السعر بيتحسب على إيه: الوجه المطبوع ولا الورقة.
    ///
    /// الاتنين طرق تسعير حقيقية، والفرق بينهم كبير: أوردر ١٢٠ وجه على
    /// ٦٠ ورقة بيطلع بسعرين مختلفين تمامًا. شوف <see cref="PriceEstimate"/>.
    /// </summary>
    public CountingMethod CountingMethod
    {
        get => _countingMethod;
        set => SetProperty(ref _countingMethod, value);
    }

    private decimal _unitPrice;
    /// <summary>
    /// سعر الوحدة الواحدة. الوحدة نفسها بتتحدد من <see cref="CountingMethod"/>.
    ///
    /// صفر معناه "مفيش تسعير" — وسطر التكلفة بيختفي خالص. رقم صفر جنب
    /// أوردر حقيقي بيبان زي عطل والمستخدم بيقعد يدوّر على السبب.
    ///
    /// السالب بيترد لصفر: مفيش خصم بالسالب، ده بيبقى غلطة كتابة.
    /// </summary>
    public decimal UnitPrice
    {
        get => _unitPrice;
        set => SetProperty(ref _unitPrice, value, v => v < 0m ? 0m : v);
    }
'@ -replace "`r`n", "`n"

    $appText = Set-Snippet $appText $appA $appB 'خانة طريقة الحساب في AppSettings.cs'

    if ($appCrLf) { $appText = $appText -replace "`n", "`r`n" }
}

# ═══════════ 2) MainViewModel.cs ═══════════

$vm     = Read-Src $vmPath
$vmCrLf = $vm.Text.Contains("`r`n")
$vmText = $vm.Text -replace "`r`n", "`n"
$vmDone = $vmText.Contains('CostSummaryIsVisible')

if (-not $vmDone) {
    $vmA1 = @'
        App.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.FileSortOrder))
'@ -replace "`r`n", "`n"
    $vmB1 = @'
        App.PropertyChanged += (_, e) =>
        {
            // السعر وطريقة الحساب عايشين في الإعدادات العامة مش في
            // إعدادات الجوب — فسطر التكلفة محفّزه هنا. من غير شرط، لنفس
            // سبب اللي في مستمع الجوب فوق.
            RefreshPaperSummary();

            if (e.PropertyName == nameof(AppSettings.FileSortOrder))
'@ -replace "`r`n", "`n"
    $vmA2 = @'
        new EnumOption<CountingMethod>(CountingMethod.ByPage, "بالصفحة"),
        new EnumOption<CountingMethod>(CountingMethod.BySheet, "بالورقة")
'@ -replace "`r`n", "`n"
    $vmB2 = @'
        new EnumOption<CountingMethod>(CountingMethod.ByPage, "بالوجه (كل وش مطبوع)"),
        new EnumOption<CountingMethod>(CountingMethod.BySheet, "بالورقة (بوجهيها)")
'@ -replace "`r`n", "`n"
    $vmA3 = @'
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
    $vmB3 = @'
    public string PaperSummary
    {
        get
        {
            var (pages, machines, processed) = CountingInputs;

            return PaperCount.Describe(pages, Settings, machines, processed);
        }
    }

    /// <summary>فيه رقم نعرضه أصلًا؟ بيرجّع false لما الأعداد لسه مجهولة.</summary>
    public bool PaperSummaryIsVisible => PaperSummary.Length > 0;

    /// <summary>
    /// تكلفة الأوردر بسعر الوحدة المكتوب في الإعدادات العامة.
    ///
    /// بيختفي خالص لما مفيش سعر متكتوب — مش بيعرض صفر. الصفر جنب أوردر
    /// حقيقي بيبان زي عطل والمستخدم بيقعد يدوّر على السبب.
    /// </summary>
    public string CostSummary
    {
        get
        {
            var (pages, machines, processed) = CountingInputs;
            var tally = PaperCount.For(pages, Settings, machines, processed);

            return PriceEstimate.Describe(tally, App.UnitPrice, App.CountingMethod);
        }
    }

    /// <summary>فيه سعر متكتوب وورق يتحسب عليه؟</summary>
    public bool CostSummaryIsVisible => CostSummary.Length > 0;

    /// <summary>
    /// مدخلات العدّ — سطر الورق وسطر التكلفة بيقروا منها الاتنين.
    ///
    /// ⚠ لازم تفضل مصدر واحد. لو كل سطر حسب مدخلاته بنفسه، أول تعديل
    /// على واحد فيهم بيخلّي السطرين يقولوا أرقام مش من نفس الأوردر —
    /// ونفس الدرس اتكرر معانا في PrinterChoiceSummary قبل كده.
    /// </summary>
    private (List<int> Pages, int Machines, bool Processed) CountingInputs
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

            return (pages, machines, processed);
        }
    }
'@ -replace "`r`n", "`n"
    $vmA4 = @'
    /// <summary>بيحدّث سطر الورق المتوقع وظهوره.</summary>
    private void RefreshPaperSummary()
    {
        OnPropertyChanged(nameof(PaperSummary));
        OnPropertyChanged(nameof(PaperSummaryIsVisible));
    }
'@ -replace "`r`n", "`n"
    $vmB4 = @'
    /// <summary>بيحدّث سطر الورق وسطر التكلفة وظهورهم.</summary>
    private void RefreshPaperSummary()
    {
        OnPropertyChanged(nameof(PaperSummary));
        OnPropertyChanged(nameof(PaperSummaryIsVisible));
        OnPropertyChanged(nameof(CostSummary));
        OnPropertyChanged(nameof(CostSummaryIsVisible));
    }
'@ -replace "`r`n", "`n"

    $vmText = Set-Snippet $vmText $vmA1 $vmB1 'مستمع الإعدادات العامة'
    $vmText = Set-Snippet $vmText $vmA2 $vmB2 'أسامي طرق الحساب'
    $vmText = Set-Snippet $vmText $vmA3 $vmB3 'خاصية الورق المتوقع'
    $vmText = Set-Snippet $vmText $vmA4 $vmB4 'دالة RefreshPaperSummary'

    if ($vmCrLf) { $vmText = $vmText -replace "`n", "`r`n" }
}

# ═══════════ 3) MainWindow.xaml ═══════════

$xaml     = Read-Src $xamlPath
$xamlCrLf = $xaml.Text.Contains("`r`n")
$xamlText = $xaml.Text -replace "`r`n", "`n"
$xamlDone = $xamlText.Contains('CostSummary')

if (-not $xamlDone) {
    $costBlock = @'


                                        <!-- التكلفة. بتختفي خالص لما مفيش سعر متكتوب —
                                             مش بتعرض صفر. الصفر جنب أوردر حقيقي بيبان زي
                                             عطل والمستخدم بيقعد يدوّر على السبب. -->
                                        <TextBlock Text="{Binding CostSummary}" TextWrapping="Wrap"
                                                   FontSize="13" FontWeight="Bold" Margin="0,4,0,0"
                                                   Foreground="#1B6E3C"
                                                   Visibility="{Binding CostSummaryIsVisible,
                                                                Converter={StaticResource BoolToVis}}"/>
'@ -replace "`r`n", "`n"
    $xamlA = @'
                                    <TextBlock Text="طريقة الحساب (قريبًا)" Style="{StaticResource FieldLabel}"/>
                                    <ComboBox ItemsSource="{Binding CountingMethods}"
                                              DisplayMemberPath="Label" SelectedValuePath="Value"
                                              SelectedValue="{Binding App.CountingMethod}" IsEnabled="False"
                                              ToolTip="الاختيار بيتحفظ، بس حساب التكلفة بالورقة لسه مش مبني"/>
'@ -replace "`r`n", "`n"
    $xamlB = @'
                                    <TextBlock Text="طريقة حساب التكلفة" Style="{StaticResource FieldLabel}"/>
                                    <ComboBox ItemsSource="{Binding CountingMethods}"
                                              DisplayMemberPath="Label" SelectedValuePath="Value"
                                              SelectedValue="{Binding App.CountingMethod}"
                                              ToolTip="بالوجه = كل وش مطبوع بسعر، والوجهين بيكلّف الضِعف (طريقة المصوّراتي).&#10;بالورقة = الورقة بسعر مهما اتطبع عليها وش ولا وشين (طريقة الملازم والكتيّبات)."/>

                                    <TextBlock Text="سعر الوحدة (جنيه)" Style="{StaticResource FieldLabel}"/>
                                    <TextBox Text="{Binding App.UnitPrice, UpdateSourceTrigger=PropertyChanged}"
                                             ToolTip="سيبها صفر لو مش عايز تسعير — سطر التكلفة هيختفي خالص."/>
'@ -replace "`r`n", "`n"

    $key = '{Binding PaperSummary}'
    $n = ([regex]::Matches($xamlText, [regex]::Escape($key))).Count
    if ($n -ne 1) {
        throw "توقّفت: «$key» اتلقى $n مرة في MainWindow.xaml، المفروض مرة واحدة. مافيش أي ملف اتغيّر."
    }

    $i   = $xamlText.IndexOf($key)
    $end = $xamlText.IndexOf('/>', $i)
    if ($end -lt 0) { throw 'توقّفت: مالقيتش نهاية عنصر PaperSummary. مافيش أي ملف اتغيّر.' }
    $end = $end + 2

    $xamlText = $xamlText.Substring(0, $end) + $costBlock + $xamlText.Substring($end)
    $xamlText = Set-Snippet $xamlText $xamlA $xamlB 'خانة طريقة الحساب المقفولة'

    if ($xamlCrLf) { $xamlText = $xamlText -replace "`n", "`r`n" }
}

# ═══════════ 4 و 5) مساعد التستات ═══════════
    $helperA = @'
        if (type == typeof(int)) return (int)value! + 7;
'@ -replace "`r`n", "`n"
    $helperB = @'
        if (type == typeof(int)) return (int)value! + 7;

        // السعر decimal. من غير الحالة دي، المساعد بيرجّع نفس القيمة —
        // فالخاصية ماتتغيّرش، ومحدش بيحفظ، والتست بيقع بسبب المساعد
        // نفسه مش بسبب باج حقيقي.
        if (type == typeof(decimal)) return (decimal)value! + 0.5m;
'@ -replace "`r`n", "`n"

$testFiles = @(
    @{ Path = $persPath; Name = 'AppSettingsPersistenceTests.cs' },
    @{ Path = $vmtPath;  Name = 'MainViewModelTests.cs' }
)

$testWork = @()

foreach ($t in $testFiles) {
    $f     = Read-Src $t.Path
    $crlf  = $f.Text.Contains("`r`n")
    $text  = $f.Text -replace "`r`n", "`n"
    $done  = $text.Contains('typeof(decimal)')

    if (-not $done) {
        $text = Set-Snippet $text $helperA $helperB ('مساعد Different في ' + $t.Name)
        if ($crlf) { $text = $text -replace "`n", "`r`n" }
    }

    $testWork += [pscustomobject]@{ Path = $t.Path; Name = $t.Name; Text = $text; Bom = $f.Bom; Done = $done }
}

# ═══════════ الكتابة — كل الحسابات نجحت خلاص ═══════════

Write-Host ''

function Save-One([string]$path, [string]$text, [bool]$bom, [bool]$done, [string]$label) {
    if ($done) {
        Write-Host ('  ' + $label.PadRight(34) + '→  متعمول قبل كده، سيبته زي ما هو.') -ForegroundColor DarkGray
        return
    }
    Copy-Item $path "$path.$stamp.bak"
    Write-Src $path $text $bom
    Write-Host ('  ' + $label.PadRight(34) + '→  اتعدّل.') -ForegroundColor Green
}

Save-One $appPath  $appText  $app.Bom  $appDone  'AppSettings.cs'
Save-One $vmPath   $vmText   $vm.Bom   $vmDone   'MainViewModel.cs'
Save-One $xamlPath $xamlText $xaml.Bom $xamlDone 'MainWindow.xaml'

foreach ($t in $testWork) {
    Save-One $t.Path $t.Text $t.Bom $t.Done $t.Name
}

# ═══════════ التأكيد ═══════════

Write-Host ''
Write-Host '  ═══ تأكيد ═══' -ForegroundColor Cyan

$checks = @(
    @{ Name = 'خانة السعر في الإعدادات';   Path = $appPath;  Needle = 'public decimal UnitPrice' },
    @{ Name = 'سطر التكلفة في الـ ViewModel'; Path = $vmPath;   Needle = 'PriceEstimate.Describe(tally' },
    @{ Name = 'مدخلات العدّ المشتركة';      Path = $vmPath;   Needle = 'CountingInputs' },
    @{ Name = 'محفّز الإعدادات العامة';     Path = $vmPath;   Needle = 'RefreshPaperSummary();' },
    @{ Name = 'خانة السعر في الواجهة';      Path = $xamlPath; Needle = 'App.UnitPrice' },
    @{ Name = 'سطر التكلفة في الواجهة';     Path = $xamlPath; Needle = 'CostSummaryIsVisible' },
    @{ Name = 'حالة decimal (حفظ)';         Path = $persPath; Needle = 'typeof(decimal)' },
    @{ Name = 'حالة decimal (افتراضي)';     Path = $vmtPath;  Needle = 'typeof(decimal)' },
    @{ Name = 'ملف الحساب PriceEstimate.cs';   Path = (Join-Path $root 'src\PrintFlow.Domain\PriceEstimate.cs');         Needle = 'UnitsIn' },
    @{ Name = 'ملف التستات PriceEstimateTests.cs'; Path = (Join-Path $root 'tests\PrintFlow.Tests\PriceEstimateTests.cs'); Needle = 'UnitsIn' }
)

$allOk = $true

foreach ($c in $checks) {
    $ok = $false
    if (Test-Path $c.Path) { $ok = (Read-Src $c.Path).Text.Contains($c.Needle) }

    if ($ok) { Write-Host ('    [تمام]  ' + $c.Name) -ForegroundColor Green }
    else     { Write-Host ('    [ناقص]  ' + $c.Name) -ForegroundColor Red; $allOk = $false }
}

Write-Host ''

if ($allOk) {
    Write-Host '  كله تمام. شغّل دلوقتي:  .\build.ps1' -ForegroundColor Green
    Write-Host '  المتوقّع: 1016 تست، صفر فشل.'        -ForegroundColor Green
    Write-Host ''
    Write-Host "  النسخ الاحتياطية اسمها *.$stamp.bak — امسحها بعد ما البناء ينجح." -ForegroundColor DarkGray
}
else {
    Write-Host '  في حاجة ناقصة فوق — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
