# 실내 사진 분석 검증

2026-11-20, Galaxy S20+ (SM-G986N, Android 13/API 33, arm64-v8a), 디버그 APK, LiteRT-LM Android 0.19.0 CPU 텍스트·비전 백엔드에서 확인했다. Gemma 4 E2B IT 모델 리비전은 `6e5c4f1`이며 기기 파일의 SHA-256은 `181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c`였다. 모델은 기존 설치 파일을 사용했다.

## 사용한 사진

세 사진은 Wikimedia Commons에서 CC0로 공개됐다. 원본 파일은 기기의 시스템 이미지 선택기로 입력했다.

- 침실 · GregoryButler. [Bedroom-TT.jpg](https://commons.wikimedia.org/wiki/File:Bedroom-TT.jpg)
- 거실 · Jarosław Ceborski. [Living room (Unsplash).jpg](https://commons.wikimedia.org/wiki/File:Living_room_(Unsplash).jpg)
- 주방 · C.M.B. 흐림과 가림이 있는 사진이다. [Kitchen area with open door and a wall out of focus in front of it.jpg](https://commons.wikimedia.org/wiki/File:Kitchen_area_with_open_door_and_a_wall_out_of_focus_in_front_of_it.jpg)

## 결과

| 입력 | 파싱·스키마 | 확인한 인식 | 관찰한 누락·오인식 |
| --- | --- | --- | --- |
| 침실 첫 요청 | 성공 | 침대, 협탁, 창문, 바닥, 벽, 천장, 램프 | 안락의자를 소파로, 천장 팬을 조명으로 분류 |
| 거실 | 성공 | 파란 소파, 흰 의자, 수납장, 플로어 램프, 창문 | TV를 모니터로 분류, 창문을 중복 기록, 사진에서 확인되지 않는 러그를 기재 |
| 주방 | 성공 | 상·하부 수납장, 노란빛 표면 | 열린 문을 누락, 수납장 중복, 보이는 광원으로 단정하고 불확실성을 기록하지 않음 |
| 침실 반복 | 성공 | 첫 요청과 원본 응답이 일치 | 첫 요청과 같은 오분류 |

출력 제약을 적용하기 전에는 침실 응답이 JSON 객체 세 개로 분리됐고, 거실과 주방에서는 허용하지 않는 유형이 나왔다. 그 빌드에서 앱이 원본과 문법 또는 스키마 오류를 함께 표시하는 것을 확인했다. 표와 아래 원본은 출력 제약을 적용한 최종 빌드의 결과다.

## 회귀 및 오류 경로

최종 APK에서 사진 미선택 안내, 시스템 선택기 취소, 이미지 첨부 제거, 손상된 JPEG 디코딩 실패 안내를 확인했다. 화면의 **검증된 JSON**에서 침실 결과의 버전과 장면 값을 확인했다. 기존 자유 질문은 `apple`을 반환했고, 거실 사진을 첨부한 자유 질문은 소파 색을 `Blue`로 반환했다.

호스트에서 JSON 파서 단위 테스트 8개, 디버그 APK 빌드, Android Lint가 통과했다. Lint는 오류 0건, 경고 7건이었다. 앱이 기존 LiteRT-LM의 Gson 의존성을 명시하면서 Gson 업데이트 경고 1건이 추가됐다.

## 계측

앱의 단조 시계로 이미지 디코딩·크기 조정·JPEG 인코딩, 엔진 초기화, 대화 생성·추론·종료, 작업 스레드 전체 처리 시간을 계측했다. 전체에는 JSON 제약 객체 준비, 파싱·검증·직렬화가 포함되며 이미지 선택과 화면 갱신은 제외된다. PSS는 각 응답 표시 직후 `adb shell dumpsys meminfo com.roomtwin.nativeai`로 읽은 앱 프로세스 스냅샷이다. 최댓값 측정용 샘플링은 수행하지 않았다.

| 입력 순서 | 모델 초기화 (ms) | 이미지 전처리 (ms) | 추론 (ms) | 전체 (ms) | 완료 직후 PSS (KiB) |
| --- | ---: | ---: | ---: | ---: | ---: |
| 침실 첫 요청 | 538 | 21 | 20,650 | 21,219 | 2,290,359 |
| 거실 | 엔진 재사용 | 86 | 23,971 | 24,062 | 2,381,339 |
| 주방 | 엔진 재사용 | 63 | 17,412 | 17,478 | 2,381,127 |
| 침실 반복 | 엔진 재사용 | 33 | 20,875 | 20,915 | 2,382,579 |

초기 대기 PSS는 72,434 KiB였다. 기존 앱 캐시가 남은 상태에서 앱을 재시작해 첫 모델 로드를 계측했다. 동일 프로세스에서 네 요청을 연속 완료했다. 발열, 장시간 반복 실행, 다른 기기에서의 성능, 실제 메모리 부족과 손상된 모델의 복구는 미검증이다.

## 모델 원본 응답

### 침실 첫 요청

```json
{"schemaVersion":"1.0","sceneType":"bedroom","objects":[{"type":"bed","attributes":["wooden frame","quilted bedding"]},{"type":"sofa","attributes":["striped fabric","light-colored"]},{"type":"table","attributes":["wooden side table","dark wood"]},{"type":"table","attributes":["ornate side table","dark wood"]},{"type":"ceiling","attributes":["light green paint"]},{"type":"floor","attributes":["wooden planks"]},{"type":"wall","attributes":["light green paint"]},{"type":"light","attributes":["table lamp","warm light"]},{"type":"light","attributes":["ceiling fan","blades"]},{"type":"other","attributes":["large area rug","blue and gold pattern"]}],"openings":[{"type":"window","attributes":["multiple panes","white blinds"]}],"uncertainObservations":[]}
```

### 거실

```json
{"schemaVersion":"1.0","sceneType":"living_room","objects":[{"type":"sofa","attributes":["blue","velvet"]},{"type":"chair","attributes":["white","tufted","metal base"]},{"type":"cabinet","attributes":["white","low storage"]},{"type":"floor","attributes":["wood","light brown"]},{"type":"wall","attributes":["white","patterned wallpaper"]},{"type":"light","attributes":["white","floor lamp"]},{"type":"monitor","attributes":["black","flat screen"]},{"type":"other","attributes":["window","white frame"]},{"type":"other","attributes":["radiator","white","under window"]},{"type":"other","attributes":["plants","green","potted"]},{"type":"other","attributes":["curtain","white","sheer"]},{"type":"other","attributes":["rug","light brown","under sofa"]},{"type":"other","attributes":["window frame","white","rectangular"]},{"type":"other","attributes":["window sill","white","horizontal"]}],"openings":[{"type":"window","attributes":["large","multiple panes","natural light"]}],"uncertainObservations":[]}
```

### 주방

```json
{"schemaVersion":"1.0","sceneType":"kitchen","objects":[{"type":"cabinet","attributes":["dark gray upper storage"]},{"type":"cabinet","attributes":["dark blue lower storage"]},{"type":"wall","attributes":["light yellow wall surface"]},{"type":"cabinet","attributes":["white built-in oven/cabinet"]},{"type":"light","attributes":["bright yellow light source"]},{"type":"cabinet","attributes":["dark gray upper storage"]},{"type":"cabinet","attributes":["dark blue lower storage"]}],"openings":[],"uncertainObservations":[]}
```

### 침실 반복

```json
{"schemaVersion":"1.0","sceneType":"bedroom","objects":[{"type":"bed","attributes":["wooden frame","quilted bedding"]},{"type":"sofa","attributes":["striped fabric","light-colored"]},{"type":"table","attributes":["wooden side table","dark wood"]},{"type":"table","attributes":["ornate side table","dark wood"]},{"type":"ceiling","attributes":["light green paint"]},{"type":"floor","attributes":["wooden planks"]},{"type":"wall","attributes":["light green paint"]},{"type":"light","attributes":["table lamp","warm light"]},{"type":"light","attributes":["ceiling fan","blades"]},{"type":"other","attributes":["large area rug","blue and gold pattern"]}],"openings":[{"type":"window","attributes":["multiple panes","white blinds"]}],"uncertainObservations":[]}
```
