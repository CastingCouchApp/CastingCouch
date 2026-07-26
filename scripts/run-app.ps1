$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$debugOutput = [System.IO.Path]::GetFullPath(
    (Join-Path $projectRoot "artifacts\bin\CreatorControlSuite.App\Debug\net10.0-windows"))
$projectFile = Join-Path $projectRoot "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"

# A running development build locks its loaded DLLs. Stop only instances whose
# executable lives in this repository's Debug output; installed copies remain
# untouched.
$developmentProcesses = Get-Process -Name "CreatorControlSuite" -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            $processPath = [System.IO.Path]::GetFullPath($_.Path)
            $processPath.StartsWith(
                $debugOutput + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    }

foreach ($developmentProcess in $developmentProcesses) {
    Write-Host "Beende laufende Entwicklungsinstanz (PID $($developmentProcess.Id)) ..."
    $developmentProcess.CloseMainWindow() | Out-Null

    if (-not $developmentProcess.WaitForExit(3000)) {
        Stop-Process -Id $developmentProcess.Id -Force
        $developmentProcess.WaitForExit(3000)
    }
}

Write-Host "Starte Creator Control Suite ..."
& dotnet run --project $projectFile
exit $LASTEXITCODE
