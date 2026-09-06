use std::{env, fs, path::Path};

fn main() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../../src/CreatorControlSuite.Modules.Overlay/CanvasOverlay");
    let mut entries = Vec::new();
    for dir in ["editor", "view", "solo", "shared"] {
        collect(&root, &root.join(dir), &mut entries);
    }
    for required in [
        "editor/index.html",
        "editor/editor.js",
        "shared/runtime.js",
        "shared/styles.css",
    ] {
        assert!(
            entries.iter().any(|(name, _)| name == required),
            "Overlay-Bundle {required} fehlt. Zuerst npm run build im CanvasOverlay ausführen."
        );
    }
    entries.sort();
    let mut generated = String::from("pub fn embedded_asset(path: &str) -> Option<(Vec<u8>, String)> { let bytes: &[u8] = match path {\n");
    for (name, path) in entries {
        generated.push_str(&format!("{name:?} => include_bytes!({path:?}),\n"));
    }
    generated.push_str("_ => return None, }; Some((bytes.to_vec(), mime_guess::from_path(path).first_or_octet_stream().essence_str().to_string())) }\n");
    fs::write(
        Path::new(&env::var("OUT_DIR").unwrap()).join("canvas_assets.rs"),
        generated,
    )
    .unwrap();
}

fn collect(root: &Path, dir: &Path, entries: &mut Vec<(String, String)>) {
    println!("cargo:rerun-if-changed={}", dir.display());
    for entry in fs::read_dir(dir).unwrap_or_else(|e| panic!("{}: {e}", dir.display())) {
        let path = entry.unwrap().path();
        if path.is_dir() {
            collect(root, &path, entries);
        } else if path.extension().and_then(|v| v.to_str()) != Some("map") {
            entries.push((
                path.strip_prefix(root)
                    .unwrap()
                    .to_string_lossy()
                    .replace('\\', "/"),
                path.to_string_lossy().into_owned(),
            ));
        }
    }
}
