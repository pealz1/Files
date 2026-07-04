# Files Pro issue inventory

Snapshot date: 2026-06-21.

The upstream issue list is large and changes constantly. A current sample taken through the GitHub API showed the largest open buckets as file operations, network drives, crashes, tags, info pane, toolbar, context menu, git, omnibar, sidebar, archives, dual pane, and status center.

This fork should treat issue work as reproducible buckets, not a single mega-fix:

- Startup and shutdown: background workers must observe cancellation and stop on app close.
- File operations: copy/move/delete must be queued, cancelable, logged, and resilient to locked files.
- Network drives: all network enumeration and preview paths need short timeouts and cancellation.
- Archives: archive browsing/extraction must cap previews, defer thumbnails, and avoid shell extension crashes.
- Tags: startup must not block on tag database scans or broken registry state.
- Thumbnails/previews: large media folders need concurrency limits and fallback icons.
- Default file manager: shell registration needs dry-run, backup, rollback, and no `explorer.exe` shell replacement.

Milestone status:

- Added cancelable Files Pro scanners and indexers.
- Added dry-run cleanup plans, guarded move execution, and operation logs.
- Added a queued copy/move service module, not yet wired as the global shell copy engine.
- Added optional Everything CLI detection, falling back to the SQLite index.
- Added NTFS fast-path capability detection. The native USN reader is intentionally not enabled yet.
- Added production package and rollback scripts.

Remaining high-risk issue buckets need dedicated repros and tests before claiming them fixed.
