# Creator Control Suite 2.0.124

## Clean build RID isolation fix
- Removed `RuntimeIdentifier=win-x64` and `SelfContained=true` from the permanent App project configuration.
- Normal `dotnet build` is now RID-neutral, so referenced class libraries use one consistent intermediate output layout.
- Windows x64 and self-contained settings remain explicitly applied only by the publish scripts.
- Added a build contract check that rejects a permanent RuntimeIdentifier or SelfContained setting in the App project.
- This addresses clean-build MSB3030 errors where module assemblies were generated/looked up in inconsistent RID and non-RID intermediate paths.
