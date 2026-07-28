using System.Security.Cryptography;

namespace CreatorControlSuite.Agent.Security;

public enum PairingAttemptResult
{
    Accepted,
    InvalidCode,
    Expired,
    Locked,
    Consumed
}

public sealed class PairingSession
{
    private readonly byte[] _code;
    private readonly DateTimeOffset _expiresAt;
    private readonly int _maximumFailedAttempts;
    private readonly Lock _sync = new();
    private int _failedAttempts;
    private bool _consumed;

    public PairingSession(
        string code,
        DateTimeOffset createdAt,
        TimeSpan lifetime,
        int maximumFailedAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        if (maximumFailedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFailedAttempts));
        }

        _code = System.Text.Encoding.UTF8.GetBytes(code);
        _expiresAt = createdAt.Add(lifetime);
        _maximumFailedAttempts = maximumFailedAttempts;
    }

    public PairingAttemptResult TryConsume(string? suppliedCode, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_consumed)
            {
                return PairingAttemptResult.Consumed;
            }

            if (_failedAttempts >= _maximumFailedAttempts)
            {
                return PairingAttemptResult.Locked;
            }

            if (now > _expiresAt)
            {
                return PairingAttemptResult.Expired;
            }

            byte[] supplied = System.Text.Encoding.UTF8.GetBytes(suppliedCode ?? "");
            if (supplied.Length != _code.Length ||
                !CryptographicOperations.FixedTimeEquals(supplied, _code))
            {
                _failedAttempts++;
                return _failedAttempts >= _maximumFailedAttempts
                    ? PairingAttemptResult.Locked
                    : PairingAttemptResult.InvalidCode;
            }

            _consumed = true;
            return PairingAttemptResult.Accepted;
        }
    }
}
