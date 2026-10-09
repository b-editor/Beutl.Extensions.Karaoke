using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Beutl.Extensions.Karaoke.Lyrics;

namespace Beutl.Extensions.Karaoke.Editing;

/// <summary>One editable lyric line. Edits are written back through the owning tab.</summary>
public sealed class LyricRowViewModel : INotifyPropertyChanged
{
    private readonly KaraokeTabViewModel _owner;
    private string _startText = "";
    private string _endText = "";
    private string _sungEndText = "";
    private string _text = "";
    private bool _loading;

    internal LyricRowViewModel(KaraokeTabViewModel owner, int index, LyricLine line)
    {
        _owner = owner;
        Load(index, line);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The index of the line in the object's lyrics.</summary>
    public int Index { get; private set; }

    public LyricLine Line { get; private set; } = null!;

    public string StartText
    {
        get => _startText;
        set => Set(ref _startText, value);
    }

    public string EndText
    {
        get => _endText;
        set => Set(ref _endText, value);
    }

    /// <summary>When the last timed run has been sung; only meaningful with per-character timing.</summary>
    public string SungEndText
    {
        get => _sungEndText;
        set => Set(ref _sungEndText, value);
    }

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    /// <summary>The timed runs, one per unit the singer times, when the line has per-character timing.</summary>
    public ObservableCollection<LyricSegmentViewModel> Segments { get; } = [];

    public bool HasWordTiming => Line.HasWordTiming;

    public string TimingName => Line.HasWordTiming ? "文字ごと" : "—";

    internal bool IsLoading => _loading;

    internal KaraokeTabViewModel Owner => _owner;

    internal void Load(int index, LyricLine line)
    {
        _loading = true;
        try
        {
            Index = index;
            Line = line;
            StartText = LrcWriter.FormatTime(line.Start);
            EndText = LrcWriter.FormatTime(line.End);
            SungEndText = LrcWriter.FormatTime(line.SungEnd);
            Text = line.Text;

            int count = line.HasWordTiming ? line.Words.Count : 0;
            while (Segments.Count > count)
                Segments.RemoveAt(Segments.Count - 1);
            for (int i = 0; i < count; i++)
            {
                LyricWord word = line.Words[i];
                string text = line.Text.Substring(word.CharStart, word.Length);
                if (i < Segments.Count)
                    Segments[i].Load(i, text, word.Start);
                else
                    Segments.Add(new LyricSegmentViewModel(this, i, text, word.Start));
            }
        }
        finally
        {
            _loading = false;
        }

        foreach (string name in (string[])[nameof(HasWordTiming), nameof(TimingName)])
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (!_loading)
            _owner.OnRowEdited(this, name!);
    }
}

/// <summary>One timed run of a line: the characters and when singing them starts.</summary>
public sealed class LyricSegmentViewModel : INotifyPropertyChanged
{
    private readonly LyricRowViewModel _row;
    private string _timeText = "";
    private bool _loading;

    internal LyricSegmentViewModel(LyricRowViewModel row, int index, string text, TimeSpan start)
    {
        _row = row;
        Load(index, text, start);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; private set; }

    public string Text { get; private set; } = "";

    public string TimeText
    {
        get => _timeText;
        set
        {
            if (_timeText == value)
                return;

            _timeText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeText)));
            if (!_loading && !_row.IsLoading)
                _row.Owner.OnSegmentEdited(_row, this);
        }
    }

    internal void Load(int index, string text, TimeSpan start)
    {
        _loading = true;
        try
        {
            Index = index;
            if (Text != text)
            {
                Text = text;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }

            TimeText = LrcWriter.FormatTime(start);
        }
        finally
        {
            _loading = false;
        }
    }
}
