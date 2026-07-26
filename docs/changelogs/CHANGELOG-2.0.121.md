# Creator Control Suite 2.0.121

## Clean-build reliability
- Reworked generated-output cleanup so `bin` and `obj` directories are removed as single targets instead of relying on unstable child-file enumeration.
- Benign races where transient files or directories disappear during cleanup no longer abort the release build.
- Cleanup still fails explicitly when a generated-output directory remains after deletion.
- Corrected the test phase: tests now build their own project before execution instead of using `--no-build` after only the app project was built.
- Retains the .NET 10 SDK preflight, native-process wrapper fixes, and StreamDeck release-build corrections from 2.0.117-2.0.120.
