use ccs_core::{AppPaths, JsonSettingsStore};
use ccs_overlay_server::{OverlayServer, RealtimeHub};
use serde_json::{json, Value};
use std::{
    io::{Cursor, Write},
    sync::Arc,
};

const MANIFEST: &[u8] = include_bytes!(
    "../../../../../tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/manifest.json"
);
const WIDGET: &[u8] = include_bytes!("../../../../../tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/widgets/banner/index.js");
const EFFECT: &[u8] = include_bytes!("../../../../../tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/effects/sparkle/index.js");
const ANIMATION: &[u8] = include_bytes!("../../../../../tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/animations/wobble/index.js");
const FONT: &[u8] = include_bytes!("../../../../../tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/fonts/CoolFont.woff2");
const SVG: &[u8] = include_bytes!("../../../../../tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/assets/icons/logo.svg");

fn fixture(version: &str, css: &str, widget: &[u8]) -> Vec<u8> {
    let mut manifest: Value = serde_json::from_slice(MANIFEST).unwrap();
    manifest["version"] = json!(version);
    manifest["widgets"][0]["css"] = json!(css);
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let options = zip::write::SimpleFileOptions::default();
    zip.start_file("manifest.json", options).unwrap();
    zip.write_all(manifest.to_string().as_bytes()).unwrap();
    for (path, bytes) in [
        ("widgets/banner/index.js", widget),
        ("effects/sparkle/index.js", EFFECT),
        ("animations/wobble/index.js", ANIMATION),
        ("fonts/CoolFont.woff2", FONT),
        ("assets/icons/logo.svg", SVG),
        (
            "styles/banner.css",
            b".cool-kit-banner{color:red}".as_slice(),
        ),
    ] {
        zip.start_file(path, options).unwrap();
        zip.write_all(bytes).unwrap();
    }
    zip.finish().unwrap().into_inner()
}

async fn install(client: &reqwest::Client, base: &str, bytes: Vec<u8>) -> reqwest::Response {
    client
        .post(format!("{base}/extensions/install"))
        .multipart(reqwest::multipart::Form::new().part(
            "file",
            reqwest::multipart::Part::bytes(bytes).file_name("cool-kit.zip"),
        ))
        .send()
        .await
        .unwrap()
}

#[tokio::test]
async fn csharp_fixture_pack_roundtrips_install_all_assets_update_restart_and_uninstall_over_http()
{
    let root = tempfile::tempdir().unwrap();
    let paths = AppPaths::from_root(root.path().into());
    let settings = Arc::new(JsonSettingsStore::new(&paths.settings_file));
    let hub = Arc::new(RealtimeHub::new());
    let mut changes = hub.subscribe_extension_changes();
    let server = OverlayServer::start(settings.clone(), paths.clone(), hub, 0)
        .await
        .unwrap();
    let client = reqwest::Client::new();
    let base = format!("http://127.0.0.1:{}", server.port);
    let installed = install(
        &client,
        &base,
        fixture("1.0.0", "styles/banner.css", WIDGET),
    )
    .await;
    assert_eq!(installed.status(), 200);
    let change = changes.try_recv().unwrap();
    assert_eq!(
        serde_json::to_value(change).unwrap(),
        json!({"action":"installed","packId":"cool-kit"})
    );
    assert_eq!(
        installed.json::<Value>().await.unwrap()["fonts"][0]["family"],
        "CoolFont"
    );
    let catalog: Value = client
        .get(format!("{base}/extensions"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    let pack = &catalog["packs"][0];
    assert_eq!(pack["baseUrl"], "/ext/cool-kit/");
    for kind in ["widgets", "effects", "animations", "fonts", "assets"] {
        assert_eq!(pack[kind].as_array().unwrap().len(), 1);
    }
    for (path, bytes, mime) in [
        ("widgets/banner/index.js", WIDGET, "javascript"),
        ("effects/sparkle/index.js", EFFECT, "javascript"),
        ("animations/wobble/index.js", ANIMATION, "javascript"),
        ("fonts/CoolFont.woff2", FONT, "font/woff2"),
        ("assets/icons/logo.svg", SVG, "image/svg+xml"),
        (
            "styles/banner.css",
            b".cool-kit-banner{color:red}".as_slice(),
            "text/css",
        ),
    ] {
        let asset = client
            .get(format!("{base}/ext/cool-kit/{path}"))
            .send()
            .await
            .unwrap();
        assert_eq!(asset.status(), 200);
        assert!(asset.headers()["content-type"]
            .to_str()
            .unwrap()
            .contains(mime));
        assert_eq!(asset.bytes().await.unwrap().as_ref(), bytes);
    }
    let rejected = install(&client, &base, fixture("broken", "missing.css", b"broken")).await;
    assert_eq!(rejected.status(), 400);
    assert!(matches!(
        changes.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    assert!(rejected.json::<Value>().await.unwrap()["error"]
        .as_str()
        .unwrap()
        .contains("missing.css"));
    assert_eq!(
        client
            .get(format!("{base}/ext/cool-kit/widgets/banner/index.js"))
            .send()
            .await
            .unwrap()
            .bytes()
            .await
            .unwrap()
            .as_ref(),
        WIDGET
    );
    let updated = install(
        &client,
        &base,
        fixture("2.0.0", "styles/banner.css", b"window.updated=1;"),
    )
    .await;
    assert_eq!(updated.status(), 200);
    server.stop();
    let reopened = OverlayServer::start(settings, paths, Arc::new(RealtimeHub::new()), 0)
        .await
        .unwrap();
    let base = format!("http://127.0.0.1:{}", reopened.port);
    let catalog: Value = client
        .get(format!("{base}/extensions"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(catalog["packs"][0]["version"], "2.0.0");
    assert_eq!(
        client
            .get(format!("{base}/ext/cool-kit/widgets/banner/index.js"))
            .send()
            .await
            .unwrap()
            .bytes()
            .await
            .unwrap()
            .as_ref(),
        b"window.updated=1;"
    );
    assert_eq!(
        client
            .delete(format!("{base}/extensions/cool-kit"))
            .send()
            .await
            .unwrap()
            .status(),
        200
    );
    assert_eq!(
        client
            .get(format!("{base}/ext/cool-kit/fonts/CoolFont.woff2"))
            .send()
            .await
            .unwrap()
            .status(),
        404
    );
    let catalog: Value = client
        .get(format!("{base}/extensions"))
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(catalog["packs"], json!([]));
    reopened.stop();
}
