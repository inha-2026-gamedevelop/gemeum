# 보스 1마리 설계

> 상태: 설계 확정본, 구현 전  
> 출처: 2026-09-01 외부 소스 감사와 함께 받은 설계 산출물. 저장소 문서로 편입했다  
> 관련: [프로젝트 구조 설계서](../04-architecture.md) · [네트워크 권위](NETWORK_AUTHORITY.md) · [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)

> 상태: 구현 제안. 코드·씬·프리팹은 수정하지 않았다.
>
> 핵심 결정: **외세 침입군 병사 원혼 1마리, 2페이즈, MVP 8개 데이터 항목(실행 템플릿 4종)**으로 고정한다. 기존 초안의 약 18개 개별 패턴은 3인·11주 일정에서 애니메이션/VFX/네트워크 검증 비용이 과도하므로 출시 후보기능으로 내린다.

## 1. 목표와 범위

이번에 만드는 것은 서버 권위 보스 1마리, HP 기반 2페이즈, 서버 패턴 선택·명중 판정, 모든 클라이언트에 동일한 예고 표현, 페이즈별 패링 가능 패턴 1개 이상, 보스 사망에 따른 `Result` 진입이다.

MVP 패턴은 아래 8개다. 보스 정체는 확정 문서인 `docs/02-game-design.md`를 따라 **외세 침입군 병사 원혼**으로 고정한다. `docs/boss-pattern-design-2026-08-31.md`의 여우/거대 괴수 초안은 채택하지 않는다. 패턴 이름은 무기·진형·포격을 기준으로 한 기계적 작업명이다.

| 페이즈 | 패턴 | 실행 템플릿 | 패링 | 세 직업의 참여 창 |
|---|---|---|---|---|
| 1 | 3연 근접 | `MeleeArc` | 마지막 타격 가능 | 차사는 패링, 신궁은 거리 딜, 무녀는 이동 중 지원 |
| 1 | 순간 접근 연계 | `TargetedDash` | 불가 | 지목자는 회피, 나머지는 후방 딜 |
| 1 | 개인 표식 | `MarkedCircle` | 불가 | 전원 각자 이동, 직업 강제 없음 |
| 1 | 전방 충격파 | `ConeBurst` | 불가 | 측후방 진입·원거리 딜 창 제공 |
| 2 | 장병기 내려찍기 | `MeleeArc` | 가능 | 성공 시 팀 공용 그로기 창 |
| 2 | 비유도 탄막형 충격파 | `ConeBurst` 반복 펄스 | 불가 | 이동 경로 읽기, 특정 직업 면역 없음 |
| 2 | 무작위 대상 돌진 | `TargetedDash` | 불가 | 최근 지목자 제외로 참여 분산 |
| 2 | 순차 표식 폭발 | `MarkedCircle` 변형 | 불가 | 3인 모두 순서대로 대응 |

만들지 않는 것은 보스 2마리 이상, 절차 생성 아레나, 분신의 독립 HP/AI, QTE, 파괴 지형, 18개 전용 실행 클래스, 데디케이티드 서버 전환이다. 특히 분신·QTE·파괴 지형은 각각 별도의 네트워크 상태·UI·맵 협업을 요구하므로 이번 일정에는 넣지 않는다.

## 2. 권위 경계

| 값 · 판정 | 서버 | 클라이언트 | 근거 |
|---|---|---|---|
| 보스 생성·파괴 | `NetworkServer.Spawn/Destroy` | 스폰 결과 수신 | Mirror는 서버 권위 오브젝트를 서버가 스폰하도록 한다. [Mirror Network Identity](https://mirror-networking.gitbook.io/docs/manual/components/network-identity) |
| HP·사망·현재 페이즈 | `[Server]`에서만 변경, HP와 페이즈는 `[SyncVar]` | 읽기와 UI·모델 전환만 | SyncVar는 서버에서 클라이언트로 상태를 동기화한다. [Mirror SyncVars](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization/syncvars) |
| 대상 선택·패턴 선택·쿨다운 | 서버 전용 필드 | 보유하지 않음 | 결과를 바꾸는 AI 판단이다. Mirror의 기본 모델도 서버 권위다. [Mirror Authority](https://mirror-networking.gitbook.io/docs/manual/guides/authority) |
| 예고 위치·방향·발동 서버 시각 | 서버가 확정 후 `RpcBeginTelegraph` 1회 | `NetworkTime.time` 기준으로 로컬 메시/VFX/사운드 재생 | 매 프레임 반경·진행률을 보내지 않고 원본 이벤트만 전송한다. RPC는 서버→클라이언트 일회성 동작에 맞는다. [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions) |
| 충돌·피해·패링 성공·그로기 | `[Server]` 판정 후 `IDamageable`/`IParryable` 호출 | 결과 연출만 | 클라이언트 애니메이션/Animation Event가 피해를 만들지 않는다. |
| 보스 위치·회전 | 서버 `NavMeshAgent`/이동 실행 | 서버 상태 보간 | 네트워크 결과가 달라지는 이동이므로 서버가 원본이다. |
| 애니메이션·카메라·파티클 | 공격 시작/피격 결과 이벤트만 서버에서 전달 | 각 클라이언트가 재생 | 판정과 표현을 분리하고 GTX 1060에서 네트워크·CPU 부하를 줄인다. |

`[Command]`는 보스에 두지 않는다. 패링 요청은 소유 플레이어의 현재 `ParryController` Command를 거친다. 공격 요청은 `docs/04-architecture.md`에서 `[계획]`인 `PlayerAttack`이 구현된 뒤 그 소유 플레이어 Command를 거친다. 따라서 보스의 플레이어 피해 수신 통합은 `PlayerAttack` 수직 슬라이스 이후 단계이며, 보스가 클라이언트 입력을 직접 받거나 `requiresAuthority = false`를 쓸 이유는 없다.

## 3. 구조

### 3.1 파일과 책임

| 파일 위치 / namespace | 형식 | 책임 | 기존 접점 |
|---|---|---|---|
| `Assets/01.Scripts/03.AI/Boss/BossSpawner.cs` / `Geumeum.AI` | `NetworkBehaviour` | 현재 phase bootstrap, `Boss` 진입 1회 스폰, 구독 해제 | `RunPhaseChangedEvent`, Mirror spawnable prefab |
| `Assets/01.Scripts/03.AI/Boss/BossController.cs` / `Geumeum.AI` | `NetworkBehaviour` | `TICK_PRIORITY_AI` 서버 틱, 페이즈 위임, 패턴 수명, 대상 로테이션, 예고/취소 RPC | `AuthorityTickScheduler`, `RunManager.Phase` |
| `Assets/01.Scripts/03.AI/Boss/BossStatus.cs` / `Geumeum.AI` | `NetworkBehaviour`, `IDamageable` | HP SyncVar, 사망 1회 확정, 이 문서에서 보강하는 `EnemyDiedEvent.ForBoss` 발행 | `DamageInfo`, `GameEventBus` |
| `Assets/01.Scripts/03.AI/Boss/BossPhaseBase.cs` / `Geumeum.AI` | 순수 C# | 허용 패턴 집합과 선택 규칙 | `BossPatternSO.PhaseEntry` |
| `Assets/01.Scripts/03.AI/Boss/Phase1State.cs` / `Geumeum.AI` | 순수 C# | 1페이즈 패턴 풀 | 위 베이스 |
| `Assets/01.Scripts/03.AI/Boss/Phase2State.cs` / `Geumeum.AI` | 순수 C# | 2페이즈 패턴 풀 | 위 베이스 |
| `Assets/01.Scripts/03.AI/Boss/BossPatternRunner.cs` / `Geumeum.AI` | 서버 전용 C# | 네 템플릿의 반복 impact, 재사용 NonAlloc/중복 제거 버퍼 | `Physics.*NonAlloc`, `IDamageable` |
| `Assets/01.Scripts/03.AI/Boss/BossParryAttack.cs` / `Geumeum.AI` | 서버 수명 객체, `IParryable` | 패링 가능 반복 타격 1회의 취소·그로기 | `IParryWindowTarget`, `ParryWindowKey` |
| `Assets/01.Scripts/00.Common/Types/BossTelegraphCue.cs` / `Geumeum.Common` | `struct` | 공격·반복·대상별 예고의 고정 크기 RPC 값 계약 | AI와 로컬 표현의 구체 클래스 참조 방지 |
| `Assets/01.Scripts/00.Common/Data/BossPatternSO.cs` / `Geumeum.Data` | 기존 `ScriptableObject` 확장 | 2페이즈, 네 템플릿, 8개 패턴의 판정·타이밍 데이터 | `GameDatabaseSO`, `GameDB.Boss` |
| `Assets/01.Scripts/00.Common/Types/RunOutcome.cs` / `Geumeum.Common` | `enum : byte` | 00 산출물의 `None=0`, `Victory=1`, `TimeExpired=2`, `PartyWipe=3`, `PlayerLeft=4` | Network와 점수의 결과 원인 계약 |
| `Assets/01.Scripts/00.Common/Events/EnemyDiedEvent.cs` / `Geumeum.Common` | 기존 확장 | nullable kind, 일반 적의 spawn 시점 field/reward snapshot, `ForEnemy`/`ForBoss` factory | `GameEventBus` |
| `Assets/01.Scripts/01.Network/RunManager.cs` / `Geumeum.Network` | 기존 확장 | 결과 원인 SyncVar와 멱등 `[Server] TryFinishRun(RunOutcome)`으로 성공/실패를 1회 확정 | `EnemyDiedEvent.IsBoss` |
| `Assets/01.Scripts/05.Art/Boss/BossTelegraphView.cs` / `Geumeum.Art` | `MonoBehaviour` | cue를 받아 풀링된 메시/VFX로 표현 | `BossController`가 클라이언트에서 발행하는 읽기 전용 로컬 이벤트 |

`BossController`는 현재 페이즈에 위임만 하고, 페이즈 전환 `switch`를 키우지 않는다. 다만 **패턴마다 클래스를 하나씩 만들지는 않는다.** 네 가지 판정 템플릿만 코드로 두고, 범위·횟수·지연·예고 모양은 `BossPatternSO` 항목으로 변형한다. 이 정도의 데이터 구동은 현재 프로젝트의 `EnemySO`/`SpawnConfigSO` 관례와 맞고, 8~18개 클래스를 유지하는 것보다 팀 규모에 맞다.

별도 보스 사망 이벤트는 만들지 않고 현재 `EnemyDiedEvent.IsBoss` 계약을 살린다. 다만 현재 `EnemyKind`에는 보스가 없으므로 `Kind`를 `EnemyKind?`로 바꾸고 `ForEnemy(enemyNetId, kind, position, killerNetId, fieldLevelAtSpawn, rewardMultiplier)`와 `ForBoss(bossNetId, position, killerNetId)` factory만 생성 경로로 허용한다. 일반 적의 보상 배수는 사망 시 현재 phase를 다시 읽지 않도록 spawn snapshot을 event에 싣고, 보스 factory는 `FieldLevelAtSpawn=3`, `RewardMultiplier=1`로 고정한다. `EnemyStatus.Initialize(fieldLevel)`는 검증된 `fieldLevel`과 해당 `EnemySO.EnhanceEntry.RewardMultiplier`를 서버 전용 필드에 함께 보관하고, `EnemyController.HandleDied`가 그 두 값을 `ForEnemy`에 넘긴다. 사망 시 `RunManager.FieldLevel`을 다시 읽지 않는다. `IsBoss == false`이면 `Kind.HasValue == true`, `IsBoss == true`이면 `Kind == null`을 부트/단위 검사로 보장한다. 기존 잡귀 발행자와 구독자는 factory 및 nullable 분기로 함께 이관하며 `default(EnemyKind)`를 의미 있는 값처럼 사용하지 않는다.

`RunManager`는 서버에서 `EnemyDiedEvent`를 구독하고 `IsBoss`일 때 `TryFinishRun(RunOutcome.Victory)`를 호출한다. 00 산출물에서 정의한 이 공개 `[Server]` API는 첫 호출만 outcome·elapsed·점수 입력 snapshot을 확정하고 시계를 멈춘 뒤 gameplay teardown과 `Result` 전환을 수행한다. 기존 `AbortRun`과 시간초과도 내부적으로 같은 API에 실패 outcome을 넘긴다. 시간초과와 보스 사망이 같은 서버 틱에 겹치면 서버 호출 순서상 먼저 성공한 `TryFinishRun` 하나만 결과를 소유하고, 후속 호출은 false다. 드랍·점수도 같은 이벤트를 구독하되 `IsBoss`를 먼저 분기한다.

### 3.2 패턴 선택과 직업 편중 방지

서버는 현재 페이즈의 후보 중 아래를 모두 만족하는 항목만 고른다.

1. 개별 쿨다운 종료.
2. 직전 패턴과 다른 `PatternId`.
3. 현재 거리·생존 인원 조건 충족.
4. 페이즈 시작 후 `MaxGapWithoutParryable` 안에 패링 가능 패턴이 한 번도 나오지 않았다면 패링 가능 후보를 강제.
5. 지목형 패턴은 최근 지목자 1명을 제외하고 선택한다. 생존자가 1명이면 예외.

직업별 전용 해제 기믹은 만들지 않는다. 차사의 패링 성공은 **팀 전체가 공격할 수 있는 그로기**를 만들고, 신궁만 닿는 약점이나 무녀만 해제하는 디버프는 두지 않는다. 대신 각 페이즈에 근접 안전 창, 원거리 유지 창, 파티 회복 여유가 모두 최소 1회씩 나오도록 패턴 조합을 검증한다. 이 방식은 “모두 필요한 순간”을 별도 미니게임으로 강제하지 않고 기존 공격·패링·지원 루프에서 만든다.

## 4. 데이터

`Assets/01.Scripts/00.Common/Data/BossPatternSO.cs`, namespace `Geumeum.Data`의 기존 SO를 아래처럼 확장한다. 기본값은 **프로토타입 후보**이며 플레이 테스트 전 확정값이 아니다. 4.1의 enum은 이 파일의 중첩 타입으로 두어 별도 경로를 만들지 않는다.

### 4.1 열거형

| 타입 | 값 |
|---|---|
| `BossPatternKind : byte` | `MeleeArc=0`, `TargetedDash=1`, `MarkedCircle=2`, `ConeBurst=3` |
| `BossTargetRule : byte` | `Current=0`, `RandomAliveExcludingLast=1`, `AllAlive=2`, `GroundPoint=3` |
| `TelegraphShape : byte` | `Arc=0`, `Line=1`, `Circle=2`, `Cone=3` |

직렬화되는 enum에는 명시값을 부여하고 재배열하지 않는다.

### 4.2 `BossPatternSO.PatternEntry`

| 필드 | 타입 | 기본값 후보 | 용도 |
|---|---|---:|---|
| `PatternId` | `int` | 101부터 | 런 중 불변 ID |
| `DisplayName` | `string` | 작업명 | UI/로그 |
| `Kind` | `BossPatternKind` | — | 네 실행 템플릿 선택 |
| `TargetRule` | `BossTargetRule` | — | 서버 대상 규칙 |
| `TelegraphShape` | `TelegraphShape` | — | 클라이언트 표현 |
| `TelegraphDuration` | `float` | 0.8~1.4초 | 예고 시작→impact |
| `ActiveDuration` | `float` | 0.10~0.25초 | 마지막 impact 뒤 공격 상태·표현 유지 시간 |
| `RecoveryDuration` | `float` | 0.8~1.8초 | 다음 행동 전 빈틈 |
| `Cooldown` | `float` | 6~12초 | 개별 재사용 대기 |
| `Damage` | `float` | 20~45 | 서버 피해량 |
| `Range` | `float` | 3~18m | 길이/사거리 |
| `Radius` | `float` | 1.5~5m | 선·원의 폭 |
| `AngleDegrees` | `float` | 70~180도 | 부채꼴 각도 |
| `RepeatCount` | `int` | 1~3 | 동일 템플릿 반복 |
| `RepeatInterval` | `float` | 0.25~0.6초 | 반복 간격 |
| `ParryableRepeatIndex` | `int` | `-1` 또는 마지막 반복 index | `-1`은 패링 불가, 그 외에는 해당 반복만 패링 가능 |
| `ParryWindowDuration` | `float` | 0.32초 | 패링 가능 공격만 사용 |
| `ParryResponseRange` | `float` | 4m | 검 사용자가 팀 패링 창을 받을 수 있는 최대 거리 |
| `StaggerDuration` | `float` | 3~5초 | 성공 시 팀 딜 창 |
| `SelectionWeight` | `float` | 1 | 후보 가중치 |
| `MinTargetDistance` / `MaxTargetDistance` | `float` | 패턴별 | 거리 부적합 후보 제외 |
| `DashSpeed` | `float` | 8~12m/s | `TargetedDash` 서버 이동 속도 |
| `DashHitRadius` | `float` | 1.2m | 돌진 충돌 반경 |
| `DashStopDistance` | `float` | 0.5m | 목표 앞 정지 거리 |

`ActiveDuration`은 impact 뒤 공격 모션·위험 표현을 유지하는 시간이다. 피해는 각 반복의 `impactServerTime`마다 NonAlloc 질의 한 번으로만 확정하며, 지속 프레임 판정을 뜻하지 않는다. 비유도 탄막형 충격파도 네트워크 projectile을 만들지 않고 `RepeatCount`개의 서로 다른 방향 cone impact와 로컬 발사체형 VFX로 표현한다. 실제 투사체로 바꾸는 것은 다섯 번째 실행 템플릿과 M3 투사체 검증이 필요한 후속 범위다.

부트 검사는 모든 float의 finite 여부, 시간·거리·피해·가중치의 비음수, `RepeatCount >= 1`, `0 <= ParryableRepeatIndex < RepeatCount` 또는 `-1`, `MinTargetDistance <= MaxTargetDistance`, dash 전용 필드 양수를 확인한다. 패링 가능 항목은 `TelegraphDuration >= GameDB.Combat.ParryStartupDuration + min(ParryWindowDuration, GameDB.Combat.ParryWindowDurationMax) + Constants.Network.PARRY_RESOLUTION_LEAD`를 만족해야 한다. 0.18초 판정 여유는 `Constants.Network`가 단독 소유하며 Boss SO에 복제하지 않는다. 데미지와 타이밍을 애니메이션 클립 길이에서 역산하지 않는다.

### 4.3 `BossPatternSO.PhaseEntry`

| 필드 | 타입 | 기본값 후보 |
|---|---|---:|
| `PhaseIndex` | `int` | 1, 2 |
| `EnterHealthRatio` | `float` | 1.0, 0.5 |
| `PatternIds` | `int[]` | 페이즈당 4개 |
| `DecisionDelayMin` / `DecisionDelayMax` | `float` | 0.35 / 0.75초 |
| `MaxGapWithoutParryable` | `float` | 25초 |
| `TransitionLockDuration` | `float` | 2초 |

phase 검사는 index 1→2의 연속성, `EnterHealthRatio`의 1.0→0.5 단조 감소, 유효 PatternId만 참조하는지, 각 phase에 `ParryableRepeatIndex >= 0`인 항목이 최소 1개인지 확인한다. `BossPatternSO` 루트의 `_maxHealth` 후보는 3인 DPS 측정 전 3,000으로 두되 확정하지 않는다. 같은 에셋에 `HitColliderBufferSize` 같은 기술 한계는 넣지 않고 코드의 안전 상한으로 둔다. 반대로 플레이 체감에 영향을 주는 시간·거리·피해는 모두 SO에 둔다.

### 4.4 `BossTelegraphCue`

| 필드 | 타입 | 의미 |
|---|---|---|
| `AttackInstanceId` | `uint` | 보스별 단조 증가 공격 ID, 0 금지 |
| `PatternId` | `int` | 로컬 `GameDB.Boss` 조회 키 |
| `RepeatIndex` | `byte` | 같은 공격 안의 반복 번호 |
| `TargetNetId` | `uint` | 개인 대상, 지면/공용 cue면 0 |
| `Origin` / `Direction` / `Center` | `Vector3` | 판정과 표현이 공유하는 서버 확정 원본 |
| `ImpactServerTime` | `double` | `NetworkTime.time` 기준 절대 impact 시각 |

이 RPC 값은 Mirror Weaver가 읽어 쓸 수 있는 **public 값 필드의 mutable `struct`**로 선언하고, 서버는 생성 뒤 복사본을 변경하지 않는다. 현재 `ParryWindowKey`와 같은 직렬화 형태이며 get-only property나 `readonly struct`에 대한 자동 역직렬화를 가정하지 않는다. 클라이언트 표현 키는 `(AttackInstanceId, RepeatIndex, TargetNetId)`다. `AllAlive`와 순차 표식은 최대 3명이므로 가변 배열을 직렬화하지 않고 대상·반복마다 cue 한 건을 신뢰 채널 RPC로 보낸다. 같은 키를 다시 받으면 재생하지 않으며 취소 RPC도 같은 키 또는 `AttackInstanceId` 전체를 지정한다.

Unity는 ScriptableObject를 GameObject 인스턴스와 분리된 공유 데이터 컨테이너로 설명한다. 이미 `GameDB`가 같은 방식을 쓰므로 별도 행동 트리 에셋을 들이지 않고 현재 SO를 확장하는 것이 학습·통합 비용이 가장 낮다. [Unity ScriptableObject](https://docs.unity3d.com/6000.1/Documentation/Manual/class-ScriptableObject.html)

## 5. 흐름

### 5.1 소환과 페이즈

1. `RunManager`가 서버에서 `Boss`에 진입한다.
2. `BossSpawner.OnStartServer`는 먼저 이벤트를 구독한 뒤 현재 `RunManager.ServerInstance.Phase`도 읽는다. 이미 `Boss`라면 같은 bootstrap 경로로 처리하고, spawn 완료 flag로 이벤트와 bootstrap의 중복 생성을 막는다.
3. `BossSpawner`가 직렬화된 보스 프리팹 1개를 생성하고 `NetworkServer.Spawn`한다. 구현 전 NetworkIdentity, 서버 권위 NetworkTransform, spawnable-prefab 등록을 검사한다.
4. `BossStatus.OnStartServer`가 `GameDB.Boss.MaxHealth`를 읽어 HP를 초기화한다.
5. `BossController`는 1페이즈를 만들고 `AuthorityTickScheduler`의 `TICK_PRIORITY_AI`에 서버 Tick을 등록한다. `OnStopServer`에서 반드시 해제한다.
6. HP 비율이 다음 `EnterHealthRatio` 이하가 되면 현재 공격과 모든 대상의 패링 창을 닫고 전환 잠금 후 다음 페이즈를 한 번만 커밋한다. 같은 틱의 다중 피해는 이미 커밋된 phase index를 다시 넘지 못한다.

### 5.2 공격 1회

1. 서버가 유효 후보와 대상 집합을 선택하고 단조 증가 `attackInstanceId`를 발급한다. 살아 있는 검 사용자가 `ParryResponseRange` 안에 있을 때만 이번 패링 가능 패턴을 `MaxGapWithoutParryable` 충족으로 센다.
2. 서버가 각 대상·반복의 위치·방향·중심·`impactServerTime`을 모두 확정하고 `RpcBeginTelegraph(BossTelegraphCue cue)`를 보낸다. `AllAlive`/순차 표식은 대상·반복마다 한 건이다.
3. 클라이언트는 SO에서 모양을 찾아 풀링된 메시/VFX와 사운드를 재생한다. 같은 cue key는 dedupe한다. 서버 시각보다 늦게 받았으면 남은 시간만 재생하고 이미 impact가 지났으면 예고를 생략한다.
4. `RepeatIndex == ParryableRepeatIndex`이면 서버는 **공격 지목자와 별개로** 살아 있는 검 사용자 중 `ParryResponseRange` 안의 한 명에게 `IParryWindowTarget.OpenParryWindow`를 호출한다. 이때 key는 `(boss.netId, attackInstanceId)`이고 SO에는 별도 lead 값을 두지 않는다. 해당 사용자가 없거나 등록이 실패하면 공격은 계속하지만 이 실행은 “패링 기회 제공” 통계에 세지 않는다.
5. 각 `impactServerTime`에서 `BossPatternRunner`가 NonAlloc 물리 질의를 한 번 수행하고, 미리 할당한 `uint[]`/count 버퍼로 `netId`별 중복 Collider를 제거한 뒤 `IDamageable.TakeDamage`를 호출한다. 공격당 `HashSet`을 새로 만들지 않는다. Unity의 `Physics.OverlapSphereNonAlloc`은 호출자가 제공한 버퍼에 결과를 쓰며 추가 할당을 피한다. [Unity Physics.OverlapSphereNonAlloc](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Physics.OverlapSphereNonAlloc.html)
6. 패링 성공이면 `BossParryAttack.OnParried`가 해당 반복을 포함한 공격 instance 전체를 취소하고 서버 그로기를 시작한다. 취소 RPC로 모든 클라이언트가 그 instance의 예고를 즉시 지운다.
7. 실패·회피이면 마지막 반복과 `ActiveDuration` 종료 후 recovery를 거쳐 다음 선택으로 간다.

### 5.3 거부·취소 경로

- 대상이 Telegraph 중 죽거나 사라짐: `Current` 대상 패턴은 취소, `AllAlive`는 남은 생존자만 계속.
- 페이즈 전환·보스 사망·`Result`: 활성 공격과 패링 창을 즉시 닫고 예약 판정을 버린다.
- `TargetedDash` 중 대상 소실·막힌 경로·벽 충돌: 서버 이동을 중단하고 아직 지나지 않은 impact를 취소한다. 클라이언트는 취소 cue만 표현한다.
- 데이터 ID 없음·형상 값 불량: 기본 공격으로 대체하지 않고 해당 패턴을 후보에서 제외하고 부트 검사를 실패시킨다.
- 클라이언트 VFX 누락: 서버 판정은 계속하며 명시적 오류 로그를 남긴다. 시각적으로는 미검증 처리한다.
- 한 Collider가 여러 개 겹침: 같은 repeat impact 안에서는 재사용 netId 버퍼로 같은 `NetworkIdentity`에 피해를 한 번만 준다. 다음 repeat는 별도 타격이므로 다시 피해를 줄 수 있다.

## 6. 단계별 구현 순서

1. **수직 슬라이스(16~20시간):** `BossSpawner`, `BossStatus`, `BossController`, `BossTelegraphCue`, 1페이즈 `MeleeArc` 1개, HP/사망, 서버 스폰. 큐브·캡슐과 단색 부채꼴 예고로 3인이 피해·회피·보스 처치를 확인한다. Mirror spawnable-prefab 등록과 phase bootstrap도 이 단계에 끝낸다.
2. **패링 연결(8~12시간):** `BossParryAttack`과 `IParryWindowTarget` 접점, 검 사용자 수신 규칙, 그로기, 취소 경로. 이 단계부터 페이즈 1의 차사 루프가 동작한다.
3. **두 번째 페이즈(10~14시간):** HP 50% 전환, `TargetedDash`/`MarkedCircle`/`ConeBurst`, 페이즈 2 패링 1개, 최근 지목자 제외.
4. **콘텐츠 채움(12~18시간):** 같은 네 템플릿으로 데이터 항목을 8개까지 채우고 애니메이션·VFX를 연결한다.
5. **통합(10~14시간):** 이 문서에서 보강한 `EnemyDiedEvent.ForBoss`와 00의 `RunManager.TryFinishRun(RunOutcome.Victory)`을 보상/점수까지 연결하고 3인 지연·이탈·보스 사망-시간초과 경합을 검증한다.

1단계 완료 정의는 “M2에서 3명이 같은 보스 HP와 같은 예고 위치를 보고, 서버가 한 번만 피해·사망을 확정한다”이다. 최종 완료 정의는 2페이즈 각각 패링 1개, 보스 사망→Result, 20분 반복에서 중복 피해/남은 예고/무한 컬렉션 증가가 없는 상태다.

## 7. 검증

| 등급 | 시나리오 | 성공 신호 |
|---|---|---|
| M0 | `BossPatternSO` 부트 검증 | 중복 ID, 없는 PatternId, 패링 패턴 0개인 페이즈, 잘못된 시간 관계를 각각 거부 |
| M2 | 3인 수직 슬라이스 | 세 화면의 예고 중심·방향·impact가 캡처상 일치, 피해와 Boss spawn/death는 서버에서 1회 |
| M2 | 3인 기본 | 30회 패턴에서 최근 지목자 연속 선택 0회(생존자 2명 이상), 각 페이즈 패링 성공 5/5, 실패 5/5 |
| M2 | 패링 수신자 | 보스 지목자가 활/방울인 경우 각 10회에도 범위 안 검 사용자만 창을 받고 성공 가능; 검 사용자가 사망/범위 밖이면 기회 제공 통계에서 제외 |
| M2 | 다중 Collider | 한 플레이어의 몸 Collider 2개가 범위에 들어가도 HP 차감 1회 |
| M2 | 경계 상태 | Telegraph 중 대상 사망·보스 페이즈 전환·보스 사망 각각 5회, 유령 피해와 남은 장판 0회 |
| M2 | 페이즈/종료 경합 | HP 50%에 같은 틱 다중 피해 20회와 보스 사망-시간초과 동시 조건 20회, 페이즈/Result/보상 커밋 각각 1회; 보스 처치가 먼저 큐에 든 경우 성공 결과 유지 |
| M3 | 서로 다른 3PC 지연/손실 | 50/100/150ms RTT와 2% loss·jitter 조건에서 각 10회, cue key 중복 재생 0, 예고 역재생 0, 서버 결과 3인 일치 |
| M3 | 장기 | 20분 보스 샌드박스 또는 패턴 500회, 활성 공격/텔레그래프/패링 창 컬렉션이 기준치(동시 1~3개)로 복귀 |
| 성능 | GTX 1060 3GB 목표 빌드 | 보스전 VFX 포함 1080p에서 p95 frame 16.67ms 이하, 보스 로직 steady-state GC.Alloc 0B/frame, Unity Profiler VRAM peak 2.5GB 이하를 캡처. 첨부물만으로는 미검증 |

씬·프리팹이 첨부되지 않았으므로 NavMesh 연결, Collider 레이어, VFX 풀, `NetworkTransform` 설정은 현재 **미검증**이다.

## 8. 대안과 선택 근거

### 채택: 코드 페이즈 2개 + SO 패턴 데이터 + 네 실행 템플릿

보통 Unity 소규모 액션 게임은 Animator/코드 상태 머신으로 큰 상태를 관리하고, 조정값과 패턴 목록을 ScriptableObject로 분리한다. Unity 공식 문서가 SO를 공유 데이터 자산으로 설명하며, 현재 저장소도 `GameDB`와 여러 SO를 이미 사용한다. Mirror 역시 서버 권위 상태는 SyncVar, 일회성 표현은 RPC로 나누는 직접적인 도구를 제공한다. [Unity ScriptableObject](https://docs.unity3d.com/6000.1/Documentation/Manual/class-ScriptableObject.html), [Mirror Communications](https://mirror-networking.gitbook.io/docs/manual/guides/communications)

이 프로젝트는 3인 리슨 서버라 호스트가 AI 판정과 렌더링을 함께 맡고, 최소 GPU가 GTX 1060 3GB이며, 세 사람이 11주 안에 완주해야 한다. 따라서 서버에는 네 개의 재사용 NonAlloc 판정 템플릿만 두고 클라이언트에는 cue 기반 풀링 표현만 두는 편이 호스트 CPU·VRAM과 네트워크 표면을 함께 제한한다. 새 행동 트리 도구를 배우거나 18개 전용 클래스를 만드는 대신 Unity/Mirror의 일반적인 SO·SyncVar·RPC 자료를 그대로 검색할 수 있어 11주 일정에도 맞는다.

### 기각: 패턴마다 MonoBehaviour/Strategy 클래스 하나

타입 안정성과 독립 테스트에는 좋지만 18개 후보를 그대로 만들면 클래스·프리팹 바인딩·RPC 취소 경로가 18벌이 된다. 현재 일정에서는 동작보다 배선 검증 시간이 더 커진다. 네 템플릿으로 표현할 수 없는 최종 패턴이 실제 플레이에서 필요하다고 확인될 때만 5번째 템플릿을 추가한다.

### 기각: Animator StateMachineBehaviour가 명중 판정을 호출

애니메이션 교체·프레임 드롭·클라이언트별 재생 시점이 게임 결과를 바꾸게 된다. Animator는 표현만 맡기고 서버 시각과 SO 타이밍이 판정 원본이어야 한다. 애니메이션 이벤트는 발소리·VFX 같은 로컬 표현에만 허용한다.

### 기각: `NetworkAnimator`로 모든 보스 상태 동기화

네트워크 파라미터가 구현을 빠르게 할 수 있지만, 어떤 패턴이 언제 어디를 때리는지라는 판정 계약을 표현 상태에 묶는다. 현재는 `currentPhase` SyncVar와 공격 이벤트 RPC 몇 개면 충분하며 디버깅도 쉽다. Mirror가 NetworkAnimator를 제공한다는 사실과 별개로 판정 데이터는 서버 원본으로 유지한다. [Mirror GameObjects](https://mirror-networking.gitbook.io/docs/manual/guides/gameobjects)

### 기각: 기존 초안 18개를 한 번에 구현

패턴마다 최소 예고·판정·취소·애니메이션·VFX·M2 검증이 필요하다. 18개는 보스 코드보다 콘텐츠 제작 병목이 크다. 8개로 먼저 완주 가능성을 확보하고, M2에서 반복감이 실제로 확인될 때 기존 템플릿의 SO 변형을 최대 2개 추가하는 게이트를 둔다.

## 9. 비용과 위험

**엔지니어링 부분합: 대, 56~78시간.** 프로그래머 1명이 뼈대와 판정을 맡고 M2/M3 검증 인력이 일부 병렬 참여한다는 전제다. 현금 구매비는 이 부분합에서 **0원으로 잡되 구매를 끝냈다는 뜻은 아니다.** `PlayerAttack` 선행 구현, 외세 침입군 병사 원혼 모델, 전용 모션, 최종 VFX·사운드, 보스 아레나/프리팹 배치와 그 구매비는 범위 밖·미산정이며 소싱 승인 뒤 별도 기록한다. 애니메이션 작업은 `05-animation-pipeline-design.md`의 부분합과 중복 합산하지 않고 전체 일정에서 하나의 공용 작업으로 잡는다.

| 위험 | 영향 | 완화 |
|---|---|---|
| 외세 침입군 병사 원혼 아트 자산 미확보 | 모델·VFX·모션 일정 증가 | 병사 무기·진형 기반 placeholder로 판정을 먼저 검증하고 05 문서의 소싱 게이트에서 구매 상한 확정 |
| 3인 DPS를 몰라 HP·그로기 시간이 부정확 | 너무 길거나 순삭 | 모든 수치를 SO로 두고 M2 3회 중앙값으로 조정 |
| 패링 성공이 보스 행동 수명을 못 끊음 | 유령 피해, 핵심 메커닉 신뢰 붕괴 | 공격 instance ID, 명시적 Cancel, 페이즈/사망 시 일괄 정리 테스트 |
| 텔레그래프 RPC 유실/지연 | 피할 수 없는 공격처럼 보임 | 신뢰 채널 사용, `impactServerTime` 기준 보정, 최소 예고시간 M3 검증 |
| 기존 `EnemyDiedEvent`의 보스 kind가 없음 | `default(EnemyKind)` 오해, 구독자 잘못된 드랍 분기 | `EnemyKind?`, `IsBoss`, `ForEnemy/ForBoss` factory 불변식으로 기존 계약을 일관되게 확장하고 모든 구독자 이관 검사 |
| 씬·프리팹 비첨부 | NavMesh·Collider·레이어 가정 오류 | 구현 전 `Boss.prefab`/아레나에서 레이어와 네트워크 컴포넌트 체크리스트 수행 |
