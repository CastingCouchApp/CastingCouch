param(
  [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$versionFile = Join-Path $RepoRoot "version.json"
$json = Get-Content $versionFile -Raw | ConvertFrom-Json
$dotnet = $json.version
$tauri = $json.tauriVersion

$confPath = Join-Path $RepoRoot "tauri-app/src-tauri/tauri.conf.json"
$conf = Get-Content $confPath -Raw | ConvertFrom-Json
$conf.version = $tauri
# WiX rejects textual SemVer prerelease identifiers. Keep its numeric version in sync.
$msiBase = ($tauri -split "[-+]")[0]
$msiBuild = if ($tauri -match "-(?:alpha|beta|rc)\.?(\d+)") { $Matches[1] } else { "0" }
if (-not $conf.bundle.windows.PSObject.Properties["wix"]) {
  $conf.bundle.windows | Add-Member -NotePropertyName wix -NotePropertyValue ([pscustomobject]@{})
}
$conf.bundle.windows.wix | Add-Member -NotePropertyName version -NotePropertyValue "$msiBase.$msiBuild" -Force
$conf | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $confPath

$pkgPath = Join-Path $RepoRoot "tauri-app/package.json"
$pkg = Get-Content $pkgPath -Raw | ConvertFrom-Json
$pkg.version = $tauri
$pkg | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $pkgPath

$props = Join-Path $RepoRoot "Directory.Build.props"
$content = Get-Content $props -Raw
$content = [regex]::Replace($content, "<Version>[^<]+</Version>", "<Version>$dotnet</Version>")
Set-Content -Encoding utf8 $props $content

Write-Host "Synced version $dotnet / tauri $tauri"
