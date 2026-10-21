namespace CaissaDesk.Components.Tournament;

/// <summary>A result the arbiter confirmed in the result dialog, not yet applied to <see cref="Pairing"/>.</summary>
public sealed record ResultEntry(Pairing Pairing, MatchResult Result, string? Remarks)
{
    /// <summary>Writes the entry into the pairing and returns an action that restores the previous values.</summary>
    public Action ApplyWithUndo()
    {
        var (previousResult, previousRemarks) = (Pairing.Result, Pairing.Remarks);
        Pairing.Result = Result;
        Pairing.Remarks = Remarks;
        return () =>
        {
            Pairing.Result = previousResult;
            Pairing.Remarks = previousRemarks;
        };
    }
}
