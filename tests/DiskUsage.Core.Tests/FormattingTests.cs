using DiskUsage.Core;

namespace DiskUsage.Core.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(1536, "1,5 КБ")]
    [InlineData(1048576, "1 МБ")]
    public void FormatsBinaryUnits(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormat.FormatBytes(bytes));

    [Fact]
    public void FormatsShareOfDisk()
    {
        Assert.Equal(50, SizeFormat.Percent(500, 1000));
        Assert.Equal("50,0 %", SizeFormat.FormatPercent(500, 1000));
        Assert.Equal("< 0,1 %", SizeFormat.FormatPercent(1, 100_000));
        Assert.Equal("0 %", SizeFormat.FormatPercent(0, 1000));
    }

    [Fact]
    public void SortsBySizeThenName()
    {
        var items = new (long Size, string Name)[] { (1, "c"), (10, "b"), (10, "a") };
        var ordered = items
            .OrderBy(item => item, Comparer<(long Size, string Name)>.Create((left, right) =>
                EntrySort.BySizeDescending(left.Size, left.Name, right.Size, right.Name)))
            .Select(item => item.Name)
            .ToArray();

        Assert.Equal(["a", "b", "c"], ordered);
    }

    [Fact]
    public void BuildsParentAndCrumbs()
    {
        Assert.Null(DiskPath.Parent("/"));
        Assert.Equal("/", DiskPath.Parent("/Users"));
        Assert.Equal("/Users", DiskPath.Parent("/Users/dennis/"));
        Assert.Equal(
            [("Корень", "/"), ("Users", "/Users"), ("dennis", "/Users/dennis")],
            DiskPath.Crumbs("/Users/dennis"));
    }

    [Fact]
    public void QuotesShellAndAppleScript()
    {
        Assert.Equal("'/tmp/a'", ProcessQuote.ShellSingleQuote("/tmp/a"));
        Assert.Equal("'a'\\''b'", ProcessQuote.ShellSingleQuote("a'b"));
        Assert.Equal("\"a\\\"b\\\\c\"", ProcessQuote.AppleScriptString("a\"b\\c"));
    }

    [Fact]
    public void RoundTripsScanMessage()
    {
        var message = new ScanMessage
        {
            Type = ScanTypes.Directory,
            Path = "/",
            Incremental = true,
            Entries =
            [
                new ScanEntryDto { Name = "Пользователи", Path = "/Users", Kind = "directory", Size = 42 }
            ]
        };

        var restored = ScanProtocol.Deserialize(ScanProtocol.Serialize(message));

        Assert.Equal(ScanTypes.Directory, restored.Type);
        Assert.True(restored.Incremental);
        var entry = Assert.Single(restored.Entries!);
        Assert.Equal("Пользователи", entry.Name);
        Assert.Equal(42, entry.Size);
        Assert.Equal(EntryKind.Directory, ScanEntryDto.ParseKind(entry.Kind));
        Assert.DoesNotContain('\n', ScanProtocol.Serialize(message));
    }
}
