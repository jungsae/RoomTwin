# Product Scope

## Product

RoomTwin은 스마트폰 AR로 방 공간을 측정하고, 보정 가능한 3D 방 모델을 만든 뒤, 기본 가구를 배치하는 앱이다.

## MVP In Scope

1. **AR 공간 측정** — ARCore/AR Foundation으로 바닥·벽 후보를 수집하고 미터 단위 치수를 추정한다.
2. **3D 방 생성** — 측정 결과를 단순 방 메시/볼륨(바닥 + 벽)으로 변환한다.
3. **수동 보정** — 사용자가 벽 길이·높이·코너를 수정할 수 있다.
4. **저장 / 불러오기** — 방 기하와 가구 배치를 로컬에 저장하고 다시 연다.
5. **기본 가구 배치** — 소수의 프리셋 가구를 방 안에 옮기고 회전한다.

## Explicitly Out of Scope (MVP)

- 자동 인테리어 추천 / VLM·LLM 기반 정밀 치수
- 클라우드 동기화, 멀티유저, 계정
- 실측 수준(cm급) 보증
- 복잡한 BIM / CAD 내보내기
- `native-ai` Kotlin 온디바이스 AI 기능의 Unity 내장 구현
  (별도 모듈이며 Codex 담당; MVP 핵심 경로에 필수가 아님)

## Success Criteria

- 한 개 방에서 스캔 → 보정 → 저장 → 재실행 후 불러오기 → 가구 배치까지 가능한 데모
- 측정값은 추정임을 UI/문서에 명시
- Editor에서 mock 데이터로 방 생성·저장 경로를 재현 가능

## Ownership Split

| Area | Owner |
|------|-------|
| Unity AR / room / furniture | Cursor |
| Kotlin LiteRT-LM / Gemma AI | Codex (`native-ai/`) |
