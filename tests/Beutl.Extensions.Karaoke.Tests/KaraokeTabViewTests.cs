using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.Extensions.Karaoke.Editing;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.PropertyAdapters;

namespace Beutl.Extensions.Karaoke.Tests;

// Drives the tool tab view and the property panel's summary in a headless Avalonia session.
public class KaraokeTabViewTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static T[] Find<T>(Control root)
        where T : Control => root.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible).ToArray();

    private static (Window Window, KaraokeTabView View) Open(EditorFixture f, bool select = true, double width = 1300)
    {
        var view = new KaraokeTabView { DataContext = f.ViewModel };
        var window = new Window { Width = width, Height = 760, Content = view };
        window.Show();
        if (select)
            f.ViewModel.SetTarget(f.Drawable);
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static ListBox List(Control view) => Find<ListBox>(view).Single();

    private static void Select(Control view, LyricRowViewModel row)
    {
        // Like a click: the selection moves straight to the row.
        List(view).SelectedItem = row;
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox Editor(Control view, string text) => Find<TextBox>(view).Single(t => t.Text == text);

    // Icon buttons carry their name as the tooltip, like Beutl's own tool bars.
    private static Button ByTip(Control view, string name) =>
        Find<Button>(view).Single(b => ToolTip.GetTip(b) is string tip && tip.StartsWith(name, StringComparison.Ordinal));

    private static ScrollViewer[] DetailPane(Control view) =>
        Find<ScrollViewer>(view).Where(s => s.Content is Border b && b.Classes.Contains("options-group")).ToArray();

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Control view, string content)
    {
        Find<Button>(view).Single(b => b.Content as string == content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTest]
    public void TabExtensionCreatesTheViewAndItsContext()
    {
        using var f = new EditorFixture();
        var extension = new KaraokeTabExtension();

        Assert.That(extension.TryCreateContent(f.Context, out Control? control), Is.True);
        Assert.That(extension.TryCreateContext(f.Context, out IToolContext? context), Is.True);
        Assert.That(control, Is.TypeOf<KaraokeTabView>());
        Assert.That(context!.Header.Value, Is.EqualTo("カラオケ字幕"));
        Assert.That(extension.Header, Is.Null, "Not in the add-tab menu; the tab opens from an object's 編集 button.");
        context.Dispose();
    }

    [AvaloniaTest]
    public void ExplainsWhatToDoWithoutATarget()
    {
        using var f = new EditorFixture();
        (_, KaraokeTabView view) = Open(f, select: false);

        Assert.That(Find<TextBlock>(view).Select(t => t.Text).OfType<string>(), Does.Contain("「編集」から開いてください"));
        Assert.That(Find<Button>(view).Where(b => ToolTip.GetTip(b) as string == "歌詞を取り込む"), Is.Empty);

        f.ViewModel.SetTarget(f.Drawable);
        Dispatcher.UIThread.RunJobs();

        Assert.That(Find<ListBoxItem>(view), Has.Length.EqualTo(3));
        Assert.That(Find<TextBlock>(view).Select(t => t.Text), Is.SupersetOf(new[] { "カラオケ字幕", "3 行", "いち", "にい", "文字ごと" }));
        Assert.That(ToolTip.GetTip(ByTip(view, "タイムラインで選択")), Is.EqualTo("タイムラインで選択（0:01.000 から始まる要素）"));
        Click(ByTip(view, "タイムラインで選択"));
        Assert.That(f.Selection.SelectedObject.Value, Is.SameAs(f.Element));
    }

    [AvaloniaTest]
    public void TheImportPageTakesTheWholeTabAndGoesBack()
    {
        using var f = new EditorFixture("");
        (Window window, KaraokeTabView view) = Open(f);
        // The import is offered in the tool bar and in the empty list.
        Assert.That(Find<Button>(view).Count(b => b.Content as string == "取り込む…"), Is.EqualTo(1));

        Click(ByTip(view, "歌詞を取り込む"));
        Assert.That(Find<ListBox>(view), Is.Empty);
        Assert.That(Find<TextBlock>(view).Select(t => t.Text), Is.SupersetOf(new[] { "歌詞を取り込む", "貼り付け", "ファイル", "既存の歌詞" }));
        Assert.That(Find<TextBlock>(view).Select(t => t.Text), Does.Not.Contain("履歴"));

        f.ViewModel.PastedText.Value = "一行目\n二行目";
        Click(view, "取り込む");

        Assert.That(f.Texts, Is.EqualTo(new[] { "一行目", "二行目" }));
        Assert.That(Find<TextBlock>(view).Select(t => t.Text).OfType<string>(), Has.Some.StartsWith("2 行を取り込みました"));
        Assert.That(Find<TextBlock>(view).Select(t => t.Text), Is.SupersetOf(new[] { "履歴", "完了", "貼り付けた歌詞" }));

        Click(ByTip(view, "戻る"));
        Assert.That(Find<ListBoxItem>(view), Has.Length.EqualTo(2));

        f.ViewModel.OpenImportPage();
        Dispatcher.UIThread.RunJobs();
        Find<Button>(view).Single(b => b.Content as string == "ファイルを選ぶ…").Focus();
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(f.ViewModel.IsImportPageOpen.Value, Is.False);
    }

    [AvaloniaTest]
    public void AShortNarrowTabKeepsTheListAndScrollsTheEditor()
    {
        // The reported case: a narrow dock with a line selected, short enough that the editor cannot fit.
        using var f = new EditorFixture();
        var view = new KaraokeTabView { DataContext = f.ViewModel };
        var window = new Window { Width = 630, Height = 520, Content = view };
        window.Show();
        f.ViewModel.SetTarget(f.Drawable);
        Dispatcher.UIThread.RunJobs();
        // The timed line, whose beat chips make the editor tall.
        Select(view, f.ViewModel.Rows[1]);

        Assert.That(List(view).Bounds.Height, Is.GreaterThanOrEqualTo(100));
        ScrollViewer editor = DetailPane(view).Single();
        Assert.That(editor.Extent.Height, Is.GreaterThan(editor.Viewport.Height));
        Assert.That(editor.Bounds.Bottom, Is.LessThanOrEqualTo(view.Bounds.Height));
    }

    [AvaloniaTest]
    public void ATimedLineShowsItsBeatsAsChips()
    {
        using var f = new EditorFixture();
        (_, KaraokeTabView view) = Open(f);

        Select(view, f.ViewModel.Rows[1]);

        Assert.That(Find<TextBox>(view).Select(t => t.Text), Is.SupersetOf(new[] { "0:03.000", "0:05.000", "にい", "0:03.500", "0:04.000" }));
        Assert.That(Find<Button>(view).Select(b => b.Content), Does.Contain("消す"));

        Select(view, f.ViewModel.Rows[0]);
        Assert.That(Find<TextBlock>(view).Select(t => t.Text), Does.Contain("未設定"));
        Assert.That(Find<Button>(view).Select(b => b.Content), Does.Not.Contain("消す"));
    }

    [AvaloniaTest]
    public void EnterCommitsAnEdit()
    {
        using var f = new EditorFixture();
        (Window window, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[0]);

        TextBox box = Editor(view, "いち");
        box.Focus();
        box.Text = "いちばん";
        Assert.That(f.Texts[0], Is.EqualTo("いち"));

        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.That(f.Texts[0], Is.EqualTo("いちばん"));
    }

    [AvaloniaTest]
    public void AChipTimeIsCommittedWithEnter()
    {
        using var f = new EditorFixture();
        (Window window, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[1]);

        TextBox chip = Editor(view, "0:03.500");
        chip.Focus();
        chip.Text = "0:03.700";
        Press(window, Key.Enter, PhysicalKey.Enter);

        Assert.That(f.Lines[1].Words[1].Start, Is.EqualTo(S(3.7)));
    }

    [AvaloniaTest]
    public void ClickingABeatSeeksToIt()
    {
        using var f = new EditorFixture();
        (_, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[1]);

        Find<Button>(view).Single(b => (b.Content as TextBlock)?.Text == "い").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // い starts at 3.5 s in a lyric that starts with the element at 1 s.
        Assert.That(f.Clock.CurrentTime.Value, Is.EqualTo(S(4.5)));
    }

    [AvaloniaTest]
    public void SwitchingTheSelectionSavesWhatWasTyped()
    {
        using var f = new EditorFixture();
        (_, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[0]);

        TextBox box = Editor(view, "いち");
        box.Focus();
        box.Text = "入力中";
        Select(view, f.ViewModel.Rows[2]);

        Assert.That(f.Texts, Is.EqualTo(new[] { "入力中", "にい", "さん" }));
        Assert.That(Find<TextBox>(view).Select(t => t.Text), Does.Contain("さん"));
    }

    [AvaloniaTest]
    public void TheTKeyTapsTheBeats()
    {
        using var f = new EditorFixture();
        (Window window, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[2]);
        Click(view, "タップで付ける");

        Assert.That(Find<Button>(view).Select(b => b.Content), Does.Contain("打つ (T)"));
        Assert.That(Find<TextBlock>(view).Select(t => t.Text).OfType<string>(), Has.Some.Contains("「さ」"));
        Find<Button>(view).Single(b => b.Content as string == "打つ (T)").Focus();

        foreach (double time in new[] { 6.2, 6.6, 7.5 })
        {
            f.Clock.CurrentTime.Value = S(time);
            Press(window, Key.T, PhysicalKey.T);
        }

        Assert.That(f.Lines[2].Words.Select(w => (w.Start.TotalSeconds, w.End.TotalSeconds)), Is.EqualTo(new[] { (5.2, 5.6), (5.6, 6.5) }));
        Assert.That(Find<Button>(view).Select(b => b.Content), Does.Not.Contain("打つ (T)"));
    }

    [AvaloniaTest]
    public void TheFirstTKeyStartsPlaybackUnlessTurnedOff()
    {
        using var f = new EditorFixture(withPlayer: true);
        (Window window, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[2]);
        Click(view, "タップで付ける");

        CheckBox startsPlayback = Find<CheckBox>(view).Single(c => c.Content as string == "最初のタップで再生");
        Assert.That(startsPlayback.IsChecked, Is.True);
        Assert.That(Find<TextBlock>(view).Select(t => t.Text), Does.Contain("T で再生を開始"));
        Find<Button>(view).Single(b => b.Content as string == "打つ (T)").Focus();
        Press(window, Key.T, PhysicalKey.T);
        Assert.That(f.Player!.Playing.Value, Is.True);
        Assert.That(f.ViewModel.TapState.Value!.Tapped, Is.Zero);

        f.Player.Playing.Value = false;
        startsPlayback.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.That(f.ViewModel.TapStartsPlayback.Value, Is.False);
        Assert.That(Find<TextBlock>(view).Select(t => t.Text).OfType<string>(), Has.Some.Contains("「さ」"));
        Press(window, Key.T, PhysicalKey.T);
        Assert.That(f.Player.Playing.Value, Is.False);
        Assert.That(f.ViewModel.TapState.Value!.Tapped, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void SelectingAnotherLineAbandonsTapping()
    {
        using var f = new EditorFixture();
        (_, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[2]);
        Click(view, "タップで付ける");

        Select(view, f.ViewModel.Rows[0]);

        Assert.That(f.ViewModel.TappingRow.Value, Is.Null);
        Assert.That(Find<Button>(view).Select(b => b.Content), Does.Contain("タップで付ける"));
    }

    [AvaloniaTest]
    public void AStartEditKeepsTheEditedLineSelected()
    {
        using var f = new EditorFixture();
        (Window window, KaraokeTabView view) = Open(f);
        Select(view, f.ViewModel.Rows[0]);

        TextBox start = Editor(view, "0:01.000");
        start.Focus();
        start.Text = "0:08";
        Press(window, Key.Enter, PhysicalKey.Enter);

        Assert.That(f.Texts, Is.EqualTo(new[] { "にい", "さん", "いち" }));
        Assert.That(List(view).SelectedItems!.Cast<LyricRowViewModel>().Single().Text, Is.EqualTo("いち"));
    }

    [AvaloniaTest]
    [TestCase(1300, true)]
    [TestCase(560, false)]
    public void TheEditorSitsBesideTheListWhenThereIsRoom(double width, bool beside)
    {
        using var f = new EditorFixture();
        (Window window, KaraokeTabView view) = Open(f, width: width);
        Select(view, f.ViewModel.Rows[0]);

        ListBox list = List(view);
        ScrollViewer title = DetailPane(view).Single();
        Point listOrigin = list.TranslatePoint(default, window)!.Value;
        Point titleOrigin = title.TranslatePoint(default, window)!.Value;

        if (beside)
            Assert.That(titleOrigin.X, Is.GreaterThan(listOrigin.X + list.Bounds.Width));
        else
            Assert.That(titleOrigin.Y, Is.GreaterThan(listOrigin.Y + list.Bounds.Height));
    }

    private static Control CreateLyricsEditor(KaraokeDrawable drawable, IServiceProvider? services)
    {
        var extension = new LyricsPropertyEditorExtension();
        List<IPropertyAdapter> adapters = drawable.Properties.Select(p => (IPropertyAdapter)Activator.CreateInstance(
            typeof(EnginePropertyAdapter<>).MakeGenericType(p.ValueType), p, drawable)!).ToList();

        IPropertyAdapter[] claimed = adapters.Where(a => extension.MatchProperty([a]).Any()).ToArray();
        Assert.That(claimed.Select(a => a.GetEngineProperty()!.Name), Is.EqualTo(new[] { nameof(KaraokeDrawable.Lyrics) }));
        Assert.That(extension.TryCreateContext(claimed, out IPropertyEditorContext? context), Is.True);
        if (services != null)
            context!.Accept(new Visitor(services));
        Assert.That(extension.TryCreateControl(context!, out Control? control), Is.True);
        return control!;
    }

    [AvaloniaTest]
    public void PropertyPanelShowsTheLineCountAndOpensTheTab()
    {
        using var f = new EditorFixture();
        Control control = CreateLyricsEditor(f.Drawable, f.Context);
        var window = new Window { Width = 400, Height = 200, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(Find<TextBlock>(control).Select(t => t.Text), Is.SupersetOf(new[] { "歌詞", "3 行" }));
        Button edit = Find<Button>(control).Single();
        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.That(f.Context.OpenRequests, Has.Count.EqualTo(2));
        var tab = (KaraokeTabViewModel)f.Context.OpenRequests.Distinct().Single();
        Assert.That(tab.Target.Value, Is.SameAs(f.Drawable));

        // 編集 on another object switches the open tab to that object.
        KaraokeDrawable other = f.AddKaraoke("[00:01.00]べつ\n[00:02.00]\n", S(20));
        Control otherControl = CreateLyricsEditor(other, f.Context);
        new Window { Width = 400, Height = 200, Content = otherControl }.Show();
        Dispatcher.UIThread.RunJobs();
        Find<Button>(otherControl).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(f.Context.OpenRequests.Distinct().Single(), Is.SameAs(tab));
        Assert.That(tab.Target.Value, Is.SameAs(other));
        tab.Dispose();

        f.Drawable.Lyrics.CurrentValue = KaraokeLyrics.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.That(Find<TextBlock>(control).Select(t => t.Text), Does.Contain("0 行"));
    }

    private sealed class Visitor(IServiceProvider services) : IPropertyEditorContextVisitor, IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IEditorContext) ? services as IEditorContext ?? services.GetService(serviceType) : services.GetService(serviceType);

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
