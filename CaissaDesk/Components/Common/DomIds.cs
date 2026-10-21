using System.Threading;

namespace CaissaDesk.Components.Common;

/// <summary>
/// Hands out element ids that are unique within the WebView document, so that several instances of
/// the same component can use <c>label for</c> / <c>aria-labelledby</c> without clashing.
/// </summary>
public static class DomIds
{
    private static long _counter;

    /// <summary>Returns a new id prefix such as <c>player-form-7</c>; append a suffix per element.</summary>
    public static string Next(string prefix) => $"{prefix}-{Interlocked.Increment(ref _counter)}";
}
