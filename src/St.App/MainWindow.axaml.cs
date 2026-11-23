using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.App;

/// <summary>
/// 셸 — 위에 대본 열기, 왼쪽에 챕터 · 장면 · 에피소드 고르기와 줄 찾기 · 종류 필터, 가운데에 고른 줄의 스텝 목록,
/// 오른쪽에 고른 커맨드 · 스텝의 칸과 접히는 카탈로그 칸(§2.4 — K7), 아래에 상태(docs/PLAN.md §4.5 · §4.7).
/// 고치는 일은 모두 <see cref="EditSession"/>을 지난다 — 조작(<c>LineEdit</c>) 하나가 되돌리기 한 칸이고, 바로 파일에 쓴다(U1).
/// 맡은 일별 partial: <c>Files</c>(열기 · 되돌리기) · <c>Library</c>(챕터 · 장면 · 줄 목록) · <c>Line</c>(스텝 목록 · 고르기) ·
/// <c>Edit</c>(칸 고치기 · 빼기 · 복사) · <c>Add</c>(더하기 · 카드) · <c>Catalog</c>(카탈로그 칸) · <c>Timeline</c>(타임라인) · <c>Keys</c>(단축키).
/// 칸의 모양은 <c>Views/ArgField</c> · <c>Views/FormGrid</c>가 맡는다 — 고치는 칸과 채우는 칸이 같은 것을 쓴다.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>종류 필터에 오르는 연출가의 종류(여섯) — 차례가 단추의 차례다.</summary>
    internal static readonly CommandKind[] DirectorKinds = CommandKinds.All.Where(CommandKinds.IsDirectorKind).ToArray();

    private readonly string _settingsPath;
    private readonly ToolSettings _settings;
    private readonly TextBlock _status = new() { FontSize = 11, Opacity = 0.8 };

    private const double MinWindow = 400;
    private const double MinSide = 160;

    private readonly Grid _body = new() { ColumnDefinitions = new ColumnDefinitions("380,4,*,4,340,4,280") };

    private EditSession? _session;

    /// <summary>열어 둔 대본 — 고칠 때마다 새로 읽은 것이다.</summary>
    private ScriptLibrary? Library => _session?.Library;

    public MainWindow() : this(ToolSettings.DefaultPath())
    {
    }

    /// <param name="settingsPath">설정 파일 자리 — 테스트가 임시 경로를 준다.</param>
    internal MainWindow(string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = ToolSettings.Load(settingsPath);
        InitializeComponent();

        DockPanel shell = new();

        Border top = new() { Padding = new Thickness(8, 6), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Palette.Line, Child = BuildToolbar() };
        DockPanel.SetDock(top, Dock.Top);
        shell.Children.Add(top);

        Border bottom = new() { Padding = new Thickness(8, 4), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Palette.Line, Child = _status };
        DockPanel.SetDock(bottom, Dock.Bottom);
        shell.Children.Add(bottom);

        Grid body = _body;
        Control[] columns =
        [
            BuildLibraryPanel(),
            new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Avalonia.Media.Brushes.Transparent },
            BuildLinePanel(),
            new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Avalonia.Media.Brushes.Transparent },
            BuildInspectorPanel(),
            new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Avalonia.Media.Brushes.Transparent },
            BuildCatalogPanel(),
        ];

        for (int i = 0; i < columns.Length; i++)
        {
            Grid.SetColumn(columns[i], i);
            body.Children.Add(columns[i]);
        }

        shell.Children.Add(body);

        Root.Child = shell;

        if (_settings.LastPath is { Length: > 0 } last && (File.Exists(last) || Directory.Exists(last)))
        {
            OpenPath(last);
        }
        else
        {
            ResetPickers();
        }

        RestoreLayout();
        Closing += (_, _) => SaveLayout();

        if (_settings.LoadProblem is { } problem)
        {
            _status.Text = problem;
        }
    }

    // ── 창의 꼴 — 크기와 양쪽 칸의 폭을 이 컴퓨터에 기억해 둔다 ─────────────

    private void RestoreLayout()
    {
        if (_settings.Window is not { } layout)
        {
            return;
        }

        if (layout.Width >= MinWindow && layout.Height >= MinWindow)
        {
            (Width, Height) = (layout.Width, layout.Height);
        }

        SetSideWidths(layout.Left, layout.Right);
        SetCatalogWidth(layout.Catalog);
        SetCatalogOpen(!layout.CatalogClosed);
    }

    /// <summary>창의 꼴을 설정에 남긴다 — 창을 닫을 때.</summary>
    internal void SaveLayout()
    {
        Size size = ClientSize.Width > 0 ? ClientSize : new Size(Width, Height);

        _settings.Window = new WindowLayout
        {
            Width = double.IsFinite(size.Width) ? size.Width : 0,
            Height = double.IsFinite(size.Height) ? size.Height : 0,
            // 양쪽 칸은 픽셀로 선 칸이다 — 가름대를 끌면 이 값이 바뀐다.
            Left = _body.ColumnDefinitions[0].Width.Value,
            Right = _body.ColumnDefinitions[4].Width.Value,
            Catalog = CatalogWidth,
            CatalogClosed = !_catalogOpen,
        };
        _settings.Save(_settingsPath);
    }

    /// <summary>왼쪽(줄 목록)과 오른쪽(칸)의 폭을 정한다 — 너무 좁은 값은 받지 않는다.</summary>
    internal void SetSideWidths(double left, double right)
    {
        if (left >= MinSide)
        {
            _body.ColumnDefinitions[0].Width = new GridLength(left);
        }

        if (right >= MinSide)
        {
            _body.ColumnDefinitions[4].Width = new GridLength(right);
        }
    }

    /// <summary>왼쪽과 오른쪽 칸의 폭.</summary>
    internal (double Left, double Right) SideWidths => (_body.ColumnDefinitions[0].Width.Value, _body.ColumnDefinitions[4].Width.Value);

    /// <summary>상태 줄의 글.</summary>
    internal string StatusLine => _status.Text ?? string.Empty;
}
