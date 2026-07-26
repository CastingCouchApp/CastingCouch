# Creator Control Suite 2.0.127

## StreamDeck build graph repair
- Restored the StreamDeck module to the same explicit class-library build contract used by the successfully building modules.
- Removed the unused project reference from StreamDeck to the Workflow module, reducing the project graph and eliminating an unnecessary transitive build edge.
- Kept only the required Core project reference.
- Updated the generated Stream Deck profile manifest version to 2.0.127.
- Updated the suite version to 2.0.127.
