import type {
    DashboardDraft,
    DashboardPreferences,
} from "../../lib/command-contract";
export type DashboardSnapshot = {
    original: unknown;
    draft: DashboardDraft;
    sceneChoices: string[];
    warnings: string[];
};
export const dashboardKey = ["dashboard-layout"] as const;
export const CARD_TITLES: Record<string, string> = {
    ConnectionStatus: "Verbindungen",
    Community: "Community",
    ObsSceneControl: "Szenen",
    StreamControl: "OBS-Ausgänge",
    StreamEnd: "Streamende und Raid",
    Countdown: "Countdown",
    Preflight: "Vorprüfung",
    SpotifyPlayer: "Musikplayer",
    TwitchChat: "Twitch-Chat",
    TwitchEvents: "Twitch-Ereignisse",
    Notifications: "Benachrichtigungen",
    StreamHistory: "Streamverlauf",
    CreatorIntelligence: "Creator Intelligence",
};
export const CARD_GROUPS: Record<string, keyof DashboardPreferences> = {
    ConnectionStatus: "showServiceStatus",
    Community: "showLivePanels",
    ObsSceneControl: "showStreamControls",
    StreamControl: "showStreamControls",
    StreamEnd: "showStreamControls",
    Countdown: "showStreamControls",
    Preflight: "showAdvancedTools",
    SpotifyPlayer: "showLivePanels",
    TwitchChat: "showLivePanels",
    TwitchEvents: "showNotifications",
    Notifications: "showNotifications",
    StreamHistory: "showStreamHistory",
    CreatorIntelligence: "showAdvancedTools",
};
export function cardVisible(
    draft: DashboardDraft,
    key: string,
    focus: boolean,
) {
    const card = draft.cards.find((c) => c.key === key);
    if (!card?.visible) return false;
    const group = CARD_GROUPS[key];
    if (
        focus &&
        [
            "showNotifications",
            "showQuickServices",
            "showStreamHistory",
        ].includes(group)
    )
        return false;
    if (
        focus &&
        [
            "showServiceStatus",
            "showStreamControls",
            "showLivePanels",
            "showAdvancedTools",
        ].includes(group)
    )
        return true;
    return !group || draft.preferences[group] !== false;
}
