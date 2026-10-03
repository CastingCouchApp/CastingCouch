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

fn archive(manifest: serde_json::Value, files: &[(&str, &[u8])]) -> Vec<u8> {
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let options = zip::write::SimpleFileOptions::default();
    zip.start_file("manifest.json", options).unwrap();
    zip.write_all(manifest.to_string().as_bytes()).unwrap();
    for (path, bytes) in files {
        zip.start_file(*path, options).unwrap();
        zip.write_all(bytes).unwrap();
    }
    zip.finish().unwrap().into_inner()
}

fn manifest() -> serde_json::Value {
    json!({"id":"test-pack","name":"Test","version":"1.0","apiVersion":1})
}

#[test]
fn complete_manifest_validation_preserves_the_previous_pack_on_bad_updates() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    library.install_pack(&pack("widgets/banner.js")).unwrap();
    for (kind, entries) in [
        (
            "widgets",
            json!([{"id":"banner","entry":"widgets/banner.js"}]),
        ),
        (
            "effects",
            json!([{"id":"glow","name":" ","entry":"widgets/banner.js"}]),
        ),
        (
            "animations",
            json!([{"id":"bounce","entry":"widgets/banner.js"}]),
        ),
        ("fonts", json!([{"src":"fonts/font.woff2"}])),
        ("fonts", json!([{"family":" ","src":"fonts/font.woff2"}])),
        (
            "widgets",
            json!([{"id":"banner","name":"Banner","entry":"widgets/banner.js","css":"missing.css"}]),
        ),
        (
            "widgets",
            json!([{"id":"banner","name":"Banner","entry":"logo.png"}]),
        ),
        ("fonts", json!([{"family":"Font","src":"logo.png"}])),
        (
            "fonts",
            json!([{"family":"Font","src":"fonts/font.woff2","weight":400}]),
        ),
        ("assets", json!(["missing.svg"])),
        ("assets", json!(["../logo.png"])),
        ("assets", json!([42])),
        ("assets", json!({"file":"logo.png"})),
    ] {
        let mut candidate = manifest();
        candidate[kind] = entries;
        assert!(
            library
                .install_pack(&archive(
                    candidate,
                    &[
                        ("widgets/banner.js", b"new"),
                        ("fonts/font.woff2", b"font"),
                        ("logo.png", b"image")
                    ]
                ))
                .is_err(),
            "invalid {kind} was accepted"
        );
        assert!(std::fs::read(
            library
                .extension_path("test-pack", "widgets/banner.js")
                .unwrap()
        )
        .unwrap()
        .starts_with(b"CcsCanvas"));
        assert_eq!(library.packs().unwrap()[0]["version"], "1.0");
    }
}

#[test]
fn csharp_manifest_casing_windows_paths_and_null_lists_are_normalized_for_both_platforms() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    let data = archive(
        json!({"ID":"test-pack","NAME":"Windows Pack","VERSION":"2.0","APIVERSION":1,
        "Widgets":[{"ID":"banner","NAME":"Banner","ENTRY":"WIDGETS\\BANNER.JS","CSS":"styles\\main.css","future":true}],
        "Effects":null,"Animations":null,"Fonts":[{"Family":"My Font","Src":"fonts\\font.WOFF2"}],
        "Assets":["ASSETS/logo.SVG"],"future":{"keep":true}}),
        &[
            ("widgets\\banner.js", b"window.fixture=1"),
            ("styles\\main.css", b".banner{color:red}"),
            ("fonts\\font.woff2", b"font"),
            ("assets\\logo.svg", b"<svg/>"),
        ],
    );
    let installed = library.install_pack(&data).unwrap();
    assert_eq!(installed["id"], "test-pack");
    assert_eq!(installed["widgets"][0]["entry"], "widgets/banner.js");
    assert_eq!(installed["widgets"][0]["css"], "styles/main.css");
    assert_eq!(installed["fonts"][0]["src"], "fonts/font.woff2");
    assert_eq!(installed["assets"], json!(["assets/logo.svg"]));
    assert_eq!(installed["effects"], json!([]));
    assert_eq!(installed["animations"], json!([]));
    assert_eq!(installed["future"]["keep"], true);
    assert_eq!(installed["widgets"][0]["future"], true);
    let reopened = MediaLibrary::new(dir.path());
    let catalog = reopened.packs().unwrap();
    assert_eq!(catalog[0]["baseUrl"], "/ext/test-pack/");
    assert_eq!(
        std::fs::read(
            reopened
                .extension_path("test-pack", "widgets/banner.js")
                .unwrap()
        )
        .unwrap(),
        b"window.fixture=1"
    );
}

#[test]
fn catalog_skips_broken_packs_and_sorts_valid_csharp_manifests() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    for (id, name) in [("zulu", "Zulu"), ("alpha", "alpha")] {
        let mut candidate = manifest();
        candidate["id"] = json!(id);
        candidate["name"] = json!(name);
        library.install_pack(&archive(candidate, &[])).unwrap();
    }
    for (id, content) in [
        ("broken", "{invalid"),
        ("absent", ""),
        (
            "future",
            "{\"id\":\"future\",\"name\":\"Future\",\"version\":\"1\",\"apiVersion\":2}",
        ),
    ] {
        let root = dir.path().join("extensions").join(id);
        std::fs::create_dir(&root).unwrap();
        if !content.is_empty() {
            std::fs::write(root.join("manifest.json"), content).unwrap();
        }
    }
    let packs = library.packs().unwrap();
    assert_eq!(
        packs
            .iter()
            .map(|p| p["id"].as_str().unwrap())
            .collect::<Vec<_>>(),
        vec!["alpha", "zulu"]
    );
    assert!(dir.path().join("extensions/broken/manifest.json").is_file());
}

#[test]
fn mixed_case_manifest_filename_and_defaults_match_csharp() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    zip.start_file("MANIFEST.JSON", zip::write::SimpleFileOptions::default())
        .unwrap();
    zip.write_all(manifest().to_string().as_bytes()).unwrap();
    let installed = library
        .install_pack(&zip.finish().unwrap().into_inner())
        .unwrap();
    for kind in ["widgets", "effects", "animations", "fonts", "assets"] {
        assert_eq!(installed[kind], json!([]));
    }
    assert!(library
        .extension_path("test-pack", "manifest.json")
        .unwrap()
        .is_file());
}

#[test]
fn extraction_failure_preserves_old_content_and_cleans_staging() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    library.install_pack(&pack("widgets/banner.js")).unwrap();
    assert!(library
        .install_pack(&archive(
            manifest(),
            &[("block.js", b"file"), ("block.js/child.js", b"child")]
        ))
        .is_err());
    assert!(library
        .extension_path("test-pack", "widgets/banner.js")
        .unwrap()
        .is_file());
    assert_eq!(
        std::fs::read_dir(dir.path().join("extensions"))
            .unwrap()
            .count(),
        1
    );
}

#[test]
fn archive_rules_reject_traversal_duplicate_case_names_symlinks_and_bad_schema() {
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    library.install_pack(&pack("widgets/banner.js")).unwrap();
    for path in [
        "../escape.js",
        "..\\escape.js",
        "/absolute.js",
        "C:\\evil.js",
        "payload.exe",
    ] {
        assert!(
            library
                .install_pack(&archive(manifest(), &[(path, b"bad")]))
                .is_err(),
            "{path}"
        );
    }
    assert!(library
        .install_pack(&archive(
            manifest(),
            &[("logo.svg", b"a"), ("LOGO.svg", b"b")]
        ))
        .is_err());
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let options = zip::write::SimpleFileOptions::default();
    zip.start_file("manifest.json", options).unwrap();
    zip.write_all(manifest().to_string().as_bytes()).unwrap();
    zip.add_symlink("link.js", "../escape.js", options).unwrap();
    assert!(library
        .install_pack(&zip.finish().unwrap().into_inner())
        .is_err());
    for (field, value) in [
        ("id", json!("UPPER")),
        ("name", json!(" ")),
        ("version", json!(null)),
        ("apiVersion", json!(2)),
        ("widgets", json!([null])),
    ] {
        let mut candidate = manifest();
        candidate[field] = value;
        assert!(
            library.install_pack(&archive(candidate, &[])).is_err(),
            "{field}"
        );
    }
    assert!(library
        .extension_path("test-pack", "widgets/banner.js")
        .unwrap()
        .is_file());
    assert_eq!(
        std::fs::read_dir(dir.path().join("extensions"))
            .unwrap()
            .count(),
        1
    );
}

#[test]
fn both_compressed_and_expanded_pack_limits_preserve_an_existing_installation() {
    const LIMIT: usize = 50 * 1024 * 1024;
    let dir = tempfile::tempdir().unwrap();
    let library = MediaLibrary::new(dir.path());
    library.install_pack(&pack("widgets/banner.js")).unwrap();
    assert!(library.install_pack(&vec![0; LIMIT + 1]).is_err());
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let options = zip::write::SimpleFileOptions::default()
        .compression_method(zip::CompressionMethod::Deflated);
    zip.start_file("manifest.json", options).unwrap();
    zip.write_all(manifest().to_string().as_bytes()).unwrap();
    zip.start_file("large.json", options).unwrap();
    zip.write_all(&vec![0; LIMIT + 1]).unwrap();
    let zipped = zip.finish().unwrap().into_inner();
    assert!(zipped.len() < LIMIT);
    assert!(library.install_pack(&zipped).is_err());
    assert!(library
        .extension_path("test-pack", "widgets/banner.js")
        .unwrap()
        .is_file());
}
