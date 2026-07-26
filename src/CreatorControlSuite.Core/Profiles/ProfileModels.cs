using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Profiles;

public sealed class CreatorProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neues Profil";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public AppSettings Settings { get; set; } = new();
}

public sealed record ProfileSummary(
    string Id,
    string Name,
    string Description,
    DateTimeOffset UpdatedAt);
