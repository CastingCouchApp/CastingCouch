namespace CreatorControlSuite.Core.Diagnostics;
public sealed class BetaReadinessService : IBetaReadinessService
{
    private readonly IReleaseReadinessService _release; private readonly IInstallerSelfTestService _installer; private readonly RuntimeHealthService _runtime;
    public BetaReadinessService(IReleaseReadinessService release,IInstallerSelfTestService installer,RuntimeHealthService runtime)
    { _release=release;_installer=installer;_runtime=runtime; }
    public async Task<BetaReadinessDashboard> BuildAsync(CancellationToken ct=default)
    {
        var release=await _release.CheckAsync(ct);var installer=await _installer.RunAsync(ct);var runtime=await _runtime.CheckAsync(ct);
        var areas=new List<BetaReadinessArea>{
            Build("Release",release.Items.Select(x=>x.Blocking?"Failed":x.Status is "OK" or "Development" or "Active"?"Passed":"Warning"),$"{release.Items.Count} Release-Prüfpunkte."),
            Build("Installation",installer.Items.Select(x=>x.Status switch{InstallerSelfTestStatus.Passed=>"Passed",InstallerSelfTestStatus.Warning=>"Warning",_=>"Failed"}),$"{installer.Items.Count} Installer-Prüfpunkte."),
            Build("Laufzeit",runtime.Select(x=>string.Equals(x.Status,"Error",StringComparison.OrdinalIgnoreCase)?"Failed":string.Equals(x.Status,"Warning",StringComparison.OrdinalIgnoreCase)?"Warning":"Passed"),$"{runtime.Count} Laufzeit-Prüfpunkte.")
        };
        var blockers=release.Items.Where(x=>x.Blocking).Select(x=>x.Area+": "+x.Detail)
            .Concat(installer.Items.Where(x=>x.Status==InstallerSelfTestStatus.Failed).Select(x=>x.Check+": "+x.Detail)).Distinct().ToList();
        var overall=areas.Count==0?0:(int)Math.Round(areas.Average(x=>x.ScorePercent));
        return new(DateTimeOffset.Now,overall,blockers.Count==0 && overall>=85,areas,blockers);
    }
    private static BetaReadinessArea Build(string area,IEnumerable<string> states,string detail)
    {
        var l=states.ToList();var p=l.Count(x=>x=="Passed");var w=l.Count(x=>x=="Warning");var f=l.Count(x=>x=="Failed");
        return new(area,p,w,f,(int)Math.Round((p*100+w*60)/(double)Math.Max(1,l.Count)),detail);
    }
}
