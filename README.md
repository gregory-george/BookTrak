# BookTrak

A portable, single-folder, double-click-to-run book & audiobook manager for **Windows x64**.

BookTrak runs a local web UI in your browser, backed by a small .NET process on your machine — there's no install, no account, no cloud. Everything lives in one folder next to the `.exe`. It works fully offline; [Open Library](https://openlibrary.org) and [audnexus](https://audnex.us) are used only to enrich your data (covers, descriptions, audiobook metadata) when you're online.

## Features

- Track books and audiobooks under a single `Author → Work → Edition` model — an audiobook is just another edition of a book, alongside its print/ebook editions.
- Add books by searching Open Library, by ISBN (including barcode scanning), or fully manually.
- Add audiobook editions by pasting an Audible ASIN (enriched via audnexus) or entering details by hand.
- Track your reading state per book: Want to Read → Reading → Read, with date started/finished, half-star ratings, and which edition you actually read/listened to.
- Per-author "what am I missing" gap list against their full Open Library catalog.
- Library-wide search, sort, and faceted filtering (status, format, genre, series, rating).
- Series view with reading order, gaps, and a "what's next" suggestion.
- Import your reading history from a Goodreads or StoryGraph CSV export.
- A reading-stats dashboard (pages/hours read, rating distribution, format breakdown, longest/shortest reads, and more).
- Automatic local backups and orphaned-cover cleanup.

## Requirements

- Windows 10/11, x64.
- Nothing else — the published build is self-contained and needs no separately-installed .NET runtime or SDK.

## Running it

Download or build `BookTrak.exe`, put it in its own folder, and double-click it. It opens your default browser to the app and shows a tray icon while it's running — use the tray icon's context menu to reopen the UI or quit.

On first run BookTrak creates everything it needs next to the exe: a SQLite database, `config.json`, and a `covers/` folder for cached cover images. Quitting cleanly waits for any in-flight downloads and open browser tabs to finish before exiting.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/BookTrak/BookTrak.csproj          # dev
dotnet test                                                  # run the test suite

dotnet publish src/BookTrak/BookTrak.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true
```

The publish output is self-contained and single-file; smoke-test it in an empty folder (no SDK installed) to confirm native SQLite extraction and first-run database creation both work.

## Folder layout

Everything is relative to the `.exe`, so the whole install is portable — copy the folder anywhere, or delete it to remove BookTrak completely.

```
BookTrak/
├── BookTrak.exe       # self-contained single file
├── config.json        # port, preferences, schema version, last-sync timestamps
├── BookTrak.db         # SQLite database (+ -wal, -shm while running)
├── BookTrak.lock       # live port + PID, removed on clean shutdown
├── covers/             # cached cover images — not backed up, re-fetched on demand
└── backups/            # timestamped DB + config backups, last 10 kept
```

## Known limitations

- No app-level authentication — BookTrak binds only to `127.0.0.1`, so any process or user on the same machine can reach it. This is the accepted trade-off for a single-user local app.
- Audiobooks are enriched by pasting an Audible ASIN, not by title search — audnexus doesn't support reliable title lookup.
- No CSV export yet; the database file plus its automatic backups are the supported way to keep or move your data.

## License

Personal project — no license file yet.
