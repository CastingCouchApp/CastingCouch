# Creator Control Suite 2.0.124

## Build diagnostics and StreamDeck project normalization
- Normalized the StreamDeck module project file to the same minimal SDK-style structure used by successfully building modules.
- Removed unnecessary explicit assembly-generation/reference-assembly properties from the StreamDeck module.
- Failed native build steps now print relevant compiler/MSBuild/NuGet error lines directly to the console before the wrapper exception.
- If no standard error signature is detected, the last 40 native output lines are printed automatically.
- Full native output continues to be written to artifacts/build-logs.
