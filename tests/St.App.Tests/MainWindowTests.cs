using Avalonia.Controls;

namespace St.App.Tests;

public class MainWindowTests
{
    [Fact]
    public void 셸이_뜨고_상태_줄에_명세_표가_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = new();
            window.Show();

            TextBlock status = window.FindControl<TextBlock>("StatusText")!;
            Assert.Equal(MainWindow.StatusLine(), status.Text);
            Assert.Contains("커맨드", status.Text);

            window.Close();
        });
    }
}
