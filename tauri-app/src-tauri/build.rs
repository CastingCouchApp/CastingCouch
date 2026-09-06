fn main() {
    // Let the linker embed the same manifest for app and test executables,
    // avoiding a duplicate resource in the application's resource.lib.
    tauri_build::try_build(
        tauri_build::Attributes::new()
            .windows_attributes(tauri_build::WindowsAttributes::new_without_app_manifest()),
    )
    .expect("Tauri build metadata");
    // Unit-test executables also load Tauri's Windows UI dependencies. They need
    // Common Controls v6 (TaskDialogIndirect), just like the application binary.
    if std::env::var("TARGET")
        .unwrap_or_default()
        .ends_with("windows-msvc")
    {
        println!("cargo:rustc-link-arg=/MANIFEST:EMBED");
        println!("cargo:rustc-link-arg=/MANIFESTDEPENDENCY:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'");
    }
}
