using DiskUsage.Core;

namespace DiskUsage.Core.Tests;

public class DiskWalkerTests
{
    [Fact]
    public async Task SumsTreeSkipsLinksMountsAndDuplicateHardLinks()
    {
        var fs = new FakeFileSystem();
        fs.Add(Dir("/", 100, 1), null);
        fs.Add(File("/a.txt", 10, 2), "/");
        fs.Add(Dir("/sub", 50, 3), "/");
        fs.Add(File("/sub/b.txt", 30, 4), "/sub");
        fs.Add(Link("/link", 8, 5), "/");
        fs.Add(Dir("/mnt", 999, 6, canDescend: false, device: 9), "/");
        fs.Add(File("/mnt/hidden.txt", 5000, 60, device: 9), "/mnt");
        fs.Add(File("/h1", 40, 7, linkCount: 2), "/");
        fs.Add(File("/h2", 40, 7, linkCount: 2), "/");

        var walker = new DiskWalker(fs);
        var published = new List<string>();
        var root = await walker.WalkAsync("/", (node, _) =>
        {
            published.Add(node.Name);
            return ValueTask.CompletedTask;
        }, null, CancellationToken.None);

        Assert.Equal(238, root.Size);
        Assert.Equal(80, root.Children.Single(child => child.Name == "sub").Size);
        Assert.Equal(["a.txt", "sub", "link", "h1", "h2"], published);
        Assert.DoesNotContain(root.Children, child => child.Name == "mnt");
        Assert.Equal(0, root.Children.Single(child => child.Name == "h2").Size);
        Assert.Equal(["/", "/sub"], fs.Enumerated);
        Assert.Equal(root.Size, walker.CountedBytes);
    }

    [Fact]
    public async Task SkipsDirectoryInodeAlreadyCounted()
    {
        var fs = new FakeFileSystem();
        fs.Add(Dir("/", 10, 1), null);
        fs.Add(Dir("/Users", 20, 10), "/");
        fs.Add(File("/Users/a.txt", 100, 11), "/Users");
        fs.Add(Dir("/Alias", 20, 10), "/");
        fs.Add(File("/Alias/a.txt", 100, 11), "/Alias");

        var root = await new DiskWalker(fs).WalkAsync("/", null, null, CancellationToken.None);

        Assert.Equal(130, root.Size);
        Assert.Single(root.Children);
        Assert.Equal("/Users", root.Children[0].Path);
        Assert.DoesNotContain("/Alias", fs.Enumerated);
    }

    [Fact]
    public async Task KeepsOwnSizeWhenDirectoryCannotBeRead()
    {
        var fs = new FakeFileSystem();
        fs.Add(Dir("/", 5, 1), null);
        fs.Add(Dir("/secret", 12, 2), "/");
        fs.Deny.Add("/secret");

        var walker = new DiskWalker(fs);
        var root = await walker.WalkAsync("/", null, null, CancellationToken.None);

        Assert.Equal(17, root.Size);
        Assert.Equal(12, root.Children.Single().Size);
        Assert.Equal(1, walker.UnreadableDirectories);
        Assert.True(walker.TryGetChildren("/secret", out var children));
        Assert.Empty(children);
    }

    [Fact]
    public async Task ReportsTopLevelChildrenAndSupportsList()
    {
        var fs = new FakeFileSystem();
        fs.Add(Dir("/", 1, 1), null);
        fs.Add(Dir("/docs", 2, 2), "/");
        fs.Add(File("/docs/a", 9, 3), "/docs");

        var walker = new DiskWalker(fs);
        await walker.WalkAsync("/", null, null, CancellationToken.None);

        Assert.True(walker.TryGetChildren("/docs", out var children));
        var child = Assert.Single(children);
        Assert.Equal("a", child.Name);
        Assert.Equal(9, child.Size);
        Assert.Equal("file", child.Kind);
    }

    [Fact]
    public async Task CancelStopsTheWalk()
    {
        var fs = new FakeFileSystem();
        fs.Add(Dir("/", 1, 1), null);
        fs.Add(File("/a", 1, 2), "/");
        fs.Add(File("/b", 1, 3), "/");
        var cts = new CancellationTokenSource();

        var walker = new DiskWalker(fs);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            walker.WalkAsync("/", (_, _) =>
            {
                cts.Cancel();
                return ValueTask.CompletedTask;
            }, null, cts.Token));
    }

    private static FileRecord Dir(string path, long length, ulong inode, bool canDescend = true, ulong device = 1) =>
        Entry(path, length, inode, EntryKind.Directory, canDescend, device, 1);

    private static FileRecord File(string path, long length, ulong inode, ulong device = 1, int linkCount = 1) =>
        Entry(path, length, inode, EntryKind.File, false, device, linkCount);

    private static FileRecord Link(string path, long length, ulong inode) =>
        Entry(path, length, inode, EntryKind.Link, false, 1, 1);

    private static FileRecord Entry(
        string path,
        long length,
        ulong inode,
        EntryKind kind,
        bool canDescend,
        ulong device,
        int linkCount) => new()
    {
        Name = path == "/" ? "/" : Path.GetFileName(path),
        Path = path,
        Kind = kind,
        Length = length,
        Device = device,
        Inode = inode,
        LinkCount = linkCount,
        CanDescend = canDescend
    };
}
