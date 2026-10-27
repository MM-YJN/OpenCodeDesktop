namespace OpenCodeDesktop.Core.Connections;

public enum ConnectionOutcome
{
    Ready,
    Cancelled,
    ValidationFailed,
    AuthenticationFailed,
    Refused,
    TimedOut,
    Unavailable,
    InitializationFailed,
    Incompatible,
    HttpFailure,
    TransportFailure,
}

/// <summary>Contains only safe, client-authored presentation text, never a response body or credentials.</summary>
public sealed record ConnectionResult(ConnectionOutcome Outcome, string Message, string? Version = null)
{
    public bool IsReady => Outcome == ConnectionOutcome.Ready;
    public static ConnectionResult Cancelled => new(ConnectionOutcome.Cancelled, "Connection cancelled. You can try again.");
}

public static class ServerVersionPolicy
{
    public const int SupportedMajorVersion = 2;

    /// <summary>Display text for diagnostics. Not an exact version requirement; see <see cref="IsSupported"/>.</summary>
    public const string SupportedVersion = "2.x";

    /// <summary>Accepts any reported server version on the supported major line, including pre-release labels of one.</summary>
    public static bool IsSupported(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        int separator = version.IndexOf('.', StringComparison.Ordinal);
        ReadOnlySpan<char> major = separator >= 0 ? version.AsSpan(0, separator) : version.AsSpan();
        return int.TryParse(major, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) && value == SupportedMajorVersion;
    }
}

public interface IServerConnectionService
{
    Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken);
}
