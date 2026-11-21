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
/// 오른쪽에 고른 커맨드 · 스텝의 칸, 아래에 상태(docs/PLAN.md §4.5 · §4.7).
/// 고치는 일은 모두 <see cref="EditSession"/>을 지난다 — 조작(<c>LineEdit</c>) 하나가 되돌리기 한 칸이고, 바로 파일에 쓴다(U1).
/// 맡은 일별 partial: <c>Files</c>(열기 · 되돌리기) · <c>Library</c>(챕터 · 장면 · 줄 목록) · <c>Line</c>(스텝 목록) · <c>Edit</c>(칸 고치기).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>종류 필터에 오르는 넷 — 차례가 단추의 차례다.</summary>
    internal static readonly CommandKind[] DirectorKinds = CommandKinds.All.Where(CommandKinds.IsDirectorKind).ToArray();

    private readonly string _settingsPath;
    private readonly ToolSettings _settings;
    private readonly TextBlock _status = new() { FontSize = 11, Opacity = 0.8 };

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

        Grid body = new() { ColumnDefinitions = new ColumnDefinitions("380,4,*,4,340") };
        Control[] columns =
        [
            BuildLibraryPanel(),
            new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Avalonia.Media.Brushes.Transparent },
            BuildLinePanel(),
            new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Avalonia.Media.Brushes.Transparent },
            BuildInspectorPanel(),
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

        if (_settings.LoadProblem is { } problem)
        {
            _status.Text = problem;
        }
    }

    /// <summary>상태 줄의 글.</summary>
    internal string StatusLine => _status.Text ?? string.Empty;
}
