# 패링 시스템 기술 설계

> 상태: 구현 계약 확정본  
> 기준일: 2026-08-30  
> 대상: `ParryController`, `ParryJudge`, `ParryGauge`, `PlayerStatus`, `PlayerAttack`, 창귀·보스의 패링 가능 공격

패링은 그믐의 시그니처 메커닉이자 네트워크 핵심 기술 과제다. 이 문서는 아래 기준 문서에 흩어진 결정을 실제 코드로 옮길 수 있도록 하나의 실행 계약으로 정리한다.

- 수치·보상: [`../02-game-design.md`](../02-game-design.md) 3장
- 네트워크 원칙: [`NETWORK_AUTHORITY.md`](NETWORK_AUTHORITY.md) 6장
- 플레이어·AI 구조: [`../04-architecture.md`](../04-architecture.md) 4.2·4.3·8장
- 검증 기준: [`../ai/03_VERIFICATION_PLAYBOOK.md`](../ai/03_VERIFICATION_PLAYBOOK.md), [`../ai/04_MULTIPLAYER_TEST_PROTOCOL.md`](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md)

수치의 원본은 `CombatConfigSO`, `EnemySO`, `BossPatternSO`다. 이 문서의 숫자는 현재 원본을 설명하는 값이며 코드 상수로 복제하지 않는다. 네트워크 허용치만 `Constants.Network`가 소유한다.

---

## 0. 이 문서가 확정하는 핵심 결정

1. **패링 판정은 서버만 확정한다.** 클라이언트는 입력 시각과 자신이 관찰한 window 키만 제출한다. 제출된 시각은 이동 입력용 허용치가 아니라 **연결 rtt 기반의 패링 전용 하한**으로 clamp한다.
2. **0.32초는 공격별 서버 window의 기본 길이**다. 플레이어가 매 프레임 패링 콜라이더를 켜는 구조가 아니다.
3. 실제 판정 시각은 `입력 시각 + 선딜 0.05초`다. 선딜 수치가 판정에서 사라지지 않는다.
4. 패링 가능 공격은 텔레그래프 시작 시점에 미래의 window와 실제 타격 시각을 함께 등록한다.
5. 서버는 늦게 도착한 입력을 처리할 시간을 확보하기 위해 window 종료와 피해 확정 사이에 **0.18초의 판정 여유**를 둔다. 피해를 적용한 뒤 롤백하지 않는다.
6. request는 `RequestId`, window는 `(AttackerNetId, WindowId)` 복합 키로 식별한다. 둘 다 중복·역순 요청 방지에 사용한다.
7. 대상 플레이어가 가진 활성 window는 한 개다. 새 window가 오면 이전 미소비 window는 무효화된다.
8. 유효한 시도가 정확한 window를 참조하면 성공·실패와 무관하게 그 window를 한 번 소비한다.
9. 패링 게이지는 플레이어별 서버 상태다. 100 이상에서 **다음 유효 공격 행동을 시작할 때** 한 번 소비하며, 공격이 빗나가도 돌려주지 않는다.
10. 구르기와 패링이 같은 `bool` 무적 플래그를 켰다 끄지 않는다. 만료 서버 시각을 연장하는 방식으로 통합한다.

위 결정은 이 문서의 나머지 절에서 타입·API·처리 순서와 함께 구체화한다.

---

## 1. 현재 구현과 목표 상태

### 1.1 현재 코드 스냅샷

| 파일 | 현재 상태 | 이 설계에서 필요한 변경 |
|---|---|---|
| `ParryController.cs` | `CmdParry(double)`이 단일 시작 시각·길이에 바로 비교 | request/window 식별, 시간 보정, 상태·무기·자원 검증, 1회 소비, 결과 코드, 공격자 콜백 |
| `ParryJudge.cs` | 입력 시각이 구간 안인지 계산하는 순수 함수 | 판정 시각을 명시적으로 받아 inclusive 경계를 유지하고 잘못된 수치 입력을 호출부가 거부 |
| `IParryable.cs` | 길이와 `OnParried` 계약만 존재 | 공격 실행 인스턴스가 구현하고, window 등록 시 해당 인스턴스 참조를 서버에 전달 |
| `IParryWindowTarget` | 없음 | AI가 Combat 구체 클래스를 참조하지 않고 window를 열고 닫는 공용 대상 계약 추가 |
| `PlayerStatus.cs` | 스태미나 차감과 `SetInvincible(bool)`만 존재 | 전량 회복 API와 만료 시각 기반 무적 API 추가 |
| `PlayerWeapon.cs` | 초기 무기 확정만 존재하고 **재배분을 명시적으로 거부한다** | 서버 무기 변경 경로 자체가 선행 과제. 8.3 참고 |
| `EnemyController.cs` | Brain이 `ShouldAttackNow`를 올린 틱에 즉시 `TakeDamage` 호출 | 창귀 패턴에서 텔레그래프 → window 등록 → 절대 시각 `impactTime` 타격 확정 순서를 구현. 7.2 참고 |
| `HeokkaebiBrain.cs` | `Time.deltaTime` 누적 타이머, 창귀 Brain은 없음 | 창귀 돌진 Brain 신설과 절대 서버 시각 기준 전이. 7.2 참고 |
| `EnemySO.asset` | 창귀 수치가 전부 0 (미입력) | 창귀 사거리·선딜·쿨다운·`ParryWindowDuration` 입력 |
| `BossPatternSO.asset` | `_phases`·`_patterns` 모두 빈 배열 | 보스 확장 단계의 선행 데이터 |
| `Constants.Network` | 이동 입력용 시각 허용치만 존재 | `PARRY_RESOLUTION_LEAD`, `PARRY_MAX_INPUT_TIME_BEHIND`, `PARRY_INPUT_TIME_TOLERANCE`, `PARRY_MIN_REQUEST_INTERVAL` 추가 |
| `ParryGauge` | 없음 | owner 동기화 게이지와 1회 치명 배율 소비 구현 |
| `PlayerAttack` | 없음 | 유효 공격 시작 시 치명 배율을 한 번 가져와 공격 실행 전체에 고정 |

`ParryGauge`와 `PlayerAttack`은 아직 존재하지 않는다. 이 문서의 8장과 16장이 두 타입을 확정된 것처럼 서술하는 것은 **구현 계약이지 현재 상태가 아니다.**

### 1.2 목표 완료 상태

창귀 한 종을 기준으로 아래 전체 경로가 이어져야 패링 기본 구현이 완료다.

```text
창귀가 타격 시각 결정
  → 대상 IParryWindowTarget에 미래 window 등록
  → 대상 클라이언트가 window 키 수신
  → 플레이어 입력과 로컬 모션 즉시 재생
  → 서버가 request/window/시각/상태/무기/스태미나 검증
  → 성공이면 window 선소비
  → 플레이어 보상 + IParryable.OnParried
  → 창귀 공격 취소·경직
  → 소유 클라이언트 성공 연출
```

`ParryJudge` 단위 테스트나 성공 로그만으로는 이 경로의 완료 증거가 아니다.

---

## 2. 게임 규칙

### 2.1 기본 수치

| 항목 | 현재 값 | 원본 | 의미 |
|---|---:|---|---|
| 선딜 | 0.05초 | `CombatConfigSO.ParryStartupDuration` | 입력 후 실제 판정 시각까지의 지연 |
| 기본 window | 0.32초 | `CombatConfigSO.ParryWindowDuration` | 공격별 패링 입력 허용 구간의 기본 길이 |
| window 상한 | 0.48초 | `CombatConfigSO.ParryWindowDurationMax` | 특성·장비 보정 후 최종 상한 |
| 실패 후딜 | 0.35초 | `CombatConfigSO.ParryRecoveryDuration` | 유효하지만 실패한 시도 뒤 행동 잠금 |
| 스태미나 비용 | 15 | `CombatConfigSO.ParryStaminaCost` | 유효한 시도를 커밋할 때 차감 |
| 성공 무적 | 0.3초 | `CombatConfigSO.ParryInvincibleDuration` | 서버 현재 시각부터 부여 |
| 게이지 획득 | 35 | `CombatConfigSO.ParryGaugeGainPerSuccess` | 특수옵션 보정 전 기본값 |
| 게이지 최대 | 100 | `CombatConfigSO.ParryGaugeMax` | 초과분 이월 없음 |
| 치명 배율 | 2.0~3.0 | `CombatConfigSO.CriticalMultiplierMin/Max` | 서버에서 1회 추첨 |

`ParryWindowDuration`은 플레이어 오브젝트에서 0.32초 동안 콜라이더를 켜라는 뜻이 아니다. 서버가 공격 실행마다 등록하는 시간 구간의 기본 길이다. 특성·장비가 window를 늘리면 **같은 타격 종료 시각을 유지한 채 시작 시각을 앞당긴다.**

### 2.2 성공·실패·거부

세 결과를 구분한다.

| 분류 | 예 | 스태미나 | 행동 잠금 | window 소비 | 보상 |
|---|---|---:|---|---|---|
| 성공 | 정확한 window와 시각이 일치 | 차감 후 전량 회복 | 선딜 종료까지만 | 소비 | 무적·게이지·공격자 콜백 |
| 실패(Miss) | 허공 패링, 정확한 window에서 타이밍을 놓침 | 차감 유지 | 선딜 + 실패 후딜 | 정확한 window를 참조했다면 소비 | 없음 |
| 거부(Rejected) | 로비·사망·다른 무기·스태미나 부족·중복 request·낡은 window 키 | 변화 없음 | 서버 상태 변화 없음 | 소비 안 함 | 없음 |

클라이언트는 입력 즉시 모션을 예측 재생하므로 거부 결과를 받을 수 있다. 거부는 실패 패널티가 아니라 **서버가 시도 자체를 수락하지 않은 것**이며, 클라이언트는 모션을 짧게 블렌드 아웃한다.

### 2.3 패링 성공 보상

- `PlayerStatus`가 스태미나를 최대치로 복구한다.
- `PlayerStatus`가 서버 현재 시각부터 0.3초 동안 무적 만료 시각을 연장한다.
- `ParryGauge`가 서버에서 유효 획득량을 더하고 최대치로 clamp한다.
- 등록된 공격 실행 인스턴스의 `IParryable.OnParried(parriedByNetId)`를 정확히 한 번 호출한다.
- 패링 자체는 피해를 주지 않는다. 피해 배율은 게이지가 100일 때 다음 공격이 소비한다.

### 2.4 행동 잠금

패링은 카메라·조준 입력을 막지 않지만 아래 전투 행동과 이동을 잠근다.

| 서버 패링 상태 | 이동 | 공격 | 구르기 | 재패링 | 카메라 |
|---|---|---|---|---|---|
| `Idle` | 허용 | 허용 | 허용 | 허용 | 허용 |
| `Startup` | 거부 | 거부 | 거부 | 거부 | 허용 |
| `Recovery` | 거부 | 거부 | 거부 | 거부 | 허용 |

성공하면 판정 활성 시각까지 `Startup`을 유지한 뒤 `Idle`로 돌아간다. 실패하면 활성 시각 뒤 `Recovery`가 이어진다. 서버가 늦게 request를 받은 경우 플레이어가 반드시 후딜을 체감하도록 실패 후딜 시작은 `max(판정 활성 시각, 서버 수신 시각)`으로 잡는다.

`PlayerActionGate.CanAct`는 phase와 생명 상태의 공통 판정만 유지한다. 패링·구르기·공격 사이의 상호 배제는 각 Command가 같은 플레이어의 상태 소유자에게 `IsActionLocked(serverNow)`를 질의해 조합한다.

---

## 3. 시간 모델

### 3.1 서버 공격 타임라인

패링 가능 공격은 텔레그래프를 시작할 때 실제 타격 시각을 먼저 확정한다.

```text
telegraphStart
    │
    │  클라이언트에 텔레그래프와 window 키·시각 통지
    ▼
windowStart ───────────── windowEnd ─── 0.18초 ─── impactTime
       패링 활성 시각이 들어올 수 있는 구간       피해 확정 또는 공격 취소
```

계산식은 다음으로 고정한다.

```text
resolutionLead          = Constants.Network.PARRY_RESOLUTION_LEAD = 0.18초
effectiveWindowDuration = Clamp(공격 기본 window + 대상의 특성·장비 증가량,
                                0,
                                CombatConfigSO.ParryWindowDurationMax)
windowEnd               = impactTime - resolutionLead
windowStart             = windowEnd - effectiveWindowDuration
```

0.18초는 밸런스 값이 아니라 150ms 지연 조건과 60Hz 서버 틱 여유를 위한 네트워크 계약값이다. 실제 지연 테스트에서 조정할 때는 `Constants.Network`와 이 문서를 함께 바꾼다.

공격 데이터는 아래 조건을 만족해야 한다.

```text
TelegraphDuration >= ParryStartupDuration
                   + effectiveWindowDuration
                   + PARRY_RESOLUTION_LEAD
```

현재 기본·최대 수치 기준 최소 텔레그래프는 각각 0.55초, 0.71초다. 조건을 만족하지 못하는 공격은 패링 가능 패턴으로 등록하지 않고 데이터 검증 오류를 낸다.

### 3.2 입력 시각 보정과 판정 시각

클라이언트의 `NetworkTime.time`은 추정 서버 시각일 뿐이므로 그대로 믿지 않는다.

**이동 입력용 `MAX_INPUT_TIME_BEHIND`를 그대로 쓰지 않는다.** 현재 값은 0.5초인데 패링 window 전체가 `impactTime - 0.50 ~ impactTime - 0.18` 구간이므로, 0.5초를 허용하면 clamp 범위가 window를 통째로 덮어 아무것도 제한하지 못한다. 늦게 누른 입력을 window 한가운데의 시각으로 신고해도 그대로 통과한다. 패링은 전용 하한을 쓴다.

```text
serverReceivedAt = NetworkTime.time

// 해당 연결의 실측 지연으로 하한을 좁히고, 고정 상한으로 한 번 더 자른다
allowedBehind = Min(Constants.Network.PARRY_MAX_INPUT_TIME_BEHIND,
                    (connectionToClient.rtt * 0.5) + Constants.Network.PARRY_INPUT_TIME_TOLERANCE)

effectiveInputTime = Clamp(request.ClientEstimatedServerTime,
                           serverReceivedAt - allowedBehind,
                           serverReceivedAt + Constants.Network.MAX_INPUT_TIME_AHEAD)

parryActivationTime = effectiveInputTime + effectiveStartupDuration
success = windowStart <= parryActivationTime <= windowEnd
```

| 상수 | 값 | 의미 |
|---|---:|---|
| `PARRY_MAX_INPUT_TIME_BEHIND` | 0.25초 | rtt가 비정상적으로 크게 측정돼도 넘지 못하는 절대 상한 |
| `PARRY_INPUT_TIME_TOLERANCE` | 0.05초 | rtt 흔들림과 서버 틱 여유 |

`rtt`는 Mirror의 `NetworkConnectionToClient.rtt`를 쓴다. 값이 유효하지 않거나 0이면 tolerance만 적용한다. 두 상수는 밸런스 수치가 아니라 네트워크 계약값이므로 `Constants.Network`가 소유한다.

**남는 한계를 명시한다.** clamp는 정직한 입력과, 지연 범위 안에서 시각을 옮긴 입력을 구분하지 못한다. 이 규칙이 하는 일은 조작 가능 폭을 고정 0.5초에서 **해당 연결의 실제 지연 수준**으로 좁히는 것뿐이다. 완전한 방어는 클라이언트 시각을 아예 받지 않고 서버 수신 시각만 쓰는 것인데, 그러면 지연이 큰 플레이어가 패링을 성립시킬 수 없어 채택하지 않는다. 3인 지인 코옵이라는 현재 스코프에서 이 잔여 위험은 수용한다.

경계는 양끝 포함이다. `ParryJudge`에는 이미 보정된 `parryActivationTime`, `windowStart`, `windowEnd`만 넘긴다. `ParryJudge`는 Mirror, 무기, 스태미나, GameDB를 알지 않는다.

클라이언트가 보낸 시각이 `NaN`, `Infinity`이면 clamp하지 않고 request를 거부한다. window 길이·타격 시각도 유한수이고 `windowStart <= windowEnd < impactTime`인지 등록 시 검증한다.

### 3.3 피해 롤백을 하지 않는 규칙

서버는 `impactTime` 전에 도착한 request만 해당 공격에 적용한다. 입력 시각이 window 안이더라도 서버 도착 시각이 `impactTime` 이상이면 `RejectedResolutionClosed`다.

공격 측은 다음 순서를 지킨다.

1. 텔레그래프 시작 시 미래 window를 등록한다.
2. `impactTime` 전에는 피해를 커밋하지 않는다.
3. 패링 성공 콜백을 받으면 공격 실행을 취소하고 경직 상태로 전환한다.
4. `impactTime`에 아직 취소되지 않았으면 서버 권위 히트 판정과 `TakeDamage`를 실행한다.
5. 성공·실패·공격 취소와 무관하게 실행 종료 시 window를 닫는다.

이 구조에서는 이미 줄어든 HP를 되돌리는 롤백이 필요 없다. 150ms를 넘는 지연에서 마감 이후 도착한 입력은 실패가 아니라 거부로 기록하고 M3 지표에 포함한다.

---

## 4. 네트워크 데이터 계약

직렬화 enum은 아래 명시값을 유지하고 기존 값을 재배열하지 않는다.

### 4.1 window 키

공격자마다 `WindowId`가 증가하므로 ID 하나만으로는 서로 다른 공격자의 window가 충돌한다. 항상 복합 키를 쓴다.

```csharp
public struct ParryWindowKey
{
    public uint AttackerNetId;
    public uint WindowId;

    public bool IsValid => (AttackerNetId != 0u) && (WindowId != 0u);
}
```

키 일치는 두 필드가 모두 같은 경우다. 실제 타입에는 `IEquatable<ParryWindowKey>` 또는 같은 의미의 `Matches`를 구현해 호출부마다 비교식을 다시 쓰지 않는다. 위 타입은 Mirror Weaver가 기본 직렬화할 수 있도록 readonly 필드가 아닌 public 값 필드를 사용한다.

- 공격자는 실행을 시작할 때 `WindowId`를 1 증가시킨다. `0`은 “관찰한 window 없음” 예약값이다.
- 한 공격이 여러 플레이어를 노리면 세 대상 모두 같은 `(AttackerNetId, WindowId)`를 쓴다.
- 한 런에서 uint wrap은 현실적으로 발생하지 않지만 증가 결과가 0이면 1로 건너뛴다.

### 4.2 request

```csharp
public struct ParryRequest
{
    public uint           RequestId;
    public ParryWindowKey ObservedWindow;
    public double         ClientEstimatedServerTime;
}
```

- 소유 클라이언트가 authority를 얻을 때 로컬 request counter를 0으로 초기화하고 입력마다 증가시킨다.
- `RequestId == 0`은 유효하지 않다.
- `[Command]`는 기본 reliable 채널과 기본 authority 요구를 유지한다.
- 서버는 `_lastSeenRequestId` 이하인 request를 중복·역순으로 버린다. 정상 request든 거부 request든 ID를 한 번 관찰하면 다시 처리하지 않는다.

### 4.3 서버 전용 window 레코드

```csharp
private sealed class ActiveParryWindow
{
    public ParryWindowKey Key;
    public uint           TargetPlayerNetId;
    public double         StartTime;
    public double         EndTime;
    public double         ImpactTime;
    public bool           IsConsumed;
    public IParryable     Source;       // 서버 프로세스 안에서만 보유, 네트워크 직렬화 금지
}
```

활성 window 슬롯과 별개로 **직전에 소비된 window의 tombstone을 하나 더 보관한다.**

```csharp
private ParryWindowKey _consumedWindowKey;        // 소비된 직후의 키
private double         _consumedWindowImpactTime; // 이 시각이 지나면 tombstone을 버린다
```

활성 슬롯 하나만으로는 5.3의 window 교체와 5.4의 소비 식별이 충돌한다. 이전 window의 `ImpactTime` 전에 새 공격이 등록되면 활성 슬롯이 덮여 소비 기록이 사라지고, 옛 키로 온 중복 요청이 `RejectedWindowConsumed`가 아니라 `RejectedWindowMismatch`로 떨어진다. 서버 상태는 어느 쪽이든 안전하지만 14.2가 결과 코드로 검증하는 항목이라 tombstone을 활성 슬롯에서 분리한다.

tombstone은 **window 교체의 영향을 받지 않고** `_consumedWindowImpactTime`이 지난 뒤에만 버린다.

`AttackerNetId`만 저장해서 나중에 `NetworkServer.spawned`에서 임의의 `IParryable`을 다시 찾지 않는다. 보스가 여러 패턴을 가질 수 있으므로 **그 window를 연 공격 실행 인스턴스**를 `Source`로 함께 저장한다.

`Source`는 window가 살아 있는 동안 유효해야 한다. 공격 취소·상태 교체·despawn에서는 반드시 `CloseParryWindow(key)`를 호출한다. 공격 측이 plain C# 실행 객체라면 해당 객체의 수명을 window보다 짧게 만들지 않는다. `MonoBehaviour` 구현체는 interface 참조의 일반 `null` 비교만으로 파괴 여부를 놓칠 수 있으므로 `source is UnityEngine.Object unityObject && unityObject == null`인 경우도 무효로 판정한다.

### 4.4 결과 코드

```csharp
public enum ParryResultCode : byte
{
    Success                  = 0,
    MissNoWindow             = 1,
    MissTiming               = 2,
    RejectedInvalidRequest   = 10,
    RejectedDuplicate        = 11,
    RejectedState            = 12,
    RejectedWeapon           = 13,
    RejectedBusy             = 14,
    RejectedStamina          = 15,
    RejectedWindowMismatch   = 16,
    RejectedResolutionClosed = 17,
    RejectedWindowConsumed   = 18,
    RejectedDependency       = 19,
}
```

TargetRpc는 최소한 `(requestId, windowKey, resultCode, serverResolvedAt)`을 보낸다. bool 하나만 보내면 클라이언트가 후딜을 재생해야 하는 실패와 예측 모션을 취소해야 하는 거부를 구분할 수 없다.

### 4.5 파일 위치와 파트 경계

AI가 `Geumeum.Combat.ParryController`를 직접 참조하면 [`../04-architecture.md`](../04-architecture.md) 8.1의 `Combat ✕ AI 상호 직접 참조 금지`를 위반한다. 공용 대상 인터페이스를 추가하고 공격 측은 이것만 본다.

```csharp
// Assets/01.Scripts/00.Common/Interfaces/IParryWindowTarget.cs
namespace Geumeum.Common
{
    public interface IParryWindowTarget
    {
        bool OpenParryWindow(
            IParryable source,
            ParryWindowKey key,
            double telegraphStartTime,
            double impactTime);

        void CloseParryWindow(ParryWindowKey key);
    }
}
```

| 타입 | 위치 | namespace | 이유 |
|---|---|---|---|
| `ParryWindowKey` | `00.Common/Types/ParryWindowKey.cs` | `Geumeum.Common` | Combat과 AI가 함께 사용하는 값 타입 |
| `IParryWindowTarget` | `00.Common/Interfaces/IParryWindowTarget.cs` | `Geumeum.Common` | AI → Combat 경계 |
| `ParryRequest` | `02.Combat/Parry/ParryRequest.cs` | `Geumeum.Combat` | 소유 클라와 자기 서버 컴포넌트 사이의 내부 요청 |
| `ParryResultCode` | `02.Combat/Parry/ParryResultCode.cs` | `Geumeum.Combat` | 패링 결과 표현 계약 |

`ParryController`가 `IParryWindowTarget`을 구현하고 구현 메서드에 `[Server]`를 붙인다. `IParryWindowTarget` 자체는 Mirror를 참조하지 않는다. AI는 target에서 인터페이스를 한 번 찾아 캐시하며 `ParryController` 타입이나 `Geumeum.Combat` namespace를 import하지 않는다.

---

## 5. `ParryController` 계약

### 5.1 캐시와 초기화

`Awake`에서 같은 플레이어의 아래 컴포넌트를 한 번 캐시한다.

- `PlayerStatus`
- `PlayerWeapon`
- `PlayerController`
- 구현 후 `PlayerAttack`
- `ParryGauge`

GameDB 값은 `Awake`에서 읽지 않는다. `OnStartServer` 이후 또는 request 처리 시 `GameDB.IsInitialized`를 확인한 뒤 읽는다. 필수 컴포넌트가 없으면 유효 request를 부분 처리하지 않고 `RejectedDependency`로 거부하며 오류를 남긴다.

`OnStartAuthority`에서는 액션을 구독·활성화하고, `OnStopAuthority`에서는 반드시 구독 해제와 `Disable()`을 둘 다 실행한다.

### 5.2 클라이언트 입력

소유 클라이언트는 다음 순서를 따른다.

1. 로컬 request ID를 증가시킨다.
2. 현재 마지막으로 통지받은 window 키를 snapshot한다. 없으면 `default`를 쓴다.
3. 패링 입력 모션을 즉시 재생한다.
4. `CmdRequestParry(request)`를 보낸다.
5. TargetRpc가 오면 같은 request ID의 예측 상태에만 결과를 적용한다. 더 오래된 결과가 늦게 와도 최신 모션을 되감지 않는다.

window 열림·닫힘 통지는 소유자 TargetRpc로 보낸다.

```text
TargetOpenParryWindow(key, startTime, endTime, impactTime)
TargetCloseParryWindow(key)
```

텔레그래프의 월드 표현은 공격 측 ClientRpc가 담당한다. TargetRpc의 window 데이터는 입력 식별과 선택적 HUD에만 쓰며, 클라이언트 판정 원본이 아니다.

### 5.3 서버 window API

```text
[Server] bool OpenParryWindow(
    IParryable source,
    ParryWindowKey key,
    double telegraphStartTime,
    double impactTime)

[Server] void CloseParryWindow(ParryWindowKey key)
```

등록 조건은 다음과 같다.

- `source != null`
- key의 두 ID가 0이 아님
- `TargetPlayerNetId == this.netId`는 현재 컨트롤러 소유로 자동 확정
- 모든 시각이 유한수
- 대상의 phase·생명 상태가 허용되고 현재 무기가 Sword임. 현재 구르기·공격 잠금과 스태미나 잔액은 미래 입력 가능성을 막지 않으므로 등록 조건에 넣지 않음
- `telegraphStartTime < impactTime`
- source duration과 대상의 window bonus가 유효함
- 계산된 `windowStart >= telegraphStartTime`

`ParryController`가 `IParryWindowTarget`의 구현체로서 source duration, 대상의 현재 장비·특성 bonus, `PARRY_RESOLUTION_LEAD`로 `windowStart`와 `windowEnd`를 계산한다. 공격 측이 같은 계산을 복제하거나 대상의 특성 데이터를 직접 읽지 않는다. window bonus는 등록 순간 snapshot하고 해당 공격 실행 중 다시 계산하지 않는다.

새 window 등록에 성공하면 이전 **활성 레코드**는 소비 여부와 무관하게 무효화하고 이전 키의 close 통지를 보낸 뒤 새 open 통지를 보낸다. 같은 서버 틱에 여러 공격이 등록되면 실제 서버 호출 순서상 마지막 등록이 이긴다. 디버그 로그에는 교체된 키와 새 키를 모두 남긴다.

교체는 4.3의 소비 tombstone을 지우지 않는다. 무효화되는 활성 레코드가 이미 소비된 것이었다면 tombstone은 그대로 두고 활성 슬롯만 새 window로 덮는다.

`CloseParryWindow`는 현재 키와 정확히 일치할 때만 닫는다. 늦게 끝난 이전 공격이 새 window를 닫아서는 안 된다.

### 5.4 Command 처리 순서

순서를 바꾸지 않는다. 특히 자원 차감 전에 모든 거부 조건을 끝내고, 성공 보상 전에 window를 먼저 소비한다.

```text
CmdRequestParry(request):
  1. request ID·시각 유한성 검사
  2. duplicate/역순 검사 후 _lastSeenRequestId 갱신
  3. 필수 컴포넌트·GameDB 준비 확인
  4. RunPhase + PlayerLifeState를 PlayerActionGate.CanAct로 확인
  5. PlayerWeapon.HasWeapon && Weapon == Sword 확인
  6. 현재 패링·구르기·공격 행동 잠금 확인
  7. 소비 tombstone과 request.ObservedWindow 키 비교
     - 키가 같음: RejectedWindowConsumed (활성 슬롯이 이미 교체됐어도 동일)
  8. 서버 활성 window와 request.ObservedWindow 일치 여부 확인
     - 둘 다 없음: 허공 패링 후보로 계속
     - 하나만 있거나 키가 다름: RejectedWindowMismatch
     - 정확한 key지만 IsConsumed: RejectedWindowConsumed
     - source 참조가 유효하지 않음: RejectedDependency
  9. 서버 수신 시각이 활성 window의 ImpactTime 이전인지 확인
 10. 서버 loadout/특성 snapshot으로 유효 패링 수치 계산
 11. 입력 시각 clamp → parryActivationTime 계산
 12. PlayerStatus.TrySpendStamina(effectiveCost)
 13. window 없음: MissNoWindow 커밋
 14. window 있음: IsConsumed = true, tombstone에 키와 ImpactTime 기록
 15. ParryJudge로 시간 판정
 16. 성공: 플레이어 보상 커밋 → Source.OnParried(netId)
 17. 실패: Recovery 종료 시각 커밋
 18. request ID가 포함된 TargetRpc 결과 전송
```

12번까지 통과한 시도만 스태미나와 행동 상태를 바꾼다. 14번 이후 예외로 반쪽 상태가 남지 않도록, window의 source와 플레이어 보상 API가 유효한지는 3번과 8번에서 미리 검증한다.

정확한 window를 참조했지만 타이밍이 틀렸다면 `MissTiming`이고 window는 소비된다. 허공에서 양쪽 키가 모두 없으면 `MissNoWindow`이며 소비할 window는 없다.

window를 소비하면 성공·실패 모두 소유 클라이언트에 `TargetCloseParryWindow(key)`를 즉시 보내 입력 snapshot에서 제거한다. 서버는 같은 key의 중복 요청을 `RejectedWindowConsumed`로 식별할 수 있도록 4.3의 tombstone을 그 window의 `ImpactTime`까지 유지한다. tombstone은 활성 슬롯과 별개이므로 그 사이 다른 공격이 새 window를 등록해도 남아 있다. 공격 측의 이후 `CloseParryWindow(key)`는 안전한 멱등 호출이다. 중복 request ID에는 원래 request의 결과가 이미 전송되므로 두 번째 TargetRpc를 보내지 않는다.

**거부 요청에도 최소 간격을 둔다.** 거부는 자원과 행동 잠금을 바꾸지 않으므로 클라이언트가 비용 없이 `[Command]`를 연타할 수 있다. 서버는 request ID를 증가시켜 보낸 요청이라도 직전 처리 시각과의 간격이 `Constants.Network.PARRY_MIN_REQUEST_INTERVAL`(0.05초) 미만이면 `RejectedInvalidRequest`로 버리고 로그도 남기지 않는다. 정상 입력은 선딜·후딜 때문에 이 간격에 걸리지 않는다.

### 5.5 서버 행동 시각

서버는 매 프레임 상태를 전환할 필요가 없다. 저장한 시각으로 상태를 파생한다.

```text
success:
  startupEnd = max(parryActivationTime, serverReceivedAt)
  actionLockEnd = startupEnd

miss:
  startupEnd = max(parryActivationTime, serverReceivedAt)
  recoveryEnd = startupEnd + effectiveRecoveryDuration
  actionLockEnd = recoveryEnd
```

`IsActionLocked(serverNow)`는 `serverNow < actionLockEnd`만 검사한다. 애니메이션 표시용 상태가 필요하면 `startupEnd`와 마지막 결과를 이용해 `Startup`/`Recovery`를 파생하고, 게임 결과를 바꾸는 별도 클라이언트 타이머를 만들지 않는다.

### 5.6 월드 관찰자 모션

소유자는 입력 즉시 모션을 재생한다. 서버가 request를 수락한 경우에만 `ClientRpc` 또는 프로젝트의 전투 애니메이션 동기화 경로로 다른 관찰자에게 패링 모션을 보낸다. 소유자는 같은 request ID의 관찰자 RPC를 무시해 모션을 두 번 재생하지 않는다.

---

## 6. `ParryJudge` 계약

순수 함수의 책임은 시간 구간 비교 하나다.

```csharp
public static bool IsSuccess(
    double parryActivationTime,
    double windowStartTime,
    double windowEndTime)
{
    return (parryActivationTime >= windowStartTime)
        && (parryActivationTime <= windowEndTime);
}
```

잘못된 수치, window 키, 소비 상태를 Judge 안에서 처리하지 않는다. 호출부가 검증한 값만 전달한다. duration 대신 end time을 받으면 호출부와 Judge가 서로 다른 방식으로 종료 시각을 계산하는 일을 막을 수 있다.

필수 단위 테스트 경계는 다음과 같다.

- activation == start → 성공
- activation == end → 성공
- start보다 `1e-6`초 이전 → 실패
- end보다 `1e-6`초 이후 → 실패
- 선딜을 더하기 전 입력은 창 밖이지만 더한 뒤 창 안 → 성공
- clamp 전 시각은 창 안이지만 clamp 후 창 밖 → 실패

---

## 7. 공격 측 통합 계약

### 7.1 `IParryable`

기존 인터페이스를 유지한다.

```csharp
public interface IParryable
{
    float ParryWindowDuration { get; }
    void OnParried(uint parriedByNetId);
}
```

구현체는 몬스터나 보스 전체가 아니라 가능하면 **한 번의 공격 실행 상태**를 대표한다. 재사용 가능한 보스 객체 하나가 여러 동시 패턴을 대신하면 `OnParried`가 어느 실행을 취소해야 하는지 모호해진다.

`OnParried`의 서버 계약은 다음과 같다.

- 같은 window에 대해 여러 번 호출되어도 공격 측 상태가 중복 전이되지 않도록 자체 방어한다.
- 현재 이동·히트 판정을 취소한다.
- 해당 실행의 피해를 이후에 적용하지 않는다.
- 경직 상태와 경직 종료 시각을 서버 상태로 커밋한다.
- 경직 애니메이션·이펙트는 서버 상태 결과를 각 클라이언트가 표현한다.
- 메서드 안에서 플레이어 보상이나 패링 게이지를 다시 처리하지 않는다.

### 7.2 창귀 기준 흐름

**선행 조건 두 가지를 먼저 해결해야 한다. 둘 다 `03.AI/`와 `09.Data/`라 몬스터 파트(@oname36-creator)와의 조율이 필요하다.**

**(1) 창귀가 코드에도 데이터에도 없다.** `EnemySO.asset`의 창귀 항목은 `AggroRange`·`AttackRange`·`WindupDuration`·`AttackCooldown`이 전부 0이고, Brain은 `HeokkaebiBrain` 하나뿐이라 돌진 패턴 구현이 없다. 13장 5단계에 착수하려면 창귀 수치 입력과 돌진 Brain이 먼저 있어야 한다. 3.1의 최소 텔레그래프 조건도 이 값이 채워져야 검증할 수 있다.

**(2) 공격 타이머를 절대 서버 시각으로 바꿔야 한다.** 현재 `EnemyController`는 Brain이 `ShouldAttackNow`를 올린 틱에 곧바로 `TakeDamage`를 호출하고, Brain은 `Time.deltaTime`을 누적해 `WindupDuration`을 넘는지 본다. 이 구조에서는 텔레그래프 시작에 등록한 `impactTime`과 실제 타격 틱이 프레임 오버슈트만큼 어긋난다. 실제 타격이 등록된 `impactTime`보다 늦어지면, 아직 피해가 들어가지 않았는데도 5.4의 9번이 요청을 `RejectedResolutionClosed`로 거부한다.

패링 가능 공격은 deltaTime 누적 대신 **텔레그래프 시작 때 확정한 `impactTime`을 `NetworkTime.time`과 직접 비교**해 타격 시점을 판정한다. 그래야 3.3의 "롤백 불필요" 구조가 성립한다. 패링 불가 공격은 기존 방식을 유지해도 된다.

```text
Idle/Chase
  → ChargeTelegraph 진입
      - target을 이번 실행 동안 고정
      - window ID 발급
      - impactTime 확정
      - 대상 IParryWindowTarget.OpenParryWindow(source, key, telegraphStart, impactTime)
      - 같은 서버 틱에 텔레그래프 ClientRpc
  → ChargeMove
  → impactTime
      - parried면 피해 없음
      - 아니면 서버 히트 판정 후 TakeDamage
      - CloseParryWindow(key)
  → Recovery

OnParried
  → Charge 이동·히트 예약 취소
  → CloseParryWindow(key)
  → Stagger
```

`target`에서 `IParryWindowTarget`을 찾는 작업은 텔레그래프 시작 때 한 번만 하고 실행 상태에 캐시한다. Tick이나 이동 루프에서 반복 탐색하지 않는다.

타겟이 죽거나 despawn하거나 공격 범위를 벗어나 공격 자체가 취소되면 window도 즉시 닫는다. 타겟 교체 시 기존 window를 새 타겟으로 옮기지 않고 기존 실행을 취소한 뒤 새 공격 실행을 만든다.

### 7.3 보스 패턴

- `BossPatternSO.PatternEntry.IsParryable == true`인 패턴만 window를 등록한다.
- `ActiveDuration`은 피해 판정 지속시간이고 `ParryWindowDuration`과 다른 개념이다. 패턴 데이터에 `ParryWindowDuration`을 별도 추가한다.
- 패턴 텔레그래프 이벤트와 대상별 window 등록은 같은 서버 메서드·같은 서버 틱에서 실행한다.
- 광역 패턴은 대상별 `IParryWindowTarget`에 같은 공격 key와 impact time으로 등록을 요청한다. 현재 직업 규칙에서는 Sword 플레이어만 등록에 성공하고 Bow·Bell 대상은 정상적으로 window 없이 공격을 받는다.
- 향후 둘 이상의 대상이 패링 자격을 갖게 되면 각 대상의 특성·장비에 따라 window start는 달라질 수 있지만 end와 impact time은 같다.
- 대상 한 명의 성공이 패턴 전체를 취소하는지 그 대상의 피해만 무효화하는지는 패턴 데이터가 아니라 패턴 클래스의 명시적 규칙으로 정한다. 기본값은 **패턴 전체 취소**다.

### 7.4 여러 공격이 같은 대상을 노릴 때

대상당 활성 window는 하나다. 새 window가 이전 것을 대체하면 먼저 등록된 공격은 패링 대상에서 제외되고 정상 진행한다. 이 규칙은 가로채기·전방위 방어를 만들지 않기 위한 현재 스코프 결정이다.

동일 서버 틱에 여러 공격이 등록돼도 서버 호출 순서상 마지막 window만 활성이다. 이 상황은 디버그 로그와 테스트에서 어떤 공격이 선택됐는지 확인한다. 플레이 테스트에서 불공정성이 확인되기 전에는 다중 window 컬렉션으로 확장하지 않는다.

---

## 8. 패링 게이지와 치명의 일격

### 8.1 `ParryGauge` API

```text
ParryGauge : NetworkBehaviour
  [SyncVar] float Value

  [Server] bool AddSuccess(float amount)
  [Server] bool TryConsumeCritical(out float multiplier)
  [Server] void ResetForWeaponChange()
```

- `Value`는 `[0, GaugeMax]`로 clamp한다.
- `35 × 3 = 105`의 초과 5는 버린다. 치명 charge를 여러 개 쌓지 않는다.
- 이미 100인 상태에서 추가 성공해도 100을 유지한다.
- `AddSuccess`는 값이 처음 최대에 도달한 경우에만 `true`를 반환해 UI 알림 중복을 막는다.
- `amount`와 배율 범위는 서버 데이터에서 계산하고 `NaN`, `Infinity`, 음수는 거부한다.
- HUD가 본인 게이지만 표시하는 현재 범위에서는 NetworkBehaviour sync mode를 `Owner`로 둔다. 파티 UI가 확정되면 `Observers` 전환을 별도 결정한다.

### 8.2 치명 소비 시점

`PlayerAttack`의 서버 처리에서 아래 순서를 지킨다.

1. request ID, 상태, 무기, 쿨다운, 자원, 조준 요청을 모두 검증한다.
2. 자원과 공격 쿨다운을 한 번 커밋한다.
3. 검 공격이면 `TryConsumeCritical(out multiplier)`를 한 번 호출한다.
4. 반환된 multiplier를 그 공격 실행의 `AbilityRequest` 또는 서버 실행 context에 복사한다.
5. 다단히트 전체가 같은 multiplier를 사용한다.

유효 공격 행동이 시작된 뒤 빗나가거나 대상이 사라져도 게이지는 복구하지 않는다. 거부된 공격 request에는 게이지를 소비하지 않는다.

배율 추첨은 서버 전용이다. 테스트에서 난수를 통제할 수 있도록 `ParryGauge` 내부에 직접 여러 번 `Random.Range`를 흩뿌리지 말고 한 메서드 또는 주입 가능한 난수 함수 한 곳으로 제한한다.

### 8.3 무기 변경과 사망

**선행 조건: 서버 무기 교체 경로가 아직 없다.** [`../02-game-design.md`](../02-game-design.md) 2장은 런 도중 무기 교체로 직업이 바뀐다고 규정하지만, 현재 `PlayerWeapon`에는 `ServerSetInitialWeapon` 하나뿐이고 **이미 무기가 확정된 뒤의 재호출을 명시적으로 거부한다.** 즉 `ResetForWeaponChange()`를 붙일 커밋 지점이 존재하지 않는다.

이 절의 규칙은 서버 무기 교체가 구현된 시점에 함께 적용한다. 그때까지 `ResetForWeaponChange()`는 API로 존재하되 호출부가 없는 상태를 허용하며, 이를 이유로 게이지 구현을 미루지 않는다. 무기 교체 자체는 이 문서의 범위가 아니다.

- 사망·관전·부활에서는 게이지를 유지한다.
- 무기 변경을 서버가 커밋한 직후 `ResetForWeaponChange()`를 호출한다.
- `PlayerWeapon`의 SyncVar hook은 표현용 클라이언트에서도 실행되므로 게이지 초기화를 hook에 넣지 않는다.
- 같은 검에서 다른 검으로 장비만 교체해도 기획상 “무기 교체”이므로 0으로 초기화한다.
- 초기 무기 배분 시 기본값 0을 다시 설정해도 문제없지만 별도 알림은 보내지 않는다.

---

## 9. `PlayerStatus` 선행 변경

### 9.1 스태미나 API

패링은 `PlayerStatus`의 서버 API만 사용한다.

```text
[Server] bool TrySpendStamina(float amount)
[Server] void RestoreStaminaToMax()
```

`TrySpendStamina`는 `amount`가 유한수이고 0 이상인지 먼저 확인한다. 음수를 받아 스태미나를 늘리는 경로를 허용하지 않는다. 성공 보상은 직접 `_stamina`에 대입하지 않고 `RestoreStaminaToMax()`를 호출한다.

### 9.2 무적 API

현재 `SetInvincible(bool)`는 구르기 타이머가 패링 무적을, 패링 타이머가 구르기 무적을 먼저 꺼버릴 수 있다. 아래 만료 시각 모델로 교체한다.

```text
[SyncVar] double TimedInvincibleUntil

IsInvincible = LifeState == ReviveInvulnerable
             || NetworkTime.time < TimedInvincibleUntil

[Server] GrantTimedInvincibility(float duration):
    TimedInvincibleUntil = max(TimedInvincibleUntil, NetworkTime.time + duration)
```

- 구르기와 패링은 `false`를 대입해 무적을 해제하지 않는다.
- 더 긴 만료 시각만 남으므로 중첩 순서와 무관하다.
- 사망·새 런 초기화처럼 전체 일시 상태를 버리는 명시적 생명주기에서만 `ClearTimedInvincibility()`를 호출한다.
- `TakeDamage`는 서버의 `IsInvincible`을 확인한다. SyncVar는 클라이언트 표현용이며 판정 원본이 아니다.

이 변경은 패링만의 편의 리팩터링이 아니라 현재 구르기와 패링이 같은 상태를 공유하기 위한 선행 권위 수정이다.

---

## 10. 밸런스 데이터 계약

### 10.1 플레이어 유효 수치 snapshot

서버는 window 등록과 request 처리 시 현재 장비·특성으로 다음 snapshot을 만든다.

```text
ParryRuntimeStats
  StartupDuration
  WindowBonus
  RecoveryDuration
  StaminaCost
  InvincibleDuration
  GaugeGain
  CriticalMultiplierMin/Max
```

프로토타입에서 장비·특성 시스템이 아직 없으면 bonus는 0이고 `CombatConfigSO` 기본값을 그대로 쓴다. `WindowBonus`는 window 등록 순간, 나머지 시도 수치는 request 수락 순간 snapshot한다. 이후 시스템을 연결할 때도 클라이언트가 계산한 비용·window·게이지량을 payload로 보내지 않는다.

보정 후 검증 규칙:

- window: `0 < source duration + bonus <= ParryWindowDurationMax`
- startup/recovery/invincible: 유한수, 0 이상
- stamina cost/gauge gain: 유한수, 0 이상
- critical min/max: `1 <= min <= max`

### 10.2 공격 데이터 추가

현재 데이터 클래스에는 공격별 패링 window가 충분히 분리되어 있지 않다.

| 데이터 | 추가/사용 필드 | 규칙 |
|---|---|---|
| `EnemySO.StatEntry` 또는 추후 공격 패턴 entry | `ParryWindowDuration` | 창귀는 기본 0.32, 비패링 잡귀는 0 |
| `BossPatternSO.PatternEntry` | `ParryWindowDuration` | `IsParryable`일 때만 0보다 큼 |
| `BossPatternSO.PatternEntry.TelegraphDuration` | 기존 | 3.1의 최소 텔레그래프 조건 충족 |
| `BossPatternSO.PatternEntry.ActiveDuration` | 기존 | 피해 판정 지속시간, 패링 window로 재사용 금지 |

`IParryable.ParryWindowDuration`은 해당 공격 데이터의 값을 반환한다. 몬스터별 수치를 `CombatConfigSO`에 넣지 않는다.

Editor 검증 또는 GameDB 초기화 검증에서 아래를 오류로 처리한다.

- `IsParryable == false`인데 window가 0보다 큼
- `IsParryable == true`인데 window가 0 이하
- 텔레그래프가 startup + 최대 window + resolution lead보다 짧음
- critical min이 max보다 큼

---

## 11. 피드백·UI·로그

### 11.1 클라이언트 피드백

| 시점 | 대상 | 처리 |
|---|---|---|
| 입력 즉시 | 소유자 | Startup 모션 예측 재생 |
| 서버 수락 | 다른 관찰자 | 패링 모션 재생 |
| `Success` | 소유자 | `PlayParrySuccess()`, 성공 사운드·이펙트 |
| `Miss*` | 소유자 | Recovery 모션, 별도 실패 이펙트는 현재 없음 |
| `Rejected*` | 소유자 | 예측 모션 블렌드 아웃, 자원 부족 등 필요한 UI만 표시 |
| 게이지 최대 최초 도달 | 소유자 | HUD 준비 상태 표시 |
| 치명 공격 결과 | 해당 결과를 보는 클라이언트 | `PlayCritical(multiplier)` |
| 공격자 경직 | 모든 관찰자 | 공격 측 네트워크 상태를 로컬 애니메이션으로 표현 |

프로토타입 동안 실제 히트스톱·슬로우모션·전용 카메라 워크는 만들지 않는다. 호출 훅만 연결하고 `NullCombatFeedback`을 허용한다.

### 11.2 진단 로그

Development Build 또는 개발 플래그에서 한 request당 한 줄로 다음을 남길 수 있어야 한다.

```text
playerNetId, requestId, observedKey, activeKey,
clientEstimatedTime, effectiveInputTime, activationTime,
windowStart, windowEnd, impactTime, serverReceivedAt,
resultCode, staminaBefore/After, gaugeBefore/After
```

릴리스 기본 로그에는 성공·실패마다 `Debug.Log`를 남기지 않는다. 반복 전투 로그가 호스트 성능과 콘솔 가독성을 해친다. 오류는 잘못된 데이터·필수 의존 누락·source 수명 위반처럼 개발자가 고쳐야 하는 경우에만 남긴다.

---

## 12. 경계 상황 규칙

| 상황 | 서버 처리 |
|---|---|
| 로비·Result·Downed·Spectating | 거부, 자원·window 변화 없음 |
| `ReviveInvulnerable` | `PlayerActionGate` 현재 계약대로 패링 허용 |
| Sword가 아니거나 아직 무기 미배분 | 거부 |
| 스태미나 부족 | 거부 |
| 패링·공격·구르기 행동 잠금 중 | 거부 |
| 양쪽 모두 window 없음 | 유효한 허공 패링: 비용과 실패 후딜 적용 |
| 클라 key와 서버 key 불일치 | 네트워크/순서 거부, 현재 window 소비 안 함 |
| 정확한 key지만 시각이 창 밖 | 유효 실패, 비용·후딜·window 소비 |
| 정확한 key지만 `impactTime` 이후 도착 | 판정 마감 거부, 피해 롤백 없음 |
| 같은 request 재전송 | 무시, 보상·비용·결과 중복 없음 |
| 같은 window로 다른 request | 첫 유효 시도가 소비했으면 이후 `RejectedWindowConsumed` |
| 소비된 window가 새 공격에 교체된 뒤 옛 key 재요청 | tombstone이 남아 있어 `RejectedWindowConsumed`, `RejectedWindowMismatch`가 아니다 |
| 새 공격이 window 대체 | 이전 key request 거부, 새 window 유지, tombstone은 보존 |
| 거부 request 연타 | `PARRY_MIN_REQUEST_INTERVAL` 미만 간격은 조용히 폐기 |
| 클라가 시각을 과거로 신고 | 연결 rtt 기반 하한으로 clamp, 지연 범위 안의 이동은 구분 불가(3.2의 수용된 잔여 위험) |
| 공격자 despawn·공격 취소 | 공격 측이 close, 이후 request는 mismatch/없음 처리 |
| 대상 사망 중 window 열림 | 등록 거부 또는 즉시 close |
| 성공 직후 공격자가 다른 원인으로 사망 | 성공 보상 유지, 중복 경직·피해 없음 |
| 3인 광역 패턴 | Sword 대상만 window 등록, Bow·Bell은 패링 불가. 공격 key와 impact time은 공통 |
| 게이지 100에서 추가 성공 | 100 유지, charge 추가 없음 |
| 게이지 100 공격이 빗나감 | 게이지 소비 유지 |
| 무기 교체 | 서버 커밋 직후 게이지 0, 활성 window·패링 행동 취소 |
| 연결 해제·authority 종료 | 로컬 입력 해제, 서버 window/행동 상태는 오브젝트 수명과 함께 정리 |

---

## 13. 구현 순서

각 단계는 앞 단계 API를 기준으로 진행한다.

1. **순수 계약**
   - `ParryWindowKey`, `IParryWindowTarget`, `ParryRequest`, `ParryResultCode`
   - `ParryJudge` end-time 시그니처와 M0 경계 테스트
   - `Constants.Network`에 `PARRY_RESOLUTION_LEAD`, `PARRY_MAX_INPUT_TIME_BEHIND`, `PARRY_INPUT_TIME_TOLERANCE`, `PARRY_MIN_REQUEST_INTERVAL` 추가
2. **공유 상태 선행 수정**
   - `PlayerStatus.RestoreStaminaToMax`
   - 만료 시각 기반 무적
   - 구르기가 새 무적 API를 사용하도록 연결
3. **게이지**
   - `ParryGauge` 서버 API·owner SyncVar
   - `Player.prefab` 컴포넌트 추가
   - 서버 무기 변경 경로의 명시적 reset
4. **패링 컨트롤러**
   - request ID, window 등록·교체·닫기
   - 서버 검증 순서, 결과 코드, 로컬 예측·관찰자 모션
5. **창귀 수직 슬라이스** — 몬스터 파트(@oname36-creator) 조율 필요
   - 선행: `EnemySO`의 창귀 수치 입력, 창귀 돌진 Brain 신설
   - 선행: 패링 가능 공격의 타이머를 절대 서버 시각 기준으로 전환 (7.2)
   - 공격 데이터, 텔레그래프, future window, impact commit, `OnParried` 경직
6. **공격·치명 연동**
   - `PlayerAttack`의 한 공격당 1회 배율 snapshot
7. **보스 확장** — 보스 파트 조율 필요
   - 선행: `BossPatternSO.asset`이 현재 완전히 비어 있어 패턴 데이터부터 필요
   - `BossPatternSO.ParryWindowDuration`
   - 단일 대상과 광역 패턴 규칙 적용
8. **M2/M3 검증과 튜닝**
   - 0/30~50/150ms, 0/0.1/2% 손실 조건 기록

창귀 수직 슬라이스가 끝나기 전 보스 패턴부터 만들지 않는다. 인터페이스만 연결된 채 실제 피해 시점과 취소가 검증되지 않으면 같은 결함을 보스 코드에 복제하게 된다.

---

## 14. 검증 계획과 합격 기준

패링은 권위 경계를 바꾸므로 코드 구현 시 L3 전체 검증이 필요하고 지연 보상은 M3까지 확인한다. 문서 변경 자체는 L1 정적 검토 대상이다.

### 14.1 M0 — 순수 로직·경계

| 항목 | 반복/케이스 | 합격 기준 |
|---|---:|---|
| Judge 양끝·바깥 경계 | 6케이스 이상 | 전부 기대값 일치 |
| NaN/Infinity request | 각 필드별 | 전부 거부, 상태 변화 0 |
| request ID 중복·역순 | 100회 | 비용·보상 각 최대 1회 |
| window 교체·old close | 20회 | 새 window가 닫히는 경우 0 |
| 소비 후 교체된 옛 key 재요청 | 20회 | 전부 `RejectedWindowConsumed` |
| 과거로 신고한 입력 시각 clamp | rtt 0/50/150ms | clamp 하한이 rtt/2 + tolerance를 넘지 않음 |
| 게이지 35×3 | 10세트 | 매번 100, 초과 이월 0 |
| 다단히트 치명 소비 | 20공격 | 공격당 추첨·소비 각 1회 |

### 14.2 M2 — 3인 기능·권위

| 항목 | 합격 기준 |
|---|---|
| 호스트·클라 2명의 같은 공격 결과 | 20/20 권위 결과 일치 |
| 로비·사망·타 무기·무자원 요청 | 각 10회, 서버 상태 변화 0 |
| 같은 window 중복 request | 100회, 공격자 콜백·게이지·무적 각 1회 이하 |
| window 대체 직후 이전 close | 20회, 현재 window 유지 20/20 |
| 공격 취소·despawn·타겟 사망 | 각 10회, stale callback·피해 0 |
| 구르기/패링 연속 입력 | 양 순서 각 20회, 무적 조기 해제 0 |
| 3인 광역 window | 20패턴, Sword에만 open 20/20, Bow·Bell 패링 보상 0 |

### 14.3 M3 — 지연·손실

기존 프로토콜대로 세 조건을 확인한다.

| 조건 | 지연 | 손실 | 각 역할 시도 |
|---|---:|---:|---:|
| 이상적 | 0ms | 0% | 20회 이상 |
| 국내 일반 | 30~50ms | 0.1% | 20회 이상 |
| 나쁨 | 150ms | 2% | 20회 이상 |

합격 기준:

- 서버 권위 성공/실패 결과가 세 클라이언트에서 다른 경우 0회
- 같은 window의 이중 보상·이중 콜백 0회
- 입력 시각은 window 안이지만 `impactTime` 뒤 도착해 거부된 비율을 별도 기록
- 동일한 사전 녹화 cue에 대한 호스트와 원격 클라이언트 성공률 차이 2/20 이하를 초기 목표로 사용
- 차이가 2/20을 넘으면 window 수치보다 먼저 window 통지 시점, `PARRY_RESOLUTION_LEAD`, 호스트 프레임·틱 지연을 조사

실제 플레이어 숙련도 차이를 네트워크 결함으로 오판하지 않도록 가능한 한 같은 입력 스크립트·동일 cue 또는 한 사람이 역할을 바꿔 반복한다.

### 14.4 완료 보고 형식

```markdown
| 주장 | 수준 | 실행 수단 | 실제 결과 | 상태 |
|---|---|---|---|---|
| 동일 window 중복 보상 방지 | L3 | M2, 100회 재전송 | 100/100 중복 0 | 완료(검증됨) |
| 150ms 판정 마감 거부율 | L3 | M3, 20회 | 1/20 | 완료(관찰값 기록) |
| 호스트/클라 성공률 차이 | L3 | 동일 cue 각 20회 | 18/20, 17/20 | 완료(목표 이내) |
```

---

## 15. 스코프 경계와 남은 비차단 결정

이번 설계에 포함하지 않는다.

- 저스트 패링·완벽 패링의 2단 판정
- 동료 대신 막는 가로채기 패링
- 여러 공격 window를 동시에 보관하는 전방위 패링
- 패링 성공 즉시 카운터 피해
- 3인 동시 성공 파티 버프
- 프로토타입 단계의 히트스톱·슬로우모션·전용 카메라 연출

아래는 구현을 막지 않는 콘텐츠 결정이다.

| 항목 | 기본 구현 규칙 | 나중에 바뀌는 범위 |
|---|---|---|
| 보스 광역 패턴 구체 형태 | 패턴 전체 취소 | 해당 패턴 클래스 |
| 실패 전용 이펙트·사운드 | 없음, Recovery 모션만 | `ICombatFeedback`와 아트 구현 |
| 파티원의 게이지 표시 | 본인만 | `ParryGauge` sync mode와 UI |
| 창귀 경직 시간 | `EnemySO`에서 몬스터 파트가 결정 | 데이터 값만 |

이 표의 항목을 이유로 `ParryController`, 창귀 수직 슬라이스, `ParryGauge`, 치명 소비 구현을 멈추지 않는다.

---

## 16. 구현 완료 체크리스트

- [ ] request ID와 `(AttackerNetId, WindowId)`를 모두 검증한다.
- [ ] 입력 시각은 패링 전용 rtt 기반 하한으로 clamp한 뒤 startup을 더해 판정한다. 이동 입력용 `MAX_INPUT_TIME_BEHIND`를 쓰지 않는다.
- [ ] 소비 tombstone이 활성 window 슬롯과 분리되어 교체에도 살아남는다.
- [ ] 거부 request에 최소 간격 제한이 있다.
- [ ] 패링 가능 공격의 타격 시점이 절대 서버 시각 `impactTime` 기준이다.
- [ ] 패링 가능 공격이 미래 window와 impact time을 텔레그래프 시작에 등록한다.
- [ ] 피해는 impact time 전에는 적용하지 않는다.
- [ ] 성공 전에 window를 먼저 소비한다.
- [ ] 정확한 window의 timing miss도 window를 소비한다.
- [ ] 거부 request는 자원·행동 잠금·게이지·window를 바꾸지 않는다.
- [ ] `IParryable` 실행 인스턴스 수명과 close 경로가 있다.
- [ ] AI는 `IParryWindowTarget`만 참조하고 `ParryController` 구체 타입을 참조하지 않는다.
- [ ] 공격 취소·despawn·타겟 변경 시 window를 닫는다.
- [ ] 구르기와 패링 무적이 만료 시각 방식으로 합쳐진다.
- [ ] 성공 시 스태미나는 `PlayerStatus` API로 전량 회복한다.
- [ ] 게이지는 owner SyncVar, 최대 100, 초과 이월 없음이다.
- [ ] 치명 배율은 유효 공격 행동당 한 번만 추첨·소비된다.
- [ ] 무기 변경 reset은 서버 커밋 경로에 있고 SyncVar hook에 없다.
- [ ] InputAction은 authority 종료 때 이벤트 해제와 Disable을 모두 한다.
- [ ] 창귀 수직 슬라이스를 M2에서 통과한 뒤 보스에 확장한다.
- [ ] 패링 지연 보상을 M3의 0/30~50/150ms 조건에서 기록한다.
