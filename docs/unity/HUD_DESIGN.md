# 인게임 HUD 기술 설계

> 상태: **채택된 HUD 규격과 현행 구현 계약.** `HUDController`, 파티 HUD, 미니맵, 시간 슬롯, 무기 능력 HUD와 `RunHUD` 프리팹이 존재한다.
> 우상단 숫자를 남은 시간으로 변경(2026-09-14, 2.2)
> / **필드 구간을 카운트업으로 되돌림 · 보스 구간만 카운트다운(2026-09-20, 2.2)**
> / **결계 경고·축소 구간의 숫자 색 전환 구현(2026-09-20, 2.2 상태 2~3)**
> / **ESC 우선순위에 펼친 미니맵을 넣음 — 한 번 눌러 지도만 접힌다(2026-09-22, 7.2)**
> / **관리 창 좌측 레일이 사라지면서 장비 양도 입구를 배낭 페이지의 버튼으로 옮김(2026-09-22)**
> / 7.1 시간 · 7.2 미니맵 · 7.3 좌하단 자원 · 7.5 파티 플레이트 · 7.6 보스 바를
>   `Dev_HUD.unity` 캔버스에 조립(2026-09-13)
> / 보스 바는 에셋 비율이 규격과 달라 `Ends` 층을 뺀 상태다 — 7.6의 경고 참고
> / `HudTimerController`는 필드 경과 카운트업과 보스 잔여 카운트다운을 모두 읽고, 결계 경고·보스 임박의
>   색 전환까지 구현되어 있다. **광폭화 구간만의 색 전환은 아직 없다** — 보스 임박(남은 03:00)과
>   광폭화 시작(`_enrageStartSeconds`)이 별개 신호인데 숫자는 전자만 본다
> 대상: `Assets/01.Scripts/06.UI/Combat/`, HUD 캔버스, UI 이미지 에셋 일체

UI는 평가자가 보는 유일한 층이다. 로직이 정확해도 화면에 없으면 없는 기능이다 — UI 최소본을 앞당긴 이유가 이것이다.

이 문서는 HUD를 실제로 만들 수 있는 수준까지 규격을 확정한다. 화면 11종의 목록과 담당은 이 문서 2장, 폴더 배치는 [프로젝트 구조 4.6](../04-architecture.md), 색 문법은 [직업 정체성 15.3](../class-identity-and-boss-2026-09-03.md)이 원본이다.

**소유.** 이미지 에셋 제작은 팀장(@AidenKim923, 아트), UI 구현은 @kjw1237이다 — `.github/CODEOWNERS`의 소유 기준 그대로다. 그래서 이 문서의 3장(에셋 규격)은 **넘길 때의 계약**이다. 규격이 맞지 않은 채로 넘어가면 받는 쪽이 이미지를 다시 만져야 하고, 그 순간 한 사람 일이 두 사람 일이 된다.

---

## 0. 이 문서가 확정하는 핵심 결정

1. **Reference Resolution은 1920×1080이다.** 새로 정한 값이 아니라 `Dev_Title`·`Dev_Lobby`의 기존 캔버스가 이미 쓰고 있는 값이다.
2. **런타임 화면은 16:9 콘텐츠 영역에 맞춘다.** CanvasScaler와 AspectContent는 [창 표시 계약](WINDOW_PRESENTATION.md)을 따른다.
3. **현재 기본 창은 1280×720, 크기 변경 가능이다.** 창 크기와 HUD의 1920×1080 기준 해상도는 다른 값이다.
4. **우상단은 위젯 두 개가 아니라 슬롯 하나에 상태 다섯이다.** 결계(필드 시계)와 보스(보스 시계)는 씬이 다르고 표시 구간이 겹치지 않으므로 같은 자리를 순서대로 쓴다.
5. **보스전 숫자는 남은 시간이고, 필드 숫자는 쓴 시간이다 (2026-09-20).** 보스는 시간이 다하면 런이 끝나므로 마감이 실재하고, 필드는 게이트가 상시 열려 있어 마감이 없다. 없는 마감을 내려가는 숫자로 만들면 거짓말이 된다.
6. **결계 단계는 숫자가 아니라 부적 3장으로 표시한다.** 단계가 3개뿐이라 세는 것이 아니라 보는 것이다.
7. **결계 계열에 여우불 청록을 쓰지 않는다.** 청록은 적의 색이다. 결계는 저승이 친 신성이므로 한지 백색 + 주묵 붉은색이다.
8. **시간 숫자는 서버가 보낸 "시각" 하나에서 각 클라이언트가 계산한다.** 매 초 SyncVar를 쏘지 않고, 클라이언트가 로컬로 세지도 않는다.
9. **이미지로 만들 것과 코드·색으로 만들 것을 가른다.** 11화면에서 고유 이미지는 9장 내외다. 나머지는 재사용이거나 `fillAmount`·틴트·알파다.
10. **HUD는 읽기 전용 구독자다.** UI 스크립트에서 상태를 바꾸지 않는다.

---

## 1. 캔버스 규격

### 1.1 현재 화면 계약

HUD 디자인 기준 해상도는 1920×1080이다. 실제 기본 창은 1280×720이며 크기를 바꿀 수 있다. `FixedAspectViewport`와 `AspectContent`가 16:9 콘텐츠 영역을 유지한다. CanvasScaler는 현재 Expand 경로를 따르므로 옛 Match=1 강제 변경 계획을 적용하지 않는다.

화면 바깥 여백과 입력 좌표, HUD 모서리·팝업·동적 로비 Canvas를 함께 확인한다. [창 표시](WINDOW_PRESENTATION.md)가 현재 코드·설정 계약이다. `06.UI/Combat`의 HUD·파티·미니맵·타이머·능력 표시와 RunHUD 프리팹을 재사용한다.

## 2. 우상단 시간 슬롯

### 2.1 왜 슬롯 하나인가

**시계는 둘이고 서로 독립이다 ([시스템 기획 2.1](../01-system-design.md), 2026-09-06 확정).** 결계 축소는 **필드 시계 10:00 / 20:00 / 29:30**에 일어나고, 보스전은 별도 씬에서 **보스 시계 0:00 – 14:00**을 새로 돈다. 보스 시계는 필드 시계에 이어 붙지 않는다 — 25분에 들어가도 30분에 들어가도 그때부터 14분이다.

**두 시계는 씬이 다르므로 표시 구간이 절대 겹치지 않는다.** 그래서 위젯을 둘 만들 이유가 없고, 하나가 내용을 바꾸면 된다.

**교체 자체가 연출이다.** 결계 카운트다운이 꺼지고 보스 시간이 켜지는 순간, 플레이어는 숫자를 읽지 않아도 "뭔가 끝나고 뭔가 시작됐다"를 안다. 새 에셋 없이 얻는다.

**단 교체 시점은 고정 시각이 아니다.** 보스맵 게이트는 런 시작부터 열려 있고, 첫 진입자가 발생하면 30초 카운트다운 뒤 전원이 강제 이동한다(29:30까지 아무도 안 들어가면 서버가 자동으로 건다). **슬롯은 시각이 아니라 씬 전환에 반응해야 한다** — "26분이 되면 바꾼다"로 구현하면 일찍 들어간 파티에서 결계 카운트다운이 보스맵까지 따라 들어온다.

### 2.2 상태

**슬롯 하나가 구간에 따라 방향을 바꾼다 (2026-09-20 변경).** 필드는 00:00에서 30:00까지 올라가고, 보스는 14:00에서 내려간다.

2026-09-14에는 필드도 내려가게 했으나 되돌렸다. **필드에는 강제 종료가 없다.** 게이트가 상시 열려 있어 30:00은 실패가 아니라 보스맵 이동 트리거이고, 시간은 점수로만 비용이 된다. 내려가는 숫자는 없는 마감을 있는 것처럼 보이게 한다. 올라가는 숫자는 "얼마나 썼나"를 그대로 말하고, 그것이 점수의 시간 보너스와 같은 축이다.

이전에는 단계마다 "다음 축소까지"를 세었으나 그 숫자는 생존만 다루고 **시간 비용을 보여 주지 못했다.** 축소 경고는 이미 배너와 부적 3장이 맡고 있어 숫자까지 쓸 필요가 없다. 이 판단은 그대로 유효하다.

| # | 상태 | 구간 | 숫자 | 부적 3장 | 색 |
|---|---|---|---|---|---|
| 1 | 대기 | 필드 씬, 축소까지 여유 | **필드 경과 시간 00:00 → 30:00** (30:00에서 멈춘다) | 남은 단계 수만큼 온전 | 한지 백색 |
| 2 | 경고 | 축소 10초 전 | 동상 | 다음 것이 깜빡임 | 주묵 붉은색 + 배너 |
| 3 | 축소 중 | 축소 진행 **30초** 동안 | 동상 | 해당 부적이 타들어감 | 붉은색 → 재 |
| 4 | 보스 | 보스 씬, 보스 시계 진행 중 | **보스 14:00 기준 남은 시간** — 슬롯을 보스 시계로 교체한다 (2026-09-19 확정) | 숨김 | 한지 백색 |
| 5 | 보스 임박 | 보스 남은 시간 03:00 이하 | 동상 | 숨김 | 주묵 붉은색 |

**상태 1~3은 필드 경과를 그대로 적는다 (2026-09-20).** `RunTimeLocator.Current.FieldElapsedSeconds`를 내림해 쓰고, `GameDB.Barrier.FieldTimeLimitSeconds`(30:00)에서 멈춘다. 두 시계를 합쳐 세지 않는다 — 합치면 필드에서만 도는 런에서 숫자가 14분 크게 시작해 틀리게 읽힌다.

**상태 4~5는 보스 시계로 교체한다 (2026-09-19 확정).** 점수 기준은 두 시계의 합인 44:00이지만([게임 디자인 11.1](../02-game-design.md)), **44:00은 결과창에서만 보여 준다.** 보스 씬에서는 슬롯이 보스 시계 14:00 카운트다운으로 바뀐다 — 광폭화 경고(12:00 · 13:30)와 같은 축이라 숫자와 경고가 어긋나지 않고, 씬 전환 순간 숫자가 뛰어 오르는 것 자체가 "뭔가 끝나고 뭔가 시작됐다"는 신호가 된다.

**구현했다 (2026-09-20).** `HudTimerController`가 `RunManager.Phase`를 읽어 `RunPhase.Boss`면 보스 시계로, 아니면 필드 경과로 적는다. 보스 시계 상한은 `BossPatternSO`의 보스 제한 시간(840초), 경과는 `RunTimeLocator.Current.BossElapsedSeconds`다. **교체 조건은 시각도 씬도 아니라 단계다** — 런이 끝나도 씬 전환은 한 프레임 늦게 온다. `Dev_BossTrial` 1인 플레이에서 확인했고 3인은 아직이다.

**기준 시간은 코드에 없다.** GameDB가 갖고 있고 `RunManager`가 정산에 쓰는 값과 같은 출처다. 밸런스에서 고치면 HUD도 따라간다.

**30:00에 멈춰도 고장이 아니다.** 필드 30:00은 실패가 아니라 보스맵 강제 이동 트리거이고([시스템 기획 2.1](../01-system-design.md)), 시간 보너스도 그 지점에서 바닥난다. 서버가 필드 경과를 상한에서 고정하고 HUD가 한 번 더 자른다 — 보스 전환이 늦어지는 동안 표시가 30:00을 넘으면 없는 시간이 생긴 것처럼 보인다.

**상태 2~3의 색 전환은 구현했다 (2026-09-20).** `HudTimerController.ApplyColor`가 `BarrierStateLocator.TransitionSource.Snapshot`을 읽어, 경고 시작(`WarningGameplayTime`)부터 축소 종료(`EndGameplayTime`)까지를 한 구간으로 보고 숫자를 주묵 붉은색으로 바꾼다. **축소 시각을 직접 세지 않고 배너와 같은 스냅샷을 읽으므로** 배너가 뜨는 순간과 숫자가 붉어지는 순간이 어긋나지 않는다. `Revision == 0`은 서버가 아직 원을 세우지 않은 상태라 걸러낸다 — 거르지 않으면 런 시작 직후 한 번 붉어진다.

경계 시각을 코드에 박지 않았다. 1차 10:00 · 2차 20:00 · **3차 29:30**(10분 간격이 아니다)은 전부 `BarrierPhaseSO`가 갖고 있고 서버가 계산해 보낸다.

**상태 5(보스 임박)는 아직 미정이다 (2026-09-20).** 임계 `03:00`은 내려가는 숫자를 전제한 값이라 보스 구간에서만 성립한다. 필드에도 둘지, 둔다면 경과 27:00 이상으로 볼지 정해지지 않았다. 상태 5는 미구현이라 당장 어긋나는 동작은 없다.

**상태 3(축소 중)은 신설이다.** 기존 문서에는 [12.1의 "축소 10초 전 경고 배너"](../02-game-design.md)만 있었다. 배틀그라운드가 무서운 것은 카운트다운이 아니라 **자기장이 실제로 좁혀지는 동안 계속 보이는 것**이다. 배너만 있으면 결계는 이벤트가 아니라 알람이다.

**상태 5는 숫자를 바꾸지 않고 색만 바꾼다.** 남은 시간을 상시 표시하면 마지막 구간에 파티가 숫자만 쳐다보다 무너진다는 반론이 있었다. 색 전환이 그 반론에 대한 답이다 — 시간이 없다는 것을 숫자를 읽지 않고도 알게 만들어 **읽는 횟수를 줄인다.**

### 2.3 상시 카운트다운을 넣는 진짜 이유

예뻐서가 아니다. **"거점 하나 더 칠 시간이 있나"를 만들려고 넣는다.** 10초 전 배너만 있으면 파밍 우선순위를 세울 수 없어 파티가 안전하게만 움직이고, 필드 30분이 밋밋해진다.

**두 시계 모델에서 이 이유가 더 강해진다.** 게이트가 상시 열려 있어 필드에 시간 제한이 없는 대신, 파밍을 오래 할수록 점수의 남은 시간 보너스가 줄어든다([게임 디자인 11.1](../02-game-design.md), 기준 상한 44:00). **시간 비용이 강제 종료가 아니라 점수로 존재하므로, 그 비용이 화면에 보이지 않으면 아예 없는 것이 된다.**

### 2.4 미니맵과의 관계

배틀그라운드의 저 헤더는 미니맵에 붙어 있다. "1분 18초 남았다"와 "나는 원 밖이다"가 같이 읽혀야 의미가 있기 때문이다. 그믐의 결계는 이전 원과 RequiredLandmarks·BarrierMapBinding 제약으로 중심을 선택하므로 현재 경계와 이동 방향을 함께 읽을 수 있어야 한다.

**결론: 미니맵 위에 놓되, 의존하지 않는다.** 시간 슬롯은 자기 완결적 위젯이어야 하고, 미니맵이 W5로 밀려도 W3에 혼자 설 수 있어야 한다.

### 2.5 네트워크 계약

**서버는 시계 상태 하나만 보내고, 각 클라이언트가 빼서 표시한다.**

구현은 `RunTimeLocator.Current.GameplayElapsedSeconds`(필드 경과 + 보스 경과)를 읽어 기준 시간에서 뺀다.
경과 시간 자체가 `RunClockState`로 동기화되므로 HUD를 위해 새로 주고받는 값은 없고, 런 도중 접속한
클라이언트도 같은 숫자를 본다.

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
| 검 반격 표식 | 최대 3칸 표식 | 현재 WeaponAbilityHUD의 CounterMarks 표시. 구 100 게이지 링은 사용하지 않음 |
| 보스 HP 바 | 위 프레임 재사용 | 스케일 |
| 스킬 · 아이템 슬롯 프레임 | 1장 | 상태는 틴트 |
| 장비 등급 테두리 4종 | 1장 (틴트 4색) | 일반·부적·영험·신물 |
| 직업 아이콘 3종 | **필요** | 검 · 활 · 방울 |
| 우상단 슬롯 프레임 | **필요** | |
| 결계 부적 표식 3종 | **필요** | 온전 / 타는 중 / 재 |
| 축소 경고 배너 | **필요** | |
| 보스 방향 마커 | **필요** | |
| 파티원 상태 플레이트 | **필요** | 초상 테두리 |
| 미니맵 프레임 (좌상단) | **필요** | 내부 지도는 **맵 확정 후 캡처한 2D 이미지**(2026-09-13 결정, 7.2 참고) |
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
| 배경 패널 (공용) | 가변 | **512 × 256** | 좌우 32 · 상하 24 |
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

BACKGROUND — must be keyable (see 7.0.5):
Output PNG with real alpha transparency. If alpha is not supported, place the asset
on a COMPLETELY FLAT, PURE #FF00FF MAGENTA FIELD — one solid colour, no gradient,
no vignette, no shadow, no reflection, no texture, nothing but the asset.
The magenta must not appear anywhere inside the asset itself.

no text, no letters, no numbers, no tassels, no creatures,
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

### 7.0.5 배경과 투명 — 생성물은 "획"만이다

**반투명한 검은 판은 이미지로 받지 않는다. 엔진이 만든다.**

생성 AI는 부분 투명(알파 그라디언트)을 거의 못 낸다. 85% 검은 판을 그림으로 받으려 하면
**불투명한 검은 사각형**이 나오고, 그걸 화면에 올리면 게임 화면이 가려진다.

| 층 | 누가 만드나 | 어떻게 |
|---|---|---|
| 뒤 — 반투명 검은 판 | **엔진** | `Image` 컴포넌트에 색 `#1A1A1A`, 알파 `0.85`. 이미지 없음 |
| 앞 — 붓 획 · 문살 · 점 | **생성 이미지** | 투명 배경 PNG |

**이게 3.1의 원칙 그대로다** — 색과 도형으로 되는 것은 이미지로 받지 않는다. 판의 불투명도를
10월에 조정하고 싶어지면, 이미지를 다시 뽑는 대신 인스펙터에서 알파만 옮기면 된다.

#### 알파를 못 내는 생성기를 쓸 때

`#FF00FF` 마젠타 단색 위에 뽑고 키잉으로 뺀다. 마젠타를 쓰는 이유는 **오방색 다섯 색
어디에도 없어서** 획 안쪽 색과 섞이지 않기 때문이다. 검정·흰색 배경은 먹 획과 한지 흰빛에
그대로 걸린다.

프롬프트에 반드시 `no gradient, no vignette, no shadow`를 함께 넣는다 — 배경에 그림자나
비네트가 깔리면 단색이 아니게 되어 한 번에 못 뺀다.

⚠️ **흰 배경으로 나왔다면 그대로 쓰지 않는다.** 흰색을 키로 빼면 먹 획 안의 비백(飛白) —
붓이 마르며 생긴 흰 줄 — 까지 같이 뚫린다. 획이 좀먹은 것처럼 된다.
흰 배경 결과물은 **알파 채널이 실제로 있는지 먼저 확인**하고, 없으면 마젠타로 다시 뽑는다.

#### 후처리 3단계

1. **키잉** — 마젠타를 알파로 뺀다
2. **가장자리 정리** — 키잉 뒤 획 테두리에 마젠타 잔상(fringe)이 남는다. Defringe 또는
   가장자리 1px 침식으로 지운다. **이걸 안 하면 어두운 화면에서 획에 분홍 테두리가 보인다**
3. **여백 자르기** — 3.2의 제작 크기로 크롭. 아이콘은 사방 여백을 남긴다(7.4)

**한지 결과 종이 번짐은 획 안쪽에만 넣는다.** 배경 전체에 종이 질감을 깔면 그 질감까지
불투명하게 따라 들어와서 화면을 덮는다.

---

### 7.0.6 조립 — 생성 이미지 한 장으로는 화면에 못 올린다

7.0.5에서 "판은 엔진이 만든다"고 했는데, **조립 방법을 안 적어서 생성물만으로는 글자 자리가
뚫려 보인다**는 문제가 나왔다(2026-09-07 실측). 판이 없으면 밝은 게임 화면 위에서 흰 숫자가
안 읽힌다.

**HUD 요소 하나는 게임오브젝트 하나가 아니라 세 층의 겹침이다.**

값이 차오르는 요소(체력·스태미나·신력·보스 바)는 **테두리가 채움보다 위**에 있어야 한다.
채움 아래에 두면 체력이 줄면서 테두리도 같이 잘려 나간다.

```
Timer                (글자만 있는 요소 - 2층)
 ├ Panel             Image · Sprite = Panel_Back.png (9-slice)          <- 생성물
 │                   Color #1A1A1A · Alpha 0.85                       <- 엔진이 틴트
 ├ Border            Image · Sprite = Timer_Border.png                  <- 생성물
 └ Label             TextMeshProUGUI                                    <- 숫자

HealthBar            (값이 차오르는 요소 - 3층)
 ├ Track             Image · Sprite = Bar_Track.png                     <- 생성물 (빈 자리)
 ├ Fill              Image · Sprite = Bar_Fill_HP.png                   <- 생성물 (차오름)
 │                   Image Type = Filled · Horizontal · Origin Left
 ├ Border            Image · Sprite = Bar_Border.png                    <- 생성물 (항상 온전)
 │                   Raycast Target 끄기
 └ Label             TextMeshProUGUI (선택)
```

**Border가 Fill보다 아래 형제로 있으면 안 된다.** Unity UI는 계층 순서대로 그리므로,
Border가 마지막이어야 채움 위에 얹힌다.

**순서가 곧 그리는 순서다.** Panel이 맨 아래라 숫자가 읽히고, Border가 그 위에 얹히고,
Label이 맨 위다. Panel에 `Sprite`를 넣지 않는 것이 핵심이다 — 색만 있는 `Image`는
스프라이트 없이도 사각형을 그린다.

| 층 | 무엇 | 왜 |
|---|---|---|
| Panel | `Panel_Back.png` + 색 `#1A1A1A` · 알파 `0.85` | 가독성. **모양과 질감은 이미지, 색과 짙기는 엔진**(7.0.7) |
| Border | 생성 스프라이트 | 붓 획. **투명 배경이라 판 위에 그대로 얹힌다** |
| Label | TMP | 3.1대로 글자는 이미지가 아니다 |

**Border 스프라이트의 색을 흰색으로 두고 `Image.color`로 틴트한다.** 그러면 오방색을
바꿀 때 이미지를 다시 안 뽑아도 된다 — 시간(황)·파티(적)·보스(청)가 같은 획을 쓸 수 있다.

#### 7.0.7 배경 패널 스프라이트 — 판도 이미지로 받되, 색은 받지 않는다

처음에는 판을 스프라이트 없는 `Image`(단색 사각형)로 잡았다. **붓 획 옆에서 그 사각형만
공장제로 보인다** — 획은 가장자리가 불규칙한데 판만 자로 잰 직사각형이기 때문이다.

그렇다고 7.0.5의 문제가 사라진 것은 아니다. 생성기는 반투명을 못 낸다.

**그래서 역할을 가른다.**

| 무엇 | 어디서 |
|---|---|
| 판의 **모양과 질감** (한지 결, 붓으로 칠한 불규칙한 가장자리) | **생성 이미지** |
| 판의 **색과 짙기** | **엔진** — `Image.color` `#1A1A1A`, 알파 `0.85` |

**이미지는 흰색으로 뽑는다.** 흰 텍스처에 어두운 색을 틴트하면 원하는 짙기가 나오고,
알파는 인스펙터 숫자 하나로 남는다. 반대로 어둡게 뽑아 오면 다시 밝게 만들 수 없다.

**1장으로 시간·파티 이름·(필요하면) 그 밖의 판을 전부 쓴다.** 9-slice로 늘린다.

제작 512 × 256 · 9-slice Border 좌우 32 · 상하 24

```
A single rectangular BACKGROUND PANEL texture for a game UI, painted in
Korean ink-painting style, isolated on transparent background
(or a flat pure magenta field if alpha is unsupported).

IT IS PAINTED IN WHITE AND LIGHT GREY ONLY — no color at all. The engine will
tint it dark, so anything colored here is a mistake.

The panel is a soft rectangle washed on with a wide flat brush: its EDGES ARE
SLIGHTLY IRREGULAR AND FEATHERED, never a ruler-straight cut, with one or two
places where the brush ran dry along the border.
Visible hanji paper fiber across its surface, plus a few faint fiber flecks.

THE CENTER IS A UNIFORM FLAT FIELD with no pattern, no gradient toward the middle,
and no ornament, so it can be stretched horizontally and vertically.
Only the outer few percent carries the brushed edge treatment.

No border line, no frame, no stroke outline, no bevel, no shadow, no glow,
no text, no letters, no numbers.
```

⚠️ **`no border line`을 넣는 이유** — 테두리는 별도 장(7.3.3 · 7.1)이 담당한다. 판에까지
외곽선이 들어가면 겹쳐서 두 줄로 보인다.

⚠️ **`PAINTED IN WHITE AND LIGHT GREY ONLY`** — 여기서 색을 받으면 엔진 틴트와 곱해져
의도하지 않은 색이 된다. 뽑고 나서 회색조인지 눈으로 확인한다.

**9-slice 확인**: Sprite Editor에서 Border를 잡은 뒤, 인스펙터에서 가로로 3배 늘려 본다.
가운데가 늘어나고 네 모서리의 붓 자국이 그대로면 성공이다. **모서리 붓 자국이 늘어나면
Border 값이 너무 작은 것**이다.

---

#### 판이 필요 없는 요소

| 요소 | 판 | 이유 |
|---|---|---|
| 시간 · 파티 이름 | **필요** | 글자가 올라간다 |
| 미니맵 링 | 불필요 | 가운데는 지도가 채운다 |
| 자원 바(트랙·채움) | 불필요 | 트랙 자체가 판 역할을 한다(7.3.1) |
| 아이콘 | 불필요 | 문살 소켓 안에 들어간다 |

#### 한 장으로 받고 싶다면

조립이 번거로우면 **획 뒤에 옅은 먹 담채(淡彩)를 함께 그리게** 할 수도 있다.
프롬프트에 아래를 더한다.

```
Behind the stroke, a soft translucent ink wash (담채) fills the panel area —
a thin, unevenly brushed dark grey field, darker near the stroke and fading out,
clearly semi-transparent, never a solid opaque rectangle.
```

**다만 권하지 않는다.** 생성기의 반투명은 값이 들쭉날쭉해서 요소마다 배경 짙기가 달라지고,
그걸 나중에 맞추려면 이미지를 전부 다시 뽑아야 한다. 엔진 알파는 숫자 하나다.

---

### 7.1 우상단 시간

180 × 40 화면 · **황(黃) 계열** · 2급

```
ONLY THE BORDER STROKE of a game timer panel, very wide and short (4.5:1).
The panel background itself is NOT drawn — the engine supplies it.
Flat graphic design. Its border is a SINGLE THIN BRUSH STROKE in muted gold
(#C9A227) that runs along the TOP EDGE AND DOWN THE LEFT SIDE ONLY —
the right and bottom stay open, unbordered.
The stroke has uneven thickness and one dry-brush break.
A small ink dot terminates the stroke at the upper left.

The interior stays EMPTY and transparent ON PURPOSE — a dark panel and a number
are layered underneath by the engine, so the stroke must not cover that area.
Keep the stroke tight to the edges; nothing crosses the middle.
No seal, no stamp, no bevel, no metal, no glow, no ornament.
```

---

### 7.2 좌상단 미니맵

180 × 180 화면 · **황(黃) 계열** · 2급
Tab 확대본은 같은 프레임을 스케일한다.

**조작 (2026-09-22 조정).** 평소에는 지도를 접어 두고 여는 키만 구석에 남긴다.

| 입력 | 결과 |
|---|---|
| `Tab` | 지도를 펼치고, 다시 누르면 접는다 |
| `Esc` (지도가 펼쳐져 있을 때) | **지도만 접는다.** ESC 메뉴는 열리지 않는다 |
| `Esc` (지도가 접혀 있을 때) | 평소대로 ESC 메뉴가 열린다 |

ESC는 한 번에 한 층만 거둔다 — 관리 창 → 설정 → 펼친 지도 → 메뉴 순이다.
지도와 메뉴가 각자 ESC를 읽으면 한 번 눌러 지도가 닫히면서 메뉴까지 열리므로,
메뉴가 있는 화면에서는 지도가 ESC를 메뉴에 넘기고 메뉴가 두 경우를 모두 처리한다.
메뉴가 없는 화면(개발 씬)에서는 지도가 직접 ESC로 접힌다.

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

**링은 틴트하지 않는다.** 오방색 5점이 이미지에 색으로 들어가 있어서, `Image.color`를 건드리면
그 다섯 점이 같이 물든다. 이 한 장만 예외이고 나머지는 7.6.6대로 흰색 + 엔진 틴트다.

**표시 범위는 400m로 둔다(2026-09-13).** `BarrierPhaseSO._maxRadius`가 400이라 기본값 100m로
두면 결계 원이 미니맵의 네 배 크기로 그려져 화면을 덮는다. 400이면 1페이즈 원이 미니맵
가장자리에 맞고, 축소가 눈에 보인다. **결계는 플레이어 중심이 아니라 자기 중심이라 언제든
미니맵 밖으로 나갈 수 있으므로 `RectMask2D`로 자른다.**

#### 7.2.1 링 안쪽 지도 — 맵을 캡처해 2D로 깐다 (2026-09-13 결정)

**셰이더나 도형으로 그리지 않는다. 맵이 확정된 뒤 위에서 캡처해 2D 이미지 한 장으로 깐다.**

3.1 표는 원래 "내부 지도는 셰이더·도형"이었으나, 맵이 수작업 배치라 절차적으로 그릴 근거가 없다.
캡처 한 장이면 지형·거점·길이 한 번에 맞고, 맵이 바뀌면 다시 찍으면 된다.

**그래서 맵이 확정될 때까지 링 안쪽은 비워 둔다.** 임시로 회색 판을 깔지 않는다 —
어차피 걷어내게 되고, 그때까지 "이게 최종인가" 하는 오해만 만든다.
현재 링 안쪽으로 게임 화면이 비치는 것은 **의도된 중간 상태**다.

| 무엇 | 언제 | 누가 |
|---|---|---|
| 맵 확정 | 미정 | 테크니컬 아트 · 레벨 배치 |
| 탑다운 캡처 → 2D 텍스처 | 맵 확정 후 | UI (@kjw1237) |
| 링 안쪽에 배치 · 스케일 맞추기 | 위와 동시 | 〃 |

⚠️ **캡처 배율은 `MinimapPlayerDots._rangeMeters`와 맞춰야 한다.** 지금 400m 기준이므로
캡처도 400m 폭을 담아야 점 위치와 지도가 어긋나지 않는다.

---

### 7.3 좌하단 자원 (HP · 스태미나 · 신력) — 트랙과 채움을 나눈다

320 × 44 화면(3줄 합계) · 3급

**한 자원당 이미지 세 장이다.** `Image.fillAmount`는 **한 스프라이트를 왼쪽부터 잘라 보여주는 것**이라, 비었을 때 보이는 판(트랙)과 차오르는 획(채움)을 같은 그림으로 쓸 수 없다.

| 층 | 파일 | 무엇 | Image Type |
|---|---|---|---|
| 1 (아래) | `Bar_Track.png` | 비었을 때 보이는 자리. **1장으로 3자원 공용** | `Simple` |
| 2 | `Bar_Fill_HP.png` 등 | 차오르는 획. **자원마다 1장** | **`Filled` · Horizontal · Origin Left** |
| 3 (위) | `Bar_Border.png` | 끝단 마감과 외곽선. **1장으로 3자원 공용** | `Simple` |

**Border가 왜 따로 필요한가.** 테두리를 채움 이미지에 그려 넣으면 체력이 절반일 때
**오른쪽 테두리가 같이 사라진다.** 바가 끝나는 자리를 알 수 없게 되고, 그러면 "얼마나 남았나"가
아니라 "바가 얼마나 짧은가"로 읽힌다.

| 줄 | 자원 | 채움 색 | 굵기 |
|---|---|---|---|
| 1 | HP | **적(赤) 火** `#B33A2E` | 14px |
| 2 | 스태미나 | **백(白) 金** `#E8E0CE` | 10px |
| 3 | 신력 | **황(黃) 土** `#C9A227` | 8px |

---

#### 7.3.1 트랙 (비었을 때) — 1장

```
A single horizontal EMPTY GAUGE TRACK for a game HUD, extremely thin (ratio 23:1),
flat Korean ink-painting style, isolated on transparent background
(or a flat pure magenta field if alpha is unsupported).

It is a MID-GREY INK STROKE (#6A6A6A) — light enough to stay visible on top of a
near-black panel, but clearly drained of color so it never reads as "filled".
Do NOT paint it near-black; it will vanish against the panel behind it.

SILHOUETTE — it must match the fill stroke exactly (7.3.2):
a soft pooled blob at the LEFT end, then a CONSISTENT, UNBROKEN HEIGHT all the way
to the RIGHT EDGE of the image, CUT OFF FLAT there. It does NOT taper and does NOT
end in a point. Dry-brush white streaks (비백) run through it as texture only.
Faint hanji paper fiber inside it.

No color accent, no fill, no frame, no bevel, no metal, no glow.
```

**트랙은 회색이다.** 색을 넣으면 비어 있을 때도 자원이 남은 것처럼 보인다.

---

#### 7.3.2 채움 (차오르는 획) — 자원마다 1장

⚠️ **채움 이미지는 오른쪽 끝을 뾰족하게 만들지 않는다.** `fillAmount`가 오른쪽부터 잘라내므로, 뾰족한 꼬리는 **가득 찼을 때만 보이고 나머지 구간에서는 통째로 사라진다.** 그러면 100%와 99%가 완전히 다른 모양이 된다.

```
A single horizontal GAUGE FILL stroke for a game HUD, extremely thin (ratio 23:1),
flat Korean ink-painting style, isolated on transparent background
(or a flat pure magenta field if alpha is unsupported).

PAINTED IN WHITE ONLY — the engine tints it per resource (red, off-white, gold,
teal). Any color painted here would multiply with that tint.

The dry-brush streaks (비백) must be HOLES IN THE ALPHA CHANNEL — actual
transparency where the brush ran dry — NOT white paint on top of the stroke.
If they are painted white, tinting will flood them and the texture disappears.
The LEFT end has a POOLED BLOB where the loaded brush landed.
From there the stroke runs at a CONSISTENT, UNBROKEN HEIGHT all the way to the
right edge of the image and is CUT OFF FLAT there — it does NOT taper, does NOT
thin out, does NOT end in a point. The right edge must look like the stroke
continues past the frame.
The alpha holes run along its length as texture only, never breaking the stroke
into separate pieces.

No frame, no outline, no bevel, no glow, no background.
```

**색만 바꿔 3장을 뽑는다** — 스태미나는 `hanji off-white (#E8E0CE)`, 신력은 `muted gold (#C9A227)`. 굵기 차이는 임포트에서 세로로 줄여 만든다(원본은 같은 비율).

---

#### 7.3.3 테두리 (항상 온전) — 1장 공용

⚠️ **가운데가 완전히 뚫려 있어야 한다.** 안쪽이 조금이라도 칠해져 있으면 그 아래 채움이 가려진다.

```
ONLY THE OUTLINE of an extremely thin horizontal gauge (ratio 23:1),
flat Korean ink-painting style, isolated on transparent background
(or a flat pure magenta field if alpha is unsupported).

The INTERIOR IS COMPLETELY HOLLOW AND TRANSPARENT — a window, not a surface.
Nothing is painted inside it; a colored bar will show through from underneath.

Draw only:
- a hanji off-white (#E8E0CE) hairline running along the TOP EDGE,
- a small ink cap at the LEFT end where the brush landed,
- a small ink cap at the RIGHT end marking where the gauge ends.
The bottom edge stays open, unbordered.

Every mark shows brush anatomy: pooled, dry-streaked, tapered.
No fill, no shading, no background, no bevel, no glow.
```

**오른쪽 끝 마감이 이 장의 존재 이유다.** 채움은 줄어들어도 이 표시는 남아서
"여기까지가 최대치"를 계속 알려 준다.

---

#### 7.3.3 왜 이렇게 나누나

| 안 나눴을 때 | 증상 |
|---|---|
| 트랙 없이 채움만 | HP가 0이 되면 **바가 화면에서 사라진다.** 죽었는지 UI가 꺼진 건지 구분이 안 된다 |
| 채움에 뾰족한 꼬리 | 99%에서 꼬리가 잘려 **모양이 급변**한다 |
| 트랙에 색 | 비어 있어도 자원이 남아 보인다 |
| 테두리를 채움에 그려 넣음 | **체력이 줄면 오른쪽 테두리도 같이 사라진다.** 최대치를 알 수 없다 |
| 테두리 안쪽이 칠해짐 | 채움이 가려져 바가 영영 비어 보인다 |
| **트랙과 채움의 실루엣이 다름** | 채움이 절반일 때 **굵기가 계단처럼 튄다.** 둘은 같은 모양이어야 한다 |
| 트랙이 너무 어두움 | `#1A1A1A` 판 위에서 **트랙이 안 보인다.** 빈 바가 아예 없는 것처럼 읽힌다 |
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

### 7.5 파티원 플레이트 — 새로 뽑는 것은 두 장뿐이다

180 × 52 화면 · 3급 · 세로 3개

**한 줄을 통째로 한 장에 그리면 안 된다.** 자원 바가 그림에 박히면 체력이 줄어도
움직이지 않는다. 파티 한 줄은 **여러 스프라이트의 조립**이다(7.0.6).

```
PartyRow
 ├ Panel        Panel_Back.png      · 색 #1A1A1A 알파 0.85   <- 7.0.7 재사용
 ├ Lattice      Party_Lattice.png                            <- 신규 1장
 ├ NameLine     Party_NameLine.png                           <- 신규 1장
 ├ Label        TextMeshProUGUI
 └ Bars         (3줄, 각각 Track/Fill/Border 3층)             <- 7.3 재사용
```

| 파일 | 어디서 | 비고 |
|---|---|---|
| `Panel_Back.png` | **7.0.7 재사용** | |
| `Party_Lattice.png` | **신규** | 문살 아이콘 소켓 |
| `Party_NameLine.png` | **신규** | 이름 줄 밑선 |
| `Bar_Track / Fill / Border` | **7.3 재사용** | RectTransform만 작게 |

**자원 바를 새로 뽑지 않는다.** 좌하단 것과 같은 스프라이트를 작게 쓴다 —
같은 붓에서 나온 것이 보장되고, 색을 바꿀 때 한 곳만 고치면 된다.

---

#### 7.5.1 문살 소켓 — 1장

```
ONLY A SMALL SQUARE FRAME for a game UI icon slot, drawn as a Korean 문살
window lattice: a few straight wooden slats crossing at right angles near the
four corners, leaving the middle open. Black sumi ink, dry brush, slightly
uneven — hand-drawn, not ruled.

THE INTERIOR IS COMPLETELY HOLLOW AND TRANSPARENT — an icon shows through it.
The frame itself is thin; it must not crowd the icon.

Isolated on transparent background (or flat pure magenta if alpha is unsupported).
No fill, no panel behind it, no bevel, no glow, no text.
```

#### 7.5.2 이름 줄 밑선 — 1장

**이건 게이지가 아니라 장식이라 잘리지 않는다.** 그래서 오른쪽이 붓끝처럼 빠져도 된다.

```
A SINGLE THIN HORIZONTAL BRUSH STROKE, hanji off-white (#E8E0CE), very long and
fine, isolated on transparent background (or flat pure magenta).

It marks the underline of a name field. Brush anatomy in full: a small pooled
blob at the LEFT end, dry-brush white streaks through the middle, TAPERING TO A
FINE POINT at the RIGHT end and lifting off the paper.

Nothing else — no box, no frame, no second line, no icon, no fill, no text.
```

⚠️ **밑선은 뾰족하게 끝나도 되고, 자원 바는 안 된다.** 밑선은 `fillAmount`가 걸리지 않아
항상 통째로 보이기 때문이다. **이 차이가 7.3과 갈리는 지점이다.**

#### 7.5.3 방금 나온 것이 왜 어긋났나

| 나온 것 | 문제 |
|---|---|
| 문살 + 획 4개가 **한 장** | 자원 바가 그림에 박혀 **줄어들지 않는다** |
| 자원 획이 오른쪽으로 **뾰족** | 채움에 쓰면 99%에서 꼬리가 사라진다(7.3.2) |
| 네 획의 굵기가 **거의 같음** | 서열이 안 읽힌다. 굵기는 임포트에서 만든다 |

**문살 사각형만 잘 나왔다.** 그것만 잘라 쓰고 나머지는 위 두 프롬프트로 다시 뽑는다.

---

### 7.6 보스 바 — 전용 스프라이트로 뽑는다

720 × 34 화면 · 1급

**보스 바는 공용 바를 재사용하지 않는다.** 7.0.1이 정한 계층 때문이다 — 1급이 3급과 같은 획을
쓰면 폭만 넓은 파티 게이지가 되고, v1에서 실패했던 "전부 같은 무게" 상태로 돌아간다.

**보스는 이 게임에서 유일하게 사건이다.** 획 자체가 달라야 한다.

```
BossBar
 ├ Panel            Panel_Back.png        · #1A1A1A 0.85          <- 7.0.7 재사용
 ├ HpTrack          Boss_Track.png        · #6A6A6A               <- 신규
 ├ HpFill           Boss_Fill.png         · #3FE0C8 (여우불)       <- 신규
 │                  Image Type = Filled · Horizontal · Origin Left
 ├ HpBorder         Boss_Border.png       · #E8E0CE               <- 신규
 ├ GroggyTrack      Boss_Track.png        (더 얇게)                <- 같은 장 재사용
 ├ GroggyFill       Boss_Fill.png         · #E8E0CE (한지)         <- 같은 장 재사용
 ├ GroggyBorder     Boss_Border.png                                <- 같은 장 재사용
 └ Ends             Boss_Ends.png                                  <- 신규
```

**신규 4장.** 두 채널(체력·그로기)은 같은 보스 스프라이트를 **높이와 색만 달리해** 쓴다 —
같은 바의 두 줄이므로 여기서까지 나누면 오히려 관계가 안 읽힌다.

배경 판만 공용을 쓴다. 판은 뒤에 깔리는 어두운 면일 뿐이라 성격이 없다.

> ⚠️ **2026-09-13 조립 결과 — 넘어온 4장이 규격 비율과 맞지 않는다.**
>
> | 파일 | 이 문서가 요구한 비율 | 실제 이미지 | 화면 규격(720 × 28) |
> |---|---|---|---|
> | `Boss_Fill` | 21 : 1 | **7.8 : 1** | 25.7 : 1 |
> | `Boss_Track` | 21 : 1 | **7.9 : 1** | 〃 |
> | `Boss_Border` | 21 : 1 | **7.2 : 1** | 〃 |
> | `Boss_Ends` | 양끝만, 가운데 비움 | **7.7 : 1** | 〃 |
>
> 720 폭에서 **가로로 3.3배 늘어난다.** 이 획이 뭉개지지 않으려면 높이가 93px이어야 하는데
> 7.0.1이 28px로 못박았으므로 둘을 동시에 만족할 수 없다. 자원 바(7.3)는 원본이 17:1이라
> 320×14에서 버텼지만 보스는 못 버틴다.
>
> **당장은 `Ends` 층을 빼고 3층(트랙·채움·테두리)만 올려 두었다.** 양끝 여우꼬리 장식이
> 가로로 뭉개져 바 전체를 덮는 것이 제일 심했다. 남은 3층도 늘어난 상태 그대로다.
>
> **4장을 21:1로 다시 뽑으면 `Ends`를 되살리고 이 보정도 사라진다.** 프롬프트는 7.6.2~7.6.5를
> 그대로 쓰면 된다. 3장 머리에 적힌 대로 제작 규격은 넘길 때의 계약이고, 지금은 받는 쪽이
> 이미지를 다시 만지고 있는 상태다.

---

#### 7.6.1 공통 — 보스 획은 무엇이 다른가

세 장(`Boss_Track` · `Boss_Fill` · `Boss_Border`) 프롬프트 앞에 함께 붙인다.

```
BOSS TIER — this stroke must feel heavier and wetter than an ordinary UI gauge.
The brush was fully loaded and pressed hard: the ink is dense, the edges show
where it flooded and bled outward into the paper (선염), and the pooled start is
noticeably larger. It reads as one decisive slash, not a drawn line.
Still FLAT — no bevel, no glow, no gradient, no highlight.
```

**"눌러 그은 한 획"이 보스와 파티를 가르는 지점이다.** 파티 획은 가늘고 조용하고,
보스 획은 붓을 눌러 먹이 번진다. 굵기가 아니라 **필압**이 다르다.

---

#### 7.6.2 보스 트랙 — 신규

```
A single wide horizontal EMPTY GAUGE TRACK for a boss health bar (ratio 21:1),
flat Korean ink-painting style, isolated on transparent background.

PAINTED IN WHITE AND LIGHT GREY ONLY — the engine tints it.

SILHOUETTE — it must match Boss_Fill exactly:
a large pooled blob at the LEFT end, then a CONSISTENT, UNBROKEN HEIGHT all the
way to the RIGHT EDGE, CUT OFF FLAT there. It does NOT taper.
Dry-brush breaks are HOLES IN THE ALPHA CHANNEL, not white paint.

No frame, no outline, no ends decoration, no color, no text.
```

#### 7.6.3 보스 채움 — 신규

```
A single wide horizontal GAUGE FILL stroke for a boss health bar (ratio 21:1),
flat Korean ink-painting style, isolated on transparent background.

PAINTED IN WHITE ONLY — the engine tints it teal for health and off-white for
the groggy gauge.

A large pooled blob at the LEFT end where the loaded brush landed hard.
From there a CONSISTENT, UNBROKEN HEIGHT to the RIGHT EDGE, CUT OFF FLAT.
It does NOT taper, does NOT thin out, does NOT end in a point.
The dry-brush streaks (비백) must be HOLES IN THE ALPHA CHANNEL — actual
transparency — NOT white paint, or the tint will flood them.
Ink bleeding (선염) along one long edge, denser than an ordinary gauge.

No frame, no outline, no background, no color.
```

#### 7.6.4 보스 테두리 — 신규

```
ONLY THE OUTLINE of a wide boss gauge (ratio 21:1), flat Korean ink-painting
style, isolated on transparent background.

THE INTERIOR IS COMPLETELY HOLLOW AND TRANSPARENT — a window. The colored fill
shows through from underneath. Nothing is painted inside.

Draw only a hairline along the TOP EDGE, a heavy ink cap at the LEFT end, and a
heavy ink cap at the RIGHT end marking the maximum. The caps are thicker and more
deliberate than on a player gauge. The bottom edge stays open.

PAINTED IN WHITE ONLY. No fill, no shading, no bevel, no glow, no text.
```

---

#### 7.6.5 양끝 장식 — 신규 1장 (그대로 간다)

```
TWO SMALL INK FLOURISHES for the two ends of a wide game HUD bar, drawn as one
image: a mirrored pair, one at the far left and one at the far right, with
NOTHING BETWEEN THEM — the middle of the image is completely empty and transparent.

Each flourish is a SINGLE CURVED BRUSH STROKE suggesting a fox tail, painted in
white and light grey only (the engine will tint it). It curls inward toward the
bar and stays compact — it must not extend far above or below the bar's height.

Brush anatomy: pooled where it starts, dry-streaked, tapering to a fine point.
Isolated on transparent background. No fox head, no creature, no face, no veil,
no glow, no color, no frame, no text.
```

⚠️ **`NOTHING BETWEEN THEM`** — 양끝을 한 장에 담되 가운데는 비운다. 바 길이가 바뀌어도
장식만 양끝에 붙는다.

---

#### 7.6.6 흰색으로 뽑고 엔진에서 틴트하는 원칙

7.0.7과 같다. **모양은 이미지, 색은 엔진.**

색을 이미지에 넣으면 체력 청록과 그로기 한지 흰빛을 위해 같은 획을 두 번 뽑아야 하고,
10월에 색을 조정할 때 다시 뽑아야 한다. 흰색 한 장이면 `Image.color` 값 하나다.

**비백은 반드시 알파 구멍이어야 한다.** 흰 획에 흰 줄을 그려 넣으면 틴트가 그 줄까지
물들여 질감이 통째로 사라진다. 이 규칙은 플레이어 바(7.3.2)와 동일하다.

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
| 신력 표시 | 좌하단 3번째 바로 잡혀 있다 | 현재 PlayerStatus.Spirit과 HUDController 자원 표시, 방울 Q/R 비용에 사용된다. 프리팹 배선·가독성을 확인한다 |
