# PrintFlow - Virtual Printer Probe
#
# ONE QUESTION: can we create a printer called "PrintFlow" that any Windows app
# can print to, WITHOUT writing and signing a real printer driver?
#
# The idea being tested: take a driver Windows already ships with, bind it to a
# "Local Port" whose name is a FILE PATH, and see whether printing to it writes
# that file SILENTLY. If it does, we get a real printer name in the print dialog
# for free. If it always pops a Save-As dialog, the idea is dead and the Hot
# Folder plan is the answer instead.
#
# This is a PROBE, not an install. Everything it creates is named with the
# prefix below and is removed again in the finally block - including if you
# press Ctrl+C. It never touches your real printers and never changes your
# default printer.
#
# NOTE: written in English on purpose. This is a throwaway diagnostic and English
# avoids every PowerShell console code-page problem. The app itself stays Arabic.
#
# Usage:
#   Right-click PowerShell -> "Run as Administrator"     (Add-Printer needs it)
#   cd <project folder>
#   .\test-virtual-printer.ps1
#
#   .\test-virtual-printer.ps1 -ListDriversOnly    # just show what drivers exist
#   .\test-virtual-printer.ps1 -Keep               # leave the test printers behind
#
# WHAT TO WATCH ON YOUR SCREEN:
#   If a "Save Print Output As" window pops up, that driver FAILED the test.
#   Close it. The script also tries to detect it by itself and will say so.

param(
    [switch]$ListDriversOnly,
    [switch]$Keep,
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = 'Stop'

$Prefix   = 'PFProbe'
$WorkDir  = Join-Path $env:TEMP 'PrintFlowProbe'
$created  = @{ Printers = @(); Ports = @() }

# Drivers worth trying, best candidate first. Only ones Windows ships with -
# the whole point is to avoid installing anything.
$Candidates = @(
    @{ Driver = 'Microsoft Print To PDF';       Ext = 'pdf';  Note = 'best case - already a PDF, no conversion needed' },
    @{ Driver = 'Microsoft XPS Document Writer'; Ext = 'oxps'; Note = 'XPS - .NET can read it, but needs converting to PDF' },
    @{ Driver = 'Microsoft Shared Fax Driver';   Ext = 'bin';  Note = 'long shot' }
)

function Write-Head($text) {
    Write-Host ''
    Write-Host ('=' * 74) -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host ('=' * 74) -ForegroundColor Cyan
}

function Write-Ok   ($t) { Write-Host "  [OK]   $t" -ForegroundColor Green }
function Write-Bad  ($t) { Write-Host "  [NO]   $t" -ForegroundColor Red }
function Write-Info ($t) { Write-Host "  ...    $t" -ForegroundColor DarkGray }
function Write-Warn ($t) { Write-Host "  [!]    $t" -ForegroundColor Yellow }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# A Save-As dialog blocks everything and is the single most likely outcome.
# Detecting it ourselves means the script can say "this driver failed" instead
# of just timing out and looking broken.
function Find-SaveDialog {
    $titles = @('Save Print Output As', 'Save Output File As', 'Save As', 'Print to File')
    Get-Process |
        Where-Object { $_.MainWindowTitle } |
        Where-Object {
            $t = $_.MainWindowTitle
            $titles | Where-Object { $t -like "*$_*" }
        } |
        Select-Object -First 1
}

# Identify what actually got written, by its magic bytes rather than its name.
function Get-FileKind($path) {
    try {
        $bytes = Get-Content -LiteralPath $path -Encoding Byte -TotalCount 8 -ErrorAction Stop
    } catch {
        return 'unreadable'
    }

    if ($bytes.Count -lt 4) { return 'empty-ish' }

    $ascii = -join ($bytes[0..3] | ForEach-Object { [char]$_ })

    switch -Regex ($ascii) {
        '^%PDF' { return 'PDF     <-- ready to use as-is' }
        '^PK'   { return 'ZIP/XPS <-- XPS package, needs converting' }
        '^%!PS' { return 'PostScript <-- needs Ghostscript, dead end for us' }
        default { return "unknown (starts with: $ascii)" }
    }
}

function New-ProbePrinter($driver, $portPath, $printerName) {
    Add-PrinterPort -Name $portPath -ErrorAction Stop
    $created.Ports += $portPath

    Add-Printer -Name $printerName -DriverName $driver -PortName $portPath -ErrorAction Stop
    $created.Printers += $printerName
}

function Remove-Probes {
    Write-Head 'CLEANING UP'

    foreach ($p in $created.Printers) {
        try {
            Remove-Printer -Name $p -ErrorAction Stop
            Write-Ok "removed printer $p"
        } catch {
            Write-Warn "could NOT remove printer $p - remove it by hand from Settings > Printers"
        }
    }

    foreach ($port in $created.Ports) {
        try {
            Remove-PrinterPort -Name $port -ErrorAction Stop
            Write-Ok "removed port $port"
        } catch {
            Write-Warn "could NOT remove port $port - remove it by hand from Print Server Properties > Ports"
        }
    }

    if (-not $created.Printers -and -not $created.Ports) {
        Write-Info 'nothing to clean up'
    }
}

# ============================================================================

try {
    Write-Head 'PrintFlow - can we get a printer name without a driver?'

    if (-not (Test-Admin)) {
        Write-Bad 'This script needs Administrator.'
        Write-Info 'Close this window, right-click PowerShell, "Run as Administrator", and run it again.'
        return
    }
    Write-Ok 'running as Administrator'

    # ---------- what drivers does this machine already have ----------

    Write-Head 'STEP 1 - drivers already installed on this machine'

    $installed = Get-PrinterDriver | Select-Object -ExpandProperty Name
    $installed | Sort-Object | ForEach-Object { Write-Host "         $_" }

    $usable = @()
    foreach ($c in $Candidates) {
        if ($installed -contains $c.Driver) {
            Write-Ok "found: $($c.Driver)   ($($c.Note))"
            $usable += $c
        } else {
            Write-Info "not installed: $($c.Driver)"
        }
    }

    if (-not $usable) {
        Write-Bad 'None of the candidate drivers are on this machine. Nothing to test.'
        return
    }

    if ($ListDriversOnly) { return }

    # ---------- something to print ----------

    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    Get-ChildItem -Path $WorkDir -File -ErrorAction SilentlyContinue | Remove-Item -Force

    $sample = Join-Path $WorkDir 'sample.txt'
    @(
        'PrintFlow virtual printer probe',
        '',
        'If you are reading this inside a PDF or XPS file that appeared',
        'on its own with no dialog, the test PASSED.'
    ) | Set-Content -LiteralPath $sample -Encoding ASCII

    # ---------- try each driver ----------

    $results = @()

    foreach ($c in $usable) {
        $name = "$Prefix-$($c.Driver -replace '[^A-Za-z0-9]', '')"
        $out  = Join-Path $WorkDir "out-$($c.Ext).$($c.Ext)"

        Write-Head "STEP 2 - trying: $($c.Driver)"
        Write-Info "printer name : $name"
        Write-Info "port (a file): $out"

        $verdict = 'FAILED'
        $detail  = ''

        try {
            New-ProbePrinter -driver $c.Driver -portPath $out -printerName $name
            Write-Ok 'printer created'

            Write-Info 'sending a print job now - WATCH YOUR SCREEN for a Save dialog'
            Get-Content -LiteralPath $sample | Out-Printer -Name $name

            # Poll for the file. A dialog waiting for a human looks exactly like
            # a slow job from here, so we check for the dialog too.
            $deadline = (Get-Date).AddSeconds($WaitSeconds)
            $landed   = $false
            $dialog   = $null

            while ((Get-Date) -lt $deadline) {
                if (Test-Path -LiteralPath $out) {
                    $size = (Get-Item -LiteralPath $out).Length
                    if ($size -gt 0) { $landed = $true; break }
                }

                $dialog = Find-SaveDialog
                if ($dialog) { break }

                Start-Sleep -Milliseconds 500
            }

            if ($dialog) {
                Write-Bad "a dialog opened: '$($dialog.MainWindowTitle)'"
                Write-Warn 'CLOSE THAT WINDOW NOW, then let the script continue.'
                $detail = "prompts with: $($dialog.MainWindowTitle)"
            }
            elseif ($landed) {
                $size = (Get-Item -LiteralPath $out).Length
                $kind = Get-FileKind $out
                Write-Ok "file written silently - $size bytes"
                Write-Ok "format: $kind"
                $verdict = 'PASSED'
                $detail  = "$size bytes, $kind"
            }
            else {
                Write-Bad "nothing appeared within $WaitSeconds seconds"
                Write-Info 'either the driver ignores the port, or a dialog is open somewhere'
                $detail = 'no output, no dialog detected'
            }
        }
        catch {
            Write-Bad "error: $($_.Exception.Message)"
            $detail = $_.Exception.Message
        }
        finally {
            # Never leave a queued job behind on a probe printer
            try {
                Get-PrintJob -PrinterName $name -ErrorAction SilentlyContinue |
                    Remove-PrintJob -ErrorAction SilentlyContinue
            } catch { }
        }

        $results += [pscustomobject]@{
            Driver  = $c.Driver
            Verdict = $verdict
            Detail  = $detail
        }
    }

    # ---------- the answer ----------

    Write-Head 'RESULT'

    $results | Format-Table -AutoSize | Out-String | Write-Host

    $winner = $results | Where-Object { $_.Verdict -eq 'PASSED' } | Select-Object -First 1

    if ($winner) {
        Write-Ok "GOOD NEWS: '$($winner.Driver)' writes a file silently."
        Write-Info 'A printer named PrintFlow in the print dialog is possible with no driver'
        Write-Info 'and no code signing. Send this whole output back and I will build it.'
    } else {
        Write-Warn 'No driver wrote a file silently on this machine.'
        Write-Info 'That kills the fake-printer idea - and it is the expected result,'
        Write-Info 'so it is not a failure of the test. Hot Folder + right-click menu'
        Write-Info 'is the plan. Send this output back either way.'
    }

    Write-Host ''
    Write-Info "anything written is under: $WorkDir"
}
finally {
    if ($Keep) {
        Write-Head 'LEAVING TEST PRINTERS BEHIND (-Keep)'
        $created.Printers | ForEach-Object { Write-Warn "still installed: $_" }
        Write-Warn 'remove them from Settings > Bluetooth & devices > Printers when done'
    } else {
        Remove-Probes
    }

    Write-Host ''
    Write-Host 'Done.' -ForegroundColor Cyan
}
