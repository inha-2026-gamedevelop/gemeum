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
| 게임플레이 시스템 | @kjw1237 | `Geumeum.Systems` | `04.Systems/` | `Dev_System.unity` |
| 테크니컬 아트 | @AidenKim923 (겸임) | `Geumeum.Art` | `05.Art/` | `Dev_Art.unity` |
| (공용) | `Geumeum.Data` `Geumeum.Common` | `00.Common/` | — |
| (각자) | `Geumeum.UI` | `06.UI/<파트>/` | — |

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
├ 10.InputSystem/   Input Action 에셋
├ 11.Timeline/      연출 타임라인 (집중개발 4~5주차부터)
├ 12.Materials/     머티리얼 (모델 · 캐릭터별 하위 폴더)
├ 99.ThirdParty/    구매 에셋 원본 — 수정 금지, 커스텀은 밖으로 복사
└ Mirror/           Mirror 패키지 — 수정 금지
```

- `99.ThirdParty/`와 `Mirror/`는 읽기 전용으로 취급한다. 구매 에셋의 머티리얼 · 프리팹을 고치고 싶으면 자기 파트 폴더로 **복사한 뒤** 수정한다 ([씬·에셋 안전](unity/SCENE_AND_ASSET_SAFETY.md)).
- Gaia Terrain 데이터는 테크니컬 아트만 수정한다 ([기술 구현 2.3](03-technical-design.md)).

---

## 3. 씬 구성

### 3.1 씬 목록

```
Assets/00.Scenes/
├ Main.unity          통합 씬. 통합 담당자(팀장)만 수정
├ Dev_Combat.unity    네트워크·전투 작업 씬
├ Dev_AI.unity        몬스터·보스 작업 씬
├ Dev_System.unity    게임플레이 시스템 작업 씬
└ Dev_Art.unity       테크니컬 아트 작업 씬
```

씬 전환은 없다. **런 전체(접속 → 파밍 → 보스 → 정산)가 `Main.unity` 하나에서 돈다.**
로비 · 정산 화면은 씬이 아니라 UI 패널 전환으로 처리한다 — 씬 전환은 Mirror의 씬 동기화 복잡도를 끌어들이는데, 40분 단일 런 구조에서는 얻는 것이 없다.

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
클래스 옆 표기: `NB` = NetworkBehaviour, `MB` = MonoBehaviour, `C#` = 순수 C# 클래스, `SO` = ScriptableObject, `enum` = 열거형, `static` = 정적 클래스.

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
│  └ DayCycleSO.cs         SO  실시간↔게임 내 시각 매핑, 조명 프리셋 (로컬 전용)
├ Constants/               namespace Geumeum.Common — 코드 계약값 (partial class)
│  ├ Constants.cs          공통: 레이어, 태그, 씬 이름
│  ├ Constants.player.cs   입력 키/축, 판정 epsilon
│  ├ Constants.combat.cs   히트스톱 등 전투 계약값
│  └ Constants.network.cs  틱 레이트, 동기화 상수
├ Utility/                 namespace Geumeum.Common
│  ├ PersistentSingleton.cs  MB  DDOL 싱글턴 베이스 (제네릭 MonoBehaviour — 코딩 컨벤션 18)
│  └ SceneSingleton.cs       MB  씬 로컬 싱글턴 베이스 (제네릭 MonoBehaviour)
├ Events/                  namespace Geumeum.Common — 파트 간 이벤트 계약 → 8장
│  ├ IGameEventBus.cs      C#  타입이 지정된 이벤트 발행 · 구독 계약
│  └ GameEventBus.cs       MB  @Managers가 소유하는 서버/로컬 이벤트 허브.
│                              SceneSingleton<GameEventBus>를 상속한다
├ Types/                   namespace Geumeum.Common — 파트 공용 열거형·값 타입
│  ├ DamageInfo.cs         struct 서버가 확정한 피해량 · 공격자 netId · HitStrength
│  ├ HitStrength.cs        enum 타격감 강도 (Light · Heavy · Critical)
│  └ WeaponKind.cs         enum 무기 종류 = 직업 (Sword · Bow · Bell)
└ Interfaces/              namespace Geumeum.Common — 파트 간 경계 → 8장
   ├ IDamageable.cs        피해를 받을 수 있는 대상 (플레이어 · 잡귀 · 보스)
   ├ IParryable.cs         패링당할 수 있는 공격의 계약
   ├ IInteractable.cs      상호작용 대상 (객주, 드랍 아이템)
   ├ ICombatFeedback.cs    타격감 호출부 (전투가 호출, 아트가 구현) → 8.3
   └ CombatFeedbackLocator.cs static ICombatFeedback 등록·조회. 기본값은 아무것도 하지
                              않는 NullCombatFeedback → 8.3
```

`Interfaces/`에는 인터페이스만 둔다. 파트가 공유하는 열거형은 `Types/`로 보낸다 — 폴더명과 내용이 어긋나면 다음 사람이 찾지 못한다.

### 4.1 01.Network — 세션 · 런 진행 (소유: 팀장)

```
01.Network/                namespace Geumeum.Network
├ GeumeumNetworkManager.cs NB  Mirror NetworkManager 상속. 호스트 생성 · 접속 · 해제,
│                              최대 3명. 플레이어 프리팹 등록과 연결 수명만 담당
├ ServerBootGate.cs        static GameDB · 런 타임라인 · Transport · 네트워크 프리팹 검사
├ LobbyController.cs       NB  3명 준비 상태와 런 시작 요청을 서버에서 관리. 런 시작 시
│                              검·활·방울을 중복 없이 배분하고, 이후 신규 접속·재접속 거부
├ RunManager.cs            NB  런 상태 머신 + 경과 시간 → 5장. 프로젝트의 심장
├ RunPhase.cs              C#  enum { Lobby, Farming, Shrink1, Shrink2, Shrink3, Boss, Result }
│                              직렬화되므로 명시값 고정, 재배열 금지
├ RunPhaseChangedEvent.cs  struct 서버 프로세스 내부 phase 전환 payload
├ RunPhaseTimeline.cs      C#  phase 경계 계산 · 순서/연속성/런 길이 불변식 검사
├ RunTimelineBuilder.cs    static BarrierPhaseSO를 순수 타임라인으로 변환
└ ReviveController.cs      NB  [계획] 사망 감지 → 관전 30초 → 제자리 부활,
                               노잣돈 차감 요청과 전멸 감지
```

`57cb59d` 기준으로 `ReviveController`를 제외한 위 네트워크 클래스는 구현되어 있다.
`RunManager.CoServerTick`은 `NetworkTime.time`으로 경과 시간과 phase만 갱신하는 런 시계다.
플레이어·AI·투사체를 함께 실행하는 공용 시뮬레이션 스케줄러로 간주하지 않는다.

### 4.2 02.Combat — 플레이어 · 패링 · 화살 (소유: 팀장)

```
02.Combat/                 namespace Geumeum.Combat
├ Player/
│  ├ PlayerController.cs   NB  [현재] 이동 입력 Command. [계획] sequence·상태·충돌 검증
│  ├ PlayerMotionModel.cs  C#  [현재] 서버와 클라 예측이 공유할 순수 이동 계산
│  ├ PlayerMoveInput.cs    C#  [계획] 순서 · 추정 서버 시각을 포함한 입력 스냅샷
│  ├ PlayerPrediction.cs   MB  [계획] 로컬 입력 버퍼 · 서버 스냅샷 재조정
│  ├ PlayerActionGate.cs   C#  [계획] phase·생명 상태·쿨다운·자원 공통 검증
│  ├ PlayerLifeState.cs    enum [계획] Alive · Downed · Spectating · ReviveInvulnerable
│  ├ PlayerStatus.cs       NB  [계획] HP · 스태미나 · 신력 · 생명 상태 SyncVar, IDamageable
│  ├ PlayerWeapon.cs       NB  [계획] 현재 무기와 서버 전투용 장비 snapshot
│  ├ PlayerAttack.cs       NB  [계획] 공격의 유일한 클라이언트 Command 진입점
│  └ InteractionController.cs NB [계획] 상호작용 Command와 서버 검증
├ Weapon/                  [계획] 직업별 실행 계약. Mirror 비의존
│  ├ IWeaponAbility.cs / IAbilityRuntime.cs
│  ├ AbilityRequest.cs / AbilityResult.cs
│  ├ TargetQuery.cs / ProjectileSpawnRequest.cs
│  └ SwordAbility.cs / BowAbility.cs / BellAbility.cs / SwordSwingJudge.cs
├ Parry/
│  ├ ParryController.cs    NB  [현재] 추정 서버 시각 Command. [계획] request/window 검증
│  ├ ParryJudge.cs         C#  [현재] 시간 창 참·거짓만 계산하는 순수 판정
│  └ ParryGauge.cs         NB  [계획] 플레이어 보유 게이지 SyncVar와 1회 치명 소비
├ Arrow/
│  ├ ArrowLauncher.cs      NB  [계획] BowAbility가 호출하는 서버 전용 스폰 실행기, Command 없음
│  ├ ArrowProjectile.cs    NB  [계획] 서버 권위 궤적·명중·관통
│  └ ArrowGuidance.cs      C#  [계획] 발사각 보정·유도 계산
└ Camera/
   ├ CameraRig.cs          MB  [계획] 3인칭 오버숄더, 완전 로컬
   └ LockOnSystem.cs       MB  [계획] 로컬 후보 선택, 공격 요청에는 후보 netId만 전달
```

`57cb59d`의 실제 구현은 `PlayerController`, `PlayerMotionModel`, `ParryController`,
`ParryJudge` 네 개다. 나머지는 구현 계약이다. **판정 수식은 순수 C# 클래스에 두고,
NetworkBehaviour는 입력·서버 실행·동기화 경계를 담당한다.** 활 공격도 `PlayerAttack`의
공통 Command를 거치며, `ArrowLauncher`가 별도의 클라이언트 요청 경로를 만들지 않는다.

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
│  │                           만든다. Unity/SO 참조는 보유하지 않는다
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
└ Shaders/, VFX/           셰이더 · 파티클 에셋
```

### 4.6 06.UI — 각 파트가 자기 화면을 소유

```
06.UI/                     namespace Geumeum.UI
├ Combat/    HUD(HP · 스태미나 · 신력 · 패링 게이지), 락온 마커, 관전 화면   ← 팀장
├ AI/        보스 HP 바                                                    ← 몬스터 파트
├ Systems/   인벤토리, 특성 트리, 객주 상점, 정산 · 리더보드,
│            결계 경고 배너 · 미니맵 원                                      ← 시스템 파트
└ Art/       페이드 · 트랜지션, 연출성 UI                                    ← 팀장 (아트 겸임)
```

UI는 게임 상태를 **읽기만** 한다. UI에서 상태를 직접 바꾸는 코드(예: 버튼에서 HP 조작)는 금지 — 반드시 해당 시스템의 Command 경유.

---

## 5. RunManager — 런 상태 머신 (프로젝트의 심장)

모든 파트가 이것 하나에 의존하고, 이것은 어느 파트에도 의존하지 않는다.

```
[호스트 권위]
RunManager : NetworkBehaviour
├ SyncVar  float ElapsedSeconds     서버가 관리하는 유일한 경과 시간 원본
├ SyncVar  RunPhase Phase           Lobby → Farming → Shrink1 → Shrink2 → Shrink3 → Boss → Result
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
| `Shrink1` | 실시간 10:00 | 실시간 20:00 | 결계 축소·초당 1 피해, FieldLevel 1 |
| `Shrink2` | 실시간 20:00 | 실시간 29:30 | 결계 축소·객주 개방, FieldLevel 2 |
| `Shrink3` | 실시간 29:30 | 실시간 30:00 | 보스맵 이동, 신규 잡귀 스폰 중단, FieldLevel 3 |
| `Boss` | 실시간 30:00 | 보스 사망 또는 실시간 40:00 | 결계 피해 종료, 보스 전투 또는 시간 초과 실패 |
| `Result` | 승리·실패·전멸·이탈 | 정산 완료 | 점수 계산, 성공 런만 리더보드 등록 |

`57cb59d`에서 실제로 구현된 전환은 타임라인 경계·시간 초과·런 중 이탈이다.
보스 사망, 전멸, 종료 사유, 점수 정산과 결계·스폰 구독자 side effect는 아직 구현 계약이다.

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

`GameDBBootstrap`만 `Resources/GameDB.asset`을 로드해 `GameDB.Initialize()`를 호출한다. `@Managers`의 초기화 순서는 `GameDBBootstrap` → `GameEventBus` → 네트워크 매니저이며, 소비 컴포넌트는 `Awake`의 필드 초기화식이 아니라 `Start`·`OnStartServer` 이후에 GameDB를 읽는다. 루트 DB나 필수 참조가 없으면 조용히 기본값으로 진행하지 않고 초기화 오류로 중단한다. 서버와 클라이언트는 같은 빌드의 GameDB를 사용한다.

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
[현재 — 57cb59d]
Player.prefab
├ NetworkIdentity / NetworkTransformReliable
├ PlayerController / ParryController
└ CapsuleCollider · MeshFilter · MeshRenderer

[계획 — 단계별 추가]
├ PlayerStatus / PlayerWeapon / PlayerAttack
├ PlayerPrediction / InteractionController
├ ParryGauge / ArrowLauncher(서버 전용)
├ InventoryController / TalentController
└ 자식: CameraRig 앵커, 모델 · 애니메이터
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
| `IParryable` | 창귀 돌진, 보스 패링 가능 패턴 | ParryJudge |
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
- 따라서 프로토타입(09.12–10.03)에는 `CombatFeedbackImpl`이 씬에 없어도 전투 코드가 그대로 돈다 — 연출은 집중개발 4~5주차 착수.
- 연출은 로컬 표현이므로 **서버에서 호출하지 않는다.** 판정은 서버가 하고, 그 결과 이벤트를 받은 각 클라이언트가 자기 화면에서 호출한다.

---

## 9. 네트워크 권위 요약

판단 기준 한 줄: **이 값이 사람마다 달라지면 게임 결과가 달라지는가.** 상세 · API 매핑은 [네트워크 권위](unity/NETWORK_AUTHORITY.md).

| 항목 | 권위 | 담당 클래스 |
|---|---|---|
| 경과 시간 · 런 상태 | 서버 | RunManager |
| HP · 스태미나 · 신력 | 서버 | PlayerStatus |
| 공격 · 패링 · 화살 판정 | 서버 (판정 수식은 순수 C# ★클래스) | ParryJudge, ArrowGuidance 등 |
| 잡귀 · 보스 AI, 결계 | 서버 | EnemyBrain*, BossPhase*, BarrierController |
| 드랍 · 노잣돈 · 소유권 · 경험치 | 서버 | EquipmentGenerator, CurrencyManager, GrowthManager |
| 사망 · 부활 · 전멸 감지 | 서버 | ReviveController |
| 런 종료 · `Result` 커밋 | 서버 | RunManager |
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
| UI 전담자 없음 | 시스템 4 | 06.UI 파트별 하위 폴더 소유 (4.6) |
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
