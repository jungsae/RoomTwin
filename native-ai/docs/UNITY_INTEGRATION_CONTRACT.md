# RoomTwin Unity–Native AI 통합 계약 초안

통합 계약 버전은 `1.0`이다. 이 문서는 호출부와 응답 형식을 정의하는 초안이다. 현재 APK에는 Android 화면 경로, `GemmaRunner`, 분석 결과 모델과 검증기가 있다. Unity에 노출되는 호출 API와 전송 구현은 후속 단계에서 정한다. 분석 모듈은 사진에서 보이는 객체 후보와 불확실성을 반환한다. AR 좌표, 미터 단위 치수, 면적, 3D 배치는 Unity가 별도로 측정·관리한다.

## 이미지 입력

- 요청당 정지 사진 한 장을 받는다. 초기 통합 형식은 JPEG와 PNG이며, 사진과 MIME 유형이 일치하고 Android 디코더로 읽을 수 있어야 한다.
- `content://` URI를 초기 전송 후보로 사용한다. Unity는 앱 전용 임시 저장소에 사진을 쓰고, Kotlin이 읽을 수 있는 URI와 필요한 읽기 권한을 제공한다. 요청이 끝날 때까지 원본·권한을 유지하고, 완료 또는 취소가 확정되면 자신이 만든 파일과 권한을 정리한다.
- Kotlin은 URI와 권한, 디코딩 가능 여부를 확인한다. 현재 전처리는 종횡비를 유지해 긴 변을 최대 768px로 줄이고 JPEG로 인코딩한다. 전송 바이트와 디코딩 픽셀의 상한은 실제 Unity 캡처와 메모리 검증을 거쳐 확정한다.
- 사진, URI, 프롬프트, 원시 모델 응답은 로그로 남기거나 저장 데이터에 포함하지 않는다. 원시 모델 응답을 봐야 하는 경우 기존 독립 앱의 디버그 화면을 사용한다.

## 분석 요청과 결과

양쪽은 JSON 객체 하나를 주고받는다. `contractVersion`은 전송 계약 버전이고 `result.schemaVersion`은 분석 데이터 버전이다. `requestId`는 호출자가 만든 세션 내 고유 문자열이며, 처리나 정리가 끝나기 전에는 재사용하지 않는다. 문서의 JSON 값은 형식 예시다.

```json
{
  "contractVersion": "1.0",
  "requestId": "room-photo-001",
  "operation": "analyzeRoom",
  "image": {
    "uri": "content://com.roomtwin.capture/room-001.jpg",
    "mimeType": "image/jpeg"
  }
}
```

성공 응답에는 스키마 검증을 통과한 `result` 객체가 포함된다. `objects`와 `openings`는 각각 `type` 문자열과 `attributes` 문자열 배열을 가진 시각적 관찰 목록이다. 빈 배열은 허용한다. 목록은 가시 범위에서 모델이 보고한 후보이며, 배열 인덱스는 사진 간에 유지되는 객체 식별자가 아니다. 불확실한 부분은 `uncertainObservations`에 남긴다. 열거형, 필수 필드, 상한과 검증 정책은 [분석 JSON 스키마 1.0](../ROOM_ANALYSIS_SCHEMA.md)을 따른다.

```json
{
  "contractVersion": "1.0",
  "requestId": "room-photo-001",
  "status": "ok",
  "result": {
    "schemaVersion": "1.0",
    "sceneType": "bedroom",
    "objects": [{"type": "bed", "attributes": ["white bedding"]}],
    "openings": [{"type": "window", "attributes": ["partially visible"]}],
    "uncertainObservations": []
  }
}
```

`GemmaInference`는 Kotlin에서 원본 모델 응답과 계측을 보관한다. `RoomAnalysisJson`으로 검증한 `RoomAnalysis`만 성공 응답의 `result`로 전달한다. Unity는 두 버전을 확인하고, 수신한 관찰을 사용자 확인 전까지 별도 후보 데이터로 취급한다.

## 오류 응답

`status`는 `error`이며 `result`는 생략한다. `code`는 Unity에서 분기하는 값이다. `fieldPath`는 해당할 때만 포함하고, `requestId`를 읽을 수 없는 요청에서는 `null`을 반환한다. Unity는 오류 코드로 안내 문구를 매핑한다. 예외 스택과 디버그 원문은 전송하지 않는다.

```json
{
  "contractVersion": "1.0",
  "requestId": "room-photo-001",
  "status": "error",
  "error": {
    "code": "MODEL_OUTPUT_INVALID",
    "fieldPath": "$.openings[0].type"
  }
}
```

| 코드 | 조건 |
| --- | --- |
| `INVALID_REQUEST`, `UNSUPPORTED_VERSION` | 요청 필드·형식 또는 전송 버전 오류 |
| `BUSY`, `SESSION_CLOSED`, `CANCELLED` | 실행 중, 세션 종료 또는 취소 확정 |
| `IMAGE_UNREADABLE` | 읽기 권한, MIME 또는 디코딩 오류 |
| `MODEL_MISSING`, `MODEL_INIT_FAILED` | 모델 설치 또는 엔진 초기화 실패 |
| `INFERENCE_FAILED`, `RESOURCE_EXHAUSTED` | 추론 오류 또는 감지 가능한 자원 부족 |
| `MODEL_OUTPUT_INVALID` | 빈 답변, JSON 문법 또는 분석 스키마 검증 실패 |

## 비동기 처리와 취소

- Unity 호출은 즉시 반환한다. Kotlin은 전용 작업 스레드에서 요청 하나를 실행하고 추가 요청에는 `BUSY`를 반환한다. 호출 연결이 살아 있고 앱 프로세스가 유지되면, 접수 또는 거부된 요청마다 완료 응답을 하나만 보낸다. Unity는 결과를 메인 스레드로 전달하고 현재 요청과 일치하는지 확인한 후 화면을 갱신한다.
- 취소는 `requestId`로 요청한다. 시작 전이면 추론을 생략하고 `CANCELLED`를 반환한다. 실행 중에는 취소를 기록하고, 동기 네이티브 호출이 끝난 뒤 결과를 폐기하고 `CANCELLED`를 반환한다. 그 전까지 작업 슬롯과 입력은 유지한다. 완료 상태가 먼저 확정된 경우 기존 완료 응답을 유지한다.
- 화면이나 Unity 소유자가 먼저 해제되면 콜백을 분리한다. Kotlin은 실행이 끝날 때 정리를 마치고, Unity는 해제된 화면의 응답을 적용하지 않는다. Unity의 화면 대기 시간 제한은 취소 요청으로 전달한다. 네이티브 호출 중단이나 프로세스 종료 시 응답 전달은 보장 범위에서 제외한다.

## 수명주기

Unity 호스트는 AI 기능 사용 시점에 세션을 열고, 사용 종료 시 해제를 요청한다. 모델 위치와 공급 방식은 호스트와 통합 단계에서 합의한다. Kotlin은 모델 파일 확인, 첫 분석의 지연 초기화, 준비된 `Engine` 재사용을 담당한다. 요청마다 새 `Conversation`을 열고 완료·실패 후 닫는다. 해제 요청 이후 신규 작업을 거절하고, 진행 중인 네이티브 호출과 대화 종료가 끝나면 동일 작업 스레드에서 엔진을 닫는다.

## Android 전달 방식 후보

| 후보 | 장점 | 고려할 점 |
| --- | --- | --- |
| 동일 프로세스 Kotlin 호출과 콜백, 이미지 URI | 기존 디코더 재사용, 큰 사진 바이트가 문자열 호출을 통과하지 않음 | Android 플러그인 패키징, URI 수명, 콜백 스레드, Unity와 AI의 메모리 공존을 검증해야 함 |
| 동일 프로세스 바이트 배열과 콜백 | 이미지 임시 파일 없이 한 번의 호출로 전달 가능 | C#·Java·네이티브 사이 복사와 메모리 피크를 계측해야 함. Android IPC로 확장할 경우 큰 배열 대신 URI 사용 |
| 별도 Android 프로세스나 Activity, URI와 작은 JSON 반환 | 장애 영향 범위 분리, 독립 화면으로 시험하기 쉬움 | 권한과 앱 수명주기 처리, 콜백 재연결, 프로세스별 메모리와 시작 비용을 검증해야 함 |

관련 플랫폼 문서 · [Android 파일 공유](https://developer.android.com/training/secure-file-sharing/share-file), [Unity AndroidJavaProxy](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/androidjavaproxy), [Android IPC 데이터 크기 제한](https://developer.android.com/reference/android/os/TransactionTooLargeException)

첫 통합 검증에서는 Unity의 실제 캡처 이미지를 사용해 URI 접근·정리, 중복 호출, 화면 전환과 취소, AR 실행 중 메모리를 확인한다. Unity의 방 생성 흐름은 AI 결과가 비어 있거나 실패해도 계속 동작한다.
