# RoomTwin Agent Instructions

## Communication

- Communicate with the developer in Korean.
- Use English for code identifiers, API names,
  and Git commits.

## Stack

- Unity 6.6 (`6000.6.x`), C#, AR Foundation, ARCore
- Android Kotlin AI module: LiteRT-LM, Gemma 4 E2B
  (owned by Codex under `native-ai/`)

## Ownership

- Cursor owns Unity/AR integration in
  `RoomTwinScanner/` and shared docs/rules.
- Codex owns `native-ai/` and its independent tests.
- Do not edit another agent's files without
  coordination. Prefer interface contracts in docs.

## Engineering Rules

- Use meters for spatial coordinates.
- Keep AR session coordinates separate
  from persisted room data.
- Never treat VLM / LLM output as precise measurements.
- Preserve Unity `.meta` files.
- Never commit credentials or model weights.
- Report tested and untested functionality separately.
- Do not invent "verified" device results;
  record assumptions in `docs/DECISIONS.md`.

## Git Workflow

- Track each development task with a GitHub issue.
  Search open issues before creating one.
- Implement on a feature branch, never on `main`.
- Commit and push the feature branch after the
  code change and its tests.
- Open a draft pull request into `main`.
  Record what passed and what was not run.
- Link the issue with `Refs #`. Do not use
  `Fixes` or `Closes` in the draft.
- Do not push `main` and do not force-push.
  Merge only after the user approves that merge.
- Do not edit another agent's worktree.
- Procedure: `docs/DEVELOPMENT_WORKFLOW.md`.

## Documentation Index

| Path | Responsibility |
|------|----------------|
| `docs/PRODUCT_SCOPE.md` | MVP in/out of scope |
| `docs/ARCHITECTURE.md` | Modules and boundaries |
| `docs/ROADMAP.md` | Phased delivery order |
| `docs/TEST_PLAN.md` | Verification strategy |
| `docs/DECISIONS.md` | Decisions + verified vs assumed |
| `docs/DEVELOPMENT_WORKFLOW.md` | Issue, branch, PR, and merge steps |
| `.cursor/rules/*.mdc` | Cursor-only coding workflow |

## How to Use Docs

- Change product intent → update `PRODUCT_SCOPE.md`.
- Change module boundaries → update `ARCHITECTURE.md`.
- Change delivery order → update `ROADMAP.md`.
- Change verification → update `TEST_PLAN.md`.
- Record trade-offs / facts vs assumptions → `DECISIONS.md`.
- Keep this file for shared agent rules only;
  avoid duplicating long design prose here.

## Reporting

- Explain changes, tests, issues, and
  next steps in Korean.
