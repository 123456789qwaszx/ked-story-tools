using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;

namespace St.App.Views;

/// <summary>오른쪽의 칸들 — 이름과 칸이 두 열로 선다. 줄마다 하나씩 더한다.</summary>
internal sealed class FormGrid : Grid
{
    private readonly Dictionary<string, CommandArgSpec> _specs = [];

    public FormGrid()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
    }

    /// <summary>칸 하나를 더한다. <paramref name="spec"/>을 주면 이름을 끌어 값을 바꿀 수 있다(수로 읽히는 칸).</summary>
    public void Add(string label, Control editor, string? note = null, CommandArgSpec? spec = null)
    {
        int row = RowDefinitions.Count;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock caption = new()
        {
            Text = label,
            FontSize = 12,
            Foreground = Palette.Dim,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Background = Brushes.Transparent,
        };
        editor.Margin = new Thickness(0, 0, 0, 4);

        if (!string.IsNullOrEmpty(note))
        {
            ToolTip.SetTip(caption, note);
        }

        if (spec is not null)
        {
            _specs[label] = spec;
            ArgField.AttachScrub(caption, spec, editor);
        }

        SetRow(caption, row);
        SetRow(editor, row);
        SetColumn(editor, 1);
        Children.Add(caption);
        Children.Add(editor);
    }

    /// <summary>칸의 이름 — 위에서부터.</summary>
    public IReadOnlyList<string> Labels => Children.OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();

    /// <summary>그 이름의 칸. 없으면 null.</summary>
    public Control? Cell(string label)
    {
        TextBlock? caption = Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == label);
        return caption is null ? null : Children.FirstOrDefault(c => c != caption && GetRow(c) == GetRow(caption));
    }

    /// <summary>그 이름의 칸의 명세 — 커맨드의 인자 칸일 때만.</summary>
    public CommandArgSpec? SpecOf(string label) => _specs.GetValueOrDefault(label);

    /// <summary>도움말 한 줄.</summary>
    public static TextBlock Hint(string text, IBrush? brush = null)
        => new() { Text = text, FontSize = 12, Foreground = brush ?? Palette.Dim, TextWrapping = TextWrapping.Wrap };
}
