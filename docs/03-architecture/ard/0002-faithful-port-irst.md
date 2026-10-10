# ADR 0002: Port faithfully first, improve later

## Status

Accepted · 2026-10-09

## Context

The original implementation contains behaviors that look odd but may exist for good reasons, for example:

- an asymmetric threshold in the fuzzy title match;
- film deduplication that depends on how a missing year is compared;
- an endpoint named `today` that does not filter by date.

If the code is rewritten and improved at the same time, a difference in output cannot be classified as a bug in the port or as an intentional change.

## Decision

Each module is ported in two steps:

1. **Parity.** Port the logic so that it matches the original behavior. Verify it with reference cases: pairs of input and expected output captured by running the original implementation.
2. **Improvement.** Only after parity is reached, change the behavior on purpose. Each change goes in a separate commit (`fix:` or `feat:`), updates the tests, and gets its own ADR when significant.

Known deviations from the original are listed in the documentation.

Reference cases are currently kept outside this repository. Parity tests read them from a configurable folder and are skipped when it is missing, so the CI stays green without them. They may be added to the repository later.

Small parity tables are committed; larger golden cases stay private

## Alternatives considered

| Option | Why not |
|---|---|
| Improve while porting | Hides the cause of every difference. |
| Write everything from the user stories only | Cleaner, but throws away edge cases learned from real sources. |

## Consequences

Positive:

- Confidence that the port behaves like the original.
- Every deliberate change is traceable.

Negative:

- Known quirks live in the code for a while.
- Reference cases take effort to produce and cannot cover every situation.
- Until the cases are in the repository, parity can only be checked locally.