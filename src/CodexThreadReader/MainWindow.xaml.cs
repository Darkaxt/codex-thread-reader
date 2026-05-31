using CodexThreadReader.Core;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexThreadReader;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ThreadRowViewModel> _allThreads = [];
    private readonly ObservableCollection<SidebarGroupViewModel> _sidebarGroups = [];
    private readonly string _settingsRoot;
    private readonly string _anchorPath;
    private readonly string _settingsPath;
    private readonly string _exportRoot;
    private AnchorStore _anchorStore;
    private ThemeSettingsStore _themeSettings;
    private ThemePalette _palette = ThemePalette.Light;
    private CancellationTokenSource? _previewCancellation;
    private ThreadRowViewModel? _selectedThread;
    private bool _initializingTheme;

    public MainWindow()
    {
        _settingsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexThreadReader");
        _anchorPath = Path.Combine(_settingsRoot, "anchors.json");
        _settingsPath = Path.Combine(_settingsRoot, "settings.json");
        _exportRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CodexThreadReader", "Exports");
        _anchorStore = new AnchorStore(_anchorPath, Array.Empty<string>());
        _themeSettings = new ThemeSettingsStore(_settingsPath, ThemeMode.System);
        InitializeComponent();
        ThreadsTree.DataContext = _sidebarGroups;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settingsRoot);
        Directory.CreateDirectory(_exportRoot);
        _anchorStore = await AnchorStore.LoadAsync(_anchorPath, CancellationToken.None);
        _themeSettings = await ThemeSettingsStore.LoadAsync(_settingsPath, CancellationToken.None);
        SelectThemeComboBoxItem(_themeSettings.Mode);
        ApplyTheme(_themeSettings.Mode);
        await RefreshCatalogAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCatalogAsync();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingTheme || ThemeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var mode = ParseThemeMode(item.Tag?.ToString());
        _themeSettings = _themeSettings.WithMode(mode);
        await _themeSettings.SaveAsync(CancellationToken.None);
        ApplyTheme(mode);
        if (_selectedThread is not null)
        {
            await LoadPreviewAsync(_selectedThread);
        }
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
        var filter = (FilterComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
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
            ChatItemsControl.ItemsSource = parsed.Messages.Select(entry => ChatMessageViewModel.FromTranscriptEntry(entry, _palette)).ToArray();
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

    private void SelectThemeComboBoxItem(ThemeMode mode)
    {
        _initializingTheme = true;
        foreach (var item in ThemeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (ParseThemeMode(item.Tag?.ToString()) == mode)
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }

        _initializingTheme = false;
    }

    private void ApplyTheme(ThemeMode mode)
    {
        var effectiveMode = mode == ThemeMode.System ? GetSystemThemeMode() : mode;
        _palette = effectiveMode == ThemeMode.Dark ? ThemePalette.Dark : ThemePalette.Light;
        SetBrush("WindowBackgroundBrush", _palette.WindowBackground);
        SetBrush("SidebarBackgroundBrush", _palette.SidebarBackground);
        SetBrush("PanelBackgroundBrush", _palette.PanelBackground);
        SetBrush("BorderBrush", _palette.Border);
        SetBrush("TextBrush", _palette.Text);
        SetBrush("MutedTextBrush", _palette.MutedText);
        SetBrush("SubtleTextBrush", _palette.SubtleText);
        SetBrush("AccentTextBrush", _palette.AccentText);
        SetBrush("InputBackgroundBrush", _palette.InputBackground);
        SetBrush("DisabledBackgroundBrush", _palette.DisabledBackground);
        SetBrush("HoverBackgroundBrush", _palette.HoverBackground);
        SetBrush("ThumbBrush", _palette.Thumb);
        SetBrush("ChatAssistantBrush", _palette.ChatAssistant);
        SetBrush("ChatUserBrush", _palette.ChatUser);
        SetBrush("ChatToolBrush", _palette.ChatTool);

        Resources[SystemColors.HighlightBrushKey] = _palette.Selection;
        Resources[SystemColors.ControlBrushKey] = _palette.Selection;
        Resources[SystemColors.HighlightTextBrushKey] = _palette.Text;
    }

    private void SetBrush(string key, Brush brush)
    {
        Resources[key] = brush;
    }

    private static ThemeMode ParseThemeMode(string? value)
    {
        return Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var mode) ? mode : ThemeMode.System;
    }

    private static ThemeMode GetSystemThemeMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
            return appsUseLightTheme is int value && value == 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Light;
        }
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

    public string SidebarMeta => $"{StatusLabel}  {UpdatedText}";

    public bool IsSmoked => Summary.Flags.Any(flag => flag is RecoveryFlag.MissingFromSessionIndex or RecoveryFlag.ExtendedPath or RecoveryFlag.RolloutOnly or RecoveryFlag.DbOnlyMissingFile);

    private string StatusLabel
    {
        get
        {
            if (Summary.IsAnchored)
            {
                return "Pinned";
            }

            if (Summary.IsArchived)
            {
                return "Archived";
            }

            return IsSmoked ? "Recovery" : "Active";
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

    public required Brush TextBrush { get; init; }

    public required FontFamily FontFamily { get; init; }

    public static ChatMessageViewModel FromTranscriptEntry(TranscriptEntry entry, ThemePalette palette)
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
            BubbleBackground = isUser ? palette.ChatUser : isTool ? palette.ChatTool : palette.ChatAssistant,
            BorderBrush = isTool ? palette.Border : palette.ChatBorder,
            HeaderBrush = isTool ? palette.MutedText : palette.SubtleText,
            TextBrush = palette.Text,
            FontFamily = isTool ? new FontFamily("Consolas") : new FontFamily("Segoe UI")
        };
    }
}

public sealed record ThemePalette(
    Brush WindowBackground,
    Brush SidebarBackground,
    Brush PanelBackground,
    Brush Border,
    Brush Text,
    Brush MutedText,
    Brush SubtleText,
    Brush AccentText,
    Brush InputBackground,
    Brush DisabledBackground,
    Brush HoverBackground,
    Brush Thumb,
    Brush Selection,
    Brush ChatAssistant,
    Brush ChatUser,
    Brush ChatTool,
    Brush ChatBorder)
{
    public static ThemePalette Light { get; } = new(
        BrushFrom("#FFFFFF"),
        BrushFrom("#F7F6F2"),
        BrushFrom("#FFFFFF"),
        BrushFrom("#E5E2DC"),
        BrushFrom("#202124"),
        BrushFrom("#6F6C66"),
        BrushFrom("#8B8780"),
        BrushFrom("#7C5C00"),
        BrushFrom("#FFFFFF"),
        BrushFrom("#F1EFEA"),
        BrushFrom("#EEEAE3"),
        BrushFrom("#C9C4BA"),
        BrushFrom("#E6E3DC"),
        BrushFrom("#FFFFFF"),
        BrushFrom("#F2EFE8"),
        BrushFrom("#F6F6F6"),
        BrushFrom("#ECE9E3"));

    public static ThemePalette Dark { get; } = new(
        BrushFrom("#101213"),
        BrushFrom("#162126"),
        BrushFrom("#151515"),
        BrushFrom("#2A2D2F"),
        BrushFrom("#ECEDEE"),
        BrushFrom("#A8ADB2"),
        BrushFrom("#858B91"),
        BrushFrom("#E1B866"),
        BrushFrom("#202325"),
        BrushFrom("#171A1C"),
        BrushFrom("#283136"),
        BrushFrom("#5F6B73"),
        BrushFrom("#293236"),
        BrushFrom("#181A1B"),
        BrushFrom("#252525"),
        BrushFrom("#1F2326"),
        BrushFrom("#2F3336"));

    private static Brush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
