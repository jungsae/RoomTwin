# Development Workflow

RoomTwin is developed in parallel by Cursor and Codex, each in its own Git worktree. This file is the shared procedure. `AGENTS.md` only summarizes it.

`main` is updated only when the user approves a merge. Agents do not push to `main` and do not merge their own pull requests.

## Ownership

| Path | Owner | Worktree |
|------|--------|----------|
| `RoomTwinScanner/`, `.cursor/`, root `AGENTS.md`, `docs/`, `.github/` | Cursor | `C:\DigitalTwin\RoomTwin` or a Cursor worktree |
| `native-ai/` | Codex | `C:\DigitalTwin\worktrees\codex-native-ai` |

Branch separation does not change ownership. Do not edit the other agent's worktree or files. If a shared interface must change, open an issue and describe the contract in `docs/` instead of editing the other module.

## A. Intake

1. Read the request and write down the completion check.
2. Search open issues before creating one. Prefer `gh issue list --repo jungsae/RoomTwin --state open --search "<keywords>"`.
3. Reuse an open issue when it is the same task. Create one only for a real code or docs change that has no matching issue. Use `.github/ISSUE_TEMPLATE/feature.yml` or `bug.yml`.
4. Name the owning agent and the directories that may change.
5. Record the current branch, `git status`, and `git worktree list`. Do not start from a worktree that belongs to the other agent.

## B. Start

1. `git fetch origin` when network access is available.
2. Work on a feature branch, not `main`. Branch names use a Conventional Commits type, such as `feat/...`, `fix/...`, or `chore/...`.
3. If the current worktree has user changes, do not switch branches, reset, restore, or stash those changes. Create a separate worktree from `origin/main` at a path that does not collide with the existing worktrees.
4. Bring in the latest `origin/main` only on a clean worktree (`git merge origin/main` or an equivalent fast-forward). If the worktree is dirty, skip the sync and say so.
5. If the sync conflicts, stop and report the files. Do not invent a resolution.
6. Unset an accidental upstream of `origin/main` before the first push. The first push is `git push -u origin HEAD` from the feature branch only.

## C. Implement and verify

1. Change only files the owning agent is allowed to change.
2. Add or update the tests that belong to the change, then run them.
3. Update the responsible doc from the index in `AGENTS.md` when behavior, scope, or verification changes.
4. Keep three results separate in notes and in the pull request:
   - automatic tests (Unity Test Runner, Gradle, GitHub Actions)
   - Editor or device checks performed by a person
   - items that were not run
5. Do not record an unrun test as passed. A green local Editor run is not a GitHub Actions result. A device check is not replaced by CI.

Unity Editor tests stay on the developer's machine. This repository does not add a Unity CI job: batch mode is blocked by the personal Editor license, and agents must not launch Unity, Hub, or batch mode on their own.

## D. Finish

1. Review `git status` and `git diff`. Leave unrelated user changes unstaged. Do not commit `native-ai` model weights, credentials, or the other agent's files.
2. Commit with a Conventional Commits subject. Explain why in the body when the subject is not enough.
3. Push only after confirming the current branch is not `main`: `git push -u origin HEAD`. If the branch is `main`, stop. Never force-push.
4. Open one draft pull request into `main`: `gh pr create --draft --base main`. If a PR for the same branch already exists, update that PR. Do not open a second one.
5. Fill `.github/pull_request_template.md`. Link the issue with `Refs #123`. Do not write `Fixes`, `Closes`, or `Resolves`; those keywords close the issue when the PR merges, and draft creation must not be treated as completion.
6. Tell the user the PR URL and what to review. Stop there.

## E. Merge

1. Do not push to `main`.
2. Do not merge, enable auto-merge, or approve on behalf of the user.
3. Merge only after the user explicitly approves that merge.
4. If CI fails, fix it on the feature branch or report the log. Do not describe a failed or unrun check as success.
5. Delete the feature branch only after the user has merged and the worktree has no uncommitted work. Do not delete the other agent's branch or worktree.

## Issue and PR commands

Search first:

```sh
gh issue list --repo jungsae/RoomTwin --state open --search "room geometry"
```

Create when nothing matches:

```sh
gh issue create --repo jungsae/RoomTwin --template feature.yml --title "feat: short title"
```

Draft PR from the feature branch:

```sh
gh pr create --repo jungsae/RoomTwin --draft --base main --title "feat: short title" --body-file pr-body.md
```

`gh` must be installed and authenticated. If it is missing, commit and push the feature branch with Git, then report that the issue and draft PR were not created. Do not invent another credential path.
