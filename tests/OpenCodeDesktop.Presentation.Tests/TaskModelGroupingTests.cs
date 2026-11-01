using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class TaskModelGroupingTests
{
    private static readonly TaskModel[] s_models =
    [
        new("z-1", "zed", "Zed One"),
        new("a-1", "alpha", "Alpha One"),
        new("a-2", "alpha", "Alpha Two"),
        new("k-1", "zed", "Kimi K3"),
    ];

    private static readonly TaskProvider[] s_providers = [new("alpha", "Alpha Cloud"), new("zed", "Zed Cloud")];
    private static readonly string[] s_alphaGroup = ["a-1", "a-2"];
    private static readonly string[] s_zedGroup = ["z-1", "k-1"];
    private static readonly string[] s_headers = ["Alpha Cloud", "Zed Cloud"];

    [Fact]
    public void GroupsAreAlphabeticalByDisplayNameAndKeepCatalogOrder()
    {
        IReadOnlyList<TaskModelGroup> groups = TaskModelGrouping.Build(s_models, s_providers, "");
        Assert.Equal(s_headers, groups.Select(g => g.Provider));
        Assert.Equal(s_alphaGroup, groups[0].Models.Select(m => m.Id));
        Assert.Equal(s_zedGroup, groups[1].Models.Select(m => m.Id));
    }

    [Fact]
    public void MissingProviderNamesFallBackToTheId()
    {
        IReadOnlyList<TaskModelGroup> groups = TaskModelGrouping.Build([new("m", "ollama-cloud", "kimi-k3")], [], "");
        Assert.Equal("ollama-cloud", Assert.Single(groups).Provider);
    }

    [Fact]
    public void SearchMatchesModelAndProviderNamesCaseInsensitivelyAndDropsEmptyGroups()
    {
        IReadOnlyList<TaskModelGroup> groups = TaskModelGrouping.Build(s_models, s_providers, "KIMI");
        TaskModelGroup group = Assert.Single(groups);
        Assert.Equal("Zed Cloud", group.Provider);
        Assert.Equal("k-1", Assert.Single(group.Models).Id);

        groups = TaskModelGrouping.Build(s_models, s_providers, "alpha");
        Assert.Equal(s_alphaGroup, Assert.Single(groups).Models.Select(m => m.Id));

        Assert.Empty(TaskModelGrouping.Build(s_models, s_providers, "nothing matches"));
    }

    [Fact]
    public void FirstOffersTheLeadingVisibleModel()
    {
        IReadOnlyList<TaskModelGroup> groups = TaskModelGrouping.Build(s_models, s_providers, "zed");
        Assert.Equal("z-1", TaskModelGrouping.First(groups)!.Id);
        Assert.Null(TaskModelGrouping.First(TaskModelGrouping.Build(s_models, s_providers, "zzz")));
    }
}
