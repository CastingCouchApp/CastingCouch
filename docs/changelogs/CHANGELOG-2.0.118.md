# Creator Control Suite 2.0.118

## Build SDK detection hotfix
- Fixed .NET 10 SDK detection for versions such as 10.0.302.
- Replaced the fragile PowerShell regex-only check with explicit System.Version parsing.
- The SDK preflight now accepts every installed SDK whose major version is 10.
- Retained UTF-8 with BOM for Windows PowerShell 5.1 compatibility and correct German console output.
