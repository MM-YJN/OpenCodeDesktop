using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Infrastructure.Connections;

/// <summary>Probes the pinned identity contract; it does not establish project or model readiness.</summary>
public sealed class OpenCodeConnectionService(HttpClient client, TimeSpan? deadline = null) : IServerConnectionService
{
    private const int MaximumResponseBytes = 65536;
    private readonly TimeSpan _deadline = deadline ?? TimeSpan.FromSeconds(5);

    // The composition root owns and disposes this client. Never use global default authorization headers.
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_deadline);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(address.Origin, "/api/info"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password)));
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return StatusFailure(response.StatusCode);
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            byte[] bytes = new byte[MaximumResponseBytes + 1];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(length), timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > MaximumResponseBytes)
            {
                return InvalidResponse();
            }

            timeout.Token.ThrowIfCancellationRequested();
            return MapIdentity(bytes.AsMemory(0, length));
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested ? ConnectionResult.Cancelled
                : new(ConnectionOutcome.TimedOut, "Connection timed out. Check the server and address, then try again.");
        }
        catch (JsonException)
        {
            return InvalidResponse();
        }
        catch (HttpRequestException exception)
        {
            return exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
                ? new(ConnectionOutcome.Refused, "No server is accepting connections at this address. Start the server or correct its port.")
                : new(ConnectionOutcome.TransportFailure, "Could not contact the server. Check the address and server, then try again.");
        }
        catch (IOException)
        {
            return new(ConnectionOutcome.TransportFailure, "The server response was interrupted. Check the server and try again.");
        }
    }

    private static ConnectionResult MapIdentity(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("pid", out JsonElement pid) || pid.ValueKind != JsonValueKind.Number || !pid.TryGetInt64(out long processId) || processId < 0
            || !root.TryGetProperty("urls", out JsonElement urls) || urls.ValueKind != JsonValueKind.Array
            || urls.EnumerateArray().Any(url => url.ValueKind != JsonValueKind.String)
            || !root.TryGetProperty("paths", out JsonElement paths) || paths.ValueKind != JsonValueKind.Object
            || !paths.TryGetProperty("tmp", out JsonElement tmp) || tmp.ValueKind != JsonValueKind.String)
        {
            return InvalidResponse();
        }

        string reportedVersion = version.GetString()!;
        if (!ServerVersionPolicy.IsSupported(reportedVersion))
        {
            // Only display a bounded numeric version; do not render arbitrary response text.
            string safeVersion = reportedVersion.Length is > 0 and <= 32
                && reportedVersion.All(c => char.IsAsciiDigit(c) || c == '.') ? reportedVersion : "unrecognized";
            return new(ConnectionOutcome.Incompatible, $"Unsupported server version {safeVersion}. Use OpenCode {ServerVersionPolicy.SupportedVersion}.");
        }

        return new(ConnectionOutcome.Ready, "Server ready · OpenCode " + reportedVersion + ". Project and model readiness have not been checked.", reportedVersion);
    }

    private static ConnectionResult InvalidResponse() => new(ConnectionOutcome.Incompatible,
        "The server returned an invalid identity response. Check the address and use OpenCode " + ServerVersionPolicy.SupportedVersion + ".");

    private static ConnectionResult StatusFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new(ConnectionOutcome.AuthenticationFailed, "Authentication failed. Check the server password and try again."),
        HttpStatusCode.ServiceUnavailable => new(ConnectionOutcome.Unavailable, "The server is starting or unavailable. Wait, then connect again."),
        HttpStatusCode.InternalServerError => new(ConnectionOutcome.InitializationFailed, "The server failed to initialize. Check its output, then try again."),
        _ => new(ConnectionOutcome.HttpFailure, $"The server returned HTTP {(int)status}. Check the address and server. Redirects are not followed."),
    };
}
