# CastingCouch — lokale Build-Targets
# Voraussetzung: .NET 10 SDK (x64), Windows (WPF / net10.0-windows)

DOTNET      ?= dotnet
SLN         := CreatorControlSuite.sln
APP         := src/CreatorControlSuite.App/CreatorControlSuite.App.csproj
CMDCLIENT   := src/CreatorControlSuite.CommandClient/CreatorControlSuite.CommandClient.csproj
UPDATER     := src/CreatorControlSuite.Updater/CreatorControlSuite.Updater.csproj
TESTS       := tests/CreatorControlSuite.Tests/CreatorControlSuite.Tests.csproj
CONFIG      ?= Release
RUN_CONFIG ?= Debug
RID         ?= win-x64
ARTIFACTS   := artifacts
PUBLISH_DIR := $(ARTIFACTS)/publish/$(RID)
LOG_DIR     := $(ARTIFACTS)/build-logs
TEST_DIR    := $(ARTIFACTS)/test-results

# Windows hat oft nur powershell.exe; pwsh optional via PWSH=pwsh
ifeq ($(OS),Windows_NT)
PWSH ?= powershell
else
PWSH ?= pwsh
endif

.PHONY: help restore canvas canvas-dev build test publish app clean ci release run watch format format-check format-analyzers

help:
	@echo "Targets:"
	@echo "  make restore         - NuGet-Pakete wiederherstellen"
	@echo "  make canvas          - Canvas Overlay TypeScript bundlen"
	@echo "  make canvas-dev      - Overlay Editor im Browser (Hot-Reload, Mock-Events)"
	@echo "  make build           - Solution bauen (CONFIG=$(CONFIG))"
	@echo "  make test            - Tests ausführen"
	@echo "  make format          - C# Autoformat (whitespace + style)"
	@echo "  make format-check    - Format prüfen ohne Änderungen"
	@echo "  make format-analyzers - Analyzer-Fixes (optional, kann scheitern)"
	@echo "  make publish         - App self-contained publishen ($(RID))"
	@echo "  make app             - restore + test + publish"
	@echo "  make ci              - restore + build + test"
	@echo "  make release         - voller Release-Build (App+Client+Updater+MSI, Windows/pwsh)"
	@echo "  make run             - App bauen, alte Instanz beenden, neu starten (RUN_CONFIG=$(RUN_CONFIG))"
	@echo "  make watch           - App mit Hot Reload starten (dotnet watch)"
	@echo "  make clean           - Build-Artefakte löschen"
	@echo ""
	@echo "Variablen: CONFIG=$(CONFIG) RUN_CONFIG=$(RUN_CONFIG) RID=$(RID) DOTNET=$(DOTNET) PWSH=$(PWSH)"

CANVAS_DIR := src/CreatorControlSuite.Modules.Overlay/CanvasOverlay

restore:
	$(DOTNET) restore $(SLN)

canvas:
	@cd $(CANVAS_DIR) && npm ci --prefer-offline --no-audit --no-fund 2>/dev/null || (cd $(CANVAS_DIR) && npm install --no-audit --no-fund)
	cd $(CANVAS_DIR) && npm run build

canvas-dev:
	@cd $(CANVAS_DIR) && npm ci --prefer-offline --no-audit --no-fund 2>/dev/null || (cd $(CANVAS_DIR) && npm install --no-audit --no-fund)
	cd $(CANVAS_DIR) && npm run dev

build: restore
	$(DOTNET) build $(SLN) -c $(CONFIG) --no-restore

test: build
	@mkdir -p $(TEST_DIR) $(LOG_DIR)
	$(DOTNET) test $(TESTS) -c $(CONFIG) --no-build \
		--logger "trx;LogFileName=tests.trx" \
		--results-directory $(TEST_DIR)

publish: build
	@mkdir -p $(PUBLISH_DIR) $(LOG_DIR)
	$(DOTNET) publish $(APP) -c $(CONFIG) -r $(RID) \
		--self-contained true \
		-p:PublishReadyToRun=false \
		-p:DebugType=embedded \
		-p:ContinuousIntegrationBuild=true \
		-o $(PUBLISH_DIR)
	$(DOTNET) publish $(CMDCLIENT) -c $(CONFIG) -r $(RID) \
		--self-contained true \
		-p:PublishSingleFile=true \
		-o $(PUBLISH_DIR)
	$(DOTNET) publish $(UPDATER) -c $(CONFIG) -r $(RID) \
		--self-contained true \
		-p:PublishSingleFile=true \
		-o $(PUBLISH_DIR)

app: restore test publish
	@echo "Publish: $(PUBLISH_DIR)"

ci: restore build test

release:
	@command -v $(PWSH) >/dev/null 2>&1 || { echo "$(PWSH) nicht gefunden"; exit 1; }
	$(PWSH) -NoProfile -ExecutionPolicy Bypass -File ./build/Build-Release.ps1 -Configuration $(CONFIG)

# App bauen, laufende Instanz beenden, neu starten.
# Override: make run RUN_CONFIG=Release
run:
	$(PWSH) -NoProfile -ExecutionPolicy Bypass -File ./build/Build-And-Restart-App.ps1 -Configuration $(RUN_CONFIG)

watch:
	$(PWSH) -NoProfile -ExecutionPolicy Bypass -File ./scripts/run-app-hotreload.ps1 -Configuration $(CONFIG)

# C# coding style: .editorconfig + SDK `dotnet format`
# Whitespace + Style sind solution-weit reliable.
# Analyzers (CA*) separat: viele Fixes unterstützen kein „Alle korrigieren“ /
# MSBuildWorkspace lehnt Compilation-Option-Änderungen ab.
FORMAT_STYLE_EXCLUDE := IDE1006,IDE0060

format: restore
	$(DOTNET) format whitespace $(SLN) --verbosity minimal
	$(DOTNET) format style $(SLN) \
		--severity info \
		--exclude-diagnostics $(FORMAT_STYLE_EXCLUDE) \
		--verbosity minimal

format-analyzers: restore
	$(DOTNET) format analyzers $(SLN) --severity warn --verbosity minimal

format-check: restore
	$(DOTNET) format whitespace $(SLN) --verify-no-changes --verbosity minimal
	$(DOTNET) format style $(SLN) \
		--severity info \
		--exclude-diagnostics $(FORMAT_STYLE_EXCLUDE) \
		--verify-no-changes \
		--verbosity minimal

clean:
	rm -rf $(ARTIFACTS)
	$(DOTNET) clean $(SLN) -c $(CONFIG) || true
