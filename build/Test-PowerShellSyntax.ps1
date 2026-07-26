Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$ScriptFiles = @(
    Get-ChildItem `
        -LiteralPath (Join-Path $Root "build") `
        -Filter "*.ps1" `
        -File
)

$Errors = New-Object System.Collections.Generic.List[string]

foreach ($ScriptFile in $ScriptFiles) {
    $Tokens = $null
    $ParseErrors = $null

    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $ScriptFile.FullName,
        [ref]$Tokens,
        [ref]$ParseErrors)

    foreach ($ParseError in @($ParseErrors)) {
        $Errors.Add(
            $ScriptFile.Name +
            " [" +
            $ParseError.Extent.StartLineNumber +
            ":" +
            $ParseError.Extent.StartColumnNumber +
            "] " +
            $ParseError.Message)
    }
}

Write-Host "PowerShell-Syntaxprüfung:"
Write-Host "  geprüfte Skripte: $($ScriptFiles.Count)"
Write-Host "  Syntaxfehler: $($Errors.Count)"

if ($Errors.Count -gt 0) {
    throw "PowerShell-Syntaxfehler:`n$($Errors -join "`n")"
}

Write-Host "PowerShell-Syntaxprüfung bestanden." -ForegroundColor Green
