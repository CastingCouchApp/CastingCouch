Produktive Public Keys hier ablegen:
update-public.pem

Private Schlüssel niemals ausliefern.

Update-Signierung:
- Public: update-public.pem (dieses Verzeichnis, wird published)
- Private: GitHub Secret UPDATE_SIGNING_KEY_PEM oder tools/dev-keys/update-private.pem
- Erzeugen: tools/Generate-DevelopmentKeys.ps1 (Public Key hierher kopieren)
