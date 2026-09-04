# ═══════════════════════════════════════════════════════════════════
#  تجميع رسايل الفشل المتكررة
#
#  جه من تجربتك: ٤٤ ملف، فشل منهم ٢٠ لنفس السبب، فطلعوا عشرين سطر
#  متطابق في شاشة النتايج — ومحدش فيهم بيقول أنهي ملف.
#
#  بقى: سطرين فيهم العدد والأسامي.
#
#  ٤ تعديلات في: src\PrintFlow.Presentation\MainViewModel.cs
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

if ($text.Contains('ReportFailures')) {
    Write-Host ''
    Write-Host '  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
    Write-Host ''
    return
}

    $a1 = @'
        var produced = new List<PrintableDocument>();
        var failures = new List<string>();
'@ -replace "`r`n", "`n"
    $b1 = @'
        var produced = new List<PrintableDocument>();

        // اسم الملف جنب الرسالة: من غيره الفشل بيتقال ومحدش يعرف
        // يروح لأنهي ملف يصلّحه.
        var failures = new List<(string File, string Message)>();
'@ -replace "`r`n", "`n"
    $a2 = @'
                failures.Add(result.Message);
                Log.Add(result.Message);
                _jobLog?.Info($"تخطّينا ملف: {result.Message}");
'@ -replace "`r`n", "`n"
    $b2 = @'
                string failed = Path.GetFileName(source);

                failures.Add((failed, result.Message));

                // ⚠ مفيش Log.Add هنا عن قصد.
                //
                // كان في سطر لكل ملف. في تجربة حقيقية فشل ٢٠ ملف لنفس
                // السبب، فطلعوا عشرين سطر متطابق غرقوا سطر النجاح اللي
                // فوقهم. الفشل بيتقال مجمّع في الآخر — شوف ReportFailures.
                //
                // السجل على القرص لسه بياخد سطر لكل ملف: هو للمراجعة
                // بعد كده، مش للعرض وقت الشغل.
                _jobLog?.Info($"تخطّينا ملف: {failed} — {result.Message}");
'@ -replace "`r`n", "`n"
    $a3 = @'
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
    $b3 = @'
        ReportFailures(failures);

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
    $a4 = @'
    /// <summary>
    /// المستخدم طالب إن الملفات المعالجة تتحفظ عنده مش في التيمب؟
'@ -replace "`r`n", "`n"
    $b4 = @'
    /// <summary>
    /// بيكتب الفشل **مجمّع بالسبب** في شاشة النتايج، بأسامي الملفات.
    ///
    /// السطر اللي كان بيتكتب لكل ملف في الحلقة اتشال عن قصد: ٢٠ ملف
    /// بيفشلوا لنفس السبب كانوا بيطلعوا ٢٠ سطر متطابق، ومحدش فيهم
    /// بيقول أنهي ملف. بقى سطرين فيهم العدد والأسامي.
    /// </summary>
    private void ReportFailures(IReadOnlyList<(string File, string Message)> failures)
    {
        foreach (string line in FailureSummary.Describe(failures))
        {
            Log.Add(line);
        }
    }

    /// <summary>
    /// المستخدم طالب إن الملفات المعالجة تتحفظ عنده مش في التيمب؟
'@ -replace "`r`n", "`n"

$text = Set-Snippet $text $a1 $b1 1
$text = Set-Snippet $text $a2 $b2 2
$text = Set-Snippet $text $a3 $b3 3
$text = Set-Snippet $text $a4 $b4 4

if ($crlf) { $text = $text -replace "`n", "`r`n" }

Copy-Item $vmPath "$vmPath.$stamp.bak"
Write-Src $vmPath $text $vm.Bom

Write-Host ''
Write-Host '  MainViewModel.cs  →  اتعدّل (4 تعديلات).' -ForegroundColor Green
Write-Host ''
Write-Host '  ═══ تأكيد ═══' -ForegroundColor Cyan

$after = (Read-Src $vmPath).Text

$checks = @(
    @{ Name = 'دالة التجميع';              Needle = 'private void ReportFailures' },
    @{ Name = 'اسم الملف جنب الرسالة';     Needle = 'failures.Add((failed, result.Message));' },
    @{ Name = 'السطر المتكرر اتشال';       Needle = 'FailureSummary.Describe(failures)' },
    @{ Name = 'ملف القاعدة FailureSummary.cs';    Path = 'src\PrintFlow.Domain\FailureSummary.cs' },
    @{ Name = 'ملف التستات FailureSummaryTests.cs'; Path = 'tests\PrintFlow.Tests\FailureSummaryTests.cs' }
)

$allOk = $true

foreach ($c in $checks) {
    $ok = if ($c.Path) { Test-Path (Join-Path $root $c.Path) } else { $after.Contains($c.Needle) }

    if ($ok) { Write-Host ('    [تمام]  ' + $c.Name) -ForegroundColor Green }
    else     { Write-Host ('    [ناقص]  ' + $c.Name) -ForegroundColor Red; $allOk = $false }
}

Write-Host ''

if ($allOk) {
    Write-Host '  كله تمام. شغّل دلوقتي:  .\build.ps1' -ForegroundColor Green
    Write-Host '  المتوقّع: 1039 تست، صفر فشل.'        -ForegroundColor Green
    Write-Host ''
    Write-Host "  النسخة الاحتياطية: MainViewModel.cs.$stamp.bak" -ForegroundColor DarkGray
}
else {
    Write-Host '  في حاجة ناقصة — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
