using CaissaDesk.Core;

namespace CaissaDesk.Tests;

public class TournamentDetailsDraftTests
{
    [Fact]
    public void Draft_Edits_DoNotTouchLiveTournamentUntilApplied()
    {
        var t = new Tournament { Name = "Live", Venue = "Hall A", TotalRounds = 7 };
        var draft = TournamentDetailsDraft.From(t);

        draft.Name = "Edited";
        draft.Venue = "Hall B";
        draft.Federation = " cze ";

        Assert.Equal("Live", t.Name);
        Assert.Equal("Hall A", t.Venue);

        Assert.Equal(TournamentDetailsValidation.Valid, draft.TryApplyTo(t));
        Assert.Equal("Edited", t.Name);
        Assert.Equal("Hall B", t.Venue);
        Assert.Equal("CZE", t.Federation);
    }

    [Fact]
    public void Draft_InvalidValues_AreRejectedAndNothingIsApplied()
    {
        var t = new Tournament { Name = "Live", Venue = "Hall A", TotalRounds = 7 };
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings = [new Pairing { BoardNumber = 1, Result = MatchResult.WhiteWin }]
        });
        t.Rounds.Add(new Round
        {
            RoundNumber = 2,
            Pairings = [new Pairing { BoardNumber = 1, Result = MatchResult.Draw }]
        });

        var emptyName = TournamentDetailsDraft.From(t);
        emptyName.Name = "   ";
        emptyName.Venue = "Hall B";
        Assert.Equal(TournamentDetailsValidation.NameRequired, emptyName.TryApplyTo(t));
        Assert.Equal("Live", t.Name);
        Assert.Equal("Hall A", t.Venue);

        var tooFew = TournamentDetailsDraft.From(t);
        tooFew.TotalRounds = 1;
        tooFew.Venue = "Hall C";
        Assert.Equal(TournamentDetailsValidation.TooFewRounds, tooFew.TryApplyTo(t));
        Assert.Equal(7, t.TotalRounds);
        Assert.Equal("Hall A", t.Venue);
    }
}
