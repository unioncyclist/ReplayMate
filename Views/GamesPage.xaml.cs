using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReplayMate.Services;

namespace ReplayMate.Views;

public sealed class GameRowVm : INotifyPropertyChanged
{
    public GameEntry Entry { get; }

    public GameRowVm(GameEntry entry) => Entry = entry;

    public string Name => Entry.Name;
    public string Match => Entry.MatchDescription;
    public string Source => Entry.SourceText;

    public bool Enabled
    {
        get => Entry.Enabled;
        set
        {
            if (Entry.Enabled == value) return;
            Entry.Enabled = value;
            AppState.Current.Save();
            Log.Info($"{(value ? "启用" : "停用")}白名单条目「{Entry.Name}」");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class GamesPage : UserControl
{
    private readonly ObservableCollection<GameRowVm> _rows = new();

    public GamesPage()
    {
        InitializeComponent();
        GameList.ItemsSource = _rows;
        Reload();
    }

    public void Reload()
    {
        var state = AppState.Current;
        _rows.Clear();
        foreach (var g in state.Config.Games)
            _rows.Add(new GameRowVm(g));

        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GameList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void AddFromRunning_Click(object sender, RoutedEventArgs e)
    {
        List<ProcInfo> procs;
        try
        {
            procs = await Task.Run(ProcessService.WithWindows);
        }
        catch (Exception ex)
        {
            Log.Error($"枚举进程失败：{ex.Message}");
            return;
        }

        if (procs.Count == 0)
        {
            await ShowMessageAsync("没有找到带窗口的进程。");
            return;
        }

        var items = procs
            .Select(p => new SelectableItem(p, $"{p.Name}   —   {p.Title}"))
            .ToList();

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemsSource = items,
            Height = 380
        };

        var dialog = new ContentDialog
        {
            Title = "选择正在运行的游戏",
            Content = list,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (list.SelectedItems.Count == 0) return;

        var state = AppState.Current;
        var added = 0;
        foreach (var item in list.SelectedItems.Cast<SelectableItem>())
        {
            if (item.Value is not ProcInfo p) continue;
            if (p.Path is not null &&
                state.Config.Games.Any(g => string.Equals(g.ExePath, p.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

            state.Config.Games.Add(new GameEntry
            {
                Name = string.IsNullOrWhiteSpace(p.Title) ? p.Name : p.Title,
                ProcessName = p.Name,
                ExePath = p.Path,
                Source = "running"
            });
            added++;
        }

        state.Save();
        Reload();
        Log.Info($"从运行中进程添加了 {added} 个游戏");
        if (added > 0) GameList.SelectedItems.Clear();
    }

    private async void ScanLibraries_Click(object sender, RoutedEventArgs e)
    {
        List<LibraryGame> games;
        try
        {
            games = await Task.Run(() =>
            {
                var list = GameLibraries.ScanAll(out var report);
                Log.Info("扫描结果：" + report);
                return list;
            });
        }
        catch (Exception ex)
        {
            Log.Error($"扫描游戏库失败：{ex.Message}");
            await ShowMessageAsync("扫描失败：" + ex.Message);
            return;
        }

        if (games.Count == 0)
        {
            await ShowMessageAsync("没有扫描到游戏。请确认 Steam / Epic 已安装并有游戏。");
            return;
        }

        var state = AppState.Current;
        var existingDirs = new HashSet<string>(
            state.Config.Games
                .Where(g => !string.IsNullOrWhiteSpace(g.InstallDir))
                .Select(g => g.InstallDir!.TrimEnd('\\').ToLowerInvariant()));

        var items = new List<SelectableItem>();
        var preselected = new List<SelectableItem>();

        foreach (var g in games)
        {
            var key = g.InstallDir.TrimEnd('\\').ToLowerInvariant();
            var exists = existingDirs.Contains(key);
            var suffix = g.Source == "steam" ? "Steam" : "Epic";
            var text = $"{g.Name}   [{suffix}]" + (exists ? "   · 已在白名单" : "");

            var item = new SelectableItem(g, text);
            items.Add(item);
            if (exists) preselected.Add(item);
        }

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemsSource = items,
            Height = 420
        };

        // 已在白名单的条目默认勾选，避免重复添加时看不出状态
        foreach (var item in preselected) list.SelectedItems.Add(item);

        var dialog = new ContentDialog
        {
            Title = $"扫描到 {games.Count} 个游戏（已在白名单的已勾选）",
            Content = list,
            PrimaryButtonText = "加入白名单",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (list.SelectedItems.Count == 0) return;

        var added = 0;
        foreach (var item in list.SelectedItems.Cast<SelectableItem>())
        {
            if (item.Value is not LibraryGame g) continue;
            if (state.Config.Games.Any(x =>
                    string.Equals(x.InstallDir, g.InstallDir, StringComparison.OrdinalIgnoreCase)))
                continue;

            state.Config.Games.Add(new GameEntry
            {
                Name = g.Name,
                InstallDir = g.InstallDir,
                Source = g.Source
            });
            added++;
        }

        state.Save();
        Reload();
        Log.Info($"从游戏库导入了 {added} 个游戏");
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItems.Count == 0) return;

        var state = AppState.Current;
        var removed = 0;
        foreach (var item in GameList.SelectedItems.Cast<GameRowVm>().ToList())
        {
            state.Config.Games.Remove(item.Entry);
            _rows.Remove(item);
            removed++;
        }

        state.Save();
        Reload();
        Log.Info($"移除了 {removed} 个白名单条目");
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "ReplayMate",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "好",
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        try { await dialog.ShowAsync(); } catch { }
    }
}

public sealed class SelectableItem
{
    public SelectableItem(object value, string text)
    {
        Value = value;
        Text = text;
    }

    public object Value { get; }
    public string Text { get; }

    public override string ToString() => Text;
}
