# ═══════════════════════════════════════════════════════════════════
#  تصليح: الإيقاف اللي بيجي **وسط** آخر خطوة
#
#  التستات كشفت فجوتين حقيقيتين في التعديل اللي قبله:
#
#   ١) وضع الدمج: السلسلة بتفحص التوكن بين المراحل، والدمج مرحلة واحدة —
#      فالإيقاف اللي بيجي وهي شغالة مابيلحقش. البرنامج كان بيقول
#      «تمت المعالجة» بعد ما المستخدم دَوس إيقاف.
#
#   ٢) وضع كل ملف لوحده: الإيقاف اللي بيجي وآخر ملف ماشي — الحلقة خلصت
#      فمفيش تكرار جاي يشوف التوكن. نفس النتيجة الغلط.
#
#  تعديلين في: src\PrintFlow.Presentation\MainViewModel.cs
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
        throw "توقّفت عند التعديل رقم $number : المكان اتلقى $n مرة، المفروض مرة واحدة. الملف ماتغيّرش."
    }
    return $text.Replace($anchor, $replacement)
}

Write-Host ''
Write-Host '  اقفل MainViewModel.cs في VS Code الأول.' -ForegroundColor Yellow
Write-Host ''
Read-Host  '  لما تقفله اضغط Enter'

$vmPath = Join-Path $root 'src\PrintFlow.Presentation\MainViewModel.cs'

$vm   = Read-Src $vmPath
$crlf = $vm.Text.Contains("`r`n")
$text = $vm.Text -replace "`r`n", "`n"

if (-not $text.Contains('_processCancel')) {
    throw 'التعديل الأساسي (إيقاف المعالجة) مش متعمول. شغّل apply-stop-processing.ps1 الأول.'
}

if ($text.Contains('stopped = stopped || token.IsCancellationRequested;')) {
    Write-Host ''
    Write-Host '  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
    Write-Host ''
    return
}

    $a1 = @'
                _output = [new PrintableDocument(outputPath, result.PageCount)];
                StatusText = $"تمت المعالجة: {Files.Count} ملف في {result.PageCount} صفحة.";
'@ -replace "`r`n", "`n"
    $b1 = @'
                // ⚠ المرحلة خلصت، بس المستخدم كان دَوس إيقاف وهي ماشية.
                //
                // السلسلة بتفحص التوكن **بين** المراحل، والدمج في الحالة
                // العادية مرحلة واحدة — فالإلغاء اللي بيجي وهي شغالة
                // مابيلحقش يوقفها. النتيجة كانت إن المستخدم يدوس إيقاف
                // والبرنامج يقوله «تمت المعالجة».
                //
                // الملف في التيمب ومحدش طالبه — بنسيبه ونقول الحقيقة.
                if (token.IsCancellationRequested)
                {
                    _output = new List<PrintableDocument>();
                    NoteProcessingStopped(0, inputs.Count);
                    return;
                }

                _output = [new PrintableDocument(outputPath, result.PageCount)];
                StatusText = $"تمت المعالجة: {Files.Count} ملف في {result.PageCount} صفحة.";
'@ -replace "`r`n", "`n"
    $a2 = @'
        _output = produced;

        // ⚠ الإيقاف قبل الفشل:
'@ -replace "`r`n", "`n"
    $b2 = @'
        // ⚠ الإلغاء اللي جه وآخر ملف ماشي.
        //
        // الحلقة خلصت، فمفيش تكرار جاي يشوف التوكن — والبرنامج كان
        // بيقول «تمت المعالجة» بعد ما المستخدم دَوس إيقاف.
        stopped = stopped || token.IsCancellationRequested;

        _output = produced;

        // ⚠ الإيقاف قبل الفشل:
'@ -replace "`r`n", "`n"

$text = Set-Snippet $text $a1 $b1 1
$text = Set-Snippet $text $a2 $b2 2

if ($crlf) { $text = $text -replace "`n", "`r`n" }

Copy-Item $vmPath "$vmPath.$stamp.bak"
Write-Src $vmPath $text $vm.Bom

Write-Host ''
Write-Host '  MainViewModel.cs  →  اتعدّل (تعديلين).' -ForegroundColor Green

$after = (Read-Src $vmPath).Text
$ok = $after.Contains('stopped = stopped || token.IsCancellationRequested;') -and
      $after.Contains('NoteProcessingStopped(0, inputs.Count);')

Write-Host ''

if ($ok) {
    Write-Host '    [تمام]  فحص الإلغاء بعد الدمج'         -ForegroundColor Green
    Write-Host '    [تمام]  فحص الإلغاء بعد آخر ملف'       -ForegroundColor Green
    Write-Host ''
    Write-Host '  ⚠ ولازم تبدّل ProcessCancelTests.cs بالنسخة الجديدة كمان.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  وبعدين:  .\build.ps1'              -ForegroundColor Green
    Write-Host '  المتوقّع: 1027 تست، صفر فشل.'      -ForegroundColor Green
}
else {
    Write-Host '  في حاجة ناقصة — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
