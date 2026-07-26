# Creator Control Suite 2.0.117

## Build and release recovery
- Fixed stale 2.0.81 version metadata in the central build properties, clean release script, setup package name and build launcher.
- Added an explicit .NET 10 SDK validation before restore/build/publish.
- The build now distinguishes between a missing SDK, a runtime-only installation and an unusable dotnet host.
- Added `global.json` to select .NET 10 while allowing newer installed .NET 10 feature bands.
- Build-App and Clean-Release now reuse the validated absolute dotnet executable path.
- Native build-process startup failures now produce a direct diagnostic message and text log.
- PowerShell build scripts touched by this release are saved with UTF-8 BOM for correct German output in Windows PowerShell 5.1.
