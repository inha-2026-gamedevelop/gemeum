# 보스 1마리 설계

> 상태: 설계 확정본, **구현 착수함** (2026-09-05, 15장 구현 순서 1~2단계)  
> 출처: 2026-09-01 외부 소스 감사와 함께 받은 설계 산출물. 저장소 문서로 편입했다  
> 관련: [프로젝트 구조 설계서](../04-architecture.md) · [네트워크 권위](NETWORK_AUTHORITY.md) · [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)

> **2026-09-05 갱신 — 15장 구현 순서 1~2단계 완료(부분 검증).**
> `Boss_Proxy.prefab`(`Assets/02.Prefabs/Enemy/`) — 11장 히트박스 표대로 본체 캡슐(반경 0.45m·높이 1.85m)과
> 꼬리 콜라이더(반경 1.20m, 본체 뒤 2.5m, 1페이즈 기본 비활성)를 만들고 `NetworkIdentity`·
> `NetworkTransformReliable`(서버 권위)을 붙였다. `BossSpawner.cs`(`Assets/01.Scripts/03.AI/Boss/`)가
> `RunPhaseChangedEvent(Current == Boss)`를 구독해 `Boss` 태그 마커 위치에 1회만 스폰한다.
> `Dev_AI.unity`에 마커·컴포넌트·spawnPrefabs 등록까지 마침. Play(Host) 1인 세션에서 이벤트 발행 →
> 정확한 위치 스폰, 중복 스폰 방지까지 확인. **L3(3인) 미검증**, `BossStatus`/`BossController`/패턴은
> 아직 없음 — 3단계부터 이어서 진행한다.
>
> 실물 모델(`Kitsune Yuna`, 162,246 tris — 12.1절 수치와 정확히 일치 확인함)은 로컬에 있으나
> 애니메이션 클립이 없어 지금은 붙이지 않았다. 설계 문서 지침대로 프록시로 L3까지 통과한 뒤 교체한다.

> 상태: 구현 제안. 코드·씬·프리팹은 수정하지 않았다.
>
> 핵심 결정: **보스 1마리, 2페이즈, MVP 8개 데이터 항목(실행 템플릿 4종)**으로 고정한다. 기존 초안의 약 18개 개별 패턴은 3인·11주 일정에서 애니메이션/VFX/네트워크 검증 비용이 과도하므로 출시 후보기능으로 내린다.

---

> ## ⚠️ 2026-09-03 개정 — 이 문서를 읽기 전에
>
> 아래 본문은 2026-09-01 기준이고 **정체·페이즈 구성·수치가 바뀌었다.** 충돌하는 부분은 이 상자와 10~14장이 우선한다.
> 결정의 근거와 기각 사유는 [직업 정체성과 보스 설계](../class-identity-and-boss-2026-09-03.md)에 있다.
>
> | 항목 | 이전 | **현재** |
> |---|---|---|
> | 정체 | 외세 침입군 병사 원혼 | **구미호** — 원혼들을 삼키고 저승길을 차지했다 |
> | 2페이즈 | (미정) | **같은 모델의 꼬리 펼침 + 아레나 교체.** 별도 야수형 모델을 만들지 않는다 |
> | HP | 25,000 | **55,000** (1페이즈 22,000 / 2페이즈 33,000 / 꼬리 3덩이 각 1,350) |
> | 패턴 Windup | 자유 | **잡귀 값 재사용으로 고정** — 10장 |
> | 부위 파괴 | 없음 | **꼬리 1단** — 12장 |
> | 최종 구간 | 없음 | **원혼 셋 독립 판정 + 삼켜짐** — 13장 |
> | 보스맵 | `Main.unity` 안 | **별도 씬 `Boss_Arena.unity`** |

## 1. 목표와 범위

이번에 만드는 것은 서버 권위 보스 1마리, HP 기반 2페이즈, 서버 패턴 선택·명중 판정, 모든 클라이언트에 동일한 예고 표현, 페이즈별 패링 가능 패턴 1개 이상, 보스 사망에 따른 `Result` 진입이다.

MVP 패턴은 아래 8개다. **보스 정체는 2026-09-03에 구미호로 개정됐다**(위 상자 참조) — 아래 표의 "장병기" 같은 병사 기반 작업명은 10장의 구미호 기준 이름으로 대체한다. 패턴 초안의 거대 괴수안은 채택하지 않는다 ([보스 패턴 계약 21.4](BOSS_PATTERN_CONTRACT.md)).

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
| `EnterHealthRatio` | `float` | 1.0, **0.5** |
| `PatternIds` | `int[]` | 페이즈당 4개 |
| `DecisionDelayMin` / `DecisionDelayMax` | `float` | 0.35 / 0.75초 |
| `MaxGapWithoutParryable` | `float` | 25초 |
| `TransitionLockDuration` | `float` | 2초 |

phase 검사는 index 1→2의 연속성, `EnterHealthRatio`의 1.0→0.5 단조 감소, 유효 PatternId만 참조하는지, 각 phase에 `ParryableRepeatIndex >= 0`인 항목이 최소 1개인지 확인한다. `BossPatternSO` 루트의 `_maxHealth`는 **55,000으로 확정한다**(2026-09-04). 3,000은 3인 DPS 측정 전의 플레이스홀더였고 폐기한다. 같은 에셋에 `HitColliderBufferSize` 같은 기술 한계는 넣지 않고 코드의 안전 상한으로 둔다. 반대로 플레이 체감에 영향을 주는 시간·거리·피해는 모두 SO에 둔다.

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
3. **두 번째 페이즈(10~14시간):** **HP 50% 전환**(`EnterHealthRatio = 0.5`), `TargetedDash`/`MarkedCircle`/`ConeBurst`, 페이즈 2 패링 1개, 최근 지목자 제외.
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

**엔지니어링 부분합: 대, 56~78시간.** 프로그래머 1명이 뼈대와 판정을 맡고 M2/M3 검증 인력이 일부 병렬 참여한다는 전제다. 현금 구매비는 이 부분합에서 **0원으로 잡되 구매를 끝냈다는 뜻은 아니다.** `PlayerAttack` 선행 구현, 보스 모델, 전용 모션, 최종 VFX·사운드, 보스 아레나/프리팹 배치와 그 구매비는 범위 밖·미산정이며 소싱 승인 뒤 별도 기록한다. **2026-09-03 갱신 — 모델(Kitsune Yuna)과 패턴 VFX(Spirit Blossom)는 구매·반입을 마쳤고, 2페이즈도 같은 모델을 쓰므로 신규 모델 제작이 없다. 대신 13장의 3인 기믹과 삼켜짐이 새 비용으로 들어온다.** 애니메이션 작업은 `05-animation-pipeline-design.md`의 부분합과 중복 합산하지 않고 전체 일정에서 하나의 공용 작업으로 잡는다.

| 위험 | 영향 | 완화 |
|---|---|---|
| ~~보스 아트 자산 미확보~~ | **2026-09-03 해소** — 모델·패턴 VFX 구매 완료 | 남은 위험은 **모델이 `99.ThirdParty`라 커밋되지 않는 것**이다. `Boss_Proxy.prefab`을 커밋해 담당자가 모델 없이 판정을 검증한다 (15장) |
| **실물 모델의 실제 키 미실측** | 1.85 m가 아니면 11장 히트박스 전체가 무효 | `Boss_Proxy` 첫 검증 항목으로 실측 |
| 3인 DPS를 몰라 HP·그로기 시간이 부정확 | 너무 길거나 순삭 | 모든 수치를 SO로 두고 M2 3회 중앙값으로 조정 |
| 패링 성공이 보스 행동 수명을 못 끊음 | 유령 피해, 핵심 메커닉 신뢰 붕괴 | 공격 instance ID, 명시적 Cancel, 페이즈/사망 시 일괄 정리 테스트 |
| 텔레그래프 RPC 유실/지연 | 피할 수 없는 공격처럼 보임 | 신뢰 채널 사용, `impactServerTime` 기준 보정, 최소 예고시간 M3 검증 |
| 기존 `EnemyDiedEvent`의 보스 kind가 없음 | `default(EnemyKind)` 오해, 구독자 잘못된 드랍 분기 | `EnemyKind?`, `IsBoss`, `ForEnemy/ForBoss` factory 불변식으로 기존 계약을 일관되게 확장하고 모든 구독자 이관 검사 |
| 씬·프리팹 비첨부 | NavMesh·Collider·레이어 가정 오류 | 구현 전 `Boss.prefab`/아레나에서 레이어와 네트워크 컴포넌트 체크리스트 수행 |

---

# 2026-09-03 개정 — 10~14장

아래는 [직업 정체성과 보스 설계](../class-identity-and-boss-2026-09-03.md)에서 확정된 내용을 구현 관점으로 옮긴 것이다. 1~9장과 충돌하면 **이쪽이 우선한다.**

## 10. 패턴 — Windup은 잡귀 값을 재사용한다

**이것이 패턴 데이터의 최상위 제약이다.** `BossPatternSO.PatternEntry`의 windup은 자유값이 아니라 **아래 넷 중 하나만** 쓴다.

| 템플릿 | Windup | 패링 | 정밀 사격 | 출처 | 1페이즈 이름 | 2페이즈 이름 |
|---|---:|:---:|:---:|---|---|---|
| `MeleeArc` | **1.2초** | ○ | △ | 허깨비 | 소맷자락 후리기 | 꼬리 후려치기 |
| `TargetedDash` | **1.5초** | ○ | **○** | 창귀 | 홀림 유인 후 반격 | 도약 돌진 |
| `MarkedCircle` | **2.0초** | ✕ | — | — | 여우불 표식 | 여우불 장판 |
| `ConeBurst` | **0.8초** | ✕ | ✕ | 액귀 | 없음 | **꼬리 부채** |

**왜 고정하는가.** 30분간 잡귀로 익힌 리듬이 보스전에 그대로 전이된다. 플레이어는 처음 보는 패턴인데도 **windup 길이만으로 "이건 못 막는다"를 안다.** 온보딩([게임 디자인 12.1](../02-game-design.md))이 요구한 학습이 별도 비용 없이 회수된다.

**즉사 패턴을 만들지 않는다.** 40분 한 판이라 두 번째 기회가 없다. `MarkedCircle` 피해는 **120**(10레벨 최대 HP 180의 약 2/3) — 안 죽지만 다음 한 대에 죽는다.

`ConeBurst`(0.8초)는 **차사도 신궁도 대응할 수 없는 유일한 패턴**이다. 이것이 12장의 꼬리 파괴 보상을 값지게 만든다.

## 11. 히트박스 — Windup 고정, 사거리만 확대

모델이 같으므로 **본체 캡슐은 두 페이즈가 동일하다.** 난도 상승은 몸집이 아니라 **꼬리가 만드는 사거리**에서 온다.

> ✅ **2026-09-07 — 실측 완료, 표를 실측값(×1.14) 기준으로 갱신했다.**
> `Boss_Proxy`에 `SK_KitsuneYuna`를 임시로 붙여 재보니 키가 **2.109 m**(가정 1.85 m 대비 ×1.14)였다.
> 원칙대로 **사거리·반경·캡슐 크기만** 비율로 스케일하고, **속도·각도·Telegraph 시간은 그대로 뒀다.**
> (`TargetedDash` 도착 판정 반경도 히트박스 크기라 함께 스케일했다 — 이 표엔 원래 없던 항목이라 추가함)

| 항목 | 1페이즈 | 2페이즈 (꼬리 펼침) |
|---|---:|---:|
| 캡슐 반경 · 높이 | **0.51 · 2.11 m** | **동일** |
| 기본 이동 | 4.5 m/s | **5.5 m/s** |
| `TargetedDash` 속도 · 거리 · 명중 반경 | 10 m/s · **9.12 m** · **1.37 m** | **12 m/s · 15.96 m · 1.37 m** |
| `MeleeArc` 사거리 · 각도 | **3.19 m** · 100° | **5.13 m · 180°** |
| `MarkedCircle` 반경 | **3.99 m** | **6.27 m** |
| `ConeBurst` 사거리 · 각도 | **10.26 m** · 60°*(주1)* | **10.26 m · 60°** |
| **꼬리 콜라이더** | 없음 (렌더러 OFF) | **본체 뒤 2.85 m · 반경 1.37 m (고정)** |

*(주1) 1페이즈 `ConeBurst` 행은 원래 이 표에 없었다("없음") — [보스 패턴 계약](BOSS_PATTERN_CONTRACT.md)이 이 문서보다 늦게(2026-09-04) 패턴 5(여우 구슬 충격파)를 1페이즈에 추가하면서 이 표가 갱신을 놓쳤다. `Phase1State.cs`가 이미 2페이즈와 같은 사거리·각도로 구현돼 있어 그 값을 실측 기준으로 스케일해 채웠다 — 수치 자체의 재설계는 아니다.*

> 🔒 **꼬리 콜라이더는 꼬리 본을 따라가지 않는다.**
> 꼬리 본은 `spine_01` 아래 `tail_01~10`의 10단 체인인데 **Humanoid 리그 대응표 밖이라 애니메이션을 받지 않고 물리 스프링으로 흔들린다.** 스프링은 클라이언트마다 다르게 흔들리므로 콜라이더를 본에 붙이면 **세 화면에서 맞는 위치가 갈라진다.**
> **보이는 꼬리와 맞는 꼬리를 분리한다.**

## 12. 꼬리 부위 파괴 — 2페이즈 전용, 1단

에셋 실측 결과 꼬리는 **별도 `SkinnedMeshRenderer`(36,864 tris)지만 `subMeshCount = 1`**이라 **메시 분할이 안 된다.**

> 🔧 **2026-09-05 개정 — 판정은 3분할한다.** 메시를 못 나누는 것이지 콜라이더를 못 붙이는 것이 아니다. `tail_01~10` 사슬 위 **세 지점에 콜라이더**를 붙이면 메시는 하나이고 판정은 셋이 된다.
> 그리고 **부위 파괴가 이정표에서 진행 조건으로 승격됐다** — 자세한 것은 [보스 패턴 계약 8.2](BOSS_PATTERN_CONTRACT.md).

| 항목 | 값 |
|---|---:|
| 꼬리 | **3덩이** · 덩이당 HP **1,350** (총 4,050) |
| **본체 게이트** | **셋을 다 부수기 전에는 본체 HP가 25% 아래로 안 내려간다** |
| 미결 페널티 | 그로기 안에 표식된 꼬리를 못 부수면 **보스 공격력 ×1.2** (부술 때까지 · 누적 없음) |
| 파괴 보상 | **`ConeBurst` 영구 봉인** + 드랍 1 |
| 표시 | 꼬리 게이지 + 손상 단계별 색 변화 + **그로기 시 대상 꼬리에 표식** |

> **"본체 50% 반영"과 *"꼬리를 안 깨고 밀어도 이길 수 있어야 한다"*는 폐기됐다.**
> 25% 하드 게이트가 그 반대를 택했다 — **꼬리를 깨야만 끝낼 수 있다.** 다만 벌이 아니라 시계다: 꼬리를 무시하면 게이트에 걸린 채 14분이 지나 **시간 초과로 진다.** 계산 근거는 [8.2](BOSS_PATTERN_CONTRACT.md).

**봉인 대상이 `ConeBurst`인 이유** — 유일하게 대응 불가한 패턴이라 사라질 때 체감이 가장 크고, 꼬리로 쏘는 패턴이라 인과가 눈에 보인다.

### 12.1 꼬리는 아홉으로 보인다 — 보이는 것과 판정하는 것을 분리한다 (2026-09-04 확정)

**구미호는 꼬리가 아홉이라서 구미호다.** 하나면 "드러난 것"이 무엇을 드러낸 것인지 읽히지 않는다. 그러나 실측대로 모델의 꼬리는 하나이고, 진짜 아홉을 만들면 폴리곤이 3배가 되며 리깅을 새로 해야 한다 — **[안 2를 고른 이유가 바로 그 리깅 도박을 없애는 것이었다.**

> **그래서 아홉은 보여주고, 판정은 하나로 한다.**

| 층 | 개수 | 정체 | 폴리곤 |
|---|---:|---|---:|
| **실물 꼬리** | 1 | `SK_KITSUNEYUNA_TAIL` 그대로. **유일한 판정 대상** | 36,864 |
| **실루엣 꼬리** | 8 | 저폴리 + 여우불 청록 발광. **콜라이더 없음, 판정 없음** | 목표 **각 2,500 이하 / 합계 20,000 이하** |

**실루엣으로 두는 것이 오히려 낫다.** 선명한 아홉은 그냥 여우지만, 실물 하나 뒤로 여덟이 어른거리면 **"저게 뭐지"**가 된다. 그리고 [색 문법](../01-system-design.md)의 여우불 청록이 이미 원귀 계열의 색이라 새 규칙을 만들지 않는다.

**독립 애니메이션을 만들지 않는다.** 실루엣 여덟이 `tail_01~10` 체인을 공유해 같이 흔들려도, 실물 뒤의 잔영으로 읽히므로 어색하지 않다. 이것이 리깅 재작업을 피하는 핵심이다.

#### 사라지는 것은 파괴가 아니라 소진이다 — 콜라이더는 하나뿐이다

부위 파괴는 **세 번**이다(2026-09-05 개정). 덩이 하나가 깨질 때마다 **다발 하나가 사라진다.**

| 남은 덩이 | 보이는 꼬리 | 사건 |
|---:|---:|---|
| 3 | **9** | — |
| 2 | **6** | 뿌리 구간 파괴 · 실루엣 다발 A 소멸 |
| 1 | **3** | 중간 구간 파괴 · 실루엣 다발 B 소멸 |
| **0** | **0** | 끝 구간 파괴 — 실물 꼬리 소멸, `ConeBurst` 봉인 · 드랍 1 · **25% 게이트 해제** |

- **콜라이더는 실물 꼬리 사슬 위 세 개다**(뿌리 · 중간 · 끝). 실루엣에는 콜라이더도 `IDamageablePart`도 붙이지 않는다
- **덩이 하나가 그로기 한 번에 대응한다** — 8초 무저항 딜 2,047 ÷ 1,350 = 1.52배 여유
- 코드 증분은 **꼬리 HP 훅에서 `SetActive` 세 번**이다. 구간 경계는 `BossPatternSO`에 두고 상수로 박지 않는다
- **9 → 6 → 3 → 0의 4단계가 하나씩 줄어드는 것보다 잘 읽힌다.** 8인지 7인지 셀 필요가 없다. 3인 코옵에서 사람들은 UI를 안 보므로, 이것이 **숫자 없이 읽히는 유일한 진행도**다

#### 🚦 폴백 — 성능이 감당 못 하면 A로 되돌린다

**이 구조가 안전한 이유는 판정 계층이 A와 완전히 같기 때문이다.** 되돌리는 것이 재설계가 아니다.

| | A (폴백) | B (확정) |
|---|---|---|
| 꼬리 HP · 본체 반영 · 보상 | 덩이당 1,350 · 본체 반영 없음 · 동일 | **같음** |
| 콜라이더 | 실물 1 | **같음** |
| `IDamageablePart` | 실물 1 | **같음** |
| 판정 코드 | — | **변경 0** |
| 차이 | 꼬리 1개 | 실루엣 8 추가 |

> **폴백 절차: 프리팹에서 실루엣 8개를 끈다. 그것이 전부다.**
> 코드도, 수치도, 계약도 건드리지 않는다.

**판단 시점과 기준**

| | |
|---|---|
| **언제** | **W5 — 보스 트라이 모드가 붙은 직후.** 그 전에는 보스 프레임을 잴 수단이 없다 |
| **어떻게** | 3인 접속 · 2페이즈 진입 상태 · 60초 측정 · 최저 사양 PC 기준 |
| **무엇을** | 평균 프레임과 **1% low**. 평균만 보면 전투 중 끊김을 놓친다 |
| **판정** | 목표 프레임을 **1% low가 못 지키면 A로 전환.** 실루엣 저폴리화로 먼저 시도하고, 그래도 안 되면 끈다 |

> ⚠️ **목표 프레임 수치는 팀 성능 예산 문서를 따른다.** 이 문서가 임의로 정하지 않는다.
>
> 🚨 **실루엣 제작 전에 실물 보스 하나(162,246 tris)가 3인에서 도는지 먼저 잰다.** 지금 그 숫자조차 미검증이다 — **차사 리메시 목표가 30,000이었던 것과 비교하면 5배다.** 실물이 이미 빠듯하면 실루엣은 만들 필요가 없다.

**부수 효과 — 1페이즈에 꼬리를 끄면 36,864 tris(22.7%)가 절약된다.** 1페이즈는 완전한 사람 형상이라 꼬리도 실루엣도 없다. 연출 결정이 성능 이득을 겸한다.

### 선행 조건 — 판정 계약

부위 파괴는 판정이 **"콜라이더에서 위로 올라가며 처음 만나는 `IDamageable`"**을 찾아야 성립한다. `Geumeum.Common.DamageTargetResolver`가 그 창구이고, 근접·화살 두 경로는 2026-09-03에 전환됐다. 중복 제거 키도 `netId`가 아니라 **해석된 참조 자체**다 — 부위와 본체는 같은 `NetworkIdentity`이기 때문이다. 상세는 [플레이어 공격 설계 5.3.1](PLAYER_ATTACK_DESIGN.md).

**꼬리는 `IDamageablePart`를 함께 구현한다.** 부위임을 알리는 마커이고, 무녀 공명이 효과를 가르는 데 쓴다([게임 디자인 2.7](../02-game-design.md)) — 부위에는 판정 창이 없으므로 창 확대 대신 피해 증폭이 걸린다. `IParryWindowTarget`은 *패링하는 쪽*(플레이어)의 계약이라 이 판별에 쓸 수 없다.

**파생** — 부채꼴 하나에 몸통과 꼬리가 함께 들어오면 **둘 다 맞는다.** 부위 파괴 게임에서는 정상이다. 다만 차사 부채꼴 2.2 m와 꼬리 거리 2.5 m의 경계가 아슬아슬하므로 **`Boss_Proxy` 첫 L2 항목**으로 실측한다.

## 13. 최종 구간 — 원혼 셋과 삼켜짐

**꼬리를 파괴하면**(12장, 1단이다) 구미호가 삼킨 원혼들을 토해내며 **무적**이 된다.

> **2026-09-06 전면 개정 — 순차를 버린다.** 이전 규칙은 차사 → 신궁 → 무녀 순서였고 앞 단계가 성공해야 다음이 열렸다. **런 중 편성이 자유로 확정되면서**([보스 패턴 계약 6장](BOSS_PATTERN_CONTRACT.md)) 그 구조는 특정 직업이 없을 때 진행을 봉쇄한다. 규칙 본문은 [게임 디자인 9.7](../02-game-design.md)이 소유한다.

> **무적 진입과 동시에 원혼 셋이 정면 · 후방 · 중앙에 나타난다.**
>
> | 위치 | 행동 | 즉시 소멸 조건 |
> |---|---|---|
> | **정면** | 앞에 있는 사람을 향해 돌진한다 (`TargetedDash` windup 1.5초) | **패링** |
> | **후방** | 시전을 시작한다 (`MarkedCircle` windup 2.0초) | **정밀 사격으로 중단** |
> | **중앙** | 제자리에서 맥동한다 | **표식이 붙은 채 맥동 종료** |
>
> **원혼은 HP를 가진 개체다.** 위 조건이 아니어도 때려서 없앨 수 있다. 느릴 뿐이다.
> 셋이 모두 사라지면 무적이 풀리고 처치 구간이 열린다. **순서는 상관없다.**
>
> 원혼의 windup은 10장의 잡귀 값을 그대로 쓴다. **여기서도 새로 배울 것이 없다.**

**순서를 두지 않는 이유** — 순차를 골랐던 원래 근거는 "3초 동시 판정이 3인 RTT에서 가장 위험하다"였다. **독립 판정 셋은 동시도 순차도 아니라 각자 자기 대상만 보므로 그 위험이 처음부터 없다.** 서버 구현은 더 단순해지고, 직업 부재가 봉쇄로 이어지지 않는다.

| 항목 | 규칙 |
|---|---|
| 실패 시 | 즉사 아님. **백귀야행을 한 번 시전**하고 무적을 유지한 채 다시 시도 |
| 백귀야행 갈래 | `3 - 없앤 원혼 수`. **이미 없앤 원혼은 돌아오지 않는다** |
| 시도 횟수 | 제한 없음. 남은 보스 시계만이 제한 |
| 판정 | **서버 시각 기준 창(임시 8초), 순서 무관.** 정밀 사격과 같은 규칙 |
| 표시 | 원혼 셋의 상태와 남은 창. 없앤 원혼은 표시에서 지워진다 |

### 삼켜짐 — 이 구간에서는 죽지 않는다

기믹은 셋이 서 있어야 성립한다. 한 명이 관전 중이면 재시도가 몇 번이든 의미가 없으므로 **사망 규칙 자체를 바꾼다.**

```
HP 0  →  사망이 아니라 "삼켜짐"
         붙잡혀 이동 불가, 10초 타이머
         ├─ 팀원이 2초 상호작용으로 끌어냄 → HP 30% 복귀. 사망 아님, 페널티 없음
         └─ 10초 초과 → 그때 진짜 사망 (관전 30초 + 노잣돈 20%)
```

**구간 경계를 명시한다.** 삼켜짐은 이 구간 전용 규칙이므로 언제 켜지고 꺼지는지가 코드에 있어야 한다.

| 시점 | 사망 규칙 |
|---|---|
| 꼬리 파괴 전 (1·2페이즈 전투) | **일반 사망** (관전 30초 + 노잣돈 20%) |
| **꼬리 파괴 → 기믹 성공까지 (무적 구간)** | **삼켜짐** |
| 기믹 성공 후 (처치 구간) | **일반 사망으로 복귀** |

기믹이 성공해 무적이 풀리는 순간 삼켜짐도 함께 끝난다. 그때 **아직 삼켜져 있던 사람은 즉시 끌려나온 것으로 처리하고 HP 30%로 복귀시킨다** — 구간이 끝났는데 삼켜진 채 남아 있으면 그 사람만 영영 못 움직인다.

**서사가 이 규칙을 요구한다** — 구미호는 삼키는 존재이고, 이 구간은 삼킨 것을 토해내는 중이다. 죽이려는 것이 아니라 삼키려는 것이다. 최악의 정지 시간이 30초에서 **12초**로 줄고, 무적 구간이라 **"구하러 갈까, 한 번 더 시도할까"**가 결정이 된다.

**L3 최우선 항목이며 단일 플레이에서는 검증이 불가능하다.**

> 📄 **패턴 세트·선택기·시간 구조·판정 계약은 [보스 패턴 계약](BOSS_PATTERN_CONTRACT.md)으로 분리했다 (2026-09-04).**
> 패턴 10개, 예고 템플릿 4종 축, 어그로 규칙, 그로기 3문, 14분 시간 구조, 엇박, 회전 잠금, 첫 조우, 트라이 모드 리셋이 거기 있다.

## 14. 아레나와 VFX

### 14.1 페이즈 전환은 씬 전환이 아니다

같은 씬 안에서 아레나 루트를 토글한다. `NetworkBehaviour`가 하나도 죽지 않으므로 상태 이관이 필요 없다.

```
Boss_Arena.unity
├ Antechamber    전실. 게이트 통과 직후 도착 지점 (2026-09-04 신설)
├ Arena_Phase1   산속 사당 · 등불 · 담장      (시작 시 활성)
├ Arena_Phase2   저승 물길 · 여우불 · 얼굴들   (시작 시 비활성)
└ Shared         바닥 콜라이더 · NavMesh · 스폰 마커 · 게이트
```

> **2026-09-06 갱신.** 씬은 `Assets/00.Scenes/Release/Boss_Arena.unity`로 신설됐다(커밋 `9d6a342`, 빌드 목록 5번). 경로가 `00.Scenes/` 직하가 아니라 `Release/` 아래다.

#### 확정 치수 (2026-09-04 · 2026-09-06 개정)

| 항목 | 값 |
|---|---|
| **걸을 수 있는 바닥 · NavMesh** | **반경 22 m** (2026-09-06 개정) |
| 마당 (석판 시각 경계) | **반경 18 m** |
| 담장 | **반경 22 m** · 높이 **2.2 m** · **`Ground` 콜라이더** · **북쪽 사당 구간 개구** (2026-09-06 개정) |
| 사당 본전 | 북쪽 **반경 20 m** · 3단 석계단 · **담장 밖으로 돌출** |
| 돌무더기 | **4기** · 비대칭 · 중심 거리 **9 / 11 / 13 / 15 m** |
| 등불 | **12개** · 담장 안쪽 원주 균등 · **링 장식이고 마당 조명이 아니다** |
| 보스 스폰 | 마당 중심 (0, 0) |
| 플레이어 스폰 | 남쪽 반경 15 m · 3점 · 3 m 간격 |

> **2026-09-06 개정 넷.** 아래 네 항목은 실제 값으로 검산해 고친 것이다. 마당 반경 18 m 자체는 검산을 통과해 그대로다.

**반경 18 m의 근거는 표식 순환 처형이다.** 2페이즈 `MarkedCircle` 반경 5.5 m × 3인 = 약 285 ㎡. 이것이 마당의 1/3을 넘으면 회피가 아니라 자리 뺏기가 되므로 하한이 약 815 ㎡(반경 16 m)이고, 360° 휩쓸기가 *"밖으로 빠지면 산다"*로 읽히려면 바깥 여유 링이 더 필요하다.

**걸을 수 있는 바닥은 마당이 아니라 담장까지다 (2026-09-06 개정).** 이전 판은 바닥을 반경 18 m로 적었는데, [패턴 계약 11.3](BOSS_PATTERN_CONTRACT.md)의 보스 이동 한계가 반경 22 m다. 18~22 m 링에 바닥이 없으면 **보스가 허공을 걷고 NavMesh도 거기서 끊긴다.** 바닥과 NavMesh를 22 m까지 깔고, 반경 18 m는 석판 머티리얼 경계로만 남긴다. 보스가 못 들어오는 링을 두면 그 링이 곧 안전지대가 되므로, 11.3이 완충을 안전지대로 만들지 않겠다고 한 것과도 이때만 맞는다.

**담장에는 콜라이더를 붙인다. "눈에만 있는 경계"를 폐기한다 (2026-09-06 개정).** 이전 판의 근거는 *"숄더뷰에서 담장에 붙으면 화면이 벽으로 찬다"*였는데, **완충 4 m는 카메라 기본 거리 4.5 m보다 작아 그 목적을 애초에 달성하지 못한다.** 보스 몸통 락온이면 카메라가 9.0 m로 물러나므로 두 배 넘게 모자란다.

해결 수단은 이미 코드에 있다 — `CameraRig`가 SphereCast(probe 0.25 · 최소 거리 0.8)로 카메라를 당기고, `CameraConfigSO._collisionMask`가 `Default`와 `Ground`를 포함한다. **담장 콜라이더를 `Ground` 레이어로 두면 카메라가 담장을 뚫지 않는다.** 담장이 실제 이동 한계가 되어 11.3의 반경 22 m와도 일치하고, 완충 4 m는 카메라 여유가 아니라 이동 여유로 재정의된다.

**바닥 콜라이더도 `Ground`로 만든다.** 현재 프로젝트는 `Ground` 레이어가 정의만 되어 있고 실제 바닥이 `Default`에 있어서 부활 지면 탐침 마스크에 `Default`를 함께 넣어 둔 상태다. 새 씬에서 바로잡는 것이 가장 싸다.

**담장은 북쪽 사당 구간에서 끊는다 (2026-09-06 개정).** 사당 본전은 반경 20 m인데 담장이 22 m라 **폭 2 m 안에 3단 석계단과 본전이 들어가지 않는다.** 계단만 3단에 0.9 m를 먹는다.

사당을 밖으로 미는 안은 안개가 죽인다 — [BOSS_STAGING 4.2](BOSS_STAGING_DESIGN.md)의 Exp² density 0.035에서 20 m는 안개 39%라 *"다가가야 알아본다"*가 성립하지만, 26 m로 밀면 56%라 그냥 안 보인다. 안으로 당기는 안은 마당을 먹는다. **따라서 사당은 20 m에 그대로 두고 담장을 그 구간에서 끊는다.** 남쪽에 참배로 게이트, 북쪽에 사당 개구로 담장이 두 곳에서 열리는 형태가 된다.

**담장은 `Arena_Phase1` 전용이다.** 2페이즈에 사라지면서 지평선 제거가 따로 비용 없이 이루어진다.

**사당 본전은 양쪽 루트에 각각 한 벌씩 둔다** (같은 좌표 · 다른 머티리얼). `Shared`에 두면 2페이즈 머티리얼로 바꿀 수 없고, `Phase1`에만 두면 2페이즈에 수직 기준점이 하나도 남지 않아 방향 감각이 죽는다. **2페이즈의 사당은 등불이 꺼진 젖은 실루엣이고, 발밑 반사에 한 채 더 있다** ([BOSS_STAGING 4.2](BOSS_STAGING_DESIGN.md)).

#### 돌무더기 4기 — 엄폐물이 아니라 표식이다

> 🔒 **이름을 "엄폐물"로 적지 않는다. 이 물건은 아무것도 막지 않는다.**

**엄폐물로 두면 막을 것이 없다.** 3번(여우불 입자)은 고정 발사라 막히면 패턴이 무효가 되고, 6번(추적)과 2P-3(탄막)은 막을 수 없으며, **2P-7(백귀야행)은 막히면 안 된다** — 설계 의도가 제자리 회피 타이밍이다.

**실제 용도는 좌표계다.** 지름 36 m짜리 빈 원은 어디나 똑같이 생겨서 **3인 코옵에서 "어디로 가"라고 말할 수 없다.** 비대칭 랜드마크 넷이 있으면 *"북동쪽 돌 옆으로"*가 성립한다.

| 기 | 좌표 (x, z) | 중심 거리 | 방위 | 스폰 최근접 |
|:--:|---|---:|---|---:|
| 가 | (−7.5, +5.0) | 9.0 m | 북서 | — |
| 나 | (+10.7, +2.5) | 11.0 m | 동 | — |
| 다 | (+8.0, −10.25) | 13.0 m | 남동 | 6.7 m |
| 라 | (−14.0, −5.4) | 15.0 m | 남서 | 14.4 m |

> **2026-09-06 개정.** 이전 좌표는 10.8 / 13.6 / 13.0 / 14.9였다. 두 가지가 걸렸다.
> **"나"와 "다"의 중심 거리 차가 0.6 m로 눈에 구분되지 않았다** — 규칙 1이 막으려던 회전 대칭을 실제로는 못 막는다. 2 m씩 벌려 9 / 11 / 13 / 15로 바꿨다.
> **"다"가 우측 플레이어 스폰과 5.45 m였다** — 규칙 3의 하한 5 m에서 여유가 0.45 m뿐이었다. 개정 좌표에서 6.7 m가 된다.

**배치 규칙 넷**

1. **네 기 모두 중심 거리가 다르다** — 회전 대칭이 생기면 좌표계 구실을 못 한다
2. **중심 8 m 안은 비운다** — 보스 패턴이 도는 구역
3. **플레이어 스폰에서 5 m 이상** — 시작하자마자 끼면 안 된다
4. **높이 1.0–1.4 m · 콜라이더 없음 · NavMesh Carve 하지 않는다** — 보고 부르는 물건이지 통과를 막는 물건이 아니다. 통행을 막으면 4번 마수 돌진 경로가 걸린다

#### 등불 12개는 마당을 밝히지 않는다 (2026-09-06 신설)

**"등불 12개가 마당을 밝힌다"는 전제는 산수로 틀렸다.** 등불을 담장 안쪽 원주(반경 약 21 m)에 두면 중심까지 21 m인데 [BOSS_STAGING 4장](BOSS_STAGING_DESIGN.md)의 등불 Point range는 8 m다. **마당 한복판에 빛이 0으로 떨어진다.** 보스 스폰이 (0, 0)이고 예고 데칼이 그려지는 바닥도 거기다.

인접 등불 간격은 2π×21÷12 = **11.0 m**라 range 8 m끼리는 서로 겹친다. 즉 부족한 것이 아니라 **모양이 도넛이다** — 링만 밝고 가운데가 빈다.

**등불 12개는 링 장식으로 남기고, 마당 중앙 조도는 Directional(달)과 사당 방향 광원이 잡는다.** 개수 12는 그대로 간다 — 먹 소등 연출이 개수에 걸려 있고, 그 검증은 회색 박스로도 되지만 개수로는 안 된다. 정확한 광량은 그레이박스를 세운 뒤 화면을 보고 정한다.

**2페이즈에는 같은 좌표에 무너진 네 기를 둔다.** `Arena_Phase2` 루트에 별도로 배치하고 `SetActive`로 교체한다 — **네트워크 상태 0 · 신규 코드 0 · 밸런스 수치 0.**

> 🔒 **보스가 돌진해 부수지 않는다.** *"파괴 지형"*은 3장의 스코프 밖이다.
> 먹이 걷힌 뒤 이미 무너져 있는 편이 연출로도 낫다 — **세계가 바뀐 것이 아니라 여기가 원래 이랬다는 것이 드러난다.**

#### 전실 (Antechamber)

**세 가지를 동시에 한다** — 하늘을 처음 보는 곳, 3인 로드 확인 구간, 그리고 마당을 밝게 만들 어둠.

| 항목 | 값 |
|---|---|
| 형태 | 게이트에서 마당으로 이어지는 **참배로.** 폭 6 m · 길이 15 m |
| 지붕 | **없다.** 하늘이 열려 있어야 [BOSS_STAGING 4.3](BOSS_STAGING_DESIGN.md)의 첫 번째 창이 성립한다 |
| 조도 | **마당보다 어둡다.** 등불 2개 |
| 바닥 | `Shared`의 젖은 석판 연장 — **반사가 전실에서 시작된다** |
| 소품 | **빈 갓걸이 1** |

> 🔒 **마당 게이트는 서버가 전원 도착을 확인해야 열린다.**
> 조건은 **"3인"이 아니라 "현재 접속 인원 전원"이다.** 3을 박으면 [보스 트라이 모드](BOSS_TRIAL_MODE.md)에서 혼자 들어올 때 영영 열리지 않는다.
> 서버 `SyncVar bool` 하나 + 열림 연출은 클라이언트 로컬. **현재 설계에 없던 "한 명이 느리면 혼자 보스 앞에 떨어진다"의 해답이다.**

**빈 갓걸이를 두는 이유는 폭로가 아니라 부재다.** 갓을 걸어두면 세 명 중 한 명이 알아채고 말해서 나머지 둘의 것까지 날아간다. **비어 있는 걸이는 첫 판에 아무 의미도 없고, 갓이 떨어지는 것을 본 뒤 두 번째 판에 의미가 생긴다.**

#### 에셋 조달 규칙

**보스 아레나 에셋은 전부 `Assets/98.FreeAssets/`에 넣는다.** `99.ThirdParty/`는 gitignore 대상이라 **거기 넣으면 팀원 둘이 빈 아레나를 연다.**

| 출처 | 주의 |
|---|---|
| 스케치팹 (사당 등 건축) | **라이선스 원문 확인 필수.** CC-BY-NC는 상업 이용 불가 → [에셋·데이터 정책 3장](../ai/12_ASSET_AND_DATA_POLICY.md) 표에 출처·라이선스·취득일 기재 |
| Meshy AI (돌무더기 등 프롭) | 폴리곤은 문제가 아니다. **MeshCollider 자동 생성을 끄고 박스 콜라이더를 수동으로 짠다** — 계단·처마에 캐릭터가 끼면 40분짜리 런이 끝난다. NavMesh도 자동 콜라이더 위에 베이크하면 처마로 올라간다 |

**전실은 정식 런과 [보스 트라이 모드](BOSS_TRIAL_MODE.md)의 공통 도착 지점이다.** 정식 런에서는 3인 로드 확인 구간을 겸한다 — 현재 설계에는 씬 전환 직후 세 사람이 모두 도착했는지 확인하는 구간이 없어 **한 명이 느리면 혼자 보스 앞에 떨어진다.**

**바닥과 NavMesh는 반드시 `Shared`에 둔다.** 페이즈 전환에 걸을 수 있는 영역이 바뀌면 런타임 재베이크가 필요해진다. 바뀌는 것은 위쪽 장식·조명·머티리얼뿐이다.

**보스맵은 전부 리얼타임 라이트로 간다.** 캐릭터 넷에 좁은 공간이라 감당되고, **베이킹이 없어서 전환이 가능하다.**

**전환 15초는 컷신 8초(입력 잠금) + 숨 고르기 7초(입력 해제)로 나눈다 (2026-09-04).** 15초를 통째로 컷신으로 덮으면 [7.3](../class-identity-and-boss-2026-09-03.md)의 "전환 중 무녀가 힐 3회를 돌린다"가 죽고, 회복을 주지 않는 제약만 남는다.

**아래 다섯은 컷신이 아니라 숨 고르기 앞 6초(8.0–14.0초)에 보간한다** — 컷신 중에는 카메라가 보스만 보고 있어 배경 보간이 보이지 않는다. 분위기의 대부분은 지오메트리가 아니라 이것들이 만든다.

| 요소 | 1페이즈 | 2페이즈 |
|---|---|---|
| Volume 프로파일 | 따뜻한 등불빛 | 창백한 청록 |
| 스카이박스 | 밤하늘 | 뒤집힌 하늘 · 별 없음 |
| 안개 | 옅음 | 짙고 낮게 |
| 라이트 색 | 주황 (등불) | 푸름 (여우불) |
| 파티클 | 없음 | 떠오르는 얼굴들 · 재 |

페이즈는 서버 SyncVar로 확정하고 연출은 `RpcPhaseChanged` 하나로 전파한다. **판정과 무관한 순수 표현**이고 15초 무적 구간이 오차를 흡수한다.

**구체 타임라인 · 색값 · 먹 전환은 [보스 연출 설계](BOSS_STAGING_DESIGN.md)로 분리했다.** 요지 셋만 여기 남긴다.

- **꼬리는 암전 *앞*(3.5–5.5초)에서 펼친다.** 화면이 덮인 동안 펼치면 세 사람 다 보지 못한다
- **전환은 흰색이 아니라 먹으로 씻는다.** 사당→지옥에 흰 플래시는 정화의 색이라 정반대다. 먹은 **보스 발밑에서 번진다** — 장소의 형상을 바꾸는 주체가 보스여야 한다
- **컷신은 전원 입력 시에만 스킵된다.** 트라이 모드에서 같은 8초를 수십 번 보게 되므로 스킵은 선택이 아니라 필수다

> ⚠️ **선행 조건 — 갱신 (2026-09-04 오후 재실측).**
> `Assets/Settings/PC_Renderer.asset`에 **`DecalRendererFeature`가 추가되었다**(`technique 0` · `maxDrawDistance 1000`). 14.2의 예고 데칼 4종을 막고 있던 차단이 풀렸다. **다만 워킹트리에만 있고 아직 커밋되지 않았다.**
> 남은 것은 먹 전환용 **Full Screen Pass 추가**와 이 변경의 **단독 커밋**이다. 렌더 파이프라인 루트 변경이므로 모든 씬에 영향이 간다 → [BOSS_STAGING 5장](BOSS_STAGING_DESIGN.md).

### 14.2 VFX 배정 — Spirit Blossom (반입 완료)

`Assets/99.ThirdParty/VFX/Piloto Studio/.../ARPG Spirit Bloom Spells/` — Shuriken 파티클 + Shader Graph. **VFX Graph가 아니라 URP에서 바로 돌고 추가 패키지가 필요 없다.** 색상 파라미터가 Shader Graph에 열려 있어 리컬러가 쉽다.

> 🔒 **예고(windup)는 자체 바닥 데칼로 통일한다. 에셋 VFX는 임팩트와 지속 효과에만 쓴다.**
> 21개 프리팹은 각자 색·타이밍·모양이 다르다. 예고까지 에셋에 맡기면 **패턴마다 예고가 달라 보여 10장의 학습 전이가 무너진다.**

| 템플릿 | 예고 (자체 데칼) | 임팩트 (에셋) |
|---|---|---|
| `MeleeArc` 1.2초 | 180° 부채꼴 데칼 | `SB_Shockwave_Multiple` |
| `TargetedDash` 1.5초 | 대상까지 직선 + 폭 | `SB_Flow`(궤적) → `SB_Shockwave_Ground`(착지) |
| `MarkedCircle` 2.0초 | 바닥 원이 차오름 | **`SB_Rune_Fall`(2초 누적) → `SB_Shockwave_Nova`(폭발)** |
| `ConeBurst` 0.8초 | 60° 쐐기 점멸 | `SB_Shockwave_Cone` |

`MarkedCircle`만 에셋을 두 단계로 이어 쓴다 — 2초를 채우는 누적 표현이 에셋 안에 있다. 반대로 `ConeBurst` 0.8초는 두 단계를 못 읽으므로 **예고는 데칼, 이펙트는 임팩트에만**이다.

**패턴 밖에서 더 많이 쓰인다.**

| 용도 | 프리팹 |
|---|---|
| **페이즈 전환 · 꼬리 펼침** | **`SB_Spirit_Bloom`** — 꽃이 피는 것 = 꼬리가 펼쳐지는 것 |
| **최종 구간 보스 무적** | **`SB_Dome`** — 순차 기믹 중 씌워지고 성공 시 깨진다 |
| **원혼 토해내기 (지속)** | `SB_Energy_Release_Loop` |
| **삼켜짐 상태 (13장)** | **`SB_Vortex`** — 발밑 소용돌이 + 10초 타이머 |
| **꼬리 파괴 순간** | `SB_Shockwave_Flash` |
| **그로기 진입** | `SB_Buff_Up` (색 변경) |
| **무녀 표식** | `SB_Curse` 축소 + 청록 리컬러 |

### 14.3 색 문법 — 진영을 색으로 가른다

| 진영 | 색 | 대상 |
|---|---|---|
| **아군 · 신성** | 한지 백색 + 주묵 붉은색, 부적 문양 | 결계 · 무녀 치유 |
| **적 · 원귀** | **여우불 청록** | 보스 패턴 · 잡귀 밤 발광 · 액귀 저주 |

원귀 계열을 청록으로 통일하면 **어두워졌을 때 "저건 적"이 한눈에 읽힌다**([게임 디자인 5.3](../02-game-design.md)의 밤 발광과 연결된다). 3인 코옵의 순간 판단이 색으로 처리된다.

## 15. 갱신된 구현 순서

```
1. Boss_Proxy 프리팹 (캡슐 2개 + 마커)      ← 모델을 기다리지 않는다. 11장 표가 스펙
2. BossSpawner (마커만 두고 런타임 스폰)      ← 씬에 보스를 배치하지 않는다
3. BossStatus · BossController · 페이즈 전환
4. 패턴 4종 (10장 windup 고정)
5. 꼬리 부위 파괴 (12장)                    ← 판정 계약이 선행, 이미 완료
6. 아레나 교체 연출 (14.1 · BOSS_STAGING_DESIGN)
7. 3인 순차 기믹 + 삼켜짐 (13장)            ← L3 최우선
8. VFX 배정 (14.2) · 실물 모델 교체
9. 전실 + 보스 트라이 모드 (BOSS_TRIAL_MODE)  ← 전실은 1번과 함께 해도 무방
```

**`Boss_Proxy`로 L3를 통과한 뒤 모델을 교체한다.** 반대로 하면 패턴 문제인지 모델 문제인지 구분되지 않는다.

구매 에셋은 `99.ThirdParty`라 **gitignore 대상이고 커밋되지 않는다.** 보스 담당자가 pull해도 모델이 없으므로 `Boss_Proxy.prefab`을 커밋해 그것으로 작업한다.
