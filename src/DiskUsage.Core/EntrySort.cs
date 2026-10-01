namespace DiskUsage.Core;

public static class EntrySort
{
    public static int BySizeDescending(long sizeA, string nameA, long sizeB, string nameB)
    {
        var bySize = sizeB.CompareTo(sizeA);
        if (bySize != 0)
            return bySize;

        return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
    }
}
