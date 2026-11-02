namespace Inactive.Core.Security;

/// <summary>A stored PIN: PBKDF2 hash and salt in Base64, the iteration count and the digit count.</summary>
public sealed record PinCredential(string Hash, string Salt, int Iterations, int Length)
{
    public override string ToString() => nameof(PinCredential);
}
