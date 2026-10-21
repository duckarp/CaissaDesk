namespace CaissaDesk.Core.Notifications;

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record Notification(Guid Id, NotificationKind Kind, string Message);

/// <summary>Lightweight toast notifications (e.g. "changes could not be saved") shown by the ToastHost.</summary>
public interface INotificationService
{
    IReadOnlyList<Notification> Notifications { get; }

    /// <summary>Raised (possibly from a timer thread) whenever the list changes.</summary>
    event Action? Changed;

    /// <param name="duration">How long the toast stays; <see cref="TimeSpan.Zero"/> keeps it until dismissed.
    /// Null uses the default for the kind (errors stay longer).</param>
    Notification Show(NotificationKind kind, string message, TimeSpan? duration = null);

    void Dismiss(Guid id);
}

public static class NotificationServiceExtensions
{
    public static Notification ShowError(this INotificationService service, string message) => service.Show(NotificationKind.Error, message);
    public static Notification ShowSuccess(this INotificationService service, string message) => service.Show(NotificationKind.Success, message);
}

public sealed class NotificationService : INotificationService, IDisposable
{
    /// <summary>Oldest toasts are dropped beyond this many.</summary>
    public const int MaxVisible = 5;

    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultErrorDuration = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private readonly List<Notification> _items = [];
    private readonly Dictionary<Guid, ITimer> _timers = [];

    public NotificationService() : this(TimeProvider.System) { }

    public NotificationService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public event Action? Changed;

    public IReadOnlyList<Notification> Notifications
    {
        get
        {
            lock (_lock) return _items.ToList();
        }
    }

    public Notification Show(NotificationKind kind, string message, TimeSpan? duration = null)
    {
        var notification = new Notification(Guid.NewGuid(), kind, message);
        var lifetime = duration ?? (kind == NotificationKind.Error ? DefaultErrorDuration : DefaultDuration);

        lock (_lock)
        {
            _items.Add(notification);
            while (_items.Count > MaxVisible)
            {
                RemoveLocked(_items[0].Id);
            }

            if (lifetime > TimeSpan.Zero)
            {
                _timers[notification.Id] = _timeProvider.CreateTimer(
                    _ => Dismiss(notification.Id), null, lifetime, Timeout.InfiniteTimeSpan);
            }
        }

        Changed?.Invoke();
        return notification;
    }

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_lock)
        {
            removed = RemoveLocked(id);
        }

        if (removed) Changed?.Invoke();
    }

    private bool RemoveLocked(Guid id)
    {
        if (_timers.Remove(id, out var timer))
        {
            timer.Dispose();
        }
        return _items.RemoveAll(n => n.Id == id) > 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var timer in _timers.Values) timer.Dispose();
            _timers.Clear();
            _items.Clear();
        }
    }
}
