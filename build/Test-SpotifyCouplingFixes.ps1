Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

function Assert-Contains([string]$Path, [string]$Pattern, [string]$Message) {
    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -notmatch $Pattern) {
        throw $Message
    }
}

function Assert-NotContains([string]$Path, [string]$Pattern, [string]$Message) {
    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -match $Pattern) {
        throw $Message
    }
}

$Module = Join-Path $Root "src\CreatorControlSuite.Modules.Spotify\SpotifyModule.cs"
$Api = Join-Path $Root "src\CreatorControlSuite.Modules.Spotify\SpotifyApiClient.cs"
$Main = Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml.cs"
$Ipc = Join-Path $Root "src\CreatorControlSuite.App\Services\AppIpcCommandRouter.cs"
$IpcModels = Join-Path $Root "src\CreatorControlSuite.Core\Ipc\IpcModels.cs"

Assert-Contains $Module 'bool\? shuffleOverride' "StartPlaylist muss shuffleOverride unterstützen."
Assert-Contains $Module 'string\? offsetTrackUri' "StartPlaylist muss offsetTrackUri unterstützen."
Assert-Contains $Module 'ResolveControlDeviceIdAsync' "Transport/Volume müssen PreferredDevice auflösen."
Assert-Contains $Module 'PlayPauseAsync' "SpotifyModule muss PlayPauseAsync bereitstellen."
Assert-Contains $Module 'AdjustVolumeAsync' "SpotifyModule muss AdjustVolumeAsync bereitstellen."
Assert-Contains $Module 'PatchPlaybackVolume' "Volume muss den lokalen Snapshot patchen."
Assert-Contains $Api 'offset = new \{ uri' "StartPlayback muss Track-Offset senden können."

Assert-NotContains $Main 'StartPlaylistAsync\(rule\.PlaylistUri,\s*rule\.Shuffle\)' `
    "Scene-Automation darf rule.Shuffle nicht als applyConfiguredStartVolume übergeben."
Assert-Contains $Main 'shuffleOverride:\s*rule\.Shuffle' "Scene-Automation muss shuffleOverride setzen."
Assert-Contains $Main 'LiveVolumePercent' "Live-Uebergang muss LiveVolumePercent setzen."
Assert-Contains $Main 'SetVolumeImmediateAsync\(liveVolume\)' "Live-Uebergang muss SetVolumeImmediateAsync mit LiveVolume aufrufen."
Assert-Contains $Main 'offsetTrackUri:\s*state\.Track\?\.Uri' "RestorePrevious muss Track-Offset nutzen."
Assert-Contains $Main 'FormatStreamDeckCommandArgs' "Stream-Deck-CMD muss korrekte Argument-Keys schreiben."
Assert-Contains $Main '"spotify\.volume"\s*=>' "FormatStreamDeckCommandArgs muss volume-Key kennen."

Assert-Contains $IpcModels 'SpotifyToggle\s*=\s*"spotify\.toggle"' "IPC muss spotify.toggle kennen."
Assert-Contains $IpcModels 'SpotifyVolumeUp\s*=\s*"spotify\.volumeup"' "IPC muss spotify.volumeup kennen."
Assert-Contains $IpcModels 'SpotifyVolumeDown\s*=\s*"spotify\.volumedown"' "IPC muss spotify.volumedown kennen."
Assert-Contains $IpcModels 'SpotifyPlaylist\s*=\s*"spotify\.playlist"' "IPC muss spotify.playlist kennen."
Assert-Contains $Ipc 'GetAny\(command,\s*"",\s*"volume",\s*"value"\)' "spotify.volume muss value-Alias akzeptieren."
Assert-Contains $Ipc 'GetAny\(command,\s*"",\s*"scene",\s*"value"\)' "obs.scene muss value-Alias akzeptieren."
Assert-Contains $Ipc 'PlayPauseAsync' "IPC-Toggle muss PlayPauseAsync aufrufen."
Assert-Contains $Ipc 'AdjustVolumeAsync\(5' "IPC volumeup muss AdjustVolumeAsync nutzen."
Assert-Contains $Ipc 'AdjustVolumeAsync\(-5' "IPC volumedown muss AdjustVolumeAsync nutzen."

Assert-Contains $Module 'PlayerControlDebounceMilliseconds = 1000' "Seek/Volume muessen 1s debounce nutzen."
Assert-Contains $Module 'SetVolumeImmediateAsync' "Fades brauchen SetVolumeImmediateAsync."
Assert-Contains $Module 'SeekImmediateAsync' "Restore braucht SeekImmediateAsync."
Assert-Contains $Module 'DebouncePlayerControlAsync' "Debounce-Helfer fehlt."

Write-Host "Spotify coupling guards passed." -ForegroundColor Green
