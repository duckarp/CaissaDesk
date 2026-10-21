using CaissaDesk.Components;
using Caissa.Shared.Models;
using Xunit;

namespace CaissaDesk.Tests;

public class WorkspaceStateTests
{
    [Fact]
    public void WorkspaceState_InitializesWithDashboard()
    {
        var ws = new WorkspaceState();

        Assert.Single(ws.Tabs);
        Assert.IsType<DashboardTab>(ws.ActiveTab);
    }

    [Fact]
    public void RequestCreateTournament_FiresEvent()
    {
        var ws = new WorkspaceState();
        bool eventFired = false;
        ws.OnRequestCreateTournament += () => eventFired = true;

        ws.RequestCreateTournament();

        Assert.True(eventFired);
    }

    [Fact]
    public void OpenTournament_AddsTournamentTabAndActivates()
    {
        var ws = new WorkspaceState();
        var tournament = new Tournament
        {
            Id = "t123",
            Name = "Open Rapid 2026",
            System = TournamentSystem.SwissDutch,
            TotalRounds = 5
        };

        var tab = ws.OpenTournament(tournament);

        Assert.Equal(2, ws.Tabs.Count);
        Assert.Same(tab, ws.ActiveTab);
        Assert.Equal("Open Rapid 2026", tab.Title);
        Assert.Same(tournament, tab.Tournament);

        // Opening the same tournament again switches to it without duplicating
        var sameTab = ws.OpenTournament(tournament);
        Assert.Same(tab, sameTab);
        Assert.Equal(2, ws.Tabs.Count);
    }
}
