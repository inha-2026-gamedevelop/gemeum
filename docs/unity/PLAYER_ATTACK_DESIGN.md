# PlayerAttack — 플레이어 기본 공격 설계

> 상태: **구현됨.** L1 통과, 근접 경로만 L2 통과, 원거리와 L3는 미검증
> 기준 코드: `8cfc8cd` (feature/Minsung, 2026-09-03)
> 관련: [프로젝트 구조 설계서](../04-architecture.md) · [네트워크 권위](NETWORK_AUTHORITY.md) · [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)

이 문서는 **지금 저장소에 있는 코드**를 기술한다. 2026-09-01 외부 감사와 함께 받은 설계 산출물이
원본이었고 구현은 그보다 작은 범위로 갔다. 원본 설계와 갈라진 지점과 그 근거는 8장에 모아 두었다.

---

## 1. 무엇을 만들었나

한 플레이어 프리팹에서 서버가 배분한 `WeaponKind`에 따라 기본 공격의 판정 방식만 바뀌는 공통 경로다.

```text
좌클릭 (소유 클라이언트)
  → PlayerAttack.CmdRequestAttack(requestId)
  → 서버: RequestId → 의존성 → 무기 → AttackReach → PlayerActionGate → 스태미나 차감
  → 선딜 뒤 PlayerController의 서버 물리 틱에서 판정
      Melee  → OverlapSphere 부채꼴 → IDamageable.TakeDamage
      Ranged → ArrowProjectile 스폰 → 화살이 자기 틱에서 SphereCast 판정
  → 후딜 동안 다음 공격, 구르기, 패링 거부
```

**클라이언트가 서버로 보내는 값은 `requestId` 하나뿐이다.** 무기, 공격력, 조준 방향, 명중 목록이
전부 없다. 방향은 서버가 `transform.forward`에서 읽는다.

### 이번 범위에 없는 것

락온과 화살 유도, 방울 기본 공격, 콤보와 차지, 쿨다운, 공격 애니메이션, 장비 공격력 연동,
클라이언트 예측, 특성 modifier. 각각의 사정은 7장과 8장에 있다.

---

## 2. 권위 경계

Command는 소유 플레이어 오브젝트에서만 보내고 서버에서 실행된다. `requiresAuthority = false`를
쓰지 않는다.

| 값 · 판정 | 서버 | 클라이언트 | 근거 |
|---|---|---|---|
| 좌클릭 | `requestId` 수신, `RequestIdTracker`로 중복·역순 차단 | `isLocalPlayer`일 때만 입력을 읽고 ID를 증가 | 입력 사실만 클라이언트에 있다 |
| 조준 방향 | `transform.forward`를 서버가 직접 읽는다 | 보내지 않는다 | 클라이언트가 방향을 보내면 등 뒤 적을 맞히도록 조작할 수 있다 |
| 현재 무기 | `PlayerWeapon._weapon` SyncVar | SyncVar를 외형과 UI로 읽기만 | 요청에 무기 값이 없다 |
| 판정 방식 | `WeaponKind.ToReach()` | 같은 규칙을 읽을 수 있으나 판정에 쓰지 않는다 | 규칙의 원본은 하나다 |
| 공격 수치 | `GameDB.Combat`의 `Melee*` / `Ranged*` | 같은 빌드의 에셋 | 판정 원본은 서버가 읽는다 |
| phase·생명·행동 잠금 | `RunManager`, `PlayerStatus`, `PlayerRollController`, `ParryController`, 자기 `_isSwinging` | 없음 | `PlayerActionGate` 재사용 |
| 스태미나 차감 | `PlayerStatus.TrySpendStamina` | SyncVar로 UI 표현 | 서버 원자적 차감 |
| 치명 게이지 | `ParryGauge.Gauge` 확인과 `ServerTryConsume()` | SyncVar로 UI 표현 | 다음 검 공격 1회 규칙 |
| 근접 hit와 피해 | 서버 물리 world에서 수행 후 `IDamageable.TakeDamage` | 없음 | AI와 HP 결과가 갈리면 안 된다 |
| 화살 생성·이동·명중·수명 | `ArrowProjectile`가 서버 스폰·틱·파괴 | 스폰된 화살을 `NetworkTransformUnreliable`로 받아 그린다 | 런타임 네트워크 오브젝트는 서버가 `NetworkServer.Spawn`한다 |
| 공격 시작 표현 | **없음** | **없음** | 7.2절, 공격 RPC와 애니메이션이 아직 없다 |

---

## 3. 구조

### 3.1 파일

| 경로 | 무엇 | 상태 |
|---|---|---|
| `00.Common/Types/AttackReach.cs` | 판정 방식 enum `None=0 / Melee=1 / Ranged=2` | 신규 |
| `00.Common/Types/WeaponKindExtensions.cs` | `WeaponKind.ToReach()` — 무기에서 판정 방식을 유도하는 **규칙의 원본** | 신규 |
| `02.Combat/Player/PlayerAttack.cs` | 입력, Command, 게이트, 타이밍, 근접 판정, 화살 발사 | 신규 |
| `02.Combat/Arrow/ArrowProjectile.cs` | 서버 권위 화살. 이동과 명중 판정 | 신규 |
| `02.Prefabs/Combat/ArrowProjectile.prefab` | 화살 네트워크 프리팹. 레이어 12(Projectile) | 신규 |
| `00.Common/Data/CombatConfigSO.cs` | `_melee*` 6개, `_ranged*` 6개 | 보강 |
| `02.Combat/Player/PlayerRollController.cs` | 시작 조건에 `_attack.IsActionInProgress` 추가 | 보강 |
| `02.Combat/Parry/ParryController.cs` | 6번 관문에 `_attack.IsActionInProgress` 추가 | 보강 |
| `02.Combat/Player/PlayerController.cs` | 서버 이동 틱에서 `_attack.ServerUpdateState(now)` 호출 | 보강 |

파트 경계는 다음과 같다.

```text
01.Network.RunManager                 ← phase, 일시정지 읽기
01.Network.AuthorityTickScheduler     ← 화살이 자기 틱을 등록

02.Combat.PlayerController            ← 서버 이동 틱에서 공격 상태를 진행시킨다
02.Combat.PlayerAttack
    ├─ PlayerStatus / PlayerWeapon / PlayerRollController / ParryController / ParryGauge
    └─ ArrowProjectile (Instantiate + NetworkServer.Spawn)

PlayerAttack / ArrowProjectile
    └─ 00.Common.IDamageable.TakeDamage(DamageInfo)
                     ▲
              03.AI.EnemyStatus
```

`Combat → AI` 직접 참조는 없다. `EnemyStatus`를 캐스팅하지 않고 `IDamageable`과 `DamageInfo`만 쓴다.
`AI`는 `PlayerAttack`과 `ArrowProjectile`을 모른다.

### 3.2 `PlayerAttack`

`Awake`에서 `PlayerStatus`, `PlayerWeapon`, `PlayerRollController`, `ParryController`, `ParryGauge`를
한 번 캐시한다. 인스펙터 필드는 셋이다.

| 필드 | 기본값 | 비고 |
|---|---|---|
| `_attackKey` | `Mouse0` | 조작 변경은 씬이 아니라 여기서 한다 |
| `_targetMask` | 코드 기본 `~0`, **프리팹 저장값 `3072`** | Enemy(10) \| Boss(11). 새 적 레이어가 생기면 여기도 넓힌다 |
| `_arrowPrefab` | `ArrowProjectile.prefab` | 비어 있으면 원거리 공격이 에러 로그만 남기고 나가지 않는다 |

서버 상태는 넷이다. **`SyncVar`가 아니다** — 1초 안팎의 일회성 상태라 서버만 안다.

```csharp
private double      _hitServerTime;       // 선딜이 끝나 판정하는 시각
private double      _recoveryServerTime;  // 이 시각까지 다음 행동을 막는다
private bool        _isSwinging;
private AttackReach _swingReach;
```

`IsActionInProgress(now)`는 `_isSwinging || now < _recoveryServerTime`이다. 구르기와 패링이
이 하나를 본다. **쿨다운을 별도로 두지 않아 후딜이 곧 다음 공격까지의 간격이다.**

`AttackProfile`은 무기에서 뽑아낸 이번 공격의 조건(판정 방식, 선딜, 후딜, 스태미나)을 묶은
`readonly struct`다. 게이트와 타이밍이 같은 값을 보게 하려고 한 번만 만든다.

### 3.3 판정을 서버 물리 틱에 얹은 이유

`PlayerAttack`은 `AuthorityTickScheduler`에 직접 등록하지 않는다. `PlayerController`의
`TICK_PRIORITY_MOVEMENT(0)` 틱 안에서 `_attack.ServerUpdateState(now)`가 불린다.

`Update`에서 돌리면 프레임과 물리 스텝이 어긋나 판정 시점의 플레이어 위치가 흔들린다.
이동을 확정하는 같은 틱에서 판정해야 눈에 보이는 위치와 판정 위치가 같다.

`Constants.Network.TICK_PRIORITY_ABILITY(300)`은 정의되어 있으나 **아직 아무도 쓰지 않는다.**

### 3.4 `ArrowProjectile`

`Constants.Network.TICK_PRIORITY_PROJECTILE(200)`에 자기 틱을 등록한다. `Update`를 쓰지 않는다.

- `OnStartServer`에서 스케줄러가 없으면 그 자리에서 `NetworkServer.Destroy`한다. 이동시킬 주체가
  없으면 화살이 영영 떠 있는다
- `OnStopServer`에서 정확히 한 번 해제한다
- `SetSimulationEnabled(false)`로 런이 멈추면 화살도 함께 멈춘다

**이동 구간을 `SphereCastNonAlloc`으로 훑는다.** 한 틱 이동이 `30 m/s × 0.02 s = 0.6 m`인데
명중 보조 반경은 `0.4 m`다. 제자리 구형 판정으로 찍으면 적을 그대로 통과한다.

- 결과를 거리순으로 삽입 정렬한다. `SphereCast` 결과는 거리순이 아니고, 최대 8개라 삽입 정렬이 가장 싸다
- 몸 하나에 콜라이더가 여럿이면 한 번 통과에 여러 번 맞는다. `NetworkIdentity.netId`를 기록해 한 번만 센다
- 관통을 다 쓰면 마지막 명중 지점으로 위치를 옮긴 뒤 `NetworkServer.Destroy`한다
- `RaycastHit[8]`과 `List<uint>(8)`을 미리 잡아 연사해도 GC 할당이 없다

**자기 자신은 레이어로 걸러진다.** `netId` 대조가 아니라 `_targetMask`가 Enemy와 Boss뿐이라
플레이어(레이어 9)가 애초에 후보에 들어오지 않는다.

프리팹 구성은 `NetworkIdentity` + `NetworkTransformUnreliable`(ServerToClient) + `ArrowProjectile`이고,
무기 외형인 `Arrow.prefab`을 자식으로 품는다. `Main`, `Dev_Combat`, `Dev_AI` 세 씬의
`spawnPrefabs`에 등록되어 있다. **등록이 빠지면 호스트에서만 멀쩡해 보이고 원격 클라이언트에서
스폰이 실패한다.**

---

## 4. 데이터

**새 ScriptableObject를 만들지 않았다.** 기존 `CombatConfigSO`와 `ArrowConfigSO`에 넣었다.

### 4.1 `CombatConfigSO` — 근접

`_attack*` 6개를 `_melee*`로 바꾸고 `[FormerlySerializedAs]`로 에셋에 저장된 값을 이었다.
실측으로 이관을 확인했다.

| 항목 | 프로퍼티 | 값 |
|---|---|---:|
| 선딜 | `MeleeStartupDuration` | `0.12 s` |
| 후딜 | `MeleeRecoveryDuration` | `0.28 s` |
| 스태미나 | `MeleeStaminaCost` | `12` |
| 사거리 | `MeleeRange` | `2.2 m` |
| 각도 | `MeleeAngle` | `90°` (좌우 합산) |
| 기본 공격력 | `MeleeBasePower` | `10` |

### 4.2 `CombatConfigSO` — 원거리

| 항목 | 프로퍼티 | 값 | 이유 |
|---|---|---:|---|
| 선딜 | `RangedStartupDuration` | `0.25 s` | 활을 당기는 시간이라 근접보다 길다 |
| 후딜 | `RangedRecoveryDuration` | `0.35 s` | |
| 스태미나 | `RangedStaminaCost` | `8` | 거리를 두고 싸우는 대신 근접보다 싸다 |
| 기본 공격력 | `RangedBasePower` | `8` | 근접 10보다 낮다 |
| 총구 높이 | `RangedMuzzleHeight` | `1.2 m` | 발밑에서 나가지 않게 |
| 총구 전방 | `RangedMuzzleForward` | `0.6 m` | 자기 몸에 걸리지 않게 |

기획서에 근접 수치표가 없어 전부 프로토타입 시작값이다. 코드 상수가 아니라 SO에 있으므로
재빌드 없이 교체할 수 있다.

### 4.3 `CombatConfigSO` — 치명

| 항목 | 프로퍼티 | 값 |
|---|---|---:|
| 게이지 최대 | `ParryGaugeMax` | `100` |
| 패링 1회 획득 | `ParryGaugeGainPerSuccess` | `35` |
| 치명 배율 | `CriticalMultiplierMin` / `Max` | `2.0` ~ `3.0` |

### 4.4 `ArrowConfigSO`

| 항목 | 값 | 지금 읽히는가 |
|---|---:|---|
| `ArrowSpeed` | `30 m/s` | 읽는다 |
| `ArrowLifetime` | `3 s` | 읽는다 |
| `UnlockedHitRadius` | `0.4 m` | 읽는다 |
| `BasePierceCount` | `1` | 읽는다, `Mathf.Max(1, ...)`로 clamp |
| `AimAssistAngle` | `12°` | **읽지 않는다** — 락온 없음 |
| `GuidanceDegreesPerSecond` | `30°/s` | **읽지 않는다** |
| `GuidanceDuration` | `0.4 s` | **읽지 않는다** |

유도 필드 3개는 락온 시스템이 생길 때까지 비어 있다. 쓰지도 않을 유도 코드를 미리 넣지 않았다.

### 4.5 공격력 원본

**장비 연동이 없다.** 공격력은 `MeleeBasePower` / `RangedBasePower` 하나뿐이고
`EquipmentSO`, `WeaponCombatSnapshot`, 등급 roll을 거치지 않는다.
04(인벤토리·장비)가 들어오면 이 두 값이 장비 snapshot으로 대체되어야 한다.

전체 피해 계산은 근접이 `MeleeBasePower × CriticalMultiplier`, 원거리가 `RangedBasePower`다.

---

## 5. 흐름

### 5.1 입력 → 서버 수락

`PlayerAttack.Update`가 `isLocalPlayer`일 때만 `_attackKey` 입력을 읽고
`CmdRequestAttack(_nextRequestId++)`을 보낸다. 로컬 예측 표현은 없다.

서버는 다음 순서로 검사한다. **한 조건이라도 실패하면 조용히 `return`한다** — 7.2절.

1. `RequestIdTracker.TryAccept` — 0, 중복, 역순 차단
2. `GameDB.IsInitialized`, `_status`, `_weapon` null 검사
3. `PlayerWeapon.HasWeapon` — 배분 전에는 무기 값 자체가 의미가 없다
4. `WeaponKind.ToReach()`로 `AttackProfile` 결정. `AttackReach.None`(방울)이면 **게이트 전에 거부**
5. `PlayerActionGate.CanStartAction` — phase, 생명, 일시정지, 무기, 행동 겹침, 후딜, 스태미나 잔액
6. `PlayerStatus.TrySpendStamina(profile.StaminaCost)` — 원자적 차감

여기까지 통과하면 상태를 commit한다.

```csharp
_isSwinging         = true;
_swingReach         = profile.Reach;
_hitServerTime      = now + profile.StartupDuration;
_recoveryServerTime = _hitServerTime + profile.RecoveryDuration;
```

행동 겹침은 세 방향 모두 닫혀 있다.

| 검사하는 쪽 | 보는 대상 |
|---|---|
| `PlayerAttack` | `PlayerRollController.IsRolling`, `ParryController.IsActionInProgress`, 자기 `_isSwinging` |
| `PlayerRollController` | `ParryController.IsActionInProgress`, `PlayerAttack.IsActionInProgress` |
| `ParryController` (6번 관문) | `PlayerRollController.IsRolling`, `PlayerAttack.IsActionInProgress`, 자기 상태 |

세 Command 모두 서버 메인 스레드에서 검증 직후 자기 상태를 바로 commit한다. 같은 프레임에 둘이
도착해도 먼저 실행된 쪽만 성공한다.

### 5.2 판정 — `ServerUpdateState(now)`

`PlayerController`의 서버 이동 틱마다 불린다.

1. `_isSwinging`이 아니면 즉시 반환
2. **휘두르는 도중 죽거나 런이 멈추면 판정 없이 취소한다.** `Cancel()`이 `_recoveryServerTime`도 0으로 돌린다
3. `now < _hitServerTime`이면 아직 선딜
4. `_swingReach`에 따라 `FireArrow` 또는 `ApplyMeleeHit`
5. `_isSwinging = false`. 후딜은 `_recoveryServerTime`이 지날 때까지 게이트가 막는다

코루틴을 쓰지 않는다. 사망과 런 중단에서 취소 지점을 따로 관리해야 하기 때문이다.

### 5.3 근접 판정 — `ApplyMeleeHit`

```text
origin  = transform.position
forward = transform.forward
cosHalf = cos(MeleeAngle / 2)
```

1. **치명 여부를 휘두르기 시작에 한 번 뽑는다.** `Gauge >= ParryGaugeMax`면 `Random.Range(2.0, 3.0)`.
   부채꼴에 여럿 걸려도 전부 같은 배율을 받는다
2. `Physics.OverlapSphereNonAlloc(origin, MeleeRange, _hitBuffer[16], _targetMask)`
3. 각 콜라이더마다
   - `transform.root == 내 root`면 건너뛴다 (자기 자신)
   - 수평 방향(`y = 0`)으로 `Dot(forward, dir) >= cosHalf`인지 확인 — **구만 쓰면 등 뒤도 맞는다**
   - `root`에서 `IDamageable`을 찾는다. 콜라이더가 자식에 붙어 있는 경우가 있다
   - `IsDead`면 건너뛴다
   - **이미 맞은 `netId`면 건너뛴다.** 몸 하나에 콜라이더가 여럿이면 한 번 휘두르기에 여러 번 맞는다.
     화살과 같은 canonical 루트 `netId` 규칙이고 `_hitNetIds`를 휘두르기마다 `Clear()`해 재사용한다
   - `TakeDamage(MeleeBasePower × multiplier, netId, Critical 또는 Light)`
4. **한 대라도 맞았을 때만** `ParryGauge.ServerTryConsume()`

`HitStrength.Critical`을 함께 넘겨 아트가 표현을 다르게 줄 수 있게 했다.

**게이지 소비 규칙: 빗나가면 보존한다. 2026-09-03 확정.** 패링 세 번으로 모은 보상이 빗나감 한 번에
사라지면 아까워서 안 쓰게 된다. 기획서에 "다음 공격"이라고만 되어 있어 이쪽을 골랐고
원본 설계(빗나가도 소비)는 채택하지 않는다 — 8장.

**원거리에는 치명을 붙이지 않았다.** 기획서 1장이 게이지를 검 전용 상태로 정하고 있고,
근접 판정에 오는 무기는 지금 검뿐이다.

### 5.4 원거리 — `FireArrow`

```text
origin = position + up × RangedMuzzleHeight + forward × RangedMuzzleForward
```

`Instantiate` → `ServerInitialize(forward, RangedBasePower, netId, _targetMask, GameDB.Arrow)` →
`NetworkServer.Spawn`. 발사 뒤 화살은 자기 값으로 독립 처리하므로 쏜 사람이 죽어도 계속 난다.

화살은 매 틱 남은 수명을 `deltaTime`만큼 줄이고, 0 이하가 되면 `NetworkServer.Destroy`한다.
서버 물리 스텝은 고정이라 이 누적은 프레임 수와 무관하다.

### 5.5 거부와 취소 경로

| 상황 | 처리 |
|---|---|
| `requestId`가 0, 중복, 역순 | 무시. 자원 변화 0 |
| `GameDB` 미초기화, 컴포넌트 없음 | 무시 |
| 무기 미배분 | 무시 |
| 방울(`AttackReach.None`) | **게이트 전에 무시.** 입력이 아무것도 하지 않는다 |
| 로비, 결과, 일시정지, 사망, 관전 | `PlayerActionGate`가 거부 |
| 공격·구르기·패링 진행 중 | `PlayerActionGate`가 거부 |
| 후딜 중 | `PlayerActionGate`가 `CooldownEndServerTime`으로 거부 |
| 스태미나 부족 | `PlayerActionGate` 또는 `TrySpendStamina`가 거부 |
| 선딜 중 사망, 런 중단 | `Cancel()`. 판정 없음, 스태미나 환급 없음 |
| 근접이 빗나감 | 정상 종료. 게이지는 보존 |
| 화살 프리팹 미연결 | 에러 로그. 스태미나는 이미 차감된 뒤다 |
| 화살 스폰 뒤 쏜 사람 파괴 | 화살이 자기 값으로 계속 처리, `attackerNetId`만 귀속에 쓴다 |
| 스케줄러 없음 | 화살이 `OnStartServer`에서 자기를 파괴 |

---

## 6. 검증 현황

| 수준 | 결과 |
|---|---|
| L1 정적 | **통과.** 컴파일 에러 0, 신규 경고 0 |
| L2 근접 | **통과** (`Dev_Combat`, 에디터) |
| L2 원거리 | **미실행** |
| L3 3인 | **미실행** |

L2에서 실제로 관찰한 것.

```
로비 단계, 무기 없음     → 게이트가 거부 (phase == Lobby)
무기 지급 + 런 시작 후   → recovery = 요청시각 + 0.12 + 0.28 로 정확히 갱신
실제 잡귀에게 피해       → 적 HP 250 → 240 (MeleeBasePower 10)
_targetMask              → 런타임 3072 확인
3인 역할 배분            → Chasa/Sword, Singung/Bow, Munyeo/Bell
```

덤으로 두 가지가 관측됐다. 창귀가 플레이어를 실제로 다운시켜 **플레이어 HP 경로가 살아 있음**이
확인됐고, 그 과정에서 판정이 안 걸린 이유가 플레이어의 선사망이었던 것으로 **사망 시 공격 취소**도
증명됐다.

### 남은 검증 항목

**근접 (검, 차사)**
- 회귀 — 잡귀 HP가 10 깎이는가
- 패링 3회 성공(35 × 3 → 100 상한) 뒤 공격의 피해가 2.0 ~ 3.0배인가
- **빗나갔을 때 게이지가 남아 있는가** — 남기는 쪽으로 정했다
- 대상 중복 제거는 **지금 관찰할 수 없다.** 잡귀 3종이 루트 콜라이더 하나씩이라 증상 자체가
  안 나온다. 다부위 적이나 보스가 들어올 때 함께 본다

**원거리 (활, 신궁)**
- 총구(높이 1.2, 전방 0.6)에서 나가 자기 몸에 걸리지 않는가
- 잡귀 HP가 8 깎이는가
- **화살이 적을 통과하지 않는가** — 이번 구현에서 가장 틀리기 쉬웠던 지점이다
- 3초 뒤 소멸하는가

**방울 (무녀)**
- 공격 입력이 아무것도 하지 않는가 (의도된 동작)

**행동 겹침**
- 공격 중 구르기, 공격 중 패링이 각각 거부되는가

**L3 — 네트워크 변경이라 예외 없이 필요**
- 원격 클라이언트에서 다른 사람의 화살이 보이는가 (`spawnPrefabs` 등록이 실제로 먹혔는지)
- 세 사람이 같은 적을 동시에 칠 때 자원 이중 차감과 중복 피해가 0인가

---

## 7. 알려진 결함과 빈 곳

우선순위 순이다.

### 7.1 화살이 벽을 통과한다

`_targetMask`가 Enemy와 Boss뿐이라 지형과 장애물이 `SphereCast` 후보에 없다. 벽 뒤의 적을
그대로 맞힌다. 환경 레이어를 마스크에 넣고 먼저 만나면 종료하는 처리가 필요하다.

### 7.2 공격 표현이 전혀 없다

공격 시작을 알리는 `ClientRpc`가 없고 `PlayerAnimationController`에도 공격 트리거가 없다.
`PlayRoll`만 있다. 지금은 판정만 돌고 모션과 이펙트가 없다.

거부 결과를 소유자에게 알리는 `TargetRpc`도 없어 클라이언트는 자기 요청이 왜 씹혔는지 모른다.
예측 표현이 생기면 blend-out 시점을 알기 위해 필요해진다.

### 7.3 무기 손 위치가 미조정

`Player.prefab → PlayerWeaponVisual → _bindings` 3개가 전부 0이라 무기가 몸통에 파묻힌다.
부착 자체는 정상이다. 눈으로 맞추는 작업이라 코드로 정할 수 없다.

### 7.4 화살 sweep 버퍼 포화 처리가 없다

`RaycastHit[8]`이 가득 차면 가까운 대상이 누락됐는지 알 수 없는데 지금은 그냥 8개만 처리한다.
진단 카운터도 없다.

### 7.5 옛 이름이 남아 있다

`CharacterKind`로 타입만 바꿨고 `CmdSelectAppearance`, `_appearance`, `HasAppearance`,
프리팹에 직렬화된 `Appearance` 필드가 옛 이름 그대로다. 직렬화가 걸려 있어
`[FormerlySerializedAs]`가 필요하다.

---

## 8. 원본 설계와 갈라진 지점

2026-09-01 감사 산출물의 설계와 구현이 다른 곳들이다. **코드가 현재의 사실이고 이 표는 왜 그렇게
됐는지의 기록이다.**

| 항목 | 원본 설계 | 구현 | 근거 |
|---|---|---|---|
| 데이터 | 신규 `WeaponAbilityConfigSO` + `BasicAttackEntry[3]` | 기존 `CombatConfigSO`에 `_melee*` / `_ranged*` 평면 필드 | 무기가 3종이고 각 수치가 6개다. SO 하나와 배열 검증 규칙을 새로 만드는 값보다 필드 12개가 작다 |
| 구조 | `IWeaponAbility` / `IAbilityRuntime` + `SwordAbility` / `BowAbility` / `BellAbility` | `PlayerAttack` 안에서 `AttackReach` switch 2갈래 | 분기가 둘뿐이고 방울은 판정이 없다. 원본이 "고르지 않은 방식"으로 적었던 switch를 골랐다 — 8.1 |
| 방울 | 원형 pulse (4 m, 360°, 0.65배) | 기본 공격 없음 | **사용자 결정.** 치유와 버프 설계가 끝난 뒤 별도 작업 |
| 검 수치 | 0.20 / 0.35 / 15 / 2.4 m / 100° | 0.12 / 0.28 / 12 / 2.2 m / 90° | 어느 쪽도 기획 근거가 없는 프로토타입 값이다. SO에 있어 교체가 싸다 |
| 쿨다운, Active 구간 | 있음 | 없음 (선딜, 후딜만) | 후딜이 곧 다음 공격까지의 간격이라 콤보가 없는 지금은 구분이 결과를 바꾸지 않는다 |
| 게이지 API | `ServerTryConsume(out float multiplier)` | `ServerTryConsume()` bool, 배수는 `PlayerAttack`이 뽑는다 | 기존 API가 이미 있었고 소비하는 쪽만 없었다. 시그니처를 바꾸지 않고 연결했다 |
| **게이지 소비 규칙** | **빗나가도 정확히 한 번 소비** | **빗나가면 보존** | 5.3절. 2026-09-03 확정, 뒤집지 않는다 |
| request 내용 | `RequestId` + `AimDirection` + `CandidateTargetNetId` | `requestId`만 | 락온이 없어 후보 ID가 쓸 데가 없고, 방향은 서버가 읽으면 검증이 필요 없다 |
| 결과 통지 | `AbilityResultCode` 10종 + `TargetRpc` | 없음, 조용히 `return` | 7.2절. 예측 표현이 없어 아직 받을 쪽이 없다 |
| 화살 수명 | 절대 만료 시각 비교 | `deltaTime` 누적 | 서버 물리 스텝이 고정이라 결과가 같다 |
| 화살 장애물 | 벽을 만나면 종료 | 통과 | 7.1절, 미구현 |
| 공격력 원본 | `WeaponCombatSnapshot` 또는 `EquipmentSO` bridge | `CombatConfigSO`의 base power | 4.5절. 04가 들어오면 대체 |
| 틱 우선순위 | `TICK_PRIORITY_ABILITY(300)` | `PlayerController`의 `MOVEMENT(0)` 틱에 얹음 | 3.3절. 이동을 확정하는 같은 틱에서 판정해야 위치가 흔들리지 않는다 |
| `AuthorityTickScheduler` 보강 | pending-add, 즉시 무효 표시, 순회 뒤 정리 | **그대로 구현됨** | 갈라지지 않은 유일한 선행 항목이다 |

### 8.1 왜 실행기 분리 대신 switch인가

원본 설계는 `PlayerAttack` 안의 `switch (WeaponKind)`를 "세 사람이 같은 파일을 수정하고 특성 분기가
늘어 회귀 범위가 커진다"는 이유로 배제했다. 그 우려는 유효하다. 그럼에도 지금 코드를 유지하는 근거는
셋이다.

- 실제 분기는 **둘**이다. 방울은 판정이 없어 게이트 앞에서 끝난다
- 공격을 고치는 사람은 지금 팀장 한 명이다. 세 사람이 같은 파일을 만지는 상황이 아직 없다
- 무기별 로직은 `ApplyMeleeHit`, `FireArrow` 두 메서드로 이미 갈라져 있다

**분리로 넘어갈 신호는 명확하다.** 특성 modifier가 들어오거나, 무기가 4종 이상이 되거나,
방울 pulse가 실제로 구현되면 그때 `IWeaponAbility`로 뽑아낸다. 지금 미리 분리하면 인터페이스 셋과
context 구조체 셋이 사용처 하나씩만 가진 채 남는다.

### 8.2 유지되는 판단

원본 설계에서 그대로 지킨 것들이다. 뒤집지 않는다.

- **네트워크 입구는 `PlayerAttack : NetworkBehaviour` 하나.** 무기마다 Command를 두면
  requestId, phase, 자원, 행동 잠금, 결과 통지를 세 번 복제한다
- **클라이언트는 무기, 공격력, 명중 목록을 보내지 않는다.** 보내면 소유자가 조작할 수 있고
  지연과 재전송에서 결과가 갈린다
- **화살은 서버만 스폰한다.** 클라이언트 스폰 뒤 서버 승인은 동일 화살 매칭과 rollback이 필요하다
- **근접에 client prediction과 서버 rewind를 넣지 않는다.** 위치 히스토리와 재판정이
  새 하위 시스템이 된다. L3에서 150 ms 불일치율을 먼저 재고, 측정 없이 권위를 옮기지 않는다
- **직업별 플레이어 프리팹을 나누지 않는다.** 한 프리팹에서 서버가 배분한 무기로 갈린다
- **화살 네트워크 풀링을 지금 하지 않는다.** Profiler가 병목을 보일 때 후속 최적화한다
- **밸런스 수치는 SO에만 둔다.** 코드 상수로 박지 않는다
- **레이어는 밸런스가 아니다.** SO가 아니라 `LayerMask` 인스펙터 필드에 둔다

---

## 9. 다음에 할 것

순서는 위험도 순이다.

1. **옛 이름 정리** `Appearance → Character` (7.5). 직렬화가 걸려 있어 테스트 전에 넣어야 한다
2. **몰아서 테스트** (6장). L2 원거리와 L3 전부
3. **화살 장애물 처리** (7.1)
4. **공격 애니메이션과 표현** (7.2). 클립 준비 여부 확인이 선행
5. **락온과 화살 유도**. 락온 대상을 정하는 시스템이 생긴 뒤 기획서 4.1을 붙인다
6. **방울 기본 공격**. 치유와 버프 설계가 선행

### 2026-09-03에 닫힌 것

- **`RequestIdTracker`의 `readonly` 제거.** 가변 `struct` 필드가 `readonly`라 메서드 호출마다
  방어 복사본이 만들어져 `_lastAcceptedRequestId`가 원본에 남지 않았다. 0이 아닌 모든 `requestId`가
  통과하고 있었다 — 중복과 역순 차단이 이제 실제로 동작한다
- **근접 대상 중복 제거.** 화살과 같은 canonical 루트 `netId` 규칙을 `ApplyMeleeHit`에 붙였다.
  한 적에 콜라이더가 여럿이면 한 번 휘두르기에 여러 번 맞던 것을 막는다 — 5.3절
- **게이지 소비 규칙을 보존으로 확정.** 원본 설계(빗나가도 소비)는 채택하지 않는다 — 5.3절, 8장

---

## 10. 주의

- **`[Command]` 본문을 리플렉션으로 부르면 한 프레임 뒤에 실행된다.** 위버가 본문을 네트워크
  메시지 전송으로 바꿔 놓기 때문이다. 에디터 검증에서 값이 안 바뀐 것처럼 보이면 다음 프레임에 다시 읽는다
- **`CharacterKind`와 `WeaponKind`의 숫자는 1:1로 짝이다.** 어느 한쪽을 재배열하면 캐릭터와 무기가
  어긋나고 직렬화된 프리팹과 씬 값도 함께 깨진다
- **`_targetMask`는 프리팹에 저장된 값이다.** 코드 기본값 `~0`이 아니라 `3072`가 실제로 쓰인다
- **`spawnPrefabs` 등록은 씬마다 따로다.** 빠지면 호스트에서만 보인다
- 플레이어 옆에 잡귀를 스폰해 시험하면 **플레이어가 먼저 죽는다.** 창귀 공격력 14, 플레이어 HP 100
