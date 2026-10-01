# Disk Usage Analyzer

A macOS desktop app that shows how full the startup disk is and lets you browse the file system by size.

The window stays in your user session. Scanning runs in a separate process with administrator rights, so protected directories can be measured. The interface is Avalonia on .NET 10, which leaves room to add Windows and Linux later without rewriting the UI.

## Requirements

- macOS
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Run

```bash
dotnet run --project src/DiskUsage.App
```

On startup, macOS asks for an administrator password. Cancelling that dialog leaves the window open and does not start a scan. **Scan again** asks for permission and starts over. **Stop** ends the walk that is in progress.

## What the window shows

The header is the startup volume: name, used space, total capacity, and a fullness bar. Those numbers come from `statfs` on `/`, so they match the system view, including APFS snapshots and other space that is not a normal file.

The list starts at `/`. Each row is a file or folder, sorted by size descending:

- **Name**, with a short kind label (folder, file, or link)
- **Size**, the logical size of that item and everything inside it
- **Share of disk**, that size as a percentage of the volume’s total capacity, plus a bar

Click a folder to open it. **Back** and the breadcrumbs move up. Already scanned levels open immediately. Clicking a file does nothing. While the walk is running, top-level rows appear as each branch finishes, and the status line shows how many files have been visited and the current path.

Sizes use binary units (1024): KB, MB, GB. The interface text is Russian.

## How sizes are counted

The scanner walks from `/` with `lstat`, so symbolic links are not followed. Hard links are counted once, by device and inode. Mounts outside the startup APFS container are skipped, including external disks under `/Volumes`, `devfs`, and network mounts. Volumes in the same container, such as Preboot and VM, are included.

macOS firmlinks (`/Users`, `/Applications`, and the others) are walked, because that is where user data lives. `/System/Volumes/Data` itself is not walked: those files are the same inodes as the firmlinks, and walking both would count them twice.

The sum of the rows will often be smaller than **used** in the header. Snapshots, compression, and APFS clones take space that a logical file walk does not see.

## Layout

| Project | Role |
| --- | --- |
| `src/DiskUsage.App` | Avalonia UI. Copies the scanner into its output directory and launches it. |
| `src/DiskUsage.Scanner` | Console process that walks the disk. |
| `src/DiskUsage.Core` | Models, the walk, and the message contract. No Avalonia dependency. |
| `tests/DiskUsage.Core.Tests` | Aggregation, sorting, link handling, and mount boundaries. |

Two seams in Core are there for other operating systems: `IFileSystem` reads directory entries and volume info, and `IElevatedScanHost` starts the scanner and exchanges messages. Only macOS implements elevation today. On another OS, starting a scan reports that privilege elevation is not implemented yet.

## Elevation and protocol

The UI listens on a Unix socket under `/tmp`, mode `0600`, then runs:

```text
osascript -e 'do shell script "<scanner> --socket <path>" with administrator privileges'
```

`do shell script` does not stream stdout until the process exits, so progress and results travel over the socket as newline-delimited JSON.

- Scanner to UI: `progress`, `directory`, `done`, `error`
- UI to scanner: `list` for the children of a path that is already measured, and `cancel`

Top-level entries are sent one at a time as each subtree finishes. Deeper levels are sent only when the UI asks for them.

## Tests

```bash
dotnet test
```

The walker tests use an in-memory file system. `MacFileSystem` tests run only on macOS and check the live startup volume, firmlink handling, and that `/System/Volumes/Data` is not descended into.

To exercise the scanner without the password dialog, point it at a fixture directory. The app itself always scans `/`.

```bash
dotnet run --project src/DiskUsage.Scanner -- --socket /tmp/disk-usage.sock --root /path/to/fixture
```

The socket must already be listening; the scanner connects to it.
