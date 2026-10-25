using System.Text;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class PromptAttachmentTests
{
    [Fact]
    public void LocalContentIsCapturedAndBoundedWithoutExposingLocalPathToServer()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("测试 # %");
        PromptAttachment file = PromptAttachment.Local("note.txt", "C:\\private\\note.txt", bytes);
        bytes[0] = 0;
        Assert.Equal("测试 # %", Encoding.UTF8.GetString(Convert.FromBase64String(file.Uri.Split(',')[1])));
        Assert.DoesNotContain("private", file.Uri, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PromptAttachment.Local("large.txt", "local", new byte[PromptAttachment.MaximumBytes + 1]));
        Assert.Throws<ArgumentException>(() => PromptAttachment.Local("binary.txt", "local", [0]));
        Assert.Throws<ArgumentException>(() => PromptAttachment.Local("bad.txt", "local", [255]));
        Assert.Throws<ArgumentException>(() => PromptAttachment.Local("image.png", "local", [1]));
        Assert.NotNull(PromptAttachment.Local("empty.txt", "local", []));
    }

    [Theory]
    [InlineData("C:\\project 空 #pct%", "dir/note 空 #%.txt")]
    [InlineData("/project 空 #pct%", "dir/note 空 #%.txt")]
    public void ServerReferencesEscapeReservedCharactersAndStayInsideProject(string directory, string path)
    {
        PromptAttachment file = PromptAttachment.Server(directory, path, new(FilePreviewKind.Text, "hello", false));
        Assert.Equal(path, ProjectFileReferences.Candidate(directory, file.Uri));
        Assert.Throws<ArgumentException>(() => PromptAttachment.Server(directory, "../outside.txt", new(FilePreviewKind.Text, "hello", false)));
        Assert.Throws<ArgumentException>(() => PromptAttachment.Server(directory, path, new(FilePreviewKind.Text, "partial", true)));
        Assert.Throws<ArgumentException>(() => PromptAttachment.Server(directory, path, new(FilePreviewKind.Unsupported, "", false)));
    }

    [Fact]
    public void DraftVersionPreservesEditsEvenWhenOriginalTextAndAttachmentsAreRestored()
    {
        PromptDraft draft = new() { Text = "original" };
        PromptAttachment file = PromptAttachment.Local("note.txt", "local", [65]);
        draft.Add(file);
        long submitted = draft.Version;
        draft.Text = "edit";
        draft.Text = "original";
        Assert.False(draft.Consume(submitted));
        submitted = draft.Version;
        draft.Remove(file);
        draft.Add(file);
        Assert.False(draft.Consume(submitted));
        Assert.True(draft.Consume(draft.Version));
        Assert.Empty(draft.Text);
        Assert.Empty(draft.Files);
    }

    [Fact]
    public void RejectedFifthFilePreservesUsableDraftAndExistingFiles()
    {
        PromptDraft draft = new() { Text = "keep" };
        for (int i = 0; i < 4; i++) { draft.Add(PromptAttachment.Local(i + ".txt", i.ToString(), [65])); }
        Assert.Throws<ArgumentException>(() => draft.Add(PromptAttachment.Local("fifth.txt", "local", [65])));
        Assert.Equal(4, draft.Files.Count);
        Assert.Equal("keep", draft.Text);
    }
}
