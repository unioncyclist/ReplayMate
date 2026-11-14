using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReplayMate.Services;

namespace ReplayMate.Views;

public sealed partial class LogsPage : UserControl
{
    private readonly ObservableCollection<string> _lines = new();

    public LogsPage()
    {
        InitializeComponent();
        LogList.ItemsSource = _lines;
        Log.LineAdded += OnLineAdded;
        Reload();
    }

    private void OnLineAdded(string line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _lines.Add(line);
            while (_lines.Count > 800) _lines.RemoveAt(0);
            if (_lines.Count > 0) LogList.ScrollIntoView(_lines[^1]);
        });
    }

    public void Reload()
    {
        _lines.Clear();
        foreach (var line in Log.RecentLines) _lines.Add(line);
        if (_lines.Count > 0) LogList.ScrollIntoView(_lines[^1]);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"打开日志目录失败：{ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Log.LineAdded -= OnLineAdded;
}
