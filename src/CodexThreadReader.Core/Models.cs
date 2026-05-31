namespace CodexThreadReader.Core;

public enum RecoveryFlag
{
    Anchored,
    Archived,
    MissingFromSessionIndex,
    ExtendedPath,
    RolloutOnly,
    DbOnlyMissingFile,
    ProjectOrderMismatch,
    LargeRollout
}

public enum TranscriptEntryKind
{
    Message,
    ToolCall,
    ToolOutput,
    Compaction,
    Context
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public sealed record ThreadCatalogOptions(string CodexHome, string? AnchorStorePath = null);

public sealed record ThreadCatalogResult(IReadOnlyList<ThreadSummary> Threads, IReadOnlyList<string> Errors);

public sealed record ThreadSummary(
    string Id,
    string Title,
    string Cwd,
    string NormalizedCwd,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string RolloutPath,
    bool RolloutExists,
    long SizeBytes,
    bool IsAnchored,
    IReadOnlyList<RecoveryFlag> Flags);

public sealed record RolloutMetadata(
    string Id,
    DateTimeOffset Timestamp,
    string Cwd,
    string Originator,
    string CliVersion,
    string Source,
    string ModelProvider);

public sealed record TranscriptEntry(
    int RawLineNumber,
    DateTimeOffset? Timestamp,
    string Role,
    string? Phase,
    TranscriptEntryKind Kind,
    string Text);

public sealed record ParseReport(int TotalLines, int ParsedLines, int InvalidLines, int OmittedLargeFields);

public sealed record ParsedThread(
    RolloutMetadata? Metadata,
    IReadOnlyList<TranscriptEntry> Messages,
    ParseReport Report);

public sealed record ExportManifest(
    string ExportDirectory,
    string HtmlPath,
    string NormalizedJsonPath,
    string RawJsonlPath,
    string HandoffMarkdownPath);
