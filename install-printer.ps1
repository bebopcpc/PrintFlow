# PrintFlow - install (or remove) the PrintFlow virtual printer
#
# Creates a printer called "PrintFlow" that shows up in every program's print
# dialog. Printing to it drops a PDF into a folder that PrintFlow watches.
#
# NO DRIVER IS INSTALLED. It reuses "Microsoft Print To PDF", which Windows
# already ships with, bound to a Local Port that is a file path. That was
# verified on a real machine with test-virtual-printer.ps1 - the PDF is
# written silently, with no Save dialog.
#
# NOTE: written in English on purpose. This is an admin script and English
# avoids every PowerShell console code-page problem. The app itself stays Arabic.
#
# Usage (right-click PowerShell -> "Run as Administrator"):
#   .\install-printer.ps1                    # install
#   .\install-printer.ps1 -Remove            # uninstall
#   .\install-printer.ps1 -Status            # just show what is there now
#   .\install-printer.ps1 -FixPermissions    # fix "Access to the path is denied"
#
# Where things go:
#   C:\ProgramData\PrintFlow\spool\incoming.pdf   <- the port; Windows writes here
#   C:\ProgramData\PrintFlow\queue\               <- PrintFlow moves finished jobs here
#
# ProgramData and not your Temp folder on purpose: the Windows print spooler
# runs as SYSTEM and may have no rights to a user's Temp folder at all.

param(
    [switch]$Remove,
    [switch]$Status,
    [switch]$FixPermissions,
    [string]$PrinterName = 'PrintFlow',
    [string]$Driver = 'Microsoft Print To PDF'
)

$ErrorActionPreference = 'Stop'

$Root  = Join-Path $env:ProgramData 'PrintFlow'
$Spool = Join-Path $Root 'spool'
$Queue = Join-Path $Root 'queue'
$Port  = Join-Path $Spool 'incoming.pdf'

function Write-Head($t) {
    Write-Host ''
    Write-Host ('=' * 70) -ForegroundColor Cyan
    Write-Host "  $t" -ForegroundColor Cyan
    Write-Host ('=' * 70) -ForegroundColor Cyan
}

function Write-Ok  ($t) { Write-Host "  [OK]   $t" -ForegroundColor Green }
function Write-Bad ($t) { Write-Host "  [NO]   $t" -ForegroundColor Red }
function Write-Info($t) { Write-Host "  ...    $t" -ForegroundColor DarkGray }
function Write-Warn($t) { Write-Host "  [!]    $t" -ForegroundColor Yellow }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ---------------------------------------------------------------------------
# THE PERMISSION FIX
#
# This is the bit that was wrong in 1.9.0 and broke the first real job.
#
# The print spooler runs as SYSTEM, so SYSTEM CREATES the port file and owns
# it. Under ProgramData the default rights let a normal user READ such a file
# but not DELETE it - and File.Move needs DELETE on the source. So PrintFlow,
# running as the logged-in user, got:
#
#     Access to the path is denied.
#
# Granting SYSTEM rights (what 1.9.0 did) never helped: SYSTEM was never the
# one that was blocked.
#
# We grant BUILTIN\Users "Modify", which includes Delete, and mark it
# inheritable so files the spooler creates LATER pick it up too.
#
# The group is addressed by SID, not by name: on a non-English Windows the
# name is localised and 'Users' would fail to resolve.
# ---------------------------------------------------------------------------
function Set-SpoolPermissions {
    $ok = $true

    foreach ($folder in @($Spool, $Queue)) {
        try {
            $acl = Get-Acl $folder

            # S-1-5-32-545 = BUILTIN\Users   (language independent)
            $users = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')

            # S-1-5-18 = NT AUTHORITY\SYSTEM
            $system = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')

            foreach ($who in @($users, $system)) {
                $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                    $who,
                    'Modify',
                    'ContainerInherit,ObjectInherit',
                    'None',
                    'Allow')
                $acl.AddAccessRule($rule)
            }

            Set-Acl -Path $folder -AclObject $acl
            Write-Ok "permissions set on $folder"
        }
        catch {
            Write-Bad "could not set permissions on ${folder}: $($_.Exception.Message)"
            $ok = $false
        }
    }

    # Files that already exist were created BEFORE the folder rule, so they do
    # not inherit it. Fix them one by one.
    foreach ($folder in @($Spool, $Queue)) {
        foreach ($file in @(Get-ChildItem -Path $folder -File -ErrorAction SilentlyContinue)) {
            try {
                $acl = Get-Acl $file.FullName
                $users = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')
                $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                    $users, 'Modify', 'None', 'None', 'Allow')
                $acl.AddAccessRule($rule)
                Set-Acl -Path $file.FullName -AclObject $acl
                Write-Ok "fixed the file already sitting there: $($file.Name)"
            }
            catch {
                Write-Warn "could not fix $($file.Name) - delete it by hand and print again"
            }
        }
    }

    return $ok
}

function Get-State {
    [pscustomobject]@{
        Printer = [bool](Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue)
        Port    = [bool](Get-PrinterPort -Name $Port -ErrorAction SilentlyContinue)
        Folders = (Test-Path $Spool) -and (Test-Path $Queue)
    }
}

function Show-State {
    $s = Get-State

    Write-Head 'CURRENT STATE'
    Write-Host "  printer '$PrinterName' : $(if ($s.Printer) { 'installed' } else { 'not installed' })"
    Write-Host "  port                   : $(if ($s.Port) { 'exists' } else { 'missing' })"
    Write-Host "  folders                : $(if ($s.Folders) { 'ready' } else { 'missing' })"
    Write-Host "  port path              : $Port"
    Write-Host "  queue folder           : $Queue"

    if ($s.Folders) {
        $waiting = @(Get-ChildItem -Path $Queue -Filter *.pdf -ErrorAction SilentlyContinue)
        Write-Host "  jobs waiting in queue  : $($waiting.Count)"
    }
}

# ============================================================================

if ($Status) {
    Show-State
    return
}

if (-not (Test-Admin)) {
    Write-Bad 'This script needs Administrator.'
    Write-Info 'Close this window, right-click PowerShell, "Run as Administrator", and run it again.'
    return
}

if ($FixPermissions) {
    if (-not (Test-Admin)) {
        Write-Bad 'This needs Administrator.'
        Write-Info 'Right-click PowerShell -> "Run as Administrator", then run it again.'
        return
    }

    Write-Head 'FIXING PERMISSIONS ON THE SPOOL FOLDERS'

    New-Item -ItemType Directory -Force -Path $Spool | Out-Null
    New-Item -ItemType Directory -Force -Path $Queue | Out-Null

    if (Set-SpoolPermissions) {
        Write-Ok 'done - print again from any program, PrintFlow should pick it up now'
    } else {
        Write-Warn 'some folders could not be fixed - see the errors above'
    }

    return
}

if ($Remove) {
    Write-Head "REMOVING THE $PrinterName PRINTER"

    # printer first: a port that a printer still uses cannot be removed
    try {
        if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) {
            Remove-Printer -Name $PrinterName
            Write-Ok "removed printer $PrinterName"
        } else {
            Write-Info 'printer was not installed'
        }
    } catch {
        Write-Bad "could not remove the printer: $($_.Exception.Message)"
    }

    try {
        if (Get-PrinterPort -Name $Port -ErrorAction SilentlyContinue) {
            Remove-PrinterPort -Name $Port
            Write-Ok 'removed the port'
        } else {
            Write-Info 'port was not there'
        }
    } catch {
        Write-Bad "could not remove the port: $($_.Exception.Message)"
    }

    Write-Info "folders were left alone: $Root"
    Write-Info 'delete them yourself if you want - check the queue for unprinted jobs first'
    return
}

# ---------- install ----------

Write-Head "INSTALLING THE $PrinterName PRINTER"

if (-not (Get-PrinterDriver -Name $Driver -ErrorAction SilentlyContinue)) {
    Write-Bad "'$Driver' is not on this machine."
    Write-Info 'Turn it on: Settings > System > Optional features > More Windows features'
    Write-Info '           -> tick "Microsoft Print to PDF" -> OK, then run this again.'
    return
}
Write-Ok "found the built-in driver: $Driver"

New-Item -ItemType Directory -Force -Path $Spool | Out-Null
New-Item -ItemType Directory -Force -Path $Queue | Out-Null
Write-Ok "folders ready under $Root"

Set-SpoolPermissions

$state = Get-State

if ($state.Printer) {
    Write-Warn "printer '$PrinterName' already exists - removing it first so this is a clean install"
    try { Remove-Printer -Name $PrinterName } catch { }
}

if (-not (Get-PrinterPort -Name $Port -ErrorAction SilentlyContinue)) {
    Add-PrinterPort -Name $Port
    Write-Ok "created the port: $Port"
} else {
    Write-Info 'port already existed'
}

Add-Printer -Name $PrinterName -DriverName $Driver -PortName $Port
Write-Ok "created the printer: $PrinterName"

# ---------- prove it actually works ----------

Write-Head 'CHECKING IT'

$sample = Join-Path $env:TEMP 'printflow-install-check.txt'
@(
    'PrintFlow virtual printer',
    '',
    'If PrintFlow picked this up, the printer works.'
) | Set-Content -LiteralPath $sample -Encoding ASCII

Write-Info 'sending a test page through the new printer'
Get-Content -LiteralPath $sample | Out-Printer -Name $PrinterName

$deadline = (Get-Date).AddSeconds(20)
$landed = $false

while ((Get-Date) -lt $deadline) {
    # PrintFlow may already have moved it to the queue - either is a success
    if ((Test-Path $Port) -or @(Get-ChildItem $Queue -Filter *.pdf -EA SilentlyContinue).Count -gt 0) {
        $landed = $true
        break
    }
    Start-Sleep -Milliseconds 500
}

Remove-Item -LiteralPath $sample -Force -ErrorAction SilentlyContinue

if ($landed) {
    Write-Ok 'a PDF appeared - the printer works'
} else {
    Write-Warn 'nothing appeared within 20 seconds'
    Write-Info 'check that the spool folder is writable, then try printing from Notepad'
}

Show-State

Write-Head 'HOW TO USE IT'
Write-Host '  1. Open PrintFlow and turn on "استقبال من طابعة PrintFlow"'
Write-Host '  2. In ANY program: Ctrl+P -> pick "PrintFlow" -> Print'
Write-Host '  3. The file shows up in the PrintFlow file list, ready to process'
Write-Host ''
Write-Info "to remove it later:  .\install-printer.ps1 -Remove"
Write-Host ''
