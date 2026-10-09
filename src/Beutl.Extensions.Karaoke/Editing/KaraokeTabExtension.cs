using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Extensibility;

namespace Beutl.Extensions.Karaoke.Editing;

[Export]
public sealed class KaraokeTabExtension : ToolTabExtension
{
    // The instance the host loaded, so a tab opened from code counts as the same tool as one the host restores.
    private static KaraokeTabExtension? s_loaded;

    public override string Name => "Karaoke editor";

    public override string DisplayName => "カラオケ字幕";

    // Not in the add-tab menu: the tab is opened from an object's "編集" button and edits that object.
    public override string? Header => null;

    public override bool CanMultiple => false;

    // The line list is wide, so it sits under the timeline like the other list-style tools.
    public override DockAnchor DefaultAnchor => DockAnchor.Bottom;

    public override void Load()
    {
        base.Load();
        s_loaded = this;
    }

    public override void Unload()
    {
        if (ReferenceEquals(s_loaded, this))
            s_loaded = null;
        base.Unload();
    }

    /// <summary>
    /// Shows the "カラオケ字幕" tab editing <paramref name="drawable"/>: the open tab switches to it, or a new tab
    /// is opened.
    /// </summary>
    public static bool Open(IEditorContext editorContext, Graphics.KaraokeDrawable drawable)
    {
        if (editorContext.FindToolTab<KaraokeTabViewModel>() is { } existing)
        {
            existing.SetTarget(drawable);
            return editorContext.OpenToolTab(existing);
        }

        KaraokeTabExtension extension = s_loaded ?? new KaraokeTabExtension();
        if (!extension.TryCreateContext(editorContext, out IToolContext? context))
            return false;

        ((KaraokeTabViewModel)context).SetTarget(drawable);
        if (editorContext.OpenToolTab(context))
            return true;

        context.Dispose();
        return false;
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new KaraokeTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new KaraokeTabViewModel(this, editorContext);
        return true;
    }
}
