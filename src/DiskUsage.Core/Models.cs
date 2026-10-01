namespace DiskUsage.Core;

public enum EntryKind
{
    File,
    Directory,
    Link
}

public sealed class VolumeInfo
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public required long TotalBytes { get; init; }
    public required long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;
}

public sealed class FileRecord
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required EntryKind Kind { get; init; }
    public required long Length { get; init; }
    public required ulong Device { get; init; }
    public required ulong Inode { get; init; }
    public required int LinkCount { get; init; }

    /// <summary>
    /// False for files, links, foreign mounts, and the APFS Data volume mount.
    /// </summary>
    public required bool CanDescend { get; init; }
}

public sealed class ScanNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required EntryKind Kind { get; init; }
    public long Size { get; set; }
    public List<ScanNode> Children { get; } = [];
}

public readonly record struct ScanProgress(long FilesVisited, long CountedBytes, string CurrentPath);
