# PrintFlow - Print Path Test (no paper)
#
# Pauses a REAL printer, sends a job through SumatraPDF using the EXACT same
# arguments PrintFlow builds, inspects what landed in the spooler, then purges
# the queue and un-pauses the printer.
#
# Why a paused real printer instead of a virtual one:
#   SumatraPDF renders every page and hands it to the Windows spooler, then exits.
#   Pausing the queue does not change that work at all - the spooler just holds
#   the data instead of feeding the rollers. So you get the real driver, the real
#   render time, and the real exit code, with zero paper.
#
# NOTE: written in English on purpose. This is a throwaway diagnostic and English
# avoids every PowerShell console code-page problem. The app itself stays Arabic.
#
# NO PHYSICAL PRINTER? Use the one Windows ships with:
#   .\test-print.ps1 -Printer "Microsoft Print to PDF" -Pdf .\test.pdf
# It is always Ready, so the whole path gets exercised. Pausing its queue also
# stops its Save-As dialog from ever appearing, so nothing blocks.
#
# Usage:
#   .\test-print.ps1                                   # list printers and exit
#   .\test-print.ps1 -Printer "HP LaserJet" -Pdf .\test.pdf
#   .\test-print.ps1 -Printer "HP LaserJet" -Pdf .\big.pdf -Copies 5 -Grayscale
#   .\test-print.ps1 -Printer "HP LaserJet" -Pdf .\a.pdf -KeepJob    # leave job queued
#
# You may need to run PowerShell as Administrator to pause a printer.

param(
    [string]$Printer,
    [string]$Pdf,
    [int]$Copies = 1,
    [ValidateSet('A2', 'A3', 'A4', 'A5', 'A6', 'Letter', 'Legal', 'Tabloid', 'Statement')]
    [string]$Paper = 'A4',
    [ValidateSet('Portrait', 'Landscape')]
    [string]$Orientation = 'Portrait',
    [switch]$Grayscale,
    [switch]$Duplex,
    [ValidateSet('Long', 'Short')]
    [string]$DuplexFlip = 'Long',
    [switch]$KeepJob
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

function Write-Step($text) { Write-Host "`n$text" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "  OK   $text" -ForegroundColor Green }
function Write-Bad($text) { Write-Host "  FAIL $text" -ForegroundColor Red }
function Write-Info($text) { Write-Host "  ...  $text" -ForegroundColor DarkGray }

# ---------- locate SumatraPDF (same place the app looks) ----------

$sumatra = @(
    (Join-Path $root 'publish\tools\SumatraPDF.exe'),
    (Join-Path $root 'tools\SumatraPDF.exe'),
    (Join-Path $root 'SumatraPDF.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $sumatra) {
    throw "SumatraPDF.exe not found. Looked in publish\tools\, tools\ and the current folder."
}

# ---------- no printer given: list them and stop ----------

if (-not $Printer) {
    Write-Step "Printers on this machine:"
    Get-WmiObject -Class Win32_Printer |
        Select-Object Name, @{n = 'Default'; e = { $_.Default } }, @{n = 'Status'; e = { $_.PrinterStatus } }, PortName |
        Format-Table -AutoSize
    Write-Host "Re-run with:  .\test-print.ps1 -Printer ""<name>"" -Pdf .\file.pdf`n"
    return
}

if (-not $Pdf) { throw "Give me a PDF with -Pdf .\file.pdf" }

# ---------- mirror PrintFlow's own eligibility rule ----------
# PrinterStatusMapper.Map + PrinterItem.IsEligible. If the app would refuse to
# use this printer, testing it here proves nothing about the app.

function Get-PrintFlowStatus($wmiPrinter) {
    if ([bool]$wmiPrinter.WorkOffline) { return 'Offline' }

    switch ([int]$wmiPrinter.PrinterStatus) {
        3 { 'Ready' }; 4 { 'Ready' }; 5 { 'Ready' }
        7 { 'Offline' }
        2 { 'Error' }
        default { 'Unknown' }
    }
}

$Pdf = (Resolve-Path $Pdf).Path
if (-not (Test-Path $Pdf)) { throw "PDF not found: $Pdf" }

if (-not (Get-Command Get-PrintJob -ErrorAction SilentlyContinue)) {
    throw "Get-PrintJob is missing. This script needs Windows 8 / Server 2012 or newer."
}

# escaped separately: nested quoting inside "$( )" is fragile on PowerShell 5.1
$escapedName = $Printer.Replace("'", "''")
$queue = @(Get-WmiObject -Class Win32_Printer -Filter "Name='$escapedName'") | Select-Object -First 1
if (-not $queue) { throw "Printer not found: $Printer" }

# ---------- build the exact same -print-settings string the app builds ----------
# Mirrors SumatraArguments.BuildPrintSettings. Keep the two in sync.

$paperMap = @{
    'A2' = 'A2'; 'A3' = 'A3'; 'A4' = 'A4'; 'A5' = 'A5'; 'A6' = 'A6'
    'Letter' = 'letter'; 'Legal' = 'legal'; 'Tabloid' = 'tabloid'; 'Statement' = 'statement'
}

$parts = @()
if ($Copies -gt 1) { $parts += "$($Copies)x" }
$parts += "paper=$($paperMap[$Paper])"
$parts += $(if ($Orientation -eq 'Landscape') { 'landscape' } else { 'portrait' })
$parts += 'noscale'
$parts += $(if ($Grayscale) { 'monochrome' } else { 'color' })
$parts += $(if ($Duplex) { if ($DuplexFlip -eq 'Short') { 'duplexshort' } else { 'duplexlong' } } else { 'simplex' })

$settings = $parts -join ','

$status = Get-PrintFlowStatus $queue
$eligible = $status -ne 'Offline' -and $status -ne 'Error'

Write-Step "Test plan"
Write-Info "Sumatra : $sumatra"
Write-Info "Printer : $Printer"
Write-Info "Status  : $status  (WorkOffline=$($queue.WorkOffline), PrinterStatus=$($queue.PrinterStatus))"
Write-Info "File    : $Pdf"

if (-not $eligible) {
    Write-Host ""
    Write-Bad "PrintFlow itself would SKIP this printer - it is '$status'."
    Write-Bad "The app would say 'no eligible printer available' and never call SumatraPDF."
    Write-Bad "Testing it here would only prove that an unreachable printer is unreachable."
    Write-Host ""
    Write-Host "  Use a printer that is always Ready instead. Microsoft Print to PDF is built in:" -ForegroundColor Yellow
    Write-Host "    .\test-print.ps1 -Printer ""Microsoft Print to PDF"" -Pdf ""$Pdf""" -ForegroundColor Yellow
    Write-Host "  (pausing its queue stops the Save-As dialog from ever appearing)`n" -ForegroundColor DarkGray
    return
}
Write-Host "  Command : -print-to ""$Printer"" -print-settings ""$settings"" -silent ""$Pdf""" -ForegroundColor Yellow

$paused = $false

try {
    # ---------- pause + clear the queue ----------

    Write-Step "Pausing the printer (no paper will move)"
    $result = $queue.Pause()

    if ($result.ReturnValue -eq 0) {
        $paused = $true
        Write-Ok "Queue paused"
    }
    else {
        Write-Bad "Could not pause (code $($result.ReturnValue)). Try running PowerShell as Administrator."
        Write-Bad "STOPPING - refusing to send a job that would actually print."
        return
    }

    try { Get-PrintJob -PrinterName $Printer -ErrorAction Stop | Remove-PrintJob -ErrorAction SilentlyContinue } catch { }
    Write-Info "Queue cleared"

    # ---------- run SumatraPDF exactly like the app does ----------

    Write-Step "Running SumatraPDF"

    # NOT $args - that is an automatic PowerShell variable and shadowing it misbehaves
    $sumatraArgs = @('-print-to', $Printer, '-print-settings', $settings, '-silent', $Pdf)

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $sumatra -ArgumentList $sumatraArgs -NoNewWindow -PassThru -Wait
    $clock.Stop()

    $seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1)

    if ($process.ExitCode -eq 0) {
        Write-Ok "Exit code 0 after $seconds s"
    }
    else {
        Write-Bad "Exit code $($process.ExitCode) after $seconds s - usually a bad printer name or the printer is unavailable"
    }

    # ---------- look at what actually reached the spooler ----------

    Write-Step "What landed in the queue"

    $jobs = $null
    foreach ($try in 1..10) {
        try { $jobs = @(Get-PrintJob -PrinterName $Printer -ErrorAction Stop) } catch { $jobs = @() }
        if ($jobs.Count -gt 0) { break }
        Start-Sleep -Milliseconds 400
    }

    if (-not $jobs -or $jobs.Count -eq 0) {
        Write-Bad "Nothing in the queue. The job never reached the spooler."

        if ($process.ExitCode -eq 0) {
            Write-Host ""
            Write-Host "  >>> EXIT CODE 0 BUT NOTHING PRINTED <<<" -ForegroundColor Magenta
            Write-Host "  SumatraPDF's -silent flag swallows the error and still returns 0." -ForegroundColor Magenta
            Write-Host "  So a zero exit code is NOT proof that anything printed." -ForegroundColor Magenta
            Write-Host ""
            Write-Info "Most likely: the printer is installed but not actually reachable."
            Write-Info "The $([math]::Round($seconds)) s it took is Windows retrying an unreachable device, not real work."
        }
    }
    else {
        foreach ($job in $jobs) {
            Write-Ok "Job $($job.Id): '$($job.DocumentName)'"
            Write-Info "pages reported : $($job.TotalPages)"
            Write-Info "size           : $([math]::Round($job.Size / 1KB)) KB"
            Write-Info "status         : $($job.JobStatus)"
            Write-Info "submitted      : $($job.SubmittedTime)"
        }

        # Mirrors SpoolTimeoutPolicy: 5 min floor + 1 min per 100 sheets, capped at 120.
        # Approximate: the spooler's TotalPages may or may not already include copies,
        # so treat this as a sanity check, not the exact number the app computes.
        $sheets = [double]$jobs[0].TotalPages
        if ($sheets -le 0) { $allowed = 15.0 } else { $allowed = [math]::Min(120.0, 5.0 + $sheets / 100.0) }

        Write-Host "`n  Spool time measured : $seconds s" -ForegroundColor Yellow
        Write-Host "  Timeout allowed     : ~$([math]::Round($allowed, 1)) min (approx)" -ForegroundColor Yellow

        if ($clock.Elapsed.TotalMinutes -gt 2) {
            Write-Host "  NOTE: this job took over 2 minutes - the OLD fixed timeout would have KILLED it." -ForegroundColor Magenta
        }
    }
}
finally {
    # ALWAYS put the printer back, even if something above blew up.
    # A print shop must never find its printer left paused by a test script.

    Write-Step "Cleaning up"

    if (-not $KeepJob) {
        try { Get-PrintJob -PrinterName $Printer -ErrorAction Stop | Remove-PrintJob -ErrorAction SilentlyContinue } catch { }
        Write-Ok "Queue purged - nothing will print"
    }
    else {
        Write-Bad "-KeepJob was set: the job is STILL QUEUED and will print when you resume the printer"
    }

    if ($paused) {
        try {
            $fresh = Get-WmiObject -Class Win32_Printer -Filter "Name='$($Printer -replace "'", "''")'"
            $null = $fresh.Resume()
            Write-Ok "Printer resumed"
        }
        catch {
            Write-Bad "COULD NOT RESUME '$Printer' - un-pause it by hand from Devices and Printers!"
        }
    }
}

Write-Host ""
