# DiskBrush

A small Windows app that shows what is taking up space on your drives. It lists every file, every folder, and every installed app, largest first.

It is **read-only**: it never deletes, moves or changes anything.

## Features

- **Files tab:** every file on a drive (or in a folder you pick), largest first, with name, size on disk, size, folder and full path.
- **Folders tab:** every folder with its total size, including its subfolders, plus file count and full path.
- **Unreadable folders tab:** folders the app couldn't read, shown in red with a **!** and the reason (for example "Access denied").
- **Apps tab:** installed programs, largest first, measured from their install folders.
  - An orange **?** marks apps with no folder of their own on disk. These may be unused or leftover entries. Visual C++ runtimes also get this mark, but many games need them, so don't uninstall them on that basis.
  - Store apps are listed when the app runs as Administrator.
- Click a column header to sort, type to filter by name, double-click to open the file or folder in Explorer, and export any tab to CSV.
- **Windows system files are never shown.** This covers files Windows marks as System, the `Windows`, `Program Files`, `ProgramData`, `System Volume Information`, `$Recycle.Bin` and `Recovery` folders, and `pagefile.sys` / `hiberfil.sys` / `swapfile.sys`.
- Folder links (junctions and symlinks) are never followed, so nothing is counted twice.

## Requirements

- Windows 10 or 11
- .NET Framework 4.6.2 or newer (already included in Windows 10 1607+ and Windows 11)

There are no third-party libraries and nothing to download.

## Download and run

Download the zip from the [Releases](../../releases) page. Extract it, then run `DiskBrush.exe`. Keep `DiskBrush.exe.config` next to it, because it enables support for long paths.

The app isn't digitally signed, so Windows SmartScreen may say *"Windows protected your PC"*. Click **More info → Run anyway**, or build it yourself from source.

## Build from source

Run:

```
build.cmd
```

This uses the C# compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). The output goes to `bin\`:

- `DiskBrush.exe` and `DiskBrush.exe.config`: the app
- `ScannerTest.exe`: the automated tests. Run it with no arguments; it prints PASS/FAIL for each check and exits with 0 when everything passes.

## Known limitations

- Very small files are stored inside the drive's file table (the MFT). For these the app shows one cluster (usually 4 KB), while Explorer may show 0.
- App sizes depend on the install folder each installer recorded. When none is recorded, the folder is guessed from the app's icon or uninstaller, and the row is labelled "folder guessed".
- Store apps can only be measured as Administrator. Use the **Restart as Administrator** button.
- Network and mapped drives are not offered.
- A full drive scan can take a few minutes. A **Cancel** button is available.

## Project layout

```
src/Core/   scanning, size-on-disk, exclusions, sorting/filtering, app catalog (no UI code)
src/Gui/    Windows Forms window and tabs
tests/      ScannerTest.cs: builds a temporary folder tree and checks the results
```

## License

MIT. See [LICENSE](LICENSE).
