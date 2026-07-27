using System.Security.Cryptography;
using System.Text;

namespace CreatorControlSuite.Core.Security;

public sealed class WindowsDpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public WindowsDpapiSecretStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        byte[] protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(
            GetPath(key),
            protectedBytes,
            cancellationToken);
    }

    public async Task<string?> LoadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(key);

        if (!File.Exists(path))
        {
            return null;
        }

        byte[] encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[] plain = ProtectedData.Unprotect(
            encrypted,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(plain);
    }

    public Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(key);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string key)
    {
        string safeName = string.Concat(
            key.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character)
                    ? '_'
                    : character));

        return Path.Combine(_directory, safeName + ".secret");
    }
}
