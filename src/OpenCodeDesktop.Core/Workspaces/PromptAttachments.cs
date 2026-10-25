using System.Text;

namespace OpenCodeDesktop.Core.Workspaces;

/// <summary>Validated immutable input. Local bytes are captured when attached; server references are read again at send.</summary>
public sealed record PromptAttachment
{
    private PromptAttachment(string name, string identity, string uri, bool server)
    { Name = name; Identity = identity; Uri = uri; IsServer = server; }
    public string Name { get; }
    public string Identity { get; }
    public string Uri { get; }
    public bool IsServer { get; }
    public const int MaximumBytes = 262144;
    public const int MaximumFiles = 4;

    public static PromptAttachment Local(string name, string identity, ReadOnlySpan<byte> bytes)
    {
        ValidateName(name);
        if (bytes.Length > MaximumBytes) { throw new ArgumentException("Choose a file no larger than 256 KiB."); }
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new ArgumentException("Choose a UTF-8 text file."); }
        if (text.Contains('\0')) { throw new ArgumentException("Binary files are unsupported. Choose a UTF-8 text file."); }
        return new(name, identity, "data:text/plain;base64," + Convert.ToBase64String(bytes), false);
    }

    public static PromptAttachment Server(string directory, string path, ProjectFilePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        string normalized = path.Replace('\\', '/');
        ValidateName(normalized);
        if (preview.Kind != FilePreviewKind.Text || preview.Truncated || Encoding.UTF8.GetByteCount(preview.Text) > MaximumBytes)
        { throw new ArgumentException("Choose a supported text file with a complete preview, no larger than 256 KiB / 100,000 characters."); }
        string root = directory.Replace('\\', '/').TrimEnd('/');
        string uri = new System.Uri((root.StartsWith('/') ? "file://" : "file:///") +
            string.Join('/', (root + "/" + normalized).Split('/').Select(System.Uri.EscapeDataString))).AbsoluteUri;
        if (ProjectFileReferences.Candidate(directory, uri) != normalized)
        { throw new ArgumentException("Choose a file inside this server project."); }
        return new(normalized.Split('/')[^1], directory + " / " + path, uri, true);
    }

    private static void ValidateName(string name)
    {
        string extension = name[(name.LastIndexOf('.') + 1)..].ToLowerInvariant();
        if (extension is not ("txt" or "md" or "cs" or "json" or "xml" or "yaml" or "yml" or "js" or "ts" or "css" or "html" or "py" or "csv" or "log"))
        { throw new ArgumentException("Unsupported type. Choose a text, source, JSON, XML, YAML or Markdown file."); }
    }
}

public interface ILocalPromptFileReader
{
    Task<PromptAttachment> ReadAsync(string path, CancellationToken cancellationToken);
}

/// <summary>In-memory draft with monotonic versions, including edits that restore the same text.</summary>
public sealed class PromptDraft
{
    private string _text = "";
    private readonly List<PromptAttachment> _files = [];
    public string Text { get => _text; set { if (_text != value) { _text = value; Version++; } } }
    public long Version { get; private set; }
    public IReadOnlyList<PromptAttachment> Files => _files.AsReadOnly();
    public void Add(PromptAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (_files.Contains(attachment)) { return; }
        if (_files.Count >= PromptAttachment.MaximumFiles) { throw new ArgumentException("Attach up to four files per prompt. Remove a file first."); }
        _files.Add(attachment); Version++;
    }
    public void Remove(PromptAttachment attachment) { if (_files.Remove(attachment)) { Version++; } }
    public bool Consume(long version)
    {
        if (Version != version) { return false; }
        _text = ""; _files.Clear(); Version++; return true;
    }
}
