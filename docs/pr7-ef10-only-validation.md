# PR #7 EF10-only validation

## Phase 1 — inventory and baseline

Starting HEAD: `3711497019fe6a24522060b23ba14f815c590a68`, matching the plan,
on feature/efcore-integration. Existing untracked planning documents and
pr_summary.md are preserved. Read adapter/state/naming/services, canonical SQLite
tests, LocalDB fixture/tests, compatibility projects, package smoke, CI and docs.
The active cross-version surface is the adapter net6.0/EF7 references, reflected
EfRuntimeParameters and test prototype, EF7–9 test projects/friend assemblies,
four-version workflow and conditional package-smoke properties. Core remains
netstandard2.0; QuerySyntax remains netstandard2.1.

SDK 10.0.401, .NET runtime 10.0.12, Windows 10.0.26200. Restore, solution test and
format verification passed before production edits: 209 tests (62 core + 5
QuerySyntax + 14 existing integration + 128 adapter), zero failed/skipped.
Separate LocalDB suite: 23 passed, zero failed/skipped, real current-user engine
17.0.4025.3. No production files changed in this phase.

Checkpoint commits and subsequent phase evidence are recorded below as work proceeds.
