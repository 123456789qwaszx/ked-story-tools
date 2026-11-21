using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using St.App.Views;
using St.Authoring.Editing;

namespace St.App;

public partial class MainWindow
{
    private readonly TextBlock _pathText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Dim, FontSize = 12 };

    private readonly Button _undoButton = new() { Content = "↶ 되돌리기" };
    private readonly Button _redoButton = new() { Content = "↷ 다시 하기" };

    private Control BuildToolbar()
    {
        Button open = new() { Content = "대본 폴더 열기…" };
        open.Click += async (_, _) => await PickFolderAsync();

        Button reload = new() { Content = "다시 읽기" };
        reload.Click += (_, _) => RefreshFromDisk();

        Button catalog = new() { Content = "카드 폴더…" };
        catalog.Click += async (_, _) => await PickCatalogAsync();
        ToolTip.SetTip(catalog, "연출 카드의 카탈로그(cards.json · kinds.json이 든 폴더)를 고른다 — 이 대본에 기억해 둔다");

        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();
        ToolTip.SetTip(_undoButton, "Ctrl+Z — 마지막 고침을 되돌려 파일에 쓴다");
        ToolTip.SetTip(_redoButton, "Ctrl+Y · Ctrl+Shift+Z");
        ToolTip.SetTip(reload, "F5 — 디스크의 대본을 다시 읽는다. 바깥에서 바뀐 파일의 되돌리기는 비워진다");

        // 창으로 돌아오면 디스크를 다시 본다 — 다른 도구가 대본을 다시 냈을 수 있다.
        Activated += (_, _) => RefreshFromDisk();

        Button keys = new() { Content = "단축키" };
        keys.Click += (_, _) => ShowShortcuts();
        ToolTip.SetTip(keys, "F1");

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { open, _undoButton, _redoButton, reload, catalog, keys, _pathText } };
    }

    private async Task PickFolderAsync()
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "대본 폴더(.yarn이 든 곳)를 고른다", AllowMultiple = false });

        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            OpenPath(path);
        }
    }

    /// <summary>대본(파일이나 폴더)을 연다 — 줄 목록을 채우고, 다음에 켜면 다시 열게 설정에 남긴다.</summary>
    internal void OpenPath(string path)
    {
        try
        {
            _session = EditSession.Open(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"열지 못했다 — {exception.Message}";
            return;
        }

        _pathText.Text = _session.Root;
        _settings.LastPath = _session.Root;
        _settings.Save(_settingsPath);
        LoadRememberedCatalog();
        ResetGraph();
        ForgetMotions();
        ResetPickers();
    }
}
