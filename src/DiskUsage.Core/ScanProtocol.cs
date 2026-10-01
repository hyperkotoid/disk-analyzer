using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiskUsage.Core;

public static class ScanTypes
{
    public const string Progress = "progress";
    public const string Directory = "directory";
    public const string Done = "done";
    public const string Error = "error";
    public const string List = "list";
    public const string Cancel = "cancel";
}

public sealed class ScanEntryDto
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "file";
    public long Size { get; init; }

    public static ScanEntryDto From(ScanNode node) => new()
    {
        Name = node.Name,
        Path = node.Path,
        Kind = node.Kind switch
        {
            EntryKind.Directory => "directory",
            EntryKind.Link => "link",
            _ => "file"
        },
        Size = node.Size
    };

    public static EntryKind ParseKind(string? kind) => kind switch
    {
        "directory" => EntryKind.Directory,
        "link" => EntryKind.Link,
        _ => EntryKind.File
    };
}

public sealed class ScanMessage
{
    public string Type { get; init; } = "";
    public string? Path { get; init; }
    public string? CurrentPath { get; init; }
    public long Files { get; init; }
    public long Bytes { get; init; }
    public bool Incremental { get; init; }
    public bool Cancelled { get; init; }
    public string? Message { get; init; }
    public List<ScanEntryDto>? Entries { get; init; }
}

public static class ScanProtocol
{
    public static string Serialize(ScanMessage message) =>
        JsonSerializer.Serialize(message, ScanJsonContext.Default.ScanMessage);

    public static ScanMessage Deserialize(string line) =>
        JsonSerializer.Deserialize(line, ScanJsonContext.Default.ScanMessage)
        ?? throw new InvalidDataException("Пустое сообщение сканера.");
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScanMessage))]
[JsonSerializable(typeof(ScanEntryDto))]
internal partial class ScanJsonContext : JsonSerializerContext;
