using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using St.App.Views;
using St.Authoring.Reading;

namespace St.App;

public partial class MainWindow
{
    private readonly TextBlock _pathText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Dim, FontSize = 12 };

    private Control BuildToolbar()
    {
        Button open = new() { Content = "대본 폴더 열기…" };
        open.Click += async (_, _) => await PickFolderAsync();

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { open, _pathText } };
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
            _library = ScriptLibrary.Open(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"열지 못했다 — {exception.Message}";
            return;
        }

        _pathText.Text = _library.Root;
        _settings.LastPath = _library.Root;
        _settings.Save(_settingsPath);
        RefreshLibrary();
    }
}
