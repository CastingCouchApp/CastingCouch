param(
    [string]$PublishPath = ""
)

& (Join-Path $PSScriptRoot "Test-ReleasePublishLayout.ps1") `
    -PublishPath $PublishPath

exit $LASTEXITCODE
