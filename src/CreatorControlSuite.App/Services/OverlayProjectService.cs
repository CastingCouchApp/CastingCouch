using System.Text.Json;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.Overlay;
using System.Runtime.InteropServices;

namespace CreatorControlSuite.App.Services;

public sealed class OverlayProjectService
{
    private readonly IObsWebSocketClient _obs;
    private readonly IAppLogger _logger;
    private readonly IOverlayDataService _overlayData;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _catalogPath;

    public OverlayProjectService(IObsWebSocketClient obs, IAppLogger logger, IOverlayDataService overlayData)
    {
        _obs = obs;
        _logger = logger;
        _overlayData = overlayData;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "overlays");
        Directory.CreateDirectory(dir);
        _catalogPath = Path.Combine(dir, "overlay-projects.json");
    }

    public async Task<List<OverlayProjectDefinition>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_catalogPath)) return [];
        await using var stream = File.OpenRead(_catalogPath);
        var projects = await JsonSerializer.DeserializeAsync<List<OverlayProjectDefinition>>(stream, _json, ct) ?? [];
        foreach (var project in projects.Where(x => !string.IsNullOrWhiteSpace(x.RootPath) && Directory.Exists(x.RootPath)))
        {
            try
            {
                await EnsureCentralDataReferenceAsync(project, ct);
            }
            catch (Exception exception)
            {
                project.DataReferenceStatus = $"Datenverknüpfung fehlt: {exception.Message}";
            }
        }
        return projects;
    }

    public async Task SaveAsync(IEnumerable<OverlayProjectDefinition> projects, CancellationToken ct = default)
    {
        var tmp = _catalogPath + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, projects, _json, ct);
        File.Move(tmp, _catalogPath, true);
    }

    public async Task<OverlayProjectDefinition> ImportFolderAsync(string folder, string? preferredManifestPath = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("Der ausgewählte Overlay-Ordner wurde nicht gefunden.");

        var fullFolder = Path.GetFullPath(folder);
        var project = await ReadManifestOrScanAsync(fullFolder, preferredManifestPath, ct);
        project.Id = string.IsNullOrWhiteSpace(project.Id) ? Guid.NewGuid().ToString("N") : project.Id;
        project.RootPath = fullFolder;
        project.ManifestPath = Path.Combine(fullFolder, "overlay.json");
        project.ImportedAt = DateTimeOffset.Now;
        project.LastSynchronizedAt = DateTimeOffset.Now;
        ValidateFiles(project);
        await EnsureCentralDataReferenceAsync(project, ct);
        await WriteManifestAsync(project, project.ManifestPath, ct);
        return project;
    }


    public async Task<List<OverlayProjectItem>> AddSceneAsync(OverlayProjectDefinition project, string sceneName, IEnumerable<string> sourceFiles, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (string.IsNullOrWhiteSpace(sceneName)) throw new InvalidOperationException("Bitte gib einen Namen für die neue Szene an.");
        if (string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
            throw new DirectoryNotFoundException("Der lokale Overlay-Projektordner wurde nicht gefunden.");

        var files = sourceFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("Es wurden keine vorhandenen Dateien ausgewählt.");

        var safeSceneFolder = MakeSafeFileName(sceneName);
        var sceneFolder = Path.Combine(project.RootPath, "scenes", safeSceneFolder);
        Directory.CreateDirectory(sceneFolder);
        var added = new List<OverlayProjectItem>();

        foreach (var sourceFile in files)
        {
            var destination = GetUniqueDestination(sceneFolder, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destination, false);
            var relative = Path.GetRelativePath(project.RootPath, destination);
            var sourceType = GetSourceType(destination);
            var item = new OverlayProjectItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Path.GetFileNameWithoutExtension(destination),
                Kind = sourceType == "asset" ? "Asset" : "Scene",
                SourceType = sourceType,
                RelativePath = relative,
                ObsScene = sceneName.Trim(),
                ObsSource = sourceType == "asset" ? "" : BuildSourceName(project, new OverlayProjectItem { Name = sceneName.Trim() + "_" + Path.GetFileNameWithoutExtension(destination) }),
                IsLocalFile = true,
                Enabled = true,
                Status = sourceType == "asset" ? "Projektdatei gespeichert" : "Bereit für OBS"
            };
            project.Items.Add(item);
            added.Add(item);
        }

        if (_obs.IsConnected)
        {
            await _obs.EnsureSceneAsync(sceneName.Trim(), ct);
            foreach (var item in added.Where(x => x.SourceType != "asset"))
                await SynchronizeItemWithObsAsync(project, item, ct);
        }
        else
        {
            foreach (var item in added.Where(x => x.SourceType != "asset")) item.Status = "Gespeichert · OBS nicht verbunden";
        }

        await EnsureCentralDataReferenceAsync(project, ct);
        project.LastSynchronizedAt = DateTimeOffset.Now;
        project.Status = _obs.IsConnected
            ? $"Szene '{sceneName.Trim()}' mit {added.Count(x => x.SourceType != "asset")} OBS-Quellen angelegt"
            : $"Szene '{sceneName.Trim()}' gespeichert · OBS nicht verbunden";
        await WriteManifestAsync(project, GetManifestPath(project), ct);
        return added;
    }

    public async Task<OverlayProjectDefinition> ImportFromObsAsync(string name, CancellationToken ct = default)
    {
        if (!_obs.IsConnected) throw new InvalidOperationException("OBS ist nicht verbunden.");
        var project = new OverlayProjectDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "OBS Overlay-Projekt" : name.Trim(),
            Version = "1.0",
            RootPath = "",
            ImportedAt = DateTimeOffset.Now,
            LastSynchronizedAt = DateTimeOffset.Now,
            Source = "OBS"
        };

        var scenes = await _obs.GetSceneListAsync(ct);
        var inputs = await _obs.GetInputListAsync(ct);
        var browserNames = inputs.Where(x => string.Equals(x.UnversionedKind, "browser_source", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Kind, "browser_source", StringComparison.OrdinalIgnoreCase)).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var scene in scenes)
        {
            var items = await _obs.GetSceneItemListAsync(scene.Name, ct);
            foreach (var item in items.Where(i => browserNames.Contains(i.SourceName)))
            {
                var settings = await _obs.GetInputSettingsAsync(item.SourceName, ct);
                var localFile = GetString(settings, "local_file");
                var url = GetString(settings, "url");
                var path = !string.IsNullOrWhiteSpace(localFile) ? localFile : url;
                var kind = GuessKind(path, scene.Name);
                project.Items.Add(new OverlayProjectItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = item.SourceName,
                    Kind = kind,
                    RelativePath = path,
                    ObsScene = scene.Name,
                    ObsSource = item.SourceName,
                    IsLocalFile = !string.IsNullOrWhiteSpace(localFile),
                    SourceType = "browser",
                    Enabled = item.Enabled
                });
            }
        }

        project.Items = project.Items.GroupBy(x => $"{x.ObsScene}\0{x.ObsSource}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        project.Status = project.Items.Count == 0 ? "Keine OBS-Browserquellen gefunden" : $"{project.Items.Count} OBS-Browserquellen übernommen";
        var importRoot = Path.Combine(Path.GetDirectoryName(_catalogPath)!, "obs-imports", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(importRoot);
        project.RootPath = importRoot;
        project.ManifestPath = Path.Combine(importRoot, "overlay.json");
        await EnsureCentralDataReferenceAsync(project, ct);
        await WriteManifestAsync(project, project.ManifestPath, ct);
        return project;
    }

    public async Task SynchronizeWithObsAsync(OverlayProjectDefinition project, CancellationToken ct = default)
    {
        if (!_obs.IsConnected) throw new InvalidOperationException("OBS ist nicht verbunden.");
        await EnsureCentralDataReferenceAsync(project, ct);
        foreach (var item in project.Items.Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.ObsScene)))
        {
            if (string.Equals(item.SourceType, "asset", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = "Projektdatei gespeichert";
                continue;
            }
            await SynchronizeItemWithObsAsync(project, item, ct);
        }
        project.LastSynchronizedAt = DateTimeOffset.Now;
        project.Status = "Mit OBS synchronisiert";
        await WriteManifestAsync(project, GetManifestPath(project), ct);
        _logger.Write(AppLogLevel.Information, "Overlay", $"Overlay-Projekt '{project.Name}' wurde mit OBS synchronisiert.");
    }

    private async Task SynchronizeItemWithObsAsync(OverlayProjectDefinition project, OverlayProjectItem item, CancellationToken ct)
    {
        var path = ResolvePath(project, item);
        if (item.IsLocalFile && !File.Exists(path))
        {
            item.Status = "Datei fehlt";
            return;
        }

        var source = string.IsNullOrWhiteSpace(item.ObsSource) ? BuildSourceName(project, item) : item.ObsSource;
        await _obs.EnsureSceneAsync(item.ObsScene, ct);
        var sourceType = string.IsNullOrWhiteSpace(item.SourceType) ? GetSourceType(path) : item.SourceType;
        item.SourceType = sourceType;

        if (sourceType == "image")
        {
            var settings = new { file = path, unload = false };
            if (!await _obs.InputExistsAsync(source, ct))
                await _obs.CreateInputAsync(item.ObsScene, source, "image_source", settings, true, ct);
            else
            {
                await _obs.SetInputSettingsAsync(source, settings, false, ct);
                if (!await _obs.SceneItemExistsAsync(item.ObsScene, source, ct)) await _obs.CreateSceneItemAsync(item.ObsScene, source, true, ct);
            }
        }
        else if (sourceType == "media")
        {
            if (!await _obs.InputExistsAsync(source, ct))
                await _obs.EnsureMediaInputAsync(item.ObsScene, source, path, ct);
            else
            {
                await _obs.SetInputSettingsAsync(source, new { local_file = path, is_local_file = true, looping = false, restart_on_activate = true }, false, ct);
                if (!await _obs.SceneItemExistsAsync(item.ObsScene, source, ct)) await _obs.CreateSceneItemAsync(item.ObsScene, source, true, ct);
            }
        }
        else
        {
            var inputSettings = item.IsLocalFile || !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile
                ? new { is_local_file = true, local_file = path, width = project.Width, height = project.Height, reroute_audio = false, restart_when_active = true, shutdown = true }
                : (object)new { is_local_file = false, url = path, width = project.Width, height = project.Height, reroute_audio = false, restart_when_active = true, shutdown = true };
            if (!await _obs.InputExistsAsync(source, ct))
                await _obs.CreateInputAsync(item.ObsScene, source, "browser_source", inputSettings, true, ct);
            else
            {
                await _obs.SetInputSettingsAsync(source, inputSettings, false, ct);
                if (!await _obs.SceneItemExistsAsync(item.ObsScene, source, ct)) await _obs.CreateSceneItemAsync(item.ObsScene, source, true, ct);
            }
        }

        if (sourceType is "browser" or "image")
            await _obs.SetSceneItemTransformAsync(item.ObsScene, source, 0, 0, project.Width, project.Height, ct);
        item.ObsSource = source;
        item.Status = "Synchronisiert";
    }

    public async Task<string> WriteManifestAsync(OverlayProjectDefinition project, string? manifestPath = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(manifestPath) ? GetManifestPath(project) : Path.GetFullPath(manifestPath);
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Für die overlay.json wurde kein gültiger Ordner angegeben.");
        Directory.CreateDirectory(directory);
        var manifest = new OverlayManifest
        {
            Id = project.Id,
            Name = project.Name,
            Version = project.Version,
            Author = project.Author,
            Width = project.Width,
            Height = project.Height,
            DataSourcePath = project.DataSourcePath,
            DataReferenceMode = project.DataReferenceMode,
            Items = project.Items
        };
        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, manifest, _json, ct);
        File.Move(tmp, path, true);
        project.ManifestPath = path;
        return path;
    }

    public async Task<string> CreateManifestAsync(string manifestPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifestPath)) throw new InvalidOperationException("Bitte gib einen Pfad für die overlay.json an.");
        var fullPath = Path.GetFullPath(manifestPath);
        if (!string.Equals(Path.GetFileName(fullPath), "overlay.json", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.Combine(Path.GetDirectoryName(fullPath) ?? fullPath, "overlay.json");
        var project = new OverlayProjectDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = new DirectoryInfo(Path.GetDirectoryName(fullPath)!).Name,
            RootPath = Path.GetDirectoryName(fullPath)!,
            ManifestPath = fullPath,
            ImportedAt = DateTimeOffset.Now,
            LastSynchronizedAt = DateTimeOffset.Now
        };
        if (Directory.Exists(project.RootPath)) AddScannedHtml(project, project.RootPath);
        await EnsureCentralDataReferenceAsync(project, ct);
        return await WriteManifestAsync(project, fullPath, ct);
    }

    private static string GetManifestPath(OverlayProjectDefinition project)
    {
        if (!string.IsNullOrWhiteSpace(project.ManifestPath)) return Path.GetFullPath(project.ManifestPath);
        if (!string.IsNullOrWhiteSpace(project.RootPath)) return Path.Combine(Path.GetFullPath(project.RootPath), "overlay.json");
        throw new InvalidOperationException("Das Overlay-Projekt besitzt keinen lokalen Pfad für die overlay.json.");
    }

    public void ValidateFiles(OverlayProjectDefinition project)
    {
        foreach (var item in project.Items)
        {
            var path = ResolvePath(project, item);
            item.Status = item.IsLocalFile && !File.Exists(path) ? "Datei fehlt" : "Bereit";
        }
        var missing = project.Items.Count(x => x.Status == "Datei fehlt");
        project.Status = missing == 0 ? $"Bereit · {project.Items.Count} Elemente" : $"{missing} Dateien fehlen";
    }

    private async Task<OverlayProjectDefinition> ReadManifestOrScanAsync(string folder, string? preferredManifestPath, CancellationToken ct)
    {
        var localManifestPath = Path.Combine(folder, "overlay.json");

        // Eine bereits in der Suite ausgewählte overlay.json gehört möglicherweise zu
        // einem völlig anderen Projekt. Sie darf beim Ordnerimport nur verwendet werden,
        // wenn sie tatsächlich innerhalb des neu ausgewählten Ordners liegt.
        var safePreferredManifestPath = GetPreferredManifestInsideFolder(folder, preferredManifestPath);
        var manifestPath = File.Exists(localManifestPath)
            ? localManifestPath
            : safePreferredManifestPath ?? localManifestPath;
        if (File.Exists(manifestPath))
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<OverlayManifest>(stream, _json, ct) ?? new OverlayManifest();
            var project = new OverlayProjectDefinition
            {
                Id = manifest.Id,
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? new DirectoryInfo(folder).Name : manifest.Name,
                Version = string.IsNullOrWhiteSpace(manifest.Version) ? "1.0" : manifest.Version,
                Author = manifest.Author ?? "",
                Width = manifest.Width > 0 ? manifest.Width : 1920,
                Height = manifest.Height > 0 ? manifest.Height : 1080,
                DataSourcePath = manifest.DataSourcePath ?? "",
                DataReferenceMode = manifest.DataReferenceMode ?? "",
                Source = "Ordner",
                ManifestPath = localManifestPath
            };
            foreach (var item in manifest.Items ?? [])
            {
                item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
                item.IsLocalFile = !IsWebUrl(item.RelativePath);
                project.Items.Add(item);
            }

            // Das Dateisystem ist beim Ordnerimport die maßgebliche Quelle. Dadurch
            // werden alte Einträge aus einer versehentlich übernommenen overlay.json
            // entfernt und HTML-Dateien des tatsächlich gewählten Ordners ergänzt.
            ReconcileHtmlItemsWithFolder(project, folder);
            return project;
        }

        var scanned = new OverlayProjectDefinition { Name = new DirectoryInfo(folder).Name, Version = "1.0", Source = "Ordner", ManifestPath = localManifestPath };
        AddScannedHtml(scanned, folder);
        return scanned;
    }

    private static void AddScannedHtml(OverlayProjectDefinition project, string folder)
    {
        foreach (var file in EnumerateHtmlFiles(folder))
        {
            var rel = Path.GetRelativePath(folder, file);
            var parent = Path.GetFileName(Path.GetDirectoryName(file));
            project.Items.Add(CreateScannedHtmlItem(rel, parent));
        }
    }

    private static void ReconcileHtmlItemsWithFolder(OverlayProjectDefinition project, string folder)
    {
        var htmlFiles = EnumerateHtmlFiles(folder)
            .Select(file => Path.GetRelativePath(folder, file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var htmlSet = new HashSet<string>(htmlFiles.Select(NormalizeRelativePath), StringComparer.OrdinalIgnoreCase);

        // Entferne nur lokale Browser-Einträge, deren Datei im ausgewählten Ordner
        // nicht existiert. Web-URLs und andere Assets bleiben unangetastet.
        project.Items.RemoveAll(item =>
            item.IsLocalFile &&
            string.Equals(item.SourceType, "browser", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.RelativePath) &&
            !htmlSet.Contains(NormalizeRelativePath(item.RelativePath)));

        var existing = new HashSet<string>(
            project.Items
                .Where(item => item.IsLocalFile && string.Equals(item.SourceType, "browser", StringComparison.OrdinalIgnoreCase))
                .Select(item => NormalizeRelativePath(item.RelativePath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in htmlFiles)
        {
            var normalized = NormalizeRelativePath(relativePath);
            if (existing.Contains(normalized)) continue;

            var parent = Path.GetFileName(Path.GetDirectoryName(Path.Combine(folder, relativePath)));
            project.Items.Add(CreateScannedHtmlItem(relativePath, parent));
            existing.Add(normalized);
        }
    }

    private static OverlayProjectItem CreateScannedHtmlItem(string relativePath, string? parent)
    {
        return new OverlayProjectItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Path.GetFileNameWithoutExtension(relativePath),
            Kind = GuessKind(relativePath, parent),
            RelativePath = relativePath,
            SourceType = "browser",
            IsLocalFile = true,
            Enabled = true
        };
    }

    private static IEnumerable<string> EnumerateHtmlFiles(string folder)
    {
        return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetExtension(file), ".html", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetExtension(file), ".htm", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRelativePath(string? path)
    {
        return (path ?? string.Empty)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }

    private static string? GetPreferredManifestInsideFolder(string folder, string? preferredManifestPath)
    {
        if (string.IsNullOrWhiteSpace(preferredManifestPath) || !File.Exists(preferredManifestPath)) return null;

        var fullFolder = Path.GetFullPath(folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullManifest = Path.GetFullPath(preferredManifestPath);
        return fullManifest.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase) ? fullManifest : null;
    }

    private static string GuessKind(string? path, string? scene)
    {
        var value = ((path ?? "") + " " + (scene ?? "")).ToLowerInvariant();
        return value.Contains("scene") || value.Contains("start") || value.Contains("pause") || value.Contains("ende") || value.Contains("end") || value.Contains("game") || value.Contains("reaction") || value.Contains("meta") ? "Scene" : "Module";
    }

    private static string ResolvePath(OverlayProjectDefinition project, OverlayProjectItem item)
    {
        if (string.IsNullOrWhiteSpace(item.RelativePath)) return "";
        if (IsWebUrl(item.RelativePath) || Path.IsPathRooted(item.RelativePath)) return item.RelativePath;
        return Path.GetFullPath(Path.Combine(project.RootPath, item.RelativePath));
    }

    private static string BuildSourceName(OverlayProjectDefinition project, OverlayProjectItem item)
    {
        static string Safe(string value) => new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return $"ccs_{Safe(project.Name)}_{Safe(item.Name)}";
    }



    public async Task<string> EnsureCentralDataReferenceAsync(OverlayProjectDefinition project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
            throw new DirectoryNotFoundException("Der Overlay-Projektordner wurde nicht gefunden.");

        var centralPath = Path.GetFullPath(await _overlayData.GetDataFilePathAsync(ct));
        await _overlayData.WriteAsync(ct);

        var projectRoot = Path.GetFullPath(project.RootPath);
        var denverUiDirectory = Path.Combine(projectRoot, "Overlay", "modules", "ui");
        var usesDenverUiLayout = Directory.Exists(denverUiDirectory) &&
            (File.Exists(Path.Combine(denverUiDirectory, "spotify.html")) ||
             File.Exists(Path.Combine(denverUiDirectory, "live-status.html")));

        // DenverJohn v18.x lädt aus Overlay/modules/ui per ../../data/... und
        // erwartet die Datei deshalb unter Overlay/data – nicht unter Root/data.
        var dataDirectory = usesDenverUiLayout
            ? Path.Combine(projectRoot, "Overlay", "data")
            : Path.Combine(projectRoot, "data");
        Directory.CreateDirectory(dataDirectory);
        var projectPath = Path.Combine(dataDirectory, Path.GetFileName(centralPath));

        if (PathsReferToSameFile(projectPath, centralPath))
        {
            project.DataSourcePath = centralPath;
            project.DataReferenceMode = DetectReferenceMode(projectPath);
            project.DataReferenceStatus = $"Zentrale Datenquelle verbunden ({project.DataReferenceMode})";
            return projectPath;
        }

        if (File.Exists(projectPath))
        {
            var backup = projectPath + ".legacy-copy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(projectPath, backup, true);
        }

        Exception? hardLinkError = null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (CreateHardLink(projectPath, centralPath, IntPtr.Zero))
                {
                    project.DataSourcePath = centralPath;
                    project.DataReferenceMode = "Hardlink";
                    project.DataReferenceStatus = "Zentrale Datenquelle verbunden (Hardlink)";
                    return projectPath;
                }
                hardLinkError = new IOException($"Hardlink-Fehler {Marshal.GetLastWin32Error()}");
            }
            catch (Exception exception)
            {
                hardLinkError = exception;
            }
        }

        try
        {
            File.CreateSymbolicLink(projectPath, centralPath);
            project.DataSourcePath = centralPath;
            project.DataReferenceMode = "Symbolischer Link";
            project.DataReferenceStatus = "Zentrale Datenquelle verbunden (symbolischer Link)";
            return projectPath;
        }
        catch (Exception symbolicLinkError)
        {
            // Auch ohne Hardlink- oder Symlink-Rechte muss ein neu importiertes
            // Overlay sofort eine gültige Datenquelle besitzen. In diesem Fall
            // wird eine lokale overlay-data.json im data-Ordner des Overlays erzeugt.
            // Dadurch scheitert der Import nicht mehr an fehlenden Windows-Rechten.
            try
            {
                File.Copy(centralPath, projectPath, true);
                project.DataSourcePath = projectPath;
                project.DataReferenceMode = "Lokale Datei";
                project.DataReferenceStatus = "overlay-data.json im Overlay-Verzeichnis neu erzeugt";
                _logger.Write(AppLogLevel.Warning, "Overlay",
                    "Dateiverweis nicht möglich; lokale overlay-data.json wurde erzeugt.",
                    new AggregateException(hardLinkError ?? symbolicLinkError, symbolicLinkError));
                return projectPath;
            }
            catch (Exception copyError)
            {
                project.DataSourcePath = centralPath;
                project.DataReferenceMode = "Nicht verbunden";
                project.DataReferenceStatus = "overlay-data.json konnte nicht erzeugt werden";
                throw new IOException(
                    "Im Overlay-Verzeichnis konnte keine overlay-data.json erzeugt werden.",
                    new AggregateException(hardLinkError ?? symbolicLinkError, symbolicLinkError, copyError));
            }
        }
    }

    private static bool PathsReferToSameFile(string projectPath, string centralPath)
    {
        if (!File.Exists(projectPath) || !File.Exists(centralPath)) return false;
        try
        {
            var target = File.ResolveLinkTarget(projectPath, true);
            if (target is not null)
                return string.Equals(Path.GetFullPath(target.FullName), centralPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Bei Hardlinks liefert ResolveLinkTarget keinen Wert.
        }

        if (!OperatingSystem.IsWindows()) return false;
        return GetWindowsFileIdentity(projectPath) is { } left &&
               GetWindowsFileIdentity(centralPath) is { } right &&
               left == right;
    }

    private static string DetectReferenceMode(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, false) is null ? "Hardlink" : "Symbolischer Link";
        }
        catch
        {
            return "Dateiverweis";
        }
    }

    private static (uint Volume, ulong Index)? GetWindowsFileIdentity(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(stream.SafeFileHandle.DangerousGetHandle(), out var info)) return null;
        var index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        return (info.VolumeSerialNumber, index);
    }

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("Kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(IntPtr file, out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private static string GetSourceType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".html" or ".htm") return "browser";
        if (extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg") return "image";
        if (extension is ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" or ".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac") return "media";
        return "asset";
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Neue Szene" : safe;
    }

    private static string GetUniqueDestination(string folder, string fileName)
    {
        var destination = Path.Combine(folder, fileName);
        if (!File.Exists(destination)) return destination;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            destination = Path.Combine(folder, $"{name}-{index}{extension}");
            if (!File.Exists(destination)) return destination;
        }
    }

    private static bool IsWebUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    private static string GetString(IReadOnlyDictionary<string, JsonElement> values, string key) => values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

public sealed class OverlayProjectDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Overlay-Projekt";
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string Source { get; set; } = "Ordner";
    public string Status { get; set; } = "Bereit";
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset LastSynchronizedAt { get; set; }
    public string DataSourcePath { get; set; } = "";
    public string DataReferenceMode { get; set; } = "";
    public string DataReferenceStatus { get; set; } = "Datenquelle noch nicht geprüft";
    public List<OverlayProjectItem> Items { get; set; } = [];
    public override string ToString() => $"{Name} · {Items.Count} Elemente · {Status} · {DataReferenceStatus}";
}

public sealed class OverlayProjectItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Overlay";
    public string Kind { get; set; } = "Module";
    public string RelativePath { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string ObsScene { get; set; } = "";
    public string ObsSource { get; set; } = "";
    public bool IsLocalFile { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string Status { get; set; } = "Bereit";
    public override string ToString() => $"{(Enabled ? "●" : "○")} {Kind}: {Name} → {(string.IsNullOrWhiteSpace(ObsScene) ? "keine OBS-Szene" : ObsScene)} · {Status}";
}

public sealed class OverlayManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string? Author { get; set; }
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string? DataSourcePath { get; set; }
    public string? DataReferenceMode { get; set; }
    public List<OverlayProjectItem>? Items { get; set; }
}
