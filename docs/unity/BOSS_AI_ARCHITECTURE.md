# 보스 AI 아키텍처 — Upper FSM · Combat BT · Pattern Execution

**2026-09-26 작성.** [보스 패턴 계약](BOSS_PATTERN_CONTRACT.md)이 "어떤 패턴이 언제 어떻게 발동하는가"를 정의한다면, 이 문서는 **"그 발동을 어떤 코드 계층이 결정하고 실행하는가"**를 정리한다. 대상 코드는 `BossController`, `Phase1State`/`Phase2State`, `BossPatternSelector`, `CombatBehaviorTree`, `BossSoulController`이며, 전부 `Geumeum.AI` 네임스페이스에 있다.

> 🔒 이 문서는 리팩터 완료 시점의 구조를 코드에서 그대로 옮긴 것이다. 문서와 코드가 다르면 **코드를 우선**하고 이 문서를 갱신한다.

---

## 0. 요약

보스 서버 로직은 `BossController.ServerTick`(`BossController.server.cs`) 하나를 진입점으로, 아래 순서로 실행된다.

```
ServerTick
 ├─ (죽음/정지/일시정지 가드)
 ├─ TickGroggySystem            — Stagger 진행 + Wake Pattern
 ├─ CommitPendingPhaseTransitions — Phase1/2 전환
 ├─ BossSoulController.ServerTick — SoulWindow/FailureAttack/Killable 전이
 ├─ ServerExpireSwallowed
 ├─ (TransitionLock 게이트)
 ├─ TickEnrageState
 ├─ Target 재스캔
 ├─ Upper State 게이트 — ResolveUpperState(...) != Combat 이면 종료
 └─ PhaseState.Tick               — Combat BT → Pattern Selection → Pattern Execution
```

이 문서의 나머지는 각 줄이 속한 책임 계층을 설명한다.

---

## 1. 전체 구조

```
[Upper State]
  BossUpperState = ResolveUpperState(IsDead, SoulState, GroggyPhase)
  우선순위: Dead > SoulWindow > Stagger > Combat
  ------------------------------------------------------------
  Combat이 아니면(Stagger/SoulWindow/Dead) -> Tick 중단
  Combat이면 -> 아래로 진행

[Global Orchestration]           [Modifiers]            [Locks]
  CommitPendingPhaseTransitions    Enrage                  TransitionLock
  BossSoulController.ServerTick    Groggy Immune           (Enrage/TargetRefresh/
  ServerExpireSwallowed                                     Combat tick을 스킵)
  Target 재스캔
  * Upper State와 무관하게 항상 실행됨, FSM State 밖에 유지

  이 셋을 전부 통과해야 아래 Combat 블록이 실행된다
  |
  v
[Combat] == PhaseState.Tick(deltaTime)
  |
  +-- Combat BT (AttackState == Idle일 때만 평가)
  |     Sequence: HasTarget
  |       -> Selector: Opening(Phase1만) / ForcedFollowUp / GeneralAttack
  |
  +-- Pattern Selection (GeneralAttack 리프가 호출)
  |     BuildCandidates -> BossPatternSelector.TrySelect
  |
  +-- Pattern Execution
  |     BeginAttack -> Windup -> Impact -> Recovery
  |
  +-- Independent Phase Systems
        TickGroundZones (Phase2 전용, AttackState/BT와 무관하게 매틱 실행)

[Forced Execution] (Combat BT · Pattern Selection을 완전히 우회)
  Wake Pattern(TickStagger 내부)
  FailureAttack(BossSoulController -> ServerHandleSoulStateChanged)
    -> TryBeginForcedAttack -> BeginAttack (Pattern Execution 직접 진입)
```

---

## 2. Upper State

`BossController.cs`의 `Types` 영역에 정의된 파생 분류다.

```csharp
private enum BossUpperState { Dead, SoulWindow, Stagger, Combat }

private static BossUpperState ResolveUpperState(bool isDead, BossFinalState soulState, GroggyPhase groggyPhase)
{
    if (isDead) return BossUpperState.Dead;
    if (soulState == BossFinalState.SoulWindow) return BossUpperState.SoulWindow;
    if (groggyPhase == GroggyPhase.Groggy) return BossUpperState.Stagger;
    return BossUpperState.Combat;
}
```

**Source of Truth는 이 함수가 아니라 인자로 들어오는 값이다.** `_status.IsDead`, `BossSoulController.SoulSnapshot.State`, `BossController._groggyPhase` 세 필드가 여전히 권위 있는 상태이고, `ResolveUpperState`는 그 값을 매 틱 읽어 계산만 하는 순수 함수다 — 별도로 저장되는 `CurrentBossState` 같은 필드는 없다.

| 조합 | 분류 | 이유 |
|---|---|---|
| `IsDead == true` | `Dead` | 최우선, 이후 아무것도 안 봄 |
| `SoulState == SoulWindow` | `SoulWindow` | `ExitGroggyForFinal`이 진입 전 Groggy를 강제로 지우므로 Groggy와 동시 관찰돼도 이쪽이 우선 |
| `GroggyPhase == Groggy` | `Stagger` | |
| `GroggyPhase == Immune` / `Active` | `Combat` | Immune은 Combat Tick을 막지 않음 |
| `SoulState == FailureAttack` / `Killable` | `Combat` | 둘 다 Tick을 막지 않음(아래 9장) |

`ServerTick`은 이 값을 아래 위치에서 게이트로 쓴다(기존 Groggy·SoulWindow 개별 `if`가 있던 자리 그대로):

```csharp
BossFinalState soulState = (_souls == null) ? BossFinalState.Dormant : _souls.SoulSnapshot.State;
BossUpperState upperState = ResolveUpperState(_status.IsDead, soulState, _groggyPhase);
if (upperState != BossUpperState.Combat) { return; }
_currentPhaseState.Tick(deltaTime);
```

---

## 3. Global Orchestration / Modifiers / Locks

Upper State와 무관하게(또는 별도 조건으로) 항상 도는 것들이다. `ServerTick`을 벗어나 FSM State 안으로 옮기지 않는다.

| 범주 | 항목 | 비고 |
|---|---|---|
| Global Orchestration | `CommitPendingPhaseTransitions` | 그로기 중에도 HP 기준 페이즈 전환은 계속 진행됨 |
| | `BossSoulController.ServerTick` | SoulWindow/FailureAttack/Killable 내부 전이(9장) |
| | `ServerExpireSwallowed` | 삼켜진 플레이어 만료, Combat과 무관 |
| | Target 재스캔(`RefreshTarget`) | TransitionLock에는 막히지만 Groggy/SoulWindow와는 무관하게 실행 |
| Modifier | Enrage(`TickEnrageState`) | `_isEnrageWarning`/`_isEnraged` 플래그만 갱신, State를 안 바꿈 |
| | Groggy Immune | `GroggyPhase.Immune`은 FSM State가 아니라 그로기 게이지 카운터 판정에만 영향을 주는 modifier |
| Lock | TransitionLock(`_transitionLockRemaining`) | `{Enrage tick, Target 재스캔, Combat state tick}`만 스킵시킴. `TickGroggySystem`/`CommitPendingPhaseTransitions`/`soul.ServerTick`은 이 락보다 먼저 실행되어 영향받지 않음 |

### Groggy / Immune 분리 (`TickGroggySystem`)

`GroggyPhase`(`Active`/`Groggy`/`Immune`)는 한 필드지만, 진행 로직은 둘로 나뉜다.

```csharp
private void TickGroggySystem(float deltaTime)
{
    if (HasEnteredFinalSequence()) return;
    switch (_groggyPhase)
    {
        case GroggyPhase.Groggy: TickStagger(deltaTime); break;
        case GroggyPhase.Immune: TickGroggyImmune(deltaTime); break;
    }
}
```

`switch`가 호출 시점의 `_groggyPhase` 값으로 딱 한 번만 분기하므로, `TickStagger` 안에서 `_groggyPhase`가 `Immune`으로 바뀌어도 같은 호출에서 `TickGroggyImmune`이 추가로 실행되지 않는다(같은 틱 이중 진행 방지). `TickStagger`는 그로기 지속시간 종료 판정과 **Wake Pattern**(`TryBeginForcedAttack`) 발동까지 포함한다.

---

## 4. Combat BT

`CombatBehaviorTree.cs`(`Geumeum.AI`)에 정의된 최소 구성이다. Running 상태가 없다 — `AttackState.Idle`일 때만 처음부터 재평가되고, `Windup`/`Recovery`의 진행은 Pattern Execution이 전담한다.

```
enum BTResult { Success, Failure, Blocked }
interface ICombatBTNode { BTResult Evaluate(); }
class SequenceNode  — 자식 중 Success가 아닌 첫 결과를 그대로 반환하고 멈춤
class SelectorNode  — Success면 즉시 반환, Blocked도 즉시 반환(다음 형제 안 봄), Failure면 다음 형제
class DelegateNode  — 델리게이트 하나를 리프로 감쌈(Condition/Action 구분 없음)
```

`Phase1State`/`Phase2State` 생성자가 조립하는 트리:

```
Sequence "CombatDecision"
├─ DelegateNode → EvaluateHasTarget
└─ Selector "PriorityBranch"
    ├─ DelegateNode → TryRunOpeningStep     [Phase1만 조립]
    ├─ DelegateNode → TryRunForcedFollowUp
    └─ DelegateNode → TryRunGeneralAttack
```

세 리프의 `BTResult` 의미:

| 리프 | Failure | Blocked | Success |
|---|---|---|---|
| `TryRunOpeningStep` | 오프닝 비활성(다음 시도) | 오프닝 패턴 데이터 조회 실패 — **즉시 종료, 다음 branch로 안 넘어감** | 발동 성공 |
| `TryRunForcedFollowUp` | 직전패턴≠Dash 또는 조회 실패(둘 다 다음 시도) | (없음) | 발동 성공 |
| `TryRunGeneralAttack` | 쿨다운 안 끝남(다음 시도, 사실상 마지막 branch) | `TrySelect` 실패 — 즉시 종료 | 발동 성공 |

`Blocked`는 "이 branch가 이번 평가를 점유했지만 실행은 못 했다"는 뜻으로 하위 우선순위 branch로 넘어가지 않는다 — 기존 `TickIdle`의 단순 `return`과 의미가 같다(오프닝 데이터 조회 실패 시 강제후속/일반공격으로 새지 않던 원래 동작 보존).

`TickIdle()` 자체는 얇은 진입점이다:
```csharp
private void TickIdle()
{
    _shadowCategoryForVerification = EvaluateShadowCategory(); // 검증용, 아래 12장 참고
    _combatBT.Evaluate();
}
```

---

## 5. Pattern Selection

`BossPatternSelector`(정적 클래스, `BossPatternSelector.cs`)는 `Combat BT`의 `TryRunGeneralAttack` 리프가 호출하는 서비스다. 응집도 있는 단일 책임(패턴 하나를 고른다)이라 별도 BT 노드로 쪼개지 않았다.

- 후보 준비는 `Phase1State`/`Phase2State`의 `BuildCandidates`(거리 적격성 계산)가 맡는다.
- `TrySelect`는 직전 패턴 제외 + 쿨다운/거리/조건부/봉인 필터 → 가중치(`SelectionWeight`) 비례 추첨. 우선순위 개념은 없다.
- RNG는 각 `Phase*State`가 소유한 `System.Random _random` 인스턴스 하나뿐이고, 호출 지점은 `TryRunGeneralAttack` 한 곳이다.

---

## 6. Pattern Execution

`Phase1State`/`Phase2State`의 `AttackState`(`Idle`/`Windup`/`Recovery`) 상태 기계다. Combat BT는 이 내부를 매 틱 들여다보지 않는다.

```
BeginAttack(pattern, kind)
 └─ _state = Windup, 텔레그래프 발행, 쿨다운 세팅
     └─ TickWindup: 반복 판정 → ResolveImpact(BossPatternRunner.ExecuteMeleeArc)
         └─ 마지막 반복 종료 → ServerNotifyPatternEnded → _state = Recovery
             └─ TickRecovery: ActiveDuration 경과 → _state = Idle
```

`BeginAttack`은 **세 곳**에서 호출되며, 이것이 두 계층(Normal/Forced)의 유일한 합류 지점이다:
1. Combat BT의 세 리프(`TryRunOpeningStep`/`TryRunForcedFollowUp`/`TryRunGeneralAttack`)
2. `TryBeginForcedAttack`(7장)

---

## 7. Forced Execution

`BossPhaseStateBase.TryBeginForcedAttack`(가상 메서드, `Phase1State`/`Phase2State`가 구현)이 유일한 진입점이다.

```csharp
public override bool TryBeginForcedAttack(int patternId, int repeatCountOverride)
{
    if (IsExited || (Target == null) || _controller.ServerIsPatternSealed(patternId) ||
        (!GameDB.Boss.TryGetPattern(patternId, out var pattern)))
    { return false; }

    CancelActiveAttack();
    BeginAttack(pattern, KindOf(patternId));
    return true;
}
```

**Combat BT를 완전히 우회한다** — `TickIdle`/`_combatBT`를 전혀 참조하지 않는다. 호출부 2곳:

| 호출부 | 위치 | patternId 출처 |
|---|---|---|
| Wake Pattern | `BossController.TickStagger` (그로기 종료 시점) | `_bossData`의 `phase.WakePatternId` |
| FailureAttack(백귀야행) | `BossController.ServerHandleSoulStateChanged`(`case FailureAttack`) | `_bossData.FinalFailurePatternId`, 반복 수는 `snapshot.FailureBranchCount` |

**같은 틱 충돌 불가능한 이유**: `ServerTick`에서 이 두 호출부(`TickGroggySystem`, `soul.ServerTick`이 유발하는 콜백)는 전부 `_currentPhaseState.Tick()`(Combat BT 진입점)**보다 코드 순서상 먼저** 실행된다. 이번 틱에 강제 발동이 일어나면 `BeginAttack`이 이미 `_state`를 `Windup`으로 바꾼 뒤에야 `Tick()`이 호출되므로, `switch(_state)`가 `TickWindup`으로 분기해 `TickIdle`(Combat BT)은 아예 실행되지 않는다. `CancelActiveAttack`은 "누가 우선인지"를 정하는 게 아니라, 진행 중이던 공격(Combat BT가 시작했든 이전 강제 발동이 시작했든)을 무조건 정리하고 새 공격으로 교체하는 장치다.

---

## 8. Independent Phase Systems

Phase2 전용, `Phase2State.TickGroundZones`. `Phase2State.Tick()` 최상단에서 `AttackState`/Combat BT 평가와 무관하게 매 틱 호출된다:

```csharp
public override void Tick(float deltaTime)
{
    if (IsExited) { return; }
    TickGroundZones(deltaTime);   // AttackState switch보다 앞
    ...
    switch (_state) { ... }
}
```

- 자체 상태(`_groundZones[]`, `_groundZoneLandmarkOccupied[]`, `_groundZoneSpawnCooldown`)를 `Phase2State`가 직접 소유하며 `BuildCandidates`/`BossPatternSelector`/`BeginAttack`과 상태를 공유하지 않는다.
- 데미지도 `BossPatternRunner.ExecuteMeleeArc`를 직접 호출해 적용한다(`ResolveImpact` 미경유).
- **AttackState/Combat BT로부터는 독립이지만 Upper State로부터는 독립이 아니다** — `PhaseState.Tick()` 자체가 Upper State 게이트에 막히면 `TickGroundZones`도 같이 멈춘다.
- Combat BT 노드로 편입하면: (a) 여러 장판을 동시 갱신하는 성격이 BT의 "하나를 고른다" 의미론과 안 맞고, (b) `Idle`일 때만 평가되는 BT 모델에 넣으면 Windup/Recovery 중 타이머가 멈추는 회귀가 생긴다. 그래서 독립 시스템으로 남긴다.
- `Phase1State`에는 대응물이 없고, `BossController`의 다른 매틱 로직(Enrage/SoulTick/Swallowed 만료)은 이미 3장의 Global/Modifier로 분류되어 성격이 다르다 — **다른 독립 시스템 후보는 없다.**

---

## 9. FinalSequence(BossSoulController)와의 관계

`BossSoulController`는 별도 컴포넌트로, `BossFinalState`(`Dormant`/`SoulWindow`/`FailureAttack`/`Killable`)를 소유한다. Upper State는 이 중 `SoulWindow`만 별도 FSM 값으로 승격했다 — 나머지는 Combat으로 분류된다(2장 표).

```
Dormant
  -> (파츠 전파, ServerHandlePartBroken: ExitGroggyForFinal + ServerBeginFinal)
  -> SoulWindow
       -> (원혼 전부 제거, TryCommitImmediateSuccess)         -> Killable
       -> (데드라인 경과, 원혼 남음)                            -> FailureAttack
                -> (강제 패턴 종료 시점, 원혼 전부 제거)         -> Killable
                -> (강제 패턴 종료 시점, 원혼 남음)              -> SoulWindow (재진입)
```

- **SoulWindow**: 진입 시 `ServerHandleSoulStateChanged`가 `_currentPhaseState?.CancelActiveAttack()` 호출 + Upper State 게이트가 `Tick()` 자체를 막음 — Combat과 완전 배타적.
- **FailureAttack**: 진입 시 `TryBeginForcedAttack`으로 백귀야행을 강제 발동하지만, `Tick()` 자체는 막히지 않는다(Combat으로 분류). 강제 패턴이 끝나는 순간(`ServerNotifyFailureAttackEnded`, `TickWindup` 내부에서 동기 호출) 원혼이 남았으면 `SoulWindow`로, 다 지워졌으면 `Killable`로 즉시 전이한다.
- **Killable**: 무적 해제(`ServerSetInvulnerable(false)`)만 하고 Combat 실행에 아무 제약을 걸지 않는다 — 사실상 평시 Combat과 동일하게 동작하다 플레이어 데미지로 사망(`HandleDied`)한다.

`BossSoulController` 내부 상태 구조와 전이 로직은 이번 리팩터에서 변경하지 않았다.

---

## 10. 클래스별 책임 요약

| 클래스 | 책임 |
|---|---|
| `BossController` | `ServerTick` 오케스트레이션, Upper State 계산, Groggy/Enrage/TransitionLock 소유, `BossSoulController`와의 연결(`ServerHandleSoulStateChanged`) |
| `Phase1State` / `Phase2State` | Combat BT 트리 조립·리프 구현, Pattern Selection 후보 준비(`BuildCandidates`), Pattern Execution(`AttackState`), `TryBeginForcedAttack` 구현. `Phase2State`만 `TickGroundZones` 추가 소유 |
| `BossPatternSelector` | 가중치 기반 패턴 추첨(정적, 상태 없음) |
| `CombatBehaviorTree.cs` (`BTResult`/`ICombatBTNode`/`SequenceNode`/`SelectorNode`/`DelegateNode`) | Combat BT 최소 구성 요소 |
| `BossSoulController` | 원혼 HP·`BossFinalState` 전이 소유(변경 없음) |

---

## 11. 남은 기술부채 / 미검증

- **L3(3인 접속) 미검증** — Combat BT(구 TickIdle 대체)와 Upper State 게이트 둘 다 L2(Dev_AI 단일 플레이)까지만 실측 확인했다.
- **FailureAttack 종료 재진입** — `ServerNotifyFailureAttackEnded`가 `Phase*State.TickWindup`의 같은 호출 프레임 안에서 재귀적으로 `CancelActiveAttack`을 호출하는 구조. 관찰된 부작용은 없으나 구조 자체는 남아있다.
- **`TryBeginForcedAttack` 실패 엣지 케이스** — FailureAttack 강제 발동이 데이터 조회 실패 등으로 실패하면, 그 틱에 일반 `GeneralAttack`이 대신 선택될 수 있는 코드상 가능성이 있다(방어 조치 없음).
- **Dev_AI 씬의 `TestPlayer` 레이어 문제** — 장식용 오브젝트가 Player 레이어에 있어 실플레이어 사망 시 대체 타겟으로 잡히는 현상(씬 설정 문제, 코드 문제 아님).
- **Stage 3 Combat BT Shadow 잔존** — `Phase1State`/`Phase2State`의 `EvaluateShadowCategory`/`_shadowCategoryForVerification`/`[ShadowBT]` 로그는 L3 검증 완료 전까지 검증용으로 유지 중이다. Stage 5-1의 Upper State Shadow(`oldAllowed`/`[UpperStateShadow]`)는 게이트 이관 완료 후 제거했다(3·9번 항목 참고).
- **테스트 내부 재현식** — `BossUpperStateShadowTests`의 `oldAllowed` 조합 테스트는 프로덕션 코드가 아니라 테스트 안에서 독립적으로 재현한 수식을 쓴다. 프로덕션 게이트 조건이 바뀌면 이 재현식도 별도로 갱신해야 한다.

---

## 12. 변경 이력 (요약)

| 단계 | 내용 |
|---|---|
| Stage 1 | 기존 `TickIdle` 분기별 계측 로그 추가(동작 변경 없음) |
| Stage 2 | Combat BT Shadow 평가 도입(읽기 전용, 결정권 없음) |
| Stage 3 | `TickIdle`의 의사결정 전체를 Combat BT(`SequenceNode`/`SelectorNode`/`DelegateNode`)로 일괄 이관, 실행 계층은 그대로 재사용 |
| Stage 4 | Forced Execution(Wake Pattern/FailureAttack)이 이미 Combat BT를 우회하고 있음을 확인 — 코드 변경 없음 |
| Stage 5-0~5-3 | `TickGroggyState`를 `TickGroggySystem`/`TickStagger`/`TickGroggyImmune`으로 분리 → `ResolveUpperState` Shadow 도입 → 실제 게이트 이관 → 진단 코드 정리 |
| Stage 6 | 이 문서 작성, `TickGroundZones`를 Independent Phase System으로 확정 |
