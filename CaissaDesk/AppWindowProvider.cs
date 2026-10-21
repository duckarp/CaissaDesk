using Photino.NET;

namespace CaissaDesk;

/// <summary>
/// Exposes the native desktop window to Blazor components. Photino.Blazor
/// creates the window during app build, so the instance is assigned in
/// Program.cs right after Build().
/// </summary>
public interface IAppWindowProvider
{
    PhotinoWindow Window { get; set; }
}

public sealed class AppWindowProvider : IAppWindowProvider
{
    public PhotinoWindow Window { get; set; } = null!;
}
