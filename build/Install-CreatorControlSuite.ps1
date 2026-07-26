param(
    [string]$InstallDir = ""
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Version = "2.0.81"
$ProductName = "Creator Control Suite"
$ScriptPath = $MyInvocation.MyCommand.Path
$ScriptRoot = Split-Path -Parent $ScriptPath
$Payload = Join-Path $ScriptRoot "Payload"

function Ensure-Administrator {
    $Identity =
        [Security.Principal.WindowsIdentity]::GetCurrent()

    $Principal =
        New-Object `
            Security.Principal.WindowsPrincipal `
            $Identity

    $IsAdmin =
        $Principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($IsAdmin) {
        return
    }

    $Arguments =
        '-NoProfile -ExecutionPolicy Bypass -File "' +
        $ScriptPath +
        '"'

    if (-not [string]::IsNullOrWhiteSpace($InstallDir)) {
        $Arguments +=
            ' -InstallDir "' +
            $InstallDir +
            '"'
    }

    $ElevatedProcess =
        Start-Process `
            -FilePath "powershell.exe" `
            -ArgumentList $Arguments `
            -Verb RunAs `
            -Wait `
            -PassThru

    if ($null -eq $ElevatedProcess) {
        throw "Der erhöhte Installationsprozess konnte nicht gestartet werden."
    }

    exit $ElevatedProcess.ExitCode
}

function Get-ExistingInstallLocation {
    $RegistryPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    $Entry =
        Get-ItemProperty `
            $RegistryPaths `
            -ErrorAction SilentlyContinue |
        Where-Object {
            $_.DisplayName -eq $ProductName
        } |
        Select-Object -First 1

    if ($null -ne $Entry -and
        -not [string]::IsNullOrWhiteSpace($Entry.InstallLocation)) {
        return $Entry.InstallLocation
    }

    $Default =
        Join-Path `
            $env:ProgramFiles `
            "Creator Control Suite"

    if (Test-Path -LiteralPath $Default -PathType Container) {
        return $Default
    }

    return ""
}

function Remove-OldMsiRegistration {
    $RegistryPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    $Entries =
        Get-ItemProperty `
            $RegistryPaths `
            -ErrorAction SilentlyContinue |
        Where-Object {
            $_.DisplayName -eq $ProductName -and
            $_.UninstallString -match 'MsiExec'
        }

    foreach ($Entry in @($Entries)) {
        if ($Entry.UninstallString -match '\{[0-9A-Fa-f-]{36}\}') {
            $ProductCode = $Matches[0]

            Write-Host "Vorhandene MSI-Version wird automatisch ersetzt: $ProductCode"

            $Process =
                Start-Process `
                    -FilePath "msiexec.exe" `
                    -ArgumentList @(
                        "/x",
                        $ProductCode,
                        "/qn",
                        "/norestart"
                    ) `
                    -Wait `
                    -PassThru

            if ($Process.ExitCode -notin @(0, 1605, 1614)) {
                throw "Alte MSI-Version konnte nicht entfernt werden. ExitCode: $($Process.ExitCode)"
            }
        }
    }
}

function Create-Shortcut {
    param(
        [Parameter(Mandatory=$true)]
        [string]$ShortcutPath,

        [Parameter(Mandatory=$true)]
        [string]$TargetPath
    )

    $Shell =
        New-Object `
            -ComObject WScript.Shell

    $Shortcut =
        $Shell.CreateShortcut(
            $ShortcutPath)

    $Shortcut.TargetPath = $TargetPath
    $Shortcut.WorkingDirectory =
        Split-Path -Parent $TargetPath
    $Shortcut.Description =
        "Creator Control Suite"
    $Shortcut.Save()
}

Ensure-Administrator

if (-not (Test-Path -LiteralPath $Payload -PathType Container)) {
    throw "Setup-Payload fehlt: $Payload"
}

$ExistingLocation =
    Get-ExistingInstallLocation

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    if (-not [string]::IsNullOrWhiteSpace($ExistingLocation)) {
        $InstallDir = $ExistingLocation
    }
    else {
        $Default =
            Join-Path `
                $env:ProgramFiles `
                "Creator Control Suite"

        Write-Host ""
        Write-Host "Installationsordner"
        Write-Host "Standard: $Default"

        $Input =
            Read-Host `
                "Anderen Ordner eingeben oder ENTER für Standard"

        $InstallDir =
            if ([string]::IsNullOrWhiteSpace($Input)) {
                $Default
            }
            else {
                $Input.Trim()
            }
    }
}

$InstallDir =
    [IO.Path]::GetFullPath(
        $InstallDir)

Write-Host ""
Write-Host "$ProductName $Version"
Write-Host "Ziel: $InstallDir"
Write-Host ""

# Stop old instance.
Get-Process `
    -Name "CreatorControlSuite" `
    -ErrorAction SilentlyContinue |
    Stop-Process `
        -Force `
        -ErrorAction SilentlyContinue

Start-Sleep -Milliseconds 500

# Preserve user data: nothing under LocalAppData is touched.
$UserData =
    Join-Path `
        $env:LOCALAPPDATA `
        "CreatorControlSuite"

Write-Host "Benutzerdaten bleiben erhalten: $UserData"

# If an old MSI installation exists, remove only the program registration/files.
# User data under LocalAppData remains untouched.
Remove-OldMsiRegistration

$BackupRoot =
    Join-Path `
        $env:TEMP `
        ("CreatorControlSuite-Backup-" + [guid]::NewGuid().ToString("N"))

$HadExisting =
    Test-Path `
        -LiteralPath $InstallDir `
        -PathType Container

try {
    if ($HadExisting) {
        Write-Host "Bestehende Installation wird gesichert ..."

        Move-Item `
            -LiteralPath $InstallDir `
            -Destination $BackupRoot `
            -Force
    }

    [void](New-Item `
        -ItemType Directory `
        -Path $InstallDir `
        -Force)

    Write-Host "Neue Version wird installiert ..."

    Copy-Item `
        -Path (Join-Path $Payload "*") `
        -Destination $InstallDir `
        -Recurse `
        -Force

    $MainExe =
        Join-Path `
            $InstallDir `
            "CreatorControlSuite.exe"

    if (-not (Test-Path -LiteralPath $MainExe -PathType Leaf)) {
        throw "CreatorControlSuite.exe fehlt nach der Installation."
    }

    # Install local uninstaller script.
    $InstalledUninstaller =
        Join-Path `
            $InstallDir `
            "Uninstall-CreatorControlSuite.ps1"

    @'
Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Get-Process -Name "CreatorControlSuite" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

$StartMenu =
    Join-Path `
        $env:ProgramData `
        "Microsoft\Windows\Start Menu\Programs\Creator Control Suite.lnk"

Remove-Item -LiteralPath $StartMenu -Force -ErrorAction SilentlyContinue

$RegPath =
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CreatorControlSuite-Clean"

Remove-Item -LiteralPath $RegPath -Recurse -Force -ErrorAction SilentlyContinue

$Cleanup =
    'Start-Sleep -Seconds 2; Remove-Item -LiteralPath "' +
    $InstallDir +
    '" -Recurse -Force -ErrorAction SilentlyContinue'

Start-Process `
    -FilePath "powershell.exe" `
    -ArgumentList @(
        "-NoProfile",
        "-WindowStyle",
        "Hidden",
        "-Command",
        $Cleanup
    )
'@ | Set-Content `
        -LiteralPath $InstalledUninstaller `
        -Encoding UTF8

    $StartMenuDir =
        Join-Path `
            $env:ProgramData `
            "Microsoft\Windows\Start Menu\Programs"

    [void](New-Item `
        -ItemType Directory `
        -Path $StartMenuDir `
        -Force)

    $ShortcutPath =
        Join-Path `
            $StartMenuDir `
            "Creator Control Suite.lnk"

    Create-Shortcut `
        -ShortcutPath $ShortcutPath `
        -TargetPath $MainExe

    $RegPath =
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CreatorControlSuite-Clean"

    [void](New-Item `
        -Path $RegPath `
        -Force)

    New-ItemProperty `
        -Path $RegPath `
        -Name "DisplayName" `
        -Value $ProductName `
        -PropertyType String `
        -Force | Out-Null

    New-ItemProperty `
        -Path $RegPath `
        -Name "DisplayVersion" `
        -Value $Version `
        -PropertyType String `
        -Force | Out-Null

    New-ItemProperty `
        -Path $RegPath `
        -Name "Publisher" `
        -Value "Creator Control Suite" `
        -PropertyType String `
        -Force | Out-Null

    New-ItemProperty `
        -Path $RegPath `
        -Name "InstallLocation" `
        -Value $InstallDir `
        -PropertyType String `
        -Force | Out-Null

    $UninstallString =
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' +
        $InstalledUninstaller +
        '"'

    New-ItemProperty `
        -Path $RegPath `
        -Name "UninstallString" `
        -Value $UninstallString `
        -PropertyType String `
        -Force | Out-Null

    if (Test-Path -LiteralPath $BackupRoot) {
        Remove-Item `
            -LiteralPath $BackupRoot `
            -Recurse `
            -Force
    }

    Write-Host ""
    Write-Host "Installation/Update erfolgreich." -ForegroundColor Green
    Write-Host "Start: $MainExe"
}
catch {
    Write-Host ""
    Write-Host "Installation fehlgeschlagen. Vorherige Version wird wiederhergestellt." -ForegroundColor Red

    Remove-Item `
        -LiteralPath $InstallDir `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue

    if (Test-Path -LiteralPath $BackupRoot -PathType Container) {
        Move-Item `
            -LiteralPath $BackupRoot `
            -Destination $InstallDir `
            -Force
    }

    throw
}
