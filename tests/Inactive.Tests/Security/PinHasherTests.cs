using Inactive.Core.Security;

namespace Inactive.Tests.Security;

public sealed class PinHasherTests
{
    private const int FastIterations = 1_000;

    [Theory]
    [InlineData("1234")]
    [InlineData("12345678")]
    [InlineData("0000")]
    public void Accepts_four_to_eight_digits(string pin) => Assert.True(PinHasher.IsValidPin(pin));

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("12a4")]
    [InlineData("12 4")]
    [InlineData("١٢٣٤")]
    public void Rejects_anything_else(string pin) => Assert.False(PinHasher.IsValidPin(pin));

    [Theory]
    [InlineData("123")]
    [InlineData("abcd")]
    public void Create_refuses_an_invalid_pin(string pin) =>
        Assert.Throws<ArgumentException>(() => PinHasher.Create(pin, FastIterations));

    [Fact]
    public void Default_uses_200000_iterations_and_a_16_byte_salt()
    {
        var credential = PinHasher.Create("1234");

        Assert.Equal(200_000, credential.Iterations);
        Assert.Equal(16, Convert.FromBase64String(credential.Salt).Length);
        Assert.Equal(32, Convert.FromBase64String(credential.Hash).Length);
        Assert.Equal(4, credential.Length);
        Assert.True(PinHasher.Verify("1234", credential));
    }

    [Fact]
    public void Same_pin_gets_a_new_salt_and_hash_each_time()
    {
        var first = PinHasher.Create("1234", FastIterations);
        var second = PinHasher.Create("1234", FastIterations);

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Matches_a_known_pbkdf2_sha256_vector()
    {
        // Computed independently with Rfc2898DeriveBytes.Pbkdf2 over ASCII "1234".
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "1234"u8, salt, FastIterations, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);

        var credential = new PinCredential(Convert.ToBase64String(hash), Convert.ToBase64String(salt), FastIterations, 4);

        Assert.True(PinHasher.Verify("1234", credential));
    }

    [Theory]
    [InlineData("1235")]
    [InlineData("12345")]
    [InlineData("0000")]
    public void Wrong_pin_does_not_verify(string pin)
    {
        var credential = PinHasher.Create("1234", FastIterations);

        Assert.False(PinHasher.Verify(pin, credential));
    }

    [Fact]
    public void Malformed_pin_does_not_verify()
    {
        var credential = PinHasher.Create("1234", FastIterations);

        Assert.False(PinHasher.Verify("12", credential));
        Assert.False(PinHasher.Verify("abcd", credential));
    }

    [Fact]
    public void Verify_rejects_a_null_credential() =>
        Assert.Throws<ArgumentNullException>(() => PinHasher.Verify("1234", null!));

    [Fact]
    public void A_good_credential_is_usable() =>
        Assert.True(PinHasher.IsUsable(PinHasher.Create("1234", FastIterations)));

    [Fact]
    public void Null_is_not_usable() => Assert.False(PinHasher.IsUsable(null));

    public static TheoryData<PinCredential> DamagedCredentials()
    {
        var good = PinHasher.Create("1234", FastIterations);
        return new TheoryData<PinCredential>(
            good with { Hash = "not base64!" },
            good with { Salt = "not base64!" },
            good with { Hash = string.Empty },
            good with { Salt = string.Empty },
            good with { Hash = Convert.ToBase64String(new byte[31]) },
            good with { Salt = Convert.ToBase64String(new byte[8]) },
            good with { Iterations = 0 },
            good with { Iterations = -1 },
            good with { Iterations = int.MaxValue },
            good with { Length = 3 },
            good with { Length = 9 });
    }

    [Theory]
    [MemberData(nameof(DamagedCredentials))]
    public void Damaged_credentials_are_unusable_and_never_verify(PinCredential credential)
    {
        Assert.False(PinHasher.IsUsable(credential));
        Assert.False(PinHasher.Verify("1234", credential));
    }

    [Fact]
    public void A_credential_never_prints_its_contents()
    {
        var credential = PinHasher.Create("1234", FastIterations);

        var text = credential.ToString();

        Assert.DoesNotContain(credential.Hash, text, StringComparison.Ordinal);
        Assert.DoesNotContain(credential.Salt, text, StringComparison.Ordinal);
    }
}
