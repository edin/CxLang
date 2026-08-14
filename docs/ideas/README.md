# CX Ideas

This folder captures language and compiler ideas that are worth preserving but
are not necessarily scheduled or accepted designs. Keeping an idea here does
not commit CX to implementing it.

Each idea lives in its own Markdown file and starts with metadata like:

```yaml
---
status: captured
area: language
created: YYYY-MM-DD
---
```

## Statuses

- `captured` — recorded for later consideration.
- `exploring` — semantics or implementation tradeoffs are being investigated.
- `ready` — the design is sufficiently clear to schedule.
- `implementing` — implementation is in progress.
- `implemented` — the behavior is implemented and verified.
- `deferred` — intentionally postponed.
- `rejected` — considered and intentionally not pursued.
- `superseded` — replaced by another idea or design.

## Suggested structure

An idea should focus on durable intent rather than implementation details that
may quickly become stale:

1. Motivation
2. Desired behavior with CX examples
3. Proposed semantic rules
4. Constraints and non-goals
5. Possible implementation direction
6. Open questions
7. Completion criteria

When an idea is implemented, update its status and link to the relevant tests
or permanent design documentation. Rejected and superseded ideas should remain
in this folder so the reasoning is not lost.

