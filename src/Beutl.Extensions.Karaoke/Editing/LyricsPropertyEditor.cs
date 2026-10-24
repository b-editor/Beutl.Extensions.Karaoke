using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Beutl.Extensibility;
using Beutl.Extensions.Karaoke.Lyrics;

namespace Beutl.Extensions.Karaoke.Editing;

/// <summary>Shows how many lines the lyrics have; they are edited in the "カラオケ字幕" tool tab.</summary>
[Export]
public sealed class LyricsPropertyEditorExtension : PropertyEditorExtension
{
    public override string Name => "Karaoke lyrics editor";

    public override string DisplayName => "カラオケ字幕の歌詞";

    protected override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.Register<KaraokeLyrics, Context>(adapter => new Context(this, adapter), context => new Summary(context));
    }

    internal sealed class Context(PropertyEditorExtension extension, IPropertyAdapter<KaraokeLyrics> adapter) : IPropertyEditorContext
    {
        public PropertyEditorExtension Extension { get; } = extension;

        public IPropertyAdapter<KaraokeLyrics> Adapter { get; } = adapter;

        /// <summary>The editor hosting the property panel, or null where there is none (settings, node graph).</summary>
        public IEditorContext? EditorContext { get; private set; }

        /// <summary>The object whose lyrics the property holds; the tab opens for exactly this object.</summary>
        public Graphics.KaraokeDrawable? Owner =>
            (Adapter as Beutl.PropertyAdapters.EnginePropertyAdapter<KaraokeLyrics>)?.Object as Graphics.KaraokeDrawable;

        public void Accept(IPropertyEditorContextVisitor visitor)
        {
            visitor.Visit(this);
            if (visitor is IServiceProvider services)
                EditorContext = services.GetService(typeof(IEditorContext)) as IEditorContext;
        }

        public void WriteToJson(JsonObject json)
        {
        }

        public void ReadFromJson(JsonObject json)
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Laid out like Beutl's own property rows: the name on the left, the value on the right, and room for the
    /// ⋮ menu the host gives its built-in editors, so the value lines up with the rows around it.
    /// </summary>
    private sealed class Summary : UserControl
    {
        // Beutl's PropertyEditor: a 4px side margin, a 4px header inset, a splitter column, a 4px value margin,
        // and a 24px menu button with a 4px right margin.
        private const double MenuColumnWidth = 28;

        private readonly Context _context;
        private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        private readonly Button _edit = new() { Content = "編集", VerticalAlignment = VerticalAlignment.Center };
        private IDisposable? _subscription;

        public Summary(Context context)
        {
            _context = context;
            Margin = new Thickness(4, 0);
            MinWidth = 200;

            var header = new TextBlock
            {
                Text = context.Adapter.DisplayName,
                Margin = new Thickness(4, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (context.Adapter.Description is { Length: > 0 } description)
                ToolTip.SetTip(header, description);

            ToolTip.SetTip(_edit, "このカラオケ字幕の歌詞を、タブで取り込み・編集・タイミング付けします");
            _edit.Click += (_, _) =>
            {
                if (_context.EditorContext is { } editor && _context.Owner is { } drawable)
                    KaraokeTabExtension.Open(editor, drawable);
            };

            var value = new Grid
            {
                Margin = new Thickness(4),
                MinWidth = 120,
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 4,
            };
            Grid.SetColumn(_edit, 1);
            value.Children.Add(_count);
            value.Children.Add(_edit);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,6,*,{MenuColumnWidth}") };
            Grid.SetColumn(value, 2);
            row.Children.Add(header);
            row.Children.Add(value);
            Content = row;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // Accept runs after the control is created, so read the editor when the row is shown.
            _edit.IsEnabled = _context.EditorContext != null && _context.Owner != null;
            _subscription = _context.Adapter.GetObservable().Subscribe(new Observer(this));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _subscription?.Dispose();
            _subscription = null;
        }

        private sealed class Observer(Summary owner) : IObserver<KaraokeLyrics?>
        {
            public void OnNext(KaraokeLyrics? value) =>
                Dispatcher.UIThread.Post(() => owner._count.Text = $"{value?.Count ?? 0} 行");

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}
