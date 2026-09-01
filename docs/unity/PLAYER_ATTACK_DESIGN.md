# PlayerAttack — 직업별 공격 공통 계약 설계

> 상태: 설계 확정본, 구현 전  
> 출처: 2026-09-01 외부 소스 감사와 함께 받은 설계 산출물. 저장소 문서로 편입했다  
> 관련: [프로젝트 구조 설계서](../04-architecture.md) · [네트워크 권위](NETWORK_AUTHORITY.md) · [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)

> 기준 코드: 2026-09-01 제공 소스  
> 결론: 플레이어 프리팹에는 **신규** `PlayerAttack : NetworkBehaviour` 하나만 추가하고, 검·활·방울은 Mirror를 모르는 **신규** `IWeaponAbility`/`IAbilityRuntime` 실행기로 분리한다. 클라이언트는 조준 의도만 보내며 무기·피해·자원·치명·명중은 서버가 현재 상태에서 산출한다. 공격력은 04 문서가 소유하는 `WeaponCombatSnapshot`을 최종 원본으로 삼고, 그 구현 전에는 기존 1인자 `PlayerWeapon.ServerSetInitialWeapon(WeaponKind)` 안에서만 결정적 임시값을 만든다.

## 1. 목표와 범위

이번에 만드는 것은 한 플레이어 프리팹에서 런 시작 때 서버가 배분한 `WeaponKind`에 따라 기본 공격 실행만 바뀌는 공통 경로다.

```text
좌클릭
  → PlayerAttack의 단일 Command
  → 서버 PlayerWeapon으로 Sword/Bow/Bell 선택
  → 공통 게이트·자원·쿨다운·치명 snapshot
  → IWeaponAbility가 무기별 IAbilityRuntime 생성
  → 서버 틱에서 근접/투사체/원형 pulse 실행
  → IDamageable + DamageInfo 또는 서버 화살 spawn
  → RPC/SyncVar로 표현
```

완료 조건은 다음과 같다.

- 클라이언트 공격 Command 진입점은 `PlayerAttack.CmdRequestAttack` 하나뿐이다.
- 클라이언트 request에는 무기 종류, 공격력, 자원 비용, 치명 배율, 특성 슬롯이 없다.
- 서버가 현재 `PlayerWeapon`을 읽어 `SwordAbility`, `BowAbility`, `BellAbility` 중 하나를 고른다.
- 세 실행기는 같은 `IWeaponAbility`/`IAbilityRuntime` 계약을 따르고 Mirror API를 호출하지 않는다.
- 검·방울 판정은 기존 `IDamageable`/`DamageInfo`를 사용하고, 활은 `ArrowLauncher`만 `NetworkServer.Spawn`을 호출한다.
- **현재 존재하는** `ParryGauge`의 API를 보강해 유효한 검 공격 시작 시 한 번만 소비한 치명 배율이 그 실행 전체에 고정된다.
- 구르기·패링·공격 세 Command가 서로의 진행 상태를 검사하고, 같은 서버 프레임에도 둘 이상을 동시에 commit하지 않는다.
- 서버 화살은 절대 만료 시각, canonical 대상별 1회 hit, 명시한 관통 횟수, 장애물 충돌, 단 한 번의 despawn 계약을 만족한다.

이번 범위에 포함하지 않는 것은 콤보 트리, 차지 공격, 복수 입력 슬롯, 클라이언트 hit 판정, 과거 위치 rewind, 범용 스킬 그래프, ECS/DI 컨테이너, 전체 특성·인벤토리 구현, 실제 공격 애니메이션 조달이다. 특히 04 문서가 소유하는 `WeaponCombatSnapshot`, `IWeaponLoadoutTarget`, `EquipmentInstance`, `EquipmentGenerator`의 본 구현은 여기서 중복 구현·산정하지 않는다. 연사·관통·치유·버프는 request 종류가 아니라 미래 `TalentController`가 서버에서 만드는 modifier로 연결할 경계만 남기며, 그 구현도 이번 완료선과 비용에서 제외한다.

방울의 최소 기본 공격은 **자기 주변 피해 pulse 한 종류**로 둔다. 치유·버프는 특성 시스템이 들어오기 전까지 실행하지 않는다. 이 선택은 무녀의 최종 밸런스 확정이 아니라 세 직업 공통 계약을 M2에서 검증하기 위한 프로토타입 수직 슬라이스다.

## 2. 권위 경계

Mirror 공식 문서에서 Command는 기본적으로 소유한 플레이어 오브젝트에서만 보낼 수 있고 서버에서 실행되며, SyncVar는 서버에서 클라이언트로 동기화된다. 이 구조에 맞춰 `PlayerAttack`은 소유 플레이어의 기본 authority Command를 사용하고 `requiresAuthority = false`를 쓰지 않는다. [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions), [Mirror SyncVars](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization/syncvars)

| 값 · 판정 | 서버 | 클라이언트 | 근거 |
|---|---|---|---|
| 좌클릭과 조준 의도 | request ID, 방향, lock-on 후보 ID를 수신·검증 | 소유자만 입력을 읽고 request 생성 | 입력 사실만 클라이언트에 있다 |
| 현재 무기/직업 | 기존 `PlayerWeapon`의 `_weapon`/`_hasWeapon` SyncVar 사용 | SyncVar를 외형·UI에 읽기만 | 클라이언트가 보낸 무기 값은 없다 |
| 기본 공격 config | 신규 `GameDB.WeaponAbility`에서 선택 | 같은 빌드의 에셋으로 표현 시간만 읽을 수 있음 | 판정 원본은 서버가 읽는다 |
| 장착 공격력 | 최종적으로 04 소유 `PlayerWeapon.CombatSnapshot.AttackPower`; 선행 구현 전에는 기존 1인자 초기화 API 내부 bridge | 툴팁용 동기 값만 읽음 | 피해 결과가 달라지는 값이며 request에는 없다 |
| phase·생명·행동 잠금 | `RunManager`, `PlayerStatus`, `PlayerRollController`, `ParryController`, `PlayerAttack` 상태로 판정 | 예측 모션을 취소할 수 있도록 결과 수신 | `PlayerActionGate` 재사용 |
| 스태미나·신력 차감 | `PlayerStatus.TrySpendStamina/Spirit` | 기존 SyncVar로 UI 표현 | 서버 원자적 차감 |
| 치명 게이지 소비·배율 추첨 | 검의 유효 공격 commit 뒤 `ParryGauge`가 한 번 수행 | 결과 배율로 연출만 | 다음 유효 공격 1회 규칙 |
| 검·방울 hit query와 피해 | 서버 물리 world에서 수행 후 `IDamageable.TakeDamage` | 피격 이펙트만 표현 | AI와 HP 결과가 갈리면 안 된다 |
| 화살 생성·궤적·명중·수명 | 신규 `ArrowLauncher`/`ArrowProjectile`가 서버 spawn·tick·despawn | spawn된 화살과 결과를 렌더링 | Mirror는 런타임 네트워크 오브젝트를 서버가 `NetworkServer.Spawn`하도록 규정한다. [Mirror Network Identity](https://mirror-networking.gitbook.io/docs/manual/components/network-identity) |
| 공격 시작 표현 | 서버 수락 후 관찰자 `ClientRpc`; 소유자는 입력 즉시 예측 | 애니메이션/VFX/사운드만 | 일회성 표현, 판정 아님 |
| HP·자원·무기·게이지 | 각 기존/보강 컴포넌트의 `[SyncVar]` | 훅/UI에서 읽음 | 지속 상태는 SyncVar |

Mirror 배치는 다음과 같다.

- `PlayerAttack.Update`는 `isOwned`일 때만 입력을 읽는다.
- `[Command] private void CmdRequestAttack(AbilityRequest request)` 하나만 둔다. 기본 reliable/authority를 유지한다.
- 검·방울 실행기와 순수 판정 함수는 RPC, SyncVar, `NetworkIdentity`를 모른다. `PlayerAttack`이 `[ClientRpc(includeOwner = false)]`로 관찰자 시작 cue를, `[TargetRpc]`로 소유자의 수락·거부 결과를 보낸다. collider→네트워크 루트 변환은 Mirror를 사용할 수 있는 Combat 인프라 어댑터 `TargetQuery` 안에서 끝낸다.
- `PlayerAttack` 자체에 공격 진행 `SyncVar`는 두지 않는다. 1초 안팎의 일회성 상태는 reliable RPC와 절대 시작 시각으로 표현하고, 서버의 `_activeRuntime`/종료 시각만 판정 원본으로 둔다.
- `PlayerWeapon`의 현재 무기와 최종 전투 snapshot, `PlayerStatus`의 자원·HP, `ParryGauge`의 게이지만 지속 SyncVar다. 04가 아직 들어오지 않은 bridge 모드에서는 공격력 동기 필드를 임시로 둘 수 있지만, 04 통합 시 별도 원본을 남기지 않는다.

## 3. 구조

### 3.1 파트 경계

```text
01.Network.RunManager
          ▲ phase 읽기
02.Combat.PlayerAttack : NetworkBehaviour
    ├─ PlayerStatus / PlayerWeapon / PlayerRollController
    ├─ ParryController / ParryGauge
    ├─ IWeaponAbility → SwordAbility / BowAbility / BellAbility
    └─ ArrowLauncher → NetworkServer.Spawn(ArrowProjectile)

SwordAbility / BellAbility
    └─ 00.Common.IDamageable.TakeDamage(DamageInfo)
                     ▲
              03.AI.EnemyStatus

향후 04.Systems.TalentController
    └─ 00.Common의 Mirror 비의존 계약 → 서버 modifier snapshot
```

- `Combat → AI` 직접 참조는 없다. `EnemyStatus`를 캐스팅하지 않고 기존 `IDamageable`과 `DamageInfo`만 쓴다.
- `AI`는 `PlayerAttack`, 무기 실행기, 투사체 실행기를 모른다.
- `Combat → Network`는 현재 허용된 공용 런타임 서비스인 `RunManager.Phase`와 `AuthorityTickScheduler`만 읽는다. 반대로 Network는 무기 실행기, `ArrowProjectile`, 장비 데이터 정책을 참조하지 않는다.
- 미래 Systems와 Combat의 결합은 구체 `TalentController` 참조가 아니라 `00.Common`의 Mirror·Combat 비의존 DTO/인터페이스로만 잇는다. 그 계약은 특성 구현 단계에서 추가하며 현재 공격은 identity modifier(`1`, `0`)를 내부 snapshot으로 쓴다.

현재 소스와 신규/공용 선행 타입의 구분은 다음과 같다. 원본 아키텍처 문서에서 `[계획]`으로 적힌 항목은 실제 파일 존재 여부를 다시 확인했다.

| 구분 | 타입·현재 API | 이번 문서의 처리 |
|---|---|---|
| **현재 존재** | `PlayerWeapon`; `[Server] bool ServerSetInitialWeapon(WeaponKind weapon)` | 호출 시그니처를 유지하고 내부 전투 snapshot 공급만 보강 |
| **현재 존재** | `PlayerStatus.TrySpendStamina(float)`, `TrySpendSpirit(float)`; `PlayerActionGate.CanStartAction(in PlayerActionContext)` | 그대로 호출 |
| **현재 존재** | `PlayerRollController.IsRolling`; `ParryController.IsActionInProgress(double)` | 두 Command에 공격 진행 상태 확인을 추가 |
| **현재 존재** | `ParryGauge`; `[Server] bool ServerTryConsume()` | `[Server] bool ServerTryConsume(out float multiplier)`로 명시적으로 API 변경하고 호출부 갱신 |
| **현재 존재** | `RequestIdTracker.TryAccept(uint)`, `ArrowConfigSO`, `DamageInfo`, `IDamageable`, `AuthorityTickScheduler` | 재사용하되 scheduler의 순회 중 등록·해제 안전성은 선행 보강 |
| **이번 신규** | `PlayerAttack`, ability request/result/context/runtime, `SwordAbility`, `BowAbility`, `BellAbility`, `TargetQuery`, `ArrowLauncher`, `ArrowProjectile`, `ArrowGuidance`, `WeaponAbilityConfigSO` | 본 문서 구현 범위 |
| **04 소유·현재 미구현 공용 선행** | `WeaponCombatSnapshot`, `IWeaponLoadoutTarget`, `EquipmentInstance`, `EquipmentGenerator` | 계약을 소비하되 본체 구현·비용은 04에만 산정 |
| **후속 계획·현재 미구현** | `IAbilityModifierSource`, `AbilityModifierSnapshot`, `TalentController` | 경계만 규정하고 이번 구현·합격선·비용에서 제외 |

### 3.2 네트워크 진입점과 서버 오케스트레이터

**신규:** `Assets/01.Scripts/02.Combat/Player/PlayerAttack.cs`, namespace `Geumeum.Combat`.

책임은 여섯 가지로 제한한다.

1. 로컬 입력을 `AbilityRequest`로 만들고 단일 Command 전송.
2. request ID·방향·후보 netId와 서버 의존성 검증.
3. 현재 무기와 config로 실행기 선택.
4. 기존 `PlayerActionGate`, 자원, 쿨다운, 치명 snapshot을 순서대로 commit.
5. active `IAbilityRuntime` 하나를 `AuthorityTickScheduler`의 `TICK_PRIORITY_ABILITY`에서 tick/cancel.
6. owner 결과와 observer 표현 cue 전송.

`PlayerAttack`은 `Awake`에서 같은 프리팹의 `PlayerStatus`, `PlayerWeapon`, `PlayerController`, `PlayerRollController`, `ParryController`, `ParryGauge`, `ArrowLauncher`, `PlayerAnimationController`를 한 번 캐시한다. 현재 `PlayerAnimationController`에는 공격 재생 API가 없으므로 이 의존성은 표현 cue용 **신규 보강**이며 판정 선행 조건으로 쓰지 않는다. `OnStartServer`에서 실행기 세 개와 물리 query buffer를 만들고 scheduler에 등록하며, `OnStopServer`에서 active runtime 취소·등록 해제를 한다.

**기존 보강:**

- `PlayerRollController`의 기존 Command는 현재 `IsRolling || ParryController.IsActionInProgress(now)`만 묶는다. 여기에 `PlayerAttack.IsActionInProgress(now)`를 추가한다.
- `ParryController`의 기존 Command는 현재 roll과 자기 action만 묶는다. 여기에 `PlayerAttack.IsActionInProgress(now)`를 추가한다.
- `PlayerAttack`은 `PlayerRollController.IsRolling || ParryController.IsActionInProgress(now) || IsActionInProgress(now)`를 기존 `PlayerActionContext.IsActionInProgress` 한 필드로 집계한다.
- `PlayerAttack.IsActionInProgress(now)`는 startup·active·recovery만 포함하고 cooldown은 별도 필드로 검사한다.
- 세 Command는 서버 메인 스레드에서 검증 직후 자신의 진행 상태를 바로 commit한다. 따라서 같은 프레임에 둘이 도착해도 먼저 실행된 Command만 성공하고 다음 Command는 방금 commit된 상태를 본다. `PlayerActionGate` 자체가 전역 lock을 소유한다고 가정하지 않는다.
- `PlayerWeapon`은 04 통합 뒤 서버 `WeaponCombatSnapshot`을 원본으로 제공하고 잘못된 유한수/음수 공격력을 거부한다. 04 전 bridge는 아래 4.3의 기존 1인자 API 내부 구현에만 둔다.

### 3.3 request/result 값 타입

**신규:** `02.Combat/Weapon/AbilityRequest.cs`.

```csharp
public struct AbilityRequest
{
    public uint RequestId;
    public Vector3 AimDirection;
    public uint CandidateTargetNetId; // 0 = lock-on 없음
}
```

서버가 위치·무기·배율을 만들기 때문에 `Origin`, `WeaponKind`, 피해량, 비용, variant, client hit 목록은 넣지 않는다. `AimDirection`은 유한수인지 확인한 뒤 길이를 1 이하로 clamp하고, 너무 작으면 서버의 `PlayerController.LastFacingDirection`으로 대체한다. 후보 netId는 힌트일 뿐이며 서버에서 살아 있음·거리·각도·시야를 통과하지 못하면 lock-on 없이 직진한다.

**신규:** `02.Combat/Weapon/AbilityResult.cs`.

```csharp
public enum AbilityResultCode : byte
{
    Accepted = 0,
    RejectedInvalidRequest = 1,
    RejectedNotInRun = 2,
    RejectedLifeState = 3,
    RejectedNoWeapon = 4,
    RejectedActionLocked = 5,
    RejectedCooldown = 6,
    RejectedResource = 7,
    RejectedAim = 8,
    RejectedDependency = 9,
}

public struct AbilityResult
{
    public uint RequestId;
    public uint ExecutionId;
    public AbilityResultCode Code;
    public WeaponKind WeaponKind;
    public float CriticalMultiplier; // 보통 1, 패링 치명일 때만 1보다 큼
}
```

enum 값은 이후 재배열하지 않는다. 거부 결과의 `ExecutionId`는 0, `CriticalMultiplier`는 1이다.

### 3.4 무기 실행 계약

아래 **순수 계약과 실행기**는 `Assets/01.Scripts/02.Combat/Weapon/`, namespace `Geumeum.Combat`에 둔다. Unity 수학/물리 값 타입은 사용할 수 있지만 Mirror 네임스페이스·특성·`NetworkBehaviour`·`NetworkIdentity`는 사용하지 않는다. 반면 `PlayerAttack`, `TargetQuery`, `ArrowLauncher`, `ArrowProjectile`은 Combat의 서버 인프라 어댑터이므로 Mirror를 사용할 수 있다.

```csharp
public interface IWeaponAbility
{
    WeaponKind WeaponKind { get; }
    AbilityResultCode Validate(in AbilityStartContext context);
    IAbilityRuntime CreateRuntime(in AbilityExecutionContext context);
}

public interface IAbilityRuntime
{
    bool IsComplete { get; }
    AbilityTickResult Tick(double serverTime);
    void Cancel();
}
```

- `AbilityStartContext`는 서버 현재 phase·생명·무기·자원·쿨다운·검증된 조준과 의존성을 담은 변경 불가 snapshot이다. `Validate`는 world를 바꾸지 않는다.
- `AbilityExecutionContext`는 commit 뒤 발급한 `ExecutionId`, 공격자 netId, 시작 시각, 서버 origin/direction, 장착 공격력, 치명 배율, effective config/modifier를 담는다.
- `AbilityTickResult`는 이번 틱에 `ProjectileSpawnRequest`가 있는지와 runtime 완료 여부만 돌려준다. 실제 spawn은 `PlayerAttack → ArrowLauncher`가 한다.
- 실행기는 시작 뒤 config나 장비를 다시 읽지 않는다. 다단히트·연사 전체가 같은 공격력·비용·치명 snapshot을 사용한다.

**신규 실행기:**

| 클래스 | 책임 |
|---|---|
| `SwordAbility.cs` | startup 종료 틱에 한 번 `TargetQuery` 실행, `SwordSwingJudge`로 거리·각도 필터, 같은 `IDamageable` 중복 collider 제거 후 피해 |
| `BowAbility.cs` | release 틱에 검증된 방향·lock-on 후보로 `ProjectileSpawnRequest` 한 건 생성; 직접 spawn하지 않음 |
| `BellAbility.cs` | startup 종료 틱에 자기 주변 `TargetQuery` 한 번 실행, 적 `IDamageable`에 기본 pulse 피해; 미래 support modifier 자리는 두되 현재 치유·버프 없음 |
| `SwordSwingJudge.cs` | 거리·반각·앞/뒤 판정을 계산하는 순수 함수; Unity collider 탐색과 분리 |
| `TargetQuery.cs` | Mirror 사용이 허용된 서버 어댑터. `Physics.OverlapSphereNonAlloc` 호출, collider→canonical netId 변환, 재사용 buffer 관리 |
| `ProjectileSpawnRequest.cs` | origin, direction, attacker/execution ID, damage, speed, lifetime, hit radius, target netId, guidance, 최대 고유 대상 hit 수 snapshot; `NetworkIdentity` 참조는 담지 않음 |

Unity 6.4 공식 API는 `Physics.OverlapSphere`가 배열을 할당한다고 명시하고, `OverlapSphereNonAlloc`은 제공된 buffer를 재사용해 garbage를 만들지 않는다고 설명한다. 검·방울은 기존 `Constants.Combat.HIT_QUERY_BUFFER_SIZE = 16` buffer를 플레이어당 한 번 만들고 재사용한다. [Unity 6.4 Physics.OverlapSphereNonAlloc](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Physics.OverlapSphereNonAlloc.html)

`TargetQuery`는 collider의 `transform`을 곧바로 대상으로 반환하지 않는다. 각 hit에서 `GetComponentInParent<NetworkIdentity>()`로 네트워크 루트를 찾고 그 루트의 `IDamageable`을 해석한 뒤, 재사용 집합에 `netId`를 기록해 몸통·발·무기 collider 중복을 제거한다. 순수 실행기에 반환하는 `DamageableTarget`은 `uint NetId`, 루트 위치/방향에 필요한 값, `IDamageable`만 담고 `NetworkIdentity`를 노출하지 않는다. 검·방울 피해와 활의 이미 맞은 대상 기록이 모두 같은 canonical 루트 `netId` 규칙을 쓴다. 이는 중첩 identity를 두지 않고 자식에서 부모 identity를 찾으라는 Mirror 공식 구성과도 맞는다. [Mirror Network Identity](https://mirror-networking.gitbook.io/docs/manual/components/network-identity)

### 3.5 활 네트워크 실행

- `Assets/01.Scripts/02.Combat/Arrow/ArrowLauncher.cs`: 플레이어 프리팹의 서버 전용 실행부. Command가 없고 `[Server] ServerSpawn(in ProjectileSpawnRequest)`만 제공한다.
- `ArrowProjectile.cs : NetworkBehaviour`: 서버에서 속도·유도·관통·명중·수명을 처리하고 `AuthorityTickScheduler.TICK_PRIORITY_PROJECTILE`에 등록한다.
- `ArrowGuidance.cs`: 방향 회전 계산만 하는 순수 C#.
- 화살 프리팹은 `NetworkIdentity`와 프로젝트가 이미 쓰는 서버 권위 `NetworkTransformReliable`을 갖고 `GeumeumNetworkManager`의 spawnable prefabs에 등록한다. 서버만 Instantiate 후 `Initialize(request)`를 완료하고 `NetworkServer.Spawn`한다.
- `ArrowProjectile.OnStartServer`는 초기화되지 않은 인스턴스를 오류 종료하고, 정상 인스턴스만 scheduler에 등록한다. `OnStopServer`는 정확히 한 번 등록 해제한다.
- 현재 `AuthorityTickScheduler`는 callback 목록을 직접 순회하면서 `Register`/`Unregister`가 즉시 목록을 바꾼다. ability priority 300 callback이 projectile priority 200을 spawn하면 같은 순회 끝에 잘못 tick될 수 있고, projectile이 자신을 제거하면 다음 callback을 건너뛸 수 있다. 이를 다음 계약으로 **Network 계층에서 선행 보강**한 뒤 화살을 등록한다.
  - 순회 중 `Register`는 pending-add에만 넣고 `finally`에서 목록에 추가한다. 새 callback은 현재 tick에 보이지 않고 다음 FixedTick의 정렬 뒤 처음 실행된다.
  - 순회 중 `Unregister`는 callback을 즉시 inactive 집합에 표시한다. 아직 차례가 오지 않았다면 loop가 같은 tick에 건너뛰고, 자기 자신을 해제했다면 현재 호출만 끝난다. 같은 tick의 matching pending-add도 취소해 spawn 직후 파괴된 callback이 다음 tick에 살아나지 않게 한다.
  - 목록의 실제 remove와 pending-add 반영, inactive/pending 임시 집합 정리는 tick의 `finally`에서 수행한다. callback 예외가 나도 scheduler가 영구 dispatching 상태나 죽은 callback을 남기지 않는다. 서버 수명을 뜻하는 기존 `_isTicking`과 순회 여부 필드는 분리한다.
- `Initialize(request)`는 클라이언트 값이 아닌 현재 `NetworkTime.time`을 `_spawnServerTime`으로 기록하고 `_expireServerTime = _spawnServerTime + Lifetime`을 계산한다. 수명은 누적 `deltaTime`이 아니라 `NetworkTime.time >= _expireServerTime` 절대 비교로 끝내며, speed/lifetime/radius/damage가 유한하고 허용 범위인지 검사한다.
- 이동은 이전 위치→다음 위치 구간의 `SphereCastNonAlloc` sweep으로 판정해 빠른 화살의 tunneling을 막는다. hit buffer는 거리순으로 처리하고, 벽/지형을 먼저 만나면 그 뒤 적에게 피해를 주지 않고 화살을 종료한다.
- 각 collider는 `TargetQuery`와 같은 부모 `NetworkIdentity` 규칙으로 canonical netId를 만든다. 화살별 재사용 집합에 이미 기록된 netId는 같은 틱과 다음 틱 모두 무시하므로 대상 하나는 화살 하나에 최대 한 번만 피해를 받는다.
- 피해 직전 attacker 자신의 netId, `NetworkServer.spawned`에서 사라진 루트, null `IDamageable`, `IDamageable.IsDead`는 무효 hit로 건너뛰며 고유 hit 수를 소모하지 않는다.
- 유도 대상 netId가 비행 중 죽거나 despawn되거나 더는 유효하지 않으면 그 tick부터 유도만 끄고 현재 방향으로 직진한다. 대상 컴포넌트 참조를 장기 보관하지 않는다.
- 모호한 `pierce` 감소 대신 request에 `MaxDistinctTargetHits = max(1, ArrowConfigSO.BasePierceCount + modifier bonus)`를 넣는다. 여기서 값 `1`은 첫 고유 대상 한 명을 맞힌 직후 종료라는 합격 계약이다.
- hit 집합은 `MaxDistinctTargetHits` 크기로 한 번 준비하고 그 이상 자라지 않는다. sweep buffer가 가득 차면 가까운 충돌 누락 여부를 알 수 없으므로 일부 결과를 임의 적용하지 않고 진단 카운터를 올린 뒤 화살을 종료하는 fail-closed 경로를 쓴다.
- 남은 고유 hit 수가 0이거나 절대 수명이 끝나면 `_resolved` guard 아래 `NetworkServer.Destroy(gameObject)`를 한 번만 호출한다. owner가 먼저 파괴돼도 컴포넌트 참조 없이 snapshot과 attacker netId로 만료/명중까지 독립 처리한다.

### 3.6 미래 특성 접점

`IAbilityModifierSource`와 `AbilityModifierSnapshot`은 **후속 계획이며 현재 소스에는 없다.** 이번 단계에서 Systems를 미리 참조하거나 사용되지 않는 Common 타입을 만들지 않는다. `PlayerAttack`은 아래 identity 값을 Combat 내부 실행 context에 직접 snapshot한다.

```text
AttackSpeedMultiplier = 1
ResourceCostMultiplier = 1
DamageMultiplier = 1
ExtraProjectileCount = 0
PierceCountBonus = 0
GuidanceDurationBonus = 0
BellMode = Basic
```

미래 `TalentController`를 구현할 때만 Mirror와 Combat 구체 타입을 모르는 DTO/인터페이스를 `00.Common`에 추가한다. Systems가 이를 구현하고 Combat가 읽되 서로의 concrete class는 참조하지 않는다. 그 뒤에도 클라이언트 request에는 `BellMode`, 연사, 관통 같은 선택을 넣지 않고 서버가 현재 찍힌 노드와 무기를 보고 snapshot을 만든다.

## 4. 데이터

### 4.1 신규 `WeaponAbilityConfigSO`

**파일:** `Assets/01.Scripts/00.Common/Data/WeaponAbilityConfigSO.cs`, namespace `Geumeum.Data`.  
**에셋:** `Assets/09.Data/Weapon/WeaponAbilityConfigSO.asset`.  
**접근:** `GameDatabaseSO._weaponAbility` → `GameDatabaseSO.WeaponAbility` → `GameDB.WeaponAbility`. `CollectMissingReferences`에도 필수 참조로 넣는다.

Unity는 ScriptableObject를 GameObject와 독립된 공유 데이터 asset으로 설명한다. 세 프리팹 복사본에 수치를 복제하지 않고 `GameDB` 하나에서 읽는 현재 프로젝트 방식과 맞는다. [Unity 6.4 ScriptableObject](https://docs.unity3d.com/6000.4/Documentation/Manual/class-ScriptableObject.html)

```csharp
[Serializable]
public struct BasicAttackEntry
{
    public WeaponKind WeaponKind;
    public AbilityResourceKind ResourceKind;
    public float ResourceCost;
    public float StartupDuration;
    public float ActiveDuration;
    public float RecoveryDuration;
    public float Cooldown;
    public float Range;
    public float Radius;
    public float ArcDegrees;
    public float DamageMultiplier;
}

[SerializeField]
private BasicAttackEntry[] _basicAttacks =
    new BasicAttackEntry[Constants.Combat.WEAPON_KIND_COUNT];
```

`AbilityResourceKind`는 `00.Common/Types/AbilityResourceKind.cs`에 명시값 `None = 0, Stamina = 1, Spirit = 2`로 둔다.

### 4.2 기본값 후보

| 무기 | Resource | Cost | Startup | Active | Recovery | Cooldown | Range | Radius | Arc | Damage multiplier |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `Sword` | `Stamina` | `15` | `0.20 s` | `0.10 s` | `0.35 s` | `0.65 s` | `2.4 m` | `0.75 m` | `100°` | `1.0` |
| `Bow` | `Stamina` | `12` | `0.20 s` | `0 s` | `0.30 s` | `0.75 s` | `40 m` | `0` | `0°` | `1.0` |
| `Bell` | `Stamina` | `10` 후보 | `0.30 s` | `0.10 s` | `0.40 s` | `0.90 s` | `4 m` | `4 m` | `360°` | `0.65` 후보 |

검 15와 활 12는 `docs/04-architecture.md`의 현재 임시값이다. 방울 기본 pulse의 비용·배율은 현재 기획에 없으므로 **후보**로 표시하고 첫 M2 전에 기획 확인한다. 확인 전에도 값은 SO에만 있어 코드 재빌드 없이 교체할 수 있다.

방울 support용 값은 같은 SO의 별도 `BellSupportEntry`로 예약하되 1단계에서는 읽지 않는다.

| 필드 | 타입 | 기본값 후보 | 근거 |
|---|---|---:|---|
| `SpiritCost` | `float` | `20` | 기존 아키텍처 임시값 |
| `HealAmount` | `float` | `30` | 기존 아키텍처 임시값 |
| `Range` | `float` | `8 m` | 기존 아키텍처 임시값 |
| `Cooldown` | `float` | `2.5 s` | 기존 아키텍처 임시값 |
| `BuffAttackRatio` | `float` | `0.15` | +15% |
| `BuffDuration` | `float` | `8 s` | 기존 아키텍처 임시값 |

### 4.3 재사용 데이터와 단일 원본

- **피해 기본값을 `WeaponAbilityConfigSO`에 추가하지 않는다.** 장착 공격력은 기존 `EquipmentSO.GradeEntry.AttackMin/AttackMax`에서 생성된 서버 장비 snapshot이 단독 소유한다.
- 전체 피해는 서버에서 `AttackPower × BasicAttackEntry.DamageMultiplier × modifier.DamageMultiplier × CriticalMultiplier`로 한 번 계산한다.
- 활 속도·수명·보정·유도·비락온 반경·기본 고유 대상 hit 수는 기존 `ArrowConfigSO`를 재사용한다. 필드명 `BasePierceCount`의 이번 실행 의미는 3.5의 `MaxDistinctTargetHits`로 고정하고 `WeaponAbilityConfigSO`에 복제하지 않는다.
- 패링 게이지 최대·치명 최소/최대 배율은 기존 `CombatConfigSO`를 재사용한다.
- 레이어는 밸런스가 아니므로 SO 필드가 아니라 기존 `Constants.Layer.ENEMY`/`BOSS`를 쓴다.

현재 소스의 `PlayerWeapon`에는 `_weapon`, `_hasWeapon`만 있고 공격력 원본이 없다. 현재 공개 API는 정확히 `[Server] bool ServerSetInitialWeapon(WeaponKind weapon)`이며 `LobbyController.AssignRoles`도 이 1인자 API를 호출한다. 존재하지 않는 2인자 overload를 전제로 Network 계층이 장비 수치를 공급하게 만들지 않는다.

최종 계약은 04 문서가 소유하는 `WeaponCombatSnapshot`이다. 04가 먼저 구현됐다면 `PlayerAttack`은 `PlayerWeapon.CombatSnapshot.AttackPower`만 snapshot하고 아래 임시 bridge를 만들지 않는다. 02 수직 슬라이스가 먼저여서 프로토타입이 막힐 때만 다음 bridge를 쓴다.

1. `LobbyController.AssignRoles`는 기존처럼 무기만 정하고 `ServerSetInitialWeapon(weapon)`을 호출한다. Network 계층에 `Geumeum.Data`, 등급, 공격력 정책을 추가하지 않는다.
2. `PlayerWeapon.ServerSetInitialWeapon(WeaponKind)` 내부가 `GameDB.Equipment.TryGetGrade(ItemGrade.Common, out EquipmentSO.GradeEntry commonGrade)`의 **현재 실제 시그니처**로 Common 등급을 조회한다.
3. `commonGrade.AttackMin`이 유한하고 0보다 큰지 검증한 뒤 임시 서버 공격력 필드와 무기/보유 여부를 같은 호출에서 확정한다. 어느 하나라도 실패하면 셋 다 바꾸지 않는다.
4. `PlayerAttack`은 수락 시 그 값을 실행 context에 복사하며 request나 `LobbyController`에서 공격력을 받지 않는다.
5. 04의 `EquipmentInstance`/`EquipmentGenerator`가 들어오면 임시 필드를 제거하고 같은 1인자 API 내부가 실제 rolled `WeaponCombatSnapshot`을 설치하도록 교체한다. `PlayerAttack`, ability 계약, Network 호출부는 바꾸지 않는다.

`AttackMin` bridge는 실제 장착 아이템의 rolled 공격력이 아니라 재현 가능한 임시값일 뿐이다. M2 결과에는 `Bridge/Common.AttackMin` 모드인지 `WeaponCombatSnapshot/rolled` 모드인지 반드시 기록한다. 새 공격력 상수나 두 번째 SO 원본을 만들지 않으며, 04의 snapshot·장비 생성 구현 시간은 02 비용에 다시 넣지 않는다.

### 4.4 데이터 검증

- `_basicAttacks`는 정확히 3개이고 `Sword/Bow/Bell`이 한 번씩만 있어야 한다.
- 모든 float는 유한수다. 비용·시간·거리·반경은 0 이상, `Cooldown >= Startup + Active`, `0 < DamageMultiplier`다.
- 검/방울은 query 반경이 0보다 커야 하고, 활은 `ArrowConfigSO.ArrowSpeed`·`ArrowLifetime`이 0보다 커야 한다.
- `ArrowConfigSO.BasePierceCount`는 `1..Constants.Combat.HIT_QUERY_BUFFER_SIZE`여야 한다. 이번 MVP에는 bonus가 없고, 후속 bonus도 같은 안전 상한으로 clamp한 뒤 `MaxDistinctTargetHits`를 만든다.
- `ArcDegrees`는 `[0, 360]`이다.
- Editor의 `OnValidate`와 서버 부트 검사를 함께 둔다. `OnValidate`는 Inspector 데이터 검증용이라는 Unity 공식 범위에 맞춘다. [Unity 6.4 OnValidate](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/MonoBehaviour.OnValidate.html)

## 5. 흐름

### 5.1 입력 → 서버 수락

1. 소유 클라이언트의 `PlayerAttack.Update`가 좌클릭을 읽는다. 로컬 `RequestId`를 1 증가시키고 0은 건너뛴다.
2. 카메라/락온이 있으면 조준 방향과 후보 netId를 넣고, 아직 없으면 로컬 `PlayerPresentation.FacingDirection`을 월드 방향으로 바꿔 넣는다. 입력 즉시 무기별 placeholder 모션/VFX를 예측 재생한다.
3. `CmdRequestAttack(request)`가 서버에서 다음 순서로 처리된다.
   1. request ID가 0인지, 중복·역순인지 `RequestIdTracker`로 검사.
   2. 방향의 `NaN`/`Infinity`, 비정상 크기 검사.
   3. `GameDB`와 필수 컴포넌트, scheduler/launcher 의존성 검사.
   4. `RunManager.Phase`와 `PlayerStatus.LifeState` 검사.
   5. `PlayerWeapon.HasWeapon`과 04 전투 snapshot 또는 명시한 bridge 공격력 검사.
   6. 서버 현재 무기로 `WeaponAbilityConfigSO.BasicAttackEntry`와 `IWeaponAbility` 선택.
   7. active 공격·구르기·패링, 쿨다운 검사.
   8. 후보 netId를 `NetworkServer.spawned`에서 찾아 거리·각도·생존·시야 검사. 실패하면 후보만 버리고 free aim은 유지한다.
   9. 현재 identity modifier로 effective 수치 snapshot. 미래 특성 계약이 실제 구현된 뒤에만 서버 modifier source로 교체한다.
   10. 공격·구르기·패링 진행 여부를 한 `PlayerActionContext.IsActionInProgress`로 집계하고 현재 실제 API `PlayerActionGate.CanStartAction(in PlayerActionContext)`과 해당 자원 잔액을 검사한다.
   11. `IWeaponAbility.Validate` 수행. 여기까지 world 상태는 바뀌지 않는다.
4. 한 조건이라도 실패하면 해당 `AbilityResultCode`를 `TargetRpc`로 보내고 끝낸다. 자원·쿨다운·게이지·실행 ID는 바뀌지 않는다.

### 5.2 서버 commit → 실행

1. `PlayerStatus.TrySpendStamina` 또는 `TrySpendSpirit`가 자원을 원자적으로 차감한다.
2. 서버 `ExecutionId`를 증가시키고 공격 action 종료·쿨다운 종료 시각을 commit한다.
3. 현재 무기가 검이면 기존 `[Server] bool ParryGauge.ServerTryConsume()`를 `[Server] bool ServerTryConsume(out float multiplier)`로 변경한 API를 **한 번** 호출한다. false이면 `multiplier = 1`, true이면 게이지를 0으로 만든 같은 호출 안에서 `CombatConfigSO.CriticalMultiplierMin/Max` 범위의 서버 배율을 반환한다. 기존 호출부는 모두 새 시그니처로 갱신하고 별도 `consume`/`roll` 두 호출로 쪼개지 않는다.
4. 장착 공격력, effective config, modifier, 치명 배율, 검증된 방향/대상을 변경 불가 `AbilityExecutionContext`에 복사한다.
5. 선택된 `IWeaponAbility.CreateRuntime`으로 active runtime을 만들고 `TargetAttackResult(Accepted)`, `RpcAttackStarted`를 보낸다.
6. `AuthorityTickScheduler`가 runtime을 tick한다.
   - 검: startup 종료 첫 틱에 query·각도 필터·중복 제거·피해를 한 번 실행한다.
   - 활: release 첫 틱에 spawn request 한 건을 내고 `ArrowLauncher`가 서버 화살 한 개를 spawn한다.
   - 방울: startup 종료 첫 틱에 원형 query·중복 제거·피해를 한 번 실행한다.
7. recovery가 끝나면 runtime을 비우고 다음 공격을 허용한다. 쿨다운이 더 길면 쿨다운 종료까지 거부한다.

치명 게이지는 **수락된 공격 행동 시작**에 소비한다. 검이 빗나가거나 대상이 그 뒤 사라져도 복구하지 않는다. 다단히트/연사가 나중에 붙어도 context의 같은 배율을 쓰고 다시 추첨하지 않는다.

### 5.3 활 세부 흐름

1. `BowAbility`는 기존 `GameDB.Arrow`의 `ArrowSpeed`, `ArrowLifetime`, `AimAssistAngle`, `GuidanceDegreesPerSecond`, `GuidanceDuration`, `UnlockedHitRadius`, `BasePierceCount`와 현재 identity modifier를 snapshot한다.
2. lock-on 후보가 서버 검증을 통과하고 조준선과 대상 방향 각도가 `AimAssistAngle` 이내면 서버가 초기 방향을 보정한다.
3. 관통 modifier면 유도 시간을 0으로 하고 직선 비행한다.
4. `ArrowLauncher.ServerSpawn`이 prefab을 Instantiate하고 `ArrowProjectile.Initialize(request)`로 서버 `_spawnServerTime`, 절대 만료 시각, `MaxDistinctTargetHits`를 고정한 뒤 `NetworkServer.Spawn`한다.
5. 새 projectile callback은 scheduler pending-add 계약에 따라 다음 FixedTick부터 실행된다. 매 tick은 만료 확인, 유도, 이전→다음 위치 sweep, 거리순 충돌 처리 순서다.
6. hit collider는 부모 `NetworkIdentity`의 canonical netId로 정규화한다. 이미 맞은 netId는 같은/후속 tick 모두 무시하고, 첫 유효 hit만 남은 고유 대상 수를 1 줄인다. 벽/지형 또는 마지막 허용 대상을 만나면 `_resolved`를 세우고 서버 destroy를 한 번만 호출한다.
7. prefab/`NetworkIdentity`/서버 권위 `NetworkTransformReliable`/spawnable 등록/scheduler가 빠졌으면 Bow request는 **자원 commit 전에** `RejectedDependency`다.

### 5.4 취소·거부 경로

| 상황 | 처리 |
|---|---|
| 중복·역순 request | 조용히 무시하거나 같은 request 결과를 재적용하지 않음; 자원 변화 0 |
| 로비·Result·Downed·Spectating | 거부; 예측 모션 blend-out |
| 무기 미배분·config 누락·공격력 없음 | `RejectedNoWeapon/Dependency`; 런 부트/설정 오류 로그 |
| 공격·구르기·패링 중 | `RejectedActionLocked` |
| 쿨다운·자원 부족 | 해당 코드 거부, 게이지 유지 |
| 잘못된 lock-on 후보 | 후보만 버리고 검증된 free-aim 공격 진행 |
| 조준 방향 자체가 비정상 | `RejectedAim` |
| commit 뒤 대상 사망·빗나감 | 공격은 정상 종료, 비용·치명 소비 복구 없음 |
| 플레이어 사망·Result·disconnect | active runtime을 취소해 이후 근접/pulse/new spawn은 없음; 이미 서버 spawn된 독립 화살은 자기 snapshot·수명으로 계속 처리; 비용은 환급하지 않음 |
| 무기 교체 요청 | 공격 runtime이 있으면 교체 쪽에서 거부; 따라서 실행 중 능력 타입이 바뀌지 않음 |
| 물리 query buffer가 가득 참 | 검·방울은 처리 수/capacity를 진단하고 임의 resize를 하지 않음; 화살 sweep은 가까운 벽 누락 가능성 때문에 피해를 적용하지 않고 fail-closed 종료 |
| 화살 spawn 뒤 launcher/owner 파괴 | projectile이 가진 snapshot으로 계속 처리; attacker netId만 귀속에 사용 |

## 6. 단계별 구현 순서

1. **공통 계약 + 검 수직 슬라이스 — 12~16시간.** `WeaponAbilityConfigSO`, GameDB 연결, request/result/context, `IWeaponAbility`/`IAbilityRuntime`, `PlayerAttack`, `SwordAbility`, `TargetQuery`, `SwordSwingJudge`를 만든다. 04가 아직 없을 때만 기존 1인자 `PlayerWeapon.ServerSetInitialWeapon` 내부에 재현 가능한 bridge 공격력을 연결한다. 여기까지 하면 M0/M1에서 서버 검 공격 한 종류가 자원·쿨다운·AI 피해까지 동작한다. 04 소유 snapshot/장비 생성 본체는 이 시간에 포함하지 않는다.
2. **행동 상호 배제 + 치명 — 5~7시간.** 구르기/패링/공격이 서로 진행 상태를 조회하고, `ParryGauge.ServerTryConsume(out multiplier)`를 commit 뒤 한 번 연결한다. 여기까지 하면 게이지 100의 다음 검 공격이 빗나가도 정확히 한 번 소비된다.
3. **공용 scheduler 선행 게이트 — 3~5시간.** `AuthorityTickScheduler`의 순회 중 등록은 pending-add, 해제는 즉시 inactive 표시, 실제 목록 변경은 `finally`로 바꾸고 M0의 등록/self-unregister/선행-unregister/예외 회귀 테스트를 만든다. 이 단계는 02만의 화살 로직이 아니라 Network 공용 런타임 보강이므로 상위 감사표에서 이미 완료·산정했다면 다시 구현하거나 더하지 않는다.
4. **활 서버 투사체 — 12~16시간.** 3단계 gate 위에서 `BowAbility`, `ProjectileSpawnRequest`, `ArrowLauncher`, `ArrowProjectile`, `ArrowGuidance`, spawn prefab 등록, 절대 수명·sweep·canonical hit 집합·단일 destroy를 구현한다. 여기까지 하면 M1에서 클라이언트가 화살을 만들지 않고 서버 화살이 양쪽에 같은 결과를 낸다.
5. **방울 기본 pulse — 4~6시간.** `BellAbility`가 같은 query/피해 계약으로 원형 기본 공격을 실행한다. support는 연결하지 않는다. 여기까지 하면 세 `WeaponKind`가 같은 Command에서 모두 실행된다.
6. **3인 권위·지연 검증 — 10~14시간.** M2에서 무기 spoof·중복·자원·치명·화살 lifecycle을, M3에서 서로 다른 PC의 궤적과 체감을 검증한다. 후보 수치만 SO에서 조정한다. 04의 인벤토리/장비 M2·M3와 05의 애니메이션 검증은 이 시간에 포함하지 않으며, 실제 `WeaponCombatSnapshot`을 소비하는 교차 1건은 04 통합 검증에서 한 번만 센다.
7. **특성 연결은 후속 단계.** `TalentController`가 생긴 시점에 Common 계약을 추가하고 이를 구현해 연사·관통·치유·버프를 서버 snapshot으로 공급한다. 새 Command나 클라이언트 슬롯 필드는 추가하지 않는다.

1단계 PR은 검만 있어도 공통 계약을 검증할 수 있다. 활·방울까지 한 PR에 묶어 리뷰와 M2를 늦추지 않는다.

## 7. 검증

### 7.1 M0 — 정적·단일 호스트

자동 테스트가 가능한 계약은 EditMode/PlayMode 테스트로 먼저 고정하고, 물리·표현은 단일 호스트에서 관찰한다.

| 관찰 항목 | 합격 신호 |
|---|---|
| config 매핑 | `WeaponAbilityConfigSO`에 `Sword/Bow/Bell`가 정확히 한 번씩 있고 누락·중복이면 런 부트가 실패한다 |
| request 순서 | 1→100은 각 한 번 수락되고 0·중복·역순 ID는 자원·쿨다운·게이지·`ExecutionId`를 바꾸지 않는다 |
| 공통 게이트 | Lobby/Result, Dead/Downed/Spectating, 무기 없음, 구르기/패링/공격 중, 쿨다운, 자원 부족마다 명시한 `AbilityResultCode`가 나온다 |
| 검증/commit 경계 | `Validate` 거부 전후 상태 변화 0; 수락 한 건은 자원 차감·실행 ID·쿨다운을 각 한 번만 commit한다 |
| 치명 소비 | 게이지 최대인 검 공격 수락 한 건에서 `ServerTryConsume(out multiplier)`가 한 번 호출되고 배율은 설정 범위 안이며 게이지는 0이다 |
| 빗나간 치명 | commit 뒤 대상이 없어도 치명은 복구되지 않고 후속 검 공격은 배율 1이다 |
| 검 query | 거리·100° 경계의 포함/제외가 고정되고, 한 적의 자식 collider 3개가 잡혀도 루트 `netId` 하나와 `TakeDamage` 한 번만 나온다 |
| 방울 query | 반경 경계와 적 layer만 통과하고 한 적당 기본 pulse 피해가 최대 한 번이다 |
| query 할당 | 1,000회 반복에서 buffer 생성 뒤 `TargetQuery` 경로 GC Alloc이 0 B이며 capacity 도달은 진단 카운터에 남는다 |
| runtime 취소 | startup·active·recovery 각 구간에서 취소해 이후 피해·spawn·종료 RPC 중복이 0이다 |
| scheduler 순회 중 등록 | ability priority callback 안에서 projectile priority callback을 등록해도 현재 `FixedUpdate` 호출 수는 0, 다음 `FixedUpdate` 호출 수는 1이며 그 전에 priority 재정렬이 끝난다 |
| scheduler self-unregister | callback이 자신을 `Unregister`하면 현재 호출은 정상 종료되고 같은 tick의 다음 기존 callback은 건너뛰지 않고 한 번 실행된다. 해제한 callback의 다음 tick 호출은 0회다 |
| scheduler 선행 unregister | 앞 callback이 아직 차례가 오지 않은 뒤 callback을 `Unregister`하면 뒤 callback은 같은 tick부터 0회다. 실제 목록 제거와 inactive 정리는 `finally`에 이루어진다 |
| scheduler add 후 remove | 한 callback을 같은 tick에 `Register`한 뒤 `Unregister`하면 matching pending-add가 취소되어 현재·다음 tick 호출이 모두 0회다 |
| scheduler 예외 정리 | callback이 예외를 던져도 `finally`에서 pending remove/add가 적용되어 다음 FixedTick에 중복·죽은 callback·영구 dispatching guard가 없다. 서버 수명 `_isTicking`은 정상 유지된다 |
| 화살 수명 | 절대 만료 시각 직전에는 존재하고 `NetworkTime.time >= ExpireServerTime`인 첫 tick에 `_resolved`와 server destroy가 각각 한 번뿐이다. 긴 frame hitch 뒤에도 추가 비행·피해가 없다 |
| 화살 고유 hit | 같은 적의 자식 collider 셋이 같은 sweep과 연속 sweep에 반복돼도 canonical netId 기록과 피해는 총 한 번이다 |
| 관통 경계 | `MaxDistinctTargetHits = 1`이면 첫 고유 대상 정확히 한 명만 피해를 받고 화살이 끝난다. 중복 collider는 수를 소모하지 않는다 |
| 장애물 순서 | 같은 sweep 안에서 벽이 적보다 가까우면 거리순 처리 후 적 피해 0, server destroy 1이다 |

할당 없는 물리 조회를 합격 조건으로 둔 근거는 Unity 공식 `OverlapSphereNonAlloc`이 제공된 buffer에 결과를 저장하고 garbage를 만들지 않는 API이기 때문이다. [Unity 6.4 `Physics.OverlapSphereNonAlloc`](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Physics.OverlapSphereNonAlloc.html)

### 7.2 M1 — 호스트 + 클라이언트 1, 같은 PC

- 원격 소유자가 공격 20회를 입력할 때 owner→server `CmdRequestAttack`은 요청당 한 번이고, 서버 수락/거부 결과와 두 화면의 시작·종료 표현이 일치한다.
- request에는 무기 종류·공격력·hit 목록이 없고, 서버의 `PlayerWeapon.Weapon`을 바꾸면 같은 입력이 즉시 해당 실행기로 라우팅된다.
- 검과 방울 각각 20회에서 AI HP 감소 횟수·값이 호스트와 클라이언트에서 같고 중복 피해가 0회다.
- 활 20회에서 `NetworkServer.Spawn`은 수락된 실행당 정확히 화살 하나를 만들며 두 화면에서 같은 `netId` 화살을 본다. 클라이언트가 별도 화살을 만드는 경우는 0회다.
- 화살 20회의 hit/만료/벽 충돌 각 경로에서 `NetworkServer.Destroy`는 화살당 한 번이고, 호스트와 클라이언트의 관찰 화살 수가 다음 동기화 뒤 모두 0이다.
- 수락 전 로컬 placeholder 모션은 거부 결과에서 recovery까지 기다리지 않고 blend-out하며, 호스트 화면에는 이 소유자 전용 예측이 게임 판정으로 보이지 않는다.

### 7.3 M2 — 호스트 + 클라이언트 2, 같은 PC

- 한 플레이어 프리팹과 같은 `PlayerAttack` 컴포넌트로 검·활·방울을 각각 배분하고, 각 20회 기본 공격이 다른 Command 추가 없이 실행된다.
- 세 직업 각각 고정 HP 표적을 20회 공격해 서버 계산식 `AttackPower × DamageMultiplier × modifier × CriticalMultiplier`과 실제 HP 감소가 모두 일치한다.
- 위 피해 기록마다 공격력 공급 모드를 `Bridge/Common.AttackMin` 또는 `WeaponCombatSnapshot/rolled`로 남긴다. bridge 값을 실제 장착 장비 roll이라고 보고하지 않으며 두 모드가 동시에 원본으로 남는 경우는 0개다.
- request에 `WeaponKind` 슬롯이 없음을 직렬화 필드 검사로 확인하고, 런 중 서버 무기와 다른 실행기를 선택할 경로가 0개임을 확인한다.
- 세 소유자가 같은 적을 동시에 공격하는 20회에서 실행 ID·투사체 netId 충돌, 자원 이중 차감, 한 collider의 중복 피해가 각각 0회다.
- 공격 중 구르기/패링, 구르기 중 공격/패링, 패링 중 공격/구르기를 양방향으로 각 10회 시도해 동시에 active인 행동이 0회다.
- 패링 성공 3회로 게이지가 `35 × 3`에서 최대 100으로 clamp되고, 다음 **수락된 검 공격** 한 번만 치명 배율을 가지며 그 공격이 빗나가도 게이지는 소비된다.
- 같은 `RequestId` 재전송과 늦은 이전 result를 주입해도 새 예측 표현을 끝내거나 실행을 두 번 만드는 경우가 0회다.
- 활의 같은 적 자식 collider가 같은 tick과 연속 tick에 겹치는 경우, `MaxDistinctTargetHits = 1/2`, 벽 앞·뒤 표적, 비행 중 표적 death/despawn을 각 10회 실행한다. 대상별 중복 피해, 관통 off-by-one, 벽 뒤 유령 피해, 예외가 모두 0회이고 화살은 각 경로에서 정확히 한 번 소멸한다.

M2는 최소 네트워크 합격선이다. 세 직업 라우팅·동시 자원 commit·행동 상호 배제는 호스트 단독으로 완료 판정하지 않는다.

### 7.4 M3 — 서로 다른 PC 3대

- **서로 다른 실제 PC 3대**에서 0ms, 30~50ms, 150ms 조건별로 직업별 20회씩 공격한다. 소유자 결과 코드, 세 화면의 실행 ID, 서버 HP, 화살 spawn/소멸 수가 매 회 일치한다. 지연은 표시 도착만 늦추며 같은 서버 입력 순서의 화살 궤적·hit 결과를 바꾸지 않는다.
- 근접 공격은 과거 위치 rewind를 하지 않으므로 150ms에서 로컬 placeholder는 맞아 보이지만 서버가 빗나감으로 판정한 비율을 별도 기록한다. 반복되면 판정 권위를 옮기지 않고 startup·범위·표현 lead를 SO에서 먼저 조정한다.
- 유도 화살의 대상이 비행 중 사망·despawn·시야 이탈하는 경우를 각 10회 실행해 null 예외, 유령 피해, 클라이언트별 궤적 분기가 0회인지 확인한다.
- GTX 1060 3GB 호스트, 동시 잡귀 18~22마리 구간에서 세 플레이어가 연속 공격한다. 전투 중 60 FPS 목표, steady-state 공격 query GC Alloc 0 B/frame, buffer overflow 진단 0을 확인한다.
- disconnect·Result 전환 직전에 검 active, 방울 startup, 화살 release를 각각 10회 반복해 사후 hit/spawn과 남은 runtime이 0개인지 확인한다.

## 8. 대안과 선택 근거

### 채택: 단일 서버 오케스트레이터 + 무기별 순수 실행기 + 데이터 자산

보통 Mirror 게임은 소유 클라이언트의 Command를 서버 판정 진입점으로 쓰고 서버가 결과를 ClientRpc/TargetRpc로 알리며, 지속 상태는 SyncVar로 동기화한다. Mirror 공식 문서도 Command를 클라이언트→서버, ClientRpc를 서버→클라이언트 원격 호출로 정의하고 서버 권위를 기본 경계로 설명한다. [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions), [Mirror Authority](https://mirror-networking.gitbook.io/docs/manual/guides/authority), [Mirror SyncVars](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization/syncvars)

따라서 네트워크 입구는 플레이어 프리팹의 `PlayerAttack : NetworkBehaviour` 하나로 두고, 서버가 현재 `PlayerWeapon.Weapon`을 읽어 Mirror 비의존 `IWeaponAbility` 실행기를 선택한다. 화살은 서버만 Instantiate/초기화/`NetworkServer.Spawn`하며, Mirror 공식 문서가 설명하는 것처럼 spawn된 오브젝트의 `NetworkIdentity`를 통해 클라이언트 표현을 만든다. [Mirror Network Identity](https://mirror-networking.gitbook.io/docs/manual/components/network-identity)

밸런스 공유값은 `WeaponAbilityConfigSO`와 기존 `ArrowConfigSO`·`CombatConfigSO`에 두고, 실행 중 값은 서버 context로 복사한다. Unity는 ScriptableObject를 GameObject와 독립된 공유 데이터 asset으로 설명하므로 세 직업 프리팹 복사 없이 한 데이터 원본을 유지하는 현재 `GameDB` 방식과 맞는다. [Unity 6.4 ScriptableObject](https://docs.unity3d.com/6000.4/Documentation/Manual/class-ScriptableObject.html)

이 조합은 이 저장소의 한 플레이어 프리팹, 런 시작 서버 무기 배분, 기존 `PlayerStatus`/`PlayerWeapon`/`ParryGauge`, 보강한 `AuthorityTickScheduler`를 쓴다. 3인 리슨 서버에서는 별도 예측 프레임워크나 범용 능력 그래프보다 한 Command와 세 실행기가 검색·디버깅하기 쉽다. 11주 동안 세 사람이 검 수직 슬라이스, 활 투사체, 방울 pulse/M2를 병렬화할 수 있고, 나중 특성은 필요해진 시점에 Common의 Mirror 비의존 접점을 추가해 네트워크 계약을 늘리지 않는다.

### 검토했지만 고르지 않은 방식

| 대안 | 장점 | 고르지 않은 이유 |
|---|---|---|
| `PlayerAttack` 안의 거대한 `switch (WeaponKind)` | 초기 파일 수가 적다 | 검 query·활 spawn·방울 범위 판정의 상태와 의존성이 한 클래스에 섞인다. 세 사람이 같은 파일을 수정하고 특성 분기가 늘어 11주 후 회귀 범위가 커진다 |
| 무기마다 `NetworkBehaviour`와 Command | 각 무기가 독립적으로 보인다 | request ID, phase, 자원, 쿨다운, 구르기·패링 잠금, 결과 RPC를 세 번 복제한다. 한 프리팹에서 어떤 Command가 활성인지도 추가 검증해야 한다 |
| 직업별 플레이어 프리팹 세 개 | Inspector 연결이 직관적이다 | 외형과 무기가 무관하고 서버가 무작위 배분한다는 현재 규칙과 충돌한다. NetworkManager spawn prefab·Lobby 배분·공통 수정이 세 벌이 된다 |
| 클라이언트가 `WeaponKind`·공격력·hit 목록 전송 | 서버 코드가 짧다 | 소유자가 무기·피해·명중을 조작할 수 있고 지연/재전송 때 결과가 갈린다. Mirror 서버 권위와 현재 서버 HP·자원 모델을 깨므로 배제한다 |
| 클라이언트 화살 spawn 뒤 서버 승인 | 입력 체감이 빠르다 | 동일 화살 매칭·거부 rollback·권위 이전이 필요하다. 3인 리슨 서버 범위에서는 서버 spawn 한 경로가 더 작고 결정적이다 |
| 근접 client prediction + 서버 rewind/reconciliation | 고지연 명중 체감이 좋아질 수 있다 | 위치 히스토리, 시각 보정, 재판정, 피격 rollback이 새 하위 시스템이 된다. M3 측정 전에는 11주·3인 범위에 비해 비용이 크다 |
| ScriptableObject 상속으로 무기 로직까지 asset화 | 디자이너가 asset만 교체할 수 있다 | 런타임 서비스·물리 buffer·서버 spawn 의존성을 asset에 숨기고 Unity 직렬화/테스트 경계를 복잡하게 만든다. SO는 수치, C# 실행기는 동작으로 제한한다 |
| 처음부터 화살 네트워크 풀링 | spawn 비용을 낮출 수 있다 | Mirror custom spawn handler와 lifecycle 검증이 추가된다. M3 profiler가 실제 병목을 보일 때 후속 최적화하고, 먼저 단순한 서버 spawn의 정확성을 확보한다 |

## 9. 비용과 위험

**02 단독 착수 비용: 중, 총 46~64 인시.** 공통 계약·검 12~16시간, 행동 배제·치명 5~7시간, 공용 scheduler 선행 게이트 3~5시간, 활 12~16시간, 방울 기본 pulse 4~6시간, 공격 소유 M2/M3 10~14시간이다. 약 6~8 인일이며, 전투 담당이 공통 계약/검, 네트워크 담당이 scheduler/화살, 팀장이 방울·프리팹·3인 통합을 맡으면 11주 전체 일정 안에서 수직 슬라이스로 나눌 수 있다.

상위 감사표의 공용 scheduler 게이트 3~5시간이 먼저 완료됐다면 **02의 증분 비용은 43~59 인시**다. 두 총액을 합산하지 않는다.

비용 경계도 고정한다. 04가 소유하는 `WeaponCombatSnapshot`/`IWeaponLoadoutTarget`/`EquipmentInstance`/`EquipmentGenerator` 본 구현과 그 인벤토리·장비 M2/M3, 05가 소유하는 공격 Animator/clip 검증, 특성·방울 치유/버프·네트워크 풀링은 어느 총액에도 포함하지 않는다. 02가 먼저일 때의 Common `AttackMin` bridge만 공통 계약·검 항목에 포함한다. 실제 snapshot 소비 교차 검증 1건은 04 통합 항목에서 한 번만 세며 02 M2/M3에 중복 산정하지 않는다.

- **공격력 원본 공백:** 현재 `EquipmentInstance`가 없어 `PlayerWeapon`에 실제 rolled 공격력이 없다. `LobbyController.AssignRoles`의 기존 1인자 호출은 유지하고, 필요할 때만 `PlayerWeapon.ServerSetInitialWeapon(WeaponKind)` 내부에서 정확한 `TryGetGrade(..., out ...)` API로 Common `AttackMin` bridge를 만든다. 장비 생성 구현 시 그 내부 공급점과 임시 필드를 04 snapshot으로 교체한다.
- **방울 기획 미확정:** 기본 pulse 비용·배율은 후보일 뿐이다. 첫 M2 전에 기획 확인하며 치유·버프는 범위 밖으로 유지한다.
- **`ParryGauge` API 차이:** 현재 `ServerTryConsume()`은 bool과 0 reset만 제공한다. 서버 한 곳에서 `out float multiplier`를 반환하도록 보강하고, 추첨 함수를 한 메서드에 모아 테스트 난수를 고정할 수 있게 한다.
- **행동 잠금 비대칭:** `PlayerActionGate`만 호출해도 기존 구르기·패링 쪽이 공격 상태를 모르면 동시 실행이 생긴다. `PlayerAttack`, `PlayerRollController`, `ParryController`가 서로의 `IsActionInProgress`를 모두 확인한 뒤 M2 양방향 행렬로 닫는다.
- **scheduler 순회 변경:** 현재 callback 목록에서 tick 중 add는 우선순서를 깨고 같은 tick 실행을 만들며 self-remove는 다음 callback을 건너뛴다. 등록은 pending-add로 다음 FixedTick부터 실행하고, 해제는 즉시 inactive 표시해 아직 차례 전인 callback을 같은 tick부터 막되 실제 변경은 `finally`에서 처리한다. M0의 등록/self-unregister/선행-unregister/예외 정리 회귀 테스트를 활 구현 merge gate로 둔다.
- **자식 collider와 buffer 포화:** collider 루트 정규화가 빠지면 중복 피해가 나고 16개 buffer가 차면 가까운 적이 누락될 수 있다. 부모 `NetworkIdentity` + 루트 `IDamageable` + 재사용 `netId` 집합을 공통 규칙으로 쓰고 overflow 카운터를 M3에서 측정한다.
- **화살 프리팹·수명 설정:** `NetworkIdentity`, 서버 권위 `NetworkTransformReliable`, spawnable prefab 등록, layer가 하나라도 빠지면 원격에 화살이 없거나 hit가 갈린다. 절대 만료 시각·canonical hit 집합·`_resolved` 단일 destroy와 함께 `OnStartServer` 의존성 검사 및 M1 hit/만료/벽 충돌 테스트를 release gate로 둔다.
- **호스트 이점:** 이 설계는 서버 rewind가 없어 150ms 원격 근접 판정이 보수적이다. M3 불일치율을 기록하고 SO 수치·표현 lead로 먼저 조정하며, 측정 없이 권위를 클라이언트로 옮기지 않는다.
- **공격 애니메이션 공백:** 현재 `PlayerAnimationController`에는 이동·구르기 중심 파라미터만 있다. 임시 모션/색 flash로 계약을 검증하고, 실제 공격 clip과 Animator 상태는 별도 애니메이션 파이프라인 설계에 연결한다.
- **범위 팽창:** 연사·다중 화살·관통·유도·치유·버프를 첫 PR에 넣으면 공통 계약 검증이 늦어진다. 1단계 검, 3단계 활 기본 한 발, 4단계 방울 pulse까지만 이 문서의 완료선으로 고정한다.
