using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CreatorControlSuite.Agent.Security;
using CreatorControlSuite.Core.Updates;
using static AgentUtilities;

internal sealed record UpdateEndpointDependencies(
    Func<HttpRequest, bool> Authorized,
    AgentPermissions Permissions,
    Func<AgentSettings> Settings,
    IUpdateSignatureVerifier SignatureVerifier,
    string AgentVersion,
    string DataDirectory,
    string UpdateStatePath,
    string MaintenancePath,
    string UpdateHistoryPath,
    Action<string> Log);

internal static class UpdateEndpointMappings
{
    internal static void MapUpdateEndpoints(
        this WebApplication app,
        UpdateEndpointDependencies dependencies)
    {
        app.MapPost("/api/v1/update/stage", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("updates.stage", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("updates.stage");
            }

            FileDeployRequest? payload = await JsonSerializer.DeserializeAsync<FileDeployRequest>(request.Body);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.Base64Zip) ||
                payload.Manifest is null)
            {
                return AgentApiResults.BadRequest("Update-Daten fehlen");
            }

            try
            {
                string target = string.IsNullOrWhiteSpace(dependencies.Settings().UpdateStagingDirectory)
                    ? Path.Combine(dependencies.DataDirectory, "Updates", DateTime.Now.ToString("yyyyMMdd-HHmmss"))
                    : Path.Combine(Path.GetFullPath(dependencies.Settings().UpdateStagingDirectory), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(target);
                string zipPath = Path.Combine(target, Path.GetFileName(payload.FileName ?? "update.zip"));
                await File.WriteAllBytesAsync(zipPath, Convert.FromBase64String(payload.Base64Zip));
                if (!string.Equals(
                        payload.Manifest.ProductId,
                        UpdateManifestCanonical.ProductId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        payload.Manifest.PackageFileName,
                        Path.GetFileName(zipPath),
                        StringComparison.OrdinalIgnoreCase) ||
                    !dependencies.SignatureVerifier.VerifyManifest(payload.Manifest) ||
                    !await dependencies.SignatureVerifier.VerifyPackageAsync(
                        zipPath,
                        payload.Manifest,
                        request.HttpContext.RequestAborted))
                {
                    File.Delete(zipPath);
                    return AgentApiResults.BadRequest(
                        "Update-Manifest, Signatur oder Paket-Prüfsumme ist ungültig.");
                }

                string signedManifestPath = Path.Combine(target, "update-manifest.json");
                await File.WriteAllTextAsync(
                    signedManifestPath,
                    JsonSerializer.Serialize(payload.Manifest),
                    request.HttpContext.RequestAborted);
                string packageDirectory = Path.Combine(target, "package");
                SafeZipExtractor.ExtractToDirectory(zipPath, packageDirectory);
                string checksum = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath)));
                string[] files = [.. Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories)];
                int fileCount = files.Length;
                string packageVersion = payload.Manifest.Version;
                var state = new AgentUpdateState("staged", payload.FileName ?? "update.zip", target, packageDirectory, "", DateTimeOffset.Now, null, "Update wurde mit Release-Signatur geprüft und bereitgestellt.", checksum, fileCount, false, false, null, packageVersion, payload.Manifest.MinimumVersion, payload.Manifest.Signature, true);
                SaveUpdateState(dependencies.UpdateStatePath, state);
                AppendUpdateHistory(dependencies.UpdateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "stage", packageVersion, checksum, true, "Update bereitgestellt"));
                dependencies.Log($"Update-Paket '{payload.FileName}' in '{target}' bereitgestellt.");
                return Results.Ok(new { staged = true, target, packageDirectory, restartRequired = true });
            }
            catch (Exception ex) { dependencies.Log("Update-Bereitstellung fehlgeschlagen: " + ex.Message); return AgentApiResults.InternalError(ex); }
        });

        app.MapGet("/api/v1/update/status", (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            return Results.Ok(LoadUpdateState(dependencies.UpdateStatePath));
        });

        app.MapGet("/api/v1/update/history", (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            return Results.Ok(LoadUpdateHistory(dependencies.UpdateHistoryPath).OrderByDescending(entry => entry.At).Take(100));
        });

        app.MapPost("/api/v1/update/validate", (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("updates.apply");
            }

            AgentUpdateState state = LoadUpdateState(dependencies.UpdateStatePath);
            if (!Directory.Exists(state.PackageDirectory))
            {
                return AgentApiResults.BadRequest("Kein Update-Paket vorhanden.");
            }

            string[] files = [.. Directory.EnumerateFiles(state.PackageDirectory, "*", SearchOption.AllDirectories)];
            bool hasExecutable = files.Any(path => path.EndsWith("CreatorControlSuite.App.exe", StringComparison.OrdinalIgnoreCase));
            string manifestPath = Path.Combine(state.StagingDirectory, "update-manifest.json");
            SignedUpdateManifest? manifest = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<SignedUpdateManifest>(File.ReadAllText(manifestPath))
                : null;
            string archivePath = Path.Combine(state.StagingDirectory, state.PackageName);
            bool signatureValid = manifest is not null &&
                dependencies.SignatureVerifier.VerifyManifest(manifest) &&
                dependencies.SignatureVerifier.VerifyPackageAsync(
                    archivePath,
                    manifest).GetAwaiter().GetResult();
            bool compatible = IsCompatibleVersion(dependencies.AgentVersion, state.MinimumAgentVersion);
            bool valid = files.Length > 0 && hasExecutable && signatureValid && compatible;
            string message = valid
                ? $"Paket geprüft: {files.Length} Dateien, Version {state.PackageVersion}, Manifest-Signatur gültig und Agent kompatibel."
                : $"Paketprüfung fehlgeschlagen: Programm={hasExecutable}, Signatur={signatureValid}, kompatibel={compatible}.";
            AgentUpdateState updated = state with { Status = valid ? "validated" : "invalid", FileCount = files.Length, Validated = valid, Message = message, SignatureValid = signatureValid };
            SaveUpdateState(dependencies.UpdateStatePath, updated);
            AppendUpdateHistory(dependencies.UpdateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "validate", state.PackageVersion, state.Sha256, valid, message));
            dependencies.Log(message);
            return valid ? Results.Ok(updated) : AgentApiResults.BadRequest(message);
        });

        app.MapPost("/api/v1/update/apply", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("updates.apply");
            }

            UpdateApplyRequest payload = await JsonSerializer.DeserializeAsync<UpdateApplyRequest>(request.Body) ?? new UpdateApplyRequest(false, true);
            AgentUpdateState state = LoadUpdateState(dependencies.UpdateStatePath);
            if (!(string.Equals(state.Status, "staged", StringComparison.OrdinalIgnoreCase) || string.Equals(state.Status, "validated", StringComparison.OrdinalIgnoreCase)) || !Directory.Exists(state.PackageDirectory))
            {
                return AgentApiResults.BadRequest("Es ist kein anwendbares Update bereitgestellt.");
            }

            try
            {
                string installDirectory = string.IsNullOrWhiteSpace(dependencies.Settings().SuiteInstallDirectory) ? AppContext.BaseDirectory : Path.GetFullPath(dependencies.Settings().SuiteInstallDirectory);
                string executable = string.IsNullOrWhiteSpace(dependencies.Settings().SuiteExecutablePath) ? Path.Combine(installDirectory, "CreatorControlSuite.App.exe") : Path.GetFullPath(dependencies.Settings().SuiteExecutablePath);
                string backupDirectory = Path.Combine(dependencies.DataDirectory, "update-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(Path.GetDirectoryName(backupDirectory)!);
                CopyDirectory(installDirectory, backupDirectory, path => !path.Contains(Path.Combine("Agent", "Updates"), StringComparison.OrdinalIgnoreCase));
                string scriptPath = Path.Combine(dependencies.DataDirectory, "apply-update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".cmd");
                string processName = Path.GetFileNameWithoutExtension(executable);
                string restartLine = payload.RestartSuite ? $"if exist \"{executable}\" start \"\" \"{executable}\"" : "rem Suite-Neustart nicht angefordert";
                string resultPath = Path.Combine(dependencies.DataDirectory, "last-update-result.txt");
                string healthBlock = payload.RestartSuite && payload.AutomaticRollback
                    ? $"timeout /t 15 /nobreak >nul\r\ntasklist /FI \"IMAGENAME eq {processName}.exe\" | find /I \"{processName}.exe\" >nul\r\nif errorlevel 1 (\r\n  robocopy \"{backupDirectory}\" \"{installDirectory}\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS\r\n  if exist \"{executable}\" start \"\" \"{executable}\"\r\n  echo automatic-rollback>\"{resultPath}\"\r\n) else (echo healthy>\"{resultPath}\")"
                    : $"echo applied>\"{resultPath}\"";
                File.WriteAllText(dependencies.MaintenancePath, DateTimeOffset.Now.ToString("O"));
                string script = $"@echo off\r\ntimeout /t 3 /nobreak >nul\r\nrobocopy \"{state.PackageDirectory}\" \"{installDirectory}\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS\r\n{restartLine}\r\n{healthBlock}\r\ndel /q \"{dependencies.MaintenancePath}\" 2>nul\r\n";
                await File.WriteAllTextAsync(scriptPath, script);
                SaveUpdateState(dependencies.UpdateStatePath, state with { Status = "applying", BackupDirectory = backupDirectory, AppliedAt = DateTimeOffset.Now, Message = "Update wird im Wartungsmodus angewendet; anschließend folgt der Health-Check.", MaintenanceMode = true, AutomaticRollback = payload.AutomaticRollback });
                AppendUpdateHistory(dependencies.UpdateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "apply", state.PackageVersion, state.Sha256, true, "Update-Anwendung gestartet"));
                dependencies.Log($"Update-Anwendung vorbereitet. Backup: '{backupDirectory}'.");
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"CCS Update\" /min \"{scriptPath}\"") { UseShellExecute = false, CreateNoWindow = true });
                _ = Task.Run(async () => { await Task.Delay(750); Environment.Exit(0); });
                return Results.Accepted(value: new { applying = true, backupDirectory, agentRestartRequired = true });
            }
            catch (Exception ex) { dependencies.Log("Update-Anwendung fehlgeschlagen: " + ex.Message); return AgentApiResults.InternalError(ex); }
        });

        app.MapPost("/api/v1/update/rollback", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("updates.apply");
            }

            AgentUpdateState state = LoadUpdateState(dependencies.UpdateStatePath);
            if (string.IsNullOrWhiteSpace(state.BackupDirectory) || !Directory.Exists(state.BackupDirectory))
            {
                return AgentApiResults.BadRequest("Kein Rollback-Backup verfügbar.");
            }

            try
            {
                string installDirectory = string.IsNullOrWhiteSpace(dependencies.Settings().SuiteInstallDirectory) ? AppContext.BaseDirectory : Path.GetFullPath(dependencies.Settings().SuiteInstallDirectory);
                string executable = string.IsNullOrWhiteSpace(dependencies.Settings().SuiteExecutablePath) ? Path.Combine(installDirectory, "CreatorControlSuite.App.exe") : Path.GetFullPath(dependencies.Settings().SuiteExecutablePath);
                string scriptPath = Path.Combine(dependencies.DataDirectory, "rollback-update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".cmd");
                string script = $"@echo off\r\ntimeout /t 3 /nobreak >nul\r\nrobocopy \"{state.BackupDirectory}\" \"{installDirectory}\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS\r\nif exist \"{executable}\" start \"\" \"{executable}\"\r\n";
                await File.WriteAllTextAsync(scriptPath, script);
                SaveUpdateState(dependencies.UpdateStatePath, state with { Status = "rolling-back", AppliedAt = DateTimeOffset.Now, Message = "Rollback wird angewendet." });
                AppendUpdateHistory(dependencies.UpdateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "rollback", state.PackageVersion, state.Sha256, true, "Rollback gestartet"));
                dependencies.Log($"Rollback aus '{state.BackupDirectory}' gestartet.");
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"CCS Rollback\" /min \"{scriptPath}\"") { UseShellExecute = false, CreateNoWindow = true });
                _ = Task.Run(async () => { await Task.Delay(750); Environment.Exit(0); });
                return Results.Accepted(value: new { rollingBack = true, agentRestartRequired = true });
            }
            catch (Exception ex) { dependencies.Log("Rollback fehlgeschlagen: " + ex.Message); return AgentApiResults.InternalError(ex); }
        });
    }
}
