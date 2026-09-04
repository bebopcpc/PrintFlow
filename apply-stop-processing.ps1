# ═══════════════════════════════════════════════════════════════════
#  إيقاف المعالجة
#
#  بيعدّل ملف واحد: src\PrintFlow.Presentation\MainViewModel.cs
#    • توكن للمعالجة جنب توكن الطباعة
#    • «إيقاف فوري» بقى يشتغل في مرحلة المعالجة كمان
#    • السلسلة بتقف عند أقرب حد آمن (آخر ملف أو آخر مرحلة)
#    • اللي خلص بيفضل، والطباعة التلقائية مابتشتغلش بعد إيقاف
#
#  ١٥ تعديل، كل واحد فيهم بيتأكد إن مكانه موجود مرة واحدة بالظبط.
#  لو أي واحد مش مظبوط، بيقف من غير ما يلمس الملف خالص.
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

function Set-Snippet([string]$text, [string]$anchor, [string]$replacement, [int]$number) {
    $n = ([regex]::Matches($text, [regex]::Escape($anchor))).Count
    if ($n -ne 1) {
        throw "توقّفت عند التعديل رقم $number : المكان المتوقّع اتلقى $n مرة، المفروض مرة واحدة بالظبط. الملف ماتغيّرش."
    }
    return $text.Replace($anchor, $replacement)
}

Write-Host ''
Write-Host '  اقفل MainViewModel.cs في VS Code الأول (قفل، مش بس تسيبه مفتوح).' -ForegroundColor Yellow
Write-Host ''
Read-Host  '  لما تقفله اضغط Enter'

$vmPath = Join-Path $root 'src\PrintFlow.Presentation\MainViewModel.cs'

$vm   = Read-Src $vmPath
$crlf = $vm.Text.Contains("`r`n")
$text = $vm.Text -replace "`r`n", "`n"

if ($text.Contains('_processCancel')) {
    Write-Host ''
    Write-Host '  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
    Write-Host ''
    return
}

    $a1 = @'
        CancelCommand = new RelayCommand(CancelPrinting, () => IsPrinting);
'@ -replace "`r`n", "`n"
    $b1 = @'
        // ده الزرار الوحيد اللي **لازم** يشتغل والشغل ماشي، فشرطه معكوس.
        // IsBusy جوّه الشرط عشان المعالجة كمان بقى ليها إيقاف — قبل كده
        // كان اللي حمّل ٥٠ ملف ودَوس معالجة مالوش طريق غير Task Manager.
        CancelCommand = new RelayCommand(CancelPrinting, () => IsBusy || IsPrinting);
'@ -replace "`r`n", "`n"
    $a2 = @'
    /// <summary>بيتلغي لما المستخدم يضغط «إيقاف فوري». null = مفيش طباعة ماشية.</summary>
    private CancellationTokenSource? _printCancel;
'@ -replace "`r`n", "`n"
    $b2 = @'
    /// <summary>بيتلغي لما المستخدم يضغط «إيقاف فوري». null = مفيش طباعة ماشية.</summary>
    private CancellationTokenSource? _printCancel;

    /// <summary>
    /// نفس الفكرة بس للمعالجة. null = مفيش معالجة ماشية.
    ///
    /// ⚠ منفصل عن توكن الطباعة عن قصد. المعالجة بتنده الطباعة التلقائية
    /// جوّاها، فالاتنين بيبقوا حيّين في نفس اللحظة — ولو كانوا توكن واحد،
    /// «وقّف الطباعة» كان هيوقّف معالجة خلصت خلاص، والعكس.
    /// </summary>
    private CancellationTokenSource? _processCancel;
'@ -replace "`r`n", "`n"
    $a3 = @'
        IsBusy = true;
        StatusText = "جاري معالجة الملفات...";

        try
        {
            CleanOldTempFiles();

            var inputs = Files.Select(f => f.FullPath).ToList();
'@ -replace "`r`n", "`n"
    $b3 = @'
        // بيتظبط **قبل** IsBusy: رفع IsBusy بينده RefreshCommandStates،
        // واللي بينوّر زرار الإيقاف. لو التوكن لسه null ساعتها، فيه لحظة
        // الزرار فيها مفتوح ومالوش أثر.
        _processCancel = new CancellationTokenSource();

        IsBusy = true;
        StatusText = "جاري معالجة الملفات...";

        try
        {
            var token = _processCancel.Token;

            CleanOldTempFiles();

            var inputs = Files.Select(f => f.FullPath).ToList();
'@ -replace "`r`n", "`n"
    $a4 = @'
                var request = MergeRequest.From(Settings, App, inputs, outputPath);
                var result = await RunPipelineAsync(request);

                Log.Add(result.Message);
'@ -replace "`r`n", "`n"
    $b4 = @'
                var request = MergeRequest.From(Settings, App, inputs, outputPath);

                MergeResult result;

                try
                {
                    result = await RunPipelineAsync(request, token);
                }
                catch (OperationCanceledException)
                {
                    // الدمج مستند واحد — نُصّه مالوش قيمة، فمابنسيبش
                    // الملف الناقص في _output. الأصول زي ما هي.
                    _output = new List<PrintableDocument>();
                    NoteProcessingStopped(0, inputs.Count);
                    return;
                }

                Log.Add(result.Message);
'@ -replace "`r`n", "`n"
    $a5 = @'
            else
            {
                await ProcessWithoutMergingAsync(inputs);
            }
'@ -replace "`r`n", "`n"
    $b5 = @'
            else
            {
                await ProcessWithoutMergingAsync(inputs, token);
            }
'@ -replace "`r`n", "`n"
    $a6 = @'
            if (Settings.PrintDirectlyAfterProcessing && _output.Count > 0)
            {
                await PrintAsync();
            }
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
'@ -replace "`r`n", "`n"
    $b6 = @'
            // ⚠ وشرط الإلغاء كمان: اللي دَوس إيقاف وسط المعالجة مش عايز
            // الملفات اللي خلصت تروح للمكن لوحدها بعدها.
            if (Settings.PrintDirectlyAfterProcessing && _output.Count > 0 && !token.IsCancellationRequested)
            {
                await PrintAsync();
            }
        }
        finally
        {
            var cts = _processCancel;
            _processCancel = null;
            cts?.Dispose();

            IsBusy = false;
            RefreshCommandStates();
        }
'@ -replace "`r`n", "`n"
    $a7 = @'
    private async Task<MergeResult> RunPipelineAsync(MergeRequest request)
'@ -replace "`r`n", "`n"
    $b7 = @'
    private async Task<MergeResult> RunPipelineAsync(MergeRequest request, CancellationToken token)
'@ -replace "`r`n", "`n"
    $a8 = @'
        return await Task.Run(() => RunStages(stages, request.OutputPath));
'@ -replace "`r`n", "`n"
    $b8 = @'
        return await Task.Run(() => RunStages(stages, request.OutputPath, token), token);
'@ -replace "`r`n", "`n"
    $a9 = @'
    private static MergeResult RunStages(IReadOnlyList<PipelineStage> stages, string finalOutput)
'@ -replace "`r`n", "`n"
    $b9 = @'
    private static MergeResult RunStages(
        IReadOnlyList<PipelineStage> stages, string finalOutput, CancellationToken token)
'@ -replace "`r`n", "`n"
    $a10 = @'
            for (int i = 0; i < stages.Count; i++)
            {
                bool last = i == stages.Count - 1;
'@ -replace "`r`n", "`n"
    $b10 = @'
            for (int i = 0; i < stages.Count; i++)
            {
                // ⚠ الفحص **بين** المراحل مش جوّاها.
                //
                // كل مرحلة بتنده خدمة خارجية (دمج، تجميع، مقياس) وماعندهاش
                // توكن — فمانقدرش نقاطعها في نُصّها. أسوأ حالة إن المستخدم
                // يستنى المرحلة اللي ماشية تخلص، مش الملفات كلها.
                //
                // والرمية دي بتخرج من Task.Run وبتتلقط في اللي نداها،
                // والـ finally تحت بيمسح الملفات الوسيطة زي ما هو.
                token.ThrowIfCancellationRequested();

                bool last = i == stages.Count - 1;
'@ -replace "`r`n", "`n"
    $a11 = @'
    private async Task ProcessWithoutMergingAsync(IReadOnlyList<string> inputs)
'@ -replace "`r`n", "`n"
    $b11 = @'
    private async Task ProcessWithoutMergingAsync(IReadOnlyList<string> inputs, CancellationToken token)
'@ -replace "`r`n", "`n"
    $a12 = @'
        var produced = new List<PrintableDocument>();
        var failures = new List<string>();
        int nextNumber = 1;
        int totalProcessedPages = 0;

        for (int i = 0; i < inputs.Count; i++)
        {
            string source = inputs[i];
'@ -replace "`r`n", "`n"
    $b12 = @'
        var produced = new List<PrintableDocument>();
        var failures = new List<string>();
        int nextNumber = 1;
        int totalProcessedPages = 0;
        bool stopped = false;

        for (int i = 0; i < inputs.Count; i++)
        {
            // الفحص على حد الملف: اللي خلص بيفضل، واللي بعده مايبدأش.
            if (token.IsCancellationRequested)
            {
                stopped = true;
                break;
            }

            string source = inputs[i];
'@ -replace "`r`n", "`n"
    $a13 = @'
            var result = await RunPipelineAsync(request);

            if (result.Success)
'@ -replace "`r`n", "`n"
    $b13 = @'
            MergeResult result;

            try
            {
                result = await RunPipelineAsync(request, token);
            }
            catch (OperationCanceledException)
            {
                // الملف ده وقف في نُصّه — مابيدخلش المخرج.
                stopped = true;
                break;
            }

            if (result.Success)
'@ -replace "`r`n", "`n"
    $a14 = @'
        _output = produced;

        if (produced.Count == 0)
        {
            StatusText = "فشلت معالجة كل الملفات. شوف التفاصيل في اللوج.";
            return;
        }
'@ -replace "`r`n", "`n"
    $b14 = @'
        _output = produced;

        // ⚠ الإيقاف قبل الفشل: لو المستخدم وقّف قبل ما أي ملف يخلص،
        // «فشلت معالجة كل الملفات» بتخلّيه يدوّر على عطل مش موجود.
        if (stopped)
        {
            NoteProcessingStopped(produced.Count, inputs.Count);
            return;
        }

        if (produced.Count == 0)
        {
            StatusText = "فشلت معالجة كل الملفات. شوف التفاصيل في اللوج.";
            return;
        }
'@ -replace "`r`n", "`n"
    $a15 = @'
    private void CancelPrinting()
    {
        var cts = _printCancel;

        if (cts is null || cts.IsCancellationRequested)
        {
            return;
        }

        string line = "[إيقاف] المستخدم طلب إيقاف فوري — مفيش حاجة جديدة هتتبعت.";
'@ -replace "`r`n", "`n"
    $b15 = @'
    private void CancelPrinting()
    {
        var printing = _printCancel;
        var processing = _processCancel;

        // ⚠ الطباعة الأول لو الاتنين حيّين.
        //
        // المعالجة بتنده الطباعة التلقائية جوّاها، فالاتنين بيبقوا حيّين
        // في نفس اللحظة. اللي على السلك هو اللي بيصرف ورق — فهو الأولى
        // بالإيقاف، والمعالجة ساعتها خلصت شغلها أصلًا.
        if (printing is not null && !printing.IsCancellationRequested)
        {
            StopPrinting(printing);
            return;
        }

        if (processing is not null && !processing.IsCancellationRequested)
        {
            StopProcessing(processing);
        }
    }

    /// <summary>
    /// إيقاف المعالجة. مفيش طوابير تتفضّى هنا — لسه مفيش حاجة راحت
    /// لويندوز أصلًا. اللي بيتعمل: التوكن يتلغي، والسلسلة تقف عند أقرب
    /// حد آمن (آخر ملف أو آخر مرحلة).
    /// </summary>
    private void StopProcessing(CancellationTokenSource cts)
    {
        string line = "[إيقاف] المستخدم طلب إيقاف المعالجة — هنقف عند أقرب حد آمن.";
        Log.Add(line);
        _jobLog?.Info(line);
        StatusText = "بنوقف المعالجة...";

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // المعالجة خلصت لوحدها قبل ما نلحق
        }
    }

    /// <summary>بيكتب اللي حصل بعد إيقاف المعالجة — بالأرقام مش بالعموم.</summary>
    private void NoteProcessingStopped(int done, int total)
    {
        string line = done > 0
            ? $"[إيقاف] المعالجة اتوقفت — خلص {done} من {total} ملف، والباقي ماتعالجش."
            : "[إيقاف] المعالجة اتوقفت قبل ما أي ملف يخلص. الملفات الأصلية زي ما هي.";

        Log.Add(line);
        _jobLog?.Info(line);

        StatusText = done > 0
            ? $"المعالجة اتوقفت. {done} ملف خلصوا وجاهزين للطباعة، والباقي لأ."
            : "المعالجة اتوقفت. مفيش ملفات جاهزة.";
    }

    private void StopPrinting(CancellationTokenSource cts)
    {
        string line = "[إيقاف] المستخدم طلب إيقاف فوري — مفيش حاجة جديدة هتتبعت.";
'@ -replace "`r`n", "`n"

$text = Set-Snippet $text $a1 $b1 1
$text = Set-Snippet $text $a2 $b2 2
$text = Set-Snippet $text $a3 $b3 3
$text = Set-Snippet $text $a4 $b4 4
$text = Set-Snippet $text $a5 $b5 5
$text = Set-Snippet $text $a6 $b6 6
$text = Set-Snippet $text $a7 $b7 7
$text = Set-Snippet $text $a8 $b8 8
$text = Set-Snippet $text $a9 $b9 9
$text = Set-Snippet $text $a10 $b10 10
$text = Set-Snippet $text $a11 $b11 11
$text = Set-Snippet $text $a12 $b12 12
$text = Set-Snippet $text $a13 $b13 13
$text = Set-Snippet $text $a14 $b14 14
$text = Set-Snippet $text $a15 $b15 15

if ($crlf) { $text = $text -replace "`n", "`r`n" }

Copy-Item $vmPath "$vmPath.$stamp.bak"
Write-Src $vmPath $text $vm.Bom

Write-Host ''
Write-Host '  MainViewModel.cs  →  اتعدّل (15 تعديل).' -ForegroundColor Green
Write-Host ''
Write-Host '  ═══ تأكيد ═══' -ForegroundColor Cyan

$after = (Read-Src $vmPath).Text

$checks = @(
    @{ Name = 'توكن المعالجة';            Needle = 'private CancellationTokenSource? _processCancel;' },
    @{ Name = 'شرط زرار الإيقاف';         Needle = '() => IsBusy || IsPrinting)' },
    @{ Name = 'إيقاف المعالجة';           Needle = 'private void StopProcessing' },
    @{ Name = 'رسالة اللي خلص واللي لأ';  Needle = 'private void NoteProcessingStopped' },
    @{ Name = 'الفحص بين المراحل';        Needle = 'token.ThrowIfCancellationRequested();' },
    @{ Name = 'الفحص على حد الملف';       Needle = 'stopped = true;' }
)

$allOk = $true

foreach ($c in $checks) {
    if ($after.Contains($c.Needle)) { Write-Host ('    [تمام]  ' + $c.Name) -ForegroundColor Green }
    else { Write-Host ('    [ناقص]  ' + $c.Name) -ForegroundColor Red; $allOk = $false }
}

$testFile = Join-Path $root 'tests\PrintFlow.Tests\ProcessCancelTests.cs'
if (Test-Path $testFile) { Write-Host '    [تمام]  ملف التستات ProcessCancelTests.cs' -ForegroundColor Green }
else { Write-Host '    [ناقص]  ملف التستات ProcessCancelTests.cs' -ForegroundColor Red; $allOk = $false }

Write-Host ''

if ($allOk) {
    Write-Host '  كله تمام. شغّل دلوقتي:  .\build.ps1' -ForegroundColor Green
    Write-Host '  المتوقّع: 1027 تست، صفر فشل.'        -ForegroundColor Green
    Write-Host ''
    Write-Host "  النسخة الاحتياطية: MainViewModel.cs.$stamp.bak — امسحها بعد ما البناء ينجح." -ForegroundColor DarkGray
}
else {
    Write-Host '  في حاجة ناقصة — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
