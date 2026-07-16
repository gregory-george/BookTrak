# CLAUDE.md — BookTrak

Portable, single-folder, double-click-to-run book & audiobook manager for **Windows x64**. A **.NET 10 Blazor Server** backend (Kestrel on loopback) serves a browser UI; a WinForms tray icon shows it's running. Local-first: full functionality offline; Open Library / audnexus are **enrichment, not dependencies**.

> Full spec: [BookTrak-Requirements-and-Build-Plan.md](BookTrak-Requirements-and-Build-Plan.md). The **code is the source of truth for what exists**; the spec remains authoritative for *design intent and rationale*. **All design questions are settled — don't redesign settled decisions.**

## Status
**Shipping — v1.0.1, fully implemented.** All build phases through packaging are done (see Build history). New work extends a shipped app: match existing patterns, don't reopen settled decisions, and don't pull in scope that isn't asked for.

## Stack
- .NET 10, `net10.0-windows`, `OutputType=WinExe`, `UseWindowsForms=true`
- Blazor Server (Interactive Server) + Kestrel bound to `127.0.0.1` only
- EF Core + **SQLite** (WAL mode), via `Microsoft.EntityFrameworkCore.Sqlite`; native provider bundled via `SQLitePCLRaw.bundle_e_sqlite3` (drives the single-file packaging gotcha below)
- Self-contained single-file publish, `win-x64`
- HTTP resilience via `Microsoft.Extensions.Http.Resilience` (Polly-based: retry + circuit breaker)
- `@zxing/browser` (vendored at `wwwroot/lib/zxing/zxing-browser.min.js`) for barcode scanning (ISBN scan on the Add-by-ISBN page)

## Project layout
Solution `BookTrak.slnx`: app in `src/BookTrak/`, xUnit tests in `tests/BookTrak.Tests/`. Inside `src/BookTrak/`:
- `Program.cs` — `[STAThread]` entry, single-instance gate, DI wiring, host-on-background-thread, shutdown orchestration.
- `Hosting/` — tray app (`TrayApplicationContext`) and lifecycle plumbing: `InstanceLock`/`PortFinder`/`AppConfig`/`AppPaths`, `InFlightOperationCounter` + `CircuitCounter` + `TrackingCircuitHandler`, `ShutdownCoordinator` ("finish, then exit"), `IdleShutdownMonitor` (auto-exit when idle).
- `Data/` — `BookTrakContext` (+ `BookTrakContextFactory`), `Entities/`, `DatabaseStartup` (backup + migrate), `SqlitePragmaInterceptor` (WAL/busy_timeout), `OrphanCoverCleanup`. EF migrations live in `Migrations/` (incl. `AddFts5Search`).
- `OpenLibrary/`, `Audnexus/`, `Audible/` — each: a typed client, a normalizer (`Raw*` → `Normalized*` models), and a `…ServiceCollectionExtensions` registrar. Shared HTTP hygiene lives under `OpenLibrary/`: `PoliteRetryPolicy`, `PoliteRateLimiter`, `UserAgent`, `CoverCacheService`.
- `Services/` — app logic over the DB: `LibraryQueryService`, `LibraryWriteService` (incl. delete/ignore/preferred-edition/refresh logic), `StatsQueryService`, `ImportService`, `MaintenanceService`, plus the `CoverPaths` path helper.
- `Import/` — Goodreads/StoryGraph CSV parsing. `Components/` — Blazor UI (`Pages/`, `Layout/`, `Shared/`).

## Architecture
- **Hosting:** `[STAThread] Main`. WinForms tray message loop owns the STA main thread; `IHost` runs on a **background thread**. Shutdown is bidirectional (tray Quit → stop host; host crash → exit process + tray cleanup).
- **Data model is `Author → Work → Edition`:**
  - BookTrak **`Book` ≈ Open Library Work** (work-level fields: title, description, subjects, average rating).
  - **`Edition`** = a specific printing (edition-level: ISBN, pages, language, cover, publisher, publish date). `pages` and `language` are **edition-level, not on Book**.
  - An **audiobook is just an `Edition` with `Format = Audiobook`** — no separate copies table. Audiobook-only fields (`Narrator`, `DurationSeconds`, `AudioPublisher`, `Asin`) hang off the edition.
  - **`Genre`/`BookGenre`** (curated taxonomy) is distinct from `Book.Subjects` (raw OL subject strings, stored as a JSON array). `Series` hangs off Book (`SetNull` on delete) with the string `SeriesPosition`.
- **Data sources:** Open Library (`openlibrary.org`, no API key) for works/editions/authors; **audnexus** (`api.audnex.us`) for audiobooks, keyed by Audible **ASIN**. Manual entry is always an available fallback. **Never scrape Audible directly** — with one narrow, documented carve-out: the `BookTrak.Audible` namespace calls Audible's unofficial catalog-search JSON endpoint (`api.audible.com/1.0/catalog/products?keywords=…`) for **discovery only** (title+author → candidate ASIN, us region). The actual audiobook metadata fetch and edition creation still go through audnexus by ASIN; this path is best-effort with manual ASIN entry as fallback, sends no bot-shaped User-Agent, and never scrapes Audible HTML.

## Non-negotiable rules (these are the bug-magnets — see §4, §10, §14)
- **EF access goes through `IDbContextFactory<BookTrakContext>` + short-lived contexts**, one per operation. Never share a `DbContext` across overlapping Blazor circuit events. This is the single most important EF decision.
- **Ratings (`MyRating`, `AverageRating`) are stored as `double`/REAL, never `decimal`.** SQLite maps `decimal`→TEXT and breaks ordering/comparison. `MyRating` is half-stars, 0.5–5.0.
- **Authors are modeled only via the `BookAuthor` join table.** There is **no scalar `AuthorId` on `Book`** (works can have multiple authors).
- **Book has no cover columns.** Its display cover is **derived from `PreferredEditionId`**. Don't denormalize a cover onto Book.
- **Self-referencing FKs** `Book.PreferredEditionId` and `Book.ReadEditionId` must be `DeleteBehavior.Restrict` (EF `NoAction`) — the Book↔Edition cycle otherwise throws multiple-cascade-path errors. Deleting a Book cascades to its Editions; null the Preferred/Read pointers first — `DeleteBookAsync`/`DeleteEditionAsync` in `LibraryWriteService` already do this (and repoint the preferred edition on edition delete), so route deletes through them.
- **`IsIgnored` is an orthogonal bool on BOTH `Book` (work) and `Edition`.** It is *not* a status value. Work-level ignore is the active UI feature: Ignore/Un-ignore on book detail, plus "include ignored" checkboxes (default hidden) on the Library and Author detail pages. **Edition-level ignore is legacy, data-level only** — the UI replaced it with edition *delete* (the editions table always shows every edition, badging ignored ones), but `Edition.IsIgnored` and `SetEditionIgnoredAsync` remain honored for data that has it. Invariant either way: **the preferred edition must never be an ignored edition** — `LibraryWriteService` repoints it on both ignore and delete.
- **Re-sync/refresh touches OL-sourced fields only.** Never clobber local state: `Status`, `MyRating`, `IsIgnored`, preferred-edition choice. Bulk "Refresh All" additionally skips anything synced within `SyncStatus.StaleThreshold` (30 days).
- **OL/audnexus field normalization is mandatory.** `description` and `bio` come as a string *or* `{type, value}`; dates (birth/death, first-publish) are **free text — never parse as `DateTime`, store as string**. audnexus runtime arrives in **minutes** — convert to `Edition.DurationSeconds` on ingest. Centralize normalization so the rest of the app sees clean types.
- **`SeriesPosition` is a string** (`"3"`, `"3.5"`, `"0.5"`), not a number.

## HTTP client hygiene (both OL and audnexus)
- Typed `HttpClient` via `IHttpClientFactory`, mandatory descriptive `User-Agent` (e.g. `BookTrak/{version} (contact)`) or risk being blocked.
- Retry + circuit breaker via the `Microsoft.Extensions.Http.Resilience` standard handler on `429`/`503`/`5xx`; the shared `PoliteRetryPolicy` (used by both the OL and audnexus clients) honors the server's `Retry-After` header — delta or date — over its own exponential-backoff-with-jitter.
- Client-side rate limit (max concurrent + min interval). **Cache aggressively**: metadata in SQLite, covers to disk (cache-first, only fetch on miss, write atomically).
- `covers.openlibrary.org` is rate-limited **separately** from the main API — throttle cover fetches and tolerate misses (placeholder, retry later). A big first CSV import is the main hazard.
- Every call degrades gracefully offline: show cached data, surface a non-blocking notice.

## Status state machine (work level)
`None → WantToRead → Reading → Read`. Every arrow is **skippable and reversible**. `Read` carries `DateRead` + `MyRating` + optional `ReadEditionId` (which edition you consumed). `Reading` carries `DateStarted`.

## Lifecycle / shutdown
- **Session-bounded** — no overnight/background work.
- **"Finish, then exit":** on Quit, stop new work, then wait for **zero in-flight ops** (any outbound OL request or cover fetch — wrap each to inc/dec a shared counter) **and** zero connected UI circuits before disposing the host (`ShutdownCoordinator`, 60 s fallback deadline).
- **Idle auto-shutdown:** once a tab has connected, if connected circuits stay at zero for a **5 s grace period** (page refresh/navigation tolerated), the app runs the same "finish, then exit" sequence as Quit — so the tray icon doesn't linger after the user closes the browser. (`IdleShutdownMonitor`.)
- **Single instance:** a named `Mutex` is the primary gate. If it's already held, read `BookTrak.lock` and reopen the browser at the live port instead of starting a second copy. A leftover lockfile when the mutex was free is from a crashed process — `InstanceLock.IsLive` confirms the recorded PID is alive *and* its process name is `BookTrak`; if not, the lock is stale and cleared.
- **Port:** start at `6123`, probe upward to next free, persist to `config.json`, write live port + PID to `BookTrak.lock`.

## Folder layout (everything relative to the .exe)
```
BookTrak/
├── BookTrak.exe       # self-contained single file
├── config.json        # port, API contact info, sort prefs, schema version (AppConfig)
├── BookTrak.db        # SQLite (+ -wal, -shm at runtime)
├── BookTrak.lock      # live port + PID, deleted on clean shutdown
├── covers/{books,authors}/   # cached images — NOT backed up (re-fetchable)
└── backups/           # DB + config.json, yyyyMMdd suffix, max one/day, keep last 10 of each
```

## Database specifics (§10)
- WAL mode + `busy_timeout` pragma (5 s) — background fetches + UI writes otherwise throw "database is locked".
- **FTS5 virtual table** over book title/subtitle/author names, kept in sync via insert/update/delete **triggers** (or rebuilt on migration). Powers local search; composes with facet filtering.
- **Startup migration: back up `BookTrak.db` (+ `config.json`) → `backups/` FIRST, then `context.Database.Migrate()`.** At most one backup per calendar day; prune to last 10 of each. `DatabaseStartup.CreateBackup` also backs the Settings page's "Back up now" button.
- A `Settings` key/value table exists in the schema but is **currently unused** — app prefs live in `config.json` via `AppConfig`.
- **Orphan cover cleanup:** startup sweep + manual button; delete cover files no longer referenced by any book/edition.

## Packaging gotchas
- Single-file + SQLite needs the native `e_sqlite3` extracted: **`IncludeNativeLibrariesForSelfExtract=true`**. Always smoke-test the published exe in a **clean folder with no SDK** to confirm native extraction + first-run DB creation.
- **`OutputType=WinExe` breaks Blazor's framework static web assets** — the SDK target that registers `blazor.web.js`/`blazor.server.js` only runs for `OutputType=Exe`, so the csproj's `_AddBlazorFrameworkStaticWebAssetsForWinExe` target re-runs that logic itself. Don't "simplify" that MSBuild block away; without it the UI loads but has no interactivity.

## Build history
The app was built in the ordered phases of §13 (Phase 0 host skeleton → Phase 1 data layer → Phase 2 OL client + normalization tests → Phase 3 read-only UI → … → Phase 14 packaging) and **all phases are complete.** That plan is now a record of how the code came together and a map of where each design decision is justified — not a forward roadmap.

## Build & run
```
dotnet run --project src/BookTrak/BookTrak.csproj            # dev (framework-dependent, fast)
dotnet test                                                  # xUnit suite (tests/BookTrak.Tests)
dotnet publish src/BookTrak/BookTrak.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```
`publish.ps1` wraps the publish command and overlays the output onto the release share (pass `-Destination <path>` for a local build). **The destination is the live app folder** — `BookTrak.db`, `covers/`, `backups/`, `config.json` sit beside the exe there — so the copy step must only add/overwrite binaries, **never mirror or delete** the destination. `RuntimeIdentifier`/`SelfContained`/`PublishSingleFile` are intentionally passed at publish time, not baked into the csproj, so `dotnet build`/`dotnet run` stay fast framework-dependent builds.

## Accepted trade-offs (documented, not bugs)
- **No app-level auth.** Loopback-only bind is the security control; any local process/user can reach the UI. Accepted for a single-user personal app.
- **Title→ASIN discovery is best-effort, not guaranteed** — paste-the-ASIN remains the reliable audiobook path, but `BookTrak.Audible` now resolves title+author → candidate ASIN via Audible's unofficial catalog search (see Data sources carve-out). Single adds on the Find-new-books page auto-attach only a single high-confidence match; its bulk "Add All" skips the lookup; the book detail page offers a "Find matching audiobook" picker. Treat any of it failing/empty as normal and fall back to manual ASIN.
- **CSV export is deferred** — `.db` + backups cover portability.
