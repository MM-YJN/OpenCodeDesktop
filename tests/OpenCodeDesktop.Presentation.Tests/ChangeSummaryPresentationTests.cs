using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class ChangeSummaryPresentationTests
{
    [Fact]
    public void RecordedChangesUsePrecedingUserBoundaryAndNeverGuessFromProse()
    {
        IReadOnlyList<ConversationEntry> entries = ConversationPresentation.From([
            new("old", "assistant", "changed guessed.txt"),
            new("u1", "user", "first"),
            new("a1", "assistant", "done") { ChangedFiles = ["actual.txt"] },
            new("u2", "user", "second"),
            new("a2", "assistant", "done") { ChangedFiles = ["other.txt"] },
        ]);
        Assert.Null(entries[0].ReviewFrom);
        Assert.Empty(entries[0].ChangedFiles);
        Assert.Equal("u1", entries[2].ReviewFrom);
        Assert.Equal("actual.txt", Assert.Single(entries[2].ChangedFiles));
        Assert.Equal("u2", entries[4].ReviewFrom);
    }
}
