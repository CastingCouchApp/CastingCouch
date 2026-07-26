# Creator Control Suite — lokale Build-Targets
# Voraussetzung: .NET 10 SDK (x64), Windows (WPF / net10.0-windows)

DOTNET      ?= dotnet
SLN         := CreatorControlSuite.sln
APP         := src/CreatorControlSuite.App/CreatorControlSuite.App.csproj
CMDCLIENT   := src/CreatorControlSuite.CommandClient/CreatorControlSuite.CommandClient.csproj
UPDATER     := src/CreatorControlSuite.Updater/CreatorControlSuite.Updater.csproj
TESTS       := tests/CreatorControlSuite.Tests/CreatorControlSuite.Tests.csproj
CONFIG      ?= Release
RID         ?= win-x64
ARTIFACTS   := artifacts
PUBLISH_DIR := $(ARTIFACTS)/publish/$(RID)
LOG_DIR     := $(ARTIFACTS)/build-logs
TEST_DIR    := $(ARTIFACTS)/test-results

.PHONY: help restore build test publish app clean ci release

help:
	@echo "Targets:"
	@echo "  make restore   - NuGet-Pakete wiederherstellen"
	@echo "  make build     - Solution bauen (CONFIG=$(CONFIG))"
	@echo "  make test      - Tests ausführen"
	@echo "  make publish   - App self-contained publishen ($(RID))"
	@echo "  make app       - restore + test + publish"
	@echo "  make ci        - restore + build + test"
	@echo "  make release   - voller Release-Build (App+Client+Updater+MSI, Windows/pwsh)"
	@echo "  make clean     - Build-Artefakte löschen"
	@echo ""
	@echo "Variablen: CONFIG=$(CONFIG) RID=$(RID) DOTNET=$(DOTNET)"

restore:
	$(DOTNET) restore $(SLN)

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
	@command -v pwsh >/dev/null 2>&1 || { echo "pwsh nicht gefunden"; exit 1; }
	pwsh -NoProfile -ExecutionPolicy Bypass -File ./build/Build-Release.ps1 -Configuration $(CONFIG)

clean:
	rm -rf $(ARTIFACTS)
	$(DOTNET) clean $(SLN) -c $(CONFIG) || true
