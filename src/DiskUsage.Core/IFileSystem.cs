namespace DiskUsage.Core;

public interface IFileSystem
{
    VolumeInfo GetVolume(string path);
    FileRecord Stat(string path);
    IReadOnlyList<FileRecord> Enumerate(string directoryPath);
}
