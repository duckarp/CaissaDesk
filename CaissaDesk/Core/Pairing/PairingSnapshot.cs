using System.Reflection;
using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

/// <summary>
/// Builds the tournament an engine pairs against without touching the live model: a shallow copy of the
/// <see cref="Tournament"/> with its own Rounds and Players lists, so a hypothetical round history can be
/// supplied while the UI keeps rendering the real one.
/// </summary>
public static class PairingSnapshot
{
    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <param name="rounds">The round history the engine should see (the sequence is copied into a new list).</param>
    public static Tournament WithRounds(Tournament source, IEnumerable<Round> rounds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rounds);

        // MemberwiseClone copies every field, including ones added to Tournament later
        var copy = (Tournament)MemberwiseCloneMethod.Invoke(source, null)!;
        copy.Rounds = rounds.ToList();
        copy.Players = source.Players.ToList();
        return copy;
    }
}
