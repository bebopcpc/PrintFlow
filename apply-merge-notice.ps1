# ═══════════════════════════════════════════════════════════════════
#  السطر العملاق في وضع الدمج
#
#  مع الدمج، كل ملف اتشالت صفحاته بيضيف تنبيه، وكلهم بيتلموا في سطر
#  واحد. ٤٠ ملف = سطر ملا مربع النتايج كله ومش ينفع يتقرا.
#
#  بقى: "40 ملف اتشالت كل صفحاتهم: a.pdf، b.pdf، c.pdf و37 غيرهم"
#
#  ٣ تعديلات في: src\PrintFlow.Infrastructure\PdfMergeService.cs
#  ⚠ لازم FailureSummary.cs يكون متحدّث الأول (فيه NameList).
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

$domain = Join-Path $root 'src\PrintFlow.Domain\FailureSummary.cs'

if (-not (Test-Path $domain) -or -not ((Read-Src $domain).Text.Contains('public static string NameList'))) {
    throw 'FailureSummary.cs القديم — لازم تبدّله بالنسخة الجديدة الأول (اللي فيها NameList).'
}

Write-Host ''
Write-Host '  اقفل PdfMergeService.cs في VS Code الأول.' -ForegroundColor Yellow
Write-Host ''
Read-Host  '  لما تقفله اضغط Enter'

$path = Join-Path $root 'src\PrintFlow.Infrastructure\PdfMergeService.cs'

$src  = Read-Src $path
$crlf = $src.Text.Contains("`r`n")
$text = $src.Text -replace "`r`n", "`n"

if ($text.Contains('var emptied = new List<string>();')) {
    Write-Host ''
    Write-Host '  متعمول قبل كده، سيبته زي ما هو.' -ForegroundColor DarkGray
    Write-Host ''
    return
}

    $a1 = @'
        var warnings = new List<string>();
'@ -replace "`r`n", "`n"
    $b1 = @'
        var warnings = new List<string>();

        // ⚠ الملفات اللي اتشالت بالكامل بتتجمّع بالاسم، مش سطر لكل ملف.
        //
        // في تجربة حقيقية اتحمّل ٤٠ ملف وكلهم اتشالوا، فالأربعين تنبيه
        // اتلمّوا في **سطر واحد** ملا مربع النتايج كله وبقى مش ينفع يتقرا.
        var emptied = new List<string>();
'@ -replace "`r`n", "`n"
    $a2 = @'
                        warnings.Add($"الملف \"{Path.GetFileName(filePath)}\" اتشالت كل صفحاته");
'@ -replace "`r`n", "`n"
    $b2 = @'
                        emptied.Add(Path.GetFileName(filePath));
'@ -replace "`r`n", "`n"
    $a3 = @'
            ApplyOverlays(output, request, fileRanges, warnings);
'@ -replace "`r`n", "`n"
    $b3 = @'
            if (emptied.Count == 1)
            {
                warnings.Add($"الملف \"{emptied[0]}\" اتشالت كل صفحاته");
            }
            else if (emptied.Count > 1)
            {
                warnings.Add($"{emptied.Count} ملف اتشالت كل صفحاتهم: {FailureSummary.NameList(emptied)}");
            }

            ApplyOverlays(output, request, fileRanges, warnings);
'@ -replace "`r`n", "`n"

$text = Set-Snippet $text $a1 $b1 1
$text = Set-Snippet $text $a2 $b2 2
$text = Set-Snippet $text $a3 $b3 3

if ($crlf) { $text = $text -replace "`n", "`r`n" }

Copy-Item $path "$path.$stamp.bak"
Write-Src $path $text $src.Bom

Write-Host ''
Write-Host '  PdfMergeService.cs  →  اتعدّل (3 تعديلات).' -ForegroundColor Green
Write-Host ''

$after = (Read-Src $path).Text
$ok = $after.Contains('var emptied = new List<string>();') -and
      $after.Contains('FailureSummary.NameList(emptied)') -and
      -not $after.Contains('warnings.Add($"الملف \"{Path.GetFileName(filePath)}\"')

if ($ok) {
    Write-Host '    [تمام]  تجميع الملفات اللي اتشالت' -ForegroundColor Green
    Write-Host ''
    Write-Host '  شغّل دلوقتي:  .\build.ps1'      -ForegroundColor Green
    Write-Host '  المتوقّع: 1043 تست، صفر فشل.'   -ForegroundColor Green
    Write-Host ''
    Write-Host "  النسخة الاحتياطية: PdfMergeService.cs.$stamp.bak" -ForegroundColor DarkGray
}
else {
    Write-Host '  في حاجة ناقصة — ابعتلي الشاشة قبل ما تبني.' -ForegroundColor Red
}

Write-Host ''
