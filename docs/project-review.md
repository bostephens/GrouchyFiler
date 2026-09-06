# Project review - September 2026

## Scope and evidence

Reviewed the application entry point, WinForms lifecycle and controls, configuration models and converters, scanner, native deletion implementation, matching, single-instance coordination, memory/disk logging, regression harness, build/publish settings, sample configuration, and both guides. Generated resources and images were inventoried; this was a source review, not a binary forensic audit. No third-party NuGet package references are declared in either project. Runtime vulnerability status was not independently audited.

Validation: Windows with .NET SDK 10.0.400; `dotnet run --project GrouchyFiler.Tests -c Release` passes 185 checks. Deletion checks operate on generated fixtures behind the existing test boundary guard. The desktop configuration was not changed. These improvements are included in release 1.0.2.

## Changes made

| Finding | Change | Verification |
| --- | --- | --- |
| Repeated JSON properties silently replaced earlier values, including dry-run and pattern settings. | Reject duplicate names case-insensitively throughout the document, before deserialization. Invalid reloads retain the existing pause/dry-run fallback. | Top-level duplicates, capitalization variants, root properties, and custom pattern objects are rejected. |
| A broad rule could delete its own configuration. | Carry the loaded source path with the scan configuration and skip that exact path before matching or deletion. | An aged configuration under a live `*` rule is preserved and never reaches the deletion callback. |
| Editor startup used a bare executable name. | Resolve Notepad under `Environment.SystemDirectory`, retaining separate argument passing. | Source inspection and successful compilation; interactive editor launch was not exercised. |
| Glob includes were translated into backtracking regexes, while exclusions used the framework wildcard matcher. | Use `FileSystemName.MatchesSimpleExpression` for includes too. Explicit regex rules retain their 100 ms timeout. | Existing matching checks plus 30 compatibility cases covering wildcard, punctuation, case, and Unicode examples. |
| Guides prohibited syntax the parser accepts. | Document comments/trailing commas and duplicate-property rejection accurately. | Parser regression for accepted commented JSON with a trailing comma. |
| Solution builds omitted the test project. | Add the existing regression executable to the solution. | Release solution build. Running the checks still requires the documented `dotnet run` command. |

## Existing safeguards worth retaining

Live deletion pins ordinary ancestor directories, rejects reparse points, exclusively opens the target, rechecks age and size through the handle, and submits disposition on that same handle. Keep those checks together when adding deletion modes. Existing regression checks cover concurrent replacement/write attempts, busy files, cancellation before disposition, and preservation filters.

The scanner serializes scans and cancels on mode, pause, reload, and shutdown changes. Configuration rejects unknown fields, entire drive roots, invalid ranges, and log paths lexically inside watched roots. Log queues and retained history are bounded. These controls reduce accidental deletion and resource growth; they do not make arbitrary user rules safe.

## Remaining boundaries and follow-up work

- **Log destination trust:** disk logging uses ordinary path-based append and rotation. Its config exclusion checks are lexical; aliases, hard links, or concurrent directory replacement are not covered by the cleanup handle protections. Keep logs in a directory controlled by the current user. A future hardening change should pin log ancestors and validate file identities for append and every rotation target, with dedicated link/race tests.
- **Configuration identity:** self-preservation compares the loaded absolute path, not all possible filesystem aliases. Keep the app/configuration outside cleanup roots where practical. Stronger identity protection would require volume/file identity checks and explicit alias tests.
- **Operational safety:** saved `dryRun: false` still starts live cleanup immediately. There is no deletion budget or undo. Pause cannot retract a disposition already submitted to Windows.
- **Resource limits:** configuration size/root/pattern counts are unbounded. A regex timeout applies per match, so many expensive rules can still slow a scan. Consider a configuration-size limit and reporting repeatedly timed-out rules.
- **Audit durability:** background disk entries can be dropped under pressure or at exit. A reliable audit mode would need an explicit durability contract and shutdown handling.
- **Distribution:** releases remain unsigned, and no repository CI workflow is present. A Windows build/test workflow and signed releases would improve repeatability and provenance. A self-contained release must be rebuilt to incorporate runtime servicing updates.

## Useful feature candidates

These are proposed follow-ups, not implemented features.

1. **Deletion budget with automatic pause:** optional maximum file count and bytes per live session. Pause on reaching the budget rather than resetting it every scheduled scan, so a bad rule cannot repeatedly consume a fresh allowance. Show the reason and require an explicit resume/reset.
2. **Structured preview:** show candidate path, rule, size, age, and exclusion reason in a sortable table, with a total reclaimable size and CSV export. Any later deletion must still revalidate eligibility rather than trusting stale preview metadata.
3. **Recovery mode:** an optional quarantine or Recycle Bin destination. Design collision handling, restore, retention, cross-volume behavior, and link/race safeguards before exposing it as undo.
4. **Folder exclusions:** skip selected subtrees before enumeration, useful for build caches containing protected subfolders. Define how exclusions interact with overlapping roots and test that another root cannot unexpectedly override preservation.
5. **Configuration validation UI:** validate edits without activating them, show rule-level errors, and explain pending changes to live mode before reload.
