using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class ShellPresentationTests
{
    [Fact]
    public void ConnectionStatusDistinguishesConnectedConnectingAndDisconnectedStates()
    {
        Assert.True(ServerAddress.TryParse("http://127.0.0.1:28894", out ServerAddress? address));
        ServerProfile profile = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Local", address!);
        ActiveServer active = new(profile, "2.1.4");

        ShellStatusPresentation connected = ShellPresentation.Connection(active, null, true);
        Assert.Equal("Connected", connected.Label);
        Assert.Contains("Local", connected.Detail, StringComparison.Ordinal);
        Assert.Contains("2.1.4", connected.Detail, StringComparison.Ordinal);
        Assert.Equal(ShellTone.Success, connected.Tone);

        ShellStatusPresentation switching = ShellPresentation.Connection(active, profile with { Id = Guid.NewGuid(), Name = "Other" }, true);
        Assert.Equal(ShellTone.Warning, switching.Tone);
        Assert.Contains("Switching", switching.Detail, StringComparison.Ordinal);

        ShellStatusPresentation disconnected = ShellPresentation.Connection(null, null, true);
        Assert.Equal("Disconnected", disconnected.Label);
        Assert.Contains("Connections", disconnected.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionCardsShowActiveConnectingAndInactiveActions()
    {
        Assert.True(ServerAddress.TryParse("http://127.0.0.1:4096", out ServerAddress? address));
        ServerProfile profile = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Sandbox", address!);
        ActiveServer active = new(profile with { Id = Guid.NewGuid(), Name = "Active" }, "2.1.4");

        ConnectionCardPresentation inactive = ShellPresentation.ConnectionCard(profile, active, null, false);
        Assert.Equal("Not connected", inactive.StateLabel);
        Assert.Equal("Connect", inactive.PrimaryActionLabel);
        Assert.True(inactive.CanRemove);

        ConnectionCardPresentation connecting = ShellPresentation.ConnectionCard(profile, active, profile.Id, false);
        Assert.Equal("Connecting…", connecting.StateLabel);
        Assert.True(connecting.IsConnecting);
        Assert.False(connecting.CanRemove);

        ConnectionCardPresentation selected = ShellPresentation.ConnectionCard(active.Profile, active, null, false);
        Assert.True(selected.IsActive);
        Assert.Equal("View", selected.PrimaryActionLabel);
        Assert.False(selected.CanRemove);
    }

    [Fact]
    public void TaskStatusIsNeutralWhenNoSessionAndUnavailableWhenUnhydrated()
    {
        ShellStatusPresentation empty = ShellPresentation.Task(null);
        Assert.Equal(ShellTone.Neutral, empty.Tone);
        Assert.Contains("session", empty.Label, StringComparison.OrdinalIgnoreCase);

        SessionTaskState state = new("session", "/project", "project");
        ShellStatusPresentation stale = ShellPresentation.Task(state);
        Assert.Equal(ShellTone.Warning, stale.Tone);
        Assert.Contains("unavailable", stale.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsSectionsKeepConnectionsAvailableAndOtherSurfacesHonest()
    {
        SettingsSectionPresentation connections = ShellPresentation.Section(0);
        Assert.True(connections.IsAvailable);
        Assert.Equal("Connections", connections.Title);

        for (int index = 1; index < 6; index++)
        {
            SettingsSectionPresentation section = ShellPresentation.Section(index);
            Assert.False(section.IsAvailable);
            Assert.Contains("not available", section.Description, StringComparison.Ordinal);
        }
    }
}
