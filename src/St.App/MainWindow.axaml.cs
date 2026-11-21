using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.App;

/// <summary>
/// 셸 — 위에 대본 열기, 왼쪽에 줄 찾기와 종류 필터, 가운데에 고른 줄의 스텝 목록, 아래에 상태(docs/PLAN.md §4.5).
/// K4-1은 읽는 화면이다 — 고치지 않는다. 고치는 일(K4-2부터)은 <c>St.Authoring</c>의 조작(<c>LineEdit</c>)을 지난다.
/// 맡은 일별 partial: <c>Files</c>(열기) · <c>Library</c>(줄 목록) · <c>Line</c>(스텝 목록).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>종류 필터에 오르는 넷 — 차례가 단추의 차례다.</summary>
    internal static readonly CommandKind[] DirectorKinds = CommandKinds.All.Where(CommandKinds.IsDirectorKind).ToArray();

    private readonly string _settingsPath;
    private readonly ToolSettings _settings;
    private readonly TextBlock _status = new() { FontSize = 11, Opacity = 0.8 };

    private ScriptLibrary? _library;

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

        Grid body = new() { ColumnDefinitions = new ColumnDefinitions("380,4,*") };
        Control left = BuildLibraryPanel();
        GridSplitter splitter = new() { ResizeDirection = GridResizeDirection.Columns, Background = Avalonia.Media.Brushes.Transparent };
        Control center = BuildLinePanel();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(center, 2);
        body.Children.Add(left);
        body.Children.Add(splitter);
        body.Children.Add(center);
        shell.Children.Add(body);

        Root.Child = shell;

        if (_settings.LastPath is { Length: > 0 } last && (File.Exists(last) || Directory.Exists(last)))
        {
            OpenPath(last);
        }
        else
        {
            RefreshLibrary();
        }

        if (_settings.LoadProblem is { } problem)
        {
            _status.Text = problem;
        }
    }

    /// <summary>상태 줄의 글.</summary>
    internal string StatusLine => _status.Text ?? string.Empty;
}
