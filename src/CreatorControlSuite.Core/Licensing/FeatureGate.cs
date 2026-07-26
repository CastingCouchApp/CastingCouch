namespace CreatorControlSuite.Core.Licensing;
public sealed class FeatureGate : IFeatureGate
{
 private readonly ILicenseService _licenses;
 public FeatureGate(ILicenseService licenses)=>_licenses=licenses;
 public async Task<bool> IsEnabledAsync(string feature,CancellationToken ct=default)
 {
  var s=await _licenses.GetStatusAsync(ct); if(!s.IsUsable)return false;
  if(s.EnabledFeatures.Contains("*",StringComparer.OrdinalIgnoreCase)||s.EnabledFeatures.Contains(feature,StringComparer.OrdinalIgnoreCase))return true;
  return s.License is not null && FeatureCatalog.ResolveEdition(s.License.Edition).Contains(feature,StringComparer.OrdinalIgnoreCase);
 }
 public async Task RequireAsync(string feature,CancellationToken ct=default){if(!await IsEnabledAsync(feature,ct))throw new InvalidOperationException($"Die Funktion „{feature}“ ist in der aktuellen Lizenz nicht freigeschaltet.");}
 public async Task<IReadOnlyDictionary<string,bool>> SnapshotAsync(CancellationToken ct=default)
 {
  var names=FeatureCatalog.Editions.Values.SelectMany(x=>x).Distinct(StringComparer.OrdinalIgnoreCase);var r=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
  foreach(var n in names)r[n]=await IsEnabledAsync(n,ct); return r;
 }
}
