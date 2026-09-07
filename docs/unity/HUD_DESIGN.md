# 인게임 HUD 기술 설계

> 상태: 캔버스 규격 · 우상단 시간 슬롯 확정본 / 생성 프롬프트 7장 추가(2026-09-07) / 구현 미착수
> 기준일: 2026-09-07
> 대상: `Assets/01.Scripts/06.UI/Combat/`, HUD 캔버스, UI 이미지 에셋 일체

UI는 평가자가 보는 유일한 층이다. 로직이 정확해도 화면에 없으면 없는 기능이다 — [주차 계획](../weekly-plan-2026-09-04.md)에서 UI 최소본을 W3로 당긴 이유가 이것이다.

이 문서는 HUD를 실제로 만들 수 있는 수준까지 규격을 확정한다. 화면 11종의 목록과 담당은 [주차 계획 3.3](../weekly-plan-2026-09-04.md), 폴더 배치는 [프로젝트 구조 4.6](../04-architecture.md), 색 문법은 [직업 정체성 15.3](../class-identity-and-boss-2026-09-03.md)이 원본이다.

**소유.** 이미지 에셋 제작은 팀장(@AidenKim923, 아트), UI 구현은 @kjw1237이다 — [주차 계획 2장](../weekly-plan-2026-09-04.md)의 소유 기준 그대로다. 그래서 이 문서의 3장(에셋 규격)은 **넘길 때의 계약**이다. 규격이 맞지 않은 채로 넘어가면 받는 쪽이 이미지를 다시 만져야 하고, 그 순간 한 사람 일이 두 사람 일이 된다.

---

## 0. 이 문서가 확정하는 핵심 결정

1. **Reference Resolution은 1920×1080이다.** 새로 정한 값이 아니라 `Dev_Title`·`Dev_Lobby`의 기존 캔버스가 이미 쓰고 있는 값이다.
2. **`Match`는 `1.0`(Height 기준)이다.** 현재 두 캔버스의 `0.5`는 Unity 기본값이지 이 프로젝트의 선택이 아니었다. HUD가 전부 모서리에 붙으므로 세로 고정이 맞다.
3. **`ProjectSettings`의 기본 해상도 `1024×768`은 결함이다.** 4:3이고 아무도 바꾼 적이 없다. 빌드 검증을 지금까지 4:3 창에서 해왔다는 뜻이다.
4. **우상단은 위젯 두 개가 아니라 슬롯 하나에 상태 다섯이다.** 결계(필드 시계)와 보스(보스 시계)는 씬이 다르고 표시 구간이 겹치지 않으므로 같은 자리를 순서대로 쓴다.
5. **보스전 숫자는 남은 시간이다.** 경과 시간은 정보가 아니라 기록이며 정산 화면 소재다.
6. **결계 단계는 숫자가 아니라 부적 3장으로 표시한다.** 단계가 3개뿐이라 세는 것이 아니라 보는 것이다.
7. **결계 계열에 여우불 청록을 쓰지 않는다.** 청록은 적의 색이다. 결계는 저승이 친 신성이므로 한지 백색 + 주묵 붉은색이다.
8. **시간 숫자는 서버가 보낸 "시각" 하나에서 각 클라이언트가 계산한다.** 매 초 SyncVar를 쏘지 않고, 클라이언트가 로컬로 세지도 않는다.
9. **이미지로 만들 것과 코드·색으로 만들 것을 가른다.** 11화면에서 고유 이미지는 9장 내외다. 나머지는 재사용이거나 `fillAmount`·틴트·알파다.
10. **HUD는 읽기 전용 구독자다.** UI 스크립트에서 상태를 바꾸지 않는다.

---

## 1. 캔버스 규격

### 1.1 확정값

| 항목 | 값 | 근거 |
|---|---|---|
| Reference Resolution | **1920 × 1080** | `Dev_Title.unity`·`Dev_Lobby.unity` 기존 캔버스와 동일 |
| UI Scale Mode | Scale With Screen Size | 동상 |
| Screen Match Mode | Match Width Or Height | 동상 |
| **Match** | **1.0 (Height)** | 모서리 앵커 HUD. 21:9에서 HUD 크기를 유지하고 가로만 벌린다 |
| Reference Pixels Per Unit | 100 | 기존과 동일 |
| Render Mode | Screen Space - Overlay | 기존과 동일 |
| 최소 지원 종횡비 | 16:9 | 4:3은 지원하지 않는다 |

**`Match = 0.5`를 쓰면 안 되는 이유.** 0.5는 가로·세로를 반씩 섞어 스케일을 정한다. 21:9에서 화면이 넓어지면 HUD가 **통째로 작아진다.** 깨지지는 않지만 값싸 보이고, 그쪽이 더 나쁘다. 세로 기준으로 고정하면 HUD 크기는 그대로이고 가운데 여백만 늘어난다 — 액션 게임에서 원하는 그림이 그것이다.

### 1.2 적용해야 하는 변경 2건

둘 다 **HUD 캔버스 신설 전에** 끝나야 한다. 이후에 바꾸면 이미 배치한 요소를 전부 다시 본다.

| # | 대상 | 현재 | 변경 후 | 주의 |
|---|---|---|---|---|
| A | `ProjectSettings/ProjectSettings.asset` | `defaultScreenWidth: 1024`<br>`defaultScreenHeight: 768`<br>`resizableWindow: 0` | `1920` / `1080` / `1` | **3인 공유 단일 파일.** 다른 두 명이 이 파일을 건드리지 않는 시점에 한 번에 처리한다 |
| B | `Dev_Title.unity`·`Dev_Lobby.unity`의 `CanvasScaler` | `m_MatchWidthOrHeight: 0.5` | `1` | **씬 파일이라 머지가 안 된다.** 바꾼 뒤 두 화면을 눈으로 확인한다 |

> `Assets/01.Scripts/06.UI/Combat/`은 현재 빈 폴더다. **HUD 캔버스는 아직 존재하지 않으므로** 새로 만드는 것은 위 값으로 처음부터 맞추면 되고 충돌 대상이 아니다.

---

## 2. 우상단 시간 슬롯

### 2.1 왜 슬롯 하나인가

**시계는 둘이고 서로 독립이다 ([시스템 기획 2.1](../01-system-design.md), 2026-09-06 확정).** 결계 축소는 **필드 시계 10:00 / 20:00 / 29:30**에 일어나고, 보스전은 별도 씬에서 **보스 시계 0:00 – 14:00**을 새로 돈다. 보스 시계는 필드 시계에 이어 붙지 않는다 — 25분에 들어가도 30분에 들어가도 그때부터 14분이다.

**두 시계는 씬이 다르므로 표시 구간이 절대 겹치지 않는다.** 그래서 위젯을 둘 만들 이유가 없고, 하나가 내용을 바꾸면 된다.

**교체 자체가 연출이다.** 결계 카운트다운이 꺼지고 보스 시간이 켜지는 순간, 플레이어는 숫자를 읽지 않아도 "뭔가 끝나고 뭔가 시작됐다"를 안다. 새 에셋 없이 얻는다.

**단 교체 시점은 고정 시각이 아니다.** 보스맵 게이트는 런 시작부터 열려 있고, 첫 진입자가 발생하면 30초 카운트다운 뒤 전원이 강제 이동한다(29:30까지 아무도 안 들어가면 서버가 자동으로 건다). **슬롯은 시각이 아니라 씬 전환에 반응해야 한다** — "26분이 되면 바꾼다"로 구현하면 일찍 들어간 파티에서 결계 카운트다운이 보스맵까지 따라 들어온다.

### 2.2 상태

| # | 상태 | 구간 | 숫자 | 부적 3장 | 색 |
|---|---|---|---|---|---|
| 1 | 대기 | 필드 씬, 축소까지 여유 | 다음 축소까지 남은 시간 | 남은 단계 수만큼 온전 | 한지 백색 |
| 2 | 경고 | 축소 10초 전 | 동상 | 다음 것이 깜빡임 | 주묵 붉은색 + 배너 |
| 3 | 축소 중 | 축소 진행 **30초** 동안 | 진행 잔여 | 해당 부적이 타들어감 | 붉은색 → 재 |
| 4 | 보스 | 보스 씬, 보스 시계 진행 중 | **보스 시계 14:00까지 남은 시간** | 숨김 | 한지 백색 |
| 5 | 보스 임박 | 남은 시간 03:00 이하 | 동상 | 숨김 | 주묵 붉은색 |

**상태 1은 3차 축소(29:30)를 마지막으로 더 이상 다음 축소가 없다.** 29:30 – 30:00 구간에는 남은 축소가 없으므로 슬롯은 **보스맵 이동 안내로 바꾼다** — 카운트다운이 00:00에 멈춰 서 있으면 고장으로 읽힌다.

**상태 3(축소 중)은 신설이다.** 기존 문서에는 [12.1의 "축소 10초 전 경고 배너"](../02-game-design.md)만 있었다. 배틀그라운드가 무서운 것은 카운트다운이 아니라 **자기장이 실제로 좁혀지는 동안 계속 보이는 것**이다. 배너만 있으면 결계는 이벤트가 아니라 알람이다.

**상태 5는 숫자를 바꾸지 않고 색만 바꾼다.** 남은 시간을 상시 표시하면 마지막 구간에 파티가 숫자만 쳐다보다 무너진다는 반론이 있었다. 색 전환이 그 반론에 대한 답이다 — 시간이 없다는 것을 숫자를 읽지 않고도 알게 만들어 **읽는 횟수를 줄인다.**

### 2.3 상시 카운트다운을 넣는 진짜 이유

예뻐서가 아니다. **"거점 하나 더 칠 시간이 있나"를 만들려고 넣는다.** 10초 전 배너만 있으면 파밍 우선순위를 세울 수 없어 파티가 안전하게만 움직이고, 필드 30분이 밋밋해진다.

**두 시계 모델에서 이 이유가 더 강해진다.** 게이트가 상시 열려 있어 필드에 시간 제한이 없는 대신, 파밍을 오래 할수록 점수의 남은 시간 보너스가 줄어든다([게임 디자인 11.1](../02-game-design.md), 기준 상한 44:00). **시간 비용이 강제 종료가 아니라 점수로 존재하므로, 그 비용이 화면에 보이지 않으면 아예 없는 것이 된다.**

### 2.4 미니맵과의 관계

배틀그라운드의 저 헤더는 미니맵에 붙어 있다. "1분 18초 남았다"와 "나는 원 밖이다"가 같이 읽혀야 의미가 있기 때문이다. 다만 **그믐의 결계는 매판 보스 지점으로 수렴하므로**([시스템 기획 1.2](../01-system-design.md)) "어디로"가 배틀그라운드만큼의 정보가 아니다.

**결론: 미니맵 위에 놓되, 의존하지 않는다.** 시간 슬롯은 자기 완결적 위젯이어야 하고, 미니맵이 W5로 밀려도 W3에 혼자 설 수 있어야 한다.

### 2.5 네트워크 계약

**서버는 "다음 축소 시각"과 "보스 시계 종료 시각"만 보내고, 각 클라이언트가 `NetworkTime.time`으로 빼서 표시한다.**

- **두 시각은 따로 산다.** 다음 축소 시각은 필드 씬에서만 유효하고, **보스 시계 종료 시각은 보스맵 진입이 확정되는 순간(강제 이동 시점)에 서버가 `진입 시각 + 14:00`으로 정해 내려보낸다.** 런 시작 시점에는 존재하지 않는 값이므로 미리 계산해 두면 안 된다.
- 매 초 SyncVar를 쏘지 않는다 — 대역폭 낭비이고 초가 튄다.
- 클라이언트가 로컬로 세지 않는다 — 세 사람 화면에서 초가 달라지면 "너 몇 초 남았어?"가 통하지 않는다. 코옵에서 바로 사고다.
- **기존 구현 패턴을 복사한다**: `ReviveController._spectateEndServerTime` ([ReviveController.cs](../../Assets/01.Scripts/02.Combat/Player/ReviveController.cs)).

---

## 3. 이미지 에셋

### 3.1 이미지로 만들 것과 아닌 것

**개수를 줄이는 것이 목적이 아니라, 바꿀 수 있는 상태로 두는 것이 목적이다.** 10~11월에 밸런싱과 배치 조정이 반복된다. 색과 도형이면 5분이고 스프라이트 세트면 재생성이다.

| 요소 | 이미지 | 대체 수단 |
|---|---|---|
| HP · 스태미나 · 신력 바 | 프레임 1장 (공용) | 채움은 `Image.fillAmount` |
| 패링 게이지 링 | 링 1장 | `Radial 360` |
| 보스 HP 바 | 위 프레임 재사용 | 스케일 |
| 스킬 · 아이템 슬롯 프레임 | 1장 | 상태는 틴트 |
| 장비 등급 테두리 4종 | 1장 (틴트 4색) | 일반·부적·영험·신물 |
| 직업 아이콘 3종 | **필요** | 검 · 활 · 방울 |
| 우상단 슬롯 프레임 | **필요** | |
| 결계 부적 표식 3종 | **필요** | 온전 / 타는 중 / 재 |
| 축소 경고 배너 | **필요** | |
| 보스 방향 마커 | **필요** | |
| 파티원 상태 플레이트 | **필요** | 초상 테두리 |
| 미니맵 프레임 (좌상단) | **필요** | 내부 지도는 셰이더 · 도형 |
| 보스 그로기 게이지 | 체력 바 프레임 재사용 | 높이만 줄여 쓴다. 채움은 `fillAmount` |
| 재화 아이콘 (노잣돈) | **필요** | |
| 결계 원 (월드) | 불필요 | 셰이더 |
| 미니맵 원 | 불필요 | 셰이더 · 도형 |
| 화면 페이드 | 불필요 | 알파 |
| 쿨다운 스윕 | 불필요 | `Radial 360` |
| 정산 · 관전 화면 | 불필요 | 레이아웃 + 텍스트 |
| 모든 숫자 · 한글 | 불필요 | **TMP** |

**숫자와 글자는 이미지로 만들지 않는다.** 생성 이미지에서 글자는 깨진다. `01:51`, `145`, `110m`은 전부 TMP이고 이미지는 **빈 프레임만** 받는다. 마찬가지로 **채워지는 부분(fill)도 이미지로 받지 않는다** — 런타임 값이다.

### 3.2 제작 규격 — 2× 원칙

화면 픽셀의 2배로 만들고 임포트에서 줄인다. 4K에서 뭉개지지 않고, UI 몇 장이라 용량은 문제되지 않는다.

| 에셋 | 화면 크기 (1080p) | 제작 크기 | 9-slice Border |
|---|---|---|---|
| 우상단 슬롯 프레임 | 360 × 72 | **720 × 144** | 좌우 32 |
| 결계 부적 표식 (3종) | 48 × 64 | **128 × 128** | — |
| 축소 경고 배너 | 960 × 120 | **1920 × 240** | 좌우 64 |
| 체력 계열 바 프레임 | 400 × 40 | **800 × 80** | 좌우 24 |
| 슬롯 프레임 (정사각) | 72 × 72 | **144 × 144** | 사방 20 |
| 아이콘류 | 64 × 64 | **128 × 128** | — |
| 미니맵 프레임 (축소) | 240 × 240 | **480 × 480** | — |
| 파티원 플레이트 | 220 × 56 | **440 × 112** | 좌우 24 |
| 보스 체력 바 프레임 | 900 × 56 | **1800 × 112** | 좌우 40 |

**임포트 설정**: Texture Type `Sprite (2D and UI)` · Mesh Type `Full Rect` · Compression `High Quality` · 9-slice는 Sprite Editor에서 Border 지정.

### 3.3 생성 AI로 뽑을 때

- **정사각(1024×1024)으로 생성하고 크롭한다.** 가로로 긴 프레임을 처음부터 요구하면 생성기가 여백을 채우려고 중앙에 장식을 넣고, 그러면 9-slice가 죽는다. 프롬프트에 **"중앙부는 균일한 스트립"**을 명시한다.
- **한 세션에 몰아서 뽑는다.** 장수가 늘어날수록 톤이 어긋나고, 어긋난 것은 붙이고 나서야 보인다.
- **퀄리티는 장수가 아니라 일관성에서 나온다.** 9장이 같은 붓에서 나온 것처럼 보이면 고급이고, 40장이 각자 예쁘면 잡탕이다.
- **생성 도구·날짜·프롬프트를 `docs/licenses/`에 기록한다.**

### 3.4 색 문법 준수

[직업 정체성 15.3](../class-identity-and-boss-2026-09-03.md)이 원본이다.

| 진영 | 색 | HUD 적용 대상 |
|---|---|---|
| 아군 · 신성 | 한지 백색 + 주묵 붉은색, 부적 문양 | **결계 표시 · 우상단 슬롯 · 체력 계열 · 무녀 치유** |
| 적 · 원귀 | 여우불 청록 `#3FE0C8` | 보스 방향 마커 · 적 관련 표시만 |

**결계 계열에 청록을 쓰면 결계가 적으로 보인다.** 배틀그라운드의 파란 점을 그대로 가져오면 정확히 이 실수가 난다. 이 프로젝트에서 제일 하면 안 되는 색 실수다.

**부적이 점보다 나은 이유**도 여기 있다. 점은 색으로만 말하므로 색 문법에 걸리지만, **부적은 형태로도 말한다.** 흰 한지에서 재로 변하면 색과 무관하게 "지나갔다"가 읽힌다.

---

## 4. 구현 규칙

1. **HUD는 읽기 전용 구독자다.** UI 스크립트에 `[Command]`·`[Server]`·상태 변경을 두지 않는다. 예외는 입력 UI(부활 홀드 등)이고 그것도 기존 파이프라인 호출만 한다.
2. **위젯은 값만 받는다.** `SetValue(float current, float max)` 형태이고, 위젯이 `PlayerStatus`를 직접 참조하지 않는다. 바인딩은 루트 하나가 전담한다.
3. **`Update()`에서 폴링하지 않는다.** `PlayerStatus.HealthChanged` 등 기존 이벤트를 구독한다. 구독과 해제를 1:1로 맞춘다 — 씬 전환 시 남은 구독이 이 프로젝트에서 가장 잦은 UI 결함이다.
4. **수치는 SO로 뺀다.** 바 보간 속도, 색 전환 임계(`03:00`), 경고 표시 시간(`10초`) 전부 코드 상수 금지.
5. **씬을 직접 고치지 않는다.** `CombatHud.prefab`으로 만들고 각자의 `Dev_*.unity`에서 확인한다.

---

## 5. 미결 항목

| # | 항목 | 왜 지금 못 정하나 |
|---|---|---|
| 1 | ~~축소 진행 시간~~ | ✅ **해소.** [시스템 기획 2.1](../01-system-design.md)이 "축소 연출은 30초에 걸쳐 진행하고 10초 전 경고 UI"로 확정했고, [게임 디자인 5.2](../02-game-design.md)의 기준 원 규칙도 30초 보간을 전제한다. 상태 3의 길이는 **30초**다 |
| 2 | 미니맵을 W3에 함께 세울지 | 2.4의 결론상 시간 슬롯은 독립이므로 **차단 요소는 아니다** |
| 3 | 부적 3장의 배치 | 슬롯 우측인지 하단인지. 실물 이미지가 나온 뒤 눈으로 결정 |

---

## 6. 검증

| 수준 | 대상 | 기준 |
|---|---|---|
| L1 | 전 변경 | 컴파일 에러 0, 신규 경고 없음 |
| L2 | 상태 전환 | 상태 5개를 에디터에서 강제 전환해 각각 1회 관찰. 16:9 · 21:9 · 16:10 세 종횡비에서 우상단이 잘리지 않는 것 확인 |
| L3 | 시간 동기 | **3인 접속 상태에서 같은 순간의 숫자가 일치**하는지 확인. 런 도중 접속한 클라이언트가 올바른 잔여 시간을 표시하는지 확인 |

**L3가 이 문서에서 가장 중요하다.** 단일 플레이에서 타이머는 항상 맞다. 틀리는 것은 세 사람이 붙었을 때이고, 특히 **런 도중 접속한 클라이언트**다.

---

## 7. 생성 프롬프트 (2026-09-07 추가)

> 이 장은 **이미지를 실제로 뽑기 위한 입력**이다. 3.2의 제작 규격과 3.4의 색 문법을 프롬프트 문장으로 옮긴 것이고, 규격이 바뀌면 여기도 같이 고친다.

### 7.0 공통 규칙 — v3 (2026-09-07 2차 개정)

> **v2를 폐기한다.** v1이 밋밋하다는 지적에 장식으로 답한 것이 잘못이었다. 결과는 여우 머리와 술과 발광이 붙은 **양산형 중화풍 UI**였고, 화면에서 차지하는 면적도 과했다.
>
> 밋밋함의 해법은 장식이 아니라 **생각**이다. 이 프로젝트에는 이미 쓸 수 있는 생각이 있다 — **먹과 오방색**이다.

#### v2가 틀린 지점

| v2에 내가 쓴 것 | 왜 틀렸나 |
|---|---|
| `NINE FOX TAILS breaking the outline` | HUD는 일러스트가 아니다. 실루엣이 프레임 밖으로 나가면 게임 화면을 가린다 |
| `strong beveled highlight` · `wet specular` | 입체 금속 테두리가 곧 "중국 양산형"의 문법이다. 3D 베벨을 넣는 순간 그 인상이 된다 |
| `tarnished brass`, `najeon inlay` 남발 | 재질을 늘릴수록 화려해질 뿐 한국적이 되지 않는다 |
| 크기 규율 없음 | 그래서 전부 거대하게 나왔다. **HUD는 작아야 한다** |

**참조한 화면(사용자 제시)의 특징**은 정반대다 — 얇은 선, 반투명한 어두운 판, 작은 원형 아이콘, 금색 헤어라인 한 줄. **장식이 거의 없는데 싸구려로 안 보인다.** 절제가 곧 완성도다.

```
STYLE BASE v3 (prepend to every prompt):
Minimal Korean ink-painting (수묵) game UI asset. Dark fantasy, moonless night.

CONSTRUCTION — flat and thin, never sculpted:
A translucent near-black panel (85% opacity) with a SINGLE HAIRLINE STROKE as its
border, drawn like one confident brush stroke — slightly uneven thickness, a dry
brush break somewhere along its length, a tiny ink pool where the stroke ends.
NO bevel, NO 3D metal rim, NO embossing, NO specular highlight, NO drop shadow,
NO glow. The asset is FLAT GRAPHIC DESIGN, not a rendered object.

COLOR — obangsaek (오방색), the five directional colors of yin-yang and wu-xing:
ink black (#1A1A1A) as the ground, hanji off-white (#E8E0CE) for surfaces,
and ONE accent per element chosen from: 청 blue-green (#2E6E5B), 적 cinnabar
(#B33A2E), 황 gochang yellow (#C9A227), 백 white, 흑 black.
Each element uses ONE accent only. Never more than one.

STROKE ANATOMY — this is what separates real 수묵 from a paint smear:
every stroke has three phases. 입필: it begins with a dark POOLED BLOB where the
loaded brush first touches. 갈필: mid-stroke the brush runs dry, leaving thin WHITE
STREAKS scratched through the ink (비백, flying white). 출필: it ends TAPERED to a
fine point, lifting off the paper, never a blunt cut.

TEXTURE — the only richness allowed:
visible hanji fiber grain, and ink BLEEDING outward into the paper (선염) along one
side of each stroke, as if brushed while the paper was still damp.

Transparent background, no text, no letters, no numbers, no tassels, no creatures,
no chains, no rivets, no ornamental filigree, no watermark.
```

**`no tassels, no creatures, no filigree`를 명시적으로 금지어에 넣었다.** v1·v2가 붙인 것이 정확히 이 셋이고, 안 적으면 생성기가 반드시 다시 붙인다.

### 7.0.1 크기 규율 — HUD는 작아야 한다

v2가 실패한 가장 실질적인 이유다. **화면 면적을 먼저 정하고 그 안에서 디자인한다.**

| 요소 | 1080p 화면 크기 | 화면 폭 대비 |
|---|---|---|
| 우상단 시간 | 180 × 40 | 9% |
| 좌상단 미니맵 | 180 × 180 | 9% |
| 체력 계열 바 | 320 × 14 | 17% · **높이 14px** |
| 파티 플레이트 | 180 × 40 | 9% |
| 보스 바 | 720 × 28 | 37% · **높이 28px** |

**3.2의 제작 크기(2× 원칙)는 이 값의 두 배로 다시 계산한다.** 이전 표는 v2 기준이라 전부 과하다 — 체력 바가 400×40이었는데 실제로는 320×14면 충분하다.

**보스 바조차 높이 28px다.** 참조 화면의 보스 바가 그 정도이고, 그래도 충분히 읽힌다. 커야 중요해 보이는 것이 아니라 **위치(상단 중앙)가 중요도를 만든다.**

### 7.0.2 계층 — 장식이 아니라 색으로 가른다

v2는 계층을 장식 밀도로 만들려 했다. **오방색을 쓰면 색 하나로 갈린다.**

| 등급 | 대상 | 오방색 | 형태 |
|---|---|---|---|
| **1급** | 보스 바 | **흑 + 청록 획 하나** | 다른 것보다 폭만 넓다. 장식은 없다 |
| **2급** | 시간 · 미니맵 | **황(#C9A227) 헤어라인** | 획 하나 + 작은 먹점 |
| **3급** | 파티 · 체력 | **백 한지 + 적 획** | 가장 얇다. 획도 반만 그린다 |

**획을 반만 그리는 것**이 수묵의 문법이다. 테두리를 사방으로 두르지 않고 위와 왼쪽만 긋고 나머지는 열어 둔다. 그러면 절제된 동시에 심심하지 않다.

### 7.0.4 한국성 신호 넷 — 장식을 늘리지 않고 더한다 (v3.1)

v3 시트는 절제와 크기가 맞았으나 *"살짝 더 한국적이었으면"* 이라는 평가를 받았다.
**획이 붓이 아니라 마커 자국처럼 보였고, 오방색이 색으로만 있고 형태로는 없었다.**

장식을 늘리면 v2로 되돌아간다. 대신 **작지만 한국 회화에만 있는 신호 넷**을 넣는다.

| 신호 | 어디에 | 왜 이것인가 |
|---|---|---|
| **붓획 3단계**(입필·갈필·출필) | 모든 획 | 마커와 붓을 가르는 것은 굵기가 아니라 **시작·중간·끝이 다르다는 것**이다. 시작은 먹이 고이고, 중간은 갈필로 흰 줄이 긁히고(비백), 끝은 뾰족하게 빠진다 |
| **오방색 5점** | 미니맵 방위 | 동청·서백·남적·북흑 **+ 중앙 황**. 4점이면 나침반이지만 **5점이면 오방(五方)** 이다. 음양오행을 형태로 넣는 가장 작은 방법 |
| **문살 격자** | 파티 아이콘 소켓 | 한국 창호의 살 구성. 사각 테두리를 격자로 바꾸는 것뿐인데 출처가 분명해진다 |
| ~~낙관 인장~~ | ~~시간 옆~~ | **2026-09-07 폐기.** 실물에서 한자가 들어갔고, 도장은 화면 구석에서 정보가 아니라 잡음이었다. 나머지 셋으로 한국성은 충분히 선다 |

**넷 다 크기가 작고 개수가 정해져 있다.** 이것이 v2와 다른 점이다 — v2는 장식의 밀도를 올렸고, 여기서는 **의미가 있는 표식 넷을 정확히 한 번씩** 쓴다.

한지 결과 선염(번짐)도 STYLE BASE에 넣었다. 종이에 스민 자국은 화면에서 보일 듯 말 듯하지만, 없으면 획이 스티커처럼 뜬다.

---

### 7.0.3 음양오행을 쓰는 이유

*"한국적"*을 재료(한지·놋쇠)로 풀면 중화권 UI와 구분되지 않는다. **재료는 겹치지만 색 체계는 겹치지 않는다.**

오방색은 방위·오행과 묶인 규칙이라 **의미를 부여할 수 있다** — 그믐의 기존 색 문법(아군=한지 백색+주묵, 적=여우불 청록)이 이미 오방색의 부분집합이다. 확장하면:

| 색 | 오행 | HUD 적용 |
|---|---|---|
| 흑(黑) 북 | 수(水) | 모든 패널의 바탕. 그믐 밤 |
| 백(白) 서 | 금(金) | 한지 면 · 파티 · 체력 |
| 적(赤) 남 | 화(火) | 주묵 부적 획 · 아군 표시 |
| 청(靑) 동 | 목(木) | 여우불 · 적 · 보스 |
| 황(黃) 중앙 | 토(土) | 시간 · 미니맵 등 "중심" 정보 |

**우상단 시간과 좌상단 미니맵이 왜 황색인가**에 답이 생긴다 — 중앙(土)은 방위를 매기는 기준이고, 시간과 지도가 정확히 그 역할이다. 색이 근거를 갖는 순간 화면이 임의로 보이지 않는다.

### 7.1 우상단 시간

180 × 40 화면 · **황(黃) 계열** · 2급

```
A minimal translucent dark panel for a game timer, very wide and short (4.5:1).
Flat graphic design. Its border is a SINGLE THIN BRUSH STROKE in muted gold
(#C9A227) that runs along the TOP EDGE AND DOWN THE LEFT SIDE ONLY —
the right and bottom stay open, unbordered.
The stroke has uneven thickness and one dry-brush break.
A small ink dot terminates the stroke at the upper left.
Interior empty and near-black.
No seal, no stamp, no bevel, no metal, no glow, no ornament.
```

---

### 7.2 좌상단 미니맵

180 × 180 화면 · **황(黃) 계열** · 2급
Tab 확대본은 같은 프레임을 스케일한다.

```
A minimal circular frame for a game minimap, drawn as ONE SINGLE INK BRUSH RING
(일필휘지) in muted gold (#C9A227) on transparent background — one continuous
sweep of the brush, thicker where it starts, thinning and breaking where it ends,
NOT a perfect geometric circle.
The center is COMPLETELY EMPTY AND TRANSPARENT.

FIVE tiny dots mark the obangsaek directions and are the only color in the piece:
blue-green at the east (right), white at the west (left), red at the south (bottom),
black at the north (top), and one yellow dot at the very center-bottom of the ring.
Each dot is a single wet brush touch that bleeds slightly into the paper.
Nothing else. Flat, no metal ring, no rivets, no bevel, no glow.
```

**일필휘지(한 붓에 그린 원)** 가 핵심이다. 완벽한 원을 요구하면 나침반 금속 링이 나온다.

---

### 7.3 좌하단 자원 (HP · 스태미나 · 신력) — 트랙과 채움을 나눈다

320 × 44 화면(3줄 합계) · 3급

**한 자원당 이미지 두 장이다.** `Image.fillAmount`는 **한 스프라이트를 왼쪽부터 잘라 보여주는 것**이라, 비었을 때 보이는 판(트랙)과 차오르는 획(채움)을 같은 그림으로 쓸 수 없다.

| 층 | 파일 | 무엇 | Image Type |
|---|---|---|---|
| 뒤 | `Bar_Track.png` | 비었을 때 보이는 자리. **1장으로 3자원 공용** | `Simple` (또는 `Sliced`) |
| 앞 | `Bar_Fill_HP.png` 등 | 차오르는 획. **자원마다 1장** | **`Filled` · Horizontal · Origin Left** |

| 줄 | 자원 | 채움 색 | 굵기 |
|---|---|---|---|
| 1 | HP | **적(赤) 火** `#B33A2E` | 14px |
| 2 | 스태미나 | **백(白) 金** `#E8E0CE` | 10px |
| 3 | 신력 | **황(黃) 土** `#C9A227` | 8px |

---

#### 7.3.1 트랙 (비었을 때) — 1장

```
A single horizontal EMPTY GAUGE TRACK for a game HUD, extremely thin (ratio 23:1),
flat Korean ink-painting style on transparent background.

It is a DESATURATED DARK INK STROKE — charcoal grey-black, clearly readable as
"the place where a bar would be" but visibly drained of color.
Full brush anatomy: a soft pooled blob at the LEFT end, dry-brush white streaks
(비백) through the middle, tapering to a fine point at the RIGHT end.
Faint hanji paper fiber inside it.

No color accent, no fill, no frame, no bevel, no metal, no glow.
```

**트랙은 회색이다.** 색을 넣으면 비어 있을 때도 자원이 남은 것처럼 보인다.

---

#### 7.3.2 채움 (차오르는 획) — 자원마다 1장

⚠️ **채움 이미지는 오른쪽 끝을 뾰족하게 만들지 않는다.** `fillAmount`가 오른쪽부터 잘라내므로, 뾰족한 꼬리는 **가득 찼을 때만 보이고 나머지 구간에서는 통째로 사라진다.** 그러면 100%와 99%가 완전히 다른 모양이 된다.

```
A single horizontal GAUGE FILL stroke for a game HUD, extremely thin (ratio 23:1),
flat Korean ink-painting style on transparent background.

Solid cinnabar red (#B33A2E) wet ink.
The LEFT end has a POOLED BLOB where the loaded brush landed.
From there the stroke runs at a CONSISTENT, UNBROKEN HEIGHT all the way to the
right edge of the image and is CUT OFF FLAT there — it does NOT taper, does NOT
thin out, does NOT end in a point. The right edge must look like the stroke
continues past the frame.
Dry-brush white streaks (비백) run along its length as texture only, never
breaking the stroke into separate pieces.

No frame, no outline, no bevel, no glow, no background.
```

**색만 바꿔 3장을 뽑는다** — 스태미나는 `hanji off-white (#E8E0CE)`, 신력은 `muted gold (#C9A227)`. 굵기 차이는 임포트에서 세로로 줄여 만든다(원본은 같은 비율).

---

#### 7.3.3 왜 이렇게 나누나

| 안 나눴을 때 | 증상 |
|---|---|
| 트랙 없이 채움만 | HP가 0이 되면 **바가 화면에서 사라진다.** 죽었는지 UI가 꺼진 건지 구분이 안 된다 |
| 채움에 뾰족한 꼬리 | 99%에서 꼬리가 잘려 **모양이 급변**한다 |
| 트랙에 색 | 비어 있어도 자원이 남아 보인다 |
| 획이 중간에 끊긴 채움 | `fillAmount` 경계가 끊긴 자리에 걸리면 **바가 두 동강 난 것처럼** 보인다 |

**임포트**: 두 장 다 `Sprite (2D and UI)` · `Full Rect`. 채움 스프라이트만 `Image Type = Filled`, `Fill Method = Horizontal`, `Fill Origin = Left`.

**같은 규칙이 파티 플레이트(7.5)와 보스 바(7.6)에도 그대로 적용된다.** 세 곳이 같은 트랙·채움 문법을 쓰면 나중에 색만 갈아 끼워도 톤이 안 깨진다.

---

### 7.4 직업 아이콘 3종 (검 · 활 · 방울)

64 × 64 화면 · 128 × 128 제작 · **문살 소켓 안에 들어간다**(7.5)

**세 장을 한 세션에서 연달아 뽑는다.** 따로 뽑으면 획 굵기와 여백이 어긋나 파티창에 세 줄로 쌓였을 때 바로 티가 난다.

| 아이콘 | 직업 | 액센트 | 근거 |
|---|---|---|---|
| 검 | 차사 | **적(赤)** `#B33A2E` | 패링으로 그로기를 여는 정면의 힘 |
| 활 | 신궁 | **백(白)** `#E8E0CE` | 정밀 사격, 거리를 두는 냉정함 |
| 방울 | 무녀 | **황(黃)** `#C9A227` | 신력·공명, 중앙에서 파티를 잇는다 |

**액센트는 획 하나뿐이다.** 아이콘 전체를 색칠하면 문살 소켓 안에서 시끄러워지고, 세 개가 나란히 있을 때 색 덩어리 셋으로만 읽힌다.

---

#### 7.4.1 공통 프롬프트 (모든 아이콘에 붙인다)

```
A single minimal Korean ink-brush pictogram (수묵) for a game UI icon,
centered on a transparent square canvas with generous even margin on all four
sides — it will be placed inside a small window frame, so nothing may touch the edges.

Drawn with ONLY 3-5 confident brush strokes. Black sumi ink.
Each stroke shows real brush anatomy: pooled where it starts, dry-brush white
streaks (비백) through the middle, tapering where it lifts off.
EXACTLY ONE accent stroke in [ACCENT COLOR] — a single short mark, not a fill.

Flat graphic design. No plate, no frame, no circle behind it, no shading,
no gradient, no glow, no outline, no perspective, no background.
No text, no letters, no numbers.
All three icons in this set must share the same stroke weight and the same
visual mass, so they read as siblings.
```

#### 7.4.2 개별 지시

```
[검 · 차사 · accent = cinnabar red #B33A2E]
A straight single-edged Korean ritual sword (환도) seen edge-on, blade vertical,
point upward. Two strokes for the blade, one for the guard, one for the grip.
The accent is a single short red mark at the grip wrap.

[활 · 신궁 · accent = hanji off-white #E8E0CE]
A traditional Korean recurve bow (각궁) seen from the side, unstrung, its double
curve clearly readable in one continuous sweeping stroke. One thin straight
stroke for the string. The accent is a single short off-white mark at the grip.

[방울 · 무녀 · accent = muted gold #C9A227]
A shaman's hand-bell cluster (무당 방울): three small bells hanging from one ring
above a short handle. Simplify ruthlessly — three small ovals, one arc, one
vertical stroke. The accent is a single short gold mark on the handle wrap.
```

⚠️ **방울에 `Simplify ruthlessly`를 넣은 이유가 있다.** 실물 무당 방울은 잔가지가 많아 그대로 그리면 혼자 복잡하고 얇아진다. 세 개가 형제로 보이려면 방울만 단순화 강도를 높여야 한다.

#### 7.4.3 상태는 이미지로 만들지 않는다

3.1의 원칙대로다. 아이콘은 **직업당 1장뿐**이고 상태는 코드가 만든다.

| 상태 | 수단 |
|---|---|
| 평시 | 원본 |
| 쓰러짐 | 알파 40% + 채도 0 |
| 말하는 중 · 강조 | 흰색 틴트 |
| 사망 확정 | 알파 20% |

**쓰러짐 상태를 별도 이미지로 만들지 않는다.** 3종 × 상태 4개 = 12장이 되고, 그중 어느 것도 다시 뽑을 수 없게 된다(톤이 어긋난다).

#### 7.4.4 뽑는 순서

**방울을 가장 먼저 뽑는다.** 셋 중 제일 복잡해서 여기서 정해진 단순화 강도가 나머지 둘의 기준이 된다. 검을 먼저 뽑으면 검 기준으로 굵기가 잡히고, 방울이 그 굵기를 못 맞춘다.

`방울 → 활 → 검` 순으로, **같은 세션에서 연속으로** 뽑는다.

---

### 7.5 파티원 플레이트 (아이콘 · 이름 · 자원 3종)

180 × 52 화면 · 3급 · 세로 3개
**이름 한 줄 아래에 자원 3줄이 들어간다.** 7.3과 같은 색·같은 서열이다.

```
A compact party member row for a co-op HUD, flat ink-painting style,
built as TWO STACKED ZONES sharing one left edge:

ZONE 1 (top, taller) — identity:
  On the far left, a SMALL SQUARE Korean 문살 window lattice outline in dry ink,
  sized for a class icon, interior fully transparent.
  To its right, a plain translucent near-black name panel with a single hanji
  off-white hairline along its TOP EDGE ONLY. Empty — no text.

ZONE 2 (directly beneath, shorter) — three resource bars:
  THREE VERY THIN horizontal strokes stacked with tiny gaps, all starting at the
  same left edge as the name panel, each a different thickness and color:
  thickest cinnabar red (#B33A2E), then hanji off-white (#E8E0CE),
  then thinnest muted gold (#C9A227).
  Each is a single brush stroke: pooled at the left, dry-brush streaked, tapered right.

The whole row is small and quiet. No portrait, no face, no tassel, no metal,
no rivets, no frame around the outside. Interiors empty.
```

⚠️ **`sharing one left edge`가 중요하다.** 이름 줄과 게이지 줄의 왼쪽이 안 맞으면 세 명분이 쌓였을 때 화면이 흐트러진다.

---

### 7.6 보스 바 (체력 + 그로기 게이지)

720 × 34 화면 · 1급 · **흑 바탕 + 청(靑) / 백(白)**

**두 채널의 색이 다른 데는 이유가 있다.**

| 채널 | 무엇 | 오방색 | 굵기 | 의미 |
|---|---|---|---|---|
| 위 | 보스 체력 | **청(靑) 여우불** `#3FE0C8` | 20px | 적의 색 (3.4) |
| 아래 | 그로기 게이지 | **백(白) 한지** `#E8E0CE` | 8px | **우리가 채우는 것**이라 아군 색이다 |

그로기는 보스의 상태가 아니라 **셋이 함께 만든 성과**다. 적 색으로 칠하면 그 뜻이 사라진다.

```
A wide boss bar for a game HUD, flat ink-painting style, ratio about 21:1.

TWO CLEARLY SEPARATE STACKED CHANNELS, sharing the same left and right edges,
with a visible gap between them so they never read as one bar:

- UPPER CHANNEL — noticeably TALLER. A fox-fire teal (#3FE0C8) brush stroke
  runs along its top edge, wet and pooled at the left, drying into white streaks
  (비백) toward the right, tapering to a point.
- LOWER CHANNEL — clearly THINNER, roughly one third the height. Its stroke is
  hanji off-white (#E8E0CE), the same brush language but quieter.

Both sit on a translucent near-black ground with visible hanji paper fiber.
At each end, ONE tiny curved ink flourish suggesting a fox tail — a single
stroke that STAYS INSIDE the bar's bounding box.
Both channels are EMPTY — fills are drawn by the engine.
No fox head, no creature, no veil, no glow bloom, no metal, no tassels.
```

⚠️ **`with a visible gap between them so they never read as one bar`** — v3.1에서 두 채널이 한 줄로 합쳐져 나온 것을 막는 문장이다.

---

### 7.7 전체 감 잡기용 시트

**에셋이 아니다.** 배치와 톤 확인용이고, 여기서 잘라 쓰지 않는다.

```
A single 16:9 game UI layout sheet for a Korean ink-painting style dark fantasy
co-op action game, on a flat dark grey background.

CRITICAL — THE UI IS SMALL AND SPARSE. All elements together cover less than
15% of the frame. The center of the screen is completely empty. Nothing is large.

Layout:
- TOP RIGHT: a tiny wide timer panel, gold hairline on top and left edges only.
- TOP LEFT: a small circle drawn as one ink brush ring, gold, hollow center.
- TOP CENTER: a boss bar made of TWO SEPARATE STACKED CHANNELS with a visible gap
  between them — a taller teal one above, a clearly thinner off-white one below.
  One tiny curved ink flourish at each end, staying inside its bounds.
- LEFT, STACKED: three party rows. Each row has a 문살 lattice icon square, a name
  panel beside it, and BENEATH the name THREE very thin resource strokes of
  descending thickness — red, then off-white, then gold.
- BOTTOM LEFT: the same THREE stacked resource bars, larger — thickest red on top,
  thinner off-white, thinnest gold at the bottom.

Everything is FLAT GRAPHIC DESIGN made of single brush strokes on translucent
near-black panels. Obangsaek palette: ink black ground, hanji off-white surfaces,
one accent color per element (gold, red, or teal — never mixed).

KOREAN SIGNALS — small, but they carry the identity:
- Every stroke shows real 수묵 anatomy: a pooled blob where it starts, dry-brush
  white streaks (비백) through its middle, a tapered lift-off at its end.
- The minimap ring carries FIVE obangsaek dots at the directions.
- The party icon sockets are 문살 window lattices, not plain squares.
- Visible hanji paper fiber, ink bleeding into the paper along one side of strokes.

NO bevels, NO 3D metal, NO glow, NO tassels, NO creatures, NO filigree,
NO rivets, NO chains. No text, no letters, no numbers, no fills.
Restrained, quiet, confident — like a Korean ink painting, not an ornate MMO.
```

**`covers less than 15% of the frame` · `the center is completely empty`** 를 넣은 것이 크기 문제의 직접 처방이다.

---

### 7.8 뽑는 순서

1. **7.7 시트 먼저.** 톤과 배치가 마음에 들 때까지 시트만 반복한다. 여기서 확정된 문장을 7.1~7.6의 STYLE BASE에 반영한다
2. 확정된 톤으로 **7.1 → 7.3 → 7.5 → 7.6** (프레임 4장). 프레임끼리 두께와 재질이 맞는지 먼저 본다
3. **7.4 아이콘 3종을 한 세션에 연달아**
4. **7.2 미니맵**은 마지막. 가운데를 비우는 것이 제일 잘 실패해서, 다른 것이 다 나온 뒤 톤을 맞춰 뽑는 편이 재시도가 싸다

각 장은 정사각 1024×1024로 생성하고 3.2의 제작 크기로 크롭·리사이즈한다(3.3).

### 7.9 남는 문제

| 항목 | 지금 | 정해야 하는 것 |
|---|---|---|
| 미니맵 확대(Tab) | 프레임 재사용 + 스케일로 잡아 뒀다 | 확대본이 화면을 얼마나 덮는지. 전체 화면인지 절반인지는 눈으로 정한다 |
| 그로기 게이지 색 | 미정 | 청록은 보스 프레임에 이미 쓴다. 게이지 채움까지 청록이면 체력과 안 갈린다 |
| 신력 표시 | 좌하단 3번째 바로 잡혀 있다 | 신력 자체의 존폐가 검토 중이라([2026-09-07 논의]) 확정 전까지 프레임만 만들고 배치는 미룬다 |
