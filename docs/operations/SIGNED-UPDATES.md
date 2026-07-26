# Signierte Updates

RSA-SHA256-Prüfung für Update-Manifeste sowie SHA-256- und Größenprüfung für Pakete.

## Artefakte pro Release

GitHub Release (`frankhildebrandt/CreatorControlSuite`) enthält:

- `CreatorControlSuite-{version}-win-x64.zip` – Portable- und Update-Payload
- `CreatorControlSuite-{version}-x64.msi` – Installer
- `update-manifest.json` – signiertes `SignedUpdateManifest`

## Signierung

1. Public Key liegt unter `src/CreatorControlSuite.App/Keys/update-public.pem` (wird mit ausgeliefert).
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
```

Schlüssel neu erzeugen: `./tools/Generate-DevelopmentKeys.ps1` (Public Key anschließend nach `src/CreatorControlSuite.App/Keys/` kopieren).
