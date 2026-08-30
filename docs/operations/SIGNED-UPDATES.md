# Signierte Updates

RSA-SHA256-Prüfung für Update-Manifeste sowie SHA-256- und Größenprüfung für Pakete.

## ProductId und Kanäle

**ProductId ist immer `CreatorControlSuite`** — WPF und Tauri, gleicher RSA-Public-Key.

Der Settings-Kanal `Alpha` / `Beta` / `Stable` kommt aus der Versionsnummer (`8.0.0-beta1` → Beta) und liegt in der gemeinsamen `settings.json`. Er trennt **nicht** WPF von Tauri.

Stack-Trennung über **Manifest-Dateinamen** (paralleler WPF- vs. Tauri-Kanal):

| Client | Manifest-Asset | Paket |
|--------|----------------|-------|
| WPF | `update-manifest.json` | `CreatorControlSuite-{version}-win-x64.zip` |
| Tauri Windows | `update-manifest-tauri-win.json` | `CastingCouch-{version}-win-x64-setup.exe` (NSIS; MSI zusätzlich im Release) |
| Tauri macOS | `update-manifest-tauri-macos.json` | `CastingCouch-{version}-macos.dmg` |

WPF liest weiterhin nur `update-manifest.json`. Die Tauri-App lädt das OS-spezifische `update-manifest-tauri-*.json`.

## Artefakte pro Release

GitHub Release (`CastingCouchApp/CastingCouch`) enthält:

- `CreatorControlSuite-{version}-win-x64.zip` – WPF Portable- und Update-Payload
- `CreatorControlSuite-{version}-x64.msi` – WPF-Installer
- `update-manifest.json` – signiertes WPF-Manifest
- `CastingCouch-{version}-win-x64-setup.exe` – Tauri NSIS (currentUser)
- `CastingCouch-{version}-win-x64.msi` – Tauri MSI
- `CastingCouch-{version}-macos.dmg` – Tauri macOS (Phase 5 ohne Apple-Notarize)
- `update-manifest-tauri-win.json` / `update-manifest-tauri-macos.json` – signierte Tauri-Manifeste

## Signierung

1. Public Key liegt unter `src/CreatorControlSuite.App/Keys/update-public.pem` (wird mit ausgeliefert; Tauri bettet dieselbe Datei ein).
2. Private Key nur als GitHub-Secret `UPDATE_SIGNING_KEY_PEM` oder lokal unter `tools/dev-keys/update-private.pem`.
3. Kanonische Payload (UTF-8, `\n`-Zeilen):

```
ProductId
Version
Channel
PackageFileName
PackageSha256
PackageSizeBytes
PublishedAt (ISO-8601 "o", UTC)
MinimumVersion
ReleaseNotes (CRLF → LF)
```

Signatur: RSA-SHA256, PKCS#1, Base64 im Feld `Signature`.

Lokal erzeugen:

```powershell
./build/New-UpdateArtifacts.ps1 `
  -PackageZipPath artifacts/release/CreatorControlSuite-VERSION-win-x64.zip `
  -Version VERSION `
  -PrivateKeyPath tools/dev-keys/update-private.pem

./build/New-UpdateArtifacts.ps1 `
  -PackageZipPath artifacts/tauri/CastingCouch-VERSION-win-x64-setup.exe `
  -Version VERSION `
  -ManifestFileName update-manifest-tauri-win.json `
  -PrivateKeyPath tools/dev-keys/update-private.pem
```

Smoke-Test: `./build/Test-UpdateManifest.ps1`.

Schlüssel neu erzeugen: `./tools/Generate-DevelopmentKeys.ps1` (Public Key anschließend nach `src/CreatorControlSuite.App/Keys/` kopieren).
