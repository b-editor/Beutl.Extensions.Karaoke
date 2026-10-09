using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
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
[Display(Name = "カラオケ字幕")]
public sealed partial class KaraokeDrawable : Drawable, IOriginalDurationProvider, ISplittable
{
    public KaraokeDrawable()
    {
        ScanProperties<KaraokeDrawable>();

        TextFill.CurrentValue = new SolidColorBrush(Colors.White);
        TextStroke.CurrentValue = CreatePen(Color.FromArgb(255, 0x10, 0x20, 0x60), 8);
        SungFill.CurrentValue = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x8B, 0xFF));
        SungStroke.CurrentValue = CreatePen(Colors.White, 8);
    }

    [Display(Name = "歌詞", Description = "ツールタブ「カラオケ字幕」で取り込みや編集ができます")]
    public IProperty<KaraokeLyrics> Lyrics { get; } = Property.Create(KaraokeLyrics.Empty);

    [Display(Name = "表示方法")]
    public IProperty<KaraokeLayout> Layout { get; } = Property.Create(KaraokeLayout.TwoLines);

    [Display(Name = "フォント")]
    public IProperty<FontFamily?> FontFamily { get; } = Property.Create<FontFamily?>(DefaultFont());

    [Display(Name = "太さ")]
    public IProperty<FontWeight> FontWeight { get; } = Property.Create(Media.FontWeight.Bold);

    [Range(1f, 1000f)]
    [Display(Name = "文字の大きさ")]
    public IProperty<float> FontSize { get; } = Property.Create(64f);

    [Display(Name = "行の間隔")]
    public IProperty<float> LineSpacing { get; } = Property.Create(16f);

    [Display(Name = "歌う前の文字の色")]
    public IProperty<Brush?> TextFill { get; } = Property.Create<Brush?>();

    [Display(Name = "歌う前の縁取り")]
    public IProperty<Pen?> TextStroke { get; } = Property.Create<Pen?>();

    [Display(Name = "歌った後の文字の色")]
    public IProperty<Brush?> SungFill { get; } = Property.Create<Brush?>();

    [Display(Name = "歌った後の縁取り")]
    public IProperty<Pen?> SungStroke { get; } = Property.Create<Pen?>();

    [Range(0f, 10f)]
    [Display(Name = "先に表示する時間 (秒)", Description = "歌い始めるこの時間前から行を表示します")]
    public IProperty<float> LeadTime { get; } = Property.Create(1.5f);

    [Range(0f, 10f)]
    [Display(Name = "残す時間 (秒)", Description = "歌い終わってからこの時間だけ行を残します")]
    public IProperty<float> TailTime { get; } = Property.Create(0.8f);

    [Range(0f, 2f)]
    [Display(Name = "フェードの長さ (秒)")]
    public IProperty<float> FadeDuration { get; } = Property.Create(0.2f);

    [Display(Name = "時刻のずれ", Description = "歌詞の時刻にこの時間を足して表示します")]
    public IProperty<TimeSpan> TimeOffset { get; } = Property.Create(TimeSpan.Zero);

    private static FontFamily DefaultFont()
    {
        var notoSansJp = new FontFamily("Noto Sans JP");
        return FontManager.Instance.IsRegistered(notoSansJp) ? notoSansJp : Media.FontFamily.Default;
    }

    private static Pen CreatePen(Color color, float thickness)
    {
        var pen = new Pen();
        pen.Brush.CurrentValue = new SolidColorBrush(color);
        pen.Thickness.CurrentValue = thickness;
        pen.StrokeJoin.CurrentValue = StrokeJoin.Round;
        pen.StrokeAlignment.CurrentValue = StrokeAlignment.Outside;
        return pen;
    }

    public bool HasOriginalDuration()
    {
        return Lyrics.CurrentValue.Count > 0;
    }

    public bool TryGetOriginalDuration(out TimeSpan timeSpan)
    {
        timeSpan = TimeSpan.Zero;
        KaraokeLyrics lyrics = Lyrics.CurrentValue;
        if (lyrics.Count == 0)
            return false;

        LineSchedule[] schedule = KaraokeTimeline.Schedule(lyrics.Lines, Layout.CurrentValue,
            TimeSpan.FromSeconds(Math.Max(0, LeadTime.CurrentValue)), TimeSpan.FromSeconds(Math.Max(0, TailTime.CurrentValue)));
        timeSpan = schedule.Max(s => s.Hide) + TimeOffset.CurrentValue;
        return timeSpan > TimeSpan.Zero;
    }

    /// <summary>
    /// Keeps the lyrics where they were when the element is split. The back half starts later, so its lyrics move back
    /// by as much; both halves keep every line, as both halves of a split video keep the whole file.
    /// </summary>
    public void NotifySplitted(bool backward, TimeSpan startDelta, TimeSpan durationDelta)
    {
        if (backward)
            TimeOffset.CurrentValue -= startDelta;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.GetLayout() is not { } layout)
            return default;

        int rows = r.Layout == KaraokeLayout.TwoLines ? 2 : 1;
        return new Size(layout.MaxWidth, layout.LineHeight * rows + Math.Max(0, r.LineSpacing) * (rows - 1));
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.GetLayout() is not { } layout || r.Document is not { } document || r.Schedule is not { } schedule)
            return;

        TimeSpan time = r.LyricTime;
        float fade = Math.Max(0, r.FadeDuration);
        float pad = Math.Max(StrokeOutset(r.TextStroke), StrokeOutset(r.SungStroke)) + 2;

        foreach (LineSchedule entry in schedule)
        {
            if (time < entry.Show || time >= entry.Hide)
                continue;

            LyricLine line = document.Lines[entry.LineIndex];
            FormattedText text = layout.Texts[entry.LineIndex];
            FormattedText sungText = layout.SungTexts[entry.LineIndex];
            // A text's outline is built from its own pen, not the one passed to DrawText.
            text.Pen = r.TextStroke;
            sungText.Pen = r.SungStroke;

            float width = layout.CharX[entry.LineIndex][^1];
            float x = r.Layout == KaraokeLayout.SingleLine
                ? (layout.MaxWidth - width) / 2
                : entry.Row == 0 ? 0 : layout.MaxWidth - width;
            float rowTop = entry.Row * (layout.LineHeight + Math.Max(0, r.LineSpacing));

            float opacity = 1;
            if (fade > 0)
            {
                opacity = (float)Math.Clamp(Math.Min((time - entry.Show).TotalSeconds, (entry.Hide - time).TotalSeconds) / fade, 0, 1);
            }

            float sungX = layout.GetX(entry.LineIndex, KaraokeTimeline.GetSungCharacters(line, time));
            using (context.PushOpacity(opacity))
            using (context.PushTransform(Matrix.CreateTranslation(x, rowTop - layout.Ascent)))
            {
                context.DrawText(text, r.TextFill, r.TextStroke);
                if (sungX > 0)
                {
                    // In baseline coordinates the row spans [Ascent, Descent]; the pad keeps outlines inside.
                    var clip = new Rect(-pad, layout.Ascent - pad, sungX + pad, layout.LineHeight + pad * 2);
                    using (context.PushClip(clip))
                    {
                        context.DrawText(sungText, r.SungFill, r.SungStroke);
                    }
                }
            }
        }
    }

    private static float StrokeOutset(Pen.Resource? pen) => pen == null ? 0 : Math.Max(0, pen.Thickness);

    public partial class Resource
    {
        private LyricsLayout? _textLayout;
        private LyricsDocument? _scheduledDocument;
        private (KaraokeLayout, float, float) _scheduleKey;
        private KaraokeLyrics? _documentLyrics;

        internal TimeSpan LyricTime { get; private set; }

        internal LyricsDocument? Document { get; private set; }

        internal LineSchedule[]? Schedule { get; private set; }

        partial void PostUpdate(KaraokeDrawable obj, CompositionContext context)
        {
            UpdateDocument();
            TimeSpan time = context.Time - obj.TimeRange.Start - TimeOffset;
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

        internal LyricsLayout? GetLayout()
        {
            if (Document is not { Lines.Count: > 0 } document)
                return null;

            FontFamily font = FontFamily ?? Media.FontFamily.Default;
            if (_textLayout == null || !_textLayout.Matches(document.Lines, font, FontSize, FontWeight))
                _textLayout = new LyricsLayout(document.Lines, font, FontSize, FontWeight);

            return _textLayout;
        }
    }
}
