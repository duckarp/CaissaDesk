using CaissaDesk.Components;
using CaissaDesk.Components.Tournament;

namespace CaissaDesk.Tests;

public class WorkbenchStateTests
{
    private static (WorkbenchState State, TournamentTab Tab, List<Tournament> Saved) Create()
    {
        var tab = new TournamentTab(new Tournament { Name = "Open" });
        var saved = new List<Tournament>();
        var state = new WorkbenchState(tab, new TiebreakService(), t =>
        {
            saved.Add(t);
            return Task.FromResult(true);
        });
        return (state, tab, saved);
    }

    [Fact]
    public async Task SaveAsync_DelegatesToCoordinator()
    {
        var (state, tab, saved) = Create();

        Assert.True(await state.SaveAsync(tab.Tournament!));
        Assert.Same(tab.Tournament, saved.Single());
    }

    [Fact]
    public void Details_IsRebuiltWhenTabTournamentIsReplaced_OrDiscarded()
    {
        var (state, tab, _) = Create();

        var first = state.Details;
        Assert.Same(first, state.Details);

        state.DiscardDetailsDraft();
        var second = state.Details;
        Assert.NotSame(first, second);

        tab.Tournament = new Tournament { Name = "Replaced" };
        Assert.Equal("Replaced", state.Details.Name);
    }

    [Fact]
    public void PairingSession_TracksTheTabsCurrentTournament()
    {
        var (state, tab, _) = Create();
        var original = tab.Tournament!;

        Assert.True(state.Pairing.IsCurrent(original));
        tab.Tournament = new Tournament();
        Assert.False(state.Pairing.IsCurrent(original));
    }

    [Fact]
    public void Dispose_CancelsPairing()
    {
        var (state, _, _) = Create();
        var token = state.Pairing.Token;

        state.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void NotifyChanged_RaisesChanged()
    {
        var (state, _, _) = Create();
        int raised = 0;
        state.Changed += () => raised++;

        state.NotifyChanged();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ResultEntry_ApplyWithUndo_RestoresPreviousValues()
    {
        var pairing = new Pairing { Result = MatchResult.NotPlayed, Remarks = "late" };
        var entry = new ResultEntry(pairing, MatchResult.Draw, null);

        var undo = entry.ApplyWithUndo();
        Assert.Equal(MatchResult.Draw, pairing.Result);
        Assert.Null(pairing.Remarks);

        undo();
        Assert.Equal(MatchResult.NotPlayed, pairing.Result);
        Assert.Equal("late", pairing.Remarks);
    }
}
