use ccs_core::{AppSettings, JsonSettingsStore};
use ccs_overlay_server::{OverlayCanvasService, OverlayLayoutStore};
#[tokio::test]
async fn rename_preserves_id_layout_and_url_and_selection_survives_restart() {
    let dir = tempfile::tempdir().unwrap();
    let store = JsonSettingsStore::new(dir.path().join("settings.json"));
    let mut settings = AppSettings::default();
    store.save(&settings).await.unwrap();
    let layouts = OverlayLayoutStore::new(dir.path().join("layouts"));
    let service = OverlayCanvasService::new(layouts.clone());
    let canvas = service
        .create(&mut settings, &store, "Original")
        .await
        .unwrap();
    let before = layouts.load(&canvas.id).await.unwrap();
    let url = settings.overlay.view_url(&canvas.id);
    service
        .update(&mut settings, &store, &canvas.id, Some("Umbenannt"), true)
        .await
        .unwrap();
    assert_eq!(settings.overlay.selected_canvas().name, "Umbenannt");
    assert_eq!(settings.overlay.view_url(&canvas.id), url);
    assert_eq!(layouts.load(&canvas.id).await.unwrap(), before);
    assert_eq!(
        store.load().await.unwrap().overlay.selected_canvas().name,
        "Umbenannt"
    );
    assert!(service
        .update(&mut settings, &store, &canvas.id, Some(" "), true)
        .await
        .is_err());
}
