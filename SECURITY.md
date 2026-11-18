# Security Policy

## Supported versions

Only the latest release of BookTrak receives fixes.

## Reporting a vulnerability

Please report vulnerabilities privately via GitHub's
[private vulnerability reporting](https://github.com/gregory-george/BookTrak/security/advisories/new)
rather than opening a public issue.

## Security model

BookTrak is a single-user, local-first desktop app. Its web UI is served by Kestrel bound to
the loopback interface (`127.0.0.1`) only, and that bind is the security boundary: there is no
application-level authentication, so any process or user on the same machine can reach the UI.
This is a documented, accepted trade-off. Reports that require local code execution on the
user's machine are generally out of scope; reports of the UI becoming reachable from another
host, or of untrusted remote data (Open Library, audnexus, Audible catalog search, imported CSV
files) leading to code execution or data corruption, are in scope.
