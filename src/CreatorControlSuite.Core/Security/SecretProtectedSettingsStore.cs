using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Security;

public sealed class SecretProtectedSettingsStore : ISettingsStore
{
    public const string StreamerBotPasswordKey = "streamerbot.password";

    private readonly ISettingsStore _inner;
    private readonly ISecretStore _secrets;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SecretProtectedSettingsStore(
        ISettingsStore inner,
        ISecretStore secrets)
    {
        _inner = inner;
        _secrets = secrets;
    }

    public async Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            AppSettings settings = await _inner.LoadAsync(cancellationToken);
            string legacyPassword = settings.StreamerBot.Password;
            string? protectedPassword = await _secrets.LoadAsync(
                StreamerBotPasswordKey,
                cancellationToken);

            if (!string.IsNullOrEmpty(legacyPassword))
            {
                protectedPassword = legacyPassword;
                await _secrets.SaveAsync(
                    StreamerBotPasswordKey,
                    legacyPassword,
                    cancellationToken);
                await SaveSanitizedAsync(settings, cancellationToken);
            }

            settings.StreamerBot.Password = protectedPassword ?? "";
            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string password = settings.StreamerBot.Password;
            if (string.IsNullOrEmpty(password))
            {
                await _secrets.DeleteAsync(
                    StreamerBotPasswordKey,
                    cancellationToken);
            }
            else
            {
                await _secrets.SaveAsync(
                    StreamerBotPasswordKey,
                    password,
                    cancellationToken);
            }

            await SaveSanitizedAsync(settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveSanitizedAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        string password = settings.StreamerBot.Password;
        try
        {
            settings.StreamerBot.Password = "";
            await _inner.SaveAsync(settings, cancellationToken);
        }
        finally
        {
            settings.StreamerBot.Password = password;
        }
    }
}
