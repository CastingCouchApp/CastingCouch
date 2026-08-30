# CastingCouch Tauri — lokale Targets
# Voraussetzung: Node 18+, Rust stable, Plattform-Toolchain (Windows: WebView2; macOS: Xcode CLT)

.PHONY: help install dev test build build-nsis build-dmg

help:
	@echo "Targets:"
	@echo "  make help        - diese Übersicht"
	@echo "  make install     - npm-Abhängigkeiten installieren"
	@echo "  make dev         - Tauri + React Dev-Server"
	@echo "  make test        - Frontend- (Vitest) + Rust-Workspace-Tests"
	@echo "  make build       - Release-Binary ohne Installer (--bundles none)"
	@echo "  make build-nsis  - Windows-NSIS-Installer"
	@echo "  make build-dmg   - macOS-DMG"

install:
	npm install --no-audit --no-fund

dev: install
	npm run tauri dev

test: install
	npm test
	cd src-tauri && cargo test --workspace

build: install
	npm run tauri build -- --bundles none

build-nsis: install
	npm run tauri build -- --bundles nsis

build-dmg: install
	npm run tauri build -- --bundles dmg
