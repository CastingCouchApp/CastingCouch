use serde::{Deserialize, Serialize};

pub const CURRENT_SCHEMA_VERSION: u32 = 2;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct AppSettings {
    #[serde(default = "current_schema")]
    pub schema_version: u32,
    #[serde(default)]
    pub additional_scenes: Vec<String>,
    #[serde(default)]
    pub product: ProductSettings,
    #[serde(default)]
    pub general: GeneralSettings,
    #[serde(default)]
    pub branding: BrandingSettings,
    #[serde(default)]
    pub obs: ObsSettings,
    #[serde(default)]
    pub twitch: TwitchSettings,
    #[serde(default)]
    pub spotify: SpotifySettings,
    #[serde(default)]
    pub music_player: MusicPlayerSettings,
    #[serde(default)]
    pub you_tube_music: YouTubeMusicSettings,
    #[serde(default)]
    pub streamer_bot: StreamerBotSettings,
    #[serde(default)]
    pub alerts: AlertSettings,
    #[serde(default)]
    pub overlay: OverlaySettings,
    #[serde(default)]
    pub workflow: WorkflowSettings,
    #[serde(default)]
    pub stream_deck: StreamDeckSettings,
    #[serde(default)]
    pub dashboard: DashboardSettings,
    #[serde(default)]
    pub updates: UpdateSettings,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            schema_version: CURRENT_SCHEMA_VERSION,
            additional_scenes: vec![],
            product: ProductSettings::default(),
            general: GeneralSettings::default(),
            branding: BrandingSettings::default(),
            obs: ObsSettings::default(),
            twitch: TwitchSettings::default(),
            spotify: SpotifySettings::default(),
            music_player: MusicPlayerSettings::default(),
            you_tube_music: YouTubeMusicSettings::default(),
            streamer_bot: StreamerBotSettings::default(),
            alerts: AlertSettings::default(),
            overlay: OverlaySettings::default(),
            workflow: WorkflowSettings::default(),
            stream_deck: StreamDeckSettings::default(),
            dashboard: DashboardSettings::default(),
            updates: UpdateSettings::default(),
        }
    }
}

fn current_schema() -> u32 {
    CURRENT_SCHEMA_VERSION
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ProductSettings {
    #[serde(default = "product_name")]
    pub product_name: String,
    #[serde(default = "product_version")]
    pub version: String,
    #[serde(default = "update_channel")]
    pub update_channel: String,
}

impl Default for ProductSettings {
    fn default() -> Self {
        Self {
            product_name: product_name(),
            version: product_version(),
            update_channel: update_channel(),
        }
    }
}

fn product_name() -> String {
    "CastingCouch".into()
}
fn product_version() -> String {
    "8.0.0-beta1".into()
}
fn update_channel() -> String {
    "Alpha".into()
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct GeneralSettings {
    #[serde(default = "lang_de")]
    pub language: String,
    #[serde(default = "theme_classic")]
    pub theme_id: String,
    #[serde(default)]
    pub title_bar_widget_cards_enabled: bool,
    #[serde(default)]
    pub title_bar_hidden_widgets: Vec<String>,
    #[serde(default)]
    pub data_root: String,
    #[serde(default)]
    pub backup_root: String,
    #[serde(default)]
    pub overlay_manifest_path: String,
    #[serde(default)]
    pub start_with_windows: bool,
    #[serde(default)]
    pub minimize_to_tray: bool,
    #[serde(default = "default_true")]
    pub connection_watchdog_enabled: bool,
    #[serde(default = "watchdog_seconds")]
    pub connection_watchdog_seconds: i32,
    #[serde(default = "default_true")]
    pub reconnect_obs: bool,
    #[serde(default = "default_true")]
    pub reconnect_twitch: bool,
    #[serde(default = "default_true")]
    pub reconnect_spotify: bool,
    #[serde(default = "default_true")]
    pub reconnect_you_tube_music: bool,
    #[serde(default = "default_true")]
    pub reconnect_streamer_bot: bool,
}

impl Default for GeneralSettings {
    fn default() -> Self {
        Self {
            language: lang_de(),
            theme_id: theme_classic(),
            title_bar_widget_cards_enabled: false,
            title_bar_hidden_widgets: vec![],
            data_root: String::new(),
            backup_root: String::new(),
            overlay_manifest_path: String::new(),
            start_with_windows: false,
            minimize_to_tray: false,
            connection_watchdog_enabled: true,
            connection_watchdog_seconds: 15,
            reconnect_obs: true,
            reconnect_twitch: true,
            reconnect_spotify: true,
            reconnect_you_tube_music: true,
            reconnect_streamer_bot: true,
        }
    }
}

fn lang_de() -> String {
    "de-DE".into()
}
fn theme_classic() -> String {
    "classic".into()
}
fn default_true() -> bool {
    true
}
fn watchdog_seconds() -> i32 {
    15
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct BrandingSettings {
    #[serde(default = "display_name")]
    pub display_name: String,
    #[serde(default)]
    pub channel_name: String,
    #[serde(default = "accent")]
    pub accent_color: String,
    #[serde(default)]
    pub logo_path: String,
}

impl Default for BrandingSettings {
    fn default() -> Self {
        Self {
            display_name: display_name(),
            channel_name: String::new(),
            accent_color: accent(),
            logo_path: String::new(),
        }
    }
}

fn display_name() -> String {
    "Mein Stream".into()
}
fn accent() -> String {
    "#FF8C00".into()
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ObsSettings {
    #[serde(default = "localhost")]
    pub host: String,
    #[serde(default = "obs_port")]
    pub port: u16,
    #[serde(default = "default_true")]
    pub auto_connect: bool,
    #[serde(default = "default_true")]
    pub connect_on_prepare: bool,
    #[serde(default)]
    pub executable_path: String,
    #[serde(default = "start_scene")]
    pub start_scene: String,
    #[serde(default = "live_scene")]
    pub live_scene: String,
    #[serde(default = "pause_scene")]
    pub pause_scene: String,
    #[serde(default = "end_scene")]
    pub end_scene: String,
    #[serde(default)]
    pub goal_overlay_scene: String,
    #[serde(default)]
    pub microphone_source: String,
    #[serde(default)]
    pub desktop_audio_source: String,
    #[serde(default)]
    pub music_source: String,
    #[serde(default)]
    pub camera_source: String,
    #[serde(default)]
    pub audio_profiles: Vec<serde_json::Value>,
}

impl Default for ObsSettings {
    fn default() -> Self {
        Self {
            host: localhost(),
            port: 4455,
            auto_connect: true,
            connect_on_prepare: true,
            executable_path: String::new(),
            start_scene: start_scene(),
            live_scene: live_scene(),
            pause_scene: pause_scene(),
            end_scene: end_scene(),
            goal_overlay_scene: String::new(),
            microphone_source: String::new(),
            desktop_audio_source: String::new(),
            music_source: String::new(),
            camera_source: String::new(),
            audio_profiles: vec![],
        }
    }
}

fn localhost() -> String {
    "127.0.0.1".into()
}
fn obs_port() -> u16 {
    4455
}
fn start_scene() -> String {
    "Start".into()
}
fn live_scene() -> String {
    "Game".into()
}
fn pause_scene() -> String {
    "Pause".into()
}
fn end_scene() -> String {
    "Ende".into()
}

fn default_twitch_scopes() -> Vec<String> {
    vec![
        "user:read:chat".into(),
        "user:write:chat".into(),
        "user:bot".into(),
        "channel:bot".into(),
        "channel:manage:broadcast".into(),
        "channel:manage:raids".into(),
        "moderator:read:followers".into(),
        "user:read:follows".into(),
        "moderator:read:chatters".into(),
        "moderator:manage:banned_users".into(),
        "channel:read:subscriptions".into(),
        "bits:read".into(),
        "channel:read:redemptions".into(),
        "channel:manage:redemptions".into(),
        "channel:read:guest_star".into(),
        "channel:manage:polls".into(),
        "channel:manage:predictions".into(),
    ]
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct TwitchSettings {
    #[serde(default)]
    pub client_id: String,
    #[serde(default)]
    pub channel_name: String,
    #[serde(default = "default_true")]
    pub auto_connect: bool,
    #[serde(default = "default_true")]
    pub connect_on_prepare: bool,
    #[serde(default)]
    pub creator_dashboard_url: String,
    #[serde(default = "default_true")]
    pub enable_chat: bool,
    #[serde(default)]
    pub chat_ui_mode: String,
    #[serde(default = "default_true")]
    pub enable_event_sub: bool,
    #[serde(default = "default_true")]
    pub use_device_code_flow: bool,
    #[serde(default = "default_twitch_scopes")]
    pub scopes: Vec<String>,
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

impl Default for TwitchSettings {
    fn default() -> Self {
        Self {
            client_id: String::new(),
            channel_name: String::new(),
            auto_connect: true,
            connect_on_prepare: true,
            creator_dashboard_url: String::new(),
            enable_chat: true,
            chat_ui_mode: "BuiltIn".into(),
            enable_event_sub: true,
            use_device_code_flow: true,
            scopes: default_twitch_scopes(),
            extra: serde_json::Value::Object(Default::default()),
        }
    }
}

fn default_spotify_redirect() -> String {
    "http://127.0.0.1:43821/callback/".into()
}

fn default_spotify_scopes() -> Vec<String> {
    vec![
        "user-read-playback-state".into(),
        "user-read-currently-playing".into(),
    ]
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct SpotifySettings {
    #[serde(default)]
    pub client_id: String,
    #[serde(default = "default_spotify_redirect")]
    pub redirect_uri: String,
    #[serde(default = "default_true")]
    pub auto_connect: bool,
    #[serde(default = "default_spotify_scopes")]
    pub scopes: Vec<String>,
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

impl Default for SpotifySettings {
    fn default() -> Self {
        Self {
            client_id: String::new(),
            redirect_uri: default_spotify_redirect(),
            auto_connect: true,
            scopes: default_spotify_scopes(),
            extra: serde_json::Value::Object(Default::default()),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct MusicPlayerSettings {
    #[serde(default)]
    pub source: String,
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct YouTubeMusicSettings {
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct StreamerBotSettings {
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct AlertSettings {
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct OverlaySettings {
    #[serde(default)]
    pub root_path: String,
    #[serde(default = "overlay_data_file")]
    pub data_file_name: String,
    #[serde(default)]
    pub data_file_path: String,
    #[serde(default)]
    pub additional_data_roots: Vec<String>,
    #[serde(default)]
    pub instances: Vec<serde_json::Value>,
    #[serde(default)]
    pub canvases: Vec<OverlayCanvasSettings>,
    #[serde(default)]
    pub selected_canvas_id: String,
    #[serde(default = "overlay_port")]
    pub web_server_port: u16,
    #[serde(default)]
    pub chat: OverlayChatSettings,
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

impl Default for OverlaySettings {
    fn default() -> Self {
        Self {
            root_path: String::new(),
            data_file_name: overlay_data_file(),
            data_file_path: String::new(),
            additional_data_roots: vec![],
            instances: vec![],
            canvases: vec![OverlayCanvasSettings::default()],
            selected_canvas_id: "default".into(),
            web_server_port: 8765,
            chat: OverlayChatSettings::default(),
            extra: serde_json::Value::Object(Default::default()),
        }
    }
}

impl OverlaySettings {
    pub fn ensure_canvases_migrated(&mut self) {
        if self.canvases.is_empty() {
            self.canvases.push(OverlayCanvasSettings::default());
        }
        if self.selected_canvas_id.is_empty() {
            self.selected_canvas_id = self.canvases[0].id.clone();
        }
    }

    pub fn selected_canvas(&self) -> OverlayCanvasSettings {
        self.canvases
            .iter()
            .find(|c| c.id == self.selected_canvas_id)
            .cloned()
            .unwrap_or_else(OverlayCanvasSettings::default)
    }

    pub fn editor_url(&self, id: &str) -> String {
        format!("http://127.0.0.1:{}/editor/{id}", self.web_server_port)
    }

    pub fn view_url(&self, id: &str) -> String {
        format!("http://127.0.0.1:{}/view/{id}", self.web_server_port)
    }

    pub fn widget_url(&self, ty: &str) -> String {
        format!("http://127.0.0.1:{}/w/{ty}", self.web_server_port)
    }
}

fn overlay_data_file() -> String {
    "overlay-data.json".into()
}
fn overlay_port() -> u16 {
    8765
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct OverlayCanvasSettings {
    #[serde(default = "default_canvas_id")]
    pub id: String,
    #[serde(default = "default_canvas_name")]
    pub name: String,
}

impl Default for OverlayCanvasSettings {
    fn default() -> Self {
        Self {
            id: default_canvas_id(),
            name: default_canvas_name(),
        }
    }
}

fn default_canvas_id() -> String {
    "default".into()
}
fn default_canvas_name() -> String {
    "Canvas".into()
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct OverlayChatSettings {
    #[serde(default = "default_true")]
    pub enabled: bool,
    #[serde(default = "default_true")]
    pub enable_bttv: bool,
    #[serde(default = "default_true")]
    pub enable_ffz: bool,
    #[serde(default = "default_true")]
    pub enable_seven_tv: bool,
    #[serde(default = "default_true")]
    pub show_twitch_events: bool,
    #[serde(default = "max_buffered")]
    pub max_buffered_messages: i32,
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

impl Default for OverlayChatSettings {
    fn default() -> Self {
        Self {
            enabled: true,
            enable_bttv: true,
            enable_ffz: true,
            enable_seven_tv: true,
            show_twitch_events: true,
            max_buffered_messages: 100,
            extra: serde_json::Value::Object(Default::default()),
        }
    }
}

fn max_buffered() -> i32 {
    100
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct WorkflowSettings {
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct StreamDeckSettings {
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct DashboardSettings {
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct UpdateSettings {
    #[serde(default = "update_channel")]
    pub channel: String,
    #[serde(default = "default_true")]
    pub check_on_startup: bool,
    #[serde(flatten)]
    pub extra: serde_json::Value,
}

impl Default for UpdateSettings {
    fn default() -> Self {
        Self {
            channel: update_channel(),
            check_on_startup: true,
            extra: serde_json::Value::Object(Default::default()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_match_wpf_schema() {
        let s = AppSettings::default();
        assert_eq!(s.schema_version, CURRENT_SCHEMA_VERSION);
        assert_eq!(s.obs.port, 4455);
        assert_eq!(s.overlay.web_server_port, 8765);
        assert_eq!(s.general.theme_id, "classic");
        assert_eq!(
            s.spotify.redirect_uri,
            "http://127.0.0.1:43821/callback/"
        );
        assert!(s.spotify.auto_connect);
        assert!(s
            .spotify
            .scopes
            .iter()
            .any(|scope| scope == "user-read-currently-playing"));
    }

    #[test]
    fn spotify_settings_keep_unknown_fields() {
        let json = r#"{
            "ClientId": "cid",
            "AutoConnect": true,
            "RedirectUri": "http://127.0.0.1:43821/callback/",
            "PreferredDeviceId": "device-x",
            "StartPlaylistUri": "spotify:playlist:x"
        }"#;
        let parsed: SpotifySettings = serde_json::from_str(json).unwrap();
        assert_eq!(parsed.client_id, "cid");
        assert_eq!(parsed.redirect_uri, "http://127.0.0.1:43821/callback/");
        let back = serde_json::to_value(&parsed).unwrap();
        assert_eq!(back["PreferredDeviceId"], "device-x");
        assert_eq!(back["StartPlaylistUri"], "spotify:playlist:x");
        assert_eq!(back["RedirectUri"], "http://127.0.0.1:43821/callback/");
        assert_eq!(back["ClientId"], "cid");
    }
}
