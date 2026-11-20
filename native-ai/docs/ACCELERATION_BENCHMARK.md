# Galaxy S20+ CPU·GPU 실내 분석 벤치마크

Galaxy S20+ SM-G986N, Snapdragon 865 SM8250, Adreno 650, Android 13/API 33, arm64에서 LiteRT-LM Android 0.19.0과 Gemma 4 E2B IT를 사용했다. 앱에서 선택한 백엔드를 텍스트와 비전 모두에 설정한다. 기본값은 CPU다. GPU는 `Backend.GPU()`와 선택적 네이티브 라이브러리 `libOpenCL.so`, `libvndksupport.so`를 사용한다. 전환 시 단일 작업 스레드에서 기존 엔진을 닫고 다음 요청에서 선택한 엔진을 로드한다. GPU 초기화 오류는 UI에 표시되며 CPU 전환은 사용자가 선택한다. 로그 태그는 `RoomTwinInference`다. [LiteRT-LM v0.19.0 Kotlin 가이드](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/docs/api/kotlin/getting_started.md)

## 이전 실험 기준선

기준 커밋 `604d88f`에서 파생한 임시 APK의 결과다. 각 백엔드에서 새 캐시로 시작해 첫 요청 1회와 재사용 4회를 측정했다.

| 지표 | CPU | GPU |
| --- | ---: | ---: |
| 첫 요청 전체 | 25.97초 | 37.30초 |
| 침실 재사용 평균 | 20.69초 | 17.71초 |
| 거실 재사용 평균 | 24.40초 | 12.89초 |
| 5회 전체 합계 | 116.16초 | 98.49초 |
| 완료 직후 프로세스 PSS | 3.18~3.27 GiB | 2.42~2.43 GiB |

## 측정 재현

1. [모듈 환경 구성](../README.md)을 완료한 뒤 `native-ai/`에서 아래 명령을 실행한다. `field`는 앱의 동일한 Activity·전처리·엔진·파서를 빌드하고 전용 계측기를 추가한다. 설치 ID는 `com.roomtwin.nativeai.field`이며 모델 저장소도 별도다. 기본 앱과 나란히 설치할 수 있다.

   ```powershell
   gradle :app:testDebugUnitTest :app:assembleDebug :app:assembleField :app:lintDebug :app:lintField
   adb install -r app/build/outputs/apk/field/app-field.apk
   $dir = '/sdcard/Android/data/com.roomtwin.nativeai.field/files'
   adb shell mkdir -p $dir
   adb shell cp /sdcard/Android/data/com.roomtwin.nativeai/files/gemma-4-E2B-it.litertlm "$dir/gemma-4-E2B-it.litertlm"
   adb push "Bedroom-TT.jpg" "$dir/bedroom.jpg"
   adb push "Living room (Unsplash).jpg" "$dir/living.jpg"
   ```

2. 모델은 Gemma 4 E2B IT 리비전 `6e5c4f1`이다. 두 CC0 사진의 원본 다운로드 링크는 [사진 출처](../ROOM_ANALYSIS_VALIDATION.md)에 있다. 스크립트는 기기에서 세 파일의 SHA-256을 대조하고 일치할 때만 측정한다.
3. USB 디버깅을 유지하고 비행기 모드를 켠 뒤 Wi-Fi를 끈다. `adb shell dumpsys connectivity`의 `Active default network: none`을 확인한다. USB 충전, 배터리 온도, 측정 순서와 냉각 간격을 기록한다. 각 실행은 field 프로세스를 중지하고 선택한 백엔드의 기존 디스크 캐시를 이름 변경해 빈 캐시로 시작한다. 반복 측정 시 보관된 캐시가 저장 공간을 차지한다.
4. 아래 명령을 개별 실행한다. 비교하기 전에 온도를 확인한다. 두 출력 경로는 새 파일이어야 한다. `-TestSwitch`는 측정 종료 뒤 반대 백엔드와 원래 백엔드에서 자유 텍스트·사진 질문을 추가한다. 검증이 끝나면 기기 네트워크 설정을 복원한다.

   ```powershell
   ./scripts/Measure-Backend.ps1 CPU "$env:TEMP/roomtwin-cpu.jsonl"
   adb shell dumpsys battery
   ./scripts/Measure-Backend.ps1 GPU "$env:TEMP/roomtwin-gpu.jsonl" -TestSwitch
   ```

5. 각 실행은 침실 → 침실 → 거실 → 침실 → 거실 → 침실 순서다. 첫 요청 이후 5회는 같은 엔진을 재사용하고 요청마다 새 대화를 만든다. 공통 `GemmaRunner.analyzeRoom`이 긴 변 768 px, JPEG 90, 이미지 1장, 컨텍스트 2048, thinking 끔, 생성 한도 768, 동일 프롬프트와 [JSON 스키마 1.0](../ROOM_ANALYSIS_SCHEMA.md)을 적용한다.
6. JSONL에는 요청 순번·입력 크기·원본 응답·검증 결과 및 백엔드가 있다. `started` 뒤 종료 행이 없으면 중단된 요청이다. `failed`, `cancelled`, `invalid` 행도 점검한다. 완료 6건과 계측기 종료 상태를 확인한다. GPU 시작 직후 같은 프로세스의 네이티브 로그에서 `Initializing OpenCL-based API from graph`를 확인할 수 있다. OpenCL 그래프 사용 증거로 해석하며 개별 연산의 배치 비율은 측정하지 않았다.

### 지표 정의

- `initMs`는 엔진 초기화이고 `null`은 재사용이다. `imageMs`는 디코딩·축소·JPEG 인코딩을 합친 시간이다. `inferMs`는 대화 생성부터 메시지 처리, 벤치마크 정보 조회 및 대화 종료까지다. `totalMs`는 이 세 단계와 앱의 JSON 파싱·검증·직렬화를 포함한다. 사진 선택, 화면 렌더링과 JSONL 기록은 제외한다.
- `decodeTokens`는 계측 프로세스에서 `ExperimentalFlags.enableBenchmark`를 켜고 읽은 `lastDecodeTokenCount`다. `charsUtf16`은 독립적인 응답 UTF-16 길이다. 출력 내용과 토큰 수에 따라 시간이 달라진다. [LiteRT-LM v0.19.0 Conversation](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/kotlin/java/com/google/ai/edge/litertlm/Conversation.kt)
- `pssKiB`는 완료 직후 Android `Debug.getPss()`로 얻은 프로세스 PSS다. `sampleMaxPssKiB`는 요청 시작부터 완료까지 약 1초 간격 표본의 최대값이다. 짧은 피크를 놓칠 수 있다. 두 지표로 GPU 전용 메모리 또는 전체 시스템 메모리 사용량을 산출할 수 없다.
- `thermal`은 Android의 열 상태 등급, `batteryDeciC`는 배터리 온도(0.1°C 단위)다. 칩 온도, 소모 전력과 클럭은 별도 지표다.

## field APK 실측 · 2026-11-20

Galaxy S20+ 한 대에서 비행기 모드를 켜고 Wi-Fi를 껐다. Android 기본 네트워크가 없음을 확인했다. 배터리 100%, USB 연결 중이었다. 두 백엔드는 각각 빈 디스크 캐시와 새 프로세스에서 시작했다. CPU를 먼저 측정했고 45초 뒤 GPU를 실행했다. CPU 시작 시 배터리 온도는 27.1°C, GPU 시작 시에는 32.2°C였다. 정량 측정 뒤 field 앱 표시 이름과 실패 보고를 정리한 최종 빌드를 다시 빌드했다. 추론 및 계측 경로는 같다.

| 지표 | CPU | GPU |
| --- | ---: | ---: |
| 첫 엔진 초기화 | 4.92초 | 15.58초 |
| 첫 침실 추론 | 20.73초 | 26.31초 |
| 첫 요청 전체 | 25.70초 | 41.95초 |
| 침실 재사용 전체 평균 (3회) | 21.45초 | 20.22초 |
| 거실 재사용 전체 평균 (2회) | 24.55초 | 13.49초 |
| 6회 전체 시간 합계 | 139.15초 | 129.57초 |
| 이미지 전처리 범위 | 11~87 ms | 12~77 ms |
| 완료 직후 프로세스 PSS 범위 | 3.18~3.28 GiB | 2.46~2.49 GiB |
| 처리 중 표본 최대 PSS | 3.32 GiB | 2.57 GiB |
| JSON 스키마 통과 | 6/6 | 6/6 |
| 실패·취소 | 0 | 0 |

| 요청 | 입력 | CPU 추론 / 전체 (ms) | GPU 추론 / 전체 (ms) | CPU / GPU 디코딩 토큰 |
| ---: | --- | ---: | ---: | ---: |
| 1 | 침실, 640 × 506 px | 20,733 / 25,699 | 26,310 / 41,946 | 162 / 241 |
| 2 | 침실 | 20,608 / 20,694 | 18,887 / 18,910 | 162 / 241 |
| 3 | 거실, 5346 × 3568 px | 24,221 / 24,307 | 13,105 / 13,183 | 217 / 146 |
| 4 | 침실 | 21,415 / 21,436 | 18,895 / 18,917 | 162 / 241 |
| 5 | 거실 | 24,704 / 24,790 | 13,706 / 13,787 | 217 / 146 |
| 6 | 침실 | 22,137 / 22,228 | 22,800 / 22,831 | 162 / 241 |

GPU 첫 요청은 16.25초 더 걸렸다. 엔진을 재사용했을 때 사진별 평균 전체 시간은 침실 5.7%, 거실 45.1% 줄었다. GPU의 마지막 침실 요청은 같은 순번의 CPU보다 0.60초 느렸다. GPU는 침실에 더 많은 토큰을, 거실에 더 적은 토큰을 생성했다. 두 사진에서 체감 지연과 출력 길이가 함께 바뀌므로 이 차이를 백엔드 자체 처리량으로 환산할 수 없다.

### 출력 품질

| 사진 | CPU | GPU |
| --- | --- | --- |
| 침실 | 침대·창문을 인식. 안락의자를 소파로 분류. 756자 / 162토큰 | 안락의자를 의자로 인식. 침대를 7개 항목으로 중복 기록하고 문이 보인다고 보고. 1,115자 / 241토큰 |
| 거실 | 파란 소파·흰 의자를 인식. 창문을 중복 기록하고 사진에서 확인되지 않는 러그를 보고. 1,001자 / 217토큰 | 파란 소파·흰 의자와 창문을 인식. 보이는 문이 없다는 속성을 문 항목에 기록. 629자 / 146토큰 |

같은 백엔드에서 같은 사진의 모든 원본 응답은 일치했다. 항목의 실제 존재 여부와 중복은 스키마 검증 범위를 벗어난다.

### 오프라인과 회귀 확인

최종 field APK로 비행기 모드 및 Wi-Fi 차단 상태에서 앱을 새로 시작했다. 다운로드 폴더의 공개 침실 파일을 시스템 사진 선택기로 불러온 뒤 CPU 기본값으로 분석하고 같은 파일을 GPU로 재분석했다. 화면에서 선택 백엔드와 JSON 스키마 성공을 확인했다. 처리 중에는 재요청과 백엔드 선택이 비활성화됐다. 동일 앱 프로세스 로그에서 CPU 종료 후 GPU 초기화 순서를 확인했다. 기존 디스크 캐시가 있는 이 UI 검증의 전체 시간은 CPU 22.01초, GPU 24.95초였다. 검증 뒤 기기의 비행기 모드와 Wi-Fi를 복원했다.

계측 종료 뒤 GPU → CPU → GPU를 전환했다. 두 백엔드에서 첫 자유 텍스트 요청은 `apple`을 반환했고 거실 사진에 대한 소파 색 질문은 같은 엔진에서 `Blue`를 반환했다. 각 전환 후 첫 요청은 새 엔진을 로드했다. 디버그·field APK 빌드와 파서 JVM 테스트 8개가 통과했다. 두 변형의 Lint는 각각 오류 0건, 경고 5건이었다.

### 발열과 동시 실행 한계

완료 시점의 배터리 온도는 CPU 27.8→32.7°C, GPU 32.1→33.8°C였다. Android 열 상태는 CPU의 앞 3회가 0, 뒤 3회가 1이었고 GPU는 6회 모두 0이었다. 한 기기와 한 실행 순서를 관찰했으며 초기 온도도 달랐다. SoC 전력, GPU 전용 메모리, 클럭 및 스로틀링 원인은 미측정이다. Unity AR 동시 실행 전에는 카메라와 GPU 큐의 경쟁, AR 프레임 시간, OS 메모리 압박, 지속 발열 및 세션 종료 중 자원 회수를 별도로 확인한다. 다른 기기·GPU 드라이버, GPU 초기화 오류 재현, 장시간 반복은 미검증이다.
