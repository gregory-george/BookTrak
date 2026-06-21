# CLAUDE.md — BookTrak

Portable, single-folder, double-click-to-run book & audiobook manager for **Windows x64**. A **.NET 10 Blazor Server** backend (Kestrel on loopback) serves a browser UI; a WinForms tray icon shows it's running. Local-first: full functionality offline; Open Library / audnexus are **enrichment, not dependencies**.

> Full spec: [BookTrak-Requirements-and-Build-Plan.md](BookTrak-Requirements-and-Build-Plan.md). This file is the quick-reference; the spec is authoritative. **All design questions are settled — build against the spec, don't redesign it.**

## Status
Greenfield. As of this writing the repo contains **only the requirements doc** — no code, no project scaffolding. Start at Phase 0 (§13).

## Stack
- .NET 10, `net10.0-windows`, `OutputType=WinExe`, `UseWindowsForms=true`
- Blazor Server + Kestrel bound to `127.0.0.1` only
- EF Core + **SQLite** (WAL mode)
- Self-contained single-file publish, `win-x64`
- Polly for HTTP resilience; `@zxing/browser` JS fallback for barcode scanning

## Architecture
- **Hosting:** `[STAThread] Main`. WinForms tray message loop owns the STA main thread; `IHost` runs on a **background thread**. Shutdown is bidirectional (tray Quit → stop host; host crash → exit process + tray cleanup).
- **Data model is `Author → Work → Edition`:**
  - BookTrak **`Book` ≈ Open Library Work** (work-level fields: title, description, subjects, average rating).
  - **`Edition`** = a specific printing (edition-level: ISBN, pages, language, cover, publisher, publish date). `pages` and `language` are **edition-level, not on Book**.
  - An **audiobook is just an `Edition` with `Format = Audiobook`** — no separate copies table. Audiobook-only fields (`Narrator`, `DurationSeconds`, `AudioPublisher`, `Asin`) hang off the edition.
- **Data sources:** Open Library (`openlibrary.org`, no API key) for works/editions/authors; **audnexus** (`api.audnex.us`) for audiobooks, keyed by Audible **ASIN**. Manual entry is always an available fallback. **Never scrape Audible directly.**

## Non-negotiable rules (these are the bug-magnets — see §4, §10, §14)
- **EF access goes through `IDbContextFactory<BookTrakContext>` + short-lived contexts**, one per operation. Never share a `DbContext` across overlapping Blazor circuit events. This is the single most important EF decision.
- **Ratings (`MyRating`, `AverageRating`) are stored as `double`/REAL, never `decimal`.** SQLite maps `decimal`→TEXT and breaks ordering/comparison. `MyRating` is half-stars, 0.5–5.0.
- **Authors are modeled only via the `BookAuthor` join table.** There is **no scalar `AuthorId` on `Book`** (works can have multiple authors).
- **Book has no cover columns.** Its display cover is **derived from `PreferredEditionId`**. Don't denormalize a cover onto Book.
- **Self-referencing FKs** `Book.PreferredEditionId` and `Book.ReadEditionId` must be `DeleteBehavior.Restrict` (EF `NoAction`) — the Book↔Edition cycle otherwise throws multiple-cascade-path errors. Deleting a Book cascades to its Editions; null the Preferred/Read pointers first if needed.
- **`IsIgnored` is an orthogonal bool on BOTH `Book` (work) and `Edition`.** It is *not* a status value. One "hide/show ignored" UI toggle (default hidden) filters both levels. The preferred edition must never be an ignored edition — repoint if you ignore it.
- **Re-sync/refresh touches OL-sourced fields only.** Never clobber local state: `Status`, `MyRating`, `IsIgnored`, preferred-edition choice.
- **OL/audnexus field normalization is mandatory.** `description` and `bio` come as a string *or* `{type, value}`; dates (birth/death, first-publish) are **free text — never parse as `DateTime`, store as string**. Centralize normalization so the rest of the app sees clean types.
- **`SeriesPosition` is a string** (`"3"`, `"3.5"`, `"0.5"`), not a number.

## HTTP client hygiene (both OL and audnexus)
- Typed `HttpClient` via `IHttpClientFactory`, mandatory descriptive `User-Agent` (e.g. `BookTrak/{version} (contact)`) or risk being blocked.
- Polly retry + circuit breaker; honor `429`/`503` backoff (audnexus sends `retryAfterSeconds` — respect it).
- Client-side rate limit (max concurrent + min interval). **Cache aggressively**: metadata in SQLite, covers to disk (cache-first, only fetch on miss, write atomically).
- `covers.openlibrary.org` is rate-limited **separately** from the main API — throttle cover fetches and tolerate misses (placeholder, retry later). A big first CSV import is the main hazard.
- Every call degrades gracefully offline: show cached data, surface a non-blocking notice.

## Status state machine (work level)
`None → WantToRead → Reading → Read`. Every arrow is **skippable and reversible**. `Read` carries `DateRead` + `MyRating` + optional `ReadEditionId` (which edition you consumed). `Reading` carries `DateStarted`.

## Lifecycle / shutdown
- **Session-bounded** — no overnight/background work.
- **"Finish, then exit":** on Quit, stop new work, then wait for **zero in-flight ops** (any outbound OL request or cover fetch — wrap each to inc/dec a shared counter) **and** zero connected UI circuits before disposing the host.
- **Single instance:** named `Mutex`. On launch, if `BookTrak.lock` exists, verify the PID is alive and is actually BookTrak: alive → reopen browser at that port and exit; dead/missing → stale lock, delete and start normally.
- **Port:** start at `6123`, probe upward to next free, persist to `config.json`, write live port + PID to `BookTrak.lock`.

## Folder layout (everything relative to the .exe)
```
BookTrak/
├── BookTrak.exe       # self-contained single file
├── config.json        # port, prefs, schema version, last-sync timestamps
├── BookTrak.db        # SQLite (+ -wal, -shm at runtime)
├── BookTrak.lock      # live port + PID, deleted on clean shutdown
├── covers/{books,authors}/   # cached images — NOT backed up (re-fetchable)
└── backups/           # DB + config.json, timestamp suffix, keep last 10
```

## Database specifics (§10)
- WAL mode + `busy_timeout` pragma (3–5 s) — background fetches + UI writes otherwise throw "database is locked".
- **FTS5 virtual table** over book title/subtitle/author names, kept in sync via insert/update/delete **triggers** (or rebuilt on migration). Powers local search; composes with facet filtering.
- **Startup migration: back up `BookTrak.db` → `backups/` FIRST, then `context.Database.Migrate()`.** Prune backups to last 10.
- **Orphan cover cleanup:** startup sweep + manual button; delete cover files no longer referenced by any book/edition.

## Packaging gotcha
Single-file + SQLite needs the native `e_sqlite3` extracted: **`IncludeNativeLibrariesForSelfExtract=true`**. Always smoke-test the published exe in a **clean folder with no SDK** to confirm native extraction + first-run DB creation.

## Build order
Build phases **in order** (§13); each phase leaves the app runnable and has explicit exit criteria. Phase 0 skeleton/host → Phase 1 data layer (schema-shaping decisions land here) → Phase 2 OL client + normalization tests → Phase 3 read-only UI → … → Phase 14 packaging. Don't pull later-phase features forward unless asked.

## Build & run
No build scripts exist yet. Once scaffolded:
```
dotnet run                                          # dev
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

## Accepted trade-offs (documented, not bugs)
- **No app-level auth.** Loopback-only bind is the security control; any local process/user can reach the UI. Accepted for a single-user personal app.
- **Title→ASIN search is deferred** — paste-the-ASIN is the reliable audiobook path.
- **CSV export is deferred** — `.db` + backups cover portability.
