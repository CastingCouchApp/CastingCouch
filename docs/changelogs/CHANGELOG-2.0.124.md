# Creator Control Suite 2.0.124

## PowerShell 5.1 build-contract compatibility
- Fixed Test-AppBuildConfiguration.ps1 so each Join-Path call is evaluated separately before being added to the array.
- Prevents System.Object[] from being passed to Join-Path -ChildPath under Windows PowerShell 5.1.
- Retains RID-neutral normal builds and win-x64/self-contained publish isolation from 2.0.123.
