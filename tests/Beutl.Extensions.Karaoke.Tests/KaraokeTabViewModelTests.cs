using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Extensions.Karaoke.Editing;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.ProjectSystem;

namespace Beutl.Extensions.Karaoke.Tests;

// Runs on the Avalonia UI thread, where the editor runs the tool tab. The fixture's element starts at 1 s and
// holds "いち" (1-3 s), "にい" timed per character (3-5 s, に at 3.0, い at 3.5, sung by 4.0) and "さん" (5-7 s).
public class KaraokeTabViewModelTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static string WriteFile(string content, string extension = ".lrc")
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"lyrics-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    private static EditorFixture Selected()
    {
        var f = new EditorFixture();
        f.ViewModel.SetTarget(f.Drawable);
        return f;
    }

    [AvaloniaTest]
    public void TheTabEditsTheObjectItWasOpenedFor()
    {
        using var f = new EditorFixture();
        Assert.That(f.ViewModel.Rows, Is.Empty);
        Assert.That(f.ViewModel.TargetMessage.Value, Does.Contain("「編集」"));

        f.ViewModel.SetTarget(f.Drawable);

        Assert.That(f.ViewModel.Target.Value, Is.SameAs(f.Drawable));
        Assert.That(f.ViewModel.TargetDescription.Value, Is.EqualTo("0:01.000 から始まる要素"));
        Assert.That(f.ViewModel.Rows.Select(r => r.Text), Is.EqualTo(new[] { "いち", "にい", "さん" }));
        LyricRowViewModel timed = f.ViewModel.Rows[1];
        Assert.That(timed.TimingName, Is.EqualTo("文字ごと"));
        Assert.That(timed.Segments.Select(s => (s.Text, s.TimeText)), Is.EqualTo(new[] { ("に", "0:03.000"), ("い", "0:03.500") }));
        Assert.That(timed.SungEndText, Is.EqualTo("0:04.000"));
        Assert.That(f.ViewModel.Rows[0].Segments, Is.Empty);

        // Selecting other things on the timeline does not change what the tab edits.
        KaraokeDrawable other = f.AddKaraoke("[00:01.00]べつ\n[00:02.00]\n", S(20));
        f.Selection.Select(other.FindHierarchicalParent<Element>());
        Assert.That(f.ViewModel.Target.Value, Is.SameAs(f.Drawable));

        f.ViewModel.SetTarget(other);
        Assert.That(f.ViewModel.Rows.Select(r => r.Text), Is.EqualTo(new[] { "べつ" }));
    }

    [AvaloniaTest]
    public void DeletingTheElementHidesTheEditorUntilUndo()
    {
        using EditorFixture f = Selected();
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);

        f.Scene.RemoveChild(f.Element);
        f.History.Commit("delete");
        Assert.That(f.ViewModel.Target.Value, Is.Null);
        Assert.That(f.ViewModel.TappingRow.Value, Is.Null);
        Assert.That(f.ViewModel.TargetMessage.Value, Does.Contain("削除されました"));

        Assert.That(f.History.Undo(), Is.True);
        Assert.That(f.ViewModel.Target.Value, Is.SameAs(f.Drawable));
        Assert.That(f.ViewModel.Rows, Has.Count.EqualTo(3));
    }

    [AvaloniaTest]
    public void ARestoredTabFindsItsObjectAgain()
    {
        using EditorFixture f = Selected();
        var json = new JsonObject();
        f.ViewModel.WriteToJson(json);

        using var restored = new KaraokeTabViewModel(new KaraokeTabExtension(), f.Context);
        restored.ReadFromJson(json);

        Assert.That(restored.Target.Value, Is.SameAs(f.Drawable));
    }

    [AvaloniaTest]
    public void WithoutATargetNothingIsImported()
    {
        using var f = new EditorFixture();
        f.ViewModel.PastedText.Value = "歌詞";

        f.ViewModel.ImportPastedText();

        Assert.That(f.ViewModel.Status.Value, Does.Contain("対象がありません"));
        Assert.That(f.ViewModel.PastedText.Value, Is.EqualTo("歌詞"), "kept, so it can be imported once there is a target");
        Assert.That(f.Scene.Children, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    public void TheImportPageOpensOnlyForATargetAndClosesWithIt()
    {
        using var f = new EditorFixture();
        f.ViewModel.OpenImportPage();
        Assert.That(f.ViewModel.IsImportPageOpen.Value, Is.False);

        f.ViewModel.SetTarget(f.Drawable);
        f.ViewModel.OpenImportPage();
        Assert.That(f.ViewModel.IsImportPageOpen.Value, Is.True);

        f.Scene.RemoveChild(f.Element);
        Assert.That(f.ViewModel.IsImportPageOpen.Value, Is.False);
    }

    [AvaloniaTest]
    public void ImportsAreListedNewestFirstWithTheirResults()
    {
        using EditorFixture f = Selected();

        f.ViewModel.ImportFile(WriteFile("[00:01.00]あ\n[00:02.00]\n"));
        f.ViewModel.ImportFile(Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing.lrc"));
        f.ViewModel.ReplaceOnImport.Value = false;
        f.ViewModel.PastedText.Value = "い\nう";
        f.ViewModel.ImportPastedText();

        Assert.That(f.ViewModel.ImportJobs.Select(j => (j.Title, j.Source, j.State)), Is.EqualTo(new[]
        {
            ("貼り付けた歌詞", "貼り付け", ImportJobState.Succeeded),
            ("missing.lrc", "ファイル", ImportJobState.Failed),
            (f.ViewModel.ImportJobs[2].Title, "ファイル", ImportJobState.Succeeded),
        }));
        Assert.That(f.ViewModel.ImportJobs[0].Message, Does.StartWith("2 行を追加しました（時刻なし"));
        Assert.That(f.ViewModel.ImportJobs[1].Message, Does.Contain("読み込めませんでした"));
        Assert.That(f.ViewModel.ImportJobs[2].Message, Is.EqualTo("1 行を取り込みました"));

        f.ViewModel.SetTarget(f.AddKaraoke("[00:01.00]べつ\n[00:02.00]\n", S(20)));
        Assert.That(f.ViewModel.ImportJobs, Is.Empty, "another object has its own history");
    }

    [AvaloniaTest]
    public void EditingTheTextIsOneUndoableChange()
    {
        using EditorFixture f = Selected();
        LyricRowViewModel row = f.ViewModel.Rows[0];

        row.Text = "いちばん";
        Assert.That(f.Texts[0], Is.EqualTo("いちばん"));

        Assert.That(f.History.Undo(), Is.True);
        Assert.That(f.Texts[0], Is.EqualTo("いち"));
        Assert.That(row.Text, Is.EqualTo("いち"), "Undo changes the lyrics from outside the tab; the row follows.");
    }

    [AvaloniaTest]
    public void ATextEditKeepsTheTimingWhenTheBeatsStillMatch()
    {
        using EditorFixture f = Selected();

        // "にゃん" splits into "にゃ" and "ん": two beats, like "に" and "い".
        f.ViewModel.Rows[1].Text = "にゃん";

        LyricLine line = f.Lines[1];
        Assert.That(line.HasWordTiming, Is.True);
        Assert.That(line.Words.Select(w => (line.Text.Substring(w.CharStart, w.Length), w.Start.TotalSeconds)),
            Is.EqualTo(new[] { ("にゃ", 3.0), ("ん", 3.5) }));
    }

    [AvaloniaTest]
    public void ATextEditWithOtherBeatsDropsTheTiming()
    {
        using EditorFixture f = Selected();

        f.ViewModel.Rows[1].Text = "にいさん";

        Assert.That(f.Lines[1].HasWordTiming, Is.False);
        Assert.That(f.ViewModel.Status.Value, Does.Contain("消しました"));
    }

    [AvaloniaTest]
    public void MovingTheStartMovesTheTimedRunsAndTheRow()
    {
        using EditorFixture f = Selected();
        LyricRowViewModel? focused = null;
        f.ViewModel.RowFocusRequested += row => focused = row;

        f.ViewModel.Rows[1].StartText = "0:03.200";
        LyricLine moved = f.Lines[1];
        Assert.That(moved.Words.Select(w => w.Start.TotalSeconds), Is.EqualTo(new[] { 3.2, 3.7 }));
        Assert.That(moved.SungEnd, Is.EqualTo(S(4.2)));

        // Moving "いち" after "さん" re-sorts the lyrics; the view is asked to follow it.
        f.ViewModel.Rows[0].StartText = "0:08";
        Assert.That(f.Texts, Is.EqualTo(new[] { "にい", "さん", "いち" }));
        Assert.That(focused, Is.SameAs(f.ViewModel.Rows[2]));
        Assert.That(focused!.Text, Is.EqualTo("いち"));
    }

    [AvaloniaTest]
    public void EndAndSungEndAreEditable()
    {
        using EditorFixture f = Selected();

        f.ViewModel.Rows[2].EndText = "0:09";
        f.ViewModel.Rows[1].SungEndText = "0:04.500";

        Assert.That(f.Lines[2].End, Is.EqualTo(S(9)));
        Assert.That(f.Lines[1].SungEnd, Is.EqualTo(S(4.5)));
    }

    [AvaloniaTest]
    public void ASegmentTimeStaysBetweenItsNeighbours()
    {
        using EditorFixture f = Selected();
        LyricRowViewModel row = f.ViewModel.Rows[1];

        row.Segments[1].TimeText = "0:09";
        Assert.That(f.Lines[1].Words[1].Start, Is.EqualTo(S(4.0)), "clamped to where singing ends");
        Assert.That(f.Lines[1].Words[0].End, Is.EqualTo(S(4.0)));
        Assert.That(row.Segments[1].TimeText, Is.EqualTo("0:04.000"));

        f.ViewModel.Rows[1].Segments[0].TimeText = "0:03.250";
        Assert.That(f.Lines[1].Words[0].Start, Is.EqualTo(S(3.25)));
    }

    [AvaloniaTest]
    public void UnreadableInputIsRevertedWithAMessage()
    {
        using EditorFixture f = Selected();
        KaraokeLyrics before = f.Drawable.Lyrics.Value;
        LyricRowViewModel row = f.ViewModel.Rows[1];

        row.StartText = "abc";
        row.Text = "  ";
        row.Segments[0].TimeText = "x";

        Assert.That(f.Drawable.Lyrics.Value, Is.SameAs(before));
        Assert.That(f.History.CanUndo, Is.False);
        Assert.That((row.StartText, row.Text, row.Segments[0].TimeText), Is.EqualTo(("0:03.000", "にい", "0:03.000")));
        Assert.That(f.ViewModel.Status.Value, Does.Contain("元に戻しました"));
    }

    [AvaloniaTest]
    public void DistributingSpreadsTheBeatsOverTheLine()
    {
        using EditorFixture f = Selected();

        f.ViewModel.DistributeEvenly(f.ViewModel.Rows[2]);

        LyricLine line = f.Lines[2];
        Assert.That(line.HasWordTiming, Is.True);
        Assert.That(line.Words.Select(w => (w.Start.TotalSeconds, w.End.TotalSeconds)), Is.EqualTo(new[] { (5.0, 6.0), (6.0, 7.0) }));

        f.ViewModel.ClearWordTiming(f.ViewModel.Rows[2]);
        Assert.That(f.Lines[2].HasWordTiming, Is.False);
    }

    [AvaloniaTest]
    public void TappingTimesEachBeatAndTheEndAsOneEdit()
    {
        using EditorFixture f = Selected();
        LyricRowViewModel row = f.ViewModel.Rows[2];

        f.ViewModel.StartTapping(row);
        Assert.That(f.ViewModel.TappingRow.Value, Is.SameAs(row));
        Assert.That(f.ViewModel.TapPrompt.Value, Does.Contain("「さ」"));
        Assert.That(f.ViewModel.TapState.Value, Is.EqualTo(new TapProgress("さん", 0, 1, 0, 2)));

        // The element starts at 1 s, so the playhead at 6.2 s is 5.2 s into the lyrics.
        f.Clock.Seek(MediaTime.FromSeconds(6.2));
        f.ViewModel.Tap();
        Assert.That(f.ViewModel.TapPrompt.Value, Does.Contain("「ん」"));
        f.Clock.Seek(MediaTime.FromSeconds(6.6));
        f.ViewModel.Tap();
        Assert.That(f.ViewModel.TapPrompt.Value, Does.Contain("歌い終わり"));
        Assert.That(f.Lines[2].HasWordTiming, Is.False, "Nothing changes until the last tap.");

        f.Clock.Seek(MediaTime.FromSeconds(7.5));
        f.ViewModel.Tap();

        LyricLine line = f.Lines[2];
        Assert.That(line.Start, Is.EqualTo(S(5.2)));
        Assert.That(line.Words.Select(w => (w.Start.TotalSeconds, w.End.TotalSeconds)), Is.EqualTo(new[] { (5.2, 5.6), (5.6, 6.5) }));
        Assert.That(f.ViewModel.TappingRow.Value, Is.Null);
        Assert.That(f.ViewModel.TapState.Value, Is.Null);

        Assert.That(f.History.Undo(), Is.True);
        Assert.That(f.Lines[2].HasWordTiming, Is.False, "The whole tapping is one undo step.");
    }

    [AvaloniaTest]
    public void TapsNeverGoBackwardsAndCanBeAbandoned()
    {
        using EditorFixture f = Selected();
        LyricRowViewModel row = f.ViewModel.Rows[2];

        f.ViewModel.StartTapping(row);
        f.Clock.Seek(MediaTime.FromSeconds(6.5));
        f.ViewModel.Tap();
        f.Clock.Seek(MediaTime.FromSeconds(6.0));
        f.ViewModel.Tap();
        f.Clock.Seek(MediaTime.FromSeconds(7.0));
        f.ViewModel.Tap();
        Assert.That(f.Lines[2].Words.Select(w => w.Start.TotalSeconds), Is.EqualTo(new[] { 5.5, 5.5 }));

        f.ViewModel.StartTapping(f.ViewModel.Rows[0]);
        f.ViewModel.Tap();
        f.ViewModel.CancelTapping();
        Assert.That(f.Lines[0].HasWordTiming, Is.False);
        Assert.That(f.ViewModel.TappingRow.Value, Is.Null);
    }

    [AvaloniaTest]
    public void TapsWhilePlayingAreTimedFromWhenPlaybackStarted()
    {
        using var f = new EditorFixture(withPlayer: true);
        f.ViewModel.SetTarget(f.Drawable);
        f.ViewModel.TapStartsPlayback.Value = false;
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);

        f.Clock.Seek(MediaTime.FromSeconds(6.0));
        f.Player!.Playing.Value = true;
        // The first frames are shown late, the earliest by 0.05 s, so the audio left 6.0 s 0.05 s after playback was asked for.
        f.Time.Advance(0.1);
        f.Clock.Seek(MediaTime.FromSeconds(6.05));
        f.Time.Advance(0.1);
        f.Clock.Seek(MediaTime.FromSeconds(6.1));

        // Then rendering stalls and the playhead stays at 6.1 s, while the taps follow the time since the audio started.
        f.Time.Advance(0.05);
        f.ViewModel.Tap();
        f.Time.Advance(0.4);
        f.ViewModel.Tap();
        f.Time.Advance(0.9);
        f.ViewModel.Tap();

        Assert.That(f.Lines[2].Words.Select(w => (w.Start.TotalSeconds, w.End.TotalSeconds)), Is.EqualTo(new[] { (5.2, 5.6), (5.6, 6.5) }));
        Assert.That(f.Player.Playing.Value, Is.True, "Playback the user started keeps going.");
    }

    [AvaloniaTest]
    public void TappingWhileStoppedReadsThePlayhead()
    {
        using var f = new EditorFixture(withPlayer: true);
        f.ViewModel.SetTarget(f.Drawable);
        f.ViewModel.TapStartsPlayback.Value = false;
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);

        foreach (double time in new[] { 6.2, 6.6, 7.5 })
        {
            f.Clock.Seek(MediaTime.FromSeconds(time));
            f.Time.Advance(10);
            f.ViewModel.Tap();
        }

        Assert.That(f.Lines[2].Words.Select(w => w.Start.TotalSeconds), Is.EqualTo(new[] { 5.2, 5.6 }));
        Assert.That(f.Context.Commands, Is.Empty);
    }

    [AvaloniaTest]
    public void TheFirstTapStartsPlaybackAndTheLastStopsIt()
    {
        using var f = new EditorFixture(withPlayer: true);
        f.ViewModel.SetTarget(f.Drawable);
        f.Clock.Seek(MediaTime.FromSeconds(6.0));
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);
        Assert.That(f.ViewModel.TapPrompt.Value, Is.EqualTo("T で再生を開始"));

        f.ViewModel.Tap();
        Assert.That(f.Context.Commands, Is.EqualTo(new[] { "editor.playPause" }));
        Assert.That(f.Player!.Playing.Value, Is.True);
        Assert.That(f.ViewModel.TapState.Value!.Tapped, Is.Zero, "The tap that starts playback is not timed.");
        Assert.That(f.ViewModel.TapPrompt.Value, Does.Contain("「さ」"));

        f.Time.Advance(0.2);
        f.ViewModel.Tap();
        f.Time.Advance(0.4);
        f.ViewModel.Tap();
        f.Time.Advance(0.9);
        f.ViewModel.Tap();

        Assert.That(f.Lines[2].Words.Select(w => (w.Start.TotalSeconds, w.End.TotalSeconds)), Is.EqualTo(new[] { (5.2, 5.6), (5.6, 6.5) }));
        Assert.That(f.Player.Playing.Value, Is.False, "Playback a tap started stops with the line.");
        Assert.That(f.Player.PauseCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void AfterAPauseTheNextTapResumesPlayback()
    {
        using var f = new EditorFixture(withPlayer: true);
        f.ViewModel.SetTarget(f.Drawable);
        f.Clock.Seek(MediaTime.FromSeconds(6.0));
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);
        f.ViewModel.Tap();
        f.Time.Advance(0.2);
        f.ViewModel.Tap();

        // The user pauses at 6.3 s.
        f.Clock.Seek(MediaTime.FromSeconds(6.3));
        f.Player!.Playing.Value = false;
        Assert.That(f.ViewModel.TapPrompt.Value, Is.EqualTo("T で再生を開始"));

        f.ViewModel.Tap();
        Assert.That(f.Player.Playing.Value, Is.True);
        Assert.That(f.ViewModel.TapState.Value!.Tapped, Is.EqualTo(1));
        f.Time.Advance(0.3);
        f.ViewModel.Tap();
        f.Time.Advance(0.5);
        f.ViewModel.Tap();

        Assert.That(f.Lines[2].Words.Select(w => w.Start.TotalSeconds), Is.EqualTo(new[] { 5.2, 5.6 }));
        Assert.That(f.Player.Playing.Value, Is.False);
    }

    [AvaloniaTest]
    public void AbandoningTappingStopsOnlyPlaybackATapStarted()
    {
        using var f = new EditorFixture(withPlayer: true);
        f.ViewModel.SetTarget(f.Drawable);
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);
        f.ViewModel.Tap();
        f.ViewModel.CancelTapping();
        Assert.That(f.Player!.Playing.Value, Is.False);

        f.Player.Playing.Value = true;
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);
        f.ViewModel.Tap();
        f.ViewModel.CancelTapping();
        Assert.That(f.Player.Playing.Value, Is.True);

        // A tap starts playback, then the user stops it and plays again: the playback is now the user's.
        f.Player.Playing.Value = false;
        f.ViewModel.StartTapping(f.ViewModel.Rows[2]);
        f.ViewModel.Tap();
        f.Player.Playing.Value = false;
        f.Player.Playing.Value = true;
        f.ViewModel.CancelTapping();
        Assert.That(f.Player.Playing.Value, Is.True);
        Assert.That(f.Player.PauseCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void TheTapPlaybackSettingIsSavedWithTheTab()
    {
        using EditorFixture f = Selected();
        f.ViewModel.TapStartsPlayback.Value = false;
        var json = new JsonObject();
        f.ViewModel.WriteToJson(json);

        using var restored = new KaraokeTabViewModel(new KaraokeTabExtension(), f.Context);
        Assert.That(restored.TapStartsPlayback.Value, Is.True);
        restored.ReadFromJson(json);
        Assert.That(restored.TapStartsPlayback.Value, Is.False);
    }

    [AvaloniaTest]
    public void AddPutsALineAtThePlayhead()
    {
        using EditorFixture f = Selected();
        f.Drawable.TimeOffset.Value = MediaTime.FromSeconds(0.5);
        f.Clock.Seek(MediaTime.FromSeconds(9.5));
        LyricRowViewModel? added = null;
        f.ViewModel.RowFocusRequested += row => added = row;

        f.ViewModel.AddAtCurrentTime();

        // The element starts at 1 s and the lyrics are offset by 0.5 s.
        Assert.That(f.Lines[^1].Start, Is.EqualTo(S(8)));
        Assert.That(added?.Text, Is.EqualTo("歌詞"));
    }

    [AvaloniaTest]
    public void DeleteAndClearAreUndoable()
    {
        using EditorFixture f = Selected();

        f.ViewModel.Delete([f.ViewModel.Rows[0], f.ViewModel.Rows[2]]);
        Assert.That(f.Texts, Is.EqualTo(new[] { "にい" }));
        f.ViewModel.ClearAll();
        Assert.That(f.Texts, Is.Empty);
        Assert.That(f.ViewModel.EmptyMessage.Value, Is.EqualTo("歌詞がありません"));

        f.History.Undo();
        f.History.Undo();
        Assert.That(f.ViewModel.Rows, Has.Count.EqualTo(3));
    }

    [AvaloniaTest]
    public void ImportReplacesOrAppends()
    {
        using EditorFixture f = Selected();

        f.ViewModel.ImportFile(WriteFile("[00:00.50]<00:00.50>あ<00:01.00>\n[00:02.00]い\n[00:03.00]\n"));
        Assert.That(f.Texts, Is.EqualTo(new[] { "あ", "い" }));
        Assert.That(f.Lines[0].HasWordTiming, Is.True);

        // Untimed lines added to existing lyrics go after them, 4 s apart.
        f.ViewModel.ReplaceOnImport.Value = false;
        f.ViewModel.ImportFile(WriteFile("う\nえ", ".txt"));
        Assert.That(f.Texts, Is.EqualTo(new[] { "あ", "い", "う", "え" }));
        Assert.That(f.Lines.Select(l => l.Start.TotalSeconds), Is.EqualTo(new[] { 0.5, 2.0, 3.0, 7.0 }));
        Assert.That(f.ViewModel.Status.Value, Does.Contain("4 秒間隔"));

        f.History.Undo();
        f.History.Undo();
        Assert.That(f.Texts, Is.EqualTo(new[] { "いち", "にい", "さん" }));
    }

    [AvaloniaTest]
    public void PastedLyricsAreImportedAndCleared()
    {
        using EditorFixture f = Selected();
        f.ViewModel.PastedText.Value = "一行目\n二行目\n";

        f.ViewModel.ImportPastedText();

        Assert.That(f.Texts, Is.EqualTo(new[] { "一行目", "二行目" }));
        Assert.That(f.ViewModel.PastedText.Value, Is.Empty);
    }

    [AvaloniaTest]
    public void ImportProblemsLeaveTheLyricsAlone()
    {
        using EditorFixture f = Selected();

        f.ViewModel.ImportFile(Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing.lrc"));
        Assert.That(f.ViewModel.Status.Value, Does.Contain("読み込めませんでした"));
        f.ViewModel.ImportFile(WriteFile("\n\n"));
        Assert.That(f.ViewModel.Status.Value, Does.Contain("見つかりませんでした"));

        Assert.That(f.Texts, Has.Length.EqualTo(3));
        Assert.That(f.History.CanUndo, Is.False);
    }

    [AvaloniaTest]
    public void ExportWritesLrcThatImportsBack()
    {
        using EditorFixture f = Selected();
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"export-{Guid.NewGuid():N}.lrc");

        f.ViewModel.ExportFile(path);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.That(bytes[0], Is.EqualTo((byte)'['), "no byte order mark");
        Assert.That(new KaraokeLyrics(LyricsImport.ReadFile(path, out bool timed)), Is.EqualTo(f.Drawable.Lyrics.Value));
        Assert.That(timed, Is.True);
    }

    [AvaloniaTest]
    public void PlayheadButtonsUseTheTimelinePosition()
    {
        using EditorFixture f = Selected();
        f.Drawable.TimeOffset.Value = MediaTime.FromSeconds(0.5);

        f.ViewModel.Seek(f.ViewModel.Rows[2]);
        Assert.That(f.Clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(6.5)), "さん starts at 5 s, plus the 1 s start and 0.5 s offset");

        f.Clock.Seek(MediaTime.FromSeconds(8));
        f.ViewModel.SetEndToPlayhead(f.ViewModel.Rows[2]);
        Assert.That(f.Lines[2].End, Is.EqualTo(S(6.5)));
        f.Clock.Seek(MediaTime.FromSeconds(5));
        f.ViewModel.SetStartToPlayhead(f.ViewModel.Rows[2]);
        Assert.That(f.Lines[2].Start, Is.EqualTo(S(3.5)));
    }

    [AvaloniaTest]
    public void TheReplaceSettingIsRemembered()
    {
        using var f = new EditorFixture();
        f.ViewModel.ReplaceOnImport.Value = false;
        var json = new JsonObject();

        f.ViewModel.WriteToJson(json);
        using var other = new EditorFixture();
        other.ViewModel.ReadFromJson(json);

        Assert.That(other.ViewModel.ReplaceOnImport.Value, Is.False);
    }
}
