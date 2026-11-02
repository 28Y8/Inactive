using System.Security.Cryptography;

namespace Inactive.Core.Security;

/// <summary>Hashes and verifies PINs with PBKDF2-SHA256.</summary>
public static class PinHasher
{
    public const int MinLength = 4;
    public const int MaxLength = 8;
    public const int DefaultIterations = 200_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // Keeps a hand edited settings file from stalling verification for minutes.
    private const int MaxIterations = 10_000_000;

    /// <summary>True when the text is 4 to 8 ASCII digits.</summary>
    public static bool IsValidPin(ReadOnlySpan<char> pin)
    {
        if (pin.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        foreach (var c in pin)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <exception cref="ArgumentException">The PIN is not 4 to 8 digits.</exception>
    public static PinCredential Create(ReadOnlySpan<char> pin) => Create(pin, DefaultIterations);

    internal static PinCredential Create(ReadOnlySpan<char> pin, int iterations)
    {
        if (!IsValidPin(pin))
        {
            throw new ArgumentException("A PIN must be 4 to 8 digits.", nameof(pin));
        }

        Span<byte> salt = stackalloc byte[SaltBytes];
        Span<byte> hash = stackalloc byte[HashBytes];
        RandomNumberGenerator.Fill(salt);
        Derive(pin, salt, iterations, hash);

        return new PinCredential(Convert.ToBase64String(hash), Convert.ToBase64String(salt), iterations, pin.Length);
    }

    /// <summary>Checks a PIN in constant time. A malformed credential never matches.</summary>
    public static bool Verify(ReadOnlySpan<char> pin, PinCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        Span<byte> salt = stackalloc byte[SaltBytes];
        Span<byte> expected = stackalloc byte[HashBytes];
        Span<byte> actual = stackalloc byte[HashBytes];

        if (!IsValidPin(pin) || !TryDecode(credential, salt, expected))
        {
            return false;
        }

        Derive(pin, salt, credential.Iterations, actual);
        var matches = CryptographicOperations.FixedTimeEquals(actual, expected);
        CryptographicOperations.ZeroMemory(actual);
        return matches;
    }

    /// <summary>True when a credential is well formed enough to be verified at all.</summary>
    public static bool IsUsable(PinCredential? credential)
    {
        Span<byte> salt = stackalloc byte[SaltBytes];
        Span<byte> hash = stackalloc byte[HashBytes];
        return credential is not null && TryDecode(credential, salt, hash);
    }

    private static bool TryDecode(PinCredential credential, Span<byte> salt, Span<byte> hash) =>
        credential.Iterations is > 0 and <= MaxIterations
        && credential.Length is >= MinLength and <= MaxLength
        && Convert.TryFromBase64String(credential.Salt ?? string.Empty, salt, out var saltLength)
        && saltLength == SaltBytes
        && Convert.TryFromBase64String(credential.Hash ?? string.Empty, hash, out var hashLength)
        && hashLength == HashBytes;

    private static void Derive(ReadOnlySpan<char> pin, ReadOnlySpan<byte> salt, int iterations, Span<byte> destination)
    {
        Span<byte> password = stackalloc byte[MaxLength];
        password = password[..pin.Length];

        for (var i = 0; i < pin.Length; i++)
        {
            password[i] = (byte)pin[i];
        }

        Rfc2898DeriveBytes.Pbkdf2(password, salt, destination, iterations, HashAlgorithmName.SHA256);
        CryptographicOperations.ZeroMemory(password);
    }
}
