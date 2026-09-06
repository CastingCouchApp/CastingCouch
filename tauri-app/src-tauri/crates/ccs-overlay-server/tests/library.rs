use ccs_overlay_server::MediaLibrary;
use serde_json::json;
use std::io::{Cursor, Write};

#[test]
fn imported_asset_survives_restart_and_delete_removes_it() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    let asset = library.import_image("logo.png", b"png test bytes").unwrap();
    let id = asset["id"].as_str().unwrap();
    let reopened = MediaLibrary::new(dir.path());
    assert_eq!(
        reopened.assets().unwrap()[0]["url"],
        format!("/assets/{id}")
    );
    assert_eq!(
        std::fs::read(reopened.asset_path(id).unwrap()).unwrap(),
        b"png test bytes"
    );
    reopened.delete_asset(id).unwrap();
    assert!(reopened.assets().unwrap().is_empty());
    assert!(reopened.asset_path(id).is_err());
    assert!(reopened.import_image("attack.exe", b"x").is_err());
    assert!(reopened.asset_path("../settings.json").is_err());
}

fn pack(entry: &str) -> Vec<u8> {
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let options = zip::write::SimpleFileOptions::default();
    zip.start_file("manifest.json", options).unwrap();
    zip.write_all(json!({"id":"test-pack","name":"Test","version":"1.0","apiVersion":1,"widgets":[{"id":"banner","name":"Banner","entry":"widgets/banner.js"}]}).to_string().as_bytes()).unwrap();
    zip.start_file(entry, options).unwrap();
    zip.write_all(b"CcsCanvas.registerWidget('ext:test-pack:banner', {});")
        .unwrap();
    zip.finish().unwrap().into_inner()
}

#[test]
fn pack_install_catalog_replacement_and_uninstall_are_real() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    library.install_pack(&pack("widgets/banner.js")).unwrap();
    assert_eq!(library.packs().unwrap()[0]["widgets"][0]["id"], "banner");
    assert!(library
        .extension_path("test-pack", "widgets/banner.js")
        .unwrap()
        .is_file());
    assert!(library.install_pack(&pack("../escape.js")).is_err());
    assert!(library
        .extension_path("test-pack", "widgets/banner.js")
        .unwrap()
        .is_file());
    assert!(library
        .extension_path("test-pack", "../manifest.json")
        .is_err());
    library.delete_pack("test-pack").unwrap();
    assert!(library.packs().unwrap().is_empty());
}
