using System.ComponentModel;
using System.Globalization;

namespace Beutl.Extensions.Karaoke.Editing;

public enum ImportJobState
{
    Running,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>One import or download on the import page's history, newest first, like a browser's download list.</summary>
public sealed class ImportJobViewModel : INotifyPropertyChanged
{
    private readonly Action? _cancel;
    private ImportJobState _state;
    private string _message;

    internal ImportJobViewModel(string title, string source, string message, Action? cancel = null)
    {
        Title = title;
        Source = source;
        _message = message;
        _cancel = cancel;
        _state = ImportJobState.Running;
        StartedAt = DateTime.Now;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What was imported: the URL or the file name.</summary>
    public string Title { get; }

    /// <summary>Where it came from, such as "ニコニコ動画" or "ファイル".</summary>
    public string Source { get; }

    public DateTime StartedAt { get; }

    public string Caption => string.Create(CultureInfo.InvariantCulture, $"{Source} ・ {StartedAt:HH:mm}");

    public ImportJobState State
    {
        get => _state;
        private set
        {
            _state = value;
            Raise(nameof(State));
            Raise(nameof(IsRunning));
        }
    }

    public bool IsRunning => _state == ImportJobState.Running;

    /// <summary>Progress while running, then the result or the reason it failed.</summary>
    public string Message
    {
        get => _message;
        set
        {
            if (_message == value)
                return;

            _message = value;
            Raise(nameof(Message));
        }
    }

    public bool CanCancel => _cancel != null;

    public void Cancel()
    {
        if (IsRunning)
            _cancel?.Invoke();
    }

    internal void Finish(ImportJobState state, string message)
    {
        Message = message;
        State = state;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
