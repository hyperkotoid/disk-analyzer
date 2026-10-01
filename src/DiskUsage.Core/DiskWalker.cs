namespace DiskUsage.Core;

public sealed class DiskWalker
{
    private readonly IFileSystem _fileSystem;
    private readonly object _gate = new();
    private readonly Dictionary<string, ScanNode> _index = new(StringComparer.Ordinal);
    private readonly HashSet<(ulong Device, ulong Inode)> _visitedDirectories = [];
    private readonly HashSet<(ulong Device, ulong Inode)> _seenFiles = [];

    public DiskWalker(IFileSystem fileSystem) => _fileSystem = fileSystem;

    public long FilesVisited { get; private set; }
    public long CountedBytes { get; private set; }
    public int UnreadableDirectories { get; private set; }

    public async Task<ScanNode> WalkAsync(
        string root,
        Func<ScanNode, CancellationToken, ValueTask>? onTopLevelChild,
        Action<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rootPath = DiskPath.Normalize(root);
        var rootStat = _fileSystem.Stat(rootPath);
        _visitedDirectories.Add((rootStat.Device, rootStat.Inode));
        CountedBytes += rootStat.Length;

        var rootNode = new ScanNode
        {
            Name = rootStat.Name,
            Path = rootPath,
            Kind = EntryKind.Directory,
            Size = rootStat.Length
        };
        Index(rootNode);

        IReadOnlyList<FileRecord> children;
        try
        {
            children = _fileSystem.Enumerate(rootPath);
        }
        catch (Exception ex) when (IsAccessError(ex))
        {
            UnreadableDirectories++;
            return rootNode;
        }

        long sum = rootStat.Length;
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var built = Build(child, progress, cancellationToken);
            if (built == null)
                continue;

            lock (_gate)
                rootNode.Children.Add(built);

            sum += built.Size;
            rootNode.Size = sum;
            if (onTopLevelChild != null)
                await onTopLevelChild(built, cancellationToken);
        }

        rootNode.Size = sum;
        return rootNode;
    }

    public bool TryGetChildren(string path, out List<ScanEntryDto> entries)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(DiskPath.Normalize(path), out var node))
            {
                entries = [];
                return false;
            }

            entries = node.Children.Select(ScanEntryDto.From).ToList();
            return true;
        }
    }

    private ScanNode? Build(FileRecord entry, Action<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FilesVisited++;
        progress?.Invoke(new ScanProgress(FilesVisited, CountedBytes, entry.Path));

        if (entry.Kind == EntryKind.Link)
        {
            CountedBytes += entry.Length;
            return Leaf(entry, entry.Length);
        }

        if (entry.Kind == EntryKind.Directory)
        {
            if (!entry.CanDescend || !_visitedDirectories.Add((entry.Device, entry.Inode)))
                return null;

            var node = new ScanNode
            {
                Name = entry.Name,
                Path = entry.Path,
                Kind = EntryKind.Directory
            };

            IReadOnlyList<FileRecord> children;
            try
            {
                children = _fileSystem.Enumerate(entry.Path);
            }
            catch (Exception ex) when (IsAccessError(ex))
            {
                UnreadableDirectories++;
                node.Size = entry.Length;
                CountedBytes += entry.Length;
                Index(node);
                return node;
            }

            long sum = entry.Length;
            foreach (var child in children)
            {
                var built = Build(child, progress, cancellationToken);
                if (built == null)
                    continue;

                lock (_gate)
                    node.Children.Add(built);
                sum += built.Size;
            }

            node.Size = sum;
            CountedBytes += entry.Length;
            Index(node);
            return node;
        }

        var size = AttributedFileSize(entry);
        CountedBytes += size;
        return Leaf(entry, size);
    }

    private long AttributedFileSize(FileRecord entry)
    {
        if (entry.LinkCount > 1 && !_seenFiles.Add((entry.Device, entry.Inode)))
            return 0;

        return entry.Length;
    }

    private static ScanNode Leaf(FileRecord entry, long size) => new()
    {
        Name = entry.Name,
        Path = entry.Path,
        Kind = entry.Kind,
        Size = size
    };

    private void Index(ScanNode node)
    {
        lock (_gate)
            _index[node.Path] = node;
    }

    private static bool IsAccessError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
