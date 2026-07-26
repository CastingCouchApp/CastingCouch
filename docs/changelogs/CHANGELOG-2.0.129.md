# Creator Control Suite 2.0.129

## Build script BOM and PowerShell 5.1 compatibility
- Removed UTF-8 BOM markers from all executable build scripts.
- Removed embedded U+FEFF characters that could turn `Set-StrictMode` into an invalid command in Windows PowerShell 5.1.
- Added `Test-BuildScriptEncoding.ps1` to detect BOM-prefixed or embedded BOM characters before build/publish steps.
- The encoding contract check now runs from both the clean release build and the standalone preflight.
- Updated clean release/package version references to 2.0.129.
