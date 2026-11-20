# RoomTwin Native AI Module

Gemma 4 E2B IT의 온디바이스 추론을 확인하는 독립 Android 앱이다. 기본 경로는 CPU이며, 요청마다 텍스트와 선택한 이미지 한 장을 받아 새 대화에서 응답을 생성한다.

## 환경 구성

- JDK 17, Gradle 9.3.1, Android SDK Platform 36, Build Tools 36.0.0 및 Platform Tools를 설치한다. `JAVA_HOME`을 JDK 경로로, `ANDROID_HOME`을 Android SDK 경로로 설정하고 `gradle`과 `adb`를 PATH에 추가한다.
- Android Studio에서는 `native-ai/`를 Gradle 프로젝트로 연다. 이 프로젝트는 AGP 9.1.1, Kotlin 2.4.0, LiteRT-LM Android 0.19.0을 사용한다. 초기 의존성 다운로드에는 Google Maven과 Maven Central 접근이 필요하다.
- 앱의 설치 대상은 Android 9(API 28) 이상인 `arm64-v8a` 기기다. 모델과 캐시를 위한 여유 저장 공간이 필요하다. 아래에서 사용하는 모델 파일은 2.59 GB다.
- 실기기 검증에 사용한 `gemma-4-E2B-it.litertlm` 파일을 PC에 별도로 내려받는다. SHA-256은 `181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c`이다. [LiteRT Community 모델 파일 리비전 6e5c4f1](https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm/blob/6e5c4f1/gemma-4-E2B-it.litertlm)

## 빌드 및 설치

저장소 루트에서 실행한다.

```sh
cd native-ai
gradle :app:assembleDebug
adb devices
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

USB 디버깅을 허용한 기기 한 대를 연결한다. 여러 기기가 연결되어 있으면 각 `adb` 명령에 `-s <serial>`을 추가한다.

## 모델 배치 및 실행

아래에서 `<local-model-path>`는 PC의 모델 파일 경로로 바꾼다. 앱을 종료한 상태에서 모델을 교체한다. 전송 중인 파일은 임시 이름으로 저장하고, `adb push` 성공을 확인한 뒤 마지막 명령을 실행한다.

```sh
adb shell am force-stop com.roomtwin.nativeai
adb shell mkdir -p /sdcard/Android/data/com.roomtwin.nativeai/files
adb push "<local-model-path>" /sdcard/Android/data/com.roomtwin.nativeai/files/gemma-4-E2B-it.litertlm.tmp
adb shell mv /sdcard/Android/data/com.roomtwin.nativeai/files/gemma-4-E2B-it.litertlm.tmp /sdcard/Android/data/com.roomtwin.nativeai/files/gemma-4-E2B-it.litertlm
adb shell am start -n com.roomtwin.nativeai/.MainActivity
```

앱에서 짧은 질문을 입력하고 **추론 실행**을 누른다. 첫 요청에 모델을 로드한다. 이미지 추론은 **이미지 선택**에서 사진 한 장을 고른 뒤 질문을 입력하고 실행한다. 앱이 선택한 파일을 기기에서 읽을 수 있어야 한다.

## 실내 사진 분석

1. 실행할 백엔드를 선택한다. 첫 실행의 기본값은 CPU다. GPU를 선택하면 다음 요청이 GPU 엔진을 초기화한다. 이전 엔진은 작업 스레드에서 닫히며, 초기화 실패 시 화면에서 원인을 확인하고 CPU를 직접 선택할 수 있다.
2. **이미지 선택**에서 기기에 저장된 실내 사진 한 장을 고른다.
3. **실내 사진 분석 · JSON**을 누른다. 앱이 실내 분석용 프롬프트를 자동으로 사용한다.
4. 결과에서 초기화·이미지 전처리·추론·전체 소요 시간, 원본 모델 응답, JSON 검증 상태를 확인한다. 검증에 성공하면 **검증된 JSON**이 원본 아래에 표시된다.
5. 자유 질문은 기존 **추론 실행**을 사용한다. 이미지를 제거하면 텍스트만 입력할 수 있다.

필드와 오류 처리 기준은 [JSON 스키마 1.0](ROOM_ANALYSIS_SCHEMA.md)을 참조한다.

## 호스트 빌드, 단위 테스트 및 정적 검사

```sh
cd native-ai
gradle :app:testDebugUnitTest :bridge:testDebugUnitTest :app:assembleDebug :app:lintDebug
```

이 명령은 JSON 호스트 단위 테스트, APK 빌드, Android Lint를 실행한다. 실기기 추론은 아래 절차로 확인한다.

## 공식 문서에서 확인한 사항

- LiteRT-LM의 Android Maven AAR은 Kotlin API를 제공한다. `Engine.initialize()`는 백그라운드에서 호출하고 사용이 끝난 `Conversation`과 `Engine`은 닫는다. 이미지 입력에는 비전 백엔드를 구성한다. [LiteRT-LM 0.19.0 Kotlin 가이드](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/docs/api/kotlin/getting_started.md)
- Gemma 4 E2B IT는 LiteRT-LM용 `.litertlm` 파일로 배포된다. 모델 카드에는 텍스트·이미지·오디오 지원과 특정 장비에서 측정한 성능이 공개되어 있다. [LiteRT Community 모델 카드](https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm)
- AGP 9.1.1의 최소 Gradle과 JDK는 각각 9.3.1과 17이다. [Android Developers의 AGP 호환성 표](https://developer.android.com/build/releases/agp-9-1-0-release-notes#compatibility)

## 실내 분석 검증

Galaxy S20+에서 침실·거실·주방 사진과 침실 반복 입력이 모두 JSON 1.0 검증을 통과했다. 사진 출처, 모델 원본 응답, 인식 오류, 단계별 시간과 PSS는 [실기기 검증 기록](ROOM_ANALYSIS_VALIDATION.md)에 있다.

## 기본 추론 검증 완료

2026-11-19에 호스트 빌드와 Galaxy S20+ (`SM-G986N`, Android 13/API 33, `arm64-v8a`)에서 다음을 확인했다. 실기기에는 LiteRT-LM Android 0.19.0, CPU 텍스트·비전 백엔드와 위 리비전의 모델을 사용했다.

- `:app:assembleDebug`와 `:app:lintDebug`를 통과했다. Lint는 오류 0건과 경고 6건을 보고했다. Android 대상 API, 의존성 업데이트, ChromeOS ABI, 백업 규칙, 런처 아이콘, RTL 설정에 관한 경고다.
- APK 설치와 실행을 확인했다. 모델이 없을 때 배치 경로가 표시됐다. SHA-256을 검증한 모델을 배치한 뒤 같은 화면에서 재시도하자 텍스트 질문에 `apple`이 나왔다.
- 빨강과 파랑 단색 PNG를 시스템 선택기에서 차례대로 선택했다. 같은 질문에 각각 `Red`와 `Blue`가 나왔다. 첨부 제거와 선택기 취소도 확인했다.
- APK 매니페스트에 인터넷 권한이 없음을 확인했다.

## 미검증 항목

- 추론 중 중복 실행 차단과 화면 닫기·회전 후 재실행을 확인한다.
- 초기화에 실패한 모델에서 오류 안내와 재시도를 확인한다.
- 다른 ARM64 기기에서 호환성, 모델 로드 시간, 단일·반복 추론 시간, 메모리와 발열을 측정한다.

## 실기기 재현 및 추가 검증

| 시나리오 | 확인 기준 |
| --- | --- |
| 설치 및 시작 | 대상 ARM64 기기에서 APK가 설치되고 앱이 열린다. |
| 모델 누락 및 복구 | 모델 없이 실행하면 배치 경로가 표시된다. 파일을 배치한 뒤 재시도하면 모델이 로드된다. |
| 텍스트 추론 | `Reply with the word apple.`을 입력하면 `apple`이 표시된다. 네트워크를 끈 상태에서 반복한다. |
| 이미지 추론 | 색이나 물체가 서로 다른 로컬 사진 두 장을 개별 선택하고 같은 질문을 보내면 응답이 각각의 사진을 반영한다. 이미지 선택을 취소하거나 읽을 수 없는 파일을 선택했을 때 다시 시도할 수 있다. |
| 반복 실행과 화면 수명주기 | 연속 두 요청이 각각 완료된다. 추론 중 중복 실행이 차단된다. 추론 중 화면을 닫거나 회전한 뒤 다시 실행할 수 있다. |
| 성능 및 호환성 | 초기 로드, 첫 응답 완료 시간, 반복 추론 시간, 메모리와 발열을 기기별로 기록한다. |

S20+에서 CPU·GPU를 비교한 결과, 오프라인 검증 범위와 별도 패키지로 재현하는 방법은 [하드웨어 가속 벤치마크](docs/ACCELERATION_BENCHMARK.md)를 참조한다. NPU, 오디오 입력, Unity 연결은 후속 범위다.

실내 사진에서 가구와 벽이 겹칠 때의 인식 품질, 기존·후보 프롬프트 비교 및 재현은 [실내 가구·벽 가림 인식 평가](docs/ROOM_OCCLUSION_EVALUATION.md)를 참조한다.

공유 Android Library는 `:bridge`에 있다. 별도 AAR 빌드, Unity 호출 계약, 호스트에서의 패키징 검증 절차는 [Unity–Native AI 통합 계약](docs/UNITY_INTEGRATION_CONTRACT.md)을 참조한다.
