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

## Current Focus

Editor room geometry in `GeometrySandbox`: four convex floor corners and a wall height, in room-local meters. AR raycast input, save/load, and furniture are not part of this slice. Phase 0 AR probe code stays as it is.
