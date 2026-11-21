using Avalonia.Controls;
using Ked.Presentation.Core;

namespace St.App;

/// <summary>
/// 셸 — K3에는 창과 상태 줄뿐이다. 화면(줄 찾기 · 종류 필터 · 스텝 목록 · 타임라인)은 K4에서 맡은 일별 partial로 선다.
/// 모든 편집은 <c>St.Authoring</c>의 조작(<c>LineEdit</c>)을 지난다 — 화면이 yarn을 직접 쓰지 않는다.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StatusText.Text = StatusLine();
    }

    /// <summary>상태 줄의 글 — 이 툴이 따르는 명세 표의 크기.</summary>
    internal static string StatusLine()
        => $"명세 표 — 커맨드 {StageCommandSpecTable.Default.All.Count}개 · 종류 {CommandKinds.All.Length}개";
}
