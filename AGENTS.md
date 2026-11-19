# RoomTwin Agent Instructions

## Communication

- Communicate with the developer in Korean.
- Use English for code identifiers, API names,
  and Git commits.

## Stack

- Unity 6.6, C#, AR Foundation, ARCore
- Android Kotlin, LiteRT-LM, Gemma 4 E2B

## Ownership

- Cursor owns Unity and AR integration.
- Codex owns native-ai and independent tests.
- Coordinate before changing another agent's files.

## Engineering Rules

- Use meters for spatial coordinates.
- Keep AR session coordinates separate
  from persisted room data.
- Never treat VLM output as precise measurements.
- Preserve Unity .meta files.
- Never commit credentials or model weights.
- Report tested and untested functionality separately.

## Reporting

- Explain changes, tests, issues, and
  next steps in Korean.
