# CastingCouch (Tauri)

React + Tailwind + TanStack (Router, Query, Table, Form) mit Rust-Backend.

```bash
# aus dem Repo-Root
make -C tauri-app help
make -C tauri-app install
make -C tauri-app test
make -C tauri-app dev

# oder Wrapper vom Root
make tauri-dev
make tauri-test
make tauri-build
make tauri-build-nsis   # Windows
make tauri-build-dmg    # macOS
```

Lokal in diesem Ordner:

```bash
cd tauri-app
make install
make test
make dev
make build          # Binary ohne Installer
make build-nsis     # Windows-NSIS
make build-dmg      # macOS-DMG
```

Ohne Make:

```bash
cd tauri-app
npm install
npm test
npm run tauri dev
```

Overlay-Loopback: `http://127.0.0.1:8765` (gleiche Routen wie die WPF-Kestrel-Instanz).
