using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskUsage.Core;

public sealed class MacFileSystem : IFileSystem
{
    private const int StatFsSize = 2168;
    private const int FsTypeOffset = 72;
    private const int FsTypeLength = 16;
    private const int MountOnOffset = 88;
    private const int MountFromOffset = 1112;
    private const int MountNameLength = 1024;
    private const int MountNoWait = 2;
    // Firmlinks such as /Users share inodes with this mount, so walking it would count the same files twice.
    private const string DataVolumeMount = "/System/Volumes/Data";

    private static readonly Regex DiskId = new(@"(?:^|/)(disk\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Lazy<VolumeGroup> _group = new(LoadGroup);

    public VolumeInfo GetVolume(string path)
    {
        EnsureMac();
        var normalized = DiskPath.Normalize(path);
        var buffer = new byte[StatFsSize];
        if (StatFs(normalized, buffer) != 0)
            throw new IOException($"Не удалось прочитать сведения о томе ({Marshal.GetLastPInvokeError()}).");

        var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var blocks = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(8));
        var freeBlocks = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(16));
        if (freeBlocks > blocks)
            freeBlocks = blocks;

        var total = blockSize == 0 ? 0 : checked((long)(blocks * blockSize));
        var free = blockSize == 0 ? 0 : checked((long)(freeBlocks * blockSize));
        return new VolumeInfo
        {
            Name = TryVolumeName(normalized) ?? "Загрузочный диск",
            RootPath = normalized,
            TotalBytes = total,
            FreeBytes = free
        };
    }

    public FileRecord Stat(string path)
    {
        EnsureMac();
        return TryStat(path) ?? throw new FileNotFoundException("Не удалось прочитать файл.", path);
    }

    public IReadOnlyList<FileRecord> Enumerate(string directoryPath)
    {
        EnsureMac();
        var result = new List<FileRecord>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            var record = TryStat(entry);
            if (record != null)
                result.Add(record);
        }

        return result;
    }

    private FileRecord? TryStat(string path)
    {
        if (LStat(path, out var stat) != 0)
            return null;

        var normalized = DiskPath.Normalize(path);
        var name = normalized == "/" ? "/" : System.IO.Path.GetFileName(normalized);
        if (name is "." or "..")
            return null;

        var kind = KindOf(stat.Mode);
        return new FileRecord
        {
            Name = name,
            Path = normalized,
            Kind = kind,
            Length = stat.Size < 0 ? 0 : stat.Size,
            Device = (ulong)(uint)stat.Dev,
            Inode = stat.Ino,
            LinkCount = stat.Nlink,
            CanDescend = CanDescend(normalized, (ulong)(uint)stat.Dev, kind)
        };
    }

    private bool CanDescend(string path, ulong device, EntryKind kind)
    {
        if (kind != EntryKind.Directory)
            return false;

        var group = _group.Value;
        if (group.BlockedPaths.Contains(path))
            return false;

        return group.AllowedDevices.Contains(device);
    }

    private static VolumeGroup LoadGroup()
    {
        var root = TryLStatDevice("/");
        var allowed = new HashSet<ulong>();
        if (root != null)
            allowed.Add(root.Value);

        var blocked = new HashSet<string>(StringComparer.Ordinal) { DataVolumeMount };
        var rootContainer = ContainerOf(MountSource("/"));
        var count = GetMntInfo(out var entries, MountNoWait);
        if (count > 0 && entries != IntPtr.Zero && rootContainer != null)
        {
            for (var index = 0; index < count; index++)
            {
                var bytes = new byte[StatFsSize];
                Marshal.Copy(IntPtr.Add(entries, index * StatFsSize), bytes, 0, StatFsSize);
                var mountedOn = ReadCString(bytes, MountOnOffset, MountNameLength);
                var mountedFrom = ReadCString(bytes, MountFromOffset, MountNameLength);
                if (!string.Equals(ContainerOf(mountedFrom), rootContainer, StringComparison.Ordinal))
                    continue;

                var device = TryLStatDevice(mountedOn);
                if (device != null)
                    allowed.Add(device.Value);
            }
        }

        return new VolumeGroup(allowed, blocked);
    }

    private static string? MountSource(string path)
    {
        var buffer = new byte[StatFsSize];
        if (StatFs(path, buffer) != 0)
            return null;

        return ReadCString(buffer, MountFromOffset, MountNameLength);
    }

    private static string? ContainerOf(string? mountSource)
    {
        if (string.IsNullOrEmpty(mountSource))
            return null;

        var match = DiskId.Match(mountSource);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static ulong? TryLStatDevice(string path)
    {
        if (LStat(path, out var stat) != 0)
            return null;

        return (ulong)(uint)stat.Dev;
    }

    private static string? TryVolumeName(string path)
    {
        var attributes = new AttrList
        {
            BitmapCount = 5,
            VolAttr = 0x80000000 | 0x00000001
        };
        var buffer = new byte[512];
        if (GetAttrList(path, ref attributes, buffer, (nuint)buffer.Length, 0) != 0 || buffer.Length < 12)
            return null;

        var offset = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8));
        var start = 4 + offset;
        if (offset < 8 || length <= 1 || start < 0 || start >= buffer.Length)
            return null;

        var nameLength = length - 1;
        if (start + nameLength > buffer.Length)
            nameLength = buffer.Length - start;

        var name = Encoding.UTF8.GetString(buffer, start, nameLength);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string ReadCString(byte[] buffer, int offset, int maxLength)
    {
        var count = 0;
        var end = Math.Min(buffer.Length, offset + maxLength);
        for (var index = offset; index < end && buffer[index] != 0; index++)
            count++;

        return Encoding.UTF8.GetString(buffer, offset, count);
    }

    private static EntryKind KindOf(ushort mode)
    {
        var type = (ushort)(mode & 0xF000);
        if (type == 0xA000)
            return EntryKind.Link;
        if (type == 0x4000)
            return EntryKind.Directory;
        return EntryKind.File;
    }

    private static void EnsureMac()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Чтение тома macOS доступно только на macOS.");
    }

    private sealed class VolumeGroup(HashSet<ulong> allowedDevices, HashSet<string> blockedPaths)
    {
        public HashSet<ulong> AllowedDevices { get; } = allowedDevices;
        public HashSet<string> BlockedPaths { get; } = blockedPaths;
    }

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct StatBuf
    {
        [FieldOffset(0)] public int Dev;
        [FieldOffset(4)] public ushort Mode;
        [FieldOffset(6)] public ushort Nlink;
        [FieldOffset(8)] public ulong Ino;
        [FieldOffset(96)] public long Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttrList
    {
        public ushort BitmapCount;
        public ushort Reserved;
        public uint CommonAttr;
        public uint VolAttr;
        public uint DirAttr;
        public uint FileAttr;
        public uint ForkAttr;
    }

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out StatBuf stat);

    [DllImport("libc", EntryPoint = "statfs", SetLastError = true)]
    private static extern int StatFs(string path, byte[] buffer);

    [DllImport("libc", EntryPoint = "getmntinfo", SetLastError = true)]
    private static extern int GetMntInfo(out IntPtr entries, int flags);

    [DllImport("libc", EntryPoint = "getattrlist", SetLastError = true)]
    private static extern int GetAttrList(string path, ref AttrList attributes, byte[] buffer, nuint bufferSize, ulong options);
}
