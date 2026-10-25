using System.Net;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed partial class OpenCodeTaskConnectionTests
{
    [Fact]
    public async Task FourMaximumInlineFilesCanBeAdmittedAndReconciledFromInbox()
    {
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('A', PromptAttachment.MaximumBytes)));
        var input = new { id = "msg_large", sessionID = "s", files = Enumerable.Range(0, 4).Select(i => new { data = content, mime = "text/plain", name = i + ".txt", source = new { type = "inline" } }) };
        string admitted = JsonSerializer.Serialize(new { data = input });
        string inbox = JsonSerializer.Serialize(new { data = new[] { input } });
        using HttpClient client = new(new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
        {
            "/api/session/s/prompt" => admitted,
            "/api/session/s/inbox" => inbox,
            "/api/session/active" => """{"data":{}}""",
            "/api/session/s/permission" or "/api/session/s/form" => """{"data":[]}""",
            _ => """{"data":{"id":"s","location":{"directory":"/dir"},"projectID":"global","title":"Task"}}""",
        }))));
        using OpenCodeWorkspaceConnection api = Create(client);
        PromptAttachment file = PromptAttachment.Local("large.txt", "local", Encoding.UTF8.GetBytes(new string('A', PromptAttachment.MaximumBytes)));
        await api.SubmitAsync("s", new TaskSubmission("msg_large", "") { Files = [file, file, file, file] }, TestContext.Current.CancellationToken);
        TaskSnapshot snapshot = await api.TaskSnapshotAsync("s", TestContext.Current.CancellationToken);
        Assert.Equal("msg_large", Assert.Single(snapshot.Inbox));
    }

    [Fact]
    public async Task InlineHistoryExposesOnlyLabelsAndKeepsVerifiedServerReferences()
    {
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json("""
            {"data":[{"id":"msg_files","type":"user","text":"text","files":[
            {"data":"c2VjcmV0","mime":"text/plain","name":"local.txt","source":{"type":"inline"}},
            {"data":"aGVsbG8=","mime":"text/plain","name":"server.txt","source":{"type":"uri","uri":"file:///C:/project/server.txt"}}
            ]}],"cursor":{"next":null}}
            """))));
        using OpenCodeWorkspaceConnection api = Create(client);
        HistoryMessage message = Assert.Single((await api.HistoryAsync("s", null, TestContext.Current.CancellationToken)).Data);
        Assert.Equal("", message.FileReferences[0].Uri);
        Assert.Equal("local.txt", message.FileReferences[0].Name);
        Assert.Equal("file:///C:/project/server.txt", message.FileReferences[1].Uri);
        Assert.DoesNotContain("c2VjcmV0", JsonSerializer.Serialize(message), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptSerializesOnlyVerifiedUriFormsAndNames()
    {
        PromptAttachment server = PromptAttachment.Server("C:\\project #pct%", "note 空 #%.txt", new(FilePreviewKind.Text, "hello", false));
        PromptAttachment local = PromptAttachment.Local("local.txt", "C:\\private\\local.txt", Encoding.UTF8.GetBytes("local data"));
        int calls = 0;
        using HttpClient client = new(new Handler(async (request, token) =>
        {
            calls++;
            using JsonDocument json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            JsonElement root = json.RootElement;
            Assert.Equal("msg_files", root.GetProperty("id").GetString());
            Assert.Equal(server.Uri, root.GetProperty("files")[0].GetProperty("uri").GetString());
            Assert.Equal(local.Uri, root.GetProperty("files")[1].GetProperty("uri").GetString());
            Assert.DoesNotContain("private", root.GetRawText(), StringComparison.Ordinal);
            Assert.Equal("steer", root.GetProperty("delivery").GetString());
            return Json("""{"data":{"id":"msg_files","sessionID":"s"}}""");
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        await api.SubmitAsync("s", new("msg_files", "text") { Files = [server, local] }, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LocalReaderRejectsMissingLockedOversizedAndInvalidFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ocd-attachments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "note.txt");
        LocalPromptFileReader reader = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(path, token));
            await File.WriteAllTextAsync(path, "captured", token);
            PromptAttachment file = await reader.ReadAsync(path, token);
            await File.WriteAllTextAsync(path, "changed", token);
            Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("captured")), file.Uri, StringComparison.Ordinal);
            using (FileStream locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(path, token)); }
            await File.WriteAllBytesAsync(path, new byte[PromptAttachment.MaximumBytes + 1], token);
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(path, token));
            await File.WriteAllBytesAsync(path, [255], token);
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(path, token));
        }
        finally { Directory.Delete(directory, true); }
    }
}
