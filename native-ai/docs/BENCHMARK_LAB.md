# 오프라인 Benchmark Lab

앱 상단에서 **Benchmark Lab**을 선택한다. 기본값은 CPU, 컨텍스트 2048, Thinking OFF, 워밍업 1회와 측정 반복 3회다. 백엔드를 양쪽으로 설정하면 CPU → GPU 또는 GPU → CPU 순서를 선택할 수 있다. 단일 이미지와 Before/After 입력에는 내장 도형 또는 사용자 사진을 사용할 수 있다. 사용자 사진은 세션당 한 번 임시 복사해 동일한 파일 바이트를 반복해서 읽는다.

| 실험 | 실행 내용 |
| --- | --- |
| Controlled Benchmark | 텍스트 생성, 단일·다중 이미지, 멀티턴, 긴 텍스트, JSON을 실행한다. 조건별로 엔진을 새로 열어 Cold 1회, 선택한 Warm-up, Warm 반복 순으로 측정한다. 디스크 캐시는 유지하며 모든 반복에 새 대화를 사용한다. |
| Context Evaluation | Needle에 앞·중간·뒤 위치를 지정한다. Recall은 앞 턴의 값, Conflict는 수정한 최신 값을 확인한다. Saturation은 추정 부하 30·60·90·110%에서 값의 회수 여부, 지연과 실패를 기록한다. 길이 비교는 2048·4096을 사용하며 8192는 명시적으로 선택할 때 추가한다. |
| Vision Evaluation | 존재·부재·개수·상대 위치·Before/After·벽과 가구·문자 판독을 확인한다. 내장 이미지에는 고정된 기준 답변이 있다. 사용자 이미지에는 직접 작성한 질문과 기준 답변을 사용한다. 직접/요약 비교는 동일한 원본 두 장과 기준 답변으로 두 방식을 각각 실행한다. 이 방식 비교에서는 하나의 백엔드를 고른다. |
| Dashboard | 실행 순서, 컨텍스트, Thinking, 비교 방식별로 Cold·Warm-up·Warm 결과를 구분한다. 성공한 시도 지연의 평균·중앙값·nearest-rank P95·모집단 표준편차·최소·최대를 표시하고 오류·중단·성공 건수를 함께 제공한다. 세션 및 반복을 선택하면 메모리·발열·토큰 지표를 확인할 수 있다. |

CPU/GPU 비교는 동일한 사용자 턴, 이미지, 모델 파일과 생성 설정을 사용한다. 샘플링과 멀티턴의 이전 모델 출력으로 실제 생성 길이와 다음 턴에서 유지되는 토큰이 달라질 수 있다. 실행 순서, 시작·종료의 발열과 배터리, 실제 출력 토큰 수를 함께 확인한다. 작은 표본의 P95와 차이는 기술 통계로 읽는다.

점수는 지정된 단일 답과 유니코드 및 공백을 정규화한 응답의 일치 여부다. 객체 수·색상·위치 등 정답을 확인할 수 있는 좁은 질문에 적합하다. 정답률은 채점된 시도를 기준으로, 추론·파싱 실패는 실행 결과에도 표시한다. Context는 현재 대화의 작업 문맥을 실험한다. 영구 기억은 사용하지 않는다. 입력 부하 비율은 영문 문자 수에 기반한 근사치이며 템플릿, 이미지, 내부 채널 및 실제 토크나이저의 컨텍스트 사용량은 런타임이 보고할 때만 표시한다.

## 지표

| 지표 | 출처와 범위 |
| --- | --- |
| 모델 초기화 | 새 LiteRT 엔진을 초기화하는 앱 측 단조 시계 시간. 이미 열린 엔진에 대해서는 N/A. |
| 첫 표시 청크·전체 요청 | 요청 시작부터 처음으로 공백이 아닌 텍스트 콜백까지, 또는 종료 콜백까지의 앱 측 시간. 이미지 디코딩과 해당하는 경우 초기화가 포함된다. 모델이 청크를 묶어서 줄 수 있다. |
| TTFT | 실제 토큰 생성 시점을 얻을 수 없어 N/A. 첫 대화 턴의 LiteRT 추정은 첫 prefill과 토큰당 평균 decode 시간을 더한 별도 값이다. |
| Prefill·Decode | 각 턴에서 LiteRT가 제공한 입력 및 출력 토큰과 tok/s. 시간은 런타임과 같은 식을 역산한다. 데이터가 없으면 N/A. 출력에는 런타임이 집계하는 토큰이 반영된다. 화면 문자 수와 일치할 필요는 없다. |
| Context usage | LiteRT의 대화 토큰 수와 KV 용량을 각각 읽는다. 0 또는 읽기 실패는 N/A로 처리한다. 컨텍스트 설정값은 별도로 보존한다. |
| 프로세스·시스템 | 프로세스 PSS/RSS, Native/Java heap, 시스템 가용 메모리, Android Thermal Status, 배터리 온도와 잔량을 기록한다. PSS/RSS와 발열은 반복 전후 및 진행 중 2초 간격에서 관측한 최대값을 보여 준다. 짧은 스파이크를 놓칠 수 있다. GPU 전용 메모리는 N/A다. |

[LiteRT-LM 0.19.0 대화 API](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/kotlin/java/com/google/ai/edge/litertlm/Conversation.kt) · [LiteRT-LM 측정 계산식](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.19.0/runtime/engine/io_types.cc) · [Android Debug](https://developer.android.com/reference/android/os/Debug) · [Android 열 상태](https://developer.android.com/reference/android/os/PowerManager#getCurrentThermalStatus())

## 실행 중단과 기록

- 실험은 앱의 기존 공유 엔진과 작업 스레드에서 한 번에 하나씩 실행한다. 중단을 요청하거나 화면이 파괴되면 현재 네이티브 턴이 끝난 뒤 추가 턴·반복을 건너뛴다. LiteRT-LM 0.19.0은 `Conversation.cancelProcess()`를 제공한다. Benchmark Lab은 완료 시점과 일부 세션 기록을 보존하기 위해 턴 경계에서 협조적으로 중단한다.
- 최대 120회로 제한한다. 8192 또는 4회를 넘는 다중 이미지 세션을 실행하려면 확인이 필요하다. Android가 SEVERE 이상을 보고하면 다음 턴 또는 반복을 중단한다. OOM 또는 GPU 초기화 오류를 세션/시도에 기록하며 GPU 조건을 CPU로 치환하지 않는다. 런타임이 맥락 초과로 식별할 수 있는 오류를 반환하면 `CONTEXT_LIMIT`으로 기록한다.
- 각 세션은 UUID, UTC, 빌드 Git SHA·앱 버전, 모델 이름·파일 크기·수정 시간, 기기·OS·LiteRT 버전, 조건, 단계별 측정값·오류·채점을 앱 내부에 기록한다. 모델 파일은 다시 내려받거나 해시를 계산하지 않는다. 실행 전후 비교는 파일 이름·크기·수정 시간으로 확인한다.
- JSONL의 `session`, `trial`, `end` 이벤트를 순차 저장한다. CSV는 턴별 행이며 턴이 없는 시도도 한 행을 기록한다. 앱이 예기치 않게 종료돼도 먼저 기록한 시도를 열 수 있다. 세션을 선택하여 내보내거나 삭제하며 내보낼 위치는 사용자가 고른다. 사진, 질문, 응답, 기준 답변, 시스템 프롬프트 원문은 로그에 남기지 않는다.
- 실제 Android 추론, GPU 호환성, 메모리·발열·회수율과 성능 수치는 Galaxy S20+에서 별도로 실측한다.

## 호스트 검증

```sh
cd native-ai
gradle :app:testDebugUnitTest :bridge:testDebugUnitTest :app:assembleDebug :app:assembleField :app:lintDebug :app:lintField :bridge:assembleRelease :bridge:lintRelease
gradle :bridge:publishReleasePublicationToUnityRepository
gradle -PusePublishedBridge=true :app:assembleDebug :app:assembleField
```
