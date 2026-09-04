# 결계 · 인벤토리 · 상점 설계

> 상태: 설계 확정본, 구현 전  
> 출처: 2026-09-01 외부 소스 감사와 함께 받은 설계 산출물. 저장소 문서로 편입했다  
> 관련: [프로젝트 구조 설계서](../04-architecture.md) · [네트워크 권위](NETWORK_AUTHORITY.md) · [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)

> 상태: 구현 제안. 코드·씬·프리팹은 수정하지 않았다.
>
> 핵심 결정: `RunManager`의 phase를 유일한 런 진행 원본으로 유지하고, 결계는 **전환 메타데이터만 동기화**, 인벤토리는 **정확히 4칸인 owner-only `SyncList`**, 구매 Command는 **소유 플레이어 오브젝트**에 둔다.

## 1. 목표와 범위

이번에 만드는 것은 다음 세 묶음이다.

- `Farming → Shrink1 → Shrink2 → Shrink3 → Boss → Result`에 맞춰 반경을 30초 동안 줄이고, 서버가 결계 밖 피해와 회복 규칙을 적용한다.
- 플레이어별 4칸 고정 인벤토리, 서버 생성 장비 인스턴스, 줍기·장착 교환·사용·3m 내 양도를 제공한다.
- 팀 공용 노잣돈, `Shrink2`에서만 열리는 객주, 물약 구매와 선택 무기의 재조정/등급 상승을 서버 트랜잭션으로 처리한다.

만들지 않는 것은 인벤토리 확장, 제작·합성, 창고, 우편, 경매장, 상점 재고 랜덤화, 저장 데이터 마이그레이션, 런 밖 영속 인벤토리다.

인벤토리 칸은 **4칸으로 확정**한다. 3칸도 기획 허용 범위지만 회복·신력 물약이 각각 최대 3개라 두 물약 스택만으로 2칸을 사용한다. 4칸이면 물약 2스택과 비교용 장비 2개가 남고, UI/네트워크 구조는 여전히 고정 크기다. 물약은 종류별 한 슬롯에 최대 3개까지 쌓고, 재조정권·상승권은 구매 즉시 선택 무기에 적용하여 슬롯을 차지하지 않는다.

## 2. 권위 경계

### 2.1 공통

| 값 · 판정 | 서버 | 클라이언트 | 근거 |
|---|---|---|---|
| `RunPhase`, 경과 시간 | 기존 `RunManager`만 변경 | SyncVar 읽기 | 별도 시스템이 자기 시계로 phase를 바꾸지 않는다. |
| 모든 요청 | 소유 플레이어의 `[Command]`를 받아 호출자·phase·거리·소유권·request ID 재검증 | 입력과 예상 UI만 | Mirror Command는 기본적으로 자기 플레이어 오브젝트에서만 호출할 수 있다. [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions) |
| SO 데이터 | 서버가 판정 원본으로 읽음 | 표시용 동일 자산 읽기 | 클라이언트가 보낸 가격·피해·아이템 수치는 무시한다. |

### 2.2 결계

| 값 · 판정 | 서버 | 클라이언트 | 배치 |
|---|---|---|---|
| 보스 지점 (결계 최종 중심) | **런 시작 때 한 번 확정** | 읽기만 | `RunManager`가 소유, `[SyncVar]` |
| 축소 시작/종료 **중심**과 반경, 서버 시각 | phase 전환 때 한 번 확정 | 수신값으로 매 프레임 시각 보간 | `[SyncVar(hook=...)] BarrierTransitionState` |
| 현재 판정 반경 | 서버가 같은 순수 함수로 필요 시 계산 | 렌더러가 같은 함수로 계산 | 매 프레임 SyncVar 금지 |
| 외부 여부·피해·쇠약·회복 봉쇄 | 서버 1초 Tick | 화면 효과/UI만 | `[Server] BarrierController.ServerDamageTick` |
| 축소 전 경고 | 서버가 다음 경계 10초 전 1회 확정 | 사운드·UI 표시 | `ClientRpc` 또는 동기화 revision 이벤트 |

### 2.3 인벤토리·상점

| 값 · 판정 | 서버 | 클라이언트 | 배치 |
|---|---|---|---|
| 드랍 내용·InstanceId | 서버 생성, `NetworkServer.Spawn` | 표시 정보 조회 | `DroppedItem : NetworkBehaviour` |
| 슬롯 내용 | 서버가 시작 시 Empty 4개를 만들고 이후 index Set만 수행 | 소유자 UI | `readonly SyncList<InventorySlotNet>`, Sync Mode `Owner` |
| 장착 무기 | 서버가 교환 | 모든 관찰자가 무기 종류/외형을 읽음 | `PlayerWeapon`의 SyncVar snapshot |
| 팀 노잣돈 | 서버 `TryCredit/TrySpend/ApplyLossRatio` | 잔액 UI | `CurrencyManager`의 SyncVar |
| 상점 구매·가격·소지 제한 | 서버가 `ShopSO`와 현재 상태로 판정 | 상품 표시와 요청만 | `InteractionController.CmdBuy` |

Mirror는 서버→클라이언트 상태에 SyncVar/SyncList를 제공하며, 인벤토리처럼 다른 플레이어에게 불필요한 데이터는 `Sync To Owner`를 권장 예로 든다. 3인에서는 대역폭 절감 폭이 작아도 권한 경계와 UI 노출 범위가 명확해진다. [Mirror Synchronization](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization), [Mirror SyncLists](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization/synclists)

## 3. 구조

### 3.1 공용 계약 — namespace `Geumeum.Common`

| 파일 위치 / namespace | 책임 |
|---|---|
| `Assets/01.Scripts/00.Common/Interfaces/IRecoveryTarget.cs` / `Geumeum.Common` | **[구현됨 · 2026-09-04]** `bool IsDead`, 서버의 `bool TryRestoreHealth(float)`; `PlayerStatus` 구현. 호출부는 **원본 회복량만** 넘긴다 |
| `Assets/01.Scripts/00.Common/Interfaces/IBarrierState.cs` / `Geumeum.Common` | **[구현됨 · 2026-09-04]** `IsActive`, `Center`, `Radius`, `IsOutside(Vector3)`, `GetHealingMultiplier(Vector3)`. `BarrierController`가 구현한다 |
| `Assets/01.Scripts/00.Common/Utility/BarrierStateLocator.cs` / `Geumeum.Common` | **[구현됨 · 2026-09-04]** `IBarrierState` 등록·조회. `BarrierController`가 `OnEnable`/`OnDisable`에서 `Register`/`Unregister`한다. 기본값은 결계가 꺼진 것으로 답하는 Null 구현 |

| `Assets/01.Scripts/00.Common/Types/WeaponCombatSnapshot.cs` / `Geumeum.Common` | `InstanceId`, `WeaponKind`, `Grade`, `AttackPower`, 옵션 최대 2개의 kind/value를 가진 고정 크기 값 |
| `Assets/01.Scripts/00.Common/Interfaces/IWeaponLoadoutTarget.cs` / `Geumeum.Common` | 현재 snapshot 조회와 서버 원자적 `TrySwapWeapon` 계약; `PlayerWeapon` 구현 |
| `Assets/01.Scripts/00.Common/Types/InventoryItemKind.cs` / `Geumeum.Common` | `enum : byte`, `Empty=0`, `Equipment=1`, `HealthPotion=2`, `SpiritPotion=3`; 명시값 유지 |
| `Assets/01.Scripts/00.Common/Events/WeaponLoadoutChangedEvent.cs` / `Geumeum.Common` | player netId, 이전/새 snapshot을 전달해 특성 초기화·표현 갱신·스탯 재구축을 한 번만 시작 |
| `Assets/01.Scripts/00.Common/Types/CurrencyLossReason.cs` / `Geumeum.Common` | `enum : byte`, `None=0`, `Revive=1`; 재배열 금지 |
| `Assets/01.Scripts/00.Common/Events/PartyCurrencyLossRequestedEvent.cs` / `Geumeum.Common` | `PlayerNetId:uint`, `DeathInstanceId:uint`, `Reason:CurrencyLossReason`을 Network의 부활 처리에서 Systems의 지갑으로 전달; 비율은 포함하지 않음 |
| `Assets/01.Scripts/00.Common/Events/EnemyDiedEvent.cs` / `Geumeum.Common` | 03 문서의 nullable kind/factory 계약과 함께 일반 적의 spawn 시점 `FieldLevelAtSpawn:int`, `RewardMultiplier:float`을 전달; 보스는 kind null |

> ⚠️ **2026-09-04 정정 — 쇠약·회복 봉쇄는 push가 아니라 pull이다.**
> 초판은 결계가 `SetBarrierRecoveryRule(float, bool)`로 규칙을 **밀어 넣는** 방식이었으나, 실제 구현은 `PlayerStatus.TryRestoreHealth`가 `BarrierStateLocator.Current.GetHealingMultiplier(position)`를 **물어보는** 방식이다.
>
> 밀어 넣는 방식은 대상마다 규칙 사본이 생겨, 결계 중심이 움직일 때(D9) 갱신 누락이 곧 판정 불일치가 된다. 물어보는 방식은 사본이 없다.
>
> **`TryRestoreSpirit(float)`은 구현하지 않았다.** 신력은 자연 회복이 없고 쇠약·회복 봉쇄의 대상도 아니다([게임 디자인 2.1](../02-game-design.md)). 필요해지면 그때 같은 인터페이스에 추가한다.

Combat와 Systems는 서로의 구체 클래스를 참조하지 않는다. `PlayerStatus`와 `PlayerWeapon`은 Common 계약을 구현하고, Systems 쪽 어댑터는 그 인터페이스만 캐싱한다. `WeaponCombatSnapshot`에는 `ScriptableObject`, `GameObject`, 배열을 넣지 않는다.

### 3.2 결계 — `Assets/01.Scripts/04.Systems/Barrier`, namespace `Geumeum.Systems`

| 파일 위치 / namespace | 책임 | 기존 접점 |
|---|---|---|
| `Assets/01.Scripts/04.Systems/Barrier/BarrierController.cs` / `Geumeum.Systems` | 현재 phase bootstrap/구독, 전환 상태 SyncVar, 경고, `TICK_PRIORITY_BARRIER=350` 서버 Tick | `RunPhaseChangedEvent`, `GameDB.Barrier`, `AuthorityTickScheduler` |
| `Assets/01.Scripts/04.Systems/Barrier/BarrierTransitionState.cs` / `Geumeum.Systems` | `FromCenter/TargetCenter: Vector3`, `FromRadius/TargetRadius: float`, `StartServerTime/EndServerTime: double`, `Revision: uint`, `IEquatable<T>` | Mirror 지원 struct |
| `Assets/01.Scripts/04.Systems/Barrier/BarrierJudge.cs` / `Geumeum.Systems` | XZ 거리 제곱과 double 서버 시각으로 반경/외부 여부 계산하는 순수 C# | 없음 |
| `Assets/01.Scripts/04.Systems/Barrier/BarrierTargetAdapter.cs` / `Geumeum.Systems` | 플레이어 루트에서 `IDamageable`/`IRecoveryTarget` 1회 캐싱, 서버 registry 등록 | Common 계약과 `DamageInfo.FromBarrier` |
| `Assets/01.Scripts/04.Systems/Barrier/BarrierTargetRegistry.cs` / `Geumeum.Systems` | 최대 3명의 어댑터를 고정/재사용 목록으로 보관 | 플레이어 서버 수명 |
| `Assets/01.Scripts/05.Art/BarrierVisual.cs` / `Geumeum.Art` | 전환 상태를 읽어 셰이더·미니맵·외부 화면효과만 갱신 | 기준 아키텍처의 기존 계획명, `BarrierController` 공개 읽기 API |

`BarrierController`는 `BarrierCenter`의 직렬화 참조를 사용하고 `Find`하지 않는다. 서버와 클라이언트가 쓰는 순수 함수는 다음과 같다.

```text
t      = InverseLerp(StartServerTime, EndServerTime, NetworkTime.time)
k      = SmoothStep01(t)
center = Lerp(FromCenter, TargetCenter, k)          // 중심도 함께 움직인다
radius = Lerp(FromRadius, TargetRadius, k)
outside = (playerXZ - center.XZ).sqrMagnitude > radius * radius
```

**중심과 반경은 같은 `k`를 쓴다.** 따로 보간하면 전환 도중 "그려진 원"과 "때리는 원"이 어긋난다.

`OnStartServer`는 이벤트를 먼저 구독한 뒤 현재 `RunManager.ServerInstance.Phase`를 같은 handler로 bootstrap하고, `OnStopServer`에서 이벤트와 scheduler 등록을 모두 해제한다. `Constants.Network`에는 `TICK_PRIORITY_BARRIER = 350`을 추가해 ability 판정(300) 뒤, status 회복(400) 전에 결계 피해·회복 규칙이 확정되게 한다.

서버 피해는 `AuthorityTickScheduler`에서 누적 시간으로 1초마다 최대 한 번 실행한다. 긴 프레임 정지 뒤 여러 번의 피해를 한 프레임에 몰아넣지 않고 다음 Tick을 `now + interval`로 재예약한다. 정확한 벽시계 DPS보다 호스트 hitch로 인한 즉사를 막는 편이 3인 리슨 서버에 적합하다.

현재 `PlayerStatus.TakeDamage`는 구르기·패링 성공의 `_isInvincible`에서 환경 피해도 버리므로 그대로 사용하지 않는다. Common `DamageInfo`에 `DamageFlags.IgnoreActionInvincibility`와 `FromBarrier(float)`를 추가한다. `PlayerStatus`는 `ReviveInvulnerable`은 계속 존중하되 이 flag가 있으면 구르기/패링 무적만 건너뛴다. 따라서 결계 DPS를 구르기로 무효화할 수 없고, 부활 직후 2초 보호는 유지된다.


### 3.2.1 `IBarrierState` 구현 계약 — 이 절은 빈칸을 남기지 않는다

`BarrierController`가 `Geumeum.Common.IBarrierState`를 구현하고 `BarrierStateLocator`에 자신을 등록한다. **결계를 읽는 파트가 넷(전투·AI·아트·UI)이므로, 아래를 정확히 지키지 않으면 파트마다 다른 답이 나온다.**

#### 등록 시점 — `OnEnable` / `OnDisable`이다

```csharp
private void OnEnable()  => BarrierStateLocator.Register(this);
private void OnDisable() => BarrierStateLocator.Unregister(this);
```

**`OnStartServer`에 두면 안 된다.** `BarrierVisual`(아트)과 미니맵 원(UI)은 **클라이언트에서** 이 인터페이스를 읽는다. 서버 콜백에만 등록하면 클라이언트에는 Null 구현만 남아 **화면에 결계가 아예 안 그려지고, 원인이 보이지 않는다.**

`Unregister`를 빠뜨리면 static 참조라 씬 전환 뒤에도 파괴된 객체가 남는다. `BarrierStateLocator.Unregister`는 등록한 본인이 아니면 무시하므로 교체 순서가 엇갈려도 안전하다.

#### 서버와 클라이언트의 답은 다를 수 있다 — 용도가 다르다

둘 다 3.2의 같은 순수 함수를 쓰지만, 클라이언트는 SyncVar 수신 지연만큼 뒤처진 `BarrierTransitionState`로 계산한다. **따라서 값이 미세하게 다른 것이 정상이다.**

| 실행 위치 | `IsOutside()` 용도 | 금지 |
|---|---|---|
| **서버** | 피해·회복 판정. **권위** | — |
| **클라이언트** | 화면 효과 · UI · 사운드 · 셰이더 | 🚫 **게임 상태를 바꾸는 데 쓰지 않는다** |

> **클라이언트 코드에서 `IsOutside()` 결과로 HP·자원·아이템·phase를 바꾸면 안 된다.** 사람마다 결과가 갈린다. 클라이언트가 "나 지금 밖이다"라고 판단해도 그것은 **빨간 화면을 켜는 근거일 뿐** 피해의 근거가 아니다.

#### 반환값 규약

| 멤버 | 규약 |
|---|---|
| `IsActive` | `RunPhase`가 `Farming`·`Shrink1`·`Shrink2`·`Shrink3`일 때만 `true`. `Lobby`·`Boss`·`Result`는 `false` |
| `Center` · `Radius` | 보간 **중인 현재 값**. 전환 30초 동안 매 호출마다 다르다. `IsActive == false`면 의미 없음 — 호출부가 먼저 확인한다 |
| `IsOutside(pos)` | XZ 평면 거리만. 높이는 보지 않는다. `IsActive == false`면 항상 `false` |
| `GetHealingMultiplier(pos)` | 아래 표 |

**`Center`·`Radius`를 프레임을 넘겨 캐싱하지 않는다.** 전환 중에는 값이 계속 변한다. 한 프레임 안에서 여러 번 쓰려고 지역 변수에 받는 것은 된다.

#### `GetHealingMultiplier` 결정표 — 분기를 여기서 끝낸다

값은 전부 `BarrierPhaseSO`의 현재 `PhaseEntry`에서 읽는다. **상수로 박지 않는다.**

| 위치 | `BlocksHealing` | `AppliesWeakened` | 반환 |
|---|---|---|---|
| 결계 **안** | — | — | **1** |
| 결계 밖 | false | false | **1** |
| 결계 밖 | false | true | **`WeakenedHealingMultiplier`** (에셋값 0.7) |
| 결계 밖 | true | — | **0** |

**봉쇄가 쇠약보다 우선한다.** 둘 다 켜져 있으면 0이다. `IsActive == false`면 항상 1이다.

호출부(`PlayerStatus.TryRestoreHealth`)는 **원본 회복량만** 넘기고 이 배수를 곱해 적용한다. 결과가 0이면 회복이 일어나지 않았으므로 `false`를 돌려주고, 호출부는 자원(신력·물약)을 소비하지 않는다.

#### 결계 밖 잡귀는 AI 파트가 이 인터페이스로 판정한다

[착수 계획 D6-b](../action-plan-2026-09-03.md)의 "결계 축소 시 밖의 잡귀를 안쪽으로 이동"은 **몬스터 파트가 구현**하되 판정은 여기를 쓴다.

```csharp
// 03.AI — RunPhaseChangedEvent 를 받았을 때 한 번
if (BarrierStateLocator.Current.IsOutside(enemy.position)) { /* 안쪽으로 이동 */ }
```

**직접 거리 계산 금지.** `SpawnManager`가 자기 XZ 규칙을 다시 구현하면 결계 중심이 움직일 때(D9) 어긋난다. 그리고 이 판정은 **서버에서만** 한다 — 잡귀 위치는 서버 권위다.

#### 금지 목록 — 여기서 틀리면 3인 접속에서만 보인다

| # | 금지 | 왜 |
|---|---|---|
| 1 | 매 프레임 `Radius`·`Center`를 SyncVar로 보내기 | 전환 struct 1회 동기화로 충분하다 (8장) |
| 2 | `BarrierCenter`를 `Find` 계열로 찾기 | 직렬화 참조를 쓴다 |
| 3 | 클라이언트에서 `IsOutside()` 결과로 게임 상태 변경 | 사람마다 결과가 갈린다 |
| 4 | `Register`를 `OnStartServer`에 두기 | 클라이언트에 결계가 안 그려진다 |
| 5 | `Unregister` 누락 | static이라 죽은 참조가 남는다 |
| 6 | 각 파트가 XZ 거리를 다시 계산 | 그리는 원과 때리는 원이 갈라진다 |
| 7 | 회복 규칙을 대상에게 push | 사본이 생겨 중심 이동 시 갱신 누락이 판정 불일치가 된다 |
| 8 | 쇠약 0.7을 코드 상수로 박기 | `BarrierPhaseSO`에서 읽는다 |

### 3.3 장비·인벤토리 — `Assets/01.Scripts/04.Systems/Equipment` 및 `Assets/01.Scripts/04.Systems/Inventory`, namespace `Geumeum.Systems`

| 파일 위치 / namespace | 책임 |
|---|---|
| `Assets/01.Scripts/04.Systems/Equipment/EquipmentInstance.cs` / `Geumeum.Systems` | 서버 런타임 장비 값. `InstanceId`, 무기, 등급, 공격력, 옵션 0~2개 |
| `Assets/01.Scripts/04.Systems/Equipment/EquipmentGenerator.cs` / `Geumeum.Systems` | `EquipmentSO`/`DropTableSO` 기반 등급·공격력·옵션 롤, 서버 ID 발급 |
| `Assets/01.Scripts/04.Systems/Equipment/EquipmentInstanceSerializer.cs` / `Geumeum.Systems` | Mirror writer/reader. SO 참조 없이 고정 필드 직렬화 |
| `Assets/01.Scripts/04.Systems/Inventory/InventorySlotNet.cs` / `Geumeum.Systems` | 슬롯 tag, 장비 snapshot 또는 물약 stack count를 담는 고정 크기 struct |
| `Assets/01.Scripts/04.Systems/Inventory/InventoryController.cs` / `Geumeum.Systems` | 서버 시작 시 Empty 4개를 채운 owner-only SyncList, index Set 기반 저장·사용·장착 교환·양도 API |
| `Assets/01.Scripts/04.Systems/Equipment/DropDirector.cs` / `Geumeum.Systems` | `EnemyDiedEvent` 구독, event의 spawn snapshot으로 장비 roll, 보스 보상 1회; `GameDB.DropTable/Equipment`, `GameEventBus` 접점 |
| `Assets/01.Scripts/04.Systems/Equipment/DroppedItem.cs` / `Geumeum.Systems` | 서버 소유 월드 드랍, `IInteractable`, 예약/소비 후 서버 Destroy |
| `Assets/01.Scripts/04.Systems/Interaction/InteractionController.cs` / `Geumeum.Systems` | 플레이어 프리팹에 붙는 소유 Command 입구; Combat 상태/무기는 Common 인터페이스로만 조회 |

`EquipmentInstance`와 네트워크용 struct는 동일한 의미를 갖되 Unity 오브젝트 참조를 포함하지 않는다. Mirror는 지원 기본 타입으로 구성된 struct를 Remote Action/SyncList에 사용할 수 있고, struct는 통째로 교체해야 변경 동기화가 된다고 문서화한다. 따라서 슬롯 내부 필드만 바꾸지 말고 새 `InventorySlotNet` 값으로 `slots[index] = changed`를 수행한다. [Mirror Data Types](https://mirror-networking.gitbook.io/docs/manual/guides/data-types)

`Assets/01.Scripts/02.Combat/Player/PlayerWeapon.cs`의 `Geumeum.Combat.PlayerWeapon`은 `IWeaponLoadoutTarget`과 snapshot SyncVar hook을 구현하도록 확장한다. 시작 무기는 기존 `ServerSetInitialWeapon`에서 `EquipmentSO`의 일반 등급 롤 범위를 서버가 한 번 굴려 `InstanceId == 0` snapshot으로 만든다. 첫 교환 때 인벤토리가 이전 시작 무기에 새 서버 ID를 부여한다. 공격력은 `[계획]`인 `WeaponAbilityConfigSO`에 넣지 않으며, 실제 공격 소비는 `PlayerAttack` 구현 뒤 연결한다.

장착 교환은 다음 순서로 서버 한 프레임 안에서 끝낸다.

1. 슬롯이 장비인지, 플레이어가 살아 있고 행동 중이 아닌지 검증한다.
2. `IWeaponLoadoutTarget.TrySwapWeapon(next, out previous)`를 호출한다.
3. 성공하면 같은 슬롯에 `previous`를 기록한다.
4. `PlayerWeapon`이 snapshot을 한 번 커밋하고 `WeaponLoadoutChangedEvent`를 발행한다.
5. 무기 종류가 바뀌면 같은 Combat 파트의 `ParryGauge.ServerReset`을 호출하고, Systems의 `TalentController`는 이벤트를 받아 이전 직업 특성을 무료 초기화한다. 공격 스탯 파이프라인은 이전 옵션을 제거한 뒤 새 snapshot 옵션을 한 번 적용한다.
6. snapshot SyncVar hook은 05 문서의 Override Controller와 무기 시각을 갱신한다. 이벤트 구독자 실패가 교환 소유권을 되돌리지는 않으므로 부트 시 필수 구독/프리팹 구성을 검사한다.

### 3.4 경제·객주 — `Assets/01.Scripts/04.Systems/Economy`, namespace `Geumeum.Systems`

| 파일 위치 / namespace | 책임 |
|---|---|
| `Assets/01.Scripts/04.Systems/Economy/CurrencyManager.cs` / `Geumeum.Systems` | 팀 공용 정수 잔액 SyncVar, `[Server] TryCredit/TrySpend/ApplyLossRatio/Refund`, 적 사망 credit·부활 손실 이벤트 구독 |
| `Assets/01.Scripts/04.Systems/Economy/ShopKeeper.cs` / `Geumeum.Systems` | `IInteractable`, 현재 phase와 거리 기준으로 UI 개방 허용 |
| `Assets/01.Scripts/04.Systems/Economy/ShopService.cs` / `Geumeum.Systems` | 불변 transaction plan 생성→지출→실패 불가능한 snapshot commit, 방어적 rollback |
| `Assets/01.Scripts/04.Systems/Interaction/InteractionController.cs` / `Geumeum.Systems` | `CmdBuy`가 `shopNetId`, `ShopItemKind`, 대상 `instanceId`, request ID 전달 |

Command를 `ShopKeeper`에 `requiresAuthority = false`로 두지 않는다. 플레이어 프리팹의 Systems `InteractionController`에 두면 Mirror 기본 권한 검사를 재사용할 수 있고, 서버는 `this.connectionToClient.identity == netIdentity`인 호출자 컨텍스트에서 거리와 상태를 검증한다. 권한 우회가 필요한 경우 Mirror는 optional sender 검증을 제공하지만, 이 설계에서는 우회 자체가 필요 없다. 기준 아키텍처의 `02.Combat/Player/InteractionController.cs` 위치 표기는 이 파트 경계를 따라 `04.Systems/Interaction`으로 갱신한다. [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions)

`IInteractable.Interact`는 “상점 UI를 열 수 있음”과 “드랍 상세를 볼 수 있음”까지만 담당한다. 가격과 선택 장비가 필요한 실제 구매는 별도 `CmdBuy`로 보낸다. 문자열 Prompt 계약에 구매 파라미터를 억지로 넣지 않는다.

줍기·사용·양도·구매 Command는 플레이어별 하나의 단조 증가 request ID 공간을 공유한다. `InteractionController`는 `Constants.Network.INTERACTION_RESULT_CACHE_SIZE = 32` 크기의 고정 ring에 `(requestId, resultCode)`만 보관한다. 최근 중복이면 이전 결과를 재전송하고, ring보다 오래된 역순 ID면 상태를 바꾸지 않고 `DuplicateRequest`를 반환한다. 0은 거부하고 wrap 때 0을 건너뛰며, 신규/과거 판정은 단순 `>`가 아니라 uint half-range serial 비교를 쓴다. `OnStartServer`/`OnStopServer`에서 ring과 newest ID를 초기화하고 런 길이에 비례하는 dictionary는 두지 않는다.

## 4. 데이터

### 4.1 기존 `BarrierPhaseSO` 재사용

| 필드 | 타입 | 현재 기본값 후보 | 사용 |
|---|---|---:|---|
| `_runTimeLimitSeconds` | `float` | 2400초 | RunManager 제한 |
| `_maxRadius` | `float` | 400m, 맵 실측 필요 | `RadiusRatio == 1` |
| `_shrinkDuration` | `float` | 30초 | 반경 전환 |
| `_warningLeadTime` | `float` | 10초 | 경고 |
| `_damageTickInterval` | `float` | 1초 | 서버 피해 주기 |
| `_weakenedHealingMultiplier` | `float` | 0.7 | 회복 효율 |
| `PhaseEntry.StartSeconds/EndSeconds` | `float` | 기존 타임라인 | 구간 경계 |
| `PhaseEntry.RadiusRatio` | `float` | 1.0/0.5/0.3/최종값 | 목표 반경 |
| `PhaseEntry.DamagePerSecond` | `float` | 문서값 | 외부 피해 |
| `PhaseEntry.AppliesWeakened/BlocksHealing` | `bool` | 문서값 | 회복 규칙 |

추가 SO는 만들지 않는다. 반경·피해·회복 규칙은 이미 필요한 필드를 갖고 있다. 대신 부트 검증에 `RadiusRatio` 단조 비증가, 반경/시간 양수, `Boss`의 피해·봉쇄 false를 추가한다.

### 4.2 새 `InventoryConfigSO`

**파일:** `Assets/01.Scripts/00.Common/Data/InventoryConfigSO.cs`, namespace `Geumeum.Data`.

| 필드 | 타입 | 기본값 후보 | 비고 |
|---|---|---:|---|
| `_pickupDistance` | `float` | 3m | 서버 거리 검증 |
| `_transferDistance` | `float` | 3m | 양도 거리 |
| `_interactionRequestCooldown` | `float` | 0.15초 | 요청 스팸 억제 |
| `_worldDropLifetime` | `float` | 180초 | 0이면 만료 없음; 런 컬렉션 상한 목적 |

칸 수는 조정 가능한 밸런스가 아니라 이번 MVP 네트워크/UI 구조의 불변식이다. `Assets/01.Scripts/00.Common/Constants/Constants.inventory.cs`의 `Constants.Inventory.SLOT_COUNT = 4` 하나만 두고, `InventoryController.OnStartServer`가 SyncList에 Empty 값 네 개를 채운 뒤 길이를 바꾸지 않는다. 칸 비우기는 Remove가 아니라 `slots[index] = InventorySlotNet.Empty`다.

`Assets/01.Scripts/00.Common/Data/GameDatabaseSO.cs`와 `GameDB.cs`에 `InventoryConfigSO` 필드/property를 추가하고 `CollectMissingReferences`에 포함한다. `Assets/01.Scripts/00.Common/Editor/GeumeumCheckMenu.cs`는 4칸 불변식, 거리·cooldown finite/비음수, drop lifetime 비음수를 검사한다. 네트워크 접속 중 에셋 핫 변경은 지원하지 않는다.

### 4.3 `EquipmentInstance` / `WeaponCombatSnapshot`

| 필드 | 타입 | 규칙 |
|---|---|---|
| `InstanceId` | `uint` | 서버 단조 증가, 0은 시작 장비 임시 ID |
| `WeaponKind` | `WeaponKind` | 서버 롤/배정 |
| `Grade` | `ItemGrade` | `EquipmentSO` |
| `AttackPower` | `float` | 등급 범위 서버 롤 |
| `Option0Kind/Option1Kind` | `EquipmentOptionKind` | 최대 2개, `None` 허용 |
| `Option0Value/Option1Value` | `float` | 서버 롤 |

현재 `EquipmentSO`에는 ID별 장비 정의 테이블이 없으므로 존재하지 않는 `DefinitionId` 조회 계약은 추가하지 않는다. 표시 이름·아이콘은 `WeaponKind`와 `ItemGrade`로 기존 `GameDB`에서 조회한다. 배열/List를 넣지 않아 Mirror 직렬화와 equality를 단순화하고, `EquipmentSO`의 기존 `GradeEntry`, 공용 옵션, 무기별 옵션 풀은 그대로 사용한다.

### 4.4 기존 `ShopSO` 확장

| 필드 | 타입 | 기본값 후보 |
|---|---|---:|
| `_openPhase` | `RunPhase` | `Shrink2` |
| `_interactionDistance` | `float` | 3m |
| `_items[].Kind` | `ShopItemKind` | 기존 4종 |
| `_items[].Price` | `int` | 60 / 50 / 150 / 350냥 |
| `_items[].EffectValue` | `float` | 회복·신력 40, 권종 0 |
| `_items[].CarryLimit` | `int` | 물약 3, 권종 0 또는 -1 |

재조정·상승은 구매 즉시 적용하므로 권종 `CarryLimit`는 사용하지 않는다. 상점은 클라이언트가 보낸 `Price`나 `EffectValue`를 받지 않는다.

## 5. 흐름

### 5.1 결계

1. 서버 `BarrierController`가 `RunPhaseChangedEvent`를 구독한다.
2. 구독 직후 현재 `RunManager.ServerInstance.Phase`를 같은 경로로 bootstrap한다. 새 phase의 `RadiusRatio * MaxRadius`를 목표 반경으로, **아래 표의 지점을 목표 중심으로** 잡고, 현재 계산 중심·반경·`NetworkTime.time`·종료 시각을 하나의 `BarrierTransitionState`로 커밋한다.

   **목표 중심은 보스 지점으로 수렴한다** ([시스템 기획 2.1.1](../01-system-design.md) D9). 시작 지점과 보스 지점은 `RunManager`가 **런 시작 때 서버에서 한 번** 확정해 SyncVar로 배포하고, `BarrierController`는 그것을 **읽기만** 한다. 결계가 스스로 굴리지 않는다.

   | phase | 목표 중심 | 목표 반경 |
   |---|---|---|
   | `Farming` | 시작 지점 부근 | 100% |
   | `Shrink1` | 시작 → 보스 지점 **1/3** | 50% |
   | `Shrink2` | **2/3** | 30% |
   | `Shrink3` | **보스 지점** | 15% |

   중심과 반경이 **같은 `BarrierTransitionState` 하나**에 들어가므로 30초 보간이 자동으로 함께 움직인다. 중심 전용 SyncVar를 따로 두지 않는다 — 두 값이 따로 도착하면 그 사이에 판정이 어긋난다.
3. SyncVar hook을 받은 클라이언트는 셰이더와 미니맵 표시를 새 상태로 전환한다. 매 프레임 네트워크 값은 보내지 않는다.
4. 서버 1초 Tick은 등록된 최대 3명만 순회한다. XZ 외부이면 `DamageInfo.FromBarrier(DamagePerSecond * interval)`를 적용한다. action 무적은 관통하고 `ReviveInvulnerable`은 존중한다.

   **회복 규칙은 여기서 밀어 넣지 않는다.** 쇠약·회복 봉쇄는 회복이 실제로 일어나는 순간 `PlayerStatus.TryRestoreHealth`가 `GetHealingMultiplier(position)`로 물어본다(3.2.1). 따라서 **"내부로 돌아오면 규칙을 복원한다"는 단계가 없다** — 복원할 사본 자체가 없기 때문이다. 중심이 움직이는 구조에서 사본은 곧 갱신 누락이다.
5. `Boss`/`Result`에서는 피해와 회복 봉쇄를 중단하고 `IsActive`가 `false`가 된다. 시각 결계 유지/소거는 디자인 값으로 표현하되 판정은 종료한다.

경고는 `RunManager.ElapsedSeconds`가 다음 `StartSeconds - WarningLeadTime`을 처음 넘을 때 서버가 revision을 1회 올린다. 각 시스템이 별도 경과 시간을 누적하지 않는다.

### 5.2 줍기·장착·사용·양도

**줍기:** 클라이언트는 `pickupNetId`, 목적(`Store` 또는 `UseNow`), request ID만 보낸다. 서버는 호출자, `Result` 아님, 생존 상태, 거리, 드랍 존재·미예약을 확인한다. `Store`는 슬롯/스택 여유를 추가로 검사한 뒤 드랍 예약→슬롯 Set→드랍 Destroy 순서다. `UseNow` 장비는 현재 행동 중이 아님과 이전 장비를 보관할 빈 슬롯이 있음을 확인한 뒤 `TrySwapWeapon`으로 교환하고 이전 장비를 그 슬롯에 기록한다. 빈 슬롯이 없으면 `InventoryFull`로 거부하며 이번 MVP에서는 자동 월드 재드랍을 만들지 않는다. `UseNow` 물약은 실제 회복 성공 뒤에만 드랍을 Destroy한다. 어느 분기든 최종 commit 전 실패하면 예약을 해제하고 상태와 드랍을 보존한다.

**장착:** 클라이언트는 슬롯 index와 request ID만 보낸다. 서버가 슬롯 장비를 현재 `PlayerWeapon` snapshot과 교환한다. 클라이언트가 무기 종류나 공격력을 보내지 않는다.

**물약:** 클라이언트는 슬롯 index를 보낸다. 서버가 종류와 수량, 생명 상태, 회복 봉쇄, HP/신력 부족 여부를 확인한 뒤 `IRecoveryTarget`에 적용하고 stack을 1 감소시킨다. 적용 실패 시 stack을 소비하지 않는다.

**양도:** 클라이언트는 자기 슬롯 index와 대상 player netId를 보낸다. 서버가 자기 자신이 아닌지, 송신자 소유, 대상 접속/생존, 3m 거리, 대상 슬롯/동일 물약 stack 여유를 확인한다. 대상의 새 slot 값을 먼저 계산하고, 수신 slot Set 뒤 송신 slot을 Empty로 Set한다. 두 조작은 Unity 서버 메인 스레드의 한 호출 안에서 처리하며 예외 시 두 이전 값을 복원한다.

### 5.3 구매

1. 클라이언트가 객주 UI에서 상품과, 권종이면 자기 장비 `instanceId`를 선택한다.
2. `InteractionController.CmdBuy(requestId, shopNetId, kind, targetInstanceId)` 호출.
3. 서버가 request ID, `RunPhase == ShopSO.OpenPhase`, 플레이어 생존/관전 아님, 상점과 3m 이내, 상품 존재를 검사한다.
4. 물약은 stack 여유, 권종은 대상 장비가 장착/인벤토리 중 어디에 있는지와 소유·등급/롤 가능 여부를 **지출 전에** 검사한다. RNG도 이때 굴려 이전/다음 slot 또는 weapon snapshot을 가진 불변 `ShopTransactionPlan`을 만든다.
5. `CurrencyManager.TrySpend(serverPrice)`가 성공하면 plan의 이미 검증된 값 대입만 수행한다. 적용 중 예외가 나면 이전 snapshot/slot을 복원하고 `Refund(serverPrice)`한 뒤 fatal 로그와 `CommitFailed`를 반환한다.
6. 서버가 갱신된 wallet/SyncList/snapshot을 동기화하고 해당 호출자에게 성공·실패 코드를 `TargetRpc`로 보낸다. 고정 ring 안의 동일 request ID는 이전 결과 코드만 재전송하고, 더 오래된 ID는 `DuplicateRequest`만 보내며 상태를 다시 바꾸지 않는다.

실패 코드는 최소 `DuplicateRequest`, `WrongPhase`, `TooFar`, `Dead`, `UnknownItem`, `CarryLimit`, `NotOwner`, `InsufficientFunds`, `MaxGrade`, `InventoryFull`, `CommitFailed`로 구분한다. bool 하나만 보내면 UI와 로그가 실제 거부 원인을 설명할 수 없다.

동시 구매는 서버 메인 스레드 도착 순서대로 처리한다. 두 요청이 같은 잔액을 본 뒤 각각 빼는 구조가 아니라 `TrySpend` 안에서 검사와 차감을 한 메서드로 수행한다.

### 5.4 드랍·노잣돈 획득·부활 손실

1. `03-boss-design.md`에서 **기존 `EnemyDiedEvent`에 보강하기로 한** `ForEnemy/ForBoss` factory와 nullable kind 불변식을 먼저 구현한다. 그 뒤 `EnemyController`/`BossStatus`가 각 factory를 서버에서 한 번 호출한다. 현재 코드에는 factory가 없으므로 03의 Common 계약 보강이 이 단계의 선행 의존성이다.
2. `DropDirector`는 일반 적이면 사망 시 현재 `RunManager.FieldLevel`을 다시 읽지 않고 event의 `FieldLevelAtSpawn`으로 `DropTableSO → EquipmentSO` 순서의 장비를 생성해 등록된 `DroppedItem` 프리팹을 서버 spawn한다. `RewardMultiplier`는 장비 등급 가중치에 곱하지 않고 3번의 화폐/경험치 소비자만 쓴다. 보스면 factory가 고정한 FieldLevel 3 snapshot으로 팀 보상 1회를 생성한다.
3. `CurrencyManager`는 일반 적의 `EnemySO.CurrencyMin/Max * spawn 시점 RewardMultiplier`를 서버 RNG로 확정해 `TryCredit`한다. 보스 보상 화폐가 필요하면 별도 SO 필드가 확정되기 전에는 0으로 둔다.
4. `ReviveController`는 사망 1회가 확정될 때 Common `PartyCurrencyLossRequestedEvent`를 발행한다. `CurrencyManager`는 `GameDB.Combat.ReviveCurrencyLossRatio`와 현재 잔액으로 손실액을 한 번 계산해 `ApplyLossRatio`하고 0 아래로 내리지 않는다.
5. `EnemyDiedEvent`, 부활 death instance ID, boss reward flag를 각각 dedupe한다. 이탈/중복 이벤트에도 장비·화폐·손실이 두 번 적용되지 않는다.

## 6. 단계별 구현 순서

1. **결계 수직 슬라이스(14~18시간):** `BarrierJudge`, transition SyncVar, current-phase bootstrap, `TICK_PRIORITY_BARRIER`, 1단계 축소, action 무적을 관통하는 1초 외부 피해. M2에서 세 화면 반경과 HP를 맞춘다.
2. **결계 전체 phase(6~10시간):** 경고, 쇠약/회복 봉쇄, Boss 종료, `GeumeumCheckMenu` 부트 검증. 여기까지 결계가 동작한다.
3. **장비 생성과 정확히 4칸 인벤토리(22~30시간):** 고정 struct, 서버 ID, Empty 4개 owner-only SyncList, `DropDirector`, 적 사망 장비 생성, Store/UseNow/장착 교환. 큐브 드랍과 실제 `EnemyDiedEvent`를 M2에서 확인한다.
4. **사용·양도(10~14시간):** 물약 stack, `IRecoveryTarget`, 3m 양도와 실패 경로.
5. **지갑·객주(18~24시간):** `CurrencyManager`, 적 사망 credit, 부활 20% 손실, Shrink2 gate, 물약/재조정/승급 transaction plan과 rollback.
6. **통합·장기 검증(12~18시간):** 무기 교체 특성/옵션/애니메이션 hook, 동시 구매, 이탈, phase 경계, 40분 런, 컬렉션 상한.

1단계 완료 정의는 “M2에서 30초 축소와 1초 피해가 세 인스턴스에 같은 결과로 보임”이다. 3단계 완료 정의는 “서버가 만든 장비를 한 플레이어만 자기 4칸 UI에서 보고 장착 교환할 수 있음”이다. 최종 완료 정의는 세 클라이언트의 동시 구매/양도에서도 잔액·소유권·슬롯이 한 번만 바뀌는 상태다.

## 7. 검증

| 등급 | 상황 | 통과 기준 |
|---|---|---|
| M0 | `BarrierJudge` 경계 | 반경 바로 안/경계/밖, 0초/15초/30초 값이 기대치와 일치 |
| M0 | SO/구조 부트 검사 | 반경 증가, phase 누락, 음수/NaN 가격·거리, 중복 상품을 각각 거부; `InventoryController` SyncList 길이는 spawn 후 항상 4 |
| M2 | 결계 3인 | 세 화면 반경 오차 0.2m 이하, 외부 10초 피해 횟수 10회, 내부 플레이어 0회 |
| M2 | 결계와 무적 | 외부에서 구르기/패링 무적 각 10회에도 결계 피해 횟수 불변, 부활 무적 2초 동안만 피해 0 |
| M2 | 경계 왕복 | 밖→안/안→밖 각 10회, 쇠약과 회복 봉쇄가 다음 서버 Tick 안에 정확히 복원/적용 |
| M2 | 동시 줍기 | 2명이 같은 드랍을 20회 동시 요청, 총 소유 수 매회 1개 |
| M2 | 슬롯/stack | 장비 4개 이후 5번째 거부, 물약 종류별 3개 이후 4번째 거부, 거부 때 지갑/드랍 불변 |
| M2 | Store/UseNow | 빈 슬롯 있음/없음, 회복 가능/불가 각 10회; 성공 때만 드랍 1개 소멸, 이전 장비/물약 복제·소실 0 |
| M2 | 양도 | 3m 안/밖, 대상 full, 대상 이탈을 각 10회; 아이템 복제·소실 0회 |
| M2 | 동시 구매 | 잔액이 1개만 살 수 있을 때 3명이 동시에 20회 요청; 성공 1명, 잔액 음수 0회 |
| M2 | 구매 commit 실패 주입 | plan 생성 뒤 강제 실패 20회, wallet·slot·weapon snapshot이 모두 이전 값으로 복원되고 `CommitFailed` 1회 |
| M2 | phase 경계 | Shrink2 직전·진입·이탈 각 요청; 진입 중만 구매 성공 |
| M2 | 보상/부활 손실 | 일반 적 각 종류 20회와 보스 10회에서 spawn 시점 배수 장비/화폐가 1회만 반영; 사망 instance 중복 20회에도 20% 손실 1회 |
| M2 | 무기 교체 후속효과 | 검↔활↔방울 각 10회, 패링 게이지 0·이전 특성 무료 초기화·이전 옵션 제거/새 옵션 1회 적용·snapshot hook 1회 |
| M3 | 서로 다른 3PC 지연/손실 | 150ms RTT/2% loss+jitter에서 재전송·중복 request ID 20회, 이중 지출·이중 소비·이중 양도 0회 |
| M2 | request ring wrap | 64개 순차 요청 뒤 최근 ID 재전송과 오래된 ID 각 20회, 최근 결과 재전송·오래된 요청 `DuplicateRequest`, cache count 항상 32 이하 |
| M2 장기 | 40분 런 | 드랍 만료/소비 후 registry·예약 set이 기준치로 복귀, 계속 증가하는 컬렉션 없음 |
| 성능 | GTX 1060 3GB 목표 빌드 | 3인+드랍 상한+결계 VFX에서 1080p p95 frame 16.67ms 이하, Systems steady-state GC.Alloc 0B/frame, VRAM peak 2.5GB 이하 |

씬이 없으므로 `BarrierCenter`, 객주/보스맵 거리, Player prefab의 어댑터와 NetworkBehaviour Sync Mode, 월드 드랍 spawn 등록은 **미검증**이다. 구현 직전 씬·프리팹에서 확인해야 한다.

## 8. 대안과 선택 근거

### 결계: 매 프레임 반경 SyncVar 대신 전환 원본 1회 동기화

보통 네트워크 게임은 결과를 재현할 수 있는 시작값·목표값·서버 시각을 보내고 클라이언트가 표현을 보간한다. Mirror도 SyncVar를 변경 시 서버에서 클라이언트로 보내며, 매 프레임 Command를 보내지 말라고 명시한다. [Mirror SyncVars](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization/syncvars), [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions)

3명이라 대역폭 자체는 작지만 리슨 서버가 렌더링도 담당하고, 현재 `RunManager`도 같은 이유로 동기화 간격을 둔다. 전환 struct 하나는 호스트/클라의 시각 일치를 유지하면서 디버깅할 원본도 남긴다. 매 프레임 radius SyncVar와 클라이언트별 독립 코루틴은 기각한다.

이 선택은 3인 리슨 서버에서 서버 판정과 화면 렌더링을 함께 하는 호스트의 CPU/대역폭을 고정하고, GTX 1060 3GB에서 매 프레임 네트워크 상태나 동적 UI/드랍 객체를 불필요하게 늘리지 않는다. 정확히 4칸, 최대 3명, 재사용 registry라는 상한은 11주 동안 custom serializer·DB·확장 UI를 만들지 않고 M2/M3에서 전체 상태를 열거해 검증할 수 있게 한다.

### 인벤토리: 4칸 `SyncList`, Owner 동기화

Mirror 공식 문서가 inventory를 owner-only 동기화의 대표 예로 들고 `SyncList`를 서버 변경 목록 동기화 수단으로 제공한다. 이 프로젝트의 목록은 4칸 고정이라 custom delta serializer나 별도 DB를 만들 이익이 없다. [Mirror Synchronization](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization), [Mirror SyncLists](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization/synclists)

고정 배열 4개를 각각 SyncVar로 두는 대안은 이벤트 구독과 빈칸 이동 코드가 반복된다. 일반 `List` 전체를 RPC로 매번 보내는 대안은 초기 상태/재전송/변경 추적을 직접 구현해야 한다. SyncList가 현재 팀의 검색 가능성과 일정에 가장 맞는다.

### 구매: Systems 플레이어 Command + 서버 transaction plan

보통 Mirror에서는 플레이어 소유 오브젝트가 Command 통로가 되고 서버가 중요한 상태를 변경한다. 상점 scene object에 권한을 나눠주거나 `requiresAuthority=false`를 쓰는 것보다 플레이어 프리팹의 `Geumeum.Systems.InteractionController`에서 기본 권한 모델을 재사용한다. [Mirror Authority](https://mirror-networking.gitbook.io/docs/manual/guides/authority), [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions)

3인 메인 스레드 서버에서는 DB 트랜잭션/lock이 필요 없다. 사전검증 후 단일 `TrySpend`와 즉시 효과 적용이면 원자성이 충분하다. 단, 외부 온라인 상점이나 영속 화폐로 범위가 바뀌면 이 판단은 다시 해야 한다.

### 데이터: 기존 SO 유지, `InventoryConfigSO`만 추가

Unity는 ScriptableObject를 여러 인스턴스가 공유하는 데이터 자산으로 제공하며, 이 저장소도 `GameDB`를 단일 진입점으로 쓴다. 결계/상점의 기존 필드를 복제하지 않고 슬롯·상호작용 값만 새 SO로 분리하면 10월 밸런싱 때 코드 재빌드가 필요 없다. [Unity ScriptableObject](https://docs.unity3d.com/6000.1/Documentation/Manual/class-ScriptableObject.html)

## 9. 비용과 위험

**엔지니어링 부분합: 대, 82~114시간.** 결계 20~28시간, 인벤토리·장비·드랍 32~44시간, 지갑·상점·부활 손실 18~24시간, M2/M3 통합 12~18시간이다. 현금 구매비는 기존 Unity/Mirror와 현재 SO를 쓰는 전제로 **0원**이다. `PlayerAttack`/Talent 본체, UI 레이아웃·최종 VFX, 씬/프리팹 배치와 별도 아트 구매비는 포함하지 않는다. 3인·11주 전체 일정에서는 03의 사망 이벤트 계약과 05의 무기 snapshot hook을 선행/공용 작업으로 잡아 중복 합산하지 않는다.

| 위험 | 영향 | 완화 |
|---|---|---|
| `PlayerWeapon`이 현재 최초 1회 배분만 허용 | 장착 교환 불가, 문서의 무기 교체 규칙과 충돌 | `IWeaponLoadoutTarget` 서버 교환 API와 snapshot으로 명시 확장, 초기 API는 로비 전용 유지 |
| 결계 회복 규칙과 PlayerStatus 직접 결합 | Combat↔Systems 경계 파괴 | Common `IRecoveryTarget`/이벤트만 사용 |
| SyncList struct 내부만 변경 | 클라이언트 갱신 누락 | 새 struct를 만들어 `slots[index] = value`로 통째로 대입 |
| 구매 사전검증 후 적용이 실패 | 돈만 빠짐 | 불변 transaction plan을 먼저 생성하고 값 대입만 commit; 예외 시 이전 snapshot 복원과 실제 `Refund`, `CommitFailed` 반환 |
| 4칸에 물약과 장비가 경쟁 | 파밍 체감 과도하게 답답할 수 있음 | 물약 stack을 종류당 1칸으로 제한하고 M2 플레이 테스트; 칸 확장은 금지, 필요하면 자동 사용/드랍 UX만 조정 |
| `BarrierCenter`/객주 위치 미확인 | 안전지대에서 피해 또는 상점 접근 불가 | 2차/3차 최종 반경을 씬에서 실측하고 M2 체크리스트에 좌표 증거 기록 |
| 장기 런 드랍/예약 누수 | 40분 후 프레임·메모리 저하 | 월드 드랍 만료, OnStopServer registry 해제, 동일 드랍 예약 set 즉시 제거, 40분 계측 |
| SO 값을 클라이언트가 변조 | 표시만 변하거나 치트 요청 | 가격·효과·롤·거리 판정은 서버의 GameDB만 사용; 클라이언트 값은 ID만 전송 |
| `EnemyDiedEvent` 보강보다 먼저 드랍 구현 | spawn 시점 배수 유실·보스 kind 오해 | 03의 nullable kind, `ForEnemy/ForBoss`, FieldLevel/RewardMultiplier 계약을 선행 완료한 뒤 `DropDirector` 착수 |
