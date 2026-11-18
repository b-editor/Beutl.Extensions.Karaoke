using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Extensions.Karaoke.Localization;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Extensions.Karaoke.Graphics;

/// <summary>
/// Shows lyrics and recolors each line from left to right as it is sung. The lyrics are stored in the object
/// and edited in the "カラオケ字幕" tool tab. Lyric times are measured from the start of the object.
/// </summary>
/// <remarks>The original duration lasts until the last line has left the screen.</remarks>
[Display(Name = nameof(Strings.KaraokeTitle), ResourceType = typeof(Strings))]
public sealed partial class KaraokeDrawable : Drawable, IOriginalDurationProvider, ISplittable
{
    public KaraokeDrawable()
    {
        ScanProperties<KaraokeDrawable>();

        TextFill.Value = new SolidColorBrush(Colors.White);
        TextStroke.Value = CreatePen(Color.FromArgb(255, 0x10, 0x20, 0x60), 8);
        SungFill.Value = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x8B, 0xFF));
        SungStroke.Value = CreatePen(Colors.White, 8);
    }

    [Display(Name = nameof(Strings.Lyrics), Description = nameof(Strings.LyricsDescription), ResourceType = typeof(Strings))]
    public IExpressionProperty<KaraokeLyrics> Lyrics { get; } = Property.CreateExpression(KaraokeLyrics.Empty);

    [Display(Name = nameof(Strings.Layout), ResourceType = typeof(Strings))]
    public IExpressionProperty<KaraokeLayout> Layout { get; } = Property.CreateExpression(KaraokeLayout.TwoLines);

    [Display(Name = nameof(Strings.FontFamily), ResourceType = typeof(Strings))]
    public IExpressionProperty<FontFamily?> FontFamily { get; } = Property.CreateExpression<FontFamily?>(DefaultFont());

    [Display(Name = nameof(Strings.FontWeight), ResourceType = typeof(Strings))]
    public IExpressionProperty<FontWeight> FontWeight { get; } = Property.CreateExpression(Media.FontWeight.Bold);

    [Range(1f, 1000f)]
    [Display(Name = nameof(Strings.FontSize), ResourceType = typeof(Strings))]
    public IExpressionProperty<float> FontSize { get; } = Property.CreateExpression(64f);

    [Display(Name = nameof(Strings.LineSpacing), ResourceType = typeof(Strings))]
    public IExpressionProperty<float> LineSpacing { get; } = Property.CreateExpression(16f);

    [Display(Name = nameof(Strings.TextFill), ResourceType = typeof(Strings))]
    public IExpressionProperty<Brush?> TextFill { get; } = Property.CreateExpression<Brush?>();

    [Display(Name = nameof(Strings.TextStroke), ResourceType = typeof(Strings))]
    public IExpressionProperty<Pen?> TextStroke { get; } = Property.CreateExpression<Pen?>();

    [Display(Name = nameof(Strings.SungFill), ResourceType = typeof(Strings))]
    public IExpressionProperty<Brush?> SungFill { get; } = Property.CreateExpression<Brush?>();

    [Display(Name = nameof(Strings.SungStroke), ResourceType = typeof(Strings))]
    public IExpressionProperty<Pen?> SungStroke { get; } = Property.CreateExpression<Pen?>();

    [Range(0f, 10f)]
    [Display(Name = nameof(Strings.LeadTime), Description = nameof(Strings.LeadTimeDescription), ResourceType = typeof(Strings))]
    public IExpressionProperty<float> LeadTime { get; } = Property.CreateExpression(1.5f);

    [Range(0f, 10f)]
    [Display(Name = nameof(Strings.TailTime), Description = nameof(Strings.TailTimeDescription), ResourceType = typeof(Strings))]
    public IExpressionProperty<float> TailTime { get; } = Property.CreateExpression(0.8f);

    [Range(0f, 2f)]
    [Display(Name = nameof(Strings.FadeDuration), ResourceType = typeof(Strings))]
    public IExpressionProperty<float> FadeDuration { get; } = Property.CreateExpression(0.2f);

    [Display(Name = nameof(Strings.TimeOffset), Description = nameof(Strings.TimeOffsetDescription), ResourceType = typeof(Strings))]
    public IExpressionProperty<MediaTime> TimeOffset { get; } = Property.CreateExpression(MediaTime.Zero);

    // The host's fonts, set when the extension loads; a new object uses Noto Sans JP when the host has it.
    internal static FontManager? HostFonts { get; set; }

    private static FontFamily DefaultFont()
    {
        var notoSansJp = new FontFamily("Noto Sans JP");
        return HostFonts?.IsRegistered(notoSansJp) == true ? notoSansJp : Media.FontFamily.Default;
    }

    private static Pen CreatePen(Color color, float thickness)
    {
        var pen = new Pen();
        pen.Brush.Value = new SolidColorBrush(color);
        pen.Thickness.Value = thickness;
        pen.StrokeJoin.Value = StrokeJoin.Round;
        pen.StrokeAlignment.Value = StrokeAlignment.Outside;
        return pen;
    }

    public bool HasOriginalDuration()
    {
        return Lyrics.Value.Count > 0;
    }

    public bool TryGetOriginalDuration(out MediaTime timeSpan)
    {
        timeSpan = MediaTime.Zero;
        KaraokeLyrics lyrics = Lyrics.Value;
        if (lyrics.Count == 0)
            return false;

        LineSchedule[] schedule = KaraokeTimeline.Schedule(lyrics.Lines, Layout.Value,
            TimeSpan.FromSeconds(Math.Max(0, LeadTime.Value)), TimeSpan.FromSeconds(Math.Max(0, TailTime.Value)));
        timeSpan = MediaTime.FromTimeSpan(schedule.Max(s => s.Hide)) + TimeOffset.Value;
        return timeSpan > MediaTime.Zero;
    }

    /// <summary>
    /// Keeps the lyrics where they were when the element is split. The back half starts later, so its lyrics move back
    /// by as much; both halves keep every line, as both halves of a split video keep the whole file.
    /// </summary>
    public void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta)
    {
        if (backward)
            TimeOffset.Value -= startDelta;
    }

    private static float StrokeOutset(Pen.Resource? pen) => pen == null ? 0 : Math.Max(0, pen.Thickness);

    public partial class Resource
    {
        private LyricsLayout? _textLayout;
        private FontManager? _fonts;
        private LyricsDocument? _scheduledDocument;
        private (KaraokeLayout, float, float) _scheduleKey;
        private KaraokeLyrics? _documentLyrics;

        private TimeSpan LyricTime { get; set; }

        private LyricsDocument? Document { get; set; }

        private LineSchedule[]? Schedule { get; set; }

        protected override Size MeasureCore(Size availableSize)
        {
            if (GetLayout() is not { } layout)
                return default;

            int rows = Layout == KaraokeLayout.TwoLines ? 2 : 1;
            return new Size(layout.MaxWidth, layout.LineHeight * rows + Math.Max(0, LineSpacing) * (rows - 1));
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
            if (GetLayout() is not { } layout || Document is not { } document || Schedule is not { } schedule)
                return;

            TimeSpan time = LyricTime;
            float fade = Math.Max(0, FadeDuration);
            float pad = Math.Max(StrokeOutset(TextStroke), StrokeOutset(SungStroke)) + 2;

            foreach (LineSchedule entry in schedule)
            {
                if (time < entry.Show || time >= entry.Hide)
                    continue;

                LyricLine line = document.Lines[entry.LineIndex];
                FormattedText text = layout.Texts[entry.LineIndex];
                FormattedText sungText = layout.SungTexts[entry.LineIndex];
                // A text's outline is built from its own pen, not the one passed to DrawText.
                text.Pen = TextStroke;
                sungText.Pen = SungStroke;

                float width = layout.CharX[entry.LineIndex][^1];
                float x = Layout == KaraokeLayout.SingleLine
                    ? (layout.MaxWidth - width) / 2
                    : entry.Row == 0 ? 0 : layout.MaxWidth - width;
                float rowTop = entry.Row * (layout.LineHeight + Math.Max(0, LineSpacing));

                float opacity = 1;
                if (fade > 0)
                {
                    opacity = (float)Math.Clamp(Math.Min((time - entry.Show).TotalSeconds, (entry.Hide - time).TotalSeconds) / fade, 0, 1);
                }

                float sungX = layout.GetX(entry.LineIndex, KaraokeTimeline.GetSungCharacters(line, time));
                using (context.PushOpacity(opacity))
                using (context.PushTransform(Matrix.CreateTranslation(x, rowTop - layout.Ascent)))
                {
                    context.DrawText(text, TextFill, TextStroke);
                    if (sungX > 0)
                    {
                        // In baseline coordinates the row spans [Ascent, Descent]; the pad keeps outlines inside.
                        var clip = new Rect(-pad, layout.Ascent - pad, sungX + pad, layout.LineHeight + pad * 2);
                        using (context.PushClip(clip))
                        {
                            context.DrawText(sungText, SungFill, SungStroke);
                        }
                    }
                }
            }
        }

        partial void PostReconcile(KaraokeDrawable obj, CompositionContext context)
        {
            // The text is shaped with the fonts of the renderer that reconciles this resource.
            if (!ReferenceEquals(_fonts, context.Fonts))
            {
                _fonts = context.Fonts;
                Version++;
            }

            UpdateDocument();
            TimeSpan time = (context.Time - obj.TimeRange.Value.Start - TimeOffset).ToTimeSpan();
            if (time != LyricTime)
            {
                LyricTime = time;
                Version++;
            }

            LyricsDocument? document = Document;
            var key = (Layout, LeadTime, TailTime);
            if (!ReferenceEquals(document, _scheduledDocument) || key != _scheduleKey)
            {
                _scheduledDocument = document;
                _scheduleKey = key;
                Schedule = document == null
                    ? null
                    : KaraokeTimeline.Schedule(document.Lines, Layout,
                        TimeSpan.FromSeconds(Math.Max(0, LeadTime)), TimeSpan.FromSeconds(Math.Max(0, TailTime)));
                Version++;
            }
        }

        partial void PostDispose(bool disposing)
        {
            _textLayout = null;
            Schedule = null;
            Document = null;
        }

        private void UpdateDocument()
        {
            // One document per lyrics value, so the schedule and the shaped text are reused until an edit.
            if (ReferenceEquals(Lyrics, _documentLyrics))
                return;

            _documentLyrics = Lyrics;
            Document = Lyrics is { Count: > 0 } lyrics ? new LyricsDocument(lyrics.Lines, null, null) : null;
        }

        private LyricsLayout? GetLayout()
        {
            if (Document is not { Lines.Count: > 0 } document)
                return null;

            FontManager fonts = _fonts ?? throw new InvalidOperationException("Text layout requires CompositionContext.Fonts.");
            FontFamily font = FontFamily ?? Media.FontFamily.Default;
            if (_textLayout == null || !_textLayout.Matches(document.Lines, fonts, font, FontSize, FontWeight))
                _textLayout = new LyricsLayout(fonts, document.Lines, font, FontSize, FontWeight);

            return _textLayout;
        }
    }
}
