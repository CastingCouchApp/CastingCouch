use ccs_overlay_server::embedded_asset;

#[test]
fn distribution_contains_editor_runtime_and_styles_without_a_checkout() {
    for path in [
        "editor/index.html",
        "editor/editor.js",
        "editor/editor.css",
        "shared/runtime.js",
        "shared/styles.css",
        "view/index.html",
        "solo/index.html",
    ] {
        let (bytes, _) =
            embedded_asset(path).unwrap_or_else(|| panic!("Missing packaged asset: {path}"));
        assert!(!bytes.is_empty(), "{path}");
    }
    assert!(embedded_asset("../Cargo.toml").is_none());
    assert!(embedded_asset("src/editor/index.ts").is_none());
}
