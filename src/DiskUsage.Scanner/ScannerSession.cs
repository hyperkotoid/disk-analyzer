using System.Net.Sockets;
using System.Text;
using DiskUsage.Core;

namespace DiskUsage.Scanner;

public static class ScannerSession
{
    public static async Task<int> RunAsync(string socketPath, string root)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        await using var stream = new NetworkStream(socket, ownsSocket: true);
        return await RunAsync(stream, root, CancellationToken.None);
    }

    public static async Task<int> RunAsync(Stream stream, string root, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };
        var writeLock = new object();

        void Send(ScanMessage message)
        {
            var line = ScanProtocol.Serialize(message);
            lock (writeLock)
            {
                writer.WriteLine(line);
                writer.Flush();
            }
        }

        using var walkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var walker = new DiskWalker(new MacFileSystem());
        var lastProgress = long.MinValue;
        var rootPath = DiskPath.Normalize(root);

        var readTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null)
                    {
                        walkCancellation.Cancel();
                        break;
                    }

                    ScanMessage incoming;
                    try
                    {
                        incoming = ScanProtocol.Deserialize(line);
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        continue;
                    }

                    if (incoming.Type == ScanTypes.Cancel)
                        walkCancellation.Cancel();
                    else if (incoming.Type == ScanTypes.List && !string.IsNullOrWhiteSpace(incoming.Path))
                        SendDirectory(incoming.Path);
                }
            }
            catch (OperationCanceledException)
            {
                walkCancellation.Cancel();
            }
            catch (IOException)
            {
                walkCancellation.Cancel();
            }
        });

        var code = 0;
        try
        {
            var rootNode = await walker.WalkAsync(
                rootPath,
                (child, _) =>
                {
                    Send(new ScanMessage
                    {
                        Type = ScanTypes.Directory,
                        Path = rootPath,
                        Incremental = true,
                        Entries = [ScanEntryDto.From(child)]
                    });
                    return ValueTask.CompletedTask;
                },
                progress =>
                {
                    var now = Environment.TickCount64;
                    if (now - lastProgress < 200)
                        return;

                    lastProgress = now;
                    Send(new ScanMessage
                    {
                        Type = ScanTypes.Progress,
                        Files = progress.FilesVisited,
                        Bytes = progress.CountedBytes,
                        CurrentPath = progress.CurrentPath
                    });
                },
                walkCancellation.Token);

            Send(new ScanMessage
            {
                Type = ScanTypes.Done,
                Files = walker.FilesVisited,
                Bytes = rootNode.Size,
                Message = walker.UnreadableDirectories > 0
                    ? $"Не удалось прочитать каталогов: {walker.UnreadableDirectories}."
                    : null
            });
        }
        catch (OperationCanceledException)
        {
            try
            {
                Send(new ScanMessage
                {
                    Type = ScanTypes.Done,
                    Cancelled = true,
                    Files = walker.FilesVisited,
                    Bytes = walker.CountedBytes
                });
            }
            catch (IOException)
            {
            }
        }
        catch (Exception ex)
        {
            try
            {
                Send(new ScanMessage { Type = ScanTypes.Error, Message = ex.Message });
            }
            catch (IOException)
            {
            }

            code = 1;
        }

        try
        {
            await readTask;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }

        return code;

        void SendDirectory(string path)
        {
            if (walker.TryGetChildren(path, out var children))
            {
                Send(new ScanMessage
                {
                    Type = ScanTypes.Directory,
                    Path = DiskPath.Normalize(path),
                    Incremental = false,
                    Entries = children
                });
                return;
            }

            Send(new ScanMessage
            {
                Type = ScanTypes.Error,
                Path = path,
                Message = "Каталог ещё не готов."
            });
        }
    }
}
