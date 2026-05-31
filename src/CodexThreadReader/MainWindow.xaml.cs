using CodexThreadReader.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CodexThreadReader;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ThreadRowViewModel> _allThreads = [];
    private readonly ObservableCollection<SidebarGroupViewModel> _sidebarGroups = [];
    private readonly string _settingsRoot;
    private readonly string _anchorPath;
    private readonly string _exportRoot;
    private AnchorStore _anchorStore;
    private CancellationTokenSource? _previewCancellation;
    private ThreadRowViewModel? _selectedThread;

    public MainWindow()
    {
        _settingsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexThreadReader");
        _anchorPath = Path.Combine(_settingsRoot, "anchors.json");
        _exportRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CodexThreadReader", "Exports");
        _anchorStore = new AnchorStore(_anchorPath, Array.Empty<string>());
        InitializeComponent();
        ThreadsTree.DataContext = _sidebarGroups;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settingsRoot);
        Directory.CreateDirectory(_exportRoot);
        _anchorStore = await AnchorStore.LoadAsync(_anchorPath, CancellationToken.None);
        await RefreshCatalogAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCatalogAsync();
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void FilterComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private async void AnchorButton_Click(object sender, RoutedEventArgs e)
    {
        var id = AnchorIdTextBox.Text.Trim();
        if (id.Length == 0)
        {
            SetStatus("Paste a thread ID before anchoring.");
            return;
        }

        _anchorStore = _anchorStore.WithThreadIds(_anchorStore.ThreadIds.Concat(new[] { id }));
        await _anchorStore.SaveAsync(CancellationToken.None);
        AnchorIdTextBox.Clear();
        await RefreshCatalogAsync();
    }

    private async void RemoveAnchorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedThread is null)
        {
            return;
        }

        _anchorStore = _anchorStore.WithThreadIds(_anchorStore.ThreadIds.Where(id => !id.Equals(_selectedThread.Id, StringComparison.OrdinalIgnoreCase)));
        await _anchorStore.SaveAsync(CancellationToken.None);
        await RefreshCatalogAsync();
    }

    private async void ThreadsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ThreadRowViewModel thread)
        {
            _selectedThread = thread;
            await LoadPreviewAsync(thread);
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedThread is null)
        {
            return;
        }

        if (!_selectedThread.Summary.RolloutExists)
        {
            SetStatus("Selected thread has no rollout file to export.");
            return;
        }

        try
        {
            SetBusy(true, "Parsing full rollout for export...");
            var parsed = await RolloutParser.ParseAsync(_selectedThread.Summary.RolloutPath, CancellationToken.None);
            SetStatus("Writing export files...");
            var manifest = await ThreadExporter.ExportAsync(_selectedThread.Summary, parsed, _exportRoot, CancellationToken.None);
            Clipboard.SetText(File.ReadAllText(manifest.HandoffMarkdownPath));
            OpenInExplorer(manifest.ExportDirectory);
            SetStatus($"Exported {_selectedThread.Id}. Handoff prompt copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetStatus($"Export failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenExportsButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_exportRoot);
        OpenInExplorer(_exportRoot);
    }

    private async Task RefreshCatalogAsync()
    {
        try
        {
            SetBusy(true, "Reading Codex thread catalog...");
            _anchorStore = await AnchorStore.LoadAsync(_anchorPath, CancellationToken.None);
            var codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            var result = await ThreadCatalog.LoadAsync(new ThreadCatalogOptions(codexHome, _anchorPath), CancellationToken.None);
            _allThreads.Clear();
            foreach (var thread in result.Threads)
            {
                _allThreads.Add(new ThreadRowViewModel(thread));
            }

            ApplyFilters();
            var errorText = result.Errors.Count == 0 ? string.Empty : " Errors: " + string.Join(" | ", result.Errors);
            SetStatus($"Loaded {_allThreads.Count} threads. Anchors: {_anchorStore.ThreadIds.Count}.{errorText}");
        }
        catch (Exception ex)
        {
            SetStatus($"Catalog load failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyFilters()
    {
        var search = SearchTextBox?.Text?.Trim() ?? string.Empty;
        var filter = (FilterComboBox?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "All";
        var matching = _allThreads
            .Where(t => MatchesFilter(t, filter) && MatchesSearch(t, search))
            .Select(t => t.Summary)
            .ToArray();

        _sidebarGroups.Clear();
        foreach (var group in SidebarTreeBuilder.Build(matching))
        {
            _sidebarGroups.Add(new SidebarGroupViewModel(group.Title, group.Threads.Select(t => new ThreadRowViewModel(t))));
        }
    }

    private static bool MatchesSearch(ThreadRowViewModel thread, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return thread.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
            || thread.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
            || thread.Cwd.Contains(search, StringComparison.OrdinalIgnoreCase)
            || thread.FlagsText.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFilter(ThreadRowViewModel thread, string filter)
    {
        return filter switch
        {
            "Anchored" => thread.Summary.IsAnchored,
            "Smoked" => thread.IsSmoked,
            "Active" => !thread.Summary.IsArchived,
            "Archived" => thread.Summary.IsArchived,
            _ => true
        };
    }

    private async Task LoadPreviewAsync(ThreadRowViewModel? selected)
    {
        _previewCancellation?.Cancel();
        ChatItemsControl.ItemsSource = null;
        DetailTitleTextBlock.Text = string.Empty;
        DetailMetaTextBlock.Text = string.Empty;
        DetailFlagsTextBlock.Text = string.Empty;
        ExportButton.IsEnabled = false;
        RemoveAnchorButton.IsEnabled = false;

        if (selected is null)
        {
            PreviewStatusTextBlock.Text = string.Empty;
            return;
        }

        DetailTitleTextBlock.Text = selected.Title;
        DetailMetaTextBlock.Text = $"ID {selected.Id}  |  {selected.UpdatedText}  |  {selected.SizeText}\n{selected.Cwd}";
        DetailFlagsTextBlock.Text = selected.FlagsText.Length == 0 ? "No recovery flags" : selected.FlagsText;
        ExportButton.IsEnabled = selected.Summary.RolloutExists;
        RemoveAnchorButton.IsEnabled = selected.Summary.IsAnchored;

        if (!selected.Summary.RolloutExists)
        {
            PreviewStatusTextBlock.Text = "No rollout file exists for this catalog row.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        PreviewStatusTextBlock.Text = "Loading chat preview...";
        try
        {
            var parsed = await RolloutParser.ParseAsync(selected.Summary.RolloutPath, maxMessages: 300, cancellation.Token);
            ChatItemsControl.ItemsSource = parsed.Messages.Select(ChatMessageViewModel.FromTranscriptEntry).ToArray();
            PreviewStatusTextBlock.Text = $"Preview entries: {parsed.Messages.Count}. Parsed lines: {parsed.Report.ParsedLines}. Invalid lines: {parsed.Report.InvalidLines}. Export parses the full rollout.";
            ChatScrollViewer.ScrollToTop();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PreviewStatusTextBlock.Text = $"Preview failed: {ex.Message}";
        }
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
        RefreshButton.IsEnabled = !isBusy;
        AnchorButton.IsEnabled = !isBusy;
        ExportButton.IsEnabled = !isBusy && _selectedThread?.Summary.RolloutExists == true;
        System.Windows.Input.Mouse.OverrideCursor = isBusy ? System.Windows.Input.Cursors.Wait : null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status);
        }
    }

    private void SetStatus(string status)
    {
        StatusTextBlock.Text = status;
    }

    private static void OpenInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = path,
            UseShellExecute = true
        });
    }
}

public sealed class SidebarGroupViewModel(string title, IEnumerable<ThreadRowViewModel> threads)
{
    public string Title { get; } = title;

    public ObservableCollection<ThreadRowViewModel> Threads { get; } = new(threads);

    public string CountText => Threads.Count.ToString();
}

public sealed class ThreadRowViewModel(ThreadSummary summary)
{
    public ThreadSummary Summary { get; } = summary;

    public string AnchorMark => Summary.IsAnchored ? "*" : string.Empty;

    public string Id => Summary.Id;

    public string Title => Summary.Title;

    public string Cwd => Summary.Cwd;

    public string UpdatedText => Summary.UpdatedAt == DateTimeOffset.MinValue ? "" : Summary.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string SizeText => FormatBytes(Summary.SizeBytes);

    public string FlagsText => Summary.Flags.Count == 0 ? "" : string.Join(", ", Summary.Flags);

    public string SidebarMeta => $"{ProjectLabel}  {UpdatedText}";

    public bool IsSmoked => Summary.Flags.Any(flag => flag is RecoveryFlag.MissingFromSessionIndex or RecoveryFlag.ExtendedPath or RecoveryFlag.RolloutOnly or RecoveryFlag.DbOnlyMissingFile);

    private string ProjectLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Summary.NormalizedCwd))
            {
                return "No project";
            }

            var label = Path.GetFileName(Summary.NormalizedCwd);
            return string.IsNullOrWhiteSpace(label) ? Summary.NormalizedCwd : label;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }
}

public sealed class ChatMessageViewModel
{
    public required string Header { get; init; }

    public required string Text { get; init; }

    public required HorizontalAlignment BubbleAlignment { get; init; }

    public required Brush BubbleBackground { get; init; }

    public required Brush BorderBrush { get; init; }

    public required Brush HeaderBrush { get; init; }

    public required FontFamily FontFamily { get; init; }

    public static ChatMessageViewModel FromTranscriptEntry(TranscriptEntry entry)
    {
        var phase = string.IsNullOrWhiteSpace(entry.Phase) ? string.Empty : $" / {entry.Phase}";
        var isUser = entry.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
        var isTool = entry.Kind is TranscriptEntryKind.ToolCall or TranscriptEntryKind.ToolOutput;
        var header = $"{entry.Role}{phase} / {entry.Kind} / line {entry.RawLineNumber}";

        return new ChatMessageViewModel
        {
            Header = header,
            Text = entry.Text,
            BubbleAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            BubbleBackground = isUser ? BrushFrom("#F2EFE8") : isTool ? BrushFrom("#F6F6F6") : Brushes.White,
            BorderBrush = isTool ? BrushFrom("#D8D6D0") : BrushFrom("#ECE9E3"),
            HeaderBrush = isTool ? BrushFrom("#6F6C66") : BrushFrom("#4A4843"),
            FontFamily = isTool ? new FontFamily("Consolas") : new FontFamily("Segoe UI")
        };
    }

    private static Brush BrushFrom(string color)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
