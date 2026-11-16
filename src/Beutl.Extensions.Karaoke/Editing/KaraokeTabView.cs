using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Extensions.Karaoke.Localization;
using Beutl.Extensions.Karaoke.Lyrics;
using FluentIcons.Common;
using static Beutl.Extensions.Karaoke.Editing.BeutlUi;

namespace Beutl.Extensions.Karaoke.Editing;

/// <summary>
/// The view of the "カラオケ字幕" tool tab, laid out like Beutl's own tool tabs. It has two pages: the editor (the
/// line list and the selected line with its per-character timing) and the import page, which takes the whole tab
/// like a browser's download page. Its DataContext is a <see cref="KaraokeTabViewModel"/>.
/// </summary>
public sealed class KaraokeTabView : UserControl
{
    private const double StartWidth = 84;
    private const double EndWidth = 84;
    private const double TimingWidth = 72;
    private const double DetailWidth = 440;
    private const double WideLayoutMinWidth = 880;

    // Column header and about three rows: the list never shrinks below this when stacked.
    private const double ListPaneMinHeight = 150;

    private static readonly IValueConverter s_isZero = new FuncValueConverter<int, bool>(n => n == 0);

    private static readonly IValueConverter s_isPositive = new FuncValueConverter<int, bool>(n => n > 0);

    private readonly ListBox _list;
    private readonly Button _deleteButton;
    private readonly Button _locateButton;
    private readonly Grid _body;
    private readonly Control _detailNone;
    private readonly Control _detailMulti;
    private readonly TextBlock _detailMultiText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Control _detailSingle;
    private readonly Control _tapPanel;
    private readonly Control _timingTools;
    private readonly TextBlock _tapLine = new() { FontSize = 22, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _tapCount = Secondary();
    private readonly List<IDisposable> _subscriptions = [];
    private KaraokeTabViewModel? _viewModel;
    private bool _syncingSelection;
    private bool _wide = true;
    private bool _showDetail;
    private bool _layoutApplied;

    public KaraokeTabView()
    {
        _list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<LyricRowViewModel>(BuildRow, supportsRecycling: true),
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(KaraokeTabViewModel.Rows)),
        };
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(PaddingProperty, new Thickness(8, 4)),
                new Setter(MinHeightProperty, 0d),
            },
        });
        _list.KeyDown += OnListKeyDown;
        _list.SelectionChanged += OnSelectionChanged;
        _list.DoubleTapped += OnListDoubleTapped;

        _deleteButton = IconButton(Icon.Delete, Strings.DeleteShortcut, DeleteSelected);
        _deleteButton.IsEnabled = false;
        _locateButton = IconButton(Icon.TargetArrow, Strings.SelectInTimeline, () => ViewModel?.SelectTargetInTimeline());

        _detailNone = Secondary(Strings.SelectLine);
        _detailMulti = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _detailMultiText, TextButton(Strings.Delete, DeleteSelected) },
        };
        (_detailSingle, _tapPanel, _timingTools) = BuildDetailForm();

        _body = new Grid();
        _body.Children.Add(BuildListPane());
        _body.Children.Add(new ScrollViewer
        {
            IsVisible = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = Group(new StackPanel { Spacing = 8, Children = { _detailNone, _detailMulti, _detailSingle } }),
        });
        _body[!IsVisibleProperty] = new Binding("Target.Value") { Converter = ObjectConverters.IsNotNull };

        TextBlock noTargetTitle = EmptyTitle();
        noTargetTitle[!TextBlock.TextProperty] = new Binding("TargetMessage.Value");
        StackPanel noTarget = EmptyState(Icon.MusicNote1, noTargetTitle);
        noTarget[!IsVisibleProperty] = new Binding("Target.Value") { Converter = ObjectConverters.IsNull };

        TextBlock status = Secondary();
        status.Margin = new Thickness(0, 0, 0, 8);
        status[!TextBlock.TextProperty] = new Binding("Status.Value");
        status[!IsVisibleProperty] = new Binding("Status.Value") { Converter = StringConverters.IsNotNullOrEmpty };

        var content = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
        DockPanel.SetDock(status, Dock.Top);
        content.Children.Add(status);
        content.Children.Add(new Panel { Children = { _body, noTarget } });

        var editorPage = new DockPanel { LastChildFill = true };
        Border bar = BuildEditorBar();
        DockPanel.SetDock(bar, Dock.Top);
        editorPage.Children.Add(bar);
        editorPage.Children.Add(content);
        editorPage[!IsVisibleProperty] = new Binding("IsImportPageOpen.Value") { Converter = BoolConverters.Not };

        Control importPage = BuildImportPage();
        importPage[!IsVisibleProperty] = new Binding("IsImportPageOpen.Value");

        Content = new Panel { Children = { editorPage, importPage } };

        // While tapping, T taps wherever the focus is in the tab.
        AddHandler(KeyDownEvent, OnTapKey, RoutingStrategies.Tunnel);

        ApplyLayout(wide: true, showDetail: false);
        UpdateDetail();
    }

    private KaraokeTabViewModel? ViewModel => DataContext as KaraokeTabViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel != null)
            _viewModel.RowFocusRequested -= OnRowFocusRequested;
        foreach (IDisposable subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();

        _viewModel = ViewModel;
        if (_viewModel != null)
        {
            _viewModel.RowFocusRequested += OnRowFocusRequested;
            // The tab does not follow the timeline selection; the locate button says which element it edits.
            _subscriptions.Add(_viewModel.TargetDescription.Subscribe(description =>
                ToolTip.SetTip(_locateButton, string.IsNullOrEmpty(description) ? Strings.SelectInTimeline : string.Format(Strings.SelectInTimelineDescription, description))));
            _subscriptions.Add(_viewModel.TotalCount.Subscribe(count => ApplyLayout(_wide, showDetail: count > 0)));
            _subscriptions.Add(_viewModel.TappingRow.Subscribe(_ => UpdateDetail()));
            _subscriptions.Add(_viewModel.TapState.Subscribe(UpdateTapLine));
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyLayout(e.NewSize.Width >= WideLayoutMinWidth, _showDetail);
    }

    // Side by side when there is room (the tab docks under the timeline by default), stacked when narrow.
    private void ApplyLayout(bool wide, bool showDetail)
    {
        if (_layoutApplied && _wide == wide && _showDetail == showDetail)
            return;

        _layoutApplied = true;
        _wide = wide;
        _showDetail = showDetail;
        Control list = _body.Children[0];
        Control detail = _body.Children[1];
        detail.IsVisible = showDetail;
        if (wide)
        {
            _body.RowDefinitions = [];
            _body.ColumnDefinitions = new ColumnDefinitions(showDetail ? $"*,8,{DetailWidth}" : "*,0,0");
            Grid.SetRow(detail, 0);
            Grid.SetColumn(detail, 2);
        }
        else
        {
            // The list and the editor share the height and each scrolls; an Auto editor row would take all of it,
            // squeezing the list to nothing and cutting the editor off.
            _body.ColumnDefinitions = [];
            _body.RowDefinitions = showDetail
                ? [new RowDefinition(2, GridUnitType.Star) { MinHeight = ListPaneMinHeight }, new RowDefinition(8, GridUnitType.Pixel), new RowDefinition(3, GridUnitType.Star)]
                : new RowDefinitions("*,0,0");
            Grid.SetRow(detail, 2);
            Grid.SetColumn(detail, 0);
        }

        Grid.SetRow(list, 0);
        Grid.SetColumn(list, 0);
    }

    // ---------------------------------------------------------------- editor page

    private Border BuildEditorBar()
    {
        TextBlock count = Secondary();
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(0, 0, 4, 0);
        count[!TextBlock.TextProperty] = new Binding("TotalCount.Value") { StringFormat = Strings.LineCount };

        var export = new MenuItem { Header = Strings.ExportLrcMenu };
        export.Click += (_, _) => ExportAsync();
        var clear = new MenuItem { Header = Strings.DeleteAll };
        clear.Click += (_, _) => ViewModel?.ClearAll();
        Button more = IconButton(Icon.MoreVertical, Strings.More, () => { });
        more.Flyout = new MenuFlyout { Items = { export, new Separator(), clear } };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                count,
                IconButton(Icon.Add, Strings.Add, () => ViewModel?.AddAtCurrentTime()),
                _deleteButton,
                IconButton(Icon.ArrowDownload, Strings.ImportLyrics, () => ViewModel?.OpenImportPage()),
                _locateButton,
                more,
            },
            [!IsVisibleProperty] = new Binding("Target.Value") { Converter = ObjectConverters.IsNotNull },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(actions, 1);
        row.Children.Add(BarTitle(Strings.KaraokeTitle));
        row.Children.Add(actions);
        return ToolBar(row);
    }

    private Control BuildListPane()
    {
        Grid header = Columns(Secondary(Strings.Start), Secondary(Strings.End), Secondary(Strings.CharacterTiming), Secondary(Strings.Lyrics));
        header.Margin = new Thickness(9, 0, 9, 4);

        TextBlock emptyTitle = EmptyTitle();
        emptyTitle[!TextBlock.TextProperty] = new Binding("EmptyMessage.Value");
        Button import = TextButton(Strings.ImportEllipsis, () => ViewModel?.OpenImportPage(), accent: true);
        import[!IsVisibleProperty] = new Binding("TotalCount.Value") { Converter = s_isZero };
        StackPanel empty = EmptyState(Icon.MusicNote1, emptyTitle, import);
        empty[!IsVisibleProperty] = new Binding("EmptyMessage.Value") { Converter = StringConverters.IsNotNullOrEmpty };

        var pane = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        pane.Children.Add(header);
        pane.Children.Add(Group(new Panel { Children = { _list, empty } }, new Thickness(0)));
        return pane;
    }

    private static Grid Columns(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{StartWidth},{EndWidth},{TimingWidth},*") };
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        return grid;
    }

    private static Control BuildRow(LyricRowViewModel row, INameScope scope)
    {
        TextBlock Cell(string path, bool secondary = false)
        {
            TextBlock cell = secondary ? Secondary() : new TextBlock();
            cell.FontSize = 14;
            cell.TextWrapping = TextWrapping.NoWrap;
            cell.VerticalAlignment = VerticalAlignment.Center;
            cell.TextTrimming = TextTrimming.CharacterEllipsis;
            cell[!TextBlock.TextProperty] = new Binding(path);
            return cell;
        }

        return Columns(
            Cell(nameof(LyricRowViewModel.StartText), secondary: true),
            Cell(nameof(LyricRowViewModel.EndText), secondary: true),
            Cell(nameof(LyricRowViewModel.TimingName), secondary: true),
            Cell(nameof(LyricRowViewModel.Text)));
    }

    private (Control Form, Control TapPanel, Control TimingTools) BuildDetailForm()
    {
        Grid form = Rows();

        TextBox start = Field(nameof(LyricRowViewModel.StartText));
        start.Width = 100;
        AddRow(form, 0, Strings.Start, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                start,
                IconButton(Icon.ClockArrowDownload, Strings.SetToPlayhead, () => WithRow(r => ViewModel?.SetStartToPlayhead(r))),
                IconButton(Icon.ArrowStepIn, Strings.SeekToTime, () => WithRow(r => ViewModel?.Seek(r))),
            },
        });

        TextBox end = Field(nameof(LyricRowViewModel.EndText));
        end.Width = 100;
        AddRow(form, 1, Strings.End, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                end,
                IconButton(Icon.ClockArrowDownload, Strings.SetToPlayhead, () => WithRow(r => ViewModel?.SetEndToPlayhead(r))),
            },
        });

        AddRow(form, 2, Strings.Lyrics, Field(nameof(LyricRowViewModel.Text)));

        // Per-character timing: the runs as chips with their times, or a note that there are none.
        var segments = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }),
            ItemTemplate = new FuncDataTemplate<LyricSegmentViewModel>(BuildSegmentChip, supportsRecycling: false),
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(LyricRowViewModel.Segments)),
        };
        TextBox sungEnd = Field(nameof(LyricRowViewModel.SungEndText));
        sungEnd.Width = 88;
        ToolTip.SetTip(sungEnd, Strings.SungEnd);
        TextBlock sungEndLabel = Secondary(Strings.SungEnd);
        sungEndLabel.VerticalAlignment = VerticalAlignment.Center;
        var timed = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                segments,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sungEndLabel, sungEnd } },
            },
            [!IsVisibleProperty] = new Binding(nameof(LyricRowViewModel.HasWordTiming)),
        };
        TextBlock untimed = Secondary(Strings.NotSet);
        untimed.Margin = new Thickness(0, 6, 0, 0);
        untimed[!IsVisibleProperty] = new Binding(nameof(LyricRowViewModel.HasWordTiming)) { Converter = BoolConverters.Not };

        Button clearTiming = TextButton(Strings.ClearTiming, () => WithRow(r => ViewModel?.ClearWordTiming(r)));
        clearTiming[!IsVisibleProperty] = new Binding(nameof(LyricRowViewModel.HasWordTiming));
        Button tap = TextButton(Strings.TapTiming, () => WithRow(r => ViewModel?.StartTapping(r)), accent: true);
        ToolTip.SetTip(tap, Strings.TapTimingTip);
        var tools = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                timed,
                untimed,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { tap, TextButton(Strings.DistributeEvenly, () => WithRow(r => ViewModel?.DistributeEvenly(r))), clearTiming },
                },
            },
        };

        var prompt = new TextBlock { TextWrapping = TextWrapping.Wrap };
        prompt.Bind(TextBlock.TextProperty, new Binding("DataContext.TapPrompt.Value") { Source = this });
        Button hit = TextButton(Strings.TapShortcut, () => ViewModel?.Tap(), accent: true);
        hit.MinWidth = 120;
        _tapCount.VerticalAlignment = VerticalAlignment.Center;
        var startsPlayback = new CheckBox
        {
            Content = Strings.TapStartsPlayback,
            [!CheckBox.IsCheckedProperty] = new Binding("DataContext.TapStartsPlayback.Value") { Source = this, Mode = BindingMode.TwoWay },
        };
        ToolTip.SetTip(startsPlayback, Strings.TapStartsPlaybackTip);
        var tapPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _tapLine,
                prompt,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { hit, TextButton(Strings.Cancel, () => ViewModel?.CancelTapping()), _tapCount } },
                startsPlayback,
            },
        };

        AddRow(form, 3, Strings.CharacterTiming, new Panel { Children = { tools, tapPanel } }, alignTop: true);
        return (form, tapPanel, tools);
    }

    private Control BuildSegmentChip(LyricSegmentViewModel segment, INameScope scope)
    {
        var character = new Button
        {
            Padding = new Thickness(6, 2),
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new TextBlock { FontSize = 18, [!TextBlock.TextProperty] = new Binding(nameof(LyricSegmentViewModel.Text)) },
        };
        WithResource(character, StyledElement.ThemeProperty, "ToolTabBarIconButtonTheme");
        ToolTip.SetTip(character, Strings.SeekToTime);
        character.Click += (_, _) =>
        {
            if (LrcWriter.TryParseTime(segment.TimeText, out TimeSpan time))
                ViewModel?.SeekTo(time);
        };

        TextBox time = Field(nameof(LyricSegmentViewModel.TimeText));
        time.Width = 84;
        time.FontSize = 12;
        time.MinHeight = 0;

        Border chip = Group(new StackPanel { Spacing = 2, Children = { character, time } }, new Thickness(4));
        chip.Margin = new Thickness(0, 0, 6, 6);
        return chip;
    }

    // The line with the timed part in the sung colour and the unit to tap next underlined.
    private void UpdateTapLine(TapProgress? state)
    {
        _tapLine.Inlines?.Clear();
        if (state == null)
            return;

        _tapLine.Inlines ??= [];
        var done = new Run(state.Text[..state.DoneChars]);
        done.Bind(TextElement.ForegroundProperty, _tapLine.GetResourceObservable("AccentTextFillColorPrimaryBrush"));
        _tapLine.Inlines.Add(done);
        if (state.CurrentLength > 0)
        {
            _tapLine.Inlines.Add(new Run(state.Text.Substring(state.DoneChars, state.CurrentLength))
            {
                FontWeight = FontWeight.Bold,
                TextDecorations = TextDecorations.Underline,
            });
        }

        var rest = new Run(state.Text[(state.DoneChars + state.CurrentLength)..]);
        rest.Bind(TextElement.ForegroundProperty, _tapLine.GetResourceObservable("TextFillColorTertiaryBrush"));
        _tapLine.Inlines.Add(rest);
        _tapCount.Text = string.Create(CultureInfo.InvariantCulture, $"{Math.Min(state.Tapped + 1, state.Total + 1)} / {state.Total + 1}");
    }

    private void WithRow(Action<LyricRowViewModel> action)
    {
        if (_detailSingle.DataContext is LyricRowViewModel row)
            action(row);
    }

    private static TextBox Field(string path)
    {
        var box = new TextBox
        {
            [!TextBox.TextProperty] = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
        };
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
            }
        }, RoutingStrategies.Tunnel);
        return box;
    }

    // ---------------------------------------------------------------- selection

    private IReadOnlyList<LyricRowViewModel> SelectedRows() =>
        _list.SelectedItems?.OfType<LyricRowViewModel>().ToArray() ?? [];

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;

        // The editor still shows the previous line; save what was typed into it before it switches.
        LyricRowViewModel[] wanted = SelectedRows().ToArray();
        LyricLine[] wantedLines = wanted.Select(r => r.Line).ToArray();
        CommitPendingEdits();

        // Saving can move lines between rows (a start edit re-sorts the lyrics); keep the clicked lines selected.
        if (!wanted.Select(r => r.Line).SequenceEqual(wantedLines))
        {
            _syncingSelection = true;
            try
            {
                _list.SelectedItems!.Clear();
                foreach (LyricRowViewModel row in _list.Items.OfType<LyricRowViewModel>())
                {
                    if (wantedLines.Any(l => ReferenceEquals(l, row.Line)))
                        _list.SelectedItems.Add(row);
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        // Switching lines abandons a half-done tapping.
        if (_viewModel?.TappingRow.Value is { } tapping && !SelectedRows().Contains(tapping))
            _viewModel.CancelTapping();

        UpdateDetail();
    }

    private void OnRowFocusRequested(LyricRowViewModel row)
    {
        if (_syncingSelection)
            return;

        // Raised from inside a binding update; select once that update has finished.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_list.Items.Contains(row))
                return;

            _list.SelectedItems!.Clear();
            _list.SelectedItem = row;
            _list.ScrollIntoView(row);
        });
    }

    private void UpdateDetail()
    {
        IReadOnlyList<LyricRowViewModel> rows = SelectedRows();
        _detailNone.IsVisible = rows.Count == 0;
        _detailMulti.IsVisible = rows.Count > 1;
        _detailMultiText.Text = string.Format(Strings.SelectedLineCount, rows.Count);
        _detailSingle.IsVisible = rows.Count == 1;
        _detailSingle.DataContext = rows.Count == 1 ? rows[0] : null;
        _deleteButton.IsEnabled = rows.Count > 0;

        bool tapping = rows.Count == 1 && ReferenceEquals(_viewModel?.TappingRow.Value, rows[0]);
        _tapPanel.IsVisible = tapping;
        _timingTools.IsVisible = !tapping;
    }

    private void CommitPendingEdits()
    {
        if (_detailSingle.DataContext is not LyricRowViewModel)
            return;

        _syncingSelection = true;
        try
        {
            foreach (TextBox box in _detailSingle.GetVisualDescendants().OfType<TextBox>().ToArray())
                BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void OnTapKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.T && e.KeyModifiers == KeyModifiers.None && _viewModel?.TappingRow.Value != null)
        {
            _viewModel.Tap();
            e.Handled = true;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } vm && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is LyricRowViewModel row)
            vm.Seek(row);
    }

    private void DeleteSelected()
    {
        if (ViewModel is { } vm && SelectedRows() is { Count: > 0 } rows)
            vm.Delete(rows);
    }

    // ---------------------------------------------------------------- import page

    private Control BuildImportPage()
    {
        var paste = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 140,
            PlaceholderText = Strings.PastePlaceholder,
            [!TextBox.TextProperty] = new Binding("PastedText.Value") { Mode = BindingMode.TwoWay },
        };
        Button importPasted = TextButton(Strings.Import, () => ViewModel?.ImportPastedText(), accent: true);
        importPasted.HorizontalAlignment = HorizontalAlignment.Right;
        importPasted[!IsEnabledProperty] = new Binding("PastedText.Value") { Converter = StringConverters.IsNotNullOrEmpty };

        Button file = TextButton(Strings.ChooseFile, ImportAsync);
        file.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(file, Strings.LyricsFileTypes);

        var mode = new ComboBox
        {
            ItemsSource = new[] { Strings.Replace, Strings.Append },
            MinWidth = 140,
            [!Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty] = new Binding("ReplaceOnImport.Value") { Mode = BindingMode.TwoWay, Converter = ReplaceModeConverter.Instance },
        };

        Grid sources = Rows();
        AddRow(sources, 0, Strings.Paste, new StackPanel { Spacing = 8, Children = { paste, importPasted } }, alignTop: true);
        AddRow(sources, 1, Strings.File, file);
        AddRow(sources, 2, Strings.ExistingLyrics, mode);

        TextBlock notice = Secondary();
        WithResource(notice, TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        notice[!TextBlock.TextProperty] = new Binding("ImportNotice.Value");
        notice[!IsVisibleProperty] = new Binding("ImportNotice.Value") { Converter = StringConverters.IsNotNullOrEmpty };

        TextBlock historyTitle = new() { Text = Strings.History, [!IsVisibleProperty] = new Binding("ImportJobs.Count") { Converter = s_isPositive } };
        var jobs = new ItemsControl
        {
            // Rows draw a separator above themselves; the first one tucks under the group's border.
            Margin = new Thickness(0, -1, 0, 0),
            ItemTemplate = new FuncDataTemplate<ImportJobViewModel>(BuildJob, supportsRecycling: false),
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(KaraokeTabViewModel.ImportJobs)),
        };
        Border history = Group(jobs, new Thickness(0));
        history[!IsVisibleProperty] = new Binding("ImportJobs.Count") { Converter = s_isPositive };

        return BuildPage(Strings.ImportLyrics, new StackPanel
        {
            Spacing = 12,
            Children = { Group(sources), notice, historyTitle, history },
        });
    }

    // A full-tab page with a back button, like a browser's download page.
    private Control BuildPage(string title, Control content)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        TextBlock titleText = BarTitle(title);
        Grid.SetColumn(titleText, 1);
        row.Children.Add(IconButton(Icon.ArrowLeft, Strings.BackShortcut, () => ViewModel?.CloseImportPage()));
        row.Children.Add(titleText);
        Border bar = ToolBar(row);

        var page = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        page.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(8), Child = content },
        });
        page.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ViewModel?.CloseImportPage();
                e.Handled = true;
            }
        };
        return page;
    }

    private static Control BuildJob(ImportJobViewModel job, INameScope scope)
    {
        TextBlock message = Secondary();
        message[!TextBlock.TextProperty] = new Binding(nameof(ImportJobViewModel.Message));
        TextBlock caption = Secondary();
        caption[!TextBlock.TextProperty] = new Binding(nameof(ImportJobViewModel.Caption));
        var details = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, [!TextBlock.TextProperty] = new Binding(nameof(ImportJobViewModel.Title)) },
                caption,
                message,
            },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(details);
        Border pill = StatePill(job);
        Grid.SetColumn(pill, 1);
        row.Children.Add(pill);

        var item = new Border { Padding = new Thickness(12), BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
        return WithResource(item, Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
    }

    // The coloured state label of Beutl's lists (the proxies tab, for one).
    private static Border StatePill(ImportJobViewModel job)
    {
        var text = new TextBlock { FontSize = 12 };
        var pill = new Border
        {
            Padding = new Thickness(8, 3),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Top,
            Child = text,
        };
        IDisposable? background = null;

        void Update()
        {
            (string label, string key) = job.State switch
            {
                ImportJobState.Running => (Strings.JobRunning, "SubtleFillColorSecondaryBrush"),
                ImportJobState.Succeeded => (Strings.JobSucceeded, "SystemFillColorSuccessBackgroundBrush"),
                ImportJobState.Failed => (Strings.JobFailed, "SystemFillColorCriticalBackgroundBrush"),
                _ => (Strings.JobCanceled, "SystemFillColorCautionBackgroundBrush"),
            };
            text.Text = label;
            background?.Dispose();
            background = pill.Bind(Border.BackgroundProperty, pill.GetResourceObservable(key));
        }

        Update();
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImportJobViewModel.State))
                Update();
        };
        return pill;
    }

    // ---------------------------------------------------------------- files

    private async void ImportAsync()
    {
        if (ViewModel is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.OpenLyricsFile,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.LyricsFiles) { Patterns = ["*.lrc", "*.txt"] }, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.ImportFile(path);
    }

    private async void ExportAsync()
    {
        if (ViewModel is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.ExportLyrics,
            SuggestedFileName = "lyrics.lrc",
            DefaultExtension = "lrc",
            FileTypeChoices = [new FilePickerFileType(Strings.LrcFiles) { Patterns = ["*.lrc"] }],
        });
        if (file?.TryGetLocalPath() is { } path)
            vm.ExportFile(path);
    }

    /// <summary>"置き換える" (index 0) when imports replace the lyrics, "追加する" (index 1) when they add to them.</summary>
    private sealed class ReplaceModeConverter : IValueConverter
    {
        public static readonly ReplaceModeConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is false ? 1 : 0;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int index ? index == 0 : BindingOperations.DoNothing;
    }
}
