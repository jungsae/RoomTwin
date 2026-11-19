# Roadmap

Phases are sequential for the MVP vertical slice. Later phases may start design notes early, but code should not skip ahead of the active phase without updating this file.

## Phase 0 — Project Foundation

- Keep docs/rules current
- Add AR Foundation + ARCore XR packages to `RoomTwinScanner`
- Establish empty module folders / asmdefs as needed
- Editor mock room data path

## Phase 1 — Geometry

- Plane / raycast capture on device
- Convert to meter estimates with uncertainty flags
- Capture UX that does not claim survey-grade accuracy

## Phase 2 — 3D Room Generation

- Build floor + walls from measurements
- Room-local coordinate frame
- Visual preview in AR and/or free camera
- Editor slice started: `GeometrySandbox` builds one convex quad (floor + four walls) without AR input

## Phase 3 — Manual Correction

- Edit wall length, height, and corners
- Recompute mesh from corrected model
- Guardrails against non-physical edits (optional soft warnings)

## Phase 4 — Save / Load

- Local persistence format for room + furniture
- Round-trip in Editor with mock data, then on device

## Phase 5 — Basic Furniture Placement

- Small preset catalog
- Move / rotate within room
- Persist placement with the room

## After MVP (Backlog Only)

- Native AI assist via `native-ai` contract (Codex)
- Better scanning (depth fusion, meshing) if device evidence supports it
- Export / share formats

## Status note (numbers unchanged)

The phase numbers above are kept. Two labels currently overlap, and this file does not renumber them:

- The Phase 1 heading says Geometry, while its bullets still describe AR measurement capture.
- Phase 2 is 3D room generation, and its last bullet is the Editor `GeometrySandbox` that has already been built.

D-009 records the proposal. Device checks and Editor tests are not GitHub Actions results.

## Current Focus

`GeometrySandbox` is in `origin/main` at `69c80fa` and has been checked in the Unity Editor. Phase 0 AR probe behavior has a separate developer device check. AR raycast input into the room model, save/load, and furniture are still later work. Git workflow setup does not change those phases.
