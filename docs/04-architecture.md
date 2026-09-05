# 그믐 (晦) — 프로젝트 구조 설계서

> 기획서([시스템](01-system-design.md) · [게임 디자인](02-game-design.md) · [기술 구현](03-technical-design.md))를 실제 Unity 프로젝트 구조로 옮기는 설계 문서.
> **이 문서 하나만 읽으면 폴더 · 씬 · 클래스 · 데이터가 어디에 어떻게 놓이는지 알 수 있어야 한다.**
> 문서 버전 0.3 · 최종 수정 2026-08-23

- 밸런스 수치는 여기 적지 않는다 → [게임 디자인](02-game-design.md)
- 서버/클라이언트 판정 기준과 Mirror API는 여기서 요약만 한다 → [네트워크 권위](unity/NETWORK_AUTHORITY.md)
- 네이밍 · 스타일은 → [코딩 컨벤션](coding-convention.md)
- 문서와 코드가 어긋나면 **코드가 우선**이고, 이 문서를 갱신한다.

---

## 1. 한눈에 보는 구조

```
런 전체를 지배하는 축 = "경과 시간" 하나
서버(호스트)가 경과 시간(초)을 관리 · 동기화하고,
결계 단계 · 스폰 밀도 · 드랍 가중치 · 객주 개방 · 조명 시각이 전부 이 값에서 파생된다.

  RunManager (경과 시간, 런 상태 머신)          ← Geumeum.Network
      ├→ BarrierController   결계 반경 · 범위 밖 피해   ← Geumeum.AI
      ├→ SpawnManager        구간별 밀집도 · 리스폰      ← Geumeum.AI
      ├→ DropDirector        필드 레벨 → 등급 가중치     ← Geumeum.Systems
      ├→ ShopKeeper          2차 결계 구간에만 개방      ← Geumeum.Systems
      └→ DayCycleController  시각 → 조명 (로컬 전용)     ← Geumeum.Art
```

파트 4개가 각자의 폴더 · 네임스페이스 · 작업 씬을 소유하고(3인 팀이므로 테크니컬 아트는 팀장이 겸임한다), 파트 간 결합은
**공용 인터페이스(`00.Common`)와 이벤트**로만 한다. Combat·AI·Systems 사이의 구체 클래스 직접 참조는 금지하고, Art·UI도 읽기 전용 뷰·이벤트를 통해서만 상태를 읽는다.

| 파트 | 담당자 | 네임스페이스 | 폴더 (`Assets/01.Scripts/`) | 작업 씬 |
|---|---|---|---|---|
| 네트워크 · 전투 (팀장) | @AidenKim923 | `Geumeum.Network` `Geumeum.Combat` | `01.Network/` `02.Combat/` | `Dev_Combat.unity` |
| 몬스터 · 보스 | @oname36-creator | `Geumeum.AI` | `03.AI/` | `Dev_AI.unity` |
| 게임플레이 시스템 | @kjw1237 | `Geumeum.Systems` | `04.Systems/` | `Dev_System.unity` **(미생성 → 3.1)** |
| 테크니컬 아트 | @AidenKim923 (겸임) | `Geumeum.Art` | `05.Art/` | `Dev_Art.unity` **(미생성 → 3.1)** |
| UI (전담) | @kjw1237 (게임플레이 시스템 겸임) | `Geumeum.UI` | `06.UI/` | `Dev_System.unity` **(미생성 → 3.1)** |
| (공용) | `Geumeum.Data` `Geumeum.Common` | `00.Common/` | — |

---

## 2. Assets 최상위 폴더

이미 만들어져 있는 번호 폴더 체계를 그대로 쓴다. 새 최상위 폴더를 임의로 만들지 않는다.

```
Assets/
├ 00.Scenes/        씬 (Main + Dev_* 4개)                     → 3장
├ 01.Scripts/       모든 C# 스크립트                           → 4장
├ 02.Prefabs/       프리팹 (파트별 하위 폴더)                  → 7장
├ 03.Image/         텍스처, 스프라이트, UI 이미지
├ 04.Animations/    애니메이션 클립
├ 05.Animator/      애니메이터 컨트롤러
├ 06.Model/         자체 제작 모델 (장승 · 솟대 등 한국 상징물)
├ 07.Sounds/        BGM · SFX (국악 라이브러리 포함)
├ 09.Data/          ScriptableObject 에셋 (GameDB)             → 6장
├ 10.InputSystem/   Input Action 에셋 (입력은 Input Manager로 받는다, 5.6장)
├ 11.Timeline/      연출 타임라인 (집중개발 4~5주차부터)
├ 12.Materials/     머티리얼 (모델 · 캐릭터별 하위 폴더)
├ 98.FreeAssets/    무료 에셋 원본 (플러그인 · 애니메이션 등) — 수정 금지, 커밋 대상
├ 99.ThirdParty/    구매 에셋 원본 — 수정 금지, 커밋 제외(.gitignore)
└ Mirror/           Mirror 패키지 — 수정 금지
```

- `98.FreeAssets/`, `99.ThirdParty/`, `Mirror/`는 읽기 전용으로 취급한다. 외부 에셋의 머티리얼 · 프리팹을 고치고 싶으면 자기 파트 폴더로 **복사한 뒤** 수정한다 ([씬·에셋 안전](unity/SCENE_AND_ASSET_SAFETY.md)).

### 2.1 98과 99를 나누는 이유

둘 다 외부 에셋이지만 **git 취급이 정반대**라서 폴더를 분리한다.

| | `98.FreeAssets/` | `99.ThirdParty/` |
|---|---|---|
| 취득 | 무료 배포 | 유료 구매 |
| git | **커밋한다** (바이너리는 LFS) | `.gitignore` 제외 |
| 팀원 준비 | clone하면 끝 | 각자 자기 계정으로 내려받아 설치 |

유료 에셋은 라이선스가 구매 계정에 묶여 저장소에 올릴 수 없다. 그래서 받지 않은 팀원은 씬이 깨진 채로 열린다 — Gaia 지형에서 실제로 겪은 문제다. 무료 에셋은 그 제약이 없으므로 **커밋해서 전원이 같은 GUID를 갖게 한다.** 각자 따로 임포트하면 GUID가 갈려 프리팹 · 씬 참조가 끊어질 수 있다.

무료 에셋을 넣을 때 지킬 것:

- **에셋 하나당 폴더 하나.** 파일을 `98.FreeAssets/` 바로 아래 흩뿌리지 않는다.
- **`.meta`를 통째로 같이 커밋한다.** 하나라도 빠지면 GUID가 새로 발급돼 참조가 끊어진다.
- **바이너리가 `.gitattributes`의 LFS 대상 확장자인지 먼저 확인한다.** 대상이 아니면서 5MB를 넘으면 LFS 대상에 추가할지 검토한다 — `.github/scripts/check_lfs.py`가 PR에서 검사한다.
- **라이선스 근거를 `docs/licenses/`에 남긴다** ([씬·에셋 안전 4장](unity/SCENE_AND_ASSET_SAFETY.md)). 무료라고 상업 이용까지 무료인 것은 아니다.
- 데모 씬이 딸려 와도 `ProjectSettings/EditorBuildSettings.asset`에 추가하지 않는다.
- Gaia Terrain 데이터는 테크니컬 아트만 수정한다 ([기술 구현 2.3](03-technical-design.md)).

---

## 3. 씬 구성

### 3.1 씬 목록

**`현재`는 저장소에 실제로 있는 씬이고, `계획`은 아직 없는 씬이다.** 계획 씬을 있는 것처럼 참조하지 않는다 — 2026-09-04 실측으로 정정했다.

| 씬 | 상태 | 소유 |
|---|---|---|
| `Main.unity` | **현재** | 통합 씬(개활 맵). 통합 담당자(팀장)만 수정 |
| `Dev_Combat.unity` | **현재** | 네트워크·전투 작업 씬 |
| `Dev_AI.unity` | **현재** | 몬스터·보스 작업 씬 |
| `Dev_Character.unity` | **현재** | 캐릭터·무기 부착 확인용. 네트워크 없음. 빌드 설정 미포함 |
| `Dev_WeatherMakerIsolation.unity` | **현재** | WeatherMaker 격리 확인용(임시). 빌드 설정 미포함 |
| `SampleScene.unity` | **현재** | Unity 기본 생성물. 사용하지 않는다 |
| `Boss_Arena.unity` | **미생성** | 보스맵. 팀장 + 보스 담당자 공동 소유 |
| `Dev_System.unity` | **미생성** | 게임플레이 시스템 작업 씬. 소유자 @kjw1237 |
| `Dev_Art.unity` | **미생성** | 테크니컬 아트 작업 씬. 소유자 @AidenKim923 |

> **`미생성` 씬은 소유자만 등록해 둔 것이다. 통합 담당자가 미리 만들어 두지 않는다.**
>
> 씬은 만든 사람의 작업 흔적이 그대로 들어가는 파일이라, 쓰지도 않을 사람이 먼저 만들어 두면 빈 껍데기에 남의 초기 설정이 박힌다. **필요해진 시점에 소유자가 직접 만든다.**
>
> 다만 **씬을 새로 만들면 씬 목록이 바뀌므로 [CLAUDE.md](../CLAUDE.md)상 구조 변경에 해당한다 — 만든 뒤 팀에 알린다.** 다른 사람의 씬은 열어도 되지만 저장하지 않는다.

#### 씬 전환은 런 중 한 번뿐이다 (2026-09-03 개정)

초판은 "씬 전환 없음 — 런 전체가 `Main.unity` 하나에서 돈다"였다. **보스맵을 별도 씬으로 분리하면서 전환이 한 번 생겼다.**

| 경계 | 방식 |
|---|---|
| 로비 · 정산 화면 | 씬이 아니라 **UI 패널 전환** (초판 그대로) |
| 개활 맵 → 보스맵 | **씬 전환.** 3차 축소 구간의 게이트 진입 시 |
| 보스 1페이즈 → 2페이즈 | **씬 전환이 아니다.** 같은 씬 안에서 아레나 루트를 토글한다 |

**분리한 이유** — 보스맵에는 잡귀도 결계도 Gaia 지형도 없어 화면에 캐릭터 넷뿐이다. 고폴리 보스 모델이 성능 문제가 되지 않고, 조명을 개활 맵과 독립적으로 잡을 수 있다.

**치르는 비용 — 런 상태의 수명이 씬 수명에서 세션 수명으로 바뀐다.** `RunManager` · `LobbyController` · `LobbyRoster` · `SpawnManager` · `AuthorityTickScheduler`가 전부 씬 오브젝트인 `NetworkBehaviour`라 **씬을 바꾸면 통째로 파괴된다.** 다섯의 영속화와 플레이어 상태 이관 계약이 필요하다. 상세는 [직업 정체성과 보스 설계 8장](class-identity-and-boss-2026-09-03.md).

**보스맵 씬 편집 규칙 — 보스를 씬에 배치하지 않는다.** `BossSpawner`가 마커(빈 `Transform`)만 두고 런타임 스폰한다. 그래야 씬 편집이 개설 시 한 번으로 끝나고 이후 보스 작업이 전부 프리팹·SO 안에서 일어나, 공동 소유여도 충돌하지 않는다. 잡귀가 이미 이 구조다.

`Boss_Arena.unity` 내부는 아레나 두 벌과 공유부로 나눈다. **바닥 콜라이더와 NavMesh는 반드시 공유부에 둔다** — 페이즈 전환에 걸을 수 있는 영역이 바뀌면 런타임 재베이크가 필요해진다.

```
Boss_Arena.unity
├ Antechamber    전실. 게이트 통과 직후 도착 지점  (2026-09-04 신설)
├ Arena_Phase1   산속 사당 · 등불 · 담장      (시작 시 활성)
├ Arena_Phase2   저승 물길 · 여우불 · 얼굴들   (시작 시 비활성)
└ Shared         바닥 콜라이더 · NavMesh · 스폰 마커 · 게이트
```

**전실(`Antechamber`)은 씬 신설 없이 두 진입 경로를 하나로 받는다.**

| | 정식 런 | [보스 트라이 모드](unity/BOSS_TRIAL_MODE.md) |
|---|---|---|
| 도착 | 게이트 30초 카운트다운 후 전원 강제 이동 | 로비에서 직행 |
| 전실에서 | **3인 도착 확인 → 자동 진행** | 편성 UI → 전원 준비 시 진행 |

**정식 런에도 필요하다** — 현재 설계에는 씬 전환 직후 세 사람이 모두 로드되었는지 확인하는 구간이 없어, 한 명이 느리면 혼자 보스 앞에 떨어진다.

⚠️ **`Boss_Arena.unity`의 진입 경로가 둘이 되므로 매니저 다섯의 영속화 계약이 두 경로 모두에서 성립해야 한다.** 씬 목록 자체는 바뀌지 않는다.

### 3.2 Main.unity 하이어라키 설계

각자 Dev 씬도 이 골격을 따라 만든다. 통합 시 "내 루트 오브젝트를 Main에 프리팹으로 옮기는" 작업이 되도록, **파트별 루트는 반드시 프리팹으로 관리**한다.

```
Main.unity
├ @Network              GeumeumNetworkManager, KCP Transport
├ @Managers             GameDBBootstrap, GameEventBus, LobbyController, RunManager, ReviveController, DropDirector,
│                       CurrencyManager, GrowthManager, ScoreManager (GameDBBootstrap/EventBus는 로컬 초기화 오브젝트,
│                       나머지는 NetworkBehaviour — 씬 배치 네트워크 오브젝트)
├ @Stage                Gaia Terrain, 건축물, 라이트, 내비메시  ← 아트 소유
│  ├ SpawnClusters      잡귀 거점 배치 지점 (빈 오브젝트 마커)   ← 아트가 배치, AI가 사용
│  ├ BarrierCenter       결계 중심 고정 마커                     ← 아트가 배치, 서버가 읽기
│  ├ Barrier            결계 비주얼 루트
│  └ BossArea           보스맵 영역
├ @AI                   SpawnManager, BarrierController, BossController
├ @Gameplay             ShopKeeper(객주 NPC), 드랍 아이템 스폰 루트
├ @UI                   HUD / Inventory / Shop / Talent / Barrier경고 / 관전 / 정산 캔버스
└ @Local                CameraRig, DayCycleController, CombatFeedbackImpl
                        (네트워크를 전혀 타지 않는 로컬 전용)
```

`@` 접두사는 "씬 구조 고정 루트"라는 표시다. 루트 오브젝트를 추가 · 삭제하는 변경은 통합 담당자 승인이 필요하다.

`57cb59d` 기준으로 `GameEventBus` · `LobbyController` · `RunManager` 조립은
`Dev_Combat.unity`에서 확인됐고, 위 `Main.unity` 최종 골격의 통합·L3 검증은 아직 남아 있다.

**`@Stage` 배치의 필수 제약** ([시스템 기획 2.1](01-system-design.md) · [기술 구현 4.4](03-technical-design.md)):

| 오브젝트 | 제약 |
|---|---|
| `BossArea` 입구 | `BarrierCenter`로부터의 거리가 **3차 결계 최종 반경보다 작아야 한다.** 3차 구간 30초 × 초당 5 = 누적 150 피해이고 회복 봉쇄까지 걸리므로, 결계 밖을 경유하는 배치는 보스전 진입을 전멸 구간으로 만든다 |
| `ShopKeeper` 위치 | `BarrierCenter`로부터의 거리가 **2차 결계 최종 반경보다 작아야 한다.** 이용 가능 시간이 12:00–15:00뿐이라 결계 밖이면 피해를 맞으며 구매하게 된다 |
| `SpawnClusters` | 각 클러스터가 어느 결계 단계까지 살아남는지 배치 시 확인한다. `SpawnManager`가 범위 밖 클러스터를 비활성화한다 |

---

## 4. 스크립트 폴더 · 클래스 배치

`Assets/01.Scripts/` 아래 전체 구조. **폴더 = 네임스페이스 = 소유권**이 항상 일치해야 한다.
클래스 옆 표기: `NM` = Mirror NetworkManager, `NB` = NetworkBehaviour, `MB` = MonoBehaviour, `C#` = 순수 C# 클래스, `SO` = ScriptableObject, `enum` = 열거형, `static` = 정적 클래스.

> **`Dev/` 하위폴더 규약 (2026-09-05 신설).** 개발 전용 도구 — 플레이어에게 제공되는 기능이 아니라 개발자가 관찰·조작·측정하기 위해 존재하고, 지워도 게임 규칙이 달라지지 않는 것 — 은 소속 파트의 `Dev/` 아래 둔다.
> **네임스페이스는 소속 파트를 그대로 따른다**(`Player/`·`Lobby/`가 그렇듯 `Dev/`도 네임스페이스에 반영하지 않는다). 별도 어셈블리가 아니므로 **폴더만으로는 빌드에서 빠지지 않는다** — 출시 빌드에서 빼려면 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`를 쓴다.
> **원칙: 프로덕션 코드는 `Dev/`의 타입에 의존하지 않는다.** 현재 예외가 하나 있다 — `GeumeumBootValidator`가 `RequireInScene<FrameRecorder>`로 `Main` 씬에 측정기가 있을 것을 강제한다(의도 여부 미확인). 새 예외를 만들지 않는다.

`[계획]` 항목의 구현 계약은 기능별 상세 설계에 있다 — [PlayerAttack](unity/PLAYER_ATTACK_DESIGN.md) · [보스](unity/BOSS_DESIGN.md) · [결계 · 인벤토리 · 상점](unity/BARRIER_INVENTORY_SHOP_DESIGN.md) · [몬스터 패링 연결](unity/MONSTER_PARRY_LINK_DESIGN.md) · [애니메이션 파이프라인](unity/ANIMATION_PIPELINE_DESIGN.md).

### 4.0 00.Common — 공용 (소유: 팀장, 변경은 팀 공지 후)

```
00.Common/
├ Data/                    namespace Geumeum.Data — GameDB 정의 → 6장
│  ├ GameDatabaseSO.cs     SO  루트 DB. 모든 *SO 참조를 모은다
│  ├ GameDB.cs             C#  정적 접근점 (GameDB.Enemy, GameDB.Barrier …)
│  ├ GameDBBootstrap.cs    MB  루트 DB를 한 번 초기화하고 누락 시 즉시 실패시킨다
│  ├ CombatConfigSO.cs     SO  스태미나 · 신력 · 이동 · 구르기 · 패링 (HP는 제외)
│  ├ ArrowConfigSO.cs      SO  화살 발사각 보정 · 유도 각도 · 지속 시간
│  ├ EquipmentSO.cs        SO  등급별 롤 범위, 무기별 특수옵션 풀
│  ├ DropTableSO.cs        SO  필드 레벨별 등급 가중치
│  ├ EnemySO.cs            SO  잡귀 3종 스탯, 결계 단계별 강화 배수
│  ├ BarrierPhaseSO.cs     SO  결계 단계별 시간 · 반경 · 피해량
│  ├ SpawnConfigSO.cs      SO  구간별 동시 존재 수 · 구성 비율 · 리스폰 주기
│  ├ BossPatternSO.cs      SO  보스 페이즈별 패턴 · 쿨다운 · 예고·판정 정보
│  ├ TalentTreeSO.cs       SO  특성 노드 구성과 수치
│  ├ ShopSO.cs             SO  객주 상품 목록과 가격
│  ├ LevelCurveSO.cs       SO  레벨별 필요 경험치 + 최대 HP 전체 (1레벨 기본값 포함)
│  ├ ScoreRuleSO.cs        SO  점수 항목별 배점
│  ├ DayCycleSO.cs         SO  실시간↔게임 내 시각 매핑, 조명 프리셋 (로컬 전용)
│  └ SoundCatalogSO.cs     SO  [현재] SoundKey → 클립 · 믹서 그룹 · 볼륨 · 중복 제거 간격.
│                               믹서 그룹 5개 참조를 여기가 들고 있다 (로컬 전용) → 사운드 설계 4.2
├ Constants/               namespace Geumeum.Common — 코드 계약값 (partial class)
│  ├ Constants.cs          공통: 레이어, 태그, 씬 이름
│  ├ Constants.player.cs   입력 키/축, 판정 epsilon
│  ├ Constants.combat.cs   히트스톱 등 전투 계약값
│  ├ Constants.network.cs  틱 레이트, 동기화 상수
│  └ Constants.sound.cs    [현재] 효과음 소스 수, 믹서 노출 파라미터 이름, dB 범위
├ Utility/                 namespace Geumeum.Common
│  ├ PersistentSingleton.cs  MB  DDOL 싱글턴 베이스 (제네릭 MonoBehaviour — 코딩 컨벤션 18)
│  ├ SceneSingleton.cs       MB  씬 로컬 싱글턴 베이스 (제네릭 MonoBehaviour)
│  ├ DamageTargetResolver.cs C#  [현재] 콜라이더 → IDamageable 해석 창구. 부위 콜라이더와
│  │                             몸통이 같은 NetworkIdentity일 때의 중복 제거를 여기서만 한다
│  ├ CombatFeedbackLocator.cs static ICombatFeedback 등록·조회. 기본값은 아무것도 하지
│  │                             않는 NullCombatFeedback → 8.3
│  └ BarrierStateLocator.cs  static [현재] IBarrierState 등록·조회. 기본값은 결계가 꺼진 것으로
│                                답하는 NullBarrierState라, 결계 구현 전에도 전투·AI·아트가 돈다
├ Events/                  namespace Geumeum.Common — 파트 간 이벤트 계약 → 8장
│  ├ IGameEventBus.cs      C#  타입이 지정된 이벤트 발행 · 구독 계약
│  └ GameEventBus.cs       MB  @Managers가 소유하는 서버/로컬 이벤트 허브.
│                              SceneSingleton<GameEventBus>를 상속한다
├ Types/                   namespace Geumeum.Common — 파트 공용 열거형·값 타입
│  ├ DamageInfo.cs         struct 서버가 확정한 피해량 · 공격자 netId · HitStrength
│  ├ HitStrength.cs        enum 타격감 강도 (Light · Heavy · Critical)
│  ├ WeaponKind.cs         enum 무기 종류 = 직업 (Sword · Bow · Bell)
│  ├ WeaponKindExtensions.cs C# 무기 → AttackReach 변환. 무기가 판정 방식을 정한다
│  ├ AttackReach.cs        enum 기본 공격의 판정 방식 (None · Melee · Ranged)
│  ├ CharacterKind.cs      enum 캐릭터 = 직업 (Chasa · Singung · Munyeo). WeaponKind와 1:1
│  ├ EquipmentOptionKind.cs enum 장비 옵션 종류. 화살 속도 · 유도 시간 · 관통 수 포함
│  ├ EnemyKind.cs / EnemyVisualState.cs / ItemGrade.cs / ShopItemKind.cs
│  ├ RunPhase.cs / RunOutcome.cs / ParryWindowKey.cs
│  ├ SoundKey.cs / SoundChannel.cs / BgmTrack.cs [현재] 사운드 키 → 사운드 설계 2.2 · 3.1
│  └ PlayerPlatformId.cs   struct 재접속에도 유지되는 신원. Steam은 SteamID64, 개발은 실행 인자값
├ Pooling/                 namespace Geumeum.Common — 로컬 표현 오브젝트 풀
│  ├ LocalObjectPoolService.cs MB [현재] 프리팹별 ObjectPool<GameObject>.
│  │                              PersistentSingleton을 상속한다. 호출부는 아직 없다
│  └ LocalPoolRoot.cs      MB  [현재] 풀에 넣어도 되는 프리팹임을 선언하는 표식.
│                              네트워크 개체를 걸러내는 화이트리스트다
├ CameraFrame.cs           static [현재] 카메라 yaw와 입력 허용 플래그.
│                                 카메라(05.Art)가 밀고 전투(02.Combat)가 읽는다.
│                                 오브젝트가 아니라 값이라 로케이터가 필요 없고,
│                                 카메라가 없으면 yaw 0이라 지금과 같이 동작한다
├ Sound/                   namespace Geumeum.Common — 사운드 재생 (전부 로컬 전용)
│  ├ SfxPlayer.cs          static [현재] SoundKey + isLocalPlayer만 받아 재생한다.
│  │                              AudioSource 16개 라운드로빈 · 같은 프레임 중복 제거 ·
│  │                              일시정지 · 프리로드. 아트 구현체가 아니라 GameDB와 같은
│  │                              위상의 공용 서비스라 05.Art가 아니라 여기 둔다 → 사운드 설계 4.1
│  ├ BgmPlayer.cs          MB  [현재] PersistentSingleton. 소스 둘로 크로스페이드,
│  │                              무음을 둔 뒤 다음 트랙 재생(9.8의 무음 3초), 일시정지
│  ├ RunSoundDirector.cs   static [현재] RunPhase → BgmTrack 매핑과 일시정지 전파.
│  │                              RunManager의 SyncVar 훅이 부르고, 반대 방향 호출은 없다
│  ├ SoundBootstrap.cs     MB  [현재] 저장된 볼륨을 첫 프레임에 믹서로 민 뒤 스스로 지운다.
│  │                              씬 로드 직후에 넣으면 믹서가 안 깨어나 값이 덮인다
│  └ SoundVolume.cs        static [현재] 마스터 · BGM · 효과음 볼륨. 0~1 ↔ dB 로그 변환,
│                                 PlayerPrefs 저장, 시작 시 자동 적용. 믹서 노출 파라미터로만
│                                 바꿔 SFX_Remote의 상대 오프셋이 유지된다. UI는 Set만 부른다
├ Dev/                     namespace Geumeum.Common.Environment — 개발 전용 도구
│  └ WeatherTimeDebugScript.cs MB [현재] 시간대 배속(Tab) · 즉시 이동(1~4). 전체 #if 가드됨.
│                              Main · Dev_Barrier에 배치. 날씨 스케줄(Environment/)과는 별개다
└ Interfaces/              namespace Geumeum.Common — 파트 간 경계 → 8장
   ├ IDamageable.cs        피해를 받을 수 있는 대상 (플레이어 · 잡귀 · 보스)
   ├ IRecoveryTarget.cs    [현재] 회복을 받을 수 있는 대상. 무녀 치유(전투)와 회복 물약(시스템)이
                              쇠약 · 회복 봉쇄를 각자 계산하지 않게 하는 단일 창구
   ├ IBarrierState.cs      [현재] 결계의 현재 중심 · 반경 · 밖 판정 · 회복 효율.
                              시스템이 구현하고 전투 · AI · 아트 · UI가 읽는다
   ├ IParryable.cs         패링당할 수 있는 공격의 계약
   ├ IInteractable.cs      상호작용 대상 (객주, 드랍 아이템)
   ├ ICombatFeedback.cs    타격감 호출부 (전투가 호출, 아트가 구현) → 8.3
   ├ IPlayerRoleAssignment.cs 무기 · 외형을 원자적으로 확정하는 계약. 로비가 Combat 타입을
                              직접 참조하지 않도록 하는 경계다
   ├ IPlayerRunLifecycle.cs   런 경계에서 플레이어를 옮기고 상태를 되돌리는 계약
   ├ IPoolable.cs             풀에서 재사용될 때 파티클 · Trail · 코루틴을 되돌리는 계약
   └ IParryWindowTarget.cs    패링 창을 여닫을 수 있는 대상의 계약
```

`Interfaces/`에는 인터페이스만 둔다. 파트가 공유하는 열거형은 `Types/`로 보낸다 — 폴더명과 내용이 어긋나면 다음 사람이 찾지 못한다.

### 4.1 01.Network — 세션 · 런 진행 (소유: 팀장)

```
01.Network/                namespace Geumeum.Network
├ GeumeumNetworkManager.cs NM  Mirror NetworkManager 상속. 호스트 생성 · 접속 · 해제,
│                              최대 3명. 플레이어 프리팹 등록과 연결 수명만 담당
├ ServerBootGate.cs        static GameDB · 런 타임라인 · Transport · 네트워크 프리팹 검사
├ LobbyController.cs       NB  준비 상태와 런 시작 게이트. 슬롯 저장과 역할 배분을 아래 두 곳에
│                              맡기고 외부에는 기존 API를 그대로 노출하는 파사드를 겸한다
├ GeumeumAuthenticator.cs  NA  접속 승인 게이트. 빌드, 신원, 입장 잠금, 중복 신원을 한 번에 본다.
│                              Mirror 96은 인증 성공 후에만 OnServerConnect를 부르므로
│                              입장 검증이 여기 있다 [현재]
├ Identity/
│  ├ IPlayerIdentitySource.cs  원격 연결 -> PlatformId. 트랜스포트마다 신원 출처가 달라 여기서 갈린다
│  ├ DevIdentitySource.cs   MB 개발 KCP 경로. 클라가 보낸 값을 그대로 믿으므로 개발 빌드 밖에서 거부한다
│  └ AuthMessages.cs        struct 인증 요청, 응답, 거부 사유. 형식은 2단계에서 확정했다
├ Lobby/
│  ├ LobbyRoster.cs        NB  슬롯 목록(SyncList) 소유, 신원 해석, 이탈 자리 보존과 반환
│  ├ LobbyRoleCoordinator.cs NB 배분기 호출, 역할 확정, 런 리셋, 일시정지 재개 시 역할 복원
│  └ LobbySlot.cs          struct 네트워크 직렬화 값 타입. PlatformId가 슬롯의 키다
├ LobbyRoleAllocator.cs    C#  무기 셔플과 외형 충돌 해결만 담는 순수 계산기. Mirror와
│                              UnityEngine에 의존하지 않아 접속 없이 결과를 검증할 수 있다.
│                              이미 가진 무기·외형은 보존해 재접속 후에도 직업이 바뀌지 않는다
├ RunManager.cs            NB  런 상태 머신 + 경과 시간 → 5장. 프로젝트의 심장
├ RunPhase.cs              C#  enum { Lobby, Farming, Shrink1, Shrink2, Shrink3, Boss, Result }
│                              직렬화되므로 명시값 고정, 재배열 금지
├ RunPhaseChangedEvent.cs  struct 서버 프로세스 내부 phase 전환 payload
├ RunPhaseTimeline.cs      C#  phase 경계 계산 · 순서/연속성/런 길이 불변식 검사
├ RunTimelineBuilder.cs    static BarrierPhaseSO를 순수 타임라인으로 변환
├ ReviveController.cs      NB  [계획] 사망 감지 → 관전 30초 → 제자리 부활,
│                              노잣돈 차감 요청과 전멸 감지
├ Dev/                     개발 전용 도구 (namespace Geumeum.Network 유지)
│  ├ FrameRecorder.cs      MB  [현재] 이름과 달리 영상 녹화가 아니라 **성능 측정기**다.
│  │                           F9/F10 · 전체 #if 가드. Main · Dev_Combat · Dev_Barrier에 배치.
│  │                           ⚠️ GeumeumBootValidator가 Main 씬에 이것을 강제한다(4장 머리말)
│  └ SteamLobbyHUD.cs      MB  [현재] 로비 ID 표시·참가용 개발 오버레이. 출시 로비 UI가 아니다.
│                              전체 #if 가드. Dev_Combat에 배치
└ AuthorityTickScheduler.cs NB [현재] 서버 전용 공용 시뮬레이션 틱. `FixedUpdate`에서
                               등록 순서(우선순위 정수)대로 콜백 실행:
                               입력 적용(이동) → AI → 투사체 → 능력 판정 → 자원 회복.
                               Rigidbody 조작이 물리 스텝 안에서만 안전하므로 물리 주기를
                               틱 경계로 삼는다 (Fixed Timestep = `SERVER_TICK_INTERVAL`)
```

`57cb59d` 기준으로 `ReviveController`를 제외한 위 네트워크 클래스는 구현되어 있다.
`RunManager.CoServerTick`은 `NetworkTime.time`으로 경과 시간과 phase만 갱신하는 런 시계다.
플레이어·AI·투사체를 함께 실행하는 공용 시뮬레이션 스케줄러로 간주하지 않는다 — 그 역할은
`AuthorityTickScheduler`로 분리한다(2026-08-30 결정). `RunManager`는 phase·타이머 책임만
유지하고, 시스템이 5개 미만이라 별도 이벤트 큐 같은 과설계는 하지 않는다.

### 4.2 02.Combat — 플레이어 · 패링 · 화살 (소유: 팀장)

```
02.Combat/                 namespace Geumeum.Combat
├ Player/
│  ├ PlayerController.cs   NB  [현재] 이동 입력 Command, sequence 검증, 구르기.
│  │                           서버가 Rigidbody 속도로 이동시키고 Y는 중력에 맡긴다
│  ├ PlayerMotionModel.cs  C#  [현재] 서버와 클라 예측이 공유하는 순수 이동 계산
│  ├ PlayerMoveInput.cs    C#  [현재] 순서 · 추정 서버 시각을 포함한 입력 스냅샷
│  ├ PlayerPrediction.cs   MB  [현재] 매 프레임 로컬 예측 · 미확인 입력 버퍼 · 3단계 재조정
│  │                           (데드존 · 지수 수렴 · 즉시 스냅)
│  ├ PlayerActionGate.cs   C#  [현재] phase · 생명 상태 · 무기 · 진행 중 행동 · 쿨다운 · 자원 공통 검증
│  ├ PlayerPresentation.cs NB  [현재] 방향 · 속도를 명시적으로 보관하는 표현 상태.
│  │                           서버는 수락한 이동, 소유 클라는 입력, 원격은 SyncVar
│  ├ PlayerAnimationController.cs MB [현재] PlayerPresentation을 읽어 LateUpdate에서
│  │                           VisualPivot을 회전시키는 표현 전용. 위치 차분을 쓰지 않는다.
│  │                           무기별 Animator를 골라 씌우고(짝이 없으면 모델 기본값 폴백)
│  │                           구르기 · 공격 모션 재생 진입점을 제공한다
│  ├ PlayerLifeState.cs    enum [현재] Alive · Downed · Spectating · ReviveInvulnerable
│  ├ PlayerStatus.cs       NB  [현재] HP · 스태미나 · 신력 · 생명 상태 SyncVar, IDamageable · IRecoveryTarget
│  ├ PlayerWeapon.cs       NB  [현재] 현재 무기 SyncVar(초기 1회 배분). 훅이 무기 모델과
│  │                           애니메이터를 함께 갱신한다. [계획] 서버 전투용 장비 snapshot 추가
│  ├ PlayerWeaponVisual.cs MB  [현재] 활성 캐릭터 모델의 오른손 뼈에 무기 모델을 붙이는 표현 전용.
│  │                           부착 보정값은 아직 전부 0이라 무기가 몸통에 파묻힌다
│  ├ PlayerCharacter.cs    NB  [현재] CharacterKind SyncVar 훅으로 캐릭터 모델을 켜는 표현 전용.
│  │                           차사 · 신궁 · 무녀 3종 바인딩 완료. 캐릭터가 무기를 정한다
│  ├ PlayerRollController.cs NB [현재] 구르기 Command와 서버 실행 · 무적 구간
│  ├ PlayerRunAgent.cs     NB  [현재] 런 경계에서 플레이어 상태를 되돌리는 창구(IPlayerRunLifecycle)
│  ├ PlayerActionContext.cs C# [현재] PlayerActionGate가 읽는 검증 입력 묶음
│  ├ RequestIdTracker.cs   struct [현재] 요청 ID의 중복 · 역순을 막는다. 가변 struct이므로
│  │                           readonly 필드로 잡으면 방어 복사본이 생겨 동작하지 않는다
│  ├ PlayerAttack.cs       NB  [현재] 공격의 유일한 클라이언트 Command 진입점. 근접 부채꼴과
│  │                           화살 발사를 AttackReach로 갈라 서버가 판정하고, 수락 시점에
│  │                           RpcAttackStarted로 공격 시작을 모든 화면에 전파한다.
│  │                           [계획] 연사 · 관통 · 치유 · 버프 변형은 서버가 특성 상태에서 산출
│  └ InteractionController.cs NB [계획] 상호작용 Command와 서버 검증
├ Weapon/                  [계획] 직업별 실행 계약. Mirror 비의존
│  ├ IWeaponAbility.cs / IAbilityRuntime.cs
│  ├ AbilityRequest.cs / AbilityResult.cs
│  ├ TargetQuery.cs / ProjectileSpawnRequest.cs
│  └ SwordAbility.cs / BowAbility.cs / BellAbility.cs / SwordSwingJudge.cs
├ Parry/
│  ├ ParryController.cs    NB  [현재] 추정 서버 시각 Command. [계획] request/window 검증.
│  │                           window ID는 공격자별 단조 증가 uint, 대상당 활성 창 1개
│  │                           (새 창이 이전 미소비 창을 무효화)
│  ├ ParryJudge.cs         C#  [현재] 시간 창 참·거짓만 계산하는 순수 판정
│  ├ ParryRequest.cs / ParryWindow.cs / ParryResultCode.cs [현재] 요청 · 창 · 결과 코드 값 타입
│  └ ParryGauge.cs         NB  [현재] 게이지 SyncVar와 누적 · 소비 · 리셋 서버 API.
│                              치명 배율 roll은 PlayerAttack이 읽어 적용한다.
│                              빗나가면 소비하지 않는다(2026-09-03 확정)
├ Arrow/
│  ├ ArrowProjectile.cs    NB  [현재] 서버 물리 틱마다 SphereCast로 훑는 궤적 · 명중 · 관통.
│  │                           netId로 대상 중복을 막는다. [계획] 장애물 판정 · 유도
│  ├ ArrowLauncher.cs      NB  [계획] 별도 스폰 실행기. 지금은 PlayerAttack이 직접 띄운다
│  └ ~~ArrowGuidance.cs~~   🚫 **폐기 (2026-09-04)** — 락온·유도를 넣지 않으므로 만들지 않는다
│                              화살은 발사 방향으로 직진한다 → 화살 조준 보정 설계 1장
├ Dev/                     개발 전용 도구 (namespace Geumeum.Combat 유지)
│  ├ CharacterPreviewRig.cs MB [현재] 네트워크 없이 캐릭터 3종 × 무기 3종과 애니메이션을 확인하고
│  │                           무기 부착 보정을 조정해 Player.prefab에 저장하는 리그.
│  │                           Dev_Character 씬 전용. 판정 · 스태미나 · 동기화는 확인할 수 없다
│  └ ParryTestDummy.cs     MB  [현재] 패링 검증용 더미. Dev_Combat에 배치.
│                              IParryable 구현이라 #if로 감싸면 계약이 깨진다 — 감싸지 않는다
└ Camera/
   ├ CameraRig.cs          MB  [계획] 3인칭 오버숄더, 완전 로컬
   └ ~~LockOnSystem.cs~~    🚫 **폐기 (2026-09-04)** — 자동 조준이 정밀 사격과 배타적이라 락온을 넣지 않는다.
                              공격 요청은 `RequestId`와 `AimDirection`만 보낸다
```

2026-09-05 기준으로 `Player/` 17개, `Parry/` 6개, `Arrow/` 1개, `Dev/` 2개가 구현되어 있다.
아직 없는 것은 `InteractionController`, `Weapon/`, `Camera/`, 그리고 `Arrow/`의 나머지 둘이다.
나머지는 구현 계약이다. **판정 수식은 순수 C# 클래스에 두고, NetworkBehaviour는 입력·서버 실행·
동기화 경계를 담당한다.** 활 공격도 `PlayerAttack`의 공통 Command를 거치며, `ArrowLauncher`가
별도의 클라이언트 요청 경로를 만들지 않는다.

**공격 능력 구조값은 `WeaponAbilityConfigSO`(6장)가 갖는다 — 피해량 자체는 갖지 않는다.**
`EquipmentSO`가 롤한 무기 공격력을 그대로 쓰고, `WeaponAbilityConfigSO`는 사거리·범위·각도·
쿨다운·자원 비용·치유/버프 환산 비율만 가진다. **아래 자원 비용·수치는 전부 프로토타입
임시값이다 — 게임 디자인 문서 자체가 "모든 수치는 시작점"이라고 전제하므로 플레이테스트로
조정한다:**

| 항목 | 임시값 |
|---|---|
| 차사 기본 공격 자원 | 스태미나 15 |
| 신궁 기본 공격 자원 | 스태미나 12 |
| 무녀 치유 (단일 대상) | 신력 20, 회복량 30, 사거리 8m, 쿨다운 2.5초 |
| 무녀 버프 (파티 단일 종류) | 신력 20, 공격력 +15% · 지속 8초, 쿨다운 2.5초 |
| 입력 발신 주기 | 30Hz (`PLAYER_INPUT_SEND_INTERVAL`, `PLAYER_SYNC_INTERVAL`과 별개 상수) |
| 입력 공백 만료 | 250ms 초과 시 0 입력 |
| 소유자 재조정 임계값 | 위치 오차 0.1m |

무기 교체는 `ReviveInvulnerable` 중에도 허용한다 — 공격·구르기 중이 아니면 막을 이유가 없고,
그 외 상태에서는 애초에 교체 요청 자체가 게이트에서 거부되므로 진행 중 능력을 취소하는
로직은 따로 두지 않는다. `EquipmentInstance`(4.4)의 직렬화 필드 확정은 4.4절 참조.

### 4.3 03.AI — 잡귀 · 보스 · 스폰 (소유: 몬스터·보스 파트 @oname36-creator)

```
03.AI/                     namespace Geumeum.AI
├ Enemy/
│  ├ EnemyController.cs    NB  잡귀 공통: 추적 · 공격 실행 · 사망 → 드랍/경험치 이벤트 발행.
│  │                           EnemySO의 종류 필드로 구동 (종류별 상속 대신 데이터 구동)
│  ├ EnemyStatus.cs        NB  HP SyncVar + 강화 배수 적용. IDamageable 구현.
│  │                           배수는 스폰 시점의 결계 단계로 확정하고 이후 재계산하지 않는다
│  │                           (교전 중 최대 HP가 변하면 체력바가 어긋난다 — 게임 디자인 5.2)
│  ├ EnemyBrainBase.cs     C#  행동 결정 베이스 (서버에서만 Tick)
│  ├ HeokkaebiBrain.cs     C#  허깨비: 근접 추적, 무리 행동, 긴 선딜
│  ├ ChanggwiBrain.cs      C#  창귀: 돌진 → 경직. 선딜이 읽히는 패링 교보재 — IParryable
│  ├ AekgwiBrain.cs        C#  액귀: 사거리 유지 · 후퇴, 저주 투사체 시전
│  └ CurseProjectile.cs    NB  액귀 투사체. 서버 스폰 · 서버 판정 (표현만 클라)
├ Spawn/
│  ├ SpawnManager.cs       NB  RunPhase별 동시 존재 수 · 리스폰 타이머 · 구성 비율 관리.
│  │                           결계 축소 시 범위 밖 클러스터 비활성화
│  └ SpawnCluster.cs       MB  씬에 배치되는 거점 마커 (3~5마리). 위치 고정 · 구성 랜덤
└ Boss/
   ├ BossController.cs     NB  현재 페이즈에 위임만 한다. 페이즈 전환 조건 관리
   ├ BossStatus.cs         NB  보스 HP SyncVar. IDamageable
   ├ BossPhaseBase.cs      C#  페이즈 추상 클래스 (enum+switch 금지 — 기술 구현 4.2)
   ├ Phase1State.cs …      C#  페이즈별 구현. 페이즈마다 IParryable 패턴 최소 1개 필수
   └ BossPatternTelegraph.cs NB 공격 예고 장판. 서버가 (패턴 · 시각 · 위치) 이벤트만 보내고
                              렌더링은 클라 — 기술 구현 3.2 "보이지만 판정에 걸리는 것"
```

### 4.4 04.Systems — 장비 · 경제 · 성장 · 점수 · 결계 (소유: 게임플레이 시스템 파트 @kjw1237)

```
04.Systems/                namespace Geumeum.Systems
├ Equipment/
│  ├ EquipmentInstance.cs  C#  개별 드랍 결과 (굴려진 공격력 · 특수옵션). 직렬화 가능 구조로
│  │                           만든다. Unity/SO 참조는 보유하지 않는다. 필드(2026-08-30 결정):
│  │                           WeaponKind · ItemGrade · InstanceId(uint, 서버 발급) ·
│  │                           AttackPower(float, 등급 롤 범위 내 확정값) ·
│  │                           Options({EquipmentOptionKind, float} 목록)
│  ├ EquipmentInstanceSerializer.cs C# Mirror Reader/Writer 확장. 장비 ID · 종류 · 등급 · 롤 ·
│  │                           옵션 ID/값만 명시적으로 직렬화
│  ├ EquipmentGenerator.cs C#  ★2단계 랜덤: 필드 레벨 → 등급 룰렛 → 롤. 서버에서만 호출.
│  │                           재조정권 = 재생성, 등급 상승권 = 상위 EquipmentSO로 교체 후 재생성
│  ├ DropDirector.cs       NB  잡귀 사망 이벤트 구독 → Generator 호출 → DroppedItem 스폰.
│  │                           보스 사망 시 FieldLevel 3 팀 보상 1회 생성
│  └ DroppedItem.cs        NB  필드에 떨어진 장비. IInteractable, 습득은 [Command] → 서버 확정
├ Inventory/
│  └ InventoryController.cs NB 슬롯 3~4칸. 장착 · 보관 · 파티원 양도 전부 호스트 권위로
│                              소유권 이전 (동시 습득 경합은 서버 도착 순서로 해소)
├ Economy/
│  ├ CurrencyManager.cs    NB  팀 공용 노잣돈 단일 SyncVar. 구매 · 부활 차감 요청을
│  │                           서버 큐로 순차 처리 — 잔액 음수 방지
│  └ ShopKeeper.cs         NB  객주 NPC. RunPhase == Shrink2에만 IInteractable 활성.
│                              상품 · 가격은 ShopSO 참조
├ Growth/
│  ├ GrowthManager.cs      NB  경험치(파티 공유) · 레벨 · 특성 포인트. LevelCurveSO 참조
│  └ TalentController.cs   NB  노드 찍기 · 무료 초기화 · 무기 교체 시 자동 초기화.
│                              효과 적용은 이벤트로 각 파트에 통지 (예: 패링 판정 창 확대)
├ Score/
│  ├ ScoreManager.cs       NB  런 동안 처치 수 · 사망 수 집계, 종료 시 ScoreRuleSO로 정산
│  └ LeaderboardService.cs C#  기록 저장. 저장 방식 미정 — 로컬 JSON으로 시작하고 교체 가능하게
│                             인터페이스 뒤에 숨긴다
└ Barrier/
   ├ BarrierController.cs  NB  현재 단계 · 중심 · 반경 SyncVar. 중심은 씬의 BarrierCenter 고정,
   │                          30초 반경 보간, 10초 전 경고 이벤트,
   │                          매 초 범위 밖 IDamageable에 피해 + 쇠약/회복 봉쇄 플래그
   └ BarrierJudge.cs       C#  ★"반경 밖인가 · 얼마의 피해인가" 계산 전용 순수 C#
```

결계는 잡귀 난이도가 아니라 런 진행에 걸린 시스템이라 이 폴더에 둔다. `SpawnManager`(4.3)와 `BarrierVisual`(4.5)은 `BarrierController`의 SyncVar를 **읽기만** 한다.

### 4.5 05.Art — 연출 · 시간대 · 결계 비주얼 (소유: 테크니컬 아트 @AidenKim923 겸임)

전부 **로컬 전용**이다. 이 폴더의 클래스가 NetworkBehaviour를 상속하면 설계 위반이다.

```
05.Art/                    namespace Geumeum.Art
├ DayCycleController.cs    MB  RunManager의 경과 시간 → DayCycleSO로 조명 · 스카이박스 · 안개
├ BarrierVisual.cs         MB  BarrierController의 반경 SyncVar → 원통 셰이더 · 경계 파티클
├ CombatFeedbackImpl.cs    MB  ICombatFeedback 구현체: 히트 이펙트, 셰이크, 패링 슬로우모션.
│                              OnEnable/OnDisable에서 CombatFeedbackLocator에 등록·해제 → 8.3
├ ScreenEffects.cs         MB  저체력 · 결계 밖 · 쇠약 화면 효과
├ Camera/                  [현재] → 카메라와 락온 사양
│  ├ CameraRig.cs          MB  추적 · 충돌 · 셰이크 · 줌. LateUpdate.
│  │                           Target이 바뀔 수 있어야 한다 — 관전 카메라가 같은 물건이다
│  └ CutsceneDirector.cs   MB  페이즈 전환 8초. Timeline이 카메라를 잡고 끝나면 리그에 돌려준다
└ Shaders/, VFX/           셰이더 · 파티클 에셋
```

**카메라는 `CameraFrame`(`00.Common`)에 yaw만 밀고, 전투는 그 값을 읽는다.** 카메라 오브젝트를 참조하지 않으므로 `02.Combat` → `05.Art` 의존이 생기지 않는다 — 로케이터를 하나 더 만들 이유도 없다.

### 4.6 06.UI — 게임플레이 시스템(@kjw1237)이 전담

기존에는 각 파트가 자기 화면의 UI를 각자 담당했으나, 2026-08-30부로 **UI 전담자를 둔다.** 담당은 게임플레이 시스템(@kjw1237)이 겸임한다. 화면이 어느 파트 소재인지와 무관하게 UI 구현·수정은 전담자를 거친다.

```
06.UI/                     namespace Geumeum.UI
├ Combat/    HUD(HP · 스태미나 · 신력 · 패링 게이지), 정밀 조준 상태, 무녀 표식 슬롯, 관전 화면
├ AI/        보스 HP 바
├ Systems/   인벤토리, 특성 트리, 객주 상점, 정산 · 리더보드,
│            결계 경고 배너 · 미니맵 원
└ Art/       페이드 · 트랜지션, 연출성 UI
```

폴더는 화면이 참조하는 게임 상태의 출처를 기준으로 그대로 나눠 두되(코드가 어느 시스템의 SyncVar · 이벤트를 읽는지 한눈에 보이도록), 소유자는 폴더 구분과 무관하게 @kjw1237 한 명이다.

UI는 게임 상태를 **읽기만** 한다. UI에서 상태를 직접 바꾸는 코드(예: 버튼에서 HP 조작)는 금지 — 반드시 해당 시스템의 Command 경유.

**UI 작업은 씬 · 프리팹 작업을 우선한다.** 레이아웃 · 앵커 · 색상 · 폰트 · 계층 구조처럼 Unity 에디터에서 직접 만들고 눈으로 확인할 수 있는 것은 전부 씬/프리팹에서 만든다. 코드로는 **에디터에서 할 수 없는 것만** 작성한다 — 게임 상태를 읽어 텍스트 · 값 · 활성화 여부를 갱신하는 바인딩, 이벤트 구독, 데이터 기반 반복 생성(슬롯 리스트 등) 같은 것들이다. 코드로 UI 계층을 만들거나(`new GameObject`로 패널 구성 등) 인스펙터에서 되는 값을 코드 상수로 박아 넣지 않는다. 상세 패턴은 [Unity 엔지니어링 패턴 9장](unity/UNITY_ENGINEERING_PATTERNS.md).

---

## 5. RunManager — 런 상태 머신 (프로젝트의 심장)

모든 파트가 이것 하나에 의존하고, 이것은 어느 파트에도 의존하지 않는다.

```
[호스트 권위]
RunManager : NetworkBehaviour
├ SyncVar  float ElapsedSeconds     서버가 관리하는 유일한 경과 시간 원본
├ SyncVar  RunPhase Phase           Lobby → Farming → Shrink1 → Shrink2 → Shrink3 → Boss → Result
│                                   Result → Lobby 만 역방향 (런 재시작, 2026-09-02)
├ SyncVar  bool IsPaused            이탈자를 기다리며 얼어 있다. phase와 직교한다
├ local    RunPhaseChangedEvent     GameEventBus로 서버 프로세스 내부 구간 전환 통지
└ int      FieldLevel               Phase에서 파생 — 드랍 가중치 · 잡귀 강화 배수의 키
```

`ElapsedSeconds`와 `Phase`는 서버만 변경한다. 서버는 `RunManager.CoServerTick`에서
`NetworkTime.time`으로 경과 시간을 다시 계산하고, 클라이언트는 수신한 값을 보간해 표시한다.
클라이언트가 자기 시계로 `Phase`를 전환하지 않는다. 구간의 시작 시각·종료 시각·반경·피해량은
`BarrierPhaseSO`에서 읽는다. 이 코루틴은 현재 런 시계·phase 전용이며 공용 전투 시뮬레이션
스케줄러가 아니다.

### 5.1 전환 계약

| 단계 | 진입 조건 | 종료 조건 | 서버 처리 |
|---|---|---|---|
| `Lobby` | 씬 로드 | 3명 준비 완료 | 런 시계 정지, 역할 배분 준비 |
| `Farming` | 런 시작 | 실시간 10:00 | 개활 스폰, FieldLevel 0 |
| `Shrink1` | 실시간 10:00 | 실시간 **19:00** | 결계 축소·초당 1 피해, FieldLevel 1 |
| `Shrink2` | 실시간 **19:00** | 실시간 **25:00** | 결계 축소·객주 개방, FieldLevel 2 |
| `Shrink3` | 실시간 **25:00** | 실시간 **26:00** | 보스맵 게이트 개방, 신규 잡귀 스폰 중단, FieldLevel 3 |
| `Boss` | 실시간 **26:00** | 보스 사망 또는 실시간 40:00 | **보스맵 씬으로 전환**, 결계 피해 종료 |
| `Result` | 승리·실패·전멸·이탈 대기 종료 | 정산 완료 후 `Lobby` 복귀 | 점수 계산, 성공 런만 리더보드 등록 |

> **이 표의 시각은 [시스템 기획 2.1](01-system-design.md)이 규범이다.** 여기에는 복제본을 두지 않고, 값이 바뀌면 그쪽을 고친 뒤 이 표를 맞춘다.
> 런타임 값은 `BarrierPhaseSO`에서 읽으며 구간이 겹치거나 끊기면 안 된다 — `phase[i].End == phase[i+1].Start`.
> *(2026-09-04 정정: `Shrink1` 종료가 20:00으로 남아 `Shrink2` 시작 19:00과 1분 겹쳐 있었다.)*

`57cb59d`에서 실제로 구현된 전환은 타임라인 경계·시간 초과·런 중 이탈이다.
보스 사망, 전멸, 종료 사유, 점수 정산과 결계·스폰 구독자 side effect는 아직 구현 계약이다.

#### 슬롯의 키는 connectionId가 아니라 PlatformId다 [현재]

`connectionId`는 재접속마다 새로 발급되어 같은 사람을 알아볼 수 없다. 그래서 슬롯과 이탈 자리 보존은
`PlayerPlatformId`로 키를 잡는다. Steam 빌드는 FizzySteamworks가 `conn.address`로 주는 SteamID64를,
개발 KCP 경로는 실행 인자 `-platformid`를 쓴다.

**개발 경로에서 `-platformid`를 주지 않으면 자리와 역할이 복구되지 않는다.** 호스트는 `connectionId`가
항상 0이라 고정값을 쓸 수 있지만 원격은 그렇지 않다. `DevIdentitySource`가 이 경우 경고를 남기고,
`tools/run-3clients.ps1`과 `run-client.bat`이 창마다 고정 신원을 넘긴다.

신원으로 키를 잡으면서 두 명 이상이 동시에 이탈해도 각자 자기 자리로 돌아온다.
예전 선입선출 큐에서는 돌아오는 순서에 따라 자리가 뒤바뀔 수 있었다.

#### 이탈은 런을 끝내지 않는다 - 일시정지 [현재]

런 도중 누가 빠지면 `Result`로 확정하지 않고 `RunManager.PauseRun`이 세계를 얼린다.
보스전 30분을 진행하다 한 명이 튕겼다고 런을 버리면 복구가 아니라 손실이기 때문이다.
멈춰 있는 동안 phase와 경과 시간이 그대로 보존되고, 입장 잠금이 풀려 이탈자가 돌아올 수 있다.

일시정지는 `RunPhase` 값이 아니라 **별도 SyncVar**다. 보스전에서 멈췄다 보스전으로 돌아오므로
phase와 직교하고, `RunPhase`에 값을 더하면 전진 전용 가드와 타임라인 순서 의미가 깨진다.

- 정지: 서버 시뮬레이션(`AuthorityTickScheduler`)과 런 시계를 멈춘다
- 입력: `PlayerActionGate.CanAct`가 `isPaused`를 함께 보므로 남은 사람도 움직이지 않는다
- 예측: `IsPaused`가 SyncVar라 소유 클라이언트도 읽고 로컬 예측을 중단한다. 안 그러면 재개 때 되돌아간다
- 재개: 돌아온 사람이 두고 간 자리와 역할을 물려받고, 멈춰 있던 시간만큼 시계를 밀어 경과 시간을 보존한다
- 포기: 대기를 끊으면 그때 `PlayerLeft`로 확정하고 아래 복귀 경로를 탄다

기다리는 시간에 제한은 없다 [현재]. 남은 인원이 대기를 끊는 UI는 아직 없어 Dev 단축키로만 가능하다 [계획].

#### Result → Lobby 복귀 [현재]

`Result`는 종착점이 아니다. 도달하고 `Constants.Network.RESULT_TO_LOBBY_SECONDS` 뒤에
`RunManager.ServerReturnToLobby`가 `Lobby`로 되돌린다. 같은 프레임에 되돌리지 않는 이유는
SyncVar가 마지막 값만 보내 클라이언트가 `Result`를 아예 수신하지 못하기 때문이다.

복귀 시 서버가 하는 일은 셋이다. 입장 잠금 해제, 로비 슬롯의 준비·무기·외형 플래그 초기화,
플레이어의 `PlayerWeapon`·`PlayerAppearance` 역할 확정과 `PlayerStatus` 시작값 복원.
플레이어 컴포넌트까지 되돌리지 않으면 다음 런의 역할 배분이 "이미 확정됨"으로 거부된다.

전환 자체는 `RunManager` 안에서 전진 전용 가드(`CommitPhase`)와 실제 커밋(`SetPhase`)을
나눠 처리한다. 역방향은 `SetPhase`만 허용하므로 런 시계가 지나간 경계를 되돌릴 수는 없다.
outcome은 여전히 `TryFinishRun`이 한 번만 커밋한다.

`SpawnManager`의 생존 잡귀 정리는 아직 없다 [계획]. 한 서버 세션에서 두 번째 런을 열면
이전 런의 잡귀가 남는다.

`FieldLevel 3`은 일반 잡귀 드랍에 사용하지 않고 보스 처치 후 팀 보상에 한 번만 사용한다. `Result`로 전환하면 모든 입력·드랍·구매 요청을 거부한다.

| 구독자 | ElapsedSeconds / Phase로 하는 일 |
|---|---|
| BarrierController | 단계별 목표 반경으로 축소 시작, 범위 밖 피해량 결정 |
| SpawnManager | 동시 존재 수 · 리스폰 주기 · 구성 비율 전환 |
| DropDirector | FieldLevel → DropTableSO 가중치 선택. 3차는 보스 보상에만 적용 |
| ShopKeeper | Shrink2에서만 상호작용 활성 |
| ScoreManager | 클리어 시간 보너스 계산 |
| DayCycleController | 시각 환산 → 조명 (클라 로컬, 네트워크 무관) |
| UI 각종 | 남은 시간 · 축소 10초 전 경고 표시 |

구간 전환 시각과 배율은 코드가 아니라 `BarrierPhaseSO` · `DayCycleSO`가 가진다. RunManager는 시계를 진행하고 전환 이벤트를 발행하며, 각 파트는 시간을 다시 계산해 자기 상태를 변경하지 않는다.

---

## 6. 데이터 에셋 (GameDB)

정의 클래스는 `01.Scripts/00.Common/Data/`(4.0장), 에셋 인스턴스는 `09.Data/`.
접근은 항상 `GameDB.<도메인>` 정적 프로퍼티 — 개별 컴포넌트가 SO를 SerializeField로 물지 않는다 ([코딩 컨벤션 16](coding-convention.md)).

```
Assets/09.Data/
├ Resources/GameDB.asset        루트 GameDatabaseSO (Resources.Load 진입점)
├ Combat/CombatConfigSO.asset 스태미나 · 신력 · 이동 · 구르기 · 패링 (HP 제외)
├ Arrow/ArrowConfigSO.asset   화살 발사각 보정 · 유도 각도 · 지속 시간
├ Weapon/WeaponAbilityConfigSO.asset [계획] 직업별 공격 능력의 사거리 · 범위 · 각도 ·
│                             쿨다운 · 자원 비용 · 치유/버프 환산 비율 (공격력 자체는 미보유)
├ Equipment/EquipmentSO.asset
├ DropTable/DropTableSO.asset
├ Enemy/EnemySO.asset
├ Barrier/BarrierPhaseSO.asset
├ Spawn/SpawnConfigSO.asset  구간별 동시 존재 수 · 구성 비율 · 리스폰 주기
├ Boss/BossPatternSO.asset   보스 페이즈별 패턴 · 쿨다운 · 예고·판정 정보
├ Talent/TalentTreeSO.asset
├ Shop/ShopSO.asset
├ LevelCurve/LevelCurveSO.asset  레벨별 필요 경험치 + 최대 HP (단일 소스)
├ Score/ScoreRuleSO.asset
└ DayCycle/DayCycleSO.asset   ← 유일하게 네트워크와 무관한 순수 로컬 리소스
```

`GameDBBootstrap`만 `Resources/GameDB.asset`을 로드해 `GameDB.Initialize()`를 호출한다.
호출 시점은 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`로 **씬 로드보다 앞이다.** `@Managers`에는
`NetworkIdentity`가 있어 Mirror가 서버를 열 때까지 오브젝트를 비활성으로 두는데, 그 위의 `Awake`에서
초기화하면 `ServerBootGate`가 서버를 열기 전에 빈 GameDB를 보게 되어 순환이 된다(실행 인자로 호스트를
자동 시작하는 개발 빌드에서 드러났다). 컴포넌트의 `Awake`도 같은 경로를 호출하지만 이미 채워져 있으면
아무것도 하지 않는다. 소비 컴포넌트는 `Awake`의 필드 초기화식이 아니라 `Start`·`OnStartServer` 이후에 GameDB를 읽는다. 루트 DB나 필수 참조가 없으면 조용히 기본값으로 진행하지 않고 초기화 오류로 중단한다. 서버와 클라이언트는 같은 빌드의 GameDB를 사용한다.

새 데이터 도메인 추가 절차: [기술 구현 3.1](03-technical-design.md)에 항목 추가 → `*SO` 클래스 → `GameDatabaseSO` 참조 → `GameDB` 프로퍼티 → 에셋 생성 ([밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md)).

---

## 7. 프리팹 구성

```
Assets/02.Prefabs/
├ Network/
│  └ Player.prefab            NetworkManager 등록 플레이어 프리팹 → 구성 아래 참조
├ Enemy/
│  ├ Heokkaebi / Changgwi / Aekgwi.prefab    잡귀 3종 (서버 스폰)
│  ├ CurseProjectile.prefab   액귀 투사체 (서버 스폰)
│  └ Boss.prefab              보스 (서버 스폰)
├ Combat/
│  └ Arrow.prefab             화살 투사체 (서버 스폰)
├ Systems/
│  └ DroppedItem.prefab       필드 드랍 장비 (서버 스폰)
├ Managers/
│  └ @Managers.prefab, @Network.prefab, @AI.prefab …   씬 골격 루트 (3.2장)
└ Art/
   └ VFX · 연출 프리팹 (로컬 재생 전용, NetworkIdentity 없음)
```

**Player.prefab 컴포넌트 구성**:

```
[현재 — 2026-09-01]
Player.prefab
├ NetworkIdentity / NetworkTransformReliable(syncRotation 꺼짐)
├ PlayerController / ParryController / ParryGauge
├ PlayerStatus / PlayerWeapon / PlayerAppearance
├ PlayerPrediction / PlayerPresentation / PlayerAnimationController / PlayerRollController
├ CapsuleCollider / Rigidbody
└ VisualPivot                   표현용 Yaw만 적용, 물리 · 네트워크 루트는 회전하지 않는다
   ├ Chasa_Art · Singung_Art · Munyeo_Art   모델별 축 보정을 로컬 회전으로 유지
   └ Chisa(비활성)                          교체 전 외부 에셋, 바인딩하지 않는다

[계획 — 단계별 추가]
├ PlayerAttack / InteractionController
├ ArrowLauncher(서버 전용)
├ InventoryController / TalentController
└ VisualPivot 자식: CameraRig 앵커
```

`ArrowLauncher`는 클라이언트 Command를 받지 않고 `BowAbility`의 서버 스폰 요청만 수행한다.
플레이어에 새 NetworkBehaviour를 붙이는 변경은 동기화 대역폭과 파트 경계에 걸리므로 팀장 승인 대상이다.

---

## 8. 파트 간 경계 — 결합 규칙

### 8.1 허용되는 의존 방향

```
Geumeum.Art  ─┐
Geumeum.UI   ─┼──읽기──▶  각 파트의 SyncVar · 이벤트
              │
Combat / AI / Systems ──▶ Geumeum.Network(RunManager) · Geumeum.Common · Geumeum.Data
Combat ✕ AI ✕ Systems 상호 직접 참조 금지 — Common의 인터페이스 · 이벤트 경유
```

예: 잡귀 사망 → 드랍은 `EnemyController`(AI)가 `DropDirector`(Systems)를 호출하는 게 아니라, **AI가 사망 이벤트를 발행하고 Systems가 구독**한다. 반대 방향도 같다.

파트 간 이벤트는 `@Managers`가 소유하는 `GameEventBus`를 통해 전달한다. 이벤트 payload는 결과를 바꾸는 서버 이벤트와 표현 전용 클라이언트 이벤트를 구분하고, 구독자는 `OnEnable`에서 구독한 뒤 `OnDisable`에서 해제한다. 정적 전역 이벤트나 다른 파트의 구체 클래스를 payload 타입으로 사용하지 않는다.

### 8.2 공용 인터페이스가 경계다

| 인터페이스 | 구현 | 호출 |
|---|---|---|
| `IDamageable` | PlayerStatus, EnemyStatus, BossStatus | 공격 판정 · 결계 피해 · 투사체 |
| `IRecoveryTarget` | PlayerStatus | 무녀 치유 · 회복 물약 |
| `IBarrierState` | BarrierController *(미구현 — 그 전까지 `BarrierStateLocator`의 Null 구현)* | 무녀 치유 · 결계 밖 잡귀 이동 · BarrierVisual · UI |
| `IParryable` | 창귀 돌진, 보스 패링 가능 패턴 | ParryController |
| `IInteractable` | ShopKeeper, DroppedItem | 플레이어 상호작용 입력 |
| `ICombatFeedback` | CombatFeedbackImpl | 전투 타이밍 → 로컬 연출 |

### 8.3 타격감 (전투 ↔ 아트)

전투 파트는 타이밍에서 `ICombatFeedback` 호출만 하고, 구현은 아트가 `CombatFeedbackImpl`로 채운다 ([기술 구현 3.3](03-technical-design.md)).

**연결 방식** — Combat이 Art의 클래스를 직접 참조하면 8.1의 의존 방향을 어기므로, `00.Common`의 정적 로케이터를 경유한다.

```
아트 측 — @Local의 CombatFeedbackImpl
    OnEnable  → CombatFeedbackLocator.Register(this)
    OnDisable → CombatFeedbackLocator.Unregister(this)

전투 측 — Player.prefab의 전투 컴포넌트
    Start 에서 1회 캐싱: _combatFeedback = CombatFeedbackLocator.Current;
    이후 호출만 한다
```

```csharp
_combatFeedback.PlayHit(strength, duration);
_combatFeedback.PlayParrySuccess();
_combatFeedback.PlayCritical(multiplier);
```

- `CombatFeedbackLocator.Current`는 **등록된 구현이 없으면 아무것도 하지 않는 `NullCombatFeedback`을 돌려준다.** null 체크를 호출부마다 흩뿌리지 않기 위해서다.
- 따라서 프로토타입(08.30–09.19)에는 `CombatFeedbackImpl`이 씬에 없어도 전투 코드가 그대로 돈다 — 연출은 집중개발 4~5주차 착수.
- 연출은 로컬 표현이므로 **서버에서 호출하지 않는다.** 판정은 서버가 하고, 그 결과 이벤트를 받은 각 클라이언트가 자기 화면에서 호출한다.

---

## 9. 네트워크 권위 요약

판단 기준 한 줄: **이 값이 사람마다 달라지면 게임 결과가 달라지는가.** 상세 · API 매핑은 [네트워크 권위](unity/NETWORK_AUTHORITY.md).

| 항목 | 권위 | 담당 클래스 |
|---|---|---|
| 경과 시간 · 런 상태 | 서버 | RunManager |
| HP · 스태미나 · 신력 | 서버 | PlayerStatus |
| 공격 · 패링 · 화살 판정 | 서버 (판정 수식은 순수 C# ★클래스) | `ParryJudge`, `BarrierJudge` 등 |
| 잡귀 · 보스 AI, 결계 | 서버 | EnemyBrain*, BossPhase*, BarrierController |
| 드랍 · 노잣돈 · 소유권 · 경험치 | 서버 | EquipmentGenerator, CurrencyManager, GrowthManager |
| 사망 · 부활 · 전멸 감지 | 서버 | ReviveController |
| 런 종료 · `Result` 커밋 · 일시정지 · `Lobby` 복귀 | 서버 | RunManager |
| 조명 · 카메라 · 이펙트 · 사운드 · UI | 클라 로컬 | 05.Art, 06.UI, CameraRig |
| 보스 예고 장판 · 결계선 · 액귀 투사체 | **서버가 이벤트, 클라가 렌더링** | BossPatternTelegraph 등 |

네트워크가 걸린 변경은 예외 없이 **3인 접속 L3 검증** 대상이다 ([검증 플레이북](ai/03_VERIFICATION_PLAYBOOK.md)).

---

## 10. 구현 순서와 구조의 대응

마일스톤([기술 구현 6장](03-technical-design.md))을 이 문서의 구조로 번역하면:

| 시기 | 만드는 것 (이 문서 기준) |
|---|---|
| 프로토 1주 | 폴더 골격 · `00.Common` 전체 · GeumeumNetworkManager · **LobbyController(3인 준비 · 무기 배분)** · Player.prefab 뼈대 · 3인 접속 |
| 프로토 2주 | PlayerController/Status 동기화 · EnemyController + 허깨비 Brain |
| 프로토 3주 | ParryController + ParryJudge · DropDirector 최소형 · BarrierController 1단계 · RunManager |
| 집중 1주 | 잡귀 3종 Brain · Barrier 전 단계 · SpawnManager |
| 집중 2주 | EquipmentGenerator 완성 · InventoryController · DroppedItem |
| 집중 3주 | TalentController · ShopKeeper · CurrencyManager |
| 집중 4주 | BossController + Phase 클래스들 |
| 집중 5주 | 05.Art 전체 (CombatFeedbackImpl · DayCycle · BarrierVisual) · 사운드 |

현재 기준선은 프로토 1주의 공용 데이터·부트·로비·런 시계 골격과
Player.prefab의 이동·패링 초기 컴포넌트까지다. 3인 M2와 `PlayerStatus`/`PlayerWeapon`
연결이 끝나기 전에는 프로토 1주 네트워크 범위를 완료로 표시하지 않는다.

`00.Common`(인터페이스 · GameDB · RunPhase enum)이 **1주차 최우선**인 이유: 이것이 없으면 3명이 각자 임시 타입을 만들고, 통합 주에 전부 다시 붙여야 한다.

---

## 11. 이 구조가 지키는 기획 요구 (추적표)

| 기획 요구 | 출처 | 구조상 대응 |
|---|---|---|
| 데디케이티드 전환 여지 | 기술 1.2 | 판정 수식을 순수 C# ★클래스로 분리 (4.2 · 4.3) |
| 밸런스 수치 코드 금지 | 기술 3 | GameDB 단일 소스 (6장) |
| 씬 머지 충돌 방지 | 기술 2.1 | Dev 씬 4개 + Main 통합 전용 + 파트 루트 프리팹화 (3장) |
| 서버는 원본 값 하나만 | 기술 3.2 | RunManager의 ElapsedSeconds 허브 (5장) |
| UI 전담자 지정 (2026-08-30~) | 시스템 5 | 06.UI를 @kjw1237 전담, 씬 작업 우선 원칙 (4.6) |
| 페이즈마다 패링 패턴 | 게임 9.2 | BossPhaseBase + IParryable 계약 (4.3) |
| 아이템 양도 · 재조정권 재사용 | 기술 4.3 | EquipmentSO(설계도) / EquipmentInstance(결과) 분리 (4.4) |
| 호스트 PC 부하 | 기술 4.4 | 05.Art 로컬 전용 강제, VFX 프리팹에 NetworkIdentity 금지 (4.5 · 7장) |
| 스코프 잠금 | 시스템 6 | 보스 1 · 고정 맵 · 제작 없음 — 해당 확장용 폴더 · 클래스를 미리 만들지 않는다 |
| 3인 고정 · 역할 무작위 배분 | 시스템 2.3 | LobbyController가 런 시작을 게이트하고 무기를 중복 없이 배분 (4.1 · 10장) |
| 모든 `[Command]`에 서버 검증 | 기술 3.2 | 판정 수식은 순수 C# ★클래스, NB는 호출자 · 상태 · 거리 검증 후 위임 (4.2 · 9장) |
| 보스맵 · 객주가 결계 안 | 시스템 2.1 | `@Stage` 배치 제약표 — `BarrierCenter` 기준 거리 조건 (3.2) |
| 강화 배수는 스폰 시 확정 | 게임 5.2 | EnemyStatus가 스폰 시점 단계로 고정, 재계산 금지 (4.3) |

---

## 12. 미결정 사항 (구조에 영향 있는 것만)

| 항목 | 현재 구조의 가정 | 결정 시 바뀌는 곳 |
|---|---|---|
| 리더보드 저장 방식 | 로컬 JSON | LeaderboardService 구현체만 교체 |
| 보스 페이즈 수 | 클래스 수 미정 | `Phase1State` 등 페이즈 클래스 추가만 — BossController 불변 |
| 데디케이티드 전환 | 리슨 서버 | ★순수 C# 판정 클래스는 그대로, NB 레이어만 이식 |
