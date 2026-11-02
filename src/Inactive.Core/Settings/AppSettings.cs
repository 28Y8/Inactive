using System.Text.Json;
using System.Text.Json.Serialization;
using Inactive.Core.Input;
using Inactive.Core.Security;

namespace Inactive.Core.Settings;

/// <summary>How the screen looks while locked.</summary>
[JsonConverter(typeof(ScreenStyleJsonConverter))]
public enum ScreenStyle
{
    Visible,
    Dimmed,
    Covered,
}

/// <summary>
/// User settings. Treat an instance as immutable: change a value with a <c>with</c> expression, then
/// save. The properties have setters because the JSON source generator resets absent init
/// properties to zero, which would lose the defaults below for any field missing from the file.
/// </summary>
public sealed record AppSettings
{
    public int UnlockKey { get; set; } = VirtualKeys.RightShift;

    public string? PinHash { get; set; }

    public string? PinSalt { get; set; }

    public int? PinIterations { get; set; }

    public int? PinLength { get; set; }

    /// <summary>The lock hotkey. Null turns the hotkey off.</summary>
    public HotkeyGesture? LockHotkey { get; set; } = HotkeyGesture.Default;

    public ScreenStyle ScreenStyle { get; set; } = ScreenStyle.Dimmed;

    public bool ShowClock { get; set; } = true;

    public string CustomMessage { get; set; } = string.Empty;

    public bool ShowUnlockHint { get; set; }

    public bool AllowDisplayOff { get; set; }

    public bool StartWithWindows { get; set; }

    public bool AutoUpdate { get; set; } = true;

    public bool HasSeenFirstLockHint { get; set; }

    public bool HasSeenTrayHint { get; set; }

    /// <summary>The stored PIN, or null when no complete PIN is configured.</summary>
    [JsonIgnore]
    public PinCredential? Pin =>
        PinHash is not null && PinSalt is not null && PinIterations is { } iterations && PinLength is { } length
            ? new PinCredential(PinHash, PinSalt, iterations, length)
            : null;

    /// <summary>True when both settings end a lock the same way: the same unlock key and PIN.</summary>
    public bool HasSameUnlock(AppSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return UnlockKey == other.UnlockKey && Pin == other.Pin;
    }

    /// <summary>Returns a copy with the PIN replaced, or removed when <paramref name="pin"/> is null.</summary>
    public AppSettings WithPin(PinCredential? pin) => this with
    {
        PinHash = pin?.Hash,
        PinSalt = pin?.Salt,
        PinIterations = pin?.Iterations,
        PinLength = pin?.Length,
    };
}

/// <summary>Writes the style as text. A style from a newer version reads as the default.</summary>
internal sealed class ScreenStyleJsonConverter : JsonConverter<ScreenStyle>
{
    public override ScreenStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<ScreenStyle>(reader.GetString(), ignoreCase: true, out var style)
            && Enum.IsDefined(style))
        {
            return style;
        }

        reader.TrySkip();
        return ScreenStyle.Dimmed;
    }

    public override void Write(Utf8JsonWriter writer, ScreenStyle value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
