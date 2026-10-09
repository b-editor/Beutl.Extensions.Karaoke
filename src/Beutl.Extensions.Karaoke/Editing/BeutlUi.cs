using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.Extensions.Karaoke.Editing;

/// <summary>
/// Builds controls that look like Beutl's own tool tabs. Extensions cannot reference Beutl.Controls, so its
/// ToolTabBar and styles are rebuilt here from the same theme resources and style classes, which Beutl provides
/// at run time.
/// </summary>
internal static class BeutlUi
{
    /// <summary>The bar at the top of a tool tab: a title, then actions on the right.</summary>
    public static Border ToolBar(Control content)
    {
        var bar = new Border
        {
            MinHeight = 38,
            Padding = new Thickness(4, 2),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content,
        };
        return WithResource(bar, Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
    }

    public static TextBlock BarTitle(string text) => new()
    {
        Text = text,
        Margin = new Thickness(8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>An icon-only button with its name as the tooltip, like the buttons in Beutl's tool bars.</summary>
    public static Button IconButton(Icon icon, string name, Action onClick)
    {
        var button = new Button
        {
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FluentIcon { Icon = icon, FontSize = 16 },
        };
        WithResource(button, StyledElement.ThemeProperty, "ToolTabBarIconButtonTheme");
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Button TextButton(string text, Action onClick, bool accent = false)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        if (accent)
            button.Classes.Add("accent");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Small text in the theme's secondary text colour.</summary>
    public static TextBlock Secondary(string? text = null)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        return WithResource(block, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
    }

    /// <summary>A bordered group, like the settings groups and lists in Beutl.</summary>
    public static Border Group(Control child, Thickness? padding = null)
    {
        var group = new Border { Child = child, Padding = padding ?? new Thickness(12) };
        group.Classes.Add("options-group");
        return group;
    }

    /// <summary>The empty state Beutl's tool tabs show: an icon in a rounded tile, a title, and an optional action.</summary>
    public static StackPanel EmptyState(Icon icon, Control title, Control? action = null)
    {
        var tile = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = WithResource(new FluentIcon { Icon = icon, FontSize = 24 }, TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush"),
        };
        WithResource(tile, Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");

        var panel = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 320,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { tile, title },
        };
        if (action != null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(action);
        }

        return panel;
    }

    public static TextBlock EmptyTitle() => new() { FontSize = 16, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };

    /// <summary>A label and its editor on one row, like a row of Beutl's property panel.</summary>
    public static void AddRow(Grid grid, int row, string label, Control editor, bool alignTop = false)
    {
        while (grid.RowDefinitions.Count <= row)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock text = new()
        {
            Text = label,
            VerticalAlignment = alignTop ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = alignTop ? new Thickness(0, 6, 0, 0) : default,
        };
        Grid.SetRow(text, row);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(text);
        grid.Children.Add(editor);
    }

    public static Grid Rows() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        ColumnSpacing = 12,
        RowSpacing = 8,
    };

    /// <summary>Binds a property to a theme resource, so the control follows Beutl's theme and its light/dark switch.</summary>
    public static T WithResource<T>(T control, AvaloniaProperty property, string key)
        where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }
}
