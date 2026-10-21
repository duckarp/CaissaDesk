using TournamentModel = Caissa.Shared.Models.Tournament;

namespace CaissaDesk.Components;

public enum TournamentView
{
    Roster = 0,
    Pairings = 1,
    Standings = 2,
    Settings = 3
}

public abstract class WorkspaceTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public abstract string Title { get; }
    public abstract string Icon { get; }
    public bool CanClose { get; protected set; } = true;
}

public class DashboardTab : WorkspaceTab
{
    public override string Title => "Dashboard";
    public override string Icon => "dashboard";

    public DashboardTab()
    {
        CanClose = false;
    }
}

public class TournamentTab : WorkspaceTab
{
    public string TournamentName { get; set; } = "New Tournament";
    public string TournamentId { get; set; } = Guid.NewGuid().ToString("N");
    public TournamentView CurrentView { get; set; } = TournamentView.Roster;
    public TournamentModel? Tournament { get; set; }

    public override string Title => Tournament?.Name ?? TournamentName;
    public override string Icon => "trophy";

    public event Action? OnTabChanged;

    public TournamentTab(string name = "New Tournament", string? tournamentId = null)
    {
        TournamentName = name;
        if (!string.IsNullOrEmpty(tournamentId))
        {
            TournamentId = tournamentId;
        }
    }

    public TournamentTab(TournamentModel tournament)
    {
        Tournament = tournament;
        TournamentId = tournament.Id;
        TournamentName = tournament.Name;
    }

    public void SetView(TournamentView view)
    {
        CurrentView = view;
        OnTabChanged?.Invoke();
    }

    public void NotifyChanged()
    {
        if (Tournament != null)
        {
            TournamentName = Tournament.Name;
        }
        OnTabChanged?.Invoke();
    }
}

public class SettingsTab : WorkspaceTab
{
    public override string Title => "Settings";
    public override string Icon => "settings";
}

public class WorkspaceState
{
    private readonly List<WorkspaceTab> _tabs = [];
    public IReadOnlyList<WorkspaceTab> Tabs => _tabs.AsReadOnly();

    public WorkspaceTab ActiveTab { get; private set; }

    public event Action? OnChange;
    public event Action? OnRequestCreateTournament;

    public WorkspaceState()
    {
        var dashboard = new DashboardTab();
        _tabs.Add(dashboard);
        ActiveTab = dashboard;
    }

    public void RequestCreateTournament()
    {
        OnRequestCreateTournament?.Invoke();
    }

    public void SelectTab(Guid id)
    {
        var target = _tabs.FirstOrDefault(t => t.Id == id);
        if (target != null && target != ActiveTab)
        {
            ActiveTab = target;
            NotifyStateChanged();
        }
    }

    public void SelectDashboard()
    {
        var dashboard = _tabs.OfType<DashboardTab>().FirstOrDefault();
        if (dashboard != null)
        {
            ActiveTab = dashboard;
            NotifyStateChanged();
        }
    }

    public TournamentTab OpenTournament(TournamentModel tournament)
    {
        var existing = _tabs.OfType<TournamentTab>().FirstOrDefault(t => t.TournamentId == tournament.Id);
        if (existing != null)
        {
            existing.Tournament = tournament;
            existing.TournamentName = tournament.Name;
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new TournamentTab(tournament);
        _tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public SettingsTab CreateSettingsTab()
    {
        var existing = _tabs.OfType<SettingsTab>().FirstOrDefault();
        if (existing != null)
        {
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new SettingsTab();
        _tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public void CloseTab(Guid id)
    {
        var target = _tabs.FirstOrDefault(t => t.Id == id);
        if (target == null || !target.CanClose) return;

        int index = _tabs.IndexOf(target);
        _tabs.Remove(target);

        if (ActiveTab == target)
        {
            int nextIndex = Math.Max(0, index - 1);
            ActiveTab = _tabs.Count > 0 ? _tabs[nextIndex] : new DashboardTab();
        }

        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
