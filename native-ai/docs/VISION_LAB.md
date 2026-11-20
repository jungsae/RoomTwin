# 오프라인 Vision Lab

Android 독립 앱 상단에서 **Vision Lab**을 누른 뒤 AI 카메라 탐정, 사진 퀴즈, 무엇이 바뀌었을까?, 물건 찾기, 번역 카메라, 공간 분석을 선택한다. 같은 모델 파일과 이미지 전처리, CPU/GPU 엔진을 Playground·Room Analysis에서 공유한다. 기본 Vision Lab 설정은 CPU, 컨텍스트 2048, 출력 최대 512토큰, Thinking OFF다. 생성 설정은 Vision Lab 대화에만 적용한다.

| 기능 | 입력과 결과 |
| --- | --- |
| AI 카메라 탐정 | 사진 한 장을 조사하고 보이는 사실·단서·위치 관계와 상황 추측을 구분한다. 같은 사진에 대해 후속 질문을 보낼 수 있다. |
| 사진 퀴즈 | 난이도를 고른 뒤 구조화된 문제 3개를 풀 수 있다. 사진 속 근거와 설명을 표시한다. JSON의 문항 수·선택지·정답 범위를 검증한다. 모델이 근거 부족을 알리거나 JSON이 잘렸으면 채점에 사용하지 않는다. 사진을 직접 대조해 정답 근거를 확인한다. |
| 무엇이 바뀌었을까? | Before, After를 차례로 선택한다. 한 요청에 두 사진을 보내 비교하며 해당 경로에서 처리 오류가 발생하면 각 사진의 관찰 기록을 생성해 비교한다. 두 번째 방식은 별도로 선택할 수 있고 결과에 사용한 방법을 표시한다. |
| 물건 찾기 | 물건 이름을 입력한다. 발견 후보, 미관측, 불확실을 구분하고 사진 기준 좌측·중앙·우측 및 주변 물체를 근거로 설명한다. 픽셀 좌표나 검출 박스는 제공하지 않는다. |
| 번역 카메라 | 한국어, 영어, 일본어, 중국어 간체, 스페인어 중 번역 대상을 선택한다. 글자를 읽은 원문, 번역문, 읽지 못한 부분을 나눠 표시한다. 전용 OCR 엔진을 쓰지 않으며 정밀한 글자 판독이 필요한 경우 원본을 대조한다. |
| 공간 분석 | 보이는 가구·전자기기·벽면, 용도 추정, 정리·배치, 가림과 추가 촬영 위치를 정리한다. 사진에 근거한 관찰이며 실제 거리·치수·3D 좌표는 제공하지 않는다. |

Gemma 4 모델 계열은 공식 예제에서 한 프롬프트의 다중 이미지를 지원한다. LiteRT-LM **0.19.0**은 `EngineConfig.maxNumImages`와 한 메시지에 복수 `Content.ImageBytes`를 전달하는 API를 제공한다. 실기기 메모리, 드라이버와 설치된 모델 조합에서의 안정성은 기기 검증에서 확인한다. [Gemma 4 이미지 이해](https://gemma.ai/get-started/capabilities/image/) · [LiteRT-LM 0.19.0 설정](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/kotlin/java/com/google/ai/edge/litertlm/Config.kt) · [메시지 입력](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/kotlin/java/com/google/ai/edge/litertlm/Message.kt)

탐정과 공간 분석은 응답을 스트리밍한다. 두 사진 직접 비교는 완료된 응답을 표시하며, 사진별 요약 비교는 최종 비교 답변을 스트리밍한다. 퀴즈·물건 찾기·번역은 답변을 검증한 뒤 각 항목에 표시한다. 전체 처리 시간과 첫 응답 청크까지의 시간은 요청을 시작한 작업 스레드와 응답 콜백 사이에서 측정한다. 구조화된 답변에서도 첫 응답 청크는 콜백 수신 시각을 뜻한다. 출력 토큰과 토큰/초는 LiteRT-LM의 마지막 디코드 측정값이다. 요약 비교의 토큰과 속도는 최종 비교 응답 기준이다. 런타임이 값을 주지 않으면 `N/A`로 표시한다. [LiteRT-LM 응답 및 측정 API](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/kotlin/java/com/google/ai/edge/litertlm/Conversation.kt)

**사진과 기록 지우기**로 현재 기능의 상태를 지울 수 있다. 다른 기능이나 모드로 이동할 때도 이전 Vision Lab 상태를 지우며 사진은 자동으로 내보내지 않는다. 갤러리 원본은 기기에 그대로 있고 카메라 촬영물은 앱 캐시를 사용한다. **사진 사본 저장**을 눌러 저장 위치를 선택한 경우만 별도 사본을 남긴다. 설정 변경 시 사진은 다시 사용할 수 있고 분석 기록과 대화는 초기화된다. 화면이 재생성되면 실험 세션을 새로 시작한다.

## 호스트 빌드

`native-ai`에서 실행한다.

```sh
gradle :app:testDebugUnitTest :bridge:testDebugUnitTest :app:assembleDebug :app:assembleField :app:lintDebug :app:lintField :bridge:lintRelease :bridge:assembleRelease
gradle :bridge:publishReleasePublicationToUnityRepository
gradle -PusePublishedBridge=true :app:assembleDebug :app:assembleField
```

설치 가능한 상태의 기기에서 기존 데이터와 모델 파일을 보존하며 Debug 앱을 업데이트하려면 `adb install -r app/build/outputs/apk/debug/app-debug.apk`를 사용한다. Field는 별도 패키지이므로 기존 Field 설치를 갱신할 때만 `adb install -r app/build/outputs/apk/field/app-field.apk`를 사용한다.
