function Get-DirectoryBuildVersion {
    param([string]$RepoRoot)
    $propsPath = Join-Path $RepoRoot "Directory.Build.props"
    $raw = Get-Content -LiteralPath $propsPath -Raw
    if ($raw -match "<Version>(?<v>[^<]+)</Version>") {
        return $Matches["v"].Trim()
    }
    throw "Version in Directory.Build.props nicht gefunden."
}

function ConvertTo-MsiProductVersion {
    param([string]$Version)
    if ($Version -match '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<label>[A-Za-z]+)(?<prenum>\d+))?') {
        $major = [int]$Matches["major"]
        $minor = [int]$Matches["minor"]
        $patch = [int]$Matches["patch"]
        if ($Matches["prenum"] -and $patch -eq 0) {
            $patch = [int]$Matches["prenum"]
        }
        return "$major.$minor.$patch"
    }
    throw "Version '$Version' kann nicht in MSI-ProductVersion umgewandelt werden."
}

function Get-UpdateChannelFromVersion {
    param([string]$Version)
    if ($Version -match '(?i)alpha') { return "Alpha" }
    if ($Version -match '(?i)beta') { return "Beta" }
    return "Stable"
}
