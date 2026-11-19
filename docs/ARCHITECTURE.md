# Architecture

## Repository Layout

```
RoomTwin/
  AGENTS.md                 # shared agent rules
  .cursor/rules/            # Cursor-only rules
  docs/                     # product / design docs
  RoomTwinScanner/          # Unity 6.6 Android AR app (Cursor)
  native-ai/                # Kotlin on-device AI module (Codex)
```

## Runtime Modules (MVP)

```
[AR Session] -> [Measurement Capture] -> [Room Builder]
                                              |
                                      [Manual Correction]
                                              |
                                      [Room Store]
                                              |
                                      [Furniture Placement]
```

| Module | Responsibility |
|--------|----------------|
| AR Session | AR Foundation session, plane/raycast input in session space |
| Measurement Capture | Convert uncertain AR hits/planes into candidate dimensions (meters) |
| Room Builder | Build editable room geometry from room-local meters. `RoomGeometryData` does not store AR session poses. |
| Manual Correction | User edits length / height / corners |
| Room Store | Serialize / deserialize room + furniture; never stores live session poses as truth |
| Furniture Placement | Place preset furniture in room-local coordinates |

## Coordinate Policy

- **Session space**: transient AR tracking frame. Not persisted.
- **Room-local space**: origin fixed at room definition time; units are meters.
- Save/load and furniture use room-local space only.

## Unity Project Status (as of repo snapshot)

- Project: `RoomTwinScanner` on Unity `6000.6.9f1`, URP, Input System
- Android min SDK 29, product version `0.1.0`
- AR Foundation / ARCore are in `Packages/manifest.json`; Phase 0 probe scene is `ARPlaneProbe`
- Editor room mesh lives in `GeometrySandbox` and does not reference the AR session

## Native AI Boundary

- `native-ai/` is a separate Android/Kotlin module (LiteRT-LM, Gemma 4 E2B).
- Codex owns implementation and independent tests.
- Unity must not embed model weights or call undocumented APIs.
- Future integration goes through an explicit contract recorded in `DECISIONS.md`.
- MVP room workflow does not depend on AI inference.

## External Reference (outside repo)

- `C:\DigitalTwin\references\arfoundation-samples` — Unity AR Foundation samples for study only; not a runtime dependency.
