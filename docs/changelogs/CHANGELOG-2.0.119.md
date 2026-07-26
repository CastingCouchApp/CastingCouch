# Creator Control Suite 2.0.119

## Clean-build launcher and native-process recovery
- Removed the UTF-8 BOM from CMD launcher files so cmd.exe no longer interprets the first command as `´╗┐@echo`.
- Fixed native process execution under Windows PowerShell 5.1 when `$ErrorActionPreference` is `Stop`.
- Native stderr output is now captured and logged without being misclassified as a process-start failure.
- Build steps are judged by the actual native process exit code.
- Added explicit checks for a missing executable and a missing native exit code.
- Updated clean-release package and application version references to 2.0.119.
