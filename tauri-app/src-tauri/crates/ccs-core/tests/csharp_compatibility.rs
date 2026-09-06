use ccs_core::{AppSettings, JsonSettingsStore};
use serde_json::Value;
fn assert_preserved(old: &Value, new: &Value, path: &str) {
    match old {
        Value::Object(map) => {
            for (k, v) in map {
                assert_preserved(v, &new[k], &format!("{path}/{k}"))
            }
        }
        Value::Array(list) => {
            assert_eq!(list.len(), new.as_array().expect(path).len(), "{path}");
            for (i, v) in list.iter().enumerate() {
                assert_preserved(v, &new[i], &format!("{path}/{i}"));
            }
        }
        _ => assert_eq!(old, new, "{path}"),
    }
}
#[tokio::test]
async fn actual_csharp_settings_roundtrip_preserves_every_field() {
    for fixture in [
        include_str!("fixtures/csharp-settings.json"),
        include_str!("fixtures/csharp-populated-settings.json"),
    ] {
        let root: Value = serde_json::from_str(fixture).unwrap();
        let _: AppSettings = serde_json::from_value(root.clone()).expect("C# settings deserialize");
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        std::fs::write(&path, root.to_string()).unwrap();
        let store = JsonSettingsStore::new(&path);
        let settings = store.load().await.unwrap();
        store.save(&settings).await.unwrap();
        let saved: Value = serde_json::from_slice(&std::fs::read(path).unwrap()).unwrap();
        assert_preserved(&root, &saved, "");
    }
}
