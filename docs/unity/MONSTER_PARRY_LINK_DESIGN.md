# 몬스터 공격 → 패링 창 연결 설계

> 상태: 설계 확정본, 구현 전  
> 출처: 2026-09-01 외부 소스 감사와 함께 받은 설계 산출물. 저장소 문서로 편입했다  
> 관련: [프로젝트 구조 설계서](../04-architecture.md) · [네트워크 권위](NETWORK_AUTHORITY.md) · [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)

> **2026-09-02 갱신 — 이 문서가 전제로 삼은 P0 게이트는 이미 구현됐다.**
> 대상당 단일 active 창, `ImpactTime`까지의 레코드 보존, 소비 tombstone, 서버 수신 마감(`RejectedResolutionClosed`),
> 정확한 복합 키 검사, 유한 시각 검사가 `ParryController`에 반영되어 커밋됐다. `IParryWindowTarget.OpenParryWindow`도 동작한다.
> 따라서 아래 6절의 단계별 구현에서 **P0 게이트 단계는 건너뛰고 `ChanggwiBrain` 연결부터 시작**하면 된다.
> 현재 계약의 실제 형태는 [패링 시스템 기술 설계](PARRY_SYSTEM_DESIGN.md)와 `ParryController.cs`를 함께 본다.

> **2026-09-05 갱신 — 6절 3단계 중 실행 단위 source 부분만 구현됨(부분 검증).**
> `ChanggwiAttackExecution.cs`를 신설해 `IParryable`을 구현시키고, `ChanggwiBrain`은 더 이상 자기 자신을
> source로 넘기지 않는다. `Brain.HandleExecutionParried`가 `ReferenceEquals(execution, _activeExecution)`로
> 지나간 공격의 늦은 통지를 무시하는 것까지 리플렉션으로 직접 검증했다(3.3절 의도대로 동작).
> **아직 안 한 것**: `EnemyController.RefreshTarget`의 canonical target 정규화(2단계), `EnemyBrainBase.Tick`에
> 서버 시각 전달·typed event(`AttackBegan`/`AttackResolved`) 재구성(3단계 나머지), `AttackTelegraphCue` /
> `IAttackTelegraphView` / `ClientRpc` 텔레그래프(5단계) — 즉 원격 클라이언트는 아직 전용 텔레그래프 연출을
> 못 본다. 3인 접속(L3) 검증도 안 됐다. 이 문서를 "구현 완료"로 표시하려면 6절의 나머지 단계가 더 필요하다.

> 기준 코드: 2026-09-01 제공 소스  
> 결론: 대상당 단일 active·`ImpactTime`·소비 tombstone·서버 수신 마감을 P0 게이트로 먼저 맞춘다. 그 위에서 `ChanggwiBrain`이 서버 절대 시각의 `ChanggwiAttackExecution`을 만들고 기존 `IParryWindowTarget`에 미래 창을 등록한다. `EnemyController`는 typed event를 받아 agent·피해·`ClientRpc`를 실행하고, 클라이언트는 애니메이션 없이 바닥 돌진선·발광·소리만 표현한다.

## 1. 목표와 범위

이번 범위의 완료 조건은 창귀 한 마리의 한 번의 돌진에서 아래 수직 경로가 끊김 없이 동작하는 것이다.

1. 서버가 대상·돌진 방향·`impactTime`·`ParryWindowKey`를 한 번 확정한다.
2. 대상의 기존 `IParryWindowTarget.OpenParryWindow`를 호출한다.
3. 검을 든 대상이 창 안에서 패링하면 기존 `ParryController`가 그 창을 연 실행 단위 `ChanggwiAttackExecution.OnParried`를 정확히 한 번 호출한다.
4. 창귀는 피해 예약과 돌진을 취소하고 `Stagger`로 전이한다.
5. 패링하지 않았으면 서버가 `impactTime`에 대상 거리와 생존 상태를 다시 검사해 피해를 적용한다.
6. 세 클라이언트 모두 같은 돌진 방향과 타격 시각을 읽을 수 있는 텔레그래프를 본다.

이번에 만드는 것은 `ChanggwiBrain.cs`의 현재 `TODO(오나)` 접점, 공격 취소·종료 시 창 닫기, 절대 서버 시각 타임라인, 애니메이션이 없어도 읽히는 최소 텔레그래프다. 다만 현재 구현과 P0 패링 계약 사이에서 이 연결의 결과를 직접 바꾸는 항목은 후속으로 미루지 않는다. 아래 네 조건을 **P0 선행 게이트**로 먼저 통과해야 서버 수직 슬라이스를 시작할 수 있다.

1. 서버와 클라이언트 모두 대상 플레이어당 활성 창이 하나이며 마지막으로 수락된 등록이 이긴다.
2. 활성 레코드가 `ImpactTime`을 보관하고, 소비 tombstone은 활성 슬롯과 분리되어 해당 impact까지 살아 있다.
3. window 안의 입력은 `windowEnd` 뒤 도착해도 서버 수신 시각이 `impactTime`보다 빠르면 판정할 수 있고, `serverReceivedAt >= impactTime`이면 항상 거부한다.
4. 등록 경계에서 모든 시각의 유한수 여부, phase·생명 상태, Sword 장착을 검증한다.

이번에 만들지 않는 것은 보스 패턴, **한 플레이어를 향한 둘 이상의 동시 활성 창**, 가로채기 패링, 패링 즉시 반격 피해, 전용 카메라·히트스톱, 공격 애니메이션 조달, 전체 `ParryRequest` DTO·세부 결과 코드·관측 텔레메트리의 완성이다. 공용 `OpenParryWindow`/`CloseParryWindow`와 `IParryable.OnParried(uint)` 서명은 유지하되, 위 P0 게이트에 필요한 저장소·시간·등록 검증은 이 작업 범위에 포함한다.

## 2. 권위 경계

Mirror는 비플레이어 오브젝트를 기본 서버 권위로 두며, Command는 클라이언트에서 서버로, ClientRpc는 서버에서 클라이언트로 실행하는 공식 경로를 제공한다. 따라서 창귀 AI와 패링 결과는 서버에만 있고, 텔레그래프만 클라이언트가 그린다. 근거는 Mirror 공식 [Authority](https://mirror-networking.gitbook.io/docs/manual/guides/authority)와 [Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions)다.

| 값 · 판정 | 서버 | 클라이언트 | 근거 |
|---|---|---|---|
| 창귀 상태, 현재 대상, 실행 키 | `ChanggwiBrain`과 현재 `ChanggwiAttackExecution`만 생성·변경 | 직접 변경하지 않음 | 사람마다 다르면 타격·경직 결과가 갈린다 |
| 감지 collider → 공격 대상 | `EnemyController.RefreshTarget`이 부모 `NetworkIdentity` 루트의 `Transform`·`IDamageable`로 정규화하고 `netId`로 중복 제거 | 대상 선택에 관여하지 않음 | 자식 collider 수나 배치가 판정을 바꾸면 안 된다 |
| `telegraphStartTime`, `chargeStartTime`, `impactTime` | `EnemyController.ServerTick`에서 읽은 `NetworkTime.time`으로 한 번 확정 | RPC로 받은 시각을 읽어 진행률만 계산 | Mirror의 `NetworkTime.time`은 동기화된 `double` 시각으로 제공된다. [Mirror Time Synchronization](https://mirror-networking.gitbook.io/docs/manual/guides/time-sync) |
| 패링 창 등록·소비·성공 | `ParryController`의 단일 active 슬롯·소비 tombstone·`CmdParry`·`ParryJudge` | 소유자는 관찰한 키와 입력만 보냄 | `[Server]` + `[Command]`; 입력 시각과 서버 수신 마감 모두 서버가 검증 |
| 돌진 명중과 `DamageInfo` | `impactTime`에 서버가 거리·생존·무적을 확인하고 적용 | HP SyncVar를 표현 | 판정 결과가 게임 상태를 바꾼다 |
| 창귀 경직 | 실행 단위 `OnParried`가 owner Brain의 현재 실행 일치를 확인한 뒤 `Stagger` 확정 | 종료 이유를 받아 발광·소리만 재생 | `OnParried`를 클라이언트에서 호출하지 않는다 |
| 텔레그래프 시작·종료 | `EnemyController`가 `[ClientRpc]`를 발신 | `IAttackTelegraphView` 구현이 렌더링 | 서버가 무엇을·언제·어디서만 보내고 표현은 로컬에서 만든다 |
| 대상 플레이어용 창 알림 | 기존 `TargetOpenParryWindow`/`TargetCloseParryWindow` | 소유자만 로컬 활성 키 보관 | 기존 `[TargetRpc]` 재사용 |
| 창귀 위치 | 기존 서버 `NavMeshAgent`와 적 NetworkTransform | 기존 보간만 수행 | 새 이동 동기화 경로를 만들지 않는다 |

배치는 다음으로 고정한다.

- `ChanggwiBrain`과 `ChanggwiAttackExecution`은 순수 C# 객체이므로 Mirror 특성을 붙이지 않는다. Brain은 실행 상태·절대 deadline·창 open/close·취소만 소유한다. 서버에서만 호출되는 `EnemyController`가 시각과 `attackerNetId`를 주입하고, `NavMeshAgent` 조작·피해 적용·ClientRpc를 전담한다.
- `EnemyController.RefreshTarget`은 `Collider.transform`을 그대로 `Target`에 넣지 않는다. `OnStartServer`에서 만든 재사용 collider buffer로 `Physics.OverlapSphereNonAlloc`을 호출하고, 각 hit에서 `GetComponentInParent<NetworkIdentity>()`로 플레이어 네트워크 루트를 찾는다. 그 루트의 `IDamageable`과 `Transform`을 하나의 후보로 취급하고 재사용 `HashSet<uint>`로 `netId`를 중복 제거하므로, 한 플레이어의 자식 collider가 여러 개 잡혀도 후보는 하나다. buffer가 가득 차면 진단 카운터/경고를 남기고 런 중 resize하지 않는다. Mirror도 중첩 `NetworkIdentity`를 지원하지 않고 자식이 `GetComponentInParent`로 부모 identity를 찾도록 안내한다. [Mirror Network Identity](https://mirror-networking.gitbook.io/docs/manual/components/network-identity), [Unity 6.4 `Physics.OverlapSphereNonAlloc`](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Physics.OverlapSphereNonAlloc.html)
- `EnemyController`의 텔레그래프 시작·종료 메서드는 `[ClientRpc]`다. 이미 존재하는 창귀 `NetworkIdentity`를 사용하므로 텔레그래프용 `NetworkIdentity`나 `NetworkServer.Spawn`은 추가하지 않는다.
- `ParryController.OpenParryWindow`, `CloseParryWindow`, 창귀 공격 시작·취소·impact 처리는 `[Server]` 경계 안에서만 호출한다.
- `ParryController`의 서버 활성 창도 플레이어당 하나다. `ActiveParryWindow`는 `Key`, `StartTime`, `EndTime`, `ImpactTime`, `Source`를 보관한다. 새 창을 정상 수락하면 현재 창을 먼저 정확한 키로 닫고 새 키로 교체한다. 창별 단조 증가 `WindowId`는 공격 실행을 만드는 `EnemyController`가 소유한다. 대상은 exact active와 소비 tombstone만 검사하고, 공격자 수만큼 계속 커지는 `_lastIdByAttacker`는 제거한다.
- `windowEnd`는 입력 구간의 끝이지 서버 레코드의 폐기 시각이 아니다. 소유 클라이언트의 활성 표시는 exact-key close로 끝내되, 이미 전송된 입력을 판정할 서버 레코드는 `ImpactTime`까지 유지한다. 성공 소비 시에는 활성 슬롯을 비우고 `(Key, ImpactTime)` 소비 tombstone을 별도로 남긴다. 새 창 교체는 이 tombstone을 지우지 않는다.
- `CmdParry`는 Mirror Command이므로 `AuthorityTickScheduler`의 AI 우선순위와 직접적인 선후 관계가 없다. Command handler가 기록한 `serverReceivedAt < ImpactTime`만 성공 후보이며, 같거나 늦으면 AI callback 실행 전이라도 판정 마감으로 거부한다. 그보다 먼저 성공한 콜백은 Brain의 취소 플래그를 동기적으로 세우고 이후 impact commit이 이를 다시 확인한다.
- 새 `[SyncVar]`는 만들지 않는다. 창·AI 실행은 서버 수명이 짧은 상태이고, 지속 상태인 HP·게이지·플레이어 무기는 이미 각 소유 컴포넌트의 SyncVar다. 런 시작 뒤 신규 입장을 막는 현재 프로젝트에서는 신뢰성 있는 일회성 RPC로 텔레그래프를 충분히 복원할 수 있다.

## 3. 구조

### 3.1 파트와 의존 방향

```text
Geumeum.AI.ChanggwiBrain
    ├─ owns → ChanggwiAttackExecution --implements--> Geumeum.Common.IParryable
    └─ uses ────────────────────────────────────────> Geumeum.Common.IParryWindowTarget / ParryWindowKey
             ▲ 구현
Geumeum.Combat.ParryController

ChanggwiBrain --typed event--> Geumeum.AI.EnemyController --ClientRpc--> 로컬 IAttackTelegraphView
                                                ▲ 구현
                                      Geumeum.Art.ChanggwiTelegraphView
```

`AI`는 `ParryController` 또는 `Geumeum.Combat`을 참조하지 않는다. `Combat`도 `ChanggwiBrain`을 참조하지 않는다. 두 판정 파트는 이미 존재하는 `00.Common`의 `IParryable`, `IParryWindowTarget`, `ParryWindowKey`로만 만난다. AI와 로컬 아트가 만나는 지점에만 작은 표현 계약을 추가한다. `01.Network`에는 새 도메인 클래스를 만들지 않는다.

### 3.2 기존 코드 재사용과 필요한 변경

| 파일 · 클래스 | 상태 | 이 설계의 책임 |
|---|---|---|
| `00.Common/Interfaces/IParryable.cs` | 기존 재사용 | `OnParried(uint)` 서명은 유지하되 장수명 Brain이 아니라 실행 단위 `ChanggwiAttackExecution`이 구현 |
| `00.Common/Interfaces/IParryWindowTarget.cs` | 기존 재사용 | AI가 창을 열고 닫는 유일한 Combat 경계 |
| `00.Common/Types/ParryWindowKey.cs` | 기존 재사용 | `(AttackerNetId, WindowId)` 복합 키; 창귀별 단조 증가 ID |
| `02.Combat/Parry/ParryController.cs` | P0 선행 보강 | phase·생명·Sword·유한수 등록 검사, 대상당 단일 active 슬롯, exact-key 교체/close, `serverReceivedAt < ImpactTime` 마감, 소비 tombstone과 TargetRpc 처리 |
| `02.Combat/Parry/ParryWindow.cs` | 필드 보강 | 서버에서 source·창 구간과 `ImpactServerTime` 보관. 입력 종료와 레코드 폐기를 분리 |
| `03.AI/Enemy/EnemyBrainBase.cs` | 서명 보강 | `Tick(float deltaTime, double serverTime)`으로 바꾸고 세 Brain에 같은 시각 전달. 허깨비·액귀는 당장 `serverTime`을 무시해도 된다 |
| `03.AI/Enemy/ChanggwiAttackExecution.cs` | 신규 | 공격 한 번의 key·canonical target·절대 시각·취소/impact 플래그를 snapshot하고 `IParryable` 구현 |
| `03.AI/Enemy/ChanggwiBrain.cs` | 주 접점 | 대상 snapshot과 실행 객체 생성, 절대 deadline, key, 창 대상 캐시, open/close, cancel·stagger, 시작/해결 이벤트를 소유. agent·피해·RPC는 소유하지 않음 |
| `03.AI/Enemy/EnemyController.cs` | 네트워크·실행 접점 | `RefreshTarget` hit를 부모 `NetworkIdentity` 루트로 정규화하고 `netId` 중복 제거, `NetworkTime.time` 전달, Brain 이벤트 구독, agent 조작, ClientRpc, snapshot 대상에 대한 최종 피해 적용, death/`OnStopServer` 정리 |
| `03.AI/Enemy/EnemyStatus.cs` | 기존 재사용 | `EnemySO.StatEntry` 값을 스폰 시 snapshot하여 Brain에 제공 |
| `00.Common/Data/EnemySO.cs` | 기존 재사용·검증 보강 | 새 밸런스 도메인을 만들지 않고 현재 창귀 필드 사용 |

`ParryController`의 구체적 교체 규칙은 다음으로 고정한다. 현재 `_activeByAttacker`, `_lastIdByAttacker`, 복수 `_windows`를 단일 `ActiveParryWindow _activeWindow`와 소비 tombstone 한 칸으로 바꾼다. `OpenParryWindow`가 key 유효성·시각·대상 자격·source 실행 일치를 모두 통과한 뒤 기존 active가 있으면 `TargetCloseParryWindow(oldKey)`를 보내고 이전 레코드를 폐기한 다음 새 창을 저장한다. `WindowId`의 0 건너뛰기와 단조 증가는 `EnemyController`의 실행 ID 생성기가 책임진다. `CloseParryWindow(key)`는 `key == _activeWindow.Key`일 때만 active를 비운다. windowEnd 도달 시 소유 클라이언트에는 exact-key close를 보내되 서버 active 레코드는 impact까지 유지한다. 성공 소비는 active를 비우고 `(Key, ImpactTime)`을 `_consumedWindowKey`, `_consumedWindowImpactTime`에 보관한다. 이 tombstone은 새 등록과 무관하게 impact 뒤에만 폐기한다. 이 순서라면 A 창귀의 늦은 close가 이미 교체된 B 창귀 창을 닫을 수 없고, 40분 런에서도 target 쪽 key 컬렉션 크기는 active 1+tombstone 1로 고정된다.

### 3.3 실행 단위 source와 Controller 이벤트 계약

`IParryable.OnParried(uint)`에는 key가 없으므로 재사용되는 `ChanggwiBrain` 자체를 source로 넘기지 않는다. 공격을 시작할 때 plain C# `ChanggwiAttackExecution`을 하나 만들고 아래 실행 snapshot을 고정한다.

- `ParryWindowKey`, 정규화된 target root, 캐시한 `IDamageable`·`IParryWindowTarget`
- origin·direction·destination과 telegraph/charge/impact 절대 시각
- `ParryWindowDuration`, `_isCancelled`, `_impactCommitted`, `_isResolved`
- owner `ChanggwiBrain` 참조

`ChanggwiAttackExecution`이 `IParryable`을 구현하고 `OnParried(playerNetId)`에서 `Owner.TryParry(this, playerNetId)`를 호출한다. Brain은 `ReferenceEquals(_activeExecution, execution)`과 key 일치를 모두 확인한 경우에만 취소한다. 따라서 이전 실행의 늦은 콜백이 다음 공격을 경직시키지 못하며 공용 인터페이스 서명도 바꾸지 않는다. 실행 객체는 window close와 impact/tombstone 수명이 끝날 때까지 파기하거나 재사용하지 않는다.

Brain과 Controller 사이는 두 typed event로 연결한다.

1. `AttackBegan(AttackTelegraphCue cue)`: Brain이 snapshot·active key·window 등록을 먼저 커밋한 뒤 발생시킨다. Controller handler는 `RpcBeginAttackTelegraph`만 보낸다.
2. `AttackResolved(ChanggwiAttackExecution execution, AttackTelegraphEndReason reason)`: Brain이 취소/impact 상태와 window close를 먼저 커밋한 뒤 정확히 한 번 발생시킨다. Controller handler는 먼저 `_agent.isStopped = true`, 다음으로 reason이 `Impact`일 때만 snapshot 대상의 생존·spawn·거리를 재검사해 피해를 최대 한 번 적용하고, 마지막으로 `RpcEndAttackTelegraph`를 보낸다.

`EnemyController.OnStartServer`는 Brain 생성 직후, scheduler 등록 전에 이벤트를 구독한다. `HandleDied`는 `NetworkServer.Destroy` 전에 active 실행을 cancel/close한다. `OnStopServer`는 scheduler 유무와 무관하게 cancel/close → 이벤트 해제 → 가능한 경우 scheduler 해제 순서를 지킨다. 창귀 impact는 이 이벤트 경로만 사용하며 기존 `ShouldAttackNow → ExecuteAttack(_brain.Target)` 분기로 다시 피해를 주지 않는다.

### 3.4 새 공용 표현 계약

`Assets/01.Scripts/00.Common/Types/AttackTelegraphCue.cs`, `AttackTelegraphEndReason.cs`를 추가한다. Mirror가 지원하는 public 값 필드만 둔다.

> ⚠️ **아래 `AttackTelegraphCue`는 초안이다. 실제로 만들 것은 [3.5.3](#353-attacktelegraphcue에-도형-정보를-넣는다)의 확장판이다** — 이 초안에는 `Shape`와 `HalfAngleDegrees`가 없어 부채꼴·원뿔을 그릴 수 없고 선인지 원인지 구분할 수도 없다. 나머지 서술(RPC 경로, 뷰 계약, 재사용 규칙)은 그대로 유효하다.

```csharp
public struct AttackTelegraphCue
{
    public ParryWindowKey Key;
    public Vector3 Origin;
    public Vector3 Direction;
    public float Distance;
    public float Radius;
    public double TelegraphStartServerTime;
    public double ChargeStartServerTime;
    public double ImpactServerTime;
}

public enum AttackTelegraphEndReason : byte
{
    Impact = 0,
    Parried = 1,
    Cancelled = 2,
}
```

`Assets/01.Scripts/00.Common/Interfaces/IAttackTelegraphView.cs`는 `Play(in AttackTelegraphCue)`와 `Stop(ParryWindowKey, AttackTelegraphEndReason)`만 노출한다. Mirror는 기본형·`Vector3`·그 조합인 struct를 Remote Action 인자로 지원한다. [Mirror Data types](https://mirror-networking.gitbook.io/docs/manual/guides/data-types)

`Assets/01.Scripts/05.Art/Enemy/ChanggwiTelegraphView.cs`는 `MonoBehaviour`, `IAttackTelegraphView` 구현이다. `NetworkBehaviour`가 아니다. 적 프리팹의 자식 `LineRenderer` 또는 얇은 quad를 돌진 길이와 지름에 맞춰 켜고, `MaterialPropertyBlock`으로 몸체 발광을 pulse하며, 선택적으로 `AudioSource.PlayOneShot`을 호출한다. 매 공격마다 오브젝트나 머티리얼을 생성하지 않고 프리팹 자식을 재사용한다.

애니메이션이 없어도 다음 신호는 반드시 보인다.

- 선딜 시작: 창귀 발밑에서 서버가 고정한 방향으로 붉은 돌진선 표시.
- 창 구간 접근: 몸체 발광 pulse 속도가 빨라짐.
- 돌진 시작: 돌진선 밝기를 한 단계 올림.
- `Impact`: 선을 즉시 끄고 짧은 먼지/소리.
- `Parried`: 선을 즉시 끄고 다른 색의 섬광/금속음. 실제 경직 모션은 애니메이션 파이프라인 범위로 남김.


### 3.5 텔레그래프 계약 — 잡귀와 보스가 함께 쓴다

`ChanggwiAttackExecution`은 창귀 전용이지만 **예고의 모양과 시간 규약은 보스가 그대로 재사용한다.** 그래야 30분간의 잡귀 학습이 보스전으로 전이된다([직업 정체성과 보스 설계](../class-identity-and-boss-2026-09-03.md)). 이 절은 그 공용 규약이다.

#### 3.5.1 🚨 선딜은 창보다 길어야 한다 — 이것이 최우선 불변 조건

`ParryController.OpenParryWindow`는 `impactTime`에서 창을 **역산**한다(코드 실측, `ParryController.cs:333`).

```text
windowEnd   = impactTime - PARRY_RESOLUTION_LEAD     // 0.18초 (Constants.Network)
windowStart = windowEnd - ParryWindowDuration        // 창귀 기준 0.32초
```

그리고 **창이 예고보다 먼저 열리면 거부한다.** 플레이어가 예고를 보기도 전에 판정이 시작되기 때문이다.

```text
if (windowStart < telegraphStartTime) → 창을 열지 못하고 false
```

> ### 그래서 모든 패링 가능 공격은 이 식을 만족해야 한다
>
> ```text
> impactTime - telegraphStartTime  ≥  ParryWindowDuration + PARRY_RESOLUTION_LEAD
> ```
>
> **왼쪽은 `WindupDuration`이 아니라 예고 시작부터 타격까지의 전체 구간이다.** 공격 형태에 따라 다르다.
>
> | 형태 | 예고 → 타격 | 예 |
> |---|---|---|
> | **즉발형** (부채꼴·원뿔·원형) | `WindupDuration` | 보스 `MeleeArc`·`ConeBurst` |
> | **돌진형** | `WindupDuration + ChargeDistance / ChargeSpeed` | 창귀 · 보스 `TargetedDash` |
>
> **이 조건을 어기면 `OpenParryWindow`가 조용히 `false`를 돌려주고 패링이 아예 열리지 않는다.** 콘솔 경고 한 줄만 남고 게임은 정상으로 보인다 — 창귀가 공격하고, 피해가 들어가고, **패링만 없다.** 밸런스 조정 중 선딜을 줄이면 시그니처 메커닉이 사라진다.

**출시 전 검사는 `CombatConfigSO.ParryWindowDurationMax`로 한다** — 특성으로 창을 늘릴 수 있으므로 최대치가 통과해야 한다(4.1).

**즉발형이 더 빡빡하다.** 돌진형은 돌진 시간이 여유로 더해지지만 즉발형은 `WindupDuration` 하나로 버텨야 한다. 즉발형을 패링 가능으로 만들 때는 **`WindupDuration ≥ 0.8초`** 를 지킨다.

`ParryWindowDuration`은 `EnemySO.StatEntry`에 잡귀별로, `IParryable.ParryWindowDuration`으로 노출된다. `0`이면 **패링 불가 공격**이고 `OpenParryWindow`가 즉시 `false`를 돌려준다 — 이것은 결함이 아니라 정상 경로다.

#### 3.5.2 예고 4종 — 창귀와 보스가 공유하는 원형

windup 값은 잡귀에서 확정하고 보스가 그대로 가져다 쓴다.

windup 값은 **잡귀 에셋에서 이미 확정돼 있고**(`EnemySO.asset` 실측) 보스가 그대로 가져다 쓴다.

| # | 원형 | windup | 도형 | 잡귀 | 보스 패턴 | 패링 |
|---|---|---:|---|---|---|---|
| **T1** | **직선 돌진** | **1.5초** | 선 — 폭 `Radius`, 길이 `Distance` | **창귀** (`ParryWindowDuration 0.32`) | `TargetedDash` | **가능** |
| **T2** | **부채꼴 근접** | **1.2초** | 부채꼴 — 반경 `Distance`, 반각 `HalfAngleDegrees` | **허깨비** (`0` = 불가) | `MeleeArc` | 보스만 가능 |
| **T3** | **지목 원형** | **2.0초** | 원 — 중심 `Origin`, 반경 `Radius` | 없음 (보스 신규) | `MarkedCircle` | **불가** |
| **T4** | **원거리 · 전방 원뿔** | **0.8초** | 원뿔 — 길이 `Distance`, 반각 `HalfAngleDegrees` | **액귀** (`0` = 불가) | `ConeBurst` | **불가** |

**잡귀 중 패링 가능한 것은 창귀뿐이다.** 허깨비·액귀는 `ParryWindowDuration = 0`이라 창이 열리지 않는다 — 결함이 아니라 설계다. **창귀가 패링 교보재이고, 그 1.5초가 보스 `TargetedDash`로 그대로 전이된다.**

**T3(지목)은 패링 불가로 고정한다.** 피하는 패턴이고, 패링으로 무효화되면 "지목당했다"는 압박 자체가 사라진다. 대신 [보스 설계](BOSS_DESIGN.md)대로 피해를 **120**(10레벨 최대 HP 180의 2/3)으로 둬 즉사시키지 않는다.

**T4(0.8초)를 보스에서 패링 가능으로 켜지 않는다.** 3.5.1의 즉발형 하한과 같아 여유가 0이다. `ConeBurst`는 [보스 설계](../class-identity-and-boss-2026-09-03.md)가 "0.8초라 못 끊는 패턴"으로 규정한 것이라, **바닥 점멸이 0.8초 전체를 채워** 회피로만 대응하게 한다.

> **페이즈마다 패링 가능 패턴이 최소 하나여야 한다**(`BossPatternSO` 검증). T3·T4가 불가이므로 **각 페이즈는 T1 또는 T2를 반드시 포함한다.**

#### 3.5.3 `AttackTelegraphCue`에 도형 정보를 넣는다

3.4의 초안 struct는 `Origin`·`Direction`·`Distance`·`Radius`만 가져서 **부채꼴과 원뿔을 그릴 수 없고, 선인지 원인지 구분할 수도 없다.** 뷰가 추측하게 두지 않는다.

```csharp
public enum AttackTelegraphShape : byte
{
    Line   = 0,  // T1 — Origin에서 Direction으로 Distance, 폭 Radius
    Arc    = 1,  // T2 — Origin 중심, 반경 Distance, Direction 기준 ±HalfAngleDegrees
    Circle = 2,  // T3 — Origin 중심, 반경 Radius. Direction 무시
    Cone   = 3,  // T4 — Origin에서 Direction으로 Distance, ±HalfAngleDegrees
}

public struct AttackTelegraphCue
{
    public ParryWindowKey        Key;
    public AttackTelegraphShape  Shape;              // 신설
    public Vector3               Origin;
    public Vector3               Direction;          // Circle이면 무시
    public float                 Distance;
    public float                 Radius;             // Line=폭, Circle=반경, 그 외 0
    public float                 HalfAngleDegrees;   // 신설. Arc·Cone만 사용, 그 외 0
    public double                TelegraphStartServerTime;
    public double                ChargeStartServerTime;
    public double                ImpactServerTime;
}
```

**`Shape`에 따라 어느 필드를 읽는지 표로 고정한다.** 안 쓰는 필드는 `0`으로 채우고, 뷰는 `Shape`로만 분기한다.

| `Shape` | `Origin` | `Direction` | `Distance` | `Radius` | `HalfAngleDegrees` |
|---|---|---|---|---|---|
| `Line` | 시작점 | 진행 방향 | 길이 | **폭** | 0 |
| `Arc` | 중심 | 정면 | **반경** | 0 | **반각** |
| `Circle` | 중심 | (무시) | 0 | **반경** | 0 |
| `Cone` | 꼭짓점 | 정면 | 길이 | 0 | **반각** |

`enum`은 `: byte`이고 **명시값을 유지하며 재배열하지 않는다** — Mirror로 직렬화되기 때문이다.

#### 3.5.4 시각 신호는 4종 모두 같은 문법을 쓴다

도형만 다르고 **읽는 법은 같아야** 학습이 전이된다.

| 시점 | 신호 | 근거 |
|---|---|---|
| `TelegraphStartServerTime` | 도형이 **청록**으로 나타난다 | [시스템 기획 7.1](../01-system-design.md) 색 문법 — 원귀 계열은 여우불 청록 |
| 창 구간 진입 (`windowStart`) | **몸체 발광 pulse가 빨라진다** | 예고 도형이 아니라 **몸에서** 읽어야 시야 밖에서도 보인다 |
| `ChargeStartServerTime` | 도형 밝기 한 단계 상승 | T1만 해당. 나머지는 예고와 발동 사이가 없다 |
| `Impact` | 도형 즉시 소거 + 먼지·타격음 | |
| `Parried` | 도형 즉시 소거 + **다른 색 섬광 + 금속음** | 성공을 색으로 구분한다 |
| `Cancelled` | 도형 즉시 소거, 소리 없음 | 취소는 사건이 아니다 |

**창 구간 진입 신호가 가장 중요하다.** 패링은 0.32초 안에 눌러야 하는데, 예고 도형만 보면 언제 눌러야 하는지 알 수 없다. 몸체 pulse가 그 타이밍을 알려주는 유일한 단서다.

> ✅ **해소 (2026-09-04 오후 재실측).** `PC_Renderer.asset`에 `DecalRendererFeature`가 추가되었다 — **예고 도형을 데칼로 그려도 된다.** 임시 대안(`LineRenderer`·quad·`MaterialPropertyBlock`)으로 우회할 이유가 없어졌다.
> **단, 이 변경은 아직 커밋되지 않았다.** 커밋 전까지는 팀원 둘의 클라이언트에서 여전히 안 보인다 → [BOSS_STAGING 5장](BOSS_STAGING_DESIGN.md).

#### 3.5.5 보스가 이 계약을 쓸 때의 차이

| 항목 | 창귀 | 보스 |
|---|---|---|
| windup 출처 | `EnemySO.StatEntry.WindupDuration` | `BossPatternSO.PatternEntry.TelegraphDuration` |
| 패링 가능 | `ParryWindowDuration > 0` | `PatternEntry.IsParryable` **그리고** `ParryWindowDuration > 0` |
| 실행 단위 | `ChanggwiAttackExecution` | 패턴별 실행 객체 (같은 형태) |
| 동시 대상 | 1명 | **여러 명일 수 있다** |

**보스는 여러 플레이어에게 동시에 창을 열 수 있다.** `ParryController`는 **대상당 활성 창 하나**만 유지하므로(코드 실측), 보스는 대상마다 `OpenParryWindow`를 각각 호출하고 **`ParryWindowKey.WindowId`는 공격자 기준으로 하나만 증가**시킨다 — 같은 공격이면 대상이 달라도 같은 `WindowId`다.

**페이즈마다 `IsParryable`이 최소 하나여야 한다.** 아니면 그 페이즈 동안 차사가 할 일이 없다. `BossPatternSO` 검증기가 이것을 막는다.

## 4. 데이터

새 ScriptableObject는 만들지 않는다. 창귀 한 종의 게임플레이 값은 현재 `EnemySO.StatEntry`가 이미 모두 갖고 있고, 여러 인스턴스가 같은 정적 데이터를 공유하는 용도는 Unity가 설명하는 ScriptableObject의 주 용도와 맞는다. [Unity 6.4 ScriptableObject](https://docs.unity3d.com/6000.4/Documentation/Manual/class-ScriptableObject.html)

### 4.1 `EnemySO.StatEntry` 창귀 후보값

| 필드 이름 | 타입 | 기본값 후보 | 사용 |
|---|---|---:|---|
| `Kind` | `EnemyKind` | `Changgwi` | entry 식별 |
| `AttackPower` | `float` | `14` | 기존 게임 디자인 값 |
| `AggroRange` | `float` | `14 m` | 추적 시작 |
| `AttackRange` | `float` | `6 m` | 돌진 실행 커밋 거리 |
| `WindupDuration` | `float` | ~~`0.65 s`~~ → **`1.5 s`** | 텔레그래프 시작부터 돌진 시작까지. **2026-09-04 에셋 실측 정정** — `EnemySO.asset`의 창귀 값은 1.5이고 후보값 0.65는 반영된 적이 없다. 코드·에셋이 기준이다 |
| `AttackCooldown` | `float` | `1.2 s` | impact 또는 취소 뒤 재행동 대기 |
| `ChargeSpeed` | `float` | `8 m/s` | 서버 NavMeshAgent 돌진 속도 |
| `ChargeDistance` | `float` | `5.5 m` | snapshot 방향의 고정 목적지 거리 |
| `ChargeHitRadius` | `float` | `0.9 m` | impact 시 snapshot 대상 허용 거리; 바닥선 폭은 이 값의 2배 |
| `StaggerDuration` | `float` | `1.4 s` | 패링 성공 경직 |
| `ParryWindowDuration` | `float` | `0.32 s` | `IParryable`이 반환하는 공격별 창 길이 |

후보값은 시작점이며 에셋에서 조정한다. 코드에 복제하지 않는다. `impactTime`은 아래처럼 기존 데이터에서 파생한다.

```text
chargeDuration = ChargeDistance / ChargeSpeed
chargeStartTime = telegraphStartTime + WindupDuration
impactTime = chargeStartTime + chargeDuration
```

데이터 검사 조건은 다음과 같다.

- 모든 시간·거리·속도는 유한수이며 음수가 아니다. `ChargeSpeed`, `ChargeDistance`, `ParryWindowDuration`은 0보다 커야 한다.
- `WindupDuration + ChargeDistance / ChargeSpeed >= ParryWindowDuration + Constants.Network.PARRY_RESOLUTION_LEAD`여야 한다.
- 특성으로 최대 창을 쓸 수 있으므로 출시 전 검사는 우변의 창 길이에 `CombatConfigSO.ParryWindowDurationMax`를 사용한다. 현재 후보는 `1.3375 >= 0.66`으로 통과한다.
- `AttackRange <= AggroRange`, `ChargeHitRadius <= ChargeDistance`여야 한다.
- `OpenParryWindow`는 `telegraphStartTime`, 계산한 `windowStart`·`windowEnd`, `impactTime`이 모두 유한수이고 `telegraphStartTime <= windowStart < windowEnd < impactTime`인지 다시 검사한다. 공격 데이터 부트 검사는 인터페이스 경계 검사를 대신하지 않는다.
- 등록 순간 `PlayerActionGate.CanAct(phase, lifeState)`와 `PlayerWeapon.HasWeapon && Weapon == Sword`를 확인한다. 활·방울은 정상적인 `false`, 잘못된 시각·phase·생명 상태는 진단 가능한 거부로 취급한다.
- Editor 검사만 믿지 않고 `GameDB` 부트 검사에서도 잘못된 창귀 entry를 오류로 보고한다. Unity 공식 문서도 `OnValidate`를 Inspector 값 검증용 Editor 콜백으로 한정하므로 런타임 부트 검사를 함께 둔다. [Unity 6.4 `OnValidate`](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/MonoBehaviour.OnValidate.html)

색·머티리얼·오디오 참조는 밸런스가 아니므로 `EnemySO`에 넣지 않는다. `ChanggwiTelegraphView`의 프리팹 직렬화 필드로 두고, 길이·폭·진행 시간만 위 gameplay 데이터에서 파생한다.

### 4.2 서버 런타임 창 레코드

밸런스 데이터와 별개로 `ParryController`는 아래 두 개의 짧은 서버 상태만 가진다.

```text
ActiveParryWindow
  Key, StartTime, EndTime, ImpactTime, Source, ClientWindowClosed

ConsumedWindowTombstone
  Key, ImpactTime
```

`EndTime`까지는 신규 입력 시각이 허용되며, 그 뒤에는 클라이언트 표시를 닫아도 이미 전송된 request가 도착할 수 있다. 따라서 active 레코드의 자연 폐기 기준은 `ImpactTime`이다. 성공 소비 후에는 같은 key 중복을 구분하기 위해 tombstone만 `ImpactTime`까지 남긴다. 새 창 등록은 active 슬롯을 교체하지만 소비 tombstone을 지우지 않는다. 서버 수신 시각이 impact와 같거나 늦은 request에는 입력 시각 clamp나 `ParryJudge`를 실행하지 않는다.

## 5. 흐름

### 5.1 정상·성공 흐름

1. `EnemyController.ServerTick`이 `NetworkTime.time`을 한 번 읽어 `ChanggwiBrain.Tick(deltaTime, now)`에 넘긴다.
2. `EnemyController.RefreshTarget`은 `Physics.OverlapSphereNonAlloc`이 채운 각 자식 collider를 부모 `NetworkIdentity` 루트로 올리고, 그 루트의 `IDamageable`과 `Transform`을 확인한 뒤 `netId`로 중복 제거한다. `ChanggwiBrain`이 `Chase → Telegraph`로 들어갈 때 이 정규화된 현재 `Target` 루트를 `_attackTarget`으로 고정한다. 이 실행 중 다음 추적 대상이 바뀌어도 `_attackTarget`은 바뀌지 않는다.
3. 같은 전이 안에서 origin·방향·목적지·`telegraphStartTime`·`chargeStartTime`·`impactTime`을 한 번 확정하고, 0을 건너뛴 다음 `WindowId`로 `ParryWindowKey(netId, windowId)`를 만든다. 현재 코드처럼 windup 끝에 live Target을 다시 보고 방향을 바꾸지 않는다.
4. `_attackTarget` 네트워크 루트에서 `IDamageable`과 `IParryWindowTarget`을 한 번만 찾고 `ChanggwiAttackExecution`에 캐시한다. 이후 패링 source는 Brain의 `this`가 아니라 이 실행 객체다.
5. 검 대상이면 `OpenParryWindow(execution, key, telegraphStartTime, impactTime)`가 `true`다. 활·방울이면 `false`지만 공격은 정상 진행한다. 같은 대상에 이미 다른 창귀 창이 있으면 새로 수락된 등록이 그 active를 exact-key close 후 교체하며, 앞 공격은 계속되지만 더 이상 패링할 수 없다.
6. Brain은 snapshot·active execution·window 결과를 먼저 커밋한 뒤 `AttackBegan(cue)`를 발생시킨다. `EnemyController` handler가 같은 서버 틱에 `RpcBeginAttackTelegraph(cue)`를 보낸다. 각 클라이언트는 cue의 절대 시각으로 늦게 받은 만큼 진행률을 따라잡는다.
7. `chargeStartTime <= now < impactTime`이면 Brain 상태만 `Charge`가 된다. Controller의 기존 이동 적용부가 snapshot 목적지와 `ChargeSpeed`를 agent에 적용한다. 거리 접촉으로 공격을 조기 확정하지 않는다.
8. 검 소유자가 우클릭하면 `CmdParry`가 서버에서 request ID, 의존 컴포넌트, 행동 자격, active/tombstone key, 서버 수신 마감, 입력 시각, 자원을 순서대로 검증한다.
9. 입력 시각이 `[windowStart, windowEnd]`이고 `serverReceivedAt < impactTime`이면, request가 windowEnd 뒤 처리돼도 성공할 수 있다. `serverReceivedAt >= impactTime`이면 입력 시각과 무관하게 거부한다.
10. 성공이면 `ParryController`가 active를 먼저 소비해 `(key, impactTime)` tombstone을 남기고, 클라이언트 창 close·게이지·무적을 반영한 뒤 `execution.OnParried(playerNetId)`를 정확히 한 번 호출한다.
11. `Owner.TryParry(execution, playerNetId)`는 현재 실행 참조와 key, `Telegraph`/`Charge`, 미해결 상태를 모두 확인한다. Brain이 취소·`Stagger` deadline·window close를 먼저 커밋하고 `AttackResolved(execution, Parried)`를 발생시키면, Controller가 agent를 멈춘 뒤 `RpcEndAttackTelegraph`를 보낸다. 중복 콜백은 아무 상태나 보상을 바꾸지 않는다.

### 5.2 미패링·빗나감 흐름

1. 패링하지 않았거나 서버 판정이 실패하면 실행은 계속된다.
2. 활성 실행 Tick은 누적 timer나 현재 state의 한 단계 전이보다 deadline을 우선한다. `now >= impactTime`이면 Telegraph에서 Charge를 거치지 않았더라도 같은 Tick에 impact를 해결하고, 그보다 이르면 `now >= chargeStartTime`일 때만 Charge로 본다. 긴 hitch가 두 경계를 한꺼번에 넘어도 피해가 한 틱 더 밀리지 않는다.
3. `now >= impactTime`인 첫 서버 틱에 Brain이 `_impactCommitted = true`, `_isResolved = true`, `Recovery`를 먼저 커밋하고 창을 exact-key close한 뒤 `AttackResolved(execution, Impact)`를 정확히 한 번 발생시킨다.
4. Controller handler는 정규화된 target `NetworkIdentity`가 아직 spawn 상태인지, 캐시한 `IDamageable`이 유효하고 살아 있는지, 실제 창귀 위치와 target root 거리가 `ChargeHitRadius` 이하인지 다시 검사한다. 모두 만족할 때만 snapshot 대상에 `TakeDamage(new DamageInfo(...))`를 한 번 호출한다. live `_brain.Target`을 사용하는 기존 generic `ExecuteAttack` 분기는 창귀에 사용하지 않는다.
5. 피해 또는 빗나감 처리가 끝난 뒤 Controller가 `RpcEndAttackTelegraph(key, Impact)`를 보낸다. impact 뒤 도착한 패링으로 피해를 되돌리는 경로는 없다.

활성 실행의 핵심 분기는 아래 우선순서를 유지한다.

```text
if target invalid: Cancel
else if now >= impactTime: ResolveImpactOnce
else if now >= chargeStartTime: Charge
else: Telegraph
```

### 5.3 실패·거부·취소 경로

| 상황 | 서버 처리 | 표현 처리 |
|---|---|---|
| 대상에 `IParryWindowTarget` 없음 | 공격은 진행, 검 플레이어라면 설정 오류 로그 | 텔레그래프는 그대로 표시 |
| `OpenParryWindow`가 비유한수·짧은 텔레그래프·과거 impact로 거부 | 공격은 비패링으로 진행, 데이터 오류 로그 | 텔레그래프는 그대로 표시 |
| 대상이 활·방울 | 정상적인 `false`, 오류 아님 | 회피용 텔레그래프만 표시 |
| 대상 사망·despawn | 캐시한 창을 즉시 닫고 실행 취소, 피해 0 | `Cancelled`로 즉시 제거 |
| 창귀 사망·despawn·`OnStopServer` | destroy/이벤트 해제 전에 active execution을 멱등 cancel·close | 적 파괴와 함께 뷰 제거 |
| 목적지 도달 실패·NavMesh 경로 문제 | impact까지 이동한 실제 위치로 거리 검사, 닿지 않았으면 피해 0 | 예정 시각에 종료 |
| 입력 시각은 window 안, `windowEnd < serverReceivedAt < impactTime` | 보존한 active 레코드로 정상 판정; 성공 시 실행 취소 | 성공/실패 결과와 exact-key close |
| `serverReceivedAt >= impactTime` | AI callback 선후와 무관하게 판정 마감 거부; rollback 없음 | 실패 결과; impact 표현 유지 |
| 패링 성공과 impact가 같은 Unity frame | `serverReceivedAt < impactTime`인 성공만 Brain 취소 플래그를 먼저 커밋; 그 외는 impact | `Parried` 또는 `Impact` 하나만 표시 |
| 이전 실행의 늦은 stop RPC | view가 key 일치 여부를 확인해 새 실행을 끄지 않음 | 무시 |
| 교체된 이전 창귀가 늦게 `CloseParryWindow(oldKey)` 호출 | 서버 active key와 다르므로 새 창은 건드리지 않음 | 이전 close는 이미 교체 시 전달됐으므로 무시 |
| 이전 실행 source의 늦은 `OnParried` | owner Brain의 실행 참조/key 불일치로 무시 | 변화 없음 |

순서 경합을 없애기 위해 parry와 impact 모두 같은 active execution 참조·key·`_isCancelled`·`_isResolved`를 검사한다. `CloseParryWindow`와 view `Stop`은 같은 키로 여러 번 호출해도 안전해야 한다. Command를 `TICK_PRIORITY_AI`보다 먼저 실행된다고 가정하지 않으며, 서버 수신 시각의 strict impact 경계와 먼저 커밋된 실행 상태만 결과를 결정한다.

## 6. 단계별 구현 순서

1. **P0 패링 선행 게이트 — 8~12시간.** `ParryController`를 대상당 단일 `ActiveParryWindow`로 바꾸고 `ImpactTime`, 소비 tombstone, exact-key 교체/close, windowEnd 표시 종료와 impact 폐기 분리, `serverReceivedAt < impactTime`, 유한수·phase·생명·Sword 등록 검사를 구현한다. active 교체·늦은 입력·impact 경계 테스트가 통과하기 전에는 아래 창귀 연결을 시작하지 않는다.
2. **canonical target 정규화 — 3~4시간.** `EnemyController.RefreshTarget`을 재사용 NonAlloc buffer, 부모 `NetworkIdentity` 루트, 루트 `IDamageable`, `netId` 중복 제거로 바꾸고 child collider·despawn 테스트를 추가한다.
3. **서버 실행·절대 타임라인 — 7~9시간.** `EnemyBrainBase.Tick`에 서버 시각을 전달하고, `ChanggwiAttackExecution`, Telegraph 진입 snapshot, direct deadline 비교, open/close, parry/impact 멱등 상태를 넣는다. Brain typed event와 Controller의 agent → snapshot 피해 → RPC 순서를 연결하고 창귀를 generic `ShouldAttackNow` 피해 경로에서 제외한다. 여기까지 하면 M0에서 패링 → 피해 없음 → `Stagger`, 미패링 → impact 최대 한 번이 동작한다.
4. **창귀 데이터·부트 검사 — 2~3시간.** `EnemySO` 창귀 값과 최소 타임라인 식, 모든 유한수·범위 조건을 부트에서 검사하고 잘못된 entry는 명시적으로 실패시킨다.
5. **애니메이션 없는 텔레그래프 — 4~5시간.** `AttackTelegraphCue`, `IAttackTelegraphView`, Controller RPC, `ChanggwiTelegraphView`의 재사용 바닥선·발광·소리를 연결한다. 여기까지 하면 M1에서 호스트와 원격 클라이언트가 같은 방향·시각을 읽는다.
6. **수명·경합 보강 — 3~4시간.** target/attacker 사망·despawn, `OnStopServer`, 실행 source 수명, 늦은 RPC/close/callback, hitch가 charge와 impact를 함께 넘는 경로를 자동/수동 테스트한다.
7. **M2/M3 검증과 수치 조정 — 6~8시간.** 3인 역할, 두 창귀의 같은 Sword 대상 교체, 0/50/150ms 지연, windowEnd/impact 경계를 반복한다. P0 게이트와 M2/M3가 끝나기 전에는 완료로 표시하지 않는다.

보스·공격 애니메이션·다중 창은 이 수직 슬라이스가 M3를 통과한 뒤 별도 항목으로 착수한다.

## 7. 검증

### 7.1 M0 — 정적·단일 호스트

| 관찰 항목 | 합격 신호 |
|---|---|
| key 증가 | 같은 창귀 100회 실행에서 0 없음, 이전 ID 재사용 없음 |
| 타임라인 경계 | `windowStart < windowEnd < impactTime`, 데이터 최소식 위반 0 |
| P0 active/tombstone | 서버 active 레코드 최대 1개; 성공 소비 뒤 active 0, tombstone 1; tombstone은 새 등록에도 남고 원래 impact 뒤 제거 |
| 늦게 도착한 유효 입력 | 입력 시각은 window 안이고 `windowEnd < serverReceivedAt < impactTime`인 20회가 정상 판정 |
| impact 수신 마감 | `serverReceivedAt == impactTime`과 그 이후 요청은 20회 모두 거부; 피해 rollback 0 |
| 패링 성공 | 20회 모두 해당 `ChanggwiAttackExecution.OnParried` 1회, 피해 0, `Stagger` 진입 |
| 미패링 | 20회 모두 impact 이전 피해 0, impact에 최대 1회 피해 |
| 회피 | impact 때 반경 밖이면 피해 0 |
| 중복 콜백 | 같은 실행 `OnParried` 2회 호출에도 close·경직·보상 각 1회 이하 |
| 실행 source 격리 | 이전 execution의 늦은 callback이 다음 active key·상태·HP를 바꾸는 횟수 0 |
| hitch catch-up | Telegraph 중 전체 charge 구간보다 긴 hitch 뒤 첫 Tick에서 impact 1회 또는 cancel 1회; 중간 조기 피해와 한 틱 추가 지연 0 |
| 정리 | 대상/공격자 파괴 후 stale window 0 |
| 자식 collider 정규화 | 몸통·발·무기 collider가 함께 잡혀도 플레이어 후보와 `TakeDamage`는 각 1개; snapshot `Transform`은 `NetworkIdentity` 루트 |
| 대상당 단일 창 | 서로 다른 창귀 두 개의 수락 순서가 A→B이면 active key는 B 하나, A는 패링 불가로 정상 진행, A의 늦은 request/close 뒤에도 B 유지 |

### 7.2 M1 — 호스트 + 클라이언트 1, 같은 PC

- 두 화면에서 바닥 돌진선 방향·길이와 종료 이유가 일치한다.
- RPC 수신이 한두 프레임 늦어도 `NetworkTime.time` 기반 진행률로 pulse가 즉시 따라잡는다.
- 늦은 이전 stop이 다음 공격의 선을 끄는 경우가 20회 중 0회다.
- 검 소유자에게만 `TargetOpenParryWindow`가 도착하고, windowEnd 표시 종료 또는 소비 시 exact-key close가 도착한다. 서버 resolution 레코드는 impact까지 필요한 경우 유지된다.

### 7.3 M2 — 호스트 + 클라이언트 2, 같은 PC

- 무기별 대상을 각각 20회 잡는다. 검 대상은 창이 열리고, 활·방울 대상은 창 없이 회피 텔레그래프만 보인다.
- 세 화면의 창귀 상태·HP 결과가 60회 모두 같다.
- 검이 성공하면 세 화면 모두 돌진 중단을 보고, 서버의 대상 HP는 변하지 않으며 게이지는 정확히 한 번 증가한다.
- 두 창귀가 같은 검을 연속/동시로 노리는 20회에서 **서버 수락 순서상 마지막 창 하나만** 활성이다. 이전 창 close 뒤 새 창 open이 오고, 이전 key의 지연/임의 request는 거부되며, 앞 공격은 패링 불가 상태로 자기 impact를 정상 해결한다. 이전 창귀의 늦은 close가 새 창을 지우는 경우와 `(AttackerNetId, WindowId)` 충돌은 각각 0회다.
- 같은 frame에 Command 처리와 AI impact가 겹치는 20회에서 `serverReceivedAt < impactTime`이면 `Parried`만, 같거나 늦으면 `Impact`만 세 화면에 보인다.
- 대상 사망, 창귀 사망, disconnect를 각 10회 반복해 stale 창·사후 피해가 0회다.

### 7.4 M3 — 서로 다른 PC 3대

- M0의 P0 active/tombstone·수신 마감 테스트가 통과하지 않으면 M3를 시작하거나 완료로 표시하지 않는다.
- 0ms, 30~50ms, 150ms 조건에서 각 20회 패링하고 호스트/두 클라이언트의 성공·실패, impact 피해, 경직 결과가 일치한다. 특히 window 안에서 보낸 입력이 windowEnd 뒤지만 impact 전에 서버에 도착한 경우를 별도 집계하고 정상 판정되는지 확인한다.
- impact 뒤 도착한 요청은 세 지연 조건에서 모두 거부되고 이미 적용한 피해가 되돌아가지 않는다.
- 텔레그래프를 봤지만 RPC 지연 때문에 창 시작 전에 이미 지나가 보인 사례 수를 기록한다. P0 수신 마감과 RPC 순서가 맞음을 먼저 확인한 뒤, 150ms에서만 반복되면 `EnemySO.WindupDuration`을 조정한다.
- 창귀를 반복 spawn/despawn하는 40분 런 뒤에도 각 대상 `ParryController`의 서버 key 상태는 active 1개+tombstone 1개 이하이고, 공격자별 이력 컬렉션은 존재하지 않는다.
- GTX 1060 3GB 호스트에서 창귀가 포함된 동시 잡귀 18~22마리 구간을 측정한다. 텔레그래프 활성/비활성 사이 GC Alloc 증가 0 B/frame, 60 FPS 목표를 확인한다.

시각 결과는 세 화면 캡처가 없으면 `미검증`으로 기록한다. 네트워크 기능이므로 M0 성공만으로 완료라 하지 않는다.

## 8. 대안과 선택 근거

### 채택: P0 단일 슬롯 + 실행 단위 source + 서버 절대 타임라인 + RPC 표현

Mirror 공식 문서는 NPC를 기본 서버 권위 대상으로 설명하고, 일회성 서버→클라이언트 동작에 ClientRpc, 특정 소유자 응답에 TargetRpc를 제공한다. [Authority](https://mirror-networking.gitbook.io/docs/manual/guides/authority), [Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions). 또한 `NetworkTime.time`은 클라이언트와 서버에서 공유하는 `double` 시각이므로 텔레그래프와 impact를 같은 축에 놓을 수 있다. [Time Synchronization](https://mirror-networking.gitbook.io/docs/manual/guides/time-sync)

이 방식은 현재 `EnemyController : NetworkBehaviour`, `ParryController : IParryWindowTarget`, `IParryable.OnParried(uint)`, `PARRY_RESOLUTION_LEAD`를 재사용한다. 장수명 Brain 대신 공격 실행 객체가 `IParryable`을 구현하므로 key 없는 기존 콜백도 어느 실행을 취소할지 분명하다. 대상당 단일 active와 impact까지의 resolution 레코드가 클라이언트 단일 키 및 P0 계약과 일치한다. 3인 리슨 서버에서 공격 하나당 시작/종료 RPC 두 번은 작고, 텔레그래프용 네트워크 프리팹·새 SyncVar·새 매니저가 없다. 데디케이티드 전환 시에도 `[Server]` 판정이 그대로 남는다.

### 검토했지만 고르지 않은 방식

| 대안 | 보통 쓰이는 이유 | 고르지 않은 이유 |
|---|---|---|
| 애니메이션 이벤트에서 창 열기·피해 적용 | 모션과 타격 프레임을 맞추기 쉽다 | 현재 공격 클립이 없고, 클라이언트 Animator 이벤트가 권위 판정을 열면 사람마다 결과가 갈린다. 나중에 클립은 표현 시각을 따르기만 한다 |
| `ChanggwiBrain`이 `ParryController`를 직접 찾기 | 구현 줄 수가 가장 적다 | `AI → Combat` 직접 참조가 생겨 현재 파트 경계를 깨고 보스 재사용도 막는다. 이미 만든 `IParryWindowTarget`의 목적을 무효화한다 |
| 장수명 `ChanggwiBrain` 자체를 `IParryable` source로 재사용 | 새 타입이 필요 없다 | `OnParried(uint)`에 key가 없어 이전 실행과 현재 실행을 콜백 인자만으로 구별할 수 없다. 실행 단위 adapter가 기존 서명을 유지하면서 모호성을 없앤다 |
| 공격자별 서버 active dictionary 유지 | 두 공격을 모두 패링 가능하게 만들기 쉽다 | 클라이언트는 키 하나만 보관하고 P0도 대상당 마지막 등록 하나만 허용한다. 보이지 않는 이전 key 선택 패링과 서버/클라이언트 불일치가 생긴다 |
| 텔레그래프 전용 네트워크 프리팹 스폰 | late join과 독립 수명이 쉽다 | 런 중 입장이 잠기고 cue 수명이 약 1초다. 적 하나마다 스폰/파괴와 등록 프리팹을 추가하는 비용이 이득보다 크다 |
| Brain 상태 전체를 `[SyncVar]`로 동기화 | 새 관찰자가 현재 상태를 복원하기 쉽다 | 현재는 late join이 없고, state만 보내도 방향·impact 시각이 별도로 필요하다. 시작/종료 RPC가 더 직접적이다 |
| `deltaTime` 누적으로 창과 impact 계산 | 현재 코드 변경이 적다 | 프레임 오버슈트 때문에 등록한 impact와 실제 피해 시각이 어긋나며, 0.18초 판정 여유의 의미가 무너진다 |
| Command가 AI priority보다 먼저라고 가정 | 별도 inbox 없이 같은 frame 경합을 설명하기 쉽다 | Mirror Command는 `AuthorityTickScheduler` callback이 아니다. strict `serverReceivedAt < impactTime`과 실행 commit 상태가 실제 권위 경계다 |
| 한 번에 범용 몬스터 공격 프레임워크 구축 | 이후 보스·다른 잡귀도 재사용 가능 | 보스는 아직 별도 설계 단계이고 창귀 한 패턴이 가장 급하다. 이번에는 cue 계약만 범용으로 두고 실행기는 창귀에 한정한다 |

## 9. 비용과 위험

**비용: 중~대, 총 33~45 인시.** P0 패링 선행 게이트 8~12시간, canonical target 3~4시간, 실행 객체·절대 타임라인·Controller 이벤트 7~9시간, 데이터/부트 검사 2~3시간, 텔레그래프 4~5시간, 수명·경합 보강 3~4시간, M2/M3 6~8시간이다. AI 담당과 전투 담당이 P0 게이트와 실행 경계를 먼저 함께 잠그고, 팀장이 프리팹·아트와 3인 통합을 맡으면 약 4~6 작업일 범위다.

주요 위험과 통제는 다음과 같다.

- **P0 게이트 실패:** 현재 복수 attacker active, `EndTime` 만료, `ImpactTime` 부재 상태에서는 150ms 입력과 두 창귀 결과를 신뢰할 수 없다. 단일 active·impact 보존·tombstone·수신 마감 테스트가 실패하면 창귀 수직 슬라이스와 M3를 중단한다. 전체 request/result DTO와 상세 결과 코드는 후속일 수 있지만 이 네 상태 계약은 후속이 아니다.
- **순수 C# 실행 source 수명:** `ChanggwiAttackExecution`은 `UnityEngine.Object`가 아니어서 `ParryWindow.HasValidSource()`가 공격자 파괴를 자동 감지하지 못한다. target/attacker 무효, death, `OnStopServer`, 모든 실행 해결에서 explicit exact-key close가 필수이며 tombstone impact 전에는 source를 파기하거나 재사용하지 않는다.
- **Command/impact 경계:** Command는 AI scheduler priority에 포함되지 않는다. 구현이나 테스트가 frame 호출 순서를 성공 조건으로 삼지 않게 하고, `serverReceivedAt < impactTime`과 먼저 커밋된 실행 상태를 로그에 함께 남긴다.
- **Brain/Controller 책임 회귀:** Brain event handler에서 generic `ShouldAttackNow` 피해 경로까지 남기면 이중 피해가 난다. 창귀는 실행 해결 이벤트 하나만 사용하고, Controller의 agent 정지 → snapshot 피해 → end RPC 순서를 테스트로 고정한다.
- **공유 무적 bool:** 현재 `PlayerStatus.SetInvincible(bool)`를 구르기와 패링이 함께 써 조기 해제 위험이 있다. 창 연결 자체와 별개지만 M2에서 함께 노출될 수 있으므로 알려진 선행 리팩터링으로 추적한다.
- **canonical target 누락:** prefab의 child collider 배치가 바뀌어도 root `NetworkIdentity`·`IDamageable` 검증과 `netId` 중복 제거가 유지돼야 한다. NonAlloc buffer 포화는 조용히 대상을 누락하지 않도록 카운터와 경고로 관찰한다.
- **NavMesh 이동과 시각선 오차:** 텔레그래프는 고정 방향이고 실제 agent가 장애물에 막힐 수 있다. 서버는 실제 위치 거리로 피해를 판정해 부당한 명중을 막고, 경로 문제가 자주 보일 때만 돌진 전용 이동 방식을 별도 검토한다.
- **텔레그래프 가시성:** 색각·지형 재질에 따라 붉은 선만으로 부족할 수 있다. 선 + 몸체 pulse + 소리의 서로 다른 신호 세 개를 쓰고 M2 캡처로 확인한다.
- **스코프 확장:** 이 cue를 보스 범용 패턴 그래프로 확장하거나 애니메이션 상태 머신을 함께 만들지 않는다. 창귀 M3 통과가 다음 확장의 게이트다.
