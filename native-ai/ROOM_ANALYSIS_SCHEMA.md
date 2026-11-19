# Room analysis JSON 1.0

실내 사진 한 장에서 얻은 시각적 관찰 결과다. 아래는 형식 예시다.

```json
{
  "schemaVersion": "1.0",
  "sceneType": "bedroom",
  "objects": [
    {"type": "bed", "attributes": ["white bedding"]},
    {"type": "floor", "attributes": ["wood pattern"]}
  ],
  "openings": [
    {"type": "window", "attributes": ["partially visible"]}
  ],
  "uncertainObservations": ["An object is partially occluded near the bed."]
}
```

| 필드 | 형식 및 값 |
| --- | --- |
| `schemaVersion` | 문자열 `"1.0"` |
| `sceneType` | `bedroom`, `living_room`, `workspace`, `kitchen`, `other`, `unknown` 중 하나 |
| `objects` | 최대 14개. 유형은 `wall`, `floor`, `ceiling`, `desk`, `chair`, `bed`, `sofa`, `table`, `cabinet`, `light`, `monitor`, `other` 중 하나 |
| `openings` | 최대 6개. 유형은 `door` 또는 `window` |
| `uncertainObservations` | 최대 6개의 짧은 문자열. 가림 등 모호한 부분을 서술 |

다섯 필드는 모두 필수다. `objects`와 `openings`의 각 항목에는 문자열 `type`과 문자열 배열 `attributes`가 필요하다. 속성은 최대 4개이며 색상, 외관, 질감, 가림을 짧게 표현한다. `other`는 목록 밖의 주요 물체에 사용하고 속성에 물체의 이름을 넣도록 프롬프트에서 지시한다. 빈 배열은 허용한다. 배열 속 문자열은 공백뿐인 값 없이 최대 120 UTF-16 코드 단위여야 한다. 열거형은 대소문자를 구분한다.

## 응답 처리

실내 분석 전용 대화는 Kotlin 계약에서 생성한 JSON Schema를 LiteRT-LM의 `ResponseFormat.json`에 전달한다. 입력 사진과 프롬프트는 기존 CPU 비전 경로를 사용한다. [LiteRT-LM 0.19.0 ResponseFormat API](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/kotlin/java/com/google/ai/edge/litertlm/ResponseFormat.kt)

- 원본 응답에서 앞뒤 공백을 파싱용으로 제거한다. 전체 응답을 감싼 단일 Markdown 코드 블록은 제거할 수 있다. 언어 태그는 비어 있거나 `json`이어야 한다.
- JSON 객체 하나를 엄격 문법으로 읽는다. 앞뒤 설명, 여러 JSON 값, 잘린 JSON, 누락된 필드, 틀린 자료형, 지원하지 않는 버전·유형, 범위를 벗어난 값, 추가 키를 거부한다. 원본 응답 상한은 16,000 UTF-16 코드 단위다.
- 실패 시 원본 응답과 오류 범주·필드 경로를 보여준다. 성공하면 검증한 Kotlin `RoomAnalysis`를 별도 JSON으로 직렬화해 표시한다. 원본 텍스트는 `GemmaInference`에 남는다.

## Unity 연동 시 계약

검증된 JSON만 소비하고 `schemaVersion`을 확인한다. 1.0의 열거형 및 필드 계약을 바꿀 때는 새 버전과 소비자 호환 정책을 함께 정한다. 인식 결과는 사진 기반 후보로 취급하고 사용자 확인을 거친다. 이 계약은 미터값, 위치, 경계 상자, 3D 좌표를 정의하지 않는다. 기하 데이터와 객체 배치는 Unity의 독립 측정·보정 경로가 담당한다. 형식 검증을 통과한 응답에도 인식 오류나 누락이 있을 수 있다.
