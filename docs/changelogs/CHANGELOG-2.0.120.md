# Creator Control Suite 2.0.120

## Clean build project-output fix
- StreamDeck module now declares explicit Library output and reference-assembly generation, matching the stable module projects.
- Clean Release builds the actual application project instead of the complete solution, so the development-only LicenseMockServer can no longer block a production release.
- The test project is still executed and therefore continues to compile and validate the StreamDeck module.
- LicenseMockServer retains an explicit executable configuration and disables reference-assembly generation.
- Critical project configuration checks now validate the StreamDeck output contract.
