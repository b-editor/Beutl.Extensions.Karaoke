using System.Globalization;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.Extensions.Karaoke.Editing;

/// <summary>
/// The "カラオケ字幕" tool tab: lists the lines of one karaoke object for editing, times them character by
/// character, and imports or exports lyrics. The tab edits the object whose "編集" button opened it, not whatever
/// the timeline selects. Every change is one undoable edit of the lyrics.
/// </summary>
public sealed class KaraokeTabViewModel : IToolContext
{
    private const string NoTargetMessage = "「編集」から開いてください";

    private const string MissingTargetMessage = "要素が削除されました";

    private static readonly TimeSpan s_newLineLength = TimeSpan.FromSeconds(4);

    private readonly IServiceProvider _services;
    private readonly IEditorSelection? _selection;
    private readonly IEditorClock? _clock;
    private readonly HistoryManager? _history;
    private readonly List<LyricRowViewModel> _allRows = [];
    private bool _committing;

    // The object the tab was opened for, its element and scene. It is the target while it is in the scene.
    private KaraokeDrawable? _bound;
    private Element? _boundElement;
    private Scene? _scene;

    // Tapping state: the line being timed, its units, and the times tapped so far.
    private LyricLine? _tapLine;
    private List<(int Start, int Length)> _tapUnits = [];
    private readonly List<TimeSpan> _tapTimes = [];

    // Taps are timed in real time while playing. The playhead only moves when a rendered frame is shown, so it
    // steps by frames and falls behind the audio when rendering is slow; the time since playback started does not.
    private readonly IPreviewPlayer? _player;
    private readonly IEditorContext? _editor;
    private readonly ContextCommandDefinition? _playPause;
    private readonly IDisposable? _playingSubscription;
    private readonly IDisposable? _frameSubscription;
    private readonly Lock _playbackLock = new();
    private bool _playing;
    // The scene time playback started from, the moment on Time when the audio was there, and the last frame shown.
    // Until a frame is shown, the moment is when playback was asked for.
    private MediaTime _playStart;
    private TimeSpan _playOrigin;
    private MediaTime _lastFrame;
    private bool _anchored;
    // Whether a tap started playback, so that the tab stops it again when the line is done.
    private bool _startedPlayback;

    public KaraokeTabViewModel(ToolTabExtension extension, IServiceProvider services)
    {
        Extension = extension;
        _services = services;
        _selection = services.GetService(typeof(IEditorSelection)) as IEditorSelection;
        _clock = services.GetService(typeof(IEditorClock)) as IEditorClock;
        _history = services.GetService(typeof(HistoryManager)) as HistoryManager;
        _player = services.GetService(typeof(IPreviewPlayer)) as IPreviewPlayer;
        _editor = services as IEditorContext;
        _playPause = _editor?.Extension.ContextCommands.FirstOrDefault(command => command.Id == "editor.playPause");
        if (_player != null && _clock != null)
        {
            _playingSubscription = _player.IsPlaying.Subscribe(OnPlayingChanged);
            _frameSubscription = _clock.CurrentTime.Subscribe(OnFrameShown);
        }

        TapStartsPlayback.Subscribe(_ =>
        {
            if (TappingRow.Value != null)
                UpdateTapPrompt();
        });
    }

    public ToolTabExtension Extension { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("カラオケ字幕");

    /// <summary>Whether the tab shows the import page instead of the editor.</summary>
    public ReactivePropertySlim<bool> IsImportPageOpen { get; } = new();

    /// <summary>A problem with what was entered on the import page; results go to the history.</summary>
    public ReactivePropertySlim<string?> ImportNotice { get; } = new();

    /// <summary>The imports into the edited object, newest first.</summary>
    public ObservableCollection<ImportJobViewModel> ImportJobs { get; } = [];

    /// <summary>Why nothing is being edited, or null while <see cref="Target"/> is set.</summary>
    public ReactivePropertySlim<string?> TargetMessage { get; } = new(NoTargetMessage);

    /// <summary>Which element is being edited, for the header.</summary>
    public ReactivePropertySlim<string> TargetDescription { get; } = new("");

    /// <summary>The element that holds <see cref="Target"/>.</summary>
    public ReactivePropertySlim<Element?> TargetElement { get; } = new();

    /// <summary>The karaoke object being edited, or null when there is none or it left the scene.</summary>
    public ReactivePropertySlim<KaraokeDrawable?> Target { get; } = new();

    public ObservableCollection<LyricRowViewModel> Rows { get; } = [];

    public ReactivePropertySlim<int> TotalCount { get; } = new();

    /// <summary>What the empty list should say, or null while it shows rows.</summary>
    public ReactivePropertySlim<string?> EmptyMessage { get; } = new();

    public ReactivePropertySlim<string?> Status { get; } = new();

    /// <summary>Whether imports replace the lyrics instead of adding to them.</summary>
    public ReactivePropertySlim<bool> ReplaceOnImport { get; } = new(true);

    /// <summary>Lyrics typed or pasted into the import area.</summary>
    public ReactivePropertySlim<string> PastedText { get; } = new("");

    /// <summary>The line being timed by tapping, or null.</summary>
    public ReactivePropertySlim<LyricRowViewModel?> TappingRow { get; } = new();

    /// <summary>What to tap next, while tapping.</summary>
    public ReactivePropertySlim<string> TapPrompt { get; } = new("");

    /// <summary>How far tapping has come through the line, for highlighting it; null when not tapping.</summary>
    public ReactivePropertySlim<TapProgress?> TapState { get; } = new();

    /// <summary>Whether a tap while playback is stopped starts it instead of being timed.</summary>
    public ReactivePropertySlim<bool> TapStartsPlayback { get; } = new(true);

    /// <summary>The clock taps are timed with while playing; tests replace it.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Raised with the row now holding a line that was just added or moved, so the view can select it.</summary>
    public event Action<LyricRowViewModel>? RowFocusRequested;

    public object? GetService(Type serviceType) => _services.GetService(serviceType);

    /// <summary>Makes the tab edit <paramref name="drawable"/>, the object whose "編集" button was pressed.</summary>
    public void SetTarget(KaraokeDrawable? drawable)
    {
        if (ReferenceEquals(drawable, _bound))
            return;

        if (_scene != null)
            _scene.Children.CollectionChanged -= OnSceneChildrenChanged;

        // The history belongs to the previous object.
        ImportJobs.Clear();
        ImportNotice.Value = null;
        IsImportPageOpen.Value = false;

        _bound = drawable;
        _boundElement = drawable?.FindHierarchicalParent<Element>();
        _scene = _boundElement?.FindHierarchicalParent<Scene>();
        if (_scene != null)
            _scene.Children.CollectionChanged += OnSceneChildrenChanged;
        UpdatePresence();
    }

    public void OpenImportPage()
    {
        if (Target.Value != null)
            IsImportPageOpen.Value = true;
    }

    public void CloseImportPage() => IsImportPageOpen.Value = false;

    /// <summary>Selects the edited element on the timeline, to show which one the tab works on.</summary>
    public void SelectTargetInTimeline()
    {
        if (_selection != null && TargetElement.Value is { } element)
            _selection.Select(element);
    }

    private KaraokeLyrics CurrentLyrics => Target.Value?.Lyrics.Value ?? KaraokeLyrics.Empty;

    // ---------------------------------------------------------------- lines

    public void AddAtCurrentTime()
    {
        if (Target.Value == null)
            return;

        TimeSpan start = Playhead() ?? TimeSpan.Zero;
        var line = new LyricLine(start, start + s_newLineLength, "歌詞", [new LyricWord(0, 2, start, start + s_newLineLength)], false);
        Commit(CurrentLyrics.Add(line), "歌詞の行を追加");
        RequestFocus(start, line.Text);
    }

    public void Delete(IEnumerable<LyricRowViewModel> rows)
    {
        int[] indices = rows.Select(r => r.Index).ToArray();
        if (Target.Value != null && indices.Length > 0)
            Commit(CurrentLyrics.RemoveAt(indices), "歌詞の行を削除");
    }

    public void ClearAll()
    {
        if (Target.Value != null && CurrentLyrics.Count > 0)
            Commit(KaraokeLyrics.Empty, "歌詞をすべて削除");
    }

    /// <summary>Moves the playhead to where the line starts on the timeline.</summary>
    public void Seek(LyricRowViewModel row) => SeekTo(row.Line.Start);

    public void SeekTo(TimeSpan lyricTime)
    {
        if (_clock != null && TargetElement.Value is { } element && Target.Value is { } drawable)
            _clock.Seek(element.Start.Value + drawable.TimeOffset.Value + MediaTime.FromTimeSpan(lyricTime));
    }

    public void SetStartToPlayhead(LyricRowViewModel row)
    {
        if (Playhead() is { } time)
            row.StartText = LrcWriter.FormatTime(time);
    }

    public void SetEndToPlayhead(LyricRowViewModel row)
    {
        if (Playhead() is { } time)
            row.EndText = LrcWriter.FormatTime(time);
    }

    // ---------------------------------------------------------------- per-character timing

    /// <summary>Splits the line into units and spreads them evenly from its start to its end.</summary>
    public void DistributeEvenly(LyricRowViewModel row)
    {
        LyricLine line = row.Line;
        List<(int Start, int Length)> units = KaraokeSegmenter.Split(line.Text);
        if (units.Count == 0)
            return;

        double step = (line.End - line.Start).TotalMilliseconds / units.Count;
        var words = units.Select((u, i) => new LyricWord(
            u.Start, u.Length,
            line.Start + TimeSpan.FromMilliseconds(Math.Round(step * i)),
            line.Start + TimeSpan.FromMilliseconds(Math.Round(step * (i + 1))))).ToList();
        ReplaceLine(row, line with { Words = words, HasWordTiming = true }, "文字ごとの時刻を均等に設定");
    }

    public void ClearWordTiming(LyricRowViewModel row)
    {
        LyricLine line = row.Line;
        if (line.HasWordTiming)
            ReplaceLine(row, Untimed(line.Start, line.End, line.Text), "文字ごとの時刻を削除");
    }

    /// <summary>
    /// Starts timing the line by tapping: each <see cref="Tap"/> takes the time as the start of the next unit, and
    /// one more tap after the last unit marks where singing ends. While playing, the time is how long playback has
    /// run, not the playhead; with <see cref="TapStartsPlayback"/>, a tap while stopped starts playback instead.
    /// </summary>
    public void StartTapping(LyricRowViewModel row)
    {
        _tapLine = row.Line;
        _tapUnits = KaraokeSegmenter.Split(row.Line.Text);
        _tapTimes.Clear();
        if (_tapUnits.Count == 0)
            return;

        TappingRow.Value = row;
        UpdateTapPrompt();
    }

    public void Tap()
    {
        if (TappingRow.Value is not { } row || _tapLine == null)
            return;

        if (WaitsForPlayback)
        {
            _startedPlayback = true;
            _ = _playPause!.RunAsync(_editor, new ContextCommandExecution(_playPause.Id) { EditorContext = _editor });
            UpdateTapPrompt();
            return;
        }

        if (ToLyricTime(SceneTimeNow()) is not { } time)
            return;

        // Lyrics keep whole milliseconds. Taps never go backwards, even if the playhead was moved back meanwhile.
        time = TimeSpan.FromMilliseconds(Math.Round(time.TotalMilliseconds));
        if (_tapTimes.Count > 0 && time < _tapTimes[^1])
            time = _tapTimes[^1];
        _tapTimes.Add(time);
        if (_tapTimes.Count <= _tapUnits.Count)
        {
            UpdateTapPrompt();
            return;
        }

        LyricLine line = _tapLine;
        var words = _tapUnits.Select((u, i) => new LyricWord(u.Start, u.Length, _tapTimes[i], _tapTimes[i + 1])).ToList();
        TimeSpan start = _tapTimes[0];
        TimeSpan end = line.End > _tapTimes[^1] ? line.End : _tapTimes[^1];
        CancelTapping();
        ReplaceLine(row, new LyricLine(start, end, line.Text, words, true), "文字ごとの時刻をタップで設定");
    }

    public void CancelTapping()
    {
        TappingRow.Value = null;
        TapPrompt.Value = "";
        TapState.Value = null;
        _tapLine = null;
        _tapTimes.Clear();

        // Playback a tap started ends with the tapping.
        if (_startedPlayback)
        {
            _startedPlayback = false;
            _ = _player?.Pause();
        }
    }

    // Whether the next tap starts playback rather than being timed.
    private bool WaitsForPlayback => TapStartsPlayback.Value && _player is { IsPlaying.Value: false }
        && _playPause?.CanRun(_editor, new ContextCommandExecution(_playPause.Id) { EditorContext = _editor }) == true;

    private void UpdateTapPrompt()
    {
        if (_tapLine == null)
            return;

        int next = _tapTimes.Count;
        string text = _tapLine.Text;
        TapState.Value = next < _tapUnits.Count
            ? new TapProgress(text, _tapUnits[next].Start, _tapUnits[next].Length, next, _tapUnits.Count)
            : new TapProgress(text, text.Length, 0, next, _tapUnits.Count);
        if (WaitsForPlayback)
            TapPrompt.Value = "T で再生を開始";
        else
            TapPrompt.Value = next < _tapUnits.Count
                ? $"「{text.Substring(_tapUnits[next].Start, _tapUnits[next].Length).Trim()}」の歌い出しで打つ"
                : "歌い終わりで打つ";
    }

    private void OnPlayingChanged(bool playing)
    {
        lock (_playbackLock)
        {
            _playing = playing;
            if (playing)
            {
                _playStart = _lastFrame = _clock!.CurrentTime.Value;
                _playOrigin = Time.GetElapsedTime(0);
                _anchored = false;
            }
        }

        // Playback starts and stops on the player's threads.
        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);

        void Apply()
        {
            if (_player?.IsPlaying.Value != true)
                _startedPlayback = false;
            UpdateTapPrompt();
        }
    }

    // Each frame is shown at or after its time, so it bounds when the audio was at the start, and the earliest bound
    // is the closest. A frame before the last one means playback was moved back, so it starts again from there.
    private void OnFrameShown(MediaTime time)
    {
        lock (_playbackLock)
        {
            if (!_playing)
                return;

            TimeSpan now = Time.GetElapsedTime(0);
            if (time < _lastFrame)
            {
                _playStart = time;
                _playOrigin = now;
            }
            else if (now - (time - _playStart).ToTimeSpan() is var origin && (!_anchored || origin < _playOrigin))
            {
                _playOrigin = origin;
            }

            _anchored = true;
            _lastFrame = time;
        }
    }

    // The scene time to tap at: how long playback has run while playing, the playhead otherwise.
    private MediaTime? SceneTimeNow()
    {
        lock (_playbackLock)
        {
            if (_playing)
                return _playStart + MediaTime.FromTimeSpan(Time.GetElapsedTime(0) - _playOrigin);
        }

        return _clock?.CurrentTime.Value;
    }

    // ---------------------------------------------------------------- import / export

    /// <summary>Imports an LRC or text file into the edited object.</summary>
    public void ImportFile(string path)
    {
        if (StartJob(Path.GetFileName(path), "ファイル") is not { } job)
            return;

        List<LyricLine> lines;
        bool timed;
        try
        {
            lines = LyricsImport.ReadFile(path, out timed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(job, $"読み込めませんでした: {ex.Message}");
            return;
        }

        Import(lines, timed, "歌詞を取り込み", job);
    }

    public void ImportPastedText()
    {
        if (StartJob("貼り付けた歌詞", "貼り付け") is not { } job)
            return;

        List<LyricLine> lines = LyricsImport.Parse(PastedText.Value, out bool timed);
        if (Import(lines, timed, "歌詞を貼り付け", job))
            PastedText.Value = "";
    }

    public void ExportFile(string path)
    {
        try
        {
            // Plain UTF-8: some LRC players misread a byte order mark as part of the first tag.
            File.WriteAllText(path, LrcWriter.ToLrc(CurrentLyrics.Lines), new System.Text.UTF8Encoding(false));
            Status.Value = $"{CurrentLyrics.Count:N0} 行を書き出しました";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Value = $"書き出せませんでした: {ex.Message}";
        }
    }

    private ImportJobViewModel? StartJob(string title, string source)
    {
        if (Target.Value == null)
        {
            Status.Value = ImportNotice.Value = "編集する対象がありません";
            return null;
        }

        ImportNotice.Value = null;
        var job = new ImportJobViewModel(title, source, "読み込み中…");
        ImportJobs.Insert(0, job);
        return job;
    }

    private bool Import(List<LyricLine> lines, bool timed, string commandName, ImportJobViewModel job)
    {
        if (lines.Count == 0)
        {
            Fail(job, "歌詞が見つかりませんでした");
            return false;
        }

        KaraokeLyrics current = CurrentLyrics;
        if (!ReplaceOnImport.Value && !timed && current.Count > 0)
        {
            // Untimed lines start at zero; put appended ones after the existing lyrics.
            TimeSpan shift = current.Lines[^1].End;
            lines = lines.Select(l => Untimed(l.Start + shift, l.End + shift, l.Text)).ToList();
        }

        KaraokeLyrics next = ReplaceOnImport.Value ? new KaraokeLyrics(lines) : current.AddRange(lines);
        if (!next.Equals(current))
            Commit(next, commandName);

        // The result goes to the history entry, and to the status line the editor shows.
        string message = string.Create(CultureInfo.InvariantCulture,
            $"{lines.Count:N0} 行を{(ReplaceOnImport.Value ? "取り込みました" : "追加しました")}");
        if (!timed)
            message += $"（時刻なし: {LyricsImport.PlainLineSpacing.TotalSeconds:0} 秒間隔で配置）";
        job.Finish(ImportJobState.Succeeded, message);
        Status.Value = message;
        return true;
    }

    private void Fail(ImportJobViewModel job, string message)
    {
        job.Finish(ImportJobState.Failed, message);
        Status.Value = message;
    }

    // ---------------------------------------------------------------- edits from the rows

    internal void OnRowEdited(LyricRowViewModel row, string property)
    {
        if (_committing || Target.Value == null || row.Index >= CurrentLyrics.Count)
            return;

        LyricLine old = CurrentLyrics.Lines[row.Index];
        LyricLine? updated = property switch
        {
            nameof(LyricRowViewModel.StartText) => WithStart(old, row.StartText),
            nameof(LyricRowViewModel.EndText) => LrcWriter.TryParseTime(row.EndText, out TimeSpan end) ? old with { End = end } : null,
            nameof(LyricRowViewModel.SungEndText) => WithSungEnd(old, row.SungEndText),
            nameof(LyricRowViewModel.Text) => WithText(old, row.Text),
            _ => null,
        };

        if (updated == null)
        {
            Status.Value = "読み取れない値を元に戻しました";
            row.Load(row.Index, old);
            return;
        }

        ReplaceLine(row, updated, "歌詞を編集");
    }

    internal void OnSegmentEdited(LyricRowViewModel row, LyricSegmentViewModel segment)
    {
        if (_committing || Target.Value == null || row.Index >= CurrentLyrics.Count)
            return;

        LyricLine old = CurrentLyrics.Lines[row.Index];
        if (!old.HasWordTiming || segment.Index >= old.Words.Count || !LrcWriter.TryParseTime(segment.TimeText, out TimeSpan time))
        {
            Status.Value = "読み取れない値を元に戻しました";
            row.Load(row.Index, old);
            return;
        }

        // A run starts no earlier than the one before it and no later than the one after it.
        var words = old.Words.ToList();
        int k = segment.Index;
        TimeSpan min = k > 0 ? words[k - 1].Start : TimeSpan.Zero;
        TimeSpan max = k + 1 < words.Count ? words[k + 1].Start : words[k].End;
        time = time < min ? min : time > max ? max : time;
        words[k] = words[k] with { Start = time };
        if (k > 0)
            words[k - 1] = words[k - 1] with { End = time };
        ReplaceLine(row, old with { Words = words }, "文字ごとの時刻を編集");
    }

    private static LyricLine? WithStart(LyricLine old, string text)
    {
        if (!LrcWriter.TryParseTime(text, out TimeSpan start))
            return null;

        // Moving the start moves the whole line, timed runs included.
        TimeSpan delta = start - old.Start;
        TimeSpan end = old.End > start ? old.End : start + s_newLineLength;
        if (!old.HasWordTiming)
            return Untimed(start, end, old.Text);

        var words = old.Words.Select(w => w with { Start = Clamp(w.Start + delta), End = Clamp(w.End + delta) }).ToList();
        return new LyricLine(start, end > words[^1].End ? end : words[^1].End, old.Text, words, true);
    }

    private static LyricLine? WithSungEnd(LyricLine old, string text)
    {
        if (!old.HasWordTiming)
            return old;
        if (!LrcWriter.TryParseTime(text, out TimeSpan sungEnd))
            return null;

        var words = old.Words.ToList();
        words[^1] = words[^1] with { End = sungEnd < words[^1].Start ? words[^1].Start : sungEnd };
        return old with { Words = words };
    }

    private LyricLine? WithText(LyricLine old, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (!old.HasWordTiming)
            return Untimed(old.Start, old.End, text);

        // Keep the timing when the new text splits into as many units; otherwise it no longer fits.
        List<(int Start, int Length)> units = KaraokeSegmenter.Split(text);
        if (units.Count == old.Words.Count)
        {
            var words = units.Select((u, i) => new LyricWord(u.Start, u.Length, old.Words[i].Start, old.Words[i].End)).ToList();
            return new LyricLine(old.Start, old.End, text, words, true);
        }

        Status.Value = "区切りが変わったため、文字ごとの時刻を消しました";
        return Untimed(old.Start, old.End, text);
    }

    private static LyricLine Untimed(TimeSpan start, TimeSpan end, string text) =>
        new(start, end, text, [new LyricWord(0, text.Length, start, end)], false);

    private static TimeSpan Clamp(TimeSpan time) => time < TimeSpan.Zero ? TimeSpan.Zero : time;

    private void ReplaceLine(LyricRowViewModel row, LyricLine updated, string commandName)
    {
        KaraokeLyrics current = CurrentLyrics;
        if (row.Index >= current.Count)
            return;

        KaraokeLyrics next = current.Replace(row.Index, updated);
        if (next.Equals(current))
        {
            // Nothing changed after normalization (for example, a time clamped back); show the stored values.
            row.Load(row.Index, current.Lines[row.Index]);
            return;
        }

        Commit(next, commandName);
        // A start edit can move the line to another row; let the view follow it.
        RequestFocus(KaraokeLyrics.Normalize(updated, null).Start, updated.Text.Trim());
    }

    private void RequestFocus(TimeSpan start, string text)
    {
        if (_allRows.FirstOrDefault(r => r.Line.Start == start && r.Line.Text == text) is { } row)
            RowFocusRequested?.Invoke(row);
    }

    private TimeSpan? Playhead() => ToLyricTime(_clock?.CurrentTime.Value);

    private TimeSpan? ToLyricTime(MediaTime? sceneTime)
    {
        if (sceneTime is not { } scene || TargetElement.Value is not { } element || Target.Value is not { } drawable)
            return null;

        TimeSpan time = (scene - element.Start.Value - drawable.TimeOffset.Value).ToTimeSpan();
        return time < TimeSpan.Zero ? TimeSpan.Zero : time;
    }

    private void Commit(KaraokeLyrics lyrics, string commandName)
    {
        if (Target.Value is not { } drawable)
            return;

        _committing = true;
        try
        {
            drawable.Lyrics.Value = lyrics;
            _history?.Commit(commandName);
        }
        finally
        {
            _committing = false;
        }

        SyncRows();
    }

    // ---------------------------------------------------------------- selection and rows

    private void OnSceneChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdatePresence();

    // Deleting the element hides the editor; undoing the deletion brings it back.
    private void UpdatePresence()
    {
        bool present = _bound != null && _boundElement != null && _scene != null
                       && _scene.Children.Contains(_boundElement)
                       && ReferenceEquals(_bound.FindHierarchicalParent<Element>(), _boundElement);
        ApplyTarget(present ? _bound : null, present ? _boundElement : null);
        TargetMessage.Value = present ? null : _bound == null ? NoTargetMessage : MissingTargetMessage;
        if (!present)
            IsImportPageOpen.Value = false;
    }

    private void ApplyTarget(KaraokeDrawable? drawable, Element? element)
    {
        if (!ReferenceEquals(drawable, Target.Value))
        {
            CancelTapping();
            if (Target.Value is { } previous)
                previous.Lyrics.ValueChanged -= OnLyricsChanged;

            TargetElement.Value = element;
            Target.Value = drawable;
            if (drawable != null)
                drawable.Lyrics.ValueChanged += OnLyricsChanged;

            _allRows.Clear();
            SyncRows();
        }

        TargetDescription.Value = element == null ? ""
            : !string.IsNullOrWhiteSpace(element.Name.Value) ? $"要素「{element.Name.Value}」"
            : $"{LrcWriter.FormatTime(element.Start.Value.ToTimeSpan())} から始まる要素";
    }

    private void OnLyricsChanged(object? sender, PropertyValueChangedEventArgs<KaraokeLyrics> e)
    {
        // Undo and redo change the lyrics from outside the tab.
        if (Dispatcher.UIThread.CheckAccess())
            SyncRows();
        else
            Dispatcher.UIThread.Post(SyncRows);
    }

    private void SyncRows()
    {
        KaraokeLyrics lyrics = CurrentLyrics;
        if (lyrics.Count == _allRows.Count)
        {
            // Update in place so the list keeps its scroll position and focus.
            for (int i = 0; i < lyrics.Count; i++)
            {
                if (!ReferenceEquals(_allRows[i].Line, lyrics.Lines[i]) || _allRows[i].Index != i)
                    _allRows[i].Load(i, lyrics.Lines[i]);
            }
        }
        else
        {
            _allRows.Clear();
            for (int i = 0; i < lyrics.Count; i++)
                _allRows.Add(new LyricRowViewModel(this, i, lyrics.Lines[i]));
        }

        if (!_allRows.SequenceEqual(Rows))
        {
            Rows.Clear();
            foreach (LyricRowViewModel row in _allRows)
                Rows.Add(row);
        }

        if (TappingRow.Value is { } tapping && !_allRows.Contains(tapping))
            CancelTapping();

        TotalCount.Value = _allRows.Count;
        EmptyMessage.Value = Target.Value == null || _allRows.Count > 0
            ? null
            : "歌詞がありません";
    }

    public void WriteToJson(JsonObject json)
    {
        json[nameof(ReplaceOnImport)] = ReplaceOnImport.Value;
        json[nameof(TapStartsPlayback)] = TapStartsPlayback.Value;
        // A tab restored with the layout finds its object again by id.
        if (_bound != null)
            json["Target"] = _bound.Id.Value.ToString();
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue(nameof(ReplaceOnImport), out JsonNode? node) && node is JsonValue value
                                                                                  && value.TryGetValue(out bool replace))
        {
            ReplaceOnImport.Value = replace;
        }

        if (json.TryGetPropertyValue(nameof(TapStartsPlayback), out JsonNode? tapNode) && tapNode is JsonValue tapValue
                                                                                      && tapValue.TryGetValue(out bool startsPlayback))
        {
            TapStartsPlayback.Value = startsPlayback;
        }

        if (json.TryGetPropertyValue("Target", out JsonNode? target) && target is JsonValue targetValue
            && targetValue.TryGetValue(out string? text) && Guid.TryParse(text, out Guid id)
            && _services.GetService(typeof(Scene)) is Scene scene)
        {
            SetTarget(Find(scene, id));
        }

        static KaraokeDrawable? Find(IHierarchical node, Guid id)
        {
            foreach (IHierarchical child in node.HierarchicalChildren)
            {
                if (child is KaraokeDrawable drawable && drawable.Id.Value == id)
                    return drawable;
                if (Find(child, id) is { } found)
                    return found;
            }

            return null;
        }
    }

    public void Dispose()
    {
        _playingSubscription?.Dispose();
        _frameSubscription?.Dispose();
        if (_scene != null)
            _scene.Children.CollectionChanged -= OnSceneChildrenChanged;
        if (Target.Value is { } drawable)
            drawable.Lyrics.ValueChanged -= OnLyricsChanged;
    }
}

/// <summary>Tapping progress through a line: the characters already timed and the unit to tap next.</summary>
public sealed record TapProgress(string Text, int DoneChars, int CurrentLength, int Tapped, int Total);
