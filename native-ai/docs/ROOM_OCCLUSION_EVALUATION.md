# 실내 가구·벽 가림 인식 평가

## 입력과 사전 판정

벽, 문, 창문과 주요 가구 다섯 종류(`cabinet`, `bed`, `chair`, `sofa`, `table`)의 사진별 검출을 평가한다. 보이는 객체 열에 없고 보류에도 없는 평가 유형은 화면 안에서 미관측이다. 문의 경계가 모호하면 보류한다. 창 한 조의 창짝, 수납장 한 세트의 패널은 각각 다른 객체로 세지 않는다. 영상 밖이나 가려진 공간의 실제 구조는 판정에 사용하지 않는다. 여기서 모서리는 두 벽면이 만나는 접합부이며 바닥 걸레받이, 창틀, 문틀, 수납장 접합부와 구분한다.

| ID | 조건 | 보이는 평가 객체 | 채점 보류 | 벽-벽 모서리 |
| --- | --- | --- | --- | --- |
| empty | 가구 없음, 벽 한 면과 창 | wall, window | — | 미관측 |
| bedroom | 가구 다수, 침대·장식으로 벽 일부 가림 | wall, window, bed, chair, table | cabinet, door | 보임 |
| living | 가구 다수, 소파·수납장·TV로 벽 일부 가림 | wall, window, sofa, chair, cabinet | — | 보임 |
| doors | 가구 없음, 근경과 복도에 서로 다른 문 | wall, door | — | 보임 |
| occluded | 근경 대부분 흐림, 뒤의 수납장 일부만 뚜렷함 | cabinet | wall, door | 미관측 |
| cabinet | 같은 주방 원본의 수납장 앞면·옆면만 자른 통제 사진 | cabinet | — | 미관측 |
| kitchen | 가구 다수, 벽의 작은 부분과 창만 노출 | wall, window, cabinet, chair, table | door | 보임 |

`kitchen` 섬은 고정 수납장, 섬 앞 상판과 의자 그룹은 테이블로 판정했다. `doors`는 복도 안쪽의 다른 문을 포함해 문이 여러 개 보이므로 유형이 같은 항목의 반복만으로 중복으로 판정하지 않는다.

## 채점 방법

- 각 사진·유형마다 앱이 소비할 유효한 JSON의 `objects`·`openings`에 해당 `type`이 있으면 검출 1건으로 센다. 동종 객체 수와 속성은 이 점수에 반영하지 않는다. 숨은 물체를 언급하는 `uncertainObservations`나 속성의 이름은 검출로 승격하지 않는다. 예를 들어 `door`의 속성이 `none visible`이어도 구조화 결과에 문 항목을 만들었으므로 거짓 양성이다. 보류 유형은 분모에서 뺀다. JSON 검증 실패는 별도로 집계하며 앱에서 쓸 수 있는 검출은 0건으로 센다.
- 정밀도는 TP/(TP+FP), 재현율은 TP/(TP+FN), F1은 2TP/(2TP+FP+FN)이다. TP·FP·FN은 타입이 아니라 사진×타입 조합을 합산한다. 다른 유형으로 분류한 한 객체는 해당 유형에 따라 FP와 FN을 함께 만들 수 있다.
- 객체별 오류는 사람이 화면과 응답을 대조한다. 눈에 띄는 실물이 빠지면 누락, 같은 실물을 여러 항목으로 쓰면 중복, 화면의 실물을 다른 유형으로 단정하면 오분류, 화면에 근거가 없는 별도 물체를 단정하면 환각이다. 동일 종류의 별개 실물은 중복에서 제외한다. 유형 점수와 별도로 사례를 기록한다.
- 모서리가 보이지 않는 세 입력에서 구조화 필드와 문장을 검토한다. 근거 없는 벽-벽 접합을 긍정적으로 단정한 경우만 센다. 수납장 모서리 언급, 모서리가 안 보인다는 진술, 불확실하다는 진술, 무응답은 단정으로 세지 않는다. 모서리는 현재 앱 스키마에 독립 필드가 없어 미언급은 모서리 검출 정답으로 해석할 수 없다. 모서리 미관측과 벽 자체 미관측은 따로 보고한다.

## 재현

1. Wikimedia Commons의 **Original file**을 아래 ID명으로 저장한다. 원본 파일에는 변경이 없다. `cabinet`만 `kitchen` 원본에서 크롭했다. 이미지 파일은 저장소에 포함하지 않는다.

| ID | 원본 및 저작자 | 사용 조건 | 입력 SHA-256 |
| --- | --- | --- | --- |
| empty | [Empty Apartment Room Window](https://commons.wikimedia.org/wiki/File:Empty_Apartment_Room_Window.jpg) · Punker1999 | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) | `9fe92936e03818e07db0dbbab13f43c61da301e45cb9f2e23f2996a2e32555e8` |
| bedroom | [Bedroom-TT](https://commons.wikimedia.org/wiki/File:Bedroom-TT.jpg) · GregoryButler | CC0 1.0 | `c5db919661ef517085764e0e60d351e7068758be65b789348ee77f4831bd1da6` |
| living | [Living room (Unsplash)](https://commons.wikimedia.org/wiki/File:Living_room_(Unsplash).jpg) · Jarosław Ceborski | CC0 1.0 | `98e196f52a67abb96e04c6a33694d1710ea885797e01ae2d52b7bba9f747893b` |
| doors | [Apartment doors](https://commons.wikimedia.org/wiki/File:Apartment_doors.jpg) · Downtowngal | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) | `a5b482b20330276cf31366745ee3f1156d9e30e888385b042df229177ec5c736` |
| occluded | [Kitchen area with open door and a wall out of focus in front of it](https://commons.wikimedia.org/wiki/File:Kitchen_area_with_open_door_and_a_wall_out_of_focus_in_front_of_it.jpg) · C.M.B. | CC0 1.0 | `767cf60c704504363049d3ce4c378f3c7662ff9bd5d3bd6cddbf825e04d518c5` |
| kitchen | [Blue white kitchen interior (Unsplash)](https://commons.wikimedia.org/wiki/File:Blue_white_kitchen_interior_(Unsplash).jpg) · NeONBRAND | CC0 1.0 | `1030543c6e3b65863e9ee11c4a4f9da6dfa78bc93c7d20b93782fb16ffcd2fb0` |
| cabinet | 같은 NeONBRAND 사진에서 좌상단 기준 `x=0, y=1220, w=705, h=1700` 크롭 | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | `30bca9fd3dd8fb9162c5b38b83112ac4a4b628f6c4c5d49cc4a25cb6f31a881b` |

Windows PowerShell에서 `kitchen.jpg`를 내려받은 디렉터리에서 크롭을 생성한다. 다른 JPEG 인코더를 쓰면 같은 크롭이어도 해시와 추론 결과가 달라질 수 있다.

```powershell
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::new((Resolve-Path ./kitchen.jpg).Path)
$crop = $source.Clone([Drawing.Rectangle]::new(0, 1220, 705, 1700), [Drawing.Imaging.PixelFormat]::Format24bppRgb)
try {
    $crop.Save((Join-Path (Get-Location) 'cabinet.jpg'), [Drawing.Imaging.ImageFormat]::Jpeg)
} finally {
    $crop.Dispose()
    $source.Dispose()
}
Get-ChildItem *.jpg | Get-FileHash
```

2. `native-ai/`에서 `gradle :app:assembleField`를 실행하고 [field APK 설치 및 모델 복사](ACCELERATION_BENCHMARK.md#측정-재현) 절차를 따른다. 기기에 있는 모델은 Gemma 4 E2B IT `6e5c4f1`이며 SHA-256은 `181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c`다. 아래 `$inputs`는 해시를 확인한 입력 디렉터리다.

```powershell
$package = 'com.roomtwin.nativeai.field'
$fixtures = "/sdcard/Android/data/$package/files/quality"
adb shell mkdir -p $fixtures
foreach ($id in 'empty','bedroom','living','doors','occluded','cabinet','kitchen') {
    adb push "$inputs/$id.jpg" "$fixtures/$id.jpg"
    adb shell sha256sum "$fixtures/$id.jpg"
}
adb shell am instrument -w -r -e backend CPU "$package/com.roomtwin.nativeai.RoomQualityInstrumentation"
adb exec-out run-as $package cat files/quality-CPU.jsonl | Set-Content -Encoding utf8 "$env:TEMP/room-quality-CPU.jsonl"
# 기기가 식은 후 GPU를 실행한다.
adb shell am instrument -w -r -e backend GPU "$package/com.roomtwin.nativeai.RoomQualityInstrumentation"
adb exec-out run-as $package cat files/quality-GPU.jsonl | Set-Content -Encoding utf8 "$env:TEMP/room-quality-GPU.jsonl"
```

3. 각 백엔드가 `status=completed`이고 결과가 14줄인지 확인한다. 각 프로세스에서 `empty` 한 번으로 엔진을 준비한 뒤 요청별로 새 대화를 연다. CPU는 첫 사진에서 기존 프롬프트, GPU는 후보 프롬프트부터 시작한다. 같은 사진의 두 요청은 연속하고, 다음 사진마다 먼저 실행할 프롬프트를 뒤집는다.
4. LiteRT-LM 0.19.0, 텍스트와 비전의 동일 백엔드, 긴 변 최대 768px·JPEG 90, 컨텍스트 2048, 출력 한도 768, thinking 끔, 스키마 1.0을 공통으로 사용한다. 기존 프롬프트는 `RoomAnalysisJson.prompt`를 그대로 사용한다. 후보는 여기에 보이는 건축면·개구부 증거, 벽 모서리와 가구 경계 구분, 중복 억제와 불확실성 지시를 추가한다. 두 프롬프트 모두 실제 치수·거리·3D 좌표를 요구하지 않는다.
5. `totalMs`는 이미지 전처리, 대화 생성·추론·종료, JSON 파싱·검증·직렬화를 포함한 워밍업 이후 시간이다. 화면·선택기·보고서 저장은 제외한다. `inferMs`는 모델 대화 단계 시간이고 `decodeTokens`는 런타임 실측 디코딩 토큰 수다. 프롬프트와 출력 길이 모두 실행 시간에 영향을 준다.

## 실측 결과 · 2026-11-20

Galaxy S20+ SM-G986N, Android 13/API 33, Snapdragon 865, USB 연결 및 배터리 100% 상태에서 측정했다. 시작 배터리 온도는 CPU 24.1°C, GPU 32.4°C였다. CPU가 끝난 뒤 60초 기다려 GPU를 실행했다. 응답 직후 Android 열 상태는 CPU 0~1, GPU 0이었다. GPU 프로세스에서 OpenCL 디바이스 생성과 직렬화된 OpenCL 그래프 초기화를 확인했다. CPU 실행 중 호스트 ADB 대기가 끊겼지만 기기는 14행을 끝까지 기록하고 엔진을 닫았다. GPU도 14행과 엔진 종료를 확인했다. 공개 사진에 대한 원본 응답 28개와 요청별 시간은 [평가 결과 JSONL](evaluation/room-occlusion-results.jsonl)에 있다.

| 백엔드·프롬프트 | JSON 통과 | TP / FP / FN | 정밀도 | 재현율 | F1 | 전체 중앙값 | 추론 중앙값 | 전체 평균 | 디코딩 토큰 중앙값 | 동일 항목 반복¹ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CPU BASELINE | 7/7 | 17 / 2 / 4 | 0.895 | 0.810 | 0.850 | 18.63초 | 18.57초 | 20.27초 | 107 | 16회 (4/7장) |
| CPU CANDIDATE | 7/7 | 15 / 2 / 6 | 0.882 | 0.714 | 0.789 | 22.80초 | 22.69초 | 22.40초 | 174 | 32회 (7/7장) |
| GPU BASELINE | 7/7 | 18 / 2 / 3 | 0.900 | 0.857 | 0.878 | 16.78초 | 16.73초 | 15.81초 | 138 | 10회 (4/7장) |
| GPU CANDIDATE | 7/7 | 13 / 1 / 8 | 0.929 | 0.619 | 0.743 | 20.48초 | 20.27초 | 18.08초 | 180 | 36회 (5/7장) |

¹ 같은 응답에서 유형과 속성 집합이 동일한 항목을 첫 등장 이후 센 값이다. 동일하게 묘사된 별개 실물도 포함할 수 있어 개별 객체의 수동 중복 판정과 차이가 있다.

| 유형 | CPU 기존 | CPU 후보 | GPU 기존 | GPU 후보 |
| --- | ---: | ---: | ---: | ---: | ---: |
| wall | 4/1/1 | 4/1/1 | 5/1/0 | 3/1/2 |
| door | 1/0/0 | 1/0/0 | 0/1/1 | 1/0/0 |
| window | 4/0/0 | 2/0/2 | 2/0/2 | 2/0/2 |
| cabinet | 4/0/0 | 4/0/0 | 4/0/0 | 3/0/1 |
| bed | 1/0/0 | 1/0/0 | 1/0/0 | 1/0/0 |
| chair | 1/0/2 | 1/0/2 | 3/0/0 | 1/0/2 |
| sofa | 1/1/0 | 1/1/0 | 1/0/0 | 1/0/0 |
| table | 1/0/1 | 1/0/1 | 2/0/0 | 1/0/1 |

각 셀은 TP/FP/FN이다. 그룹마다 51개 사진·유형 조합을 채점했고 그중 실제 보이는 유형은 21개였다. 문 유형의 양성 사진은 한 장이다.

| 사진 | CPU 기존 → 후보 (초) | GPU 기존 → 후보 (초) |
| --- | ---: | ---: |
| empty | 13.76 → 14.16 | 8.60 → 8.88 |
| bedroom | 20.86 → 24.23 | 25.16 → 23.55 |
| living | 24.43 → 24.31 | 17.61 → 10.10 |
| doors | 18.19 → 22.80 | 9.38 → 27.94 |
| occluded | 18.63 → 20.65 | 16.78 → 23.12 |
| cabinet | 18.25 → 19.51 | 12.06 → 12.50 |
| kitchen | 27.75 → 31.11 | 21.06 → 20.48 |

### 대표 오류 사례

| 유형 | 사진 및 관찰 |
| --- | --- |
| 누락 | CPU 후보가 `empty`와 `bedroom`의 창문을 빠뜨렸다. GPU 후보는 `living`에서 소파와 창문만 남기고 기존 응답이 검출한 벽·의자·수납장을 놓쳤다. `doors`에서는 GPU 후보가 기존 응답이 불확실 항목으로만 언급한 문을 개구부로 검출했다. |
| 중복 | CPU 후보가 `doors`에서 같은 회색 벽과 흰 몰딩 설명을 반복했다. GPU 기존 응답은 `bedroom`의 침대를 7개 항목에 반복했고, GPU 후보는 `occluded`에서 동일한 상부 수납장 묘사를 반복했다. |
| 오분류 | `cabinet`에는 수납장의 앞면·옆면만 보인다. CPU와 GPU 모두 기존·후보 응답에서 패널을 벽으로 단정했다(4/4). CPU는 두 프롬프트에서 `bedroom`의 안락의자를 소파로 분류했다. |
| 환각·근거 없는 항목 | `living`에서 기존 CPU는 보이지 않는 러그를 보고했다. 기존 GPU는 `none visible` 속성이 있는 문 항목을 생성했다. GPU 후보는 문 항목을 생략했지만 가시 객체도 일부 누락했다. |

`occluded`의 흐린 면은 판정 근거가 부족해 벽 유형 점수에서 보류했다. CPU는 두 프롬프트 모두 이를 벽으로 단정했다. GPU 기존 응답은 흐린 영역을 불확실하게 서술했고, 후보는 별도 벽 항목을 만들었다. 모서리 미관측 사진 3장에 대한 12응답 중 벽-벽 접합을 단정한 응답은 0건이었다. 현재 스키마는 모서리 판단을 요구하지 않으며 전체 28응답에서 모서리 언급이 없었다. 이 자료로 모서리 검출 정확도를 산출할 수 없다. 응답에서 실제 3D 좌표나 벽 치수는 관측되지 않았다.

### 채택 판단과 한계

후보는 기본 앱에 적용하지 않았다. 유형 F1이 CPU에서 0.850→0.789, GPU에서 0.878→0.743으로 하락했다. GPU에서 거짓 양성은 2건에서 1건으로 줄었으나 누락은 3건에서 8건으로 늘었다. 동일 항목 반복도 두 백엔드에서 늘었다. 전체 처리 시간 중앙값은 CPU 22.4%, GPU 22.0% 증가했다. 후보의 입력 프롬프트가 더 길고 응답 토큰 중앙값도 증가했다. 백엔드별 시작 온도가 달라 이 수치를 두 백엔드의 하드웨어 성능 비교로 사용하지 않는다.

실측은 기기 1대, 공개 원본 6장과 크롭 1장, 조건당 1회, 단일 판정자 기준이다. `kitchen`과 `cabinet`은 같은 원본에서 나왔다. 사진별 유형 점수에는 실제 가구 수, 물체 위치, 속성 정확도 및 보류 영역이 포함되지 않는다. 현장 일반화와 통계적 유의성은 미검증이다. 새 후보를 평가할 때는 후보 작성에 쓰지 않은 사진과 명확한 문·벽 접합의 양성·음성 입력을 확보하면 문과 모서리의 성능을 따로 판단할 수 있다.

JVM 파서 테스트 8개와 디버그·field APK 빌드가 통과했다. Android Lint는 두 변형 모두 오류 0건, 경고 5건이었다.
