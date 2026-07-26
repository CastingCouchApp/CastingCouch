$ErrorActionPreference = "Continue"
$Out = Join-Path $env:USERPROFILE "Desktop\CreatorControlSuite-StartupDiagnostics.txt"
$Lines = New-Object System.Collections.Generic.List[string]
$Lines.Add("Creator Control Suite Startup Diagnostics")
$Lines.Add("Time: $(Get-Date -Format o)")
$Lines.Add("")

$Candidates = @(
    "$env:ProgramFiles\Creator Control Suite\CreatorControlSuite.exe",
    "$env:ProgramFiles\CreatorControlSuite\CreatorControlSuite.exe",
    "${env:ProgramFiles(x86)}\Creator Control Suite\CreatorControlSuite.exe"
)

foreach($Candidate in $Candidates) {
    if(Test-Path -LiteralPath $Candidate) {
        $Lines.Add("EXE: $Candidate")
        try {
            $Item = Get-Item -LiteralPath $Candidate
            $Lines.Add("Version: $($Item.VersionInfo.FileVersion)")
        } catch {}
    }
}

$LogRoot = Join-Path $env:LOCALAPPDATA "CreatorControlSuite"
$Lines.Add("LocalAppData: $LogRoot")
if(Test-Path -LiteralPath $LogRoot) {
    Get-ChildItem -LiteralPath $LogRoot -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { $Lines.Add("FILE: $($_.FullName) [$($_.Length) bytes]") }
}

$Lines | Set-Content -LiteralPath $Out -Encoding UTF8
Write-Host "Diagnose gespeichert: $Out"
