using DiskUsage.Core;

namespace DiskUsage.Core.Tests;

public class MacFileSystemTests
{
    [Fact]
    public void ReadsBootVolumeAndVolumeBoundaries()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var fs = new MacFileSystem();
        var volume = fs.GetVolume("/");
        Assert.False(string.IsNullOrWhiteSpace(volume.Name));
        Assert.InRange(volume.TotalBytes, 1_000_000_000L, 100L * 1024 * 1024 * 1024 * 1024);
        Assert.InRange(volume.FreeBytes, 0, volume.TotalBytes);

        var root = fs.Stat("/");
        Assert.Equal(EntryKind.Directory, root.Kind);
        Assert.True(root.Inode > 0);
        Assert.True(root.Length > 0);

        var top = fs.Enumerate("/");
        var users = Assert.Single(top, entry => entry.Name == "Users");
        Assert.Equal(EntryKind.Directory, users.Kind);
        Assert.True(users.CanDescend);

        var etc = top.SingleOrDefault(entry => entry.Name == "etc");
        if (etc != null)
            Assert.Equal(EntryKind.Link, etc.Kind);

        var dev = top.SingleOrDefault(entry => entry.Name == "dev");
        if (dev != null)
            Assert.False(dev.CanDescend);

        if (!Directory.Exists("/System/Volumes/Data"))
            return;

        var volumes = fs.Enumerate("/System/Volumes");
        var data = Assert.Single(volumes, entry => entry.Name == "Data");
        Assert.False(data.CanDescend);

        var preboot = volumes.SingleOrDefault(entry => entry.Name == "Preboot");
        if (preboot != null)
            Assert.True(preboot.CanDescend);
    }
}
