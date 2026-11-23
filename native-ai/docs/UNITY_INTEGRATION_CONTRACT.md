# RoomTwin Unity–Native AI 통합 계약 1.0

`:bridge`는 Gemma 4 E2B의 Kotlin 추론기, 이미지 전처리, [분석 JSON 스키마 1.0](../ROOM_ANALYSIS_SCHEMA.md), Unity 호출 API를 포함한다. 독립 앱과 두 field 계측기는 같은 추론기를 사용한다. 앱과 브리지 모두 평가에서 사용한 기존 프롬프트를 기본으로 한다. 분석 결과는 사진 속 가시 객체 후보다. AR 좌표, 미터 단위 치수, 면적과 배치는 Unity에서 별도로 측정·관리한다.

## AAR 생성과 Unity Android 설정

`native-ai/`에서 실행한다.

```sh
gradle :bridge:assembleRelease :bridge:testDebugUnitTest :bridge:lintRelease :app:testDebugUnitTest :app:assembleDebug :app:assembleField
gradle :bridge:publishReleasePublicationToUnityRepository
gradle -PusePublishedBridge=true :app:testDebugUnitTest :app:assembleDebug :app:assembleField :app:lintDebug :app:lintField
```

- 독립 AAR은 `bridge/build/outputs/aar/bridge-release.aar`에 생성된다. 로컬 Maven 저장소는 `bridge/build/unity-repository`에 AAR·POM·Gradle 메타데이터를 생성한다. 소비 좌표는 `com.roomtwin:native-ai:0.1.0`이다. 발행은 이 로컬 폴더에 기록한다.
- Unity 6의 Android Player Publishing Settings에서 Custom Gradle Settings Template과 Custom Main Gradle Template을 활성화한다. `settingsTemplate.gradle`의 `dependencyResolutionManagement.repositories`에 위 Maven 저장소의 절대 경로를 추가한다. 예를 들어 Gradle 속성 `roomTwinAiRepository`를 준비하고 다음을 추가한다. `google()`, `mavenCentral()`와 기존 플레이스홀더를 보존한다. [Unity Gradle 템플릿](https://docs.unity3d.com/6000.0/Documentation/Manual/gradle-templates.html)

  ```groovy
  maven {
      url = uri(providers.gradleProperty("roomTwinAiRepository").get())
      content { includeModule("com.roomtwin", "native-ai") }
  }
  ```

- `mainTemplate.gradle`의 `dependencies`에 `implementation 'com.roomtwin:native-ai:0.1.0'`를 추가한다. Unity Android의 ARM64를 활성화하고 컴파일 SDK 36 및 Java 17 호환 도구를 사용한다. 라이브러리의 최소 SDK는 28이다. R8을 사용하는 빌드는 AAR에 포함된 브리지 메서드, 분석 DTO 필드, LiteRT-LM JNI 유지 규칙을 소비한다. 생성된 Gradle 프로젝트와 병합 매니페스트를 확인한다.
- 파일로 가져오는 경로를 택하면 `bridge-release.aar`를 Android 전용 Plugin으로 두고 Kotlin stdlib `2.4.0`, Gson `2.14.0`, `com.google.ai.edge.litertlm:litertlm-android:0.19.0`을 Unity Gradle 의존성에 직접 추가한다. LiteRT-LM이 가져오는 Kotlin reflect와 coroutines도 Gradle로 해결한다. Maven 좌표 경로와 AAR 직접 가져오기는 한 가지를 선택한다. [Unity AAR 플러그인](https://docs.unity3d.com/6000.0/Documentation/Manual/AndroidAARPlugins.html) · [Android Library 의존성](https://developer.android.com/studio/projects/android-library)
- Google Maven과 Maven Central에 접근하는 단계는 Gradle 의존성 준비다. 런타임은 앱 저장소에 모델을 미리 설치하고 로컬 파일 또는 로컬 콘텐츠 제공자에서 사진을 읽을 때 오프라인으로 동작한다. 브리지 매니페스트는 인터넷 권한을 추가하지 않는다. GPU 용 선택적 `libOpenCL.so`와 `libvndksupport.so` 선언은 AAR에서 병합한다.

## Java API 및 Unity 호출

Unity Android에서 `com.roomtwin.nativeai.bridge.RoomTwinAiBridge.getInstance(Context)`를 호출한다. 같은 프로세스의 Unity 소유자들은 한 세션을 공유한다. Kotlin은 application context만 보관한다. 예시에서 모델은 Unity 앱의 `Application.persistentDataPath`에 미리 배치돼 있다.

```csharp
using System.IO;
using UnityEngine;

// Android Player 코드에서 호출한다.
using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
using var api = new AndroidJavaClass("com.roomtwin.nativeai.bridge.RoomTwinAiBridge");
AndroidJavaObject bridge = api.CallStatic<AndroidJavaObject>("getInstance", activity);
var modelPath = Path.Combine(Application.persistentDataPath, "gemma-4-E2B-it.litertlm");
string initAck = bridge.Call<string>("initialize", modelPath, "CPU");
// Unity Update 또는 코루틴에서 getStatus를 조회해 READY를 확인한 뒤 요청을 제출한다.
string snapshot = bridge.Call<string>("getStatus");
```

모든 인스턴스 메서드는 JSON 문자열을 즉시 반환한다. JNI 호출에서 수행하는 작업은 짧은 검증, 상태 조회와 큐 등록이다. 파일 접근, 디코딩, 엔진 초기화, 추론과 해제는 전용 작업 스레드에서 수행한다. Unity는 일정 간격으로 상태를 조회하고 게임 UI는 Unity 메인 스레드에서 갱신한다. 콜백 등록은 필요 없다. [Unity AndroidJavaObject](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AndroidJavaObject.html)

| API | 호출과 관측 |
| --- | --- |
| `initialize(modelPath, backend)` | 절대 경로의 로컬 모델과 `CPU` 또는 `GPU`를 지정한다. `accepted` 이후 `READY` 또는 `FAILED`를 조회한다. 같은 설정의 중복 초기화는 요청을 추가하지 않는다. 준비된 엔진의 설정을 바꾸려면 해제하고 새 세션을 얻는다. 초기화 실패 후 엔진이 정리됐으면 같은 세션에서 명시적으로 CPU를 다시 요청할 수 있다. |
| `analyzeImageAsync(requestJson)` | `READY`인 엔진을 재사용해 단일 사진 요청을 접수한다. 중복·동시 실행은 `BUSY`, 완료 응답 미소비 상태는 `RESULT_PENDING`을 반환한다. |
| `getStatus()` | `state`, `requestedBackend`, `activeBackend`, `activeRequestId`, `resultRequestId`, `cancelRequested`, `nativeCancellation`, `lastError`를 반환한다. `activeBackend`는 초기화에 성공한 런타임 설정이다. GPU 초기화에 실패하면 오류가 표시되고 엔진 백엔드는 비어 있다. 자동 CPU 폴백은 없다. |
| `takeResult(requestId)` | 완료 JSON을 한 번 꺼낸다. 아직 실행 중이면 `pending`이다. 상태의 `resultRequestId`를 확인한 뒤 해당 ID를 넘긴다. |
| `cancel(requestId)` | 큐에서 시작 전이면 `SKIP_IF_QUEUED`, 실행 중이면 `DISCARD_ON_COMPLETION`을 반환한다. 응답 상태 `accepted`는 취소 처리 접수다. |
| `release()` | 신규 작업을 닫고 현재 대화가 끝나기를 기다린 뒤 엔진을 해제하도록 등록한다. 미소비 결과는 폐기한다. 엔진 해제가 확인된 상태는 `RELEASED`다. |

상태는 `NEW → INITIALIZING → READY ↔ ANALYZING`과 `FAILED`, `RELEASING → RELEASED`, `RELEASE_FAILED`가 있다. 작업 하나마다 새 대화를 생성해 닫고 엔진은 재사용한다. 모델 로딩 오류는 `lastError`에서 확인한다. 프로세스가 메모리 오류를 포착할 수 있으면 `RESOURCE_EXHAUSTED`를 반환하고 엔진을 닫는다. Android가 앱 프로세스를 종료하는 경우 결과 보존은 보장되지 않는다.

### 이미지와 결과

`requestId`는 1~80자의 영문, 숫자, 점, 밑줄, 하이픈으로 구성한다. 한 세션에서 새 분석마다 고유 값을 사용한다. 요청은 최대 8,192자다. 이미지 한 장의 형식은 JPEG 또는 PNG다. 앱 전용 내부·외부 files/cache 디렉터리에 있는 파일은 `file:///` URI로 넘길 수 있다. `content://` URI는 Android에서 열 수 있는 제공자를 사용하고 읽기 권한을 유지한다. 이미지 디코더가 파일의 실제 MIME을 요청과 대조한다. 긴 변을 최대 768px로 축소해 JPEG로 인코딩한다. 앱 전용 경로의 모델도 파일로 공급하며 AAR에 포함되지 않는다.

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

Unity에서는 요청 본문을 `bridge.Call<string>("analyzeImageAsync", requestJson)`으로 넘긴다. `resultRequestId`가 해당 ID이면 `bridge.Call<string>("takeResult", requestId)`로 결과를 꺼낸다. Unity는 `contractVersion`과 `result.schemaVersion`을 각각 검사한다. 결과의 배열 인덱스는 지속 객체 ID로 사용할 수 없다. 브리지는 스키마를 통과한 데이터와 오류 코드만 반환한다. 원시 응답과 사진 경로를 전송 결과에 저장하거나 로그에 기록하지 않는다.

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

```json
{
  "contractVersion": "1.0",
  "requestId": "room-photo-001",
  "status": "error",
  "error": {"code": "MODEL_OUTPUT_INVALID", "fieldPath": "$.openings[0].type"}
}
```

| 오류 코드 | 조건 |
| --- | --- |
| `INVALID_REQUEST`, `UNSUPPORTED_VERSION` | 요청 형식, URI·MIME, 버전 또는 백엔드 인자 오류 |
| `BUSY`, `ALREADY_INITIALIZED`, `NOT_READY`, `SESSION_CLOSED` | 요청·초기화 실행 중, 설정 변경, 미준비 또는 종료 세션 |
| `RESULT_PENDING`, `RESULT_NOT_FOUND`, `REQUEST_NOT_ACTIVE` | 완료 응답 미소비, 해당 결과 또는 취소 대상 없음 |
| `IMAGE_UNREADABLE`, `MODEL_MISSING`, `MODEL_INIT_FAILED` | 이미지 권한·파일·디코딩 실패, 모델 접근 또는 백엔드 초기화 실패 |
| `MODEL_OUTPUT_INVALID`, `INFERENCE_FAILED`, `RESOURCE_EXHAUSTED` | 출력 검증, 추론 또는 감지 가능한 메모리 오류 |
| `CANCELLED`, `RESOURCE_RELEASE_FAILED` | 취소가 확정됐거나 엔진 종료가 실패함. 해제 실패 시 `release`를 다시 호출할 수 있다. |

### 취소와 호스트 수명주기

브리지는 기존 동기 LiteRT-LM 호출을 사용한다. LiteRT-LM 0.19.0의 대화에는 `cancelProcess()`가 있으며, 이 브리지는 해당 API를 호출하지 않는다. 따라서 `nativeCancellation`은 `false`다. 시작 전 취소가 처리되면 추론을 생략한다. 실행 중에는 네이티브 호출이 돌아올 때까지 작업 슬롯과 이미지 권한을 유지한 뒤 `CANCELLED`를 게시한다. 완료가 먼저 확정됐으면 기존 응답을 소비한다. [LiteRT-LM 0.19.0 Conversation](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/kotlin/java/com/google/ai/edge/litertlm/Conversation.kt)

Unity에서 소유자 한 곳이 세션과 이미지 파일을 관리한다. 화면이 사라지면 UI 갱신을 중단하고, 세션이 계속 필요한 경우 해당 요청을 취소한다. AI 기능 자체를 종료할 때는 `release`를 호출하고 `RELEASED` 이후 소유한 파일과 URI 권한을 정리한다. 결과를 소비했다면 해당 이미지도 바로 정리할 수 있다. Java 프록시의 `Dispose()`는 JNI 래퍼 해제다. 엔진의 종료는 `release`가 담당한다. 새 세션은 해제 완료 뒤 `getInstance`로 얻는다.

## 호스트 검증과 다음 Unity 검증

JVM 테스트 20개가 통과했다. 기존 스키마 파서 8개, 요청 포맷 3개와 가짜 엔진을 이용한 상태·오류 테스트 9개다. 프로젝트 연결과 로컬 Maven AAR 소비 양쪽으로 디버그·field APK를 만들었다. 후자의 디버그 APK에서 브리지만 선언해 LiteRT-LM 전이 의존성이 `lib/arm64-v8a/liblitertlm_jni.so`로 포함된 것을 확인했다. 디버그·field APK의 ELF 아키텍처는 AArch64였고, field의 두 계측기 등록과 OpenCL 선언도 확인했다. 세 아카이브에 모델 파일은 없다. Library Release Lint는 이슈 0건, 두 앱 변형은 각각 오류 0건과 경고 5건이었다.

실제 Unity의 Android export, IL2CPP와 R8 런타임, 사진 캡처 URI 전달, 새 AAR의 실기기 CPU/GPU와 차단된 네트워크에서의 추론은 미검증이다. 첫 Unity 통합에서는 CPU로 모델 파일 경로와 JNI 호출을 확인한 뒤 GPU 초기화 실패와 명시적 CPU 재시도, 화면 이동 중 취소, AR 동시 메모리·프레임 시간과 장시간 발열을 확인한다.
