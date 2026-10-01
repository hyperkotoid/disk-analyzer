using DiskUsage.Core;

namespace DiskUsage.Core.Tests;

internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, FileRecord> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);

    public VolumeInfo Volume { get; set; } = new()
    {
        Name = "Test",
        RootPath = "/",
        TotalBytes = 1000,
        FreeBytes = 100
    };

    public HashSet<string> Deny { get; } = new(StringComparer.Ordinal);
    public List<string> Enumerated { get; } = [];

    public void Add(FileRecord record, string? parent)
    {
        _nodes[record.Path] = record;
        _children.TryAdd(record.Path, []);
        if (parent == null)
            return;

        if (!_children.TryGetValue(parent, out var list))
        {
            list = [];
            _children[parent] = list;
        }

        list.Add(record.Path);
    }

    public VolumeInfo GetVolume(string path) => Volume;

    public FileRecord Stat(string path) => _nodes[path];

    public IReadOnlyList<FileRecord> Enumerate(string directoryPath)
    {
        Enumerated.Add(directoryPath);
        if (Deny.Contains(directoryPath))
            throw new UnauthorizedAccessException(directoryPath);

        return _children[directoryPath].Select(path => _nodes[path]).ToList();
    }
}
