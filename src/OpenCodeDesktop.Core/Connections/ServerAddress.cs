using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenCodeDesktop.Core.Connections;

/// <summary>A validated origin within the verified local-server boundary.</summary>
public sealed partial class ServerAddress
{
    private ServerAddress(string origin) => Origin = new Uri(origin);

    public Uri Origin { get; }
    public string Display => "http://127.0.0.1:" + Origin.Port.ToString(CultureInfo.InvariantCulture);
    public const string DefaultOrigin = "http://127.0.0.1:28894";
    public const string FormatMessage = "Use http://127.0.0.1:<port> with a port from 1 to 65535 and no path, query, or fragment.";

    public static bool TryParse(string? input, out ServerAddress? address)
    {
        address = null;
        Match match = OriginPattern().Match(input?.Trim() ?? "");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        address = new ServerAddress("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    [GeneratedRegex(@"\Ahttp://127\.0\.0\.1:([0-9]{1,5})/?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginPattern();
}
