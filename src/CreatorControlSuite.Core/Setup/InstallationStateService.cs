using System.Text.Json;
namespace CreatorControlSuite.Core.Setup;
public sealed class InstallationStateService : IInstallationStateService
{
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    private readonly string _path; public InstallationStateService(string statePath){_path=statePath;}
    public async Task<InstallationTransition> RegisterStartAsync(string currentVersion,CancellationToken ct=default)
    {
        var s=await LoadAsync(ct);var first=string.IsNullOrWhiteSpace(s.InstalledVersion);
        var upgrade=!first&&!string.Equals(s.InstalledVersion,currentVersion,StringComparison.OrdinalIgnoreCase);var previous=s.InstalledVersion;
        if(first)s.InstalledAt=DateTimeOffset.Now;s.PreviousVersion=upgrade?s.InstalledVersion:s.PreviousVersion;
        s.InstalledVersion=currentVersion;s.LastStartedAt=DateTimeOffset.Now;s.StartCount++;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);var tmp=_path+".tmp";
        await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(s,Options),ct);File.Move(tmp,_path,true);
        return new(first,upgrade,previous,currentVersion);
    }
    public async Task<InstallationState> LoadAsync(CancellationToken ct=default)
    {
        if(!File.Exists(_path))return new();await using var stream=File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<InstallationState>(stream,Options,ct)??new();
    }
}
