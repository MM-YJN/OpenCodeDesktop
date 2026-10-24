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
    public const string SupportedVersion = "2.1.4";
    public static bool IsSupported(string version) => version == SupportedVersion;
}

public interface IServerConnectionService
{
    Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken);
}
