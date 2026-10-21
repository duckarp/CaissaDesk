namespace CaissaDesk.Components.Common;

/// <summary>
/// State of one confirmation dialog (title, texts, icons and the action to run on confirm), rendered by
/// <see cref="ConfirmDialog"/>. Replaces the per-component copies of the same nine fields.
/// </summary>
public sealed class ConfirmDialogState
{
    private Func<Task>? _action;

    public bool IsOpen { get; private set; }
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public string? Details { get; private set; }
    public string? ConfirmText { get; private set; }
    public string? HeaderIcon { get; private set; }
    public string? ConfirmIcon { get; private set; }
    public bool IsDestructive { get; private set; } = true;

    /// <summary>Opens the dialog; <paramref name="icon"/> is used for both the header and the confirm button.</summary>
    public void Request(
        string title,
        string message,
        Func<Task> onConfirm,
        string? details = null,
        string? confirmText = null,
        string? icon = null,
        bool isDestructive = true)
    {
        Title = title;
        Message = message;
        Details = details;
        ConfirmText = confirmText;
        HeaderIcon = icon;
        ConfirmIcon = icon;
        IsDestructive = isDestructive;
        _action = onConfirm;
        IsOpen = true;
    }

    public void Cancel()
    {
        IsOpen = false;
        _action = null;
    }

    /// <summary>Closes the dialog and runs the pending action (exceptions propagate to the caller).</summary>
    public async Task ConfirmAsync()
    {
        IsOpen = false;
        var action = _action;
        _action = null;
        if (action != null)
        {
            await action();
        }
    }
}
