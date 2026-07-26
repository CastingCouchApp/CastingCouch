# Creator Control Suite 2.0.127

## Windows path-length build resilience
- Fixed the persistent StreamDeck MSB3030 build failure caused by the generated DLL path exceeding the classic Windows MAX_PATH limit.
- All normal build intermediates now use the short centralized path `artifacts/obj/<ProjectName>/`.
- All normal build outputs now use the short centralized path `artifacts/bin/<ProjectName>/`.
- Clean Release removes only these centralized build caches while preserving build logs and release artifacts.
- Added a preflight build-path contract so long nested project output paths cannot silently return.
- The source ZIP now uses the short root directory `CreatorControlSuite-2.0.127`.
