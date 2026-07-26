namespace CreatorControlSuite.Core.Licensing;
public enum LicenseState { Unknown, Development, Trial, Active, Expired, Invalid, Revoked }
public sealed record LicenseDocument(string LicenseId,string ProductId,string Edition,string CustomerName,string CustomerEmail,DateTimeOffset IssuedAt,DateTimeOffset? ExpiresAt,IReadOnlyList<string> Features,string Signature);
public sealed record LicenseStatus(LicenseState State,string Detail,LicenseDocument? License,IReadOnlyList<string> EnabledFeatures)
{
    public bool IsUsable => State is LicenseState.Development or LicenseState.Trial or LicenseState.Active;
}
