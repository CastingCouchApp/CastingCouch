Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Xaml = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml") -Raw
$Code = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml.cs") -Raw
$ObsApi = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.OBS\IObsWebSocketClient.cs") -Raw
$SpotifyApi = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.Spotify\ISpotifyApiClient.cs") -Raw
$TwitchApi = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.Twitch\ITwitchApiClient.cs") -Raw

function Require-All {
    param([string]$Area,[string]$Content,[string[]]$Patterns)
    foreach ($Pattern in $Patterns) {
        if ($Content -notmatch [regex]::Escape($Pattern)) {
            throw "$Area unvollstaendig: '$Pattern' fehlt."
        }
    }
}

Require-All "OBS-API" $ObsApi @(
    "GetSceneListAsync", "GetInputListAsync", "SetCurrentProgramSceneAsync",
    "SetInputMuteAsync", "SetInputVolumeDbAsync", "StartStreamAsync", "StopStreamAsync",
    "StartRecordAsync", "PauseRecordAsync", "StartReplayBufferAsync", "SaveReplayBufferAsync",
    "GetSourceFilterListAsync", "SetSceneItemEnabledAsync", "SetSceneItemTransformAsync",
    "GetProfileListAsync", "GetSceneCollectionListAsync"
)
Require-All "OBS-UI" $Xaml @(
    "ServicesObsScenesList", "ServicesObsInputsList",
    "ServicesObsStartRecordButton", "ServicesObsSaveReplayButton", "ServicesObsTransitionBox"
)
Require-All "OBS-Verknuepfung" $Code @(
    "SwitchServicesObsSceneAsync", "SetSelectedObsInputMuteAsync", "SetSelectedObsInputVolumeAsync",
    "ApplySelectedObsTransitionAsync", "ApplySelectedObsSceneItemTransformAsync"
)

Require-All "Spotify-API" $SpotifyApi @(
    "GetDevicesAsync", "GetQueueAsync", "GetRecentlyPlayedAsync", "SearchTracksAsync",
    "GetCurrentUserPlaylistsAsync", "GetPlaylistTracksAsync", "TransferPlaybackAsync",
    "SetVolumeAsync", "SetShuffleAsync", "SetRepeatAsync", "SeekPlaybackAsync"
)
Require-All "Spotify-UI" $Xaml @(
    "ServicesSpotifyDeviceBox", "ServicesSpotifyQueueList", "ServicesSpotifyHistoryList",
    "ServicesSpotifyPlaylistBox", "ServicesSpotifyVolumeSlider", "ServicesSpotifyOverlaySourceBox"
)
Require-All "Spotify-Verknuepfung" $Code @(
    "_spotifyModule.RefreshDevicesAsync", "_spotifyModule.RefreshQueueAsync", "_spotifyModule.RefreshRecentlyPlayedAsync",
    "SearchSpotifyTracksAsync", "WriteSpotifyDataJsonNowAsync", "QueueSpotifyVolumeUpdateAsync"
)

Require-All "Twitch-API" $TwitchApi @(
    "GetChannelInformationAsync", "UpdateChannelInformationAsync", "SearchCategoriesAsync",
    "GetChattersAsync", "SendChatMessageAsync", "StartRaidAsync", "CancelRaidAsync",
    "GetCustomRewardsAsync", "CreatePollAsync", "CreatePredictionAsync"
)
Require-All "Twitch-UI" $Xaml @(
    "ServicesTwitchChatList", "ServicesTwitchUsersList", "ServicesTwitchEventsList",
    "ServicesTwitchTitleBox", "ServicesTwitchCategoryResultsBox", "ServicesTwitchRaidTargetBox"
)
Require-All "Twitch-Verknuepfung" $Code @(
    "ConnectTwitchAsync", "ServicesTwitchChatList", "ServicesTwitchUsersList",
    "ServicesTwitchEventsList", "SaveTwitchEndSettingsAsync"
)

Require-All "Streamer.bot-UI" $Xaml @(
    "ServicesStreamerBotServicesList", "ServicesStreamerBotActionsList",
    "ServicesStreamerBotActionArgumentsBox", "ServicesStreamerBotHistoryList",
    "ServicesStreamerBotDiagnosticText"
)
Require-All "Streamer.bot-Verknuepfung" $Code @(
    "ConnectStreamerBotAsync", "RefreshStreamerBotActionsAsync", "RunSelectedStreamerBotActionAsync",
    "DiagnoseStreamerBotAsync", "ReconnectStreamerBotAsync", "SetStreamerBotAlertsEnabledAsync",
    "ServicesStreamerBotLiveEventsList.ItemsSource"
)

Write-Host "Dienstintegrations-Prüfung bestanden: OBS, Spotify, Twitch und Streamer.bot sind vollstaendig verdrahtet." -ForegroundColor Green
