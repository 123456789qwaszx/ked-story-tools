using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using St.App.Views;

namespace St.App;

/// <summary>설정 창의 갈래 — 차례가 탭의 차례다.</summary>
internal enum SettingsTab
{
    /// <summary>단축키의 표(F1).</summary>
    Shortcuts,

    /// <summary>보이는 것을 켜고 끄는 것들.</summary>
    Other,
}

/// <summary>
/// 설정 창 — 위의 "설정" 단추. 탭 둘: 단축키(표는 <c>MainWindow.Keys</c>의 것을 그대로 보인다) · 기타(켜고 끄는 것).
/// 기타의 칸은 바꾸는 즉시 설정 파일에 남고 화면이 따라간다.
/// </summary>
public partial class MainWindow
{
    private Window? _settingsWindow;
    private TabControl? _settingsTabs;

    /// <summary>줄 목록에 화자의 이름을 보이는가.</summary>
    internal bool ShowSpeaker => _settings.ShowSpeaker;

    /// <summary>줄 목록에 화자의 이름을 보이거나 숨긴다 — 설정에 남는다. 설정 창의 칸을 누른 것과 같다.</summary>
    internal void SetShowSpeaker(bool show)
    {
        if (_settings.ShowSpeaker == show)
        {
            return;
        }

        _settings.ShowSpeaker = show;
        _settings.Save(_settingsPath);
        RefreshLibrary();
    }

    /// <summary>설정 창을 그 탭으로 연다 — 이미 열려 있으면 그 탭으로 바꿔 앞으로 낸다.</summary>
    internal Window ShowSettings(SettingsTab tab)
    {
        if (_settingsWindow is not null)
        {
            _settingsTabs!.SelectedIndex = (int)tab;
            _settingsWindow.Activate();
            return _settingsWindow;
        }

        _settingsTabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "단축키", Content = ShortcutsPage() },
                new TabItem { Header = "기타", Content = OtherPage() },
            },
            SelectedIndex = (int)tab,
        };

        _settingsWindow = new Window
        {
            Title = "설정",
            Width = 600,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = "Esc로 닫는다", FontSize = 11, Foreground = Palette.Faint, HorizontalAlignment = HorizontalAlignment.Right, [DockPanel.DockProperty] = Dock.Bottom },
                    _settingsTabs,
                },
            },
        };

        _settingsWindow.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.F1)
            {
                _settingsWindow?.Close();
            }
        };
        _settingsWindow.Closed += (_, _) => (_settingsWindow, _settingsTabs) = (null, null);
        _settingsWindow.Show(this);
        return _settingsWindow;
    }

    /// <summary>열린 설정 창의 탭. 닫혀 있으면 null.</summary>
    internal SettingsTab? SettingsTabShown => _settingsTabs is null ? null : (SettingsTab)_settingsTabs.SelectedIndex;

    private static Control ShortcutsPage()
    {
        FormGrid keys = new();

        foreach (Shortcut shortcut in Shortcuts)
        {
            keys.Add(shortcut.Keys, FormGrid.Hint(shortcut.Does, Brushes.White));
        }

        FormGrid gestures = new();

        foreach ((string name, string does) in Gestures)
        {
            gestures.Add(name, FormGrid.Hint(does, Brushes.White));
        }

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(6, 12),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "키", FontWeight = FontWeight.SemiBold, FontSize = 13 },
                    keys,
                    new TextBlock { Text = "손으로", FontWeight = FontWeight.SemiBold, FontSize = 13, Margin = new Thickness(0, 6, 0, 0) },
                    gestures,
                },
            },
        };
    }

    private Control OtherPage()
    {
        CheckBox speaker = new() { Content = "줄 목록에 화자 보이기", IsChecked = _settings.ShowSpeaker };
        speaker.IsCheckedChanged += (_, _) => SetShowSpeaker(speaker.IsChecked == true);

        return new StackPanel
        {
            Margin = new Thickness(6, 12),
            Spacing = 6,
            Children =
            {
                speaker,
                FormGrid.Hint("왼쪽 줄 목록의 줄마다 앞에 서는 화자의 이름. 끄면 대사 글만 보인다 — 가운데의 고른 줄에는 그대로 보인다.", Palette.Faint),
            },
        };
    }
}
