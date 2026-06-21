# BookTrak — Requirements & Build Plan

> A portable, single-folder, bi-directional front-end client to manage books and audiobooks on Windows x64.
> Browser UI served by a local **.NET 10 Blazor Server** backend, launched by double-clicking one `.exe`.
> Works mostly offline; enriches data from the **Open Library API**.

---

## 1. Overview

BookTrak is a personal library manager. You add **authors** and **books**, pull metadata from Open Library, and track your own reading state (rating, date read, want-to-read, ignored). Everything lives in a single folder relative to the `.exe`, in a local **SQLite** database via **EF Core**. No cloud, no account, no cross-device sync.

The UI runs in your default browser, served by a Blazor Server backend on `127.0.0.1`. A WinForms tray icon shows it's running and lets you reopen the UI or quit.

### Primary goals
1. One-folder, double-click-to-run, no install.
2. Local-first: full functionality offline; Open Library is enrichment, not a dependency.
3. Manage both **books** and **audiobooks**, including audiobook-specific fields Open Library doesn't track.
4. Track per-author book lists, reading state, ratings, and "what am I missing for this author."

---

## 2. Key concept: Open Library's data model (read this first)

This shapes the whole schema, so it's worth being precise.

Open Library splits a book into two record types:

| Record | What it is | Carries |
|---|---|---|
| **Work** (`/works/OL…W`) | The abstract book | title, authors, description, subjects, first-publish year, **average rating** |
| **Edition** (`/books/OL…M`) | A specific printing | ISBN, **number of pages**, **language**, cover, publisher, publish date |
| **Author** (`/authors/OL…A`) | A person | name, bio, birth/death dates, photo, alternate names, links |

A single Work can have hundreds of editions across languages. Your original spec put `number_of_pages` and `language` on the book — those are actually **edition-level**.

**BookTrak's mapping:**
- A BookTrak **`Book` ≈ an Open Library Work.**
- Each `Book` optionally points at a **preferred edition** (`PreferredEditionId`) that supplies edition-level fields (pages, language, cover, ISBN, publish date). The Book's **displayed cover is derived from its preferred edition** — we do *not* store a separate cover on `Book` (avoids a denormalized field that goes stale when you repoint the preferred edition).
- **Preferred-edition auto-pick (on add).** OL's `editions.json` is noisy, so when adding a book we score editions and pick the best by: **English language → has ISBN → has cover → has page count**, then fall back to newest publish date, then first returned. The user can always repoint it later.
- **Average rating** comes from the work's ratings endpoint (`/works/{id}/ratings.json`); stored as `double`/REAL. **My rating** is local, also `double` (see §4 — SQLite has no real `decimal`).
- **"Genres"** in Open Library are really `subjects` — an uncontrolled, noisy tag cloud. We store raw OL subjects for reference **and** keep a separate, curated local **`Genre`** taxonomy you control. Don't try to treat OL subjects as clean genres.

### Endpoints we'll use

| Purpose | Endpoint |
|---|---|
| Search works/editions | `GET /search.json?q=…&fields=…` |
| Search authors | `GET /search/authors.json?q=…` |
| Work detail | `GET /works/{id}.json` |
| Work editions | `GET /works/{id}/editions.json?limit=…&offset=…` |
| Work average rating | `GET /works/{id}/ratings.json` |
| Author detail | `GET /authors/{id}.json` |
| Author's works | `GET /authors/{id}/works.json?limit=…&offset=…` |
| Edition by ISBN | `GET /isbn/{isbn}.json` |
| Cover image | `https://covers.openlibrary.org/b/id/{coverId}-{S\|M\|L}.jpg` |
| Author photo | `https://covers.openlibrary.org/a/olid/{OLID}-{S\|M\|L}.jpg` |

**No API key.** It's a free non-profit service. In return we **must**:
- Send a descriptive `User-Agent` (e.g. `BookTrak/1.0 (your-contact)`), or risk being blocked.
- **Cache aggressively** — catalogue data and covers change slowly. Cache covers to disk; cache metadata in SQLite.
- Rate-limit ourselves and avoid bulk pulls. This is a real-time, low-volume use case, which is exactly what they support.

---

## 3. Audiobooks (sourced via audnexus, not Open Library)

Open Library has no concept of audiobooks, narrators, or runtime. An audiobook is **just another edition** of a work, distinguished by `Edition.Format = Audiobook`. No separate copies table — editions already are the per-printing record, and an audiobook is simply a printing OL doesn't catalog. So a single work can have a print edition, an ebook edition, and an audiobook edition side by side.

Audiobook metadata comes from **audnexus** (`api.audnex.us`) — a community aggregation API that fronts Audible's catalog behind clean REST/JSON, the de-facto standard used by Audiobookshelf and the Plex audiobook agents. We **don't scrape Audible directly** (ToS issues + brittle HTML + CAPTCHA). See §9 for the provider design.

Each `Edition` carries a `Format` and, for audiobooks, fields OL can't supply:

| Format | Primary source | Extra local fields on the edition |
|---|---|---|
| Physical | Open Library | — |
| Ebook | Open Library | — |
| Audiobook | **audnexus** (by ASIN) | **Narrator**, **DurationSeconds** (audnexus returns minutes → store seconds; display hh:mm:ss), **AudioPublisher**, **Asin** |

This keeps the model flat: **author → work → edition**, with `Format` and the audiobook fields hanging off the edition that needs them (null/unused for print and ebook). Manual entry is always available as a fallback.

---

## 4. Data model

EF Core entities. Edition-level fields hang off `Edition`; work-level off `Book`.

### Author
| Field | Source | Notes |
|---|---|---|
| `Id` | local | PK |
| `OpenLibraryId` | OL | `OL…A`, unique, nullable (manual authors allowed) |
| `Name` | OL | |
| `PersonalName`, `AlternateNames` | OL | alt names as JSON or child table |
| `Bio` | OL | OL returns this as string *or* `{type, value}` — normalize on ingest |
| `BirthDate`, `DeathDate` | OL | free-text strings in OL, **not** real dates — store as string |
| `PhotoId` / `PhotoPath` | OL + cache | photo cached to `covers/authors/` |
| `Links` | OL | external links, JSON |
| `Wikipedia` | OL | |
| `DateAdded`, `LastSyncedUtc` | local | |

### Book (≈ Work)
| Field | Source | Notes |
|---|---|---|
| `Id` | local | PK |
| `OpenLibraryWorkId` | OL | `OL…W`, unique, nullable |
| `Title`, `Subtitle` | OL | |
| `Description` | OL | normalize string-or-object |
| `FirstPublishDate` | OL | string in OL |
| `Subjects` | OL | raw OL subjects, JSON or child table |
| `Genres` | local | curated `Genre` many-to-many |
| `SeriesId` | local/audnexus | nullable FK → `Series`; OL series data is unreliable, audnexus supplies it for audiobooks, else manual |
| `SeriesPosition` | local/audnexus | nullable **string** (allows `"3"`, `"3.5"`, `"0.5"`) |
| ~~`CoverId` / `CoverPath`~~ | — | **removed** — Book's display cover is derived from `PreferredEdition` (see §2) |
| `AverageRating`, `RatingsCount` | OL | from `/ratings.json`; `AverageRating` stored as **`double`/REAL** |
| `MyRating` | local | nullable; **0.5–5.0 in half-star steps, stored as `double`/REAL** (SQLite has no real `decimal`; EF maps `decimal`→TEXT and breaks ordering/comparison) |
| `Status` | local | enum, see §5 |
| `IsIgnored` | local | bool, orthogonal to status — hides the whole work |
| `DateStarted` | local | nullable; set when `Status = Reading` |
| `DateRead` | local | nullable |
| `ReadEditionId` | local | nullable FK → Edition; which edition you actually read/listened to. **`DeleteBehavior.Restrict/NoAction`** (see note) |
| `DateAdded` | local | |
| `LastSyncedUtc` | local | |
| `PreferredEditionId` | local | nullable FK → Edition. **`DeleteBehavior.Restrict/NoAction`** (see note) |

> **Authors:** modeled **only** via the **`BookAuthor`** join table (OL works can have multiple authors). There is **no scalar `AuthorId`** on `Book`.
>
> **Self-referencing FK delete behavior:** `Book` → `Edition` (Preferred/Read) and `Edition` → `Book` form a reference cycle. Configure `PreferredEditionId` and `ReadEditionId` with **`DeleteBehavior.Restrict`** (EF Core `NoAction`) to avoid multiple-cascade-path errors. Deleting a `Book` cascades to its `Edition`s; null the work's Preferred/Read pointers first if needed.

### Edition
| Field | Source | Notes |
|---|---|---|
| `Id` | local | PK |
| `OpenLibraryEditionId` | OL | `OL…M`, unique, nullable |
| `BookId` | local | FK → Book |
| `Format` | local | enum: `Physical` \| `Ebook` \| `Audiobook` |
| `Isbn10`, `Isbn13` | OL | print/ebook identifiers |
| `Asin` | audnexus | audiobook identifier (Audible ASIN); nullable |
| `NumberOfPages` | OL | edition-level |
| `Language` | OL | edition-level, e.g. `eng` |
| `Publisher`, `PublishDate` | OL | |
| `Narrator` | local | audiobook-only, nullable |
| `DurationSeconds` | local | audiobook-only, nullable; display hh:mm:ss |
| `AudioPublisher` | local | audiobook-only, nullable |
| `CoverId` / `CoverPath` | OL + cache | |
| `IsIgnored` | local | bool — hides this edition (e.g. non-English printings) |

### Genre (local taxonomy)
`Id`, `Name`, `Slug`. Many-to-many with `Book` via `BookGenre`.

### Series (local)
`Id`, `Name`, `OpenLibrarySeriesKey` (nullable — OL series data is patchy). `Book` has a nullable `SeriesId` + string `SeriesPosition`. Source: **audnexus** for audiobooks (it returns series + position), manual otherwise. Powers the **"next in series" / missing-volumes** view.

### Settings / KeyValue
Port, last-used values, schema version, etc. (Some of this lives in `config.json` instead — see §7.)

---

## 5. Reading status & ignoring

**Read state lives on the work** (you've "read the book" regardless of format), captured as a small state machine. **Ignoring is a separate, orthogonal `IsIgnored` bool** that exists at **both** the work and the edition level — confirmed.

### Status (work level)
```
None  →  WantToRead  →  Reading  →  Read (DateRead + MyRating + optional ReadEditionId)
```
`Reading` is a real state, but every arrow is skippable — you can go `None → Read`, `WantToRead → Read`, etc. Any status can also move back (e.g. abandon a `Reading` book to `None`).

| Status | Meaning | Attached data |
|---|---|---|
| `None` | Known/cataloged, no intent set | — |
| `WantToRead` | On the wishlist | — |
| `Reading` | In progress | `DateStarted` (nullable) |
| `Read` | Finished | **DateRead**, **MyRating** (half-stars), optional **ReadEditionId** (which edition you consumed) |

`ReadEditionId` lets you record *how* you read it — e.g. you finished the audiobook edition, not the print one. Nullable; ignore it if you don't care.

### Ignoring (orthogonal, two levels)
- **`Work.IsIgnored`** — drop the whole book from default views (irrelevant works, dupes).
- **`Edition.IsIgnored`** — drop a specific printing (e.g. non-English editions) while keeping the work.

Rules:
- The **preferred edition** must never be an ignored edition; if you ignore the current preferred edition, repoint to the best non-ignored one.
- "**Hide/show ignored**" is one UI toggle (default = hidden) that filters both levels: ignored works disappear from author/book lists; ignored editions disappear from a work's edition list.

---

## 6. Features & user flows

### Browse
- **Library view (all books)** → a flat, whole-library grid/list — *not* author-scoped — for browsing everything by cover. **Virtualized** (Blazor `<Virtualize>`) so a multi-thousand-book library (e.g. after a big CSV import) stays snappy.
- **Author list** → click an author → see their books (their local books + status badges).
- **Book detail** → all work fields + its **editions** (print/ebook/audiobook), cover, average rating, my rating, status, **series + position**.
- **Series view** → for a book in a series, show the full series order and **which volumes you're missing / what's next** after your last-read.
- **Filter, sort & facets** → on the library view: facet by **status**, **genre**, **format**, **rating**, **series**, **owned/ignored**; sort by **date added**, **rating**, **title**, **author**, **publish date**. Multi-select within a facet and stack across facets, with live counts. Pure query work over the existing schema — no new tables.
- **Local search** → fast title/author search backed by **SQLite FTS5** (see §10); composes with the facets (filters the FTS result set).
- **Hide/show ignored** toggle (default = hidden) — filters ignored works *and* ignored editions.

### Add & enrich
- **Add author** — search OL authors, pick one, pull bio/photo/dates. Allow manual author with no OL match.
- **Add book** — search OL works, pick one, pull work + best-edition + cover + rating. Set preferred edition.
- **Add by ISBN** — `/isbn/{isbn}.json` resolves a specific edition. The natural way to pin a specific audiobook or printing.
- **Barcode → ISBN scan** — scan a physical book's barcode from a webcam/phone camera straight into the add-by-ISBN flow. Loopback (`127.0.0.1`/`localhost`) counts as a **secure context**, so `getUserMedia` works over plain HTTP — no TLS needed. Feature-detect the native `BarcodeDetector` (Chromium) and fall back to a JS decoder (e.g. `@zxing/browser`) when the default browser lacks it. Manual ISBN entry is always available.
- **Add an edition** — add another edition to an existing work. For an **audiobook**, paste the Audible **ASIN** and enrich from audnexus (narrator, runtime, audio publisher, cover), or enter manually.
- **Find new books for an author** — fetch `/authors/{id}/works.json`, diff against local books for that author, show the **gap list**, let you add any with one click. Handle pagination and dedupe by work id.
- **Re-sync / refresh** — re-pull OL (and audnexus) metadata for an author or work *already* in the library: updated description, average rating, new editions, author bio/photo. Stamps `LastSyncedUtc`; surfaces a subtle **"stale"** indicator when a record hasn't synced in a while. **Never clobbers local state** (`Status`/`MyRating`/`IsIgnored`/preferred-edition) — OL-sourced fields only. Throttle hard: a "refresh all" is a burst of OL calls, same caution as CSV import.

### Track
- **Mark as reading** — sets `Status = Reading`, captures `DateStarted` (default today, editable).
- **Mark as read** — sets `Status = Read`, captures `DateRead` (default today, editable) + `MyRating`, optionally `ReadEditionId`.
- **Want to read** — sets `Status = WantToRead`.
- **Ignore / un-ignore** — toggles `IsIgnored` on a **work** or an **edition**.
- **Rate** — half-star picker (0.5–5.0); edit rating / dates independently of status.

### Audiobook specifics
- An audiobook is an **edition** with `Format = Audiobook` plus **narrator**, **duration**, **audio publisher**.

### Library tools
- **CSV import (Goodreads / StoryGraph)** — import an existing library from a Goodreads or StoryGraph export. Map `ISBN`/`ISBN13` → the add-by-ISBN resolver, `My Rating` (1–5 int) → `MyRating` (×1.0), `Date Read` → `DateRead`, and the shelf (`read` / `to-read` / `currently-reading`) → `Status`. **Dedupe by work id / ISBN** against the existing library; report skipped + unresolved rows. Runs through the same polite OL client (it's a batch of single lookups, so cache hard and rate-limit).
- **Reading stats dashboard** — derived entirely from data we already capture, no new schema: books read this year, ratings distribution, pages read (sum of read editions' `NumberOfPages`), **hours listened** (sum of read audiobook editions' `DurationSeconds`), reads-per-month, **format breakdown** (print/ebook/audiobook), **average rating by genre and by author**, **most-read author / narrator**, **longest & shortest reads**, and an **added-vs-read trend**. All `GROUP BY`/aggregate queries over existing rows.

---

## 7. Portability, files & layout

Everything relative to the `.exe`:

```
BookTrak/
├── BookTrak.exe              # self-contained single file
├── config.json                # port, prefs, schema version
├── BookTrak.db               # SQLite (+ -wal, -shm at runtime)
├── BookTrak.lock             # live port + PID, written at startup
├── covers/
│   ├── books/                 # cached cover images by id
│   └── authors/               # cached author photos
└── backups/
    └── BookTrak_20260621T140312.db   # last 10 kept
```

- **`config.json`** — small, human-editable: chosen port, UI prefs, last Open Library sync timestamps, schema version.
- **`BookTrak.lock`** — written on startup with the **live port** and PID so a relaunch can find/reopen the running instance instead of starting a second one. Delete on clean shutdown.
- **Single-instance + stale-lock handling.** Enforce one running instance with a **named `Mutex`** (two processes against one SQLite file = WAL contention + racing migrations). On launch, if a lockfile exists, **verify the PID is alive and is actually BookTrak** before trusting it: if alive → just reopen the browser at that port and exit; if dead/missing (crash left it behind) → treat the lockfile as **stale**, delete it, and start normally.
- **Covers** are **not** backed up (re-fetchable). DB + `config.json` **are**.

---

## 8. Hosting & lifecycle

Matches your initial decisions, tightened:

- **Output type `WinExe`**, `[STAThread]` `Main`. WinForms tray message loop owns the main STA thread; `IHost` runs on a background thread. Shutdown bridged both ways (tray Quit → stop host; host crash → exit process + tray cleanup).
- **Kestrel binds loopback only** (`http://127.0.0.1:{port}`) — never `0.0.0.0`. This is the main security control; no external exposure. **Note (accepted):** there is no app-level auth, so any process or other signed-in user on the same machine can reach the UI. For a single-user personal app on a personal machine this is an accepted trade-off, documented rather than mitigated.
- **Port conflict** → start at `6123`, probe upward to next free port, persist chosen port to `config.json`, write live port to lockfile.
- **Launch** → start host → wait until Kestrel is listening → open default browser to `http://127.0.0.1:{port}/`.
- **Tray icon on by default** — running indicator, "Open BookTrak" (reopens tab), "Quit".
- **Lifecycle: session-bounded.** No overnight/background work. Refresh + downloads only during active sessions.
- **Shutdown: "finish, then exit"** — on Quit, stop accepting new work, then wait for **zero active background operations** (cover fetches, author-works refreshes, OL calls in flight) **and** zero connected UI circuits before disposing the host. Track in-flight ops with a counter/`CountdownEvent`; surface "finishing N downloads…" in the tray tooltip.

> **Define "download/active op":** any outbound OL request or cover fetch. Wrap them so each increments/decrements a shared in-flight counter the shutdown logic checks.

---

## 9. External data clients (HTTP layer)

### 9.1 Open Library client

- A typed `HttpClient` (via `IHttpClientFactory`) with:
  - Mandatory `User-Agent: BookTrak/{version} (contact)`.
  - **Polly** retry + circuit breaker; respect `429`/`503` with backoff.
  - A simple **client-side rate limiter** (e.g. max N concurrent + min interval) so we stay polite.
- **Cover cache service** — check `covers/` first; only fetch on miss; write atomically. **Note:** `covers.openlibrary.org` is rate-limited **separately** from the main API (cover requests have historically been throttled per-IP). Cache-first mostly handles it, but a first-run **CSV import of a large library** can still trip it — throttle cover fetches and tolerate cover misses gracefully (placeholder, retry later).
- **Normalization layer** — OL fields are inconsistent (`description` is sometimes a string, sometimes `{type,value}`; `bio` likewise; dates are free text). Centralize this so the rest of the app sees clean types.
- **Offline behavior** — every OL call is wrapped so failure degrades gracefully: show cached data, queue nothing overnight, surface a non-blocking "couldn't reach Open Library" notice.

### 9.2 Audiobook metadata provider (audnexus)

Behind a small `IAudiobookMetadataProvider` interface so the source is swappable. **audnexus is the primary implementation**; manual entry is the always-available fallback. (audimeta / Hardcover could be added later as additional implementations behind the same interface.)

- Same hygiene as the OL client: typed `HttpClient`, descriptive `User-Agent`, Polly retry/circuit-breaker, self rate-limit. audnexus rate-limits with `429` + a `retryAfterSeconds` hint — honor it.
- **ASIN-keyed.** audnexus enriches by Audible ASIN, not ISBN — that's why `Edition.Asin` exists.

| Purpose | Endpoint |
|---|---|
| Book by ASIN | `GET https://api.audnex.us/books/{asin}?region=us` |
| Book chapters *(optional)* | `GET https://api.audnex.us/books/{asin}/chapters` |
| Author by ASIN | `GET https://api.audnex.us/authors/{asin}` |
| Author search | `GET https://api.audnex.us/authors?name=…` |

- **Field mapping** (audnexus → BookTrak `Edition`): `narrators[].name` → `Narrator`; `runtimeLengthMin` → `DurationSeconds` (×60); `publisherName` → `AudioPublisher`; `image` → cached cover; `asin` → `Asin`. Work-level bits (title, description, series, genres) reconcile against the existing OL work rather than creating a duplicate.
- **Finding the ASIN (the one rough edge).** audnexus is enrichment-by-ASIN; it isn't a great title search. Two supported paths:
  1. **Paste the ASIN** — user copies it from the audible.com product URL → enrich. Reliable, zero-scrape, recommended default.
  2. **Match an existing work** — when adding an audiobook to a work already in the library, let the user paste/confirm the ASIN, then enrich.
  *(A real title→ASIN search would need Audible's catalog endpoint or Hardcover; deferred — flag if you want it.)*
- **Caching & offline** — cache audnexus responses (SQLite) and covers (disk) exactly like OL; degrade gracefully when unreachable.
- **Honest caveat** — audnexus ultimately derives from Audible. It's community infrastructure in a gray area, but it's centralized, cached, and keeps BookTrak off Audible's servers directly.

---

## 10. Database, migrations & backup

- **EF Core + SQLite**, **WAL mode** enabled (multiple Blazor circuits = concurrent reads; WAL handles it cleanly for a single-user app).
- **`IDbContextFactory<BookTrakContext>` — required, not optional.** Blazor Server keeps a scoped service alive for the whole circuit, and a single `DbContext` can't service overlapping async UI events ("a second operation started on this context"). Use the **factory** and create **short-lived contexts** per operation/unit of work. This is the single most important EF decision for this app.
- **`busy_timeout` pragma** (e.g. 3–5 s) set alongside WAL. WAL gives many readers + one writer, but background cover/audnexus fetches writing while the UI writes can throw "database is locked"; the busy timeout lets writers wait instead of failing.
- **Self-referencing FK delete behavior** — configure `Book.PreferredEditionId` and `Book.ReadEditionId` as **`DeleteBehavior.Restrict`** (see §4) so the Book↔Edition reference cycle doesn't produce multiple-cascade-path errors.
- **Local search via FTS5** — an `fts5` virtual table over book title/subtitle/author names, kept in sync with **triggers** (insert/update/delete) or rebuilt on demand. Powers the §6 local search box.
- **Auto-migrate on startup**: back up `BookTrak.db` → `backups/` first, *then* `context.Database.Migrate()`. Migrations are compiled into the exe.
- **Backups**: copy DB (+ `config.json`) to `backups/`, filename suffixed `yyyyMMddTHHmmss`, **keep last 10**, prune older. Also expose a manual "Back up now" button.
- **Orphan cover cleanup** — deleting a book/edition leaves files in `covers/`. Run a small prune (startup sweep + manual "Clean up covers" button) that deletes cover files no longer referenced by any book/edition.
- **Single-file + SQLite gotcha:** the native `e_sqlite3` library must be extracted — set `IncludeNativeLibrariesForSelfExtract=true` (see §12).

---

## 11. Decisions (confirmed) + open questions

### Confirmed (from your spec, refined)
| Area | Decision |
|---|---|
| UI model | Browser UI served by local Blazor Server (no native shell) |
| Stack | .NET 10 (`net10.0-windows`), Blazor Server, Kestrel on loopback, EF Core + SQLite, JSON config |
| Hosting | `IHost` on background thread; WinForms tray on STA main thread; bidirectional shutdown |
| Target | Windows x64 |
| Portability | Single folder, relative to `.exe` |
| Launch | Double-click → backend → default browser, normal tab |
| Lifecycle | Session-bounded; no overnight work |
| Shutdown | "Finish, then exit" — zero UI circuits **and** zero active ops |
| Tray | On by default — indicator + Quit + reopen |
| Port conflict | Auto-fallback from `6123`; persisted; live port in lockfile |
| Backup | DB + config → `backups/`, timestamp suffix, keep last 10 |
| Updates | Manual `.exe` swap; EF auto-migrate on startup (backup first) |
| Sync | Single machine only |
| **Data model** | **`Author → Work → Edition`; work-level fields on `Book`, printing fields on `Edition`; preferred edition per work** |
| **Audiobooks** | **An audiobook is an `Edition` with `Format = Audiobook` + narrator/duration/audio-publisher — no separate copies table** |
| **Ignoring** | **Orthogonal `IsIgnored` bool on **both** `Work` and `Edition`; one hide/show toggle filters both** |
| **Read state** | **Work-level `Status` (`None`/`WantToRead`/`Reading`/`Read`) + `DateStarted`/`DateRead` + `MyRating`, with optional `ReadEditionId`; all transitions skippable** |
| **Ratings** | **Half-stars, 0.5–5.0, stored as `double`/REAL** (SQLite has no real `decimal`) |
| **Add by ISBN** | **Included — resolves a specific edition/audiobook** |
| **Authors** | **Many-to-many `BookAuthor` only — no scalar `AuthorId` on `Book`** |
| **Preferred edition** | **Auto-picked on add: English → ISBN → cover → page count, then newest; Book cover derived from it (no Book cover columns)** |
| **EF / Blazor** | **`IDbContextFactory` + short-lived contexts; `busy_timeout` pragma; self-ref FKs `DeleteBehavior.Restrict`** |
| **Single instance** | **Named `Mutex`; stale-lockfile detection via PID liveness check** |
| **Local search** | **SQLite FTS5 virtual table kept in sync via triggers** |
| **Series** | **`Series` entity + `Book.SeriesId`/`SeriesPosition`; powers "next in series"** |
| **Genres** | **Local curated taxonomy + raw OL subjects stored separately** |
| **Covers** | **Cached to `covers/`; not backed up; orphan-cleanup sweep** |
| **OL client** | **Typed HttpClient, mandatory User-Agent, Polly retry, self rate-limit, normalization layer; covers endpoint throttled separately** |
| **Audiobook data** | **audnexus (`api.audnex.us`) primary, by ASIN, behind a swappable provider interface; manual entry fallback; no direct Audible scraping** |
| **Security** | **Loopback-only bind; no app-level auth (accepted trade-off for single-user)** |

### Resolved decisions (formerly open)
- **O-1** Owned-copies model → audiobook = edition; no copies table.
- **O-2** Ignore status vs. flag → orthogonal `IsIgnored` bool on work **and** edition.
- **O-3** Add-by-ISBN → **in**.
- **O-4** Rating scale → **half-stars (0.5–5.0)**.
- **O-5** `Reading` status → **in**, alongside the simple `WantToRead → Read` path.

### Review-pass decisions (this revision)
Hardening + scope decisions from the design review:
- **R-1** Rating storage → **`double`/REAL** (not `decimal` — SQLite stores `decimal` as TEXT and breaks ordering). Applies to `MyRating` and `AverageRating`.
- **R-2** Self-referencing FK delete behavior → **`DeleteBehavior.Restrict`** on `PreferredEditionId` / `ReadEditionId`.
- **R-3** Book cover → **derived from preferred edition**; no cover columns on `Book`.
- **R-4** Preferred-edition auto-pick → **English → ISBN → cover → page count**, then newest, then first.
- **R-5** EF access → **`IDbContextFactory` + short-lived contexts** (Blazor Server requirement).
- **R-6** **Drop scalar `AuthorId`** from `Book`; authors via `BookAuthor` join only.
- **R-7** **Single-instance `Mutex`** + **PID-liveness stale-lockfile** handling.
- **R-8** **`busy_timeout` pragma** alongside WAL.
- **R-9** Schema-shaping decisions (R-1, R-5) land in **Phase 1**, not later.
- **R-10** Add **covers-endpoint rate-limit** + **no-auth-on-loopback** notes (documented, accepted).
- **R-11** Build now: **CSV import** (Goodreads/StoryGraph), **reading stats dashboard**, **Series entity**, **FTS5 local search**, **normalization-layer tests**, **orphan cover cleanup**.
- **R-12** Build now (UX batch): **flat library view** (virtualized), **filter/sort/facets**, **barcode→ISBN scan**, **re-sync/refresh** (`LastSyncedUtc` + stale indicator, never overwrites local state), and a **richer stats dashboard**. All ride the existing schema — **no new entities**.

### Deferred / backlog (revisit later)
- **Export (CSV / JSON)** — plain-text export of the library. Not in current scope; the `.db` + backups cover portability for now.
- **Title→ASIN search** for audiobooks (would need Audible catalog or Hardcover) — still deferred per §9.2.

All design questions are settled — the schema is ready to build against.

---

## 12. Packaging (single-file self-contained)

`.csproj` essentials:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWindowsForms>true</UseWindowsForms>     <!-- tray icon -->
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  <!-- InvariantGlobalization weighs size vs. correct culture handling; -->
  <!-- leave false if you care about non-English title/sort behavior. -->
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

Publish:
```
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:SelfContained=true
```

Smoke-test the published exe in a **clean folder** (no SDK) to confirm SQLite native extraction and first-run DB creation work.

---

## 13. Phased build plan (Claude Code-friendly)

Each phase is independently runnable and leaves the app in a working state. Build in order.

### Phase 0 — Skeleton & host bridge
- Scaffold Blazor Server app, `WinExe`, `[STAThread]`.
- Wire `IHost` on background thread + WinForms tray (indicator, Open, Quit).
- Bind Kestrel to `127.0.0.1`; implement port probe/fallback from `6123`; write `config.json` + `BookTrak.lock`.
- On startup: wait for "listening," then open default browser.
- Bidirectional shutdown; in-flight-op counter scaffolding.
- **Single-instance `Mutex`** + lockfile written with live port + PID; relaunch checks PID liveness and reopens the browser if alive, else clears the stale lock.
- **Exit criteria:** double-click launches, browser opens to a hello page, tray Quit cleanly stops everything; a second launch detects the running instance (live PID) and just reopens the tab; a stale lockfile (killed process) is cleared and startup proceeds.

### Phase 1 — Data layer
- EF Core models (Author, Book, Edition, Genre, **Series**, BookAuthor, BookGenre, Settings) — `Edition.Format` enum + audiobook fields, `IsIgnored` on Book and Edition, `Book.SeriesId`/`SeriesPosition`, **no scalar `AuthorId`**.
- **`IDbContextFactory` + short-lived contexts** (Blazor Server pattern) — wire this now, it's load-bearing.
- Ratings as **`double`/REAL**; self-ref FKs (`PreferredEditionId`/`ReadEditionId`) configured `DeleteBehavior.Restrict`.
- DbContext, **WAL + `busy_timeout`** pragmas, initial migration.
- **FTS5** virtual table over title/subtitle/author + sync triggers.
- Startup: backup-then-`Migrate()`; backup rotation (keep 10).
- **Exit criteria:** DB created on first run, migrations apply, backup written before migrate, prune works; ratings round-trip and sort correctly as `double`; FTS5 returns hits on seeded rows.

### Phase 2 — Open Library client
- Typed `HttpClient` + User-Agent + Polly + self rate-limit.
- Implement: author search, work search, work detail, editions, ratings, author works, ISBN lookup, covers cache.
- Normalization layer (string-or-object, free-text dates).
- **Normalization-layer unit tests** — a thin test project covering the bug-magnet cases: `description`/`bio` as string vs `{type,value}`, free-text dates, missing fields. This is where OL inconsistency bites hardest.
- **Exit criteria:** can fetch + normalize an author and a work from console/test harness; covers land in `covers/`; normalization tests pass.

### Phase 3 — Core read-only UI
- Author list → author detail (their books) → book detail.
- **Flat library view (all books)** — whole-library grid/list, **virtualized** with Blazor `<Virtualize>` so big libraries stay responsive (keep row rendering and cover loading cheap/lazy).
- Cover rendering from cache (derived from preferred edition); status badges; average + my rating display.
- **FTS5-backed search box** (title/author).
- **Basic sort** on the library view (date added, title, author, publish date) — facet *filtering* that depends on status/format lands in Phase 9.
- Responsive layout/theme.
- **Exit criteria:** browse seeded data end to end; the flat library view virtualizes a few-thousand-row seed without jank; search returns expected hits; sort orders are correct.

### Phase 4 — Add & enrich flows
- Add author (OL search + manual).
- Add book (OL search → work + **auto-picked preferred edition** [English → ISBN → cover → pages] + cover + rating).
- Add by ISBN (resolves a specific edition/audiobook).
- **Barcode → ISBN scan** feeding the add-by-ISBN resolver: camera capture on loopback (secure-context exemption — no TLS), native `BarcodeDetector` with a `@zxing/browser` fallback; manual ISBN entry always available.
- **Exit criteria:** add a real author and book from OL; preferred edition is chosen sensibly; data + cover persist and display; a scanned EAN-13 barcode resolves to the right edition (with manual fallback when the camera or detector is unavailable).

### Phase 5 — "Find new books for an author"
- Fetch author works, diff vs. local, render gap list, one-click add (paginate + dedupe).
- **Exit criteria:** gap list is correct; adding from it works.

### Phase 6 — Re-sync / refresh
- Re-pull OL (and audnexus) metadata for an existing author/work: description, average rating, new editions, author bio/photo. Reuse the Phase 2 client hygiene (User-Agent, Polly, rate-limit) + normalization layer.
- Stamp `LastSyncedUtc` on success; surface a **"stale"** indicator when a record hasn't synced past a threshold.
- **Don't clobber local state** — re-sync touches OL-sourced fields only, never `Status`/`MyRating`/`IsIgnored`/preferred-edition choice.
- Throttle + dedupe — a "refresh all" is a burst of OL calls; stay polite, tolerate partial failure, leave already-fresh records untouched.
- **Exit criteria:** refreshing a stale author/work pulls updated fields and any new editions without touching local reading state; "refresh all" stays polite under rate limits and reports per-record success/failure.

### Phase 7 — Status & ignoring
- Status transitions: want-to-read, **mark as reading (DateStarted)**, mark read (date + half-star rating + optional read-edition); all arrows skippable + reversible.
- Half-star rating picker (0.5–5.0), stored as `double`.
- Ignore/un-ignore at **work** and **edition** level; repoint preferred edition off an ignored edition.
- Hide/show-ignored filter (default hidden) across both levels.
- **Exit criteria:** all status transitions persist; half-stars save correctly; ignoring a work hides it, ignoring an edition hides just that printing; filter works.

### Phase 8 — Editions & audiobooks
- Add/manage multiple editions per work; `Format` (print/ebook/audiobook).
- `IAudiobookMetadataProvider` with an **audnexus** implementation (reuse the Phase 2 HttpClient hygiene: User-Agent, Polly, rate-limit, honor `retryAfterSeconds`).
- Audiobook flow: paste ASIN → enrich (narrator, duration, audio publisher, cover) → map onto `Edition`; manual entry fallback.
- **Exit criteria:** a single work shows a print edition and an audnexus-enriched audiobook edition with narrator + runtime side by side; ASIN paste + manual entry both work.

### Phase 9 — Library: filtering, sorting & facets
- On the flat library view (Phase 3): facet by **status** (Phase 7), **format** (Phase 8), **genre**, **rating**, **series**, **owned/ignored**; multi-select within a facet *and* stack across facets.
- Sort by date added, rating, title, author, publish date (extends the Phase 3 basic sort).
- Live facet counts; compose cleanly with the FTS5 search box (filter the FTS result set).
- Keep it index-friendly — filtered/sorted queries should hit indexes, not table-scan a few-thousand-row library.
- **Exit criteria:** stacking facets (e.g. *audiobook + want-to-read + ≥4 stars*) returns the correct set; sort orders are correct; facets compose with search; large-library queries stay fast.

### Phase 10 — Polish & shutdown semantics
- "Finish, then exit": real wait on in-flight ops + UI circuits; tray tooltip status.
- Empty states, error toasts, offline degradation, manual "Back up now," settings page.
- **Orphan cover cleanup** — startup sweep + manual "Clean up covers" button.
- **Exit criteria:** graceful shutdown verified with downloads in flight; offline mode degrades cleanly; orphaned covers get pruned.

### Phase 11 — Series & "next in series"
- Populate `Series`/`SeriesPosition` (audnexus for audiobooks, manual otherwise).
- Series view: full order, your read/unread state per volume, **what's next** after your last-read, missing-volumes list.
- **Exit criteria:** a multi-book series renders in order with correct gaps and a "next up" suggestion.

### Phase 12 — CSV import (Goodreads / StoryGraph)
- Parse a Goodreads/StoryGraph export; map ISBN → add-by-ISBN resolver, `My Rating` → `MyRating`, `Date Read` → `DateRead`, shelf → `Status`.
- Dedupe by work id / ISBN; throttle OL + cover fetches; report imported / skipped / unresolved rows.
- **Exit criteria:** a real export imports cleanly, dupes are skipped, unresolved rows are surfaced (not silently dropped).

### Phase 13 — Reading stats dashboard
- Derived views only (no new schema): books read this year, ratings distribution, pages read, hours listened, reads-per-month, **format breakdown**, **avg rating by genre/author**, **most-read author/narrator**, **longest/shortest reads**, **added-vs-read trend**.
- **Exit criteria:** numbers match a hand-counted sample of the library; each aggregate (incl. the new ones) reconciles against a manual count.

### Phase 14 — Packaging
- Icon, single-file publish, clean-folder smoke test, README.
- **Exit criteria:** one exe in an empty folder runs the whole app.

---

## 14. Risks & watch-items
- **Single-file + SQLite native lib** — easy to miss; verify in a clean folder early (Phase 1).
- **OL field inconsistency** — invest in the normalization layer up front (Phase 2) or it leaks everywhere.
- **Politeness** — User-Agent + caching + rate limiting are non-optional; getting blocked breaks enrichment.
- **Free-text OL dates** — never parse author birth/death or first-publish as `DateTime`; keep as strings.
- **Multiple authors per work** — the `BookAuthor` join avoids a painful later refactor.
- **audnexus dependency** — a community service that derives from Audible; it can rate-limit, lag, or change. Keep it behind `IAudiobookMetadataProvider`, cache responses, and always allow manual entry so audiobooks work even when it's down.
- **ASIN discovery** — audnexus enriches by ASIN but doesn't search well by title; the paste-the-ASIN flow is the reliable path. Don't over-invest in title→ASIN search unless you add Hardcover/Audible catalog later.
- **Shutdown race** — define "active op" precisely and increment/decrement consistently, or "finish then exit" can hang or exit early.
- **Blazor + DbContext** — never share one `DbContext` across overlapping circuit events; always go through `IDbContextFactory`. This is the most likely "works on my machine, throws under clicking" bug.
- **SQLite `decimal`** — resolved by using `double` for ratings; don't reintroduce `decimal` anywhere you sort/compare.
- **FTS5 sync** — keep the FTS triggers in lockstep with the base tables (or rebuild on migration), or search silently goes stale.
- **CSV import dupes & rate limits** — a big first import is a burst of OL/cover calls; dedupe hard, throttle, and never silently drop unresolved rows.
- **Re-sync burst** — "refresh all" is the same rate-limit hazard as a big CSV import: throttle, dedupe, tolerate partial failure, and never overwrite local reading state (`Status`/`MyRating`/`IsIgnored`/preferred edition) with OL data.
- **Virtualization correctness** — `<Virtualize>` only helps if rows are cheap; keep cover loading lazy and facet/sort queries index-backed, or a multi-thousand-book library will jank despite virtualization.
- **Barcode scanning** — camera access works on loopback (secure-context exemption), but `BarcodeDetector` is Chromium-only; feature-detect and fall back to a JS decoder (`@zxing/browser`) so a Firefox/Safari default browser still scans. Always allow manual ISBN entry.
