# 서버 API 레퍼런스 — 그믐

서버 partial(`.server.cs`)은 `Assets/01.Scripts/98.Servers/`에 모으고, 판정·AI 클래스와 공개 요청·조회 창구는 기존 도메인 위치를 유지한다. 실제 범위와 위치는 [서버·클라이언트 협업](SERVER_CLIENT_WORKFLOW.md#5-현재-적용-범위)을 따른다.

담당: 김민성(@AidenKim923) · 대상: 서버 경로 코드를 작성하는 전 파트(판정·AI 포함)와 계약 검토자

서버 내부 호출과 읽기용 계약을 함께 기록한 목록이다. **클라이언트·UI 코드가 여기 있는 모든 메서드를 호출할 수 있다는 뜻은 아니다.**
클라이언트·UI 코드를 작성할 때는 [클라이언트 API 안내](CLIENT_API_REFERENCE.md)부터 읽는다. 구현 책임은 [서버·클라이언트 협업](SERVER_CLIENT_WORKFLOW.md)을 따른다.

`TakeDamage`·`TryRestoreHealth`·결과 확정·서버 사실 발행처럼 게임 상태를 바꾸는 호출은 서버 구현용이다. 클라이언트가 이 메서드를 직접 호출해도 서버 요청으로 전송되는 것은 아니다. AI의 공격 결정과 스폰도 서버가 자체 실행하며 클라이언트 요청으로 옮기지 않는다.

이 문서의 시그니처는 전부 현재 코드에서 뽑았다. **문서와 코드가 다르면 코드가 옳고, 그 차이를 알려 달라.**

---

## 0. 세 가지 원칙

**1) 게임 결과를 바꾸는 값은 서버에서만 만든다.**
`[Server]` 표시가 있는 것은 서버에서만 부른다. 클라이언트에서 부르면 사람마다 결과가 갈린다.

**2) 로케이터는 절대 null을 돌려주지 않는다.**
`BarrierStateLocator.Current` 같은 것은 등록된 구현체가 없으면 **아무 일도 하지 않는 대역**을 돌려준다. `Current != null` 검사를 넣지 않아도 된다. 대신 "등록이 안 됐는데 조용히 넘어가는 것"이 문제인 자리라면 그 자리에서 로그를 남긴다.

**3) 상대 파트의 구체 클래스를 직접 참조하지 않는다.**
AI가 `RunManager`를 직접 잡지 말고 `RunTimeLocator`를 쓴다. 그래야 씬 구조가 바뀌어도 그쪽 코드가 안 깨진다.

---

## 1. 런 시간 읽기

스폰 주기, 결계 축소, 보스 패턴 쿨다운은 **`Time.time`이 아니라 런 시간**을 봐야 한다. 런은 이탈자 대기 중에 멈추는데 `Time.time`은 안 멈춘다.

```csharp
using Geumeum.Common;

IRunTimeSource time = RunTimeLocator.Current;   // 항상 null이 아니다

double field    = time.FieldElapsedSeconds;      // 필드 경과(초), 보스전에서는 안 늘어난다
double boss     = time.BossElapsedSeconds;       // 보스전 경과(초), 열리기 전에는 0
double gameplay = time.GameplayElapsedSeconds;   // 위 둘의 합

if (time.IsPaused)
{
    return;   // 멈춘 동안 스폰, 쿨다운, 만료를 진행시키지 않는다
}
```

| 멤버 | 형 | 의미 |
|---|---|---|
| `FieldElapsedSeconds` | `double` | 필드 구간 경과. 필드 상한은 1800초(30분) |
| `BossElapsedSeconds` | `double` | 보스전 경과. 보스 제한은 840초(14분) |
| `GameplayElapsedSeconds` | `double` | 표식, 구조, 관전, 상호작용 만료, AI 쿨다운이 쓰는 시계 |
| `PauseReasons` | `RunPauseReason` | 지금 걸린 정지 사유(플래그) |
| `IsPaused` | `bool` | 하나라도 걸려 있으면 true |

**등록되기 전에 읽으면 전부 0이고 `IsPaused`는 false다.** 부트 직후 한 프레임 그럴 수 있으니, 0을 "런이 막 시작됐다"로 착각하지 않는다.

### 정지 사유

```csharp
[Flags]
public enum RunPauseReason
{
    None               = 0,
    ParticipantMissing = 1 << 0,   // 이탈자를 기다린다
    SceneTransition    = 1 << 1,   // 씬을 옮기는 중이다
    BossCinematic      = 1 << 2,   // 페이즈 전환 연출 등 판정을 멈추는 구간
    Result             = 1 << 3,   // 결과가 확정되어 더 진행하지 않는다
}
```

**자기가 건 사유만 푼다.** 한 명이 돌아왔다고 씬 로딩까지 재개시키면 안 된다.

---

## 2. 런 상태 읽기

```csharp
using Geumeum.Network;

RunManager run = RunManager.ServerInstance;   // 클라이언트에서는 null
if (run == null)
{
    return;
}

RunPhase   phase   = run.Phase;
RunOutcome outcome = run.Outcome;
RunId      runId   = run.RunId;
bool       paused  = run.IsPaused;
```

읽기로 쓰는 것만 적는다. **`StartRun`, `TryFinishRun`, `ServerReturnToLobby`는 네트워크 파트가 부른다. 다른 파트에서 부르지 않는다.**

### RunPhase

| 값 | 의미 |
|---|---|
| `Lobby` | 준비 대기, 런 시계 정지 |
| `Farming` | 개활 파밍, FieldLevel 0 |
| `Shrink1` | 1차 축소, FieldLevel 1 |
| `Shrink2` | 2차 축소, 객주 개방, FieldLevel 2 |
| `Shrink3` | 3차 축소, 신규 스폰 중단, FieldLevel 3 |
| `Boss` | 보스전, 결계 피해 종료 |
| `Result` | 정산, 모든 입력/드랍/구매 요청 거부 |

### RunOutcome

`None` / `Victory` / `TimeExpired` / `PartyWipe` / `PlayerLeft`

**승패 우선순위는 `Victory > PartyWipe > TimeExpired`이고 같은 틱에 겹치면 한 번만 확정된다.** 직접 결과를 확정하지 말고 사실만 보고한다.

---

## 3. 피해 주기

```csharp
using Geumeum.Common;

public interface IDamageable
{
    bool IsDead { get; }
    void TakeDamage(in DamageInfo damage);
}
```

**콜라이더에서 대상을 찾을 때는 반드시 `DamageTargetResolver`를 쓴다.** 직접 `GetComponent<IDamageable>()`을 하면 부위와 본체가 같은 오브젝트일 때 누구를 맞혔는지가 순회 순서에 따라 달라진다.

```csharp
if (!DamageTargetResolver.TryResolve(hitCollider, out IDamageable target))
{
    return;
}

if (target.IsDead)
{
    return;   // 사망 후 피해는 드랍과 점수 귀속을 중복시킨다
}

var info = new DamageInfo(amount, attackerNetId, HitStrength.Light);
target.TakeDamage(in info);
```

`CombatClock` and `MarkRegistry` use `RunTimeLocator.Current.GameplayElapsedSeconds`; paused wall-clock time does not consume the combat grace period. `NetworkTime.time` remains for network timing such as telegraph presentation and parry correction.

| API | 설명 |
|---|---|
| `DamageTargetResolver.TryResolve(Collider, out IDamageable)` | 콜라이더에서 |
| `DamageTargetResolver.TryResolve(Transform, out IDamageable)` | 트랜스폼에서 |
| `new DamageInfo(float amount, uint attackerNetId, HitStrength strength)` | 피해 값 |

**같은 공격이 여러 콜라이더를 맞히면 해석된 대상 기준으로 중복을 제거한다.** `netId`로 거르면 꼬리와 몸통이 같은 `NetworkIdentity`라 구분이 안 된다.

---

## 4. 회복시키기

```csharp
public interface IRecoveryTarget
{
    bool IsDead { get; }
    bool TryRestoreHealth(float amount);
}
```

**결계 회복 배수와 최대 HP 제한은 `TryRestoreHealth` 안에서 이미 적용된다.** 부르는 쪽에서 배수를 또 곱하면 두 번 걸린다.

**쓰러진 대상은 이 함수가 스스로 거부한다.** 회복이 부활을 대신하지 않는다.

---

## 5. 대상 지목 — TargetRef

표식, 락온, 화살, 피해, 보스 카운터가 **같은 방식으로** 대상을 가리킨다.

```csharp
TargetRef target = TargetRef.Create(runId, sceneRevision, entityNetId, partId);

bool valid  = target.IsValid;   // netId가 0이 아니다
bool isBody = target.IsBody;    // PartId == 0
```

| 필드 | 의미 |
|---|---|
| `RunIdValue` | 런이 바뀌면 옛 대상 참조가 통하지 않게 한다 |
| `SceneRevisionValue` | 씬이 바뀌면 같은 netId라도 다른 대상이다 |
| `EntityNetId` | 대상의 netId |
| `PartId` | **0은 본체**(`TargetRef.BODY_PART_ID`), 그 외는 부위. 보스 꼬리 세 덩이가 여기서 갈린다 |

**부위가 있는 대상은 부위마다 다른 `PartId`를 준다.** 꼬리를 지목했는데 몸통으로 해석되면 그로기 중 지정 꼬리 판정이 무너진다.

---

## 6. 표식 (무녀)

무녀가 방울로 건 표식을 읽는 창구다. **보스 카운터와 부위 피해 배율이 여기를 조회한다.**

```csharp
using Geumeum.Common;

// 누구든 걸었는가
double gameplay = RunTimeLocator.Current.GameplayElapsedSeconds;
bool marked = MarkRegistry.IsMarked(target, gameplay);

// 특정 무녀가 걸었는가 (사람별로 카운터 권리가 따로다)
bool mine = MarkRegistry.IsMarkedBy(owner, target, gameplay);

// 부여 시각까지 봐야 할 때
if (MarkRegistry.TryGetSlot(owner, index, gameplay, out MarkSlot slot))
{
    double appliedAt = slot.AppliedServerTime;
}
```

| API | 설명 |
|---|---|
| `IsMarked(TargetRef, double)` | 아무나 걸었는지 |
| `IsMarkedBy(ParticipantId, TargetRef, double)` | 이 소유자가 걸었는지 |
| `CountActive(ParticipantId, double)` | 이 소유자가 유지 중인 수 |
| `TryGetSlot(ParticipantId, int, double, out MarkSlot)` | 슬롯 하나 읽기 |
| `TryApply(...)` | **전투 파트가 부른다. 다른 파트는 읽기만 한다** |
| `ClearOwner(ParticipantId)` | 이탈 시 |
| `ClearAll()` | 씬 전환, 런 초기화 시 |

**규칙**

- 소유자 한 명당 **동시 2개**, 3번째를 걸면 가장 오래된 것이 밀린다
- **8초 만료**, 비교는 `[부여 시각, 만료 시각)` 이다
- **무녀가 둘이면 같은 대상에 각자 건다.** 남이 걸었다고 거부되지 않는다
- 같은 소유자가 같은 대상에 다시 걸면 거부된다. 매 타격마다 갱신되면 "패턴 종료 전 3초에 걸었나"를 판정할 수 없다
- 소유자 키는 `netId`가 아니라 **`ParticipantId`**다. 재접속해도 표식 소유권이 유지된다

---

## 7. 결계 상태 읽기

```csharp
using Geumeum.Common;

IBarrierState barrier = BarrierStateLocator.Current;   // 항상 null이 아니다

bool    active = barrier.IsActive;
Vector3 center = barrier.Center;     // 단계마다 보스 지점 쪽으로 보간된다, 고정값이 아니다
float   radius = barrier.Radius;     // 축소 중에는 보간 중인 값

bool  outside    = barrier.IsOutside(worldPosition);            // 높이는 안 보고 XZ 거리로만
float healingMul = barrier.GetHealingMultiplier(worldPosition); // 1이면 정상, 0이면 회복 봉쇄
```

등록된 결계가 없으면 **`IsActive=false`, `IsOutside=false`, 회복 배수 1**이 돌아온다. 즉 결계가 없는 씬에서도 코드가 그대로 돈다.

**실제 피해는 현재 원, 몰이 대상 선정은 확정 목표 원 기준이다.** 두 개가 다르다.

---

## 8. 참가자 상태 담고 되붓기

이탈 후 재접속과 씬 전환에서 플레이어 값을 보존하는 계약이다. **자기 파트가 플레이어에 붙는 상태를 들고 있으면 이걸 구현한다.**

```csharp
public interface IRunStateCarrier
{
    int  CarrierOrder { get; }                                           // 낮은 값이 먼저 담기고 먼저 되부어진다
    void Capture(RunParticipantSnapshot snapshot);                       // 담는다, 상태를 바꾸지 않는다
    bool Validate(RunParticipantSnapshot snapshot, out string failure);  // 되부을 수 있는지 본다
    bool Restore(RunParticipantSnapshot snapshot);                       // 붓는다, Validate 통과 후에만
}
```

**`Validate`가 실패하면 런을 재개하지 않는다.** 필수 상태가 빠졌는데 성공을 돌려주면 중간 상태로 게임이 진행된다.

`Capture`는 **상태를 읽기만** 한다. 여기서 값을 바꾸면 이탈 처리 도중에 게임이 변한다.

### 복구 실패와 재접속 (2026-09-12)

- `RunParticipantRegistry.TryPeek`으로 상태를 읽고 성공 후 `ServerCompleteRestore`로 소비한다. 실패한 복구나 오래된 완료 요청이 원본 상태를 없애면 안 된다.
- 씬 전환에서는 `RunSceneDirector`가 캡처한 참가자 전원의 상태를 바인딩 완료까지 소유한다. 로스터는 이 상태를 덮어쓰거나 먼저 복구하지 않는다.
- 원격 참가자 복구 실패 또는 도착 제한 시간 초과는 해당 연결만 끊고 `WaitingForParticipant`와 `ParticipantMissing` 정지를 유지한다. 같은 인증 신원이 돌아와 상태 복구를 마쳐야 진행한다. 정상 참가자 수를 줄여 성공 처리하지 않는다.
- 이미 복구한 플레이어는 다시 적용하지 않는다. 연결이나 플레이어 오브젝트가 바뀌면 새 대상으로 복구한다. 호스트 복구 실패와 캡처 불가는 명시적인 전환 실패로 남긴다.
- 연결 번호는 재사용될 수 있다. 도착 timeout의 대상은 인증 신원으로 찾은 현재 미도착 연결이며, 도착 보고와 복구 거절은 연결 객체에 귀속한다. 다른 참가자나 같은 신원의 새 연결이 이전 번호의 도착·실패 상태를 물려받지 않는다.
- 대기 포기의 말단 종료도 실패 당시 연결 객체가 현재 연결과 같을 때만 분리한다. 캡처 실패 연결 역시 이 목록에 기록하며, 그 번호로 돌아온 새 연결은 종료 대상이 아니다.
- 일반 재접속도 복구 실패 시 초기 상태로 허용하지 않는다. 원래 자리와 snapshot을 보존하고 재접속을 기다린다. 호스트는 기존 대기 포기 API로 Result 경로를 선택할 수 있다.
- 새 런과 서버 종료에서 이전 복구 대기와 빈자리 권한을 정리한다. 구현 검증 기록은 주간 계획의 참가자 복구 항목을 따른다.

---

## 9. 씬이 바뀔 때 다시 잡기

```csharp
public interface IRunSceneBinding
{
    void Bind(in SceneTransitionSnapshot transition);   // 같은 세대로 두 번 불려도 결과가 같아야 한다
    void Unbind();
}
```

**영속 오브젝트의 `OnStartServer`는 씬마다 다시 불리지 않는다.** 씬에 있는 `GameEventBus`나 마커를 참조한다면 `Bind`/`Unbind`에서 직접 구독을 갈아 끼워야 한다. 안 하면 파괴된 옛 씬의 버스를 계속 붙들고 있게 된다.

---

## 10. 시스템 파트가 구현해서 넘기는 것

### 전투 수치

```csharp
public interface ICombatLoadoutProvider
{
    uint Revision { get; }   // 장비, 특성, 레벨 중 하나라도 바뀌면 올라간다
    bool TryBuildLoadout(ParticipantId participant, out CombatStatsSnapshot snapshot);
}
```

`Revision`이 같으면 전투 쪽에서 다시 만들지 않는다. **매 명중마다 인벤토리를 다시 뒤지지 않게 하려는 값이다.**

2026-09-09 현재 구현은 `InventoryController`다. `PlayerAttack`은 공격 수락 때 값을 복사하고 명중 때 그 복사본을 사용한다. 검·활·방울의 장착 공격력과 수락 당시 치명 준비 상태가 유지된다. `MaxHealth`는 현재 파티 성장 상태를 사용하며, 성장 관리자가 없는 개발 구성에서는 시작 레벨 곡선 값으로 대체한다. 장비 옵션·특성 적용은 아직 없다.

서버의 `IGrowthParticipant.GrowthChanged`를 구독해 성장 변경 즉시 전투 revision을 갱신한다. 따라서 `TryBuildLoadout`을 다시 호출하기 전에 revision만 읽는 캐시도 성장 변경을 감지한다. 동일 성장 상태 재적용은 중복 증가시키지 않는다.

### 장착과 해제

- 소유 플레이어의 `InventoryController.RequestEquip(slot, instanceId, expectedRevision)` 또는 `RequestUnequip(expectedRevision)`를 사용한다. 내부 Command가 서버 소유권·런 상태·생존·비전투·revision을 검사한다.
- 결과는 소유 클라이언트의 `EquipResult(requestId, result, revision)` 이벤트로 온다. 장착값은 `EquippedWeapon`, 보관 revision은 `Revision`, 전투 revision은 `LoadoutRevision`이다.
- 장착 교체는 선택한 보관 칸과 현재 장비를 교환한다. ID 0 기본 무기는 보관하지 않는다. 해제는 실제 장비를 빈 칸에 넣고 같은 종류의 기본 무기를 만든다.
- 서버 파트 간 호출은 `ServerTryEquip`/`ServerTryUnequip`을 사용한다. `IEquipmentWeaponTarget`은 검증을 끝낸 인벤토리가 Combat 무기에 적용하는 내부 계약이며, 소유권 검사를 대신하지 않는다.
- 재접속 snapshot에는 인벤토리와 장착 무기가 함께 들어간다. 역할 무기 종류 불일치와 장착/보관 중복 ID는 복구 전에 거부한다.

### 파티 성장

- `GrowthManager`가 공용 누적 XP와 레벨을 소유한다. 조회는 `GrowthProviderLocator.HasProvider`를 확인한 뒤 `Current.TryGetState(participant, out state)`를 사용한다. 성장 상태는 SyncVar로 전달된다.
- 잡귀 사망은 `EnemyDiedEvent.ForEnemy`에 스폰 당시 `FieldLevelAtSpawn`과 `RewardMultiplier`를 전달한다. XP와 재화는 이 배수를 정수 내림해 적용하며 현재 구간의 배수를 다시 읽지 않는다. 같은 적의 보상은 런마다 한 번만 지급한다. 보스는 `ForBoss`를 사용하고 `Kind`는 null이다.
- `ServerTryAddExperience`는 여러 레벨을 한 번에 올릴 수 있다. `PlayerStatus`는 최대 HP 증가분만 생존자의 현재 HP에 더하며, 사망·관전 상태를 되살리지 않는다.
- 재접속은 현재 파티 성장 상태를 우선하고, 오래된 스냅샷과의 최대 HP 차이를 한 번 반영한다. 성장 조회 실패 시 참가자 등록과 기존 상태를 바꾸지 않는다. `RunResetEvent`에서 다음 런 상태로 초기화하며 호스트에도 `GrowthChanged`를 통지한다.

### 공용 재화와 부활 비용

- `PartyWalletLocator.HasProvider`로 연결 여부를 확인하고 `Current`의 `Balance`/`BalanceChanged`를 읽는다. 생산 `SessionManagers.prefab`의 `CurrencyManager`가 서버 원본을 소유한다.
- 서버 거래는 `TryPrepareSpend`로 계획을 만들고 `TryCommitSpend`로 확정한다. 계획의 런 ID·잔액·revision이 하나라도 달라지면 거부한다. 구매 상품 지급·위치·개방 조건은 아직 별도 상점 실행기 구현 대상이다.
- 잡귀 재화는 스폰 당시 FieldLevel과 보상 배수를 적용해 정수 내림한다. 보스는 잡귀 재화를 지급하지 않는다. 같은 런의 같은 적은 한 번만 지급한다.
- 장비 드랍도 `FieldLevelAtSpawn`을 사용하고 같은 사망의 드랍 여부를 한 번만 결정한다. 보스 factory는 3구간 보상으로 고정하며 잡귀 배수를 장비 등급에 곱하지 않는다.
- 부활 차감은 `TryApplyReviveLoss(ParticipantId, deathGeneration, ratio, out loss)`로 확정한다. 같은 틱에는 구조 우선이며 구조 10%, 자동 20%를 확정 직전 잔액에서 내림한다. 잔액 0도 부활할 수 있다.
- `ReviveDeathGeneration`을 포함하는 참가자 스냅샷은 스키마 3이다. 유료 부활에 지갑이 없으면 상태 변경 없이 거부하며, 개발용 `ServerReviveAll`은 무상 경로다.
- 부활의 최종 중재는 플레이어별 상태 계산 뒤 서버 scheduler에서 실행한다. 씬 전환 중 플레이어 수가 일시적으로 0이 되어도 같은 scheduler의 중재 등록을 유지한다.
- `AuthorityTickScheduler.ServerGeneration`은 서버 scheduler 실행 수명을 구분한다. 부활 중재는 인스턴스뿐 아니라 이 세대를 비교하므로 같은 매니저의 서버 재시작 뒤에도 다시 등록된다.
- 서버 관전 잔여 시간은 정지 중 고정된다. 정지 중 복구했다면 재개 시 복구 이후 대기 시간만 보정한다. 비유한·음수 잔여 시간은 복구 전에 거부하고, 구조 확정 직전에는 구조자의 이동·거리·행동과 대상 사망 세대를 다시 검사한다.

### 필수 네트워크 스폰 프리팹

- `IRequiredSpawnPrefabProvider.CollectRequiredSpawnPrefabs(List<GameObject>)`는 부트 검사에 필요한 프리팹을 제공하는 Common 계약이다. `DropDirector`는 DroppedItem 프리팹을 제공한다.
- Main 프로필의 `ServerBootGate`는 비활성 오브젝트의 공급자도 초기 검사에 포함하고, null 또는 `NetworkManager.spawnPrefabs` 미등록 참조가 있으면 서버 시작을 거부한다. 런타임 등록을 대신하지 않으므로 Boot 씬의 목록에도 프리팹이 있어야 한다. DevScene 프로필은 이 생산 배선 검사를 생략한다.
### 점수

```csharp
public interface IRunResultProvider
{
    int ScoreRuleVersion { get; }                     // 배점 판. 결과에 함께 실린다
    int CalculateScore(in RunResultSnapshot facts);   // 순수 함수, 상태를 읽거나 바꾸지 않는다
}
```

```csharp
RunResultProviderLocator.Register(provider);
bool real = RunResultProviderLocator.HasProvider;   // 등록 전에는 false, 점수는 0이 나온다
```

**살아 있는 플레이어나 적 목록을 다시 읽지 않는다.** 정산 시점에는 이미 파괴됐을 수 있어서, 확정된 사실만 담긴 `RunResultSnapshot`으로 계산한다.

---

## 11. 세계의 확정 사실 알리기

```csharp
using Geumeum.Common;

GameEventBus bus = GameEventBus.Instance;   // 씬 로컬 싱글턴

bus.Publish(EnemyDiedEvent.ForEnemy(enemyNetId, kind, position, killerNetId,
    fieldLevelAtSpawn, rewardMultiplier));
bus.Subscribe<EnemyDiedEvent>(OnEnemyDied);
bus.Unsubscribe<EnemyDiedEvent>(OnEnemyDied);
```

**무엇을 버스에 싣는가**

| 성격 | 어디로 |
|---|---|
| 주인 없는 세계의 사실 (적이 죽었다, 결계가 줄었다) | `GameEventBus` |
| 내 캐릭터의 이산 전이 (무기가 바뀌었다, 락온이 걸렸다) | C# event |
| 매 틱 바뀌는 연속값 (HP, 스태미나) | 프로퍼티를 읽는다 |

**버스에 내 HP를 실으면 3인에서 남의 변화까지 매번 받게 된다.** 연속값에 훅을 달면 초당 50번 나가는데 UI가 하는 일은 같다.

🚨 **버스는 네트워크 전송 기능이 없다.** 호스트에서만 들리는 `Publish`를 멀티플레이 구현으로 착각하면 안 된다. 원격에 알리려면 SyncVar나 RPC를 쓴다.

---

## 12. 연출 호출하기

```csharp
using Geumeum.Common;

ICombatFeedback fb = CombatFeedbackLocator.Current;   // 항상 null이 아니다

fb.PlayHit(HitStrength.Light, duration);
fb.PlayParrySuccess();
fb.PlayCritical(multiplier);
fb.PlayGroggyEnter();
fb.PlayPartBreak();
```

**구현체가 없으면 아무 일도 하지 않는다.** 아트가 안 붙은 상태에서도 로직을 돌릴 수 있다.

⚠️ **연출이 끝났다고 서버 승패가 정해지면 안 된다.** 렌더링 완료나 애니메이션 이벤트가 페이즈 판정의 주인이 되지 않는다.

### 소리

```csharp
using Geumeum.Common;

SfxPlayer.Play(SoundKey.EnemyDeath, transform.position, isLocalPlayer, netId);  // 3D
SfxPlayer.Play(SoundKey.BossTellA, isLocalPlayer);                              // 2D
```

**2D인지 3D인지는 카탈로그가 정한다.** 보스 예고음을 위치와 함께 부르면 아레나 반대편에서 안 들려 패링을 못 배운다. 필요한 키가 없으면 추가해 달라고 요청한다 — 카탈로그에 없는 키를 쓰면 경고만 쌓인다.

---

## 13. 락온 조준점

```csharp
public interface IAimAssistTarget
{
    Vector3 AimPoint { get; }
}
```

`AimAssistPoint` 컴포넌트를 프리팹의 겨냥할 위치에 붙이면 된다.
**붙은 게 없으면 렌더러 경계 상자 중심으로 폴백한다 — 배를 겨눈다.**

🚨 **부위마다 하나씩 붙인다.** 꼬리가 세 덩이인데 몸통에만 붙이면 꼬리를 잠가도 시선이 몸통으로 간다.

---

## 14. 패링당하는 쪽

```csharp
public interface IParryable
{
    float ParryWindowDuration { get; }        // 이 공격이 여는 판정 창 길이(초)
    void  OnParried(uint parriedByNetId);     // 패링당했을 때 서버가 부른다
}
```

경직 같은 반응은 구현체가 정한다.

⚠️ **예고 선딜이 패링 창보다 짧으면 안 된다.** 짧으면 창을 열 시간이 없어 패링이 사실상 사라지고 경고 한 줄만 남는다.

---

## 15. UI가 읽는 창구

UI는 플레이어 컴포넌트를 직접 잡지 않는다. **`LocalCombatState` 하나만 본다.**

```csharp
using Geumeum.Combat;

float health  = LocalCombatState.Health;
float maxHp   = LocalCombatState.MaxHealth;
float stamina = LocalCombatState.Stamina;
float spirit  = LocalCombatState.Spirit;
float gauge   = LocalCombatState.ParryGauge;

bool ready    = LocalCombatState.IsCriticalReady;
bool lockedOn = LocalCombatState.IsLockedOn;

LocalCombatState.LifeStateChanged += OnLifeStateChanged;
LocalCombatState.ActionRejected   += OnRejected;
```

| 이벤트 | 언제 |
|---|---|
| `BoundChanged` | 자기 캐릭터가 생기거나 사라졌다 |
| `LifeStateChanged` | 살아있음/쓰러짐/관전 전이 |
| `WeaponChanged` | 무기 교체 |
| `LockOnChanged` | 락온 획득/해제 |
| `CriticalReadyChanged` | 치명의 일격 준비 |
| `RescuingChanged` | 구조 중 여부 |
| `ActionRejected` | 요청이 거부됨 |

**늦게 열린 UI는 `LocalCombatState.RaiseAll()`로 현재 값을 다시 받는다.** 재접속이나 패널을 나중에 켰을 때 빈 화면이 되지 않게 한다.

### 거부 사유

```csharp
public enum ActionRejection
{
    None      = 0,
    Duplicate = 1,   // 같은 요청이 두 번 왔다
    NotReady  = 2,   // 아직 준비되지 않았다
    NoWeapon  = 3,   // 이 행동에 필요한 무기가 아니다
    State     = 4,   // 페이즈, 생명 상태, 정지 때문에 안 된다
    Busy      = 5,   // 다른 행동 중이거나 쿨다운
    Resource  = 6,   // 스태미나나 신력 부족
}
```

**"씹혔다"와 "거부됐다"를 구분하려고 만든 값이다.** 조용히 return하면 플레이어는 버그로 느낀다.

---

## 16. 밸런스 수치 읽기

```csharp
using Geumeum.Data;

float walkSpeed = GameDB.Combat.WalkSpeed;
var   enemy     = GameDB.Enemy;
var   boss      = GameDB.Boss;
```

`Combat` / `Arrow` / `Enemy` / `Barrier` / `Spawn` / `Boss` / `Equipment` / `DropTable` / `Shop` / `LevelCurve` / `Talent` / `Score` / `DayCycle` / `Sound` / `Camera`

🚨 **`Awake`와 필드 초기화식에서 읽지 않는다.** 같은 씬 컴포넌트의 `Awake` 순서는 보장되지 않아 초기화 전 DB를 잡는다. **`Start`, `OnStartServer`, `OnStartClient` 이후에 읽는다.**

```csharp
private EnemySO _enemyData;
public override void OnStartServer() { _enemyData = GameDB.Enemy; }
```

매 프레임 `GameDB.X`를 타지 말고 초기화 때 한 번 캐싱한다.

---

## 16.5 화면 디버그 로그 — GDebug

판정 결과를 게임을 멈추지 않고 **화면 오른쪽 가운데**에서 바로 본다. 콘솔을 열지 않아도 되고, 3인 테스트 중에도 읽힌다.

```csharp
using Geumeum.Common;

GDebug.Log("일반 메시지");
GDebug.Success("치유 완료, 2명 회복");
GDebug.Fail("패링 실패, 타이밍");
GDebug.Warn("스태미나 부족");
GDebug.Server("방울 3명 적중, 신력 +15");   // 서버가 확정한 사실
```

`F8` 표시 토글, `F9` 비우기. **씬에 아무것도 배치하지 않아도 첫 로그에서 오버레이가 스스로 생긴다.**

| 알아 둘 것 | 내용 |
|---|---|
| 릴리스 빌드 | `[Conditional]`이라 **호출부가 인자 계산까지 통째로 제거된다.** `if (개발중)` 검사를 넣을 필요가 없다 |
| 성능 | 고정 링 버퍼 18줄, 프레임당 할당 없음. 판정 루프에 걸어도 된다 |
| 반복 | 같은 줄이 연달아 오면 `x24`처럼 합쳐진다. 매 프레임 도는 자리에 걸어도 화면이 안 덮인다 |
| 시간 | `Time.unscaledTime` 기준이라 히트스톱이나 슬로우모션에 얼어붙지 않는다 |

🚨 **어느 화면에 뜨는지 먼저 정하고 건다.**

| 어디서 부르나 | 누가 보나 |
|---|---|
| `[Server]` 메서드 | **호스트 화면만** |
| `[TargetRpc]` | 그 요청을 보낸 사람만 |
| `[ClientRpc]`, SyncVar 훅 | 전원 |

`[ClientRpc]`에서 그냥 부르면 3인에서 남의 판정까지 내 화면을 덮는다. 자기 것만 봐야 하면 `isLocalPlayer`로 거른다.

---

## 17. 하지 말 것

| 하지 말 것 | 왜 | 대신 |
|---|---|---|
| `Time.time`으로 런 진행 재기 | 이탈 대기 중에도 흘러간다 | `RunTimeLocator.Current` |
| `RunManager`를 AI/Systems가 직접 참조 | 씬 구조가 바뀌면 깨진다 | `IRunTimeSource` |
| `GetComponent<IDamageable>()` 직접 호출 | 부위와 본체가 같은 오브젝트라 순회 순서가 결과를 정한다 | `DamageTargetResolver` |
| 회복에 결계 배수를 곱하기 | `TryRestoreHealth`가 이미 곱한다 | 그냥 양만 넘긴다 |
| `netId`로 참가자 기억하기 | 재접속하면 바뀐다 | `ParticipantId` |
| 결과를 직접 확정하기 | 같은 틱에 겹치면 이미 보낸 결과를 못 되돌린다 | 사실만 보고한다 |
| `GameEventBus`로 원격에 알리려 하기 | 전송 기능이 없다, 호스트에서만 들린다 | SyncVar 또는 RPC |
| `Awake`에서 `GameDB` 읽기 | 초기화 전 DB를 잡는다 | `Start` 이후 |
| 로케이터에 null 검사 넣기 | 절대 null이 아니다 | 그냥 쓴다 |

---

## 17.5 거래와 정밀 사격 창구 (2026-09-09 구현, 통합 검증 중)

아래 창구는 현재 코드에 추가됐다. 검증 통과 범위는 후속 실행 기록으로 확인하며, 구매·양도 UI와 물약 사용은 별도 구현 대상이다.

| 창구 | 호출/구독 | 계약 |
|---|---|---|
| `ShopTransactionService` | `RequestPurchase(shopNetId, kind, walletRevision, inventoryRevision)` | 현재 객주 위치·개방 구간·생명·비전투·잔액·소지 제한을 서버가 다시 검사한다 |
| 보관 장비 티켓 | `RequestRerollTicket` / `RequestUpgradeTicket` | 티켓 슬롯, 장비 슬롯, 장비 InstanceId, 예상 inventory revision을 보낸다. 구매한 티켓 한 장을 소비한다 |
| 장착 장비 티켓 | `RequestRerollEquippedTicket` / `RequestUpgradeEquippedTicket` | 티켓 슬롯, 현재 장착 장비 InstanceId, 예상 inventory revision을 보낸다. 사용을 위해 장비를 임시 해제하지 않는다 |
| `ShopTransactionService.TransactionCompleted` | `ShopTransactionResult` | RequestId, 작업 종류, 거부 사유, 서버 revision, 적용 장비를 받는다 |
| `EquipmentTransferService` | `RequestOffer` / `RequestAccept` / `RequestReject` / `RequestCancel` | 단방향 장비 양도. 수락 시 양쪽 상태와 revision을 재검증한다 |
| `EquipmentTransferService.TransferChanged` | `EquipmentTransferResult` | 제안 상태와 양쪽 revision을 읽는다. 만료 시각은 게임 시각이다 |
| `IPrecisionInterruptTarget` | `TryInterruptPrecisionShot(in PrecisionInterruptContext)` | 서버 화살의 실제 충돌이 호출한다. HitServerTime은 `NetworkTime.time` 기준이며 게임 경과 시간과 혼용하지 않는다 |

거래 요청 메서드의 반환값은 로컬 RequestId다. `0`은 전송하지 못했다는 뜻이고 양수도 성공 확정이 아니다. 결과 이벤트의 `Rejection`을 확인한다. 런·씬 revision과 소유 플레이어 netId 세대는 서비스가 헤더에 넣으며, 화면에서 서버 잔액이나 인벤토리 원본을 직접 바꾸지 않는다.

양도 제안의 송신자는 상대 인벤토리 revision을 알 필요가 없다. 서버가 제안 시점의 수신자 revision을 캡처하며, 수락할 때 수신자가 자기 화면의 revision을 보낸다. 소유자 전용 인벤토리를 이 기능 때문에 전체 공개로 바꾸지 않는다.

티켓 사용은 결제와 분리된다. 구매 때 공용 지갑을 차감하고, 사용 때는 보관한 티켓을 소비한다. `ShopSO.ItemEntry.MaxStack`은 슬롯당 한도이고 `CarryLimit`은 전체 소지 한도다. 현재 초기값은 물약 stack 3, 티켓 stack 1이며 기존 상품 가격은 유지한다. 양도 제안 시간은 `CombatConfigSO.EquipmentTransferOfferDuration`의 초기값 30초다. 이 두 초기값은 생산 밸런스 확정과 구분한다.

정밀 입력은 Player의 `PrecisionShotController`가 소유한다. 활을 장착한 뒤 Mouse1 홀드·해제로 요청하며, 홀드 시간·이동 배율·스태미나는 `CombatConfigSO`에서 읽는다. 충돌 대상의 `PrecisionTargetRef`가 RunId·SceneRevision·NetId·PartId를 제공하며, 화살은 발사 당시 세대와 일치하는 대상만 전달한다. 보스는 현재 예고·대상·신궁 대응 패턴과 phase의 `IsAttackWindup(attackInstanceId)`를 검사하고 보스 공격 InstanceId당 한 번만 중단한다. UI가 이 인터페이스를 호출해 적을 직접 중단해서는 안 된다.

## 18. 아직 없는 것

기대하고 짜면 안 되는 것들이다.

- ~~**표식을 읽는 소비자**~~ — 해소됨(2026-09-08). 보스 무녀 공명이 첫 소비자다. 아래 "무녀 표식 공명" 참조
- ~~**`MarkRegistry.ClearAll()` 호출부**~~ — 해소됨(2026-09-09). 런 초기화·결과·서버 종료·씬 전환에 연결됨
- **그로기 잔여** — 보스 게이지 감소·차사 패링·**무녀 공명**·진입 시 공격 취소·본체 피해 배율은 구현됨(2026-09-08). 신궁 정밀 사격은 2026-09-09 코드 추가 후 통합 검증 중이며, 꼬리 배율·잡귀 연장은 아직 미구현
- **보스 부위** — `TargetRef.PartId`는 준비됐지만 부위를 가진 프리팹이 없다
- **장비 옵션/특성 수치** — 장착 공격력과 파티 성장의 최대 HP 공급은 구현됨(2026-09-09). 옵션·특성 반영은 남아 있다
- **점수** — `IRunResultProvider` 구현체가 없어 지금은 0점이 나온다

---

## 물어볼 곳

전부 @AidenKim923에게. 특히 **"이걸 서버에서 해야 하나 클라에서 해야 하나"**가 애매하면 짜기 전에 물어보는 게 싸다. 나중에 발견되면 3인 접속에서만 재현되는 결함이 된다.

## 보스 공격 수명과 예고 (2026-09-08)

2026-09-09 광폭화: `BossController.IsEnrageWarning`/`IsEnraged`와 변경 이벤트를 표현에서 읽을 수 있다. 서버는 보스 시계 720/810초를 소비하며, 정지·씬 전환·페이즈 잠금 중에는 진행하지 않는다. 새 공격의 피해 배율은 수락 순간 고정한다. SO 전역 간격은 1페이즈 3초, 2페이즈 2.2초, 광폭화 1.4초이며 개별 패턴 쿨다운과 별개다. 꼬리 잔여 수에 따른 2.0/1.8초 분기는 꼬리 시스템 구현 후 연결한다.

보스 페이즈·패턴 로직은 서버에서만 실행되며 작성은 몬스터·보스 담당(@oname36-creator)이 한다. 새 네트워크 멤버(`[SyncVar]`·`[Command]`·Rpc)와 `.server.cs` 변경은 @AidenKim923에게 요청한다.

- `BossPhaseStateBase.CancelActiveAttack()`: 현재 판정과 창을 버리고 회복 상태로 이동한다. `Exit()`는 멱등이며 이후 Tick을 받더라도 다시 공격하지 않는다.
- 패링 키의 `WindowId`는 보스 전체의 `AttackInstanceId`다. 페이즈 교체로 카운터를 초기화하지 않는다.
- 전환·그로기·사망·Result·런 초기화·서버 종료는 같은 정리 경로를 사용한다. 전환 공격 잠금은 `GameDB.Boss.TransitionLockDuration`을 사용하며, 컷신/플레이어 입력 잠금과는 별개다.
- 예고 표현은 `IBossTelegraphSource`의 `TelegraphChanged`를 구독한 다음 `TelegraphCue`를 읽는다. 구독 해제는 표현 컴포넌트가 책임진다. 서버 상태는 SyncVar로 전달하므로 늦은 접속도 현재 값을 받는다.
- 표현은 `cue.IsPendingAt(NetworkTime.time)`이 false면 기존 예고를 지운다. true면 공격 ID를 키로 갱신한다. 별도 판정이나 피해 적용을 하지 않는다.
### 무녀 표식 공명 (2026-09-08)

무녀가 방울로 건 표식이 그로기 게이지를 깎는 경로다. **표식을 거는 쪽은 이미 있었고, 읽는 쪽이 이번에 생겼다.**

- 성립 조건: **카운터 패턴이 끝나는 시점**에, 그 시점 기준 `GameDB.Boss.BrandWindow`(3초) 안에 보스 본체에 걸린 표식이 있을 것.
- "패턴이 끝나는 시점"은 판정이 끝나고 회복으로 넘어가는 순간이다. **패링이나 대상 소실로 취소된 공격은 성립하지 않는다** — 패턴이 끝나야 카운터라는 계약이다.
- 반복 패턴(9번 연쇄 부채꼴)은 마지막 판정에서 한 번만 통지한다. 펄스마다 깎이지 않는다.
- **공격 하나당 한 번만 깎인다.** 무녀가 둘이어도 두 번 깎이지 않는다.
- 카운터 패턴은 코드가 아니라 `BossPatternSO`의 `GaugeReductionEntry.CounterPatternIds`에 있다 — 차사 1·6, 신궁 5·9, 무녀 4·8. **바꿀 때 코드를 고치지 않는다.**
- 표식 조회는 `MarkRegistry.IsMarkedWithin(target, serverTime, window)`다. `serverTime`에는 부여·조회 모두 `RunTimeLocator.Current.GameplayElapsedSeconds`를 넣는다. 누가 걸었는지는 보지 않는다.
- 대상 참조에는 런 id와 씬 세대가 함께 들어간다. 이전 런에서 남은 표식은 구조적으로 맞지 않는다.
- 신궁(5·9) 카운터는 **데이터만 있고 호출부가 없다.** 정밀 사격 시전 취소가 선행이다.

- 현재는 단일 예고만 표현할 수 있는 상태 계약이다. 3인 순차 표식·다중 cue·재접속 일시정지 시각 보정·실제 데칼은 추가 구현 대상이다. 3PC 검증 전에는 네트워크 완료로 보지 않는다.
