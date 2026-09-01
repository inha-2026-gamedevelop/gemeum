# 애니메이션 파이프라인 설계

> 상태: 설계 확정본, 구현 전  
> 출처: 2026-09-01 외부 소스 감사와 함께 받은 설계 산출물. 저장소 문서로 편입했다  
> 관련: [프로젝트 구조 설계서](../04-architecture.md) · [네트워크 권위](NETWORK_AUTHORITY.md) · [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)

> 상태: 구현 제안. 코드·Animator·프리팹은 수정하지 않았다.
>
> 핵심 결정: 유효한 Humanoid Avatar를 활용해 **플레이어 기본 Controller 1개 + 무기별 Animator Override Controller 3개**로 통합한다. 애니메이션은 판정의 원본이 아니며, 서버가 확정한 action cue를 각 클라이언트가 표현한다.

## 1. 목표와 범위

이번에 만드는 것은 다음이다.

- 플레이어 3외형 × 검/활/방울 3무기의 조합 폭발 없이 이동·구르기·기본 공격·피격·경직·다운/사망을 재생한다.
- 잡귀 3종과 보스 1마리에 이동·예고·공격·피격·경직·사망의 공통 파라미터 계약을 둔다.
- Mixamo, Unity Asset Store/기존 무료 에셋, 최소 수작업의 우선순위와 라이선스 기록 절차를 정한다.
- 서버 판정 시각과 클라이언트 모션이 지연 환경에서도 납득 가능한 범위로 맞도록 action cue를 정의한다.
- 무기는 활성 모델의 손 본 아래 socket에 붙이고, 외형과 무기 선택을 독립적으로 유지한다.

만들지 않는 것은 전용 모션캡처, 전 캐릭터 수제 애니메이션, 손가락 리깅 추가, 전신 procedural IK, 모션 매칭, Playables 기반 커스텀 애니메이션 그래프, 애니메이션 root motion에 의한 권위 이동이다.

현재 첨부된 7개 Controller 중 `PlayerLocomotion.controller`만 `Speed`, `Roll`, `RollSpeed`와 Roll 전이를 갖고 있고, 차사·신궁·무녀 Controller는 단일 Walk 중심이다. 이를 외형별 Controller 3벌로 확장하지 않는다.

## 2. 권위 경계

| 값 · 판정 | 서버 | 클라이언트 | 근거 |
|---|---|---|---|
| 공격 허용, 자원 소비, 대상, impact 시각, 피해 | `[계획] PlayerAttack`/AI의 `[Server]` 로직만 결정 | 변경 불가 | 애니메이션 프레임·Event가 판정을 만들지 않는다. |
| 공격 action cue | `PlayerAttack`/`EnemyController`/`BossController` `NetworkBehaviour`가 승인 후 `ClientRpc`로 cue 전송 | AI/보스는 전원 재생; 플레이어는 관찰자 재생, 소유자는 예측과 `TargetAttackResult` dedupe | 표현용 `MonoBehaviour`는 RPC를 소유하지 않는다. [Mirror Remote Actions](https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions) |
| 구르기 | 기존 서버 `_isRolling` SyncVar와 판정 유지 | hook에서 `PlayRoll` | 기존 흐름 재사용. 공격 파이프라인 때문에 구르기를 다시 쓰지 않는다. |
| 피격·경직 cue | 서버 피해/패링 결과 뒤 RPC | 로컬 fixed-time transition·VFX | 결과가 확정된 뒤 표현한다. |
| 생명 상태·무기 종류·외형 | 기존/확장 SyncVar | hook에서 Controller/모델/무기 시각 갱신 | 지속 상태는 SyncVar가 적합하다. [Mirror SyncVars](https://mirror-networking.gitbook.io/docs/manual/guides/synchronization/syncvars) |
| Animator state, normalized time, IK, 파티클 | 보유하지 않음 | 각 클라이언트가 계산 | 사람마다 달라도 게임 결과가 같다. |
| 이동 Transform | 기존 예측+서버 재조정 | Animator는 `PlayerPresentation`을 읽음 | root motion은 끈다. |

소유 클라이언트는 버튼을 누르는 즉시 공격 모션을 시작할 수 있다. 02 설계의 `PlayerAttack.TargetAttackResult(AbilityResult)`가 같은 `RequestId`의 승인을 돌려주면 재시작하지 않고 유지하고, 거부 결과면 0.08초 fixed transition으로 Locomotion에 복귀한다. 플레이어 관찰자용 `RpcAttackStarted(AnimationActionCue)`는 02의 결정대로 `[ClientRpc(includeOwner = false)]`이며, 소유자의 승인/거부 dedupe와 이중 경로를 만들지 않는다. 이 예측은 시각 전용이며 hitbox·피해·자원은 미리 적용하지 않는다.

원격 관찰자는 cue가 늦게 도착하면 다음으로 시작한다.

```text
elapsed = max(0, NetworkTime.time - startServerTime)
normalizedStart = clamp01(elapsed / duration)
fixedTimeOffset = normalizedStart * clipLength
Animator.CrossFadeInFixedTime(stateHash, transitionSeconds, layer=0, fixedTimeOffset)
```

`duration <= 0`, NaN, Infinity cue는 거부한다. 이미 `duration`을 지난 cue는 모션을 건너뛰고 아직 표시 가치가 있는 서버 확정 결과 VFX만 한 번 표시한다. 클라이언트 지연 때문에 오래된 공격을 처음부터 재생하지 않는다. 코드 transition 설정은 전부 초 단위 `transitionSeconds`이며 `Animator.CrossFadeInFixedTime`에만 넘긴다. normalized transition duration을 받는 overload는 사용하지 않는다.

## 3. 구조

### 3.1 Controller 자산

| 위치 | 자산 | 책임 |
|---|---|---|
| `Assets/05.Animator/Player/PlayerBase.controller` | 기본 Controller 1개 | Locomotion, Roll, AttackA/B, Cast, Hit, Stagger, Downed, Death의 공통 상태·전이 |
| `Assets/05.Animator/Player/Player_Sword.overrideController` | Override | AttackA/B를 검 클립으로 교체 |
| `Assets/05.Animator/Player/Player_Bow.overrideController` | Override | AttackA/B를 활 클립으로 교체 |
| `Assets/05.Animator/Player/Player_Bell.overrideController` | Override | AttackA/B/Cast를 방울 클립으로 교체 |
| `Assets/05.Animator/Enemy/EnemyBase.controller` | 잡귀 기본 Controller | Idle/Move/Telegraph/Attack/Hit/Stagger/Death 공통 계약 |
| `Assets/05.Animator/Enemy/{Kind}.overrideController` | 3개 Override | 잡귀별 이동·공격 클립 교체 |
| `Assets/05.Animator/Boss/Boss.controller` | 보스 전용 | 두 페이즈 표현 상태. 판정 상태 머신과 분리 |

Unity의 Animator Override Controller는 기본 Controller의 상태 구조·파라미터·로직을 유지한 채 클립만 교체하는 공식 기능이다. 외형 3 × 무기 3으로 Controller 9벌을 복제하는 대신 무기별 Override 3개만 관리하는 근거다. [Unity Animator Override Controller](https://docs.unity3d.com/6000.0/Documentation/Manual/AnimatorOverrideController.html)

플레이어 기본 Controller는 1개 Base Layer로 시작한다.

```text
Entry → Locomotion(Idle/Walk/Run 1D Blend Tree)
Locomotion → Roll → Locomotion
code CrossFadeInFixedTime → AttackA / AttackB / Cast → exit time → Locomotion
Any State → Hit / Stagger
Any State → Downed → Death
```

활·방울을 이동 중 상반신만 재생해야 한다는 플레이 테스트 요구가 생길 때만 `UpperBodyAction` layer와 AvatarMask를 2단계로 추가한다. Unity Animation Layer는 마스크로 상·하체를 나눌 수 있지만, 지금 바로 추가하면 검의 전신 공격과 전이 조합만 늘어난다. [Unity Animation Layers](https://docs.unity3d.com/6000.0/Documentation/Manual/AnimationLayers.html)

상태의 exit time은 normalized 값을 사용해 Override 클립 길이가 달라도 전이 비율을 유지한다. 반면 cue 수신과 거부 복귀처럼 코드가 시작하는 transition은 초 단위 `CrossFadeInFixedTime`만 사용한다.

### 3.2 파라미터 계약

| 이름 | 타입 | 작성자 | 의미 |
|---|---|---|---|
| `Speed` | float | `PlayerAnimationController` | 0 idle, 1 walk, 2 run; 기존 계약 유지 |
| `Roll` | trigger | 기존 Roll hook | 구르기 |
| `RollSpeed` | float | 기존 코드 | 판정 duration에 클립 맞춤 |
| `ActionSpeed` | float | 표현 코드 | 클립 길이/서버 action duration |
| `Hit` | trigger | 서버 결과 cue | 일반 피격 |
| `HitStrength` | int | 서버 결과 cue | Light/Heavy 표현 |
| `Stagger` | trigger | 서버 결과 cue | 패링·강경직 |
| `LifeState` | int | PlayerStatus hook | Alive/Downed/Spectating/ReviveInvulnerable |

`ActionId`를 Animator parameter로 두지 않는다. cue의 안정 enum을 로컬 state hash로 매핑하고 코드는 해당 상태에 직접 `CrossFadeInFixedTime`한다. int 조건을 계속 유지해 Locomotion 복귀 직후 같은 공격으로 재진입하는 루프를 만들지 않는다. 문자열은 `Animator.StringToHash` 결과를 Animator/Controller가 바뀌는 시점에 한 번 캐싱한다. 현재 `FindClipLength` 방식도 그 시점에 action별 길이 사전으로 확장하고 매 프레임 Controller clip 배열을 순회하지 않는다.

모든 action state의 Speed Multiplier는 `ActionSpeed` parameter에 바인딩한다. cue 적용 순서는 `ActionSpeed 설정 → 낮은 우선순위 trigger 정리 → CrossFadeInFixedTime(stateHash, transitionSeconds, 0, fixedTimeOffset)`로 고정한다.

표현 상태 우선순위는 `Downed/Death > Revive > Stagger > Hit > Attack/Parry/Roll > Locomotion`으로 고정한다. 높은 상태에 들어갈 때 낮은 상태의 trigger와 pending cue를 지우며, Downed/Death 이후 도착한 action cue는 sequence를 소비하되 재생하지 않는다. 같은 우선순위에서는 서버 sequence가 큰 cue만 적용한다.

### 3.3 코드와 파트 경계

| 파일 위치 / namespace | 타입 | 책임 | 기존 접점 |
|---|---|---|---|
| `Assets/01.Scripts/00.Common/Types/AnimationActionId.cs` / `Geumeum.Common` | `enum : byte` | 명시값 action ID, 재배열 금지 | Combat/AI/cue 공용 |
| `Assets/01.Scripts/00.Common/Types/AnimationActionCue.cs` / `Geumeum.Common` | `struct` | actor, request, sequence, action, 시작 시각, 판정 duration의 고정 크기 RPC 값 | 파트 간 구체 클래스 참조 방지 |
| `Assets/01.Scripts/00.Common/Events/DamageAppliedEvent.cs` / `Geumeum.Common` | `readonly struct` | `ActorNetId:uint`, `Strength:HitStrength`, `WasFatal:bool`, `Sequence:uint`, `StartServerTime:double` | invincible/0 damage에서 피격 모션 방지 |
| `Assets/01.Scripts/00.Common/Events/StaggerAppliedEvent.cs` / `Geumeum.Common` | `readonly struct` | `ActorNetId:uint`, `Sequence:uint`, `StartServerTime:double`, `Duration:float` | 피해 없는 패링 경직도 표현 |
| `Assets/01.Scripts/02.Combat/Player/PlayerAttack.cs` / `Geumeum.Combat` | `[계획] NetworkBehaviour` | `CmdRequestAttack`, `TargetAttackResult`, `RpcAttackStarted`와 sibling `PlayerStatus`의 피격 RPC 소유자 | 02 공격 설계 선행 의존 |
| `Assets/01.Scripts/02.Combat/Player/PlayerStatus.cs` / `Geumeum.Combat` | 기존 확장 | life-state SyncVar hook, 실제 HP 감소 뒤 `DamageAppliedEvent` | 현재 `void TakeDamage` 내부 commit 지점 |
| `Assets/01.Scripts/02.Combat/Player/PlayerWeapon.cs` / `Geumeum.Combat` | 기존 확장 | snapshot SyncVar hook과 로컬 무기 변경 이벤트 | 04의 `WeaponCombatSnapshot` |
| `Assets/01.Scripts/02.Combat/Player/PlayerAnimationController.cs` / `Geumeum.Combat` | 기존 `MonoBehaviour` 확장 | Controller 교체, cue dedupe/우선순위, 공격/피격/경직/생명 모션 | `PlayerPresentation`, `PlayerAppearance`, `PlayerWeapon` |
| `Assets/01.Scripts/02.Combat/Player/PlayerAppearance.cs` / `Geumeum.Combat` | 기존 `NetworkBehaviour` 확장 | active Animator/socket 공개와 외형 교체 완료 event | 현재 private `AppearanceModelBinding` |
| `Assets/01.Scripts/02.Combat/Player/PlayerWeaponVisual.cs` / `Geumeum.Combat` | `MonoBehaviour` | 활성 외형의 authored socket에 무기 시각 프리팹 1개 바인딩 | weapon snapshot 로컬 hook |
| `Assets/01.Scripts/03.AI/Enemy/EnemyStatus.cs` / `Geumeum.AI` | 기존 확장 | 실제 HP 감소 뒤 `DamageAppliedEvent`, 사망 판정·보상 시점은 즉시 유지 | 현재 `TakeDamage`/`Died` |
| `Assets/01.Scripts/03.AI/Enemy/EnemyController.cs` / `Geumeum.AI` | 기존 `NetworkBehaviour` 확장 | enemy action/hit RPC의 네트워크 소유자 | 서버 AI 상태·`DamageAppliedEvent` |
| `Assets/01.Scripts/03.AI/Enemy/EnemyAnimationBridge.cs` / `Geumeum.AI` | `MonoBehaviour` | 수신된 cue를 로컬 Animator 호출로 변환 | `EnemyController` 로컬 이벤트 |
| `Assets/01.Scripts/03.AI/Boss/BossStatus.cs` / `Geumeum.AI` | 03의 `NetworkBehaviour` 확장 | 실제 HP 감소 뒤 `DamageAppliedEvent`, 사망/`EnemyDiedEvent.ForBoss` 즉시 확정 | 03 보스 사망 계약 |
| `Assets/01.Scripts/03.AI/Boss/BossController.cs` / `Geumeum.AI` | 03의 `NetworkBehaviour` | boss action/phase RPC의 네트워크 소유자 | `BossTelegraphCue`/action cue |
| `Assets/01.Scripts/03.AI/Boss/BossAnimationBridge.cs` / `Geumeum.AI` | `MonoBehaviour` | Boss action cue/phase cue 표현 | `BossController` 로컬 이벤트 |
| `Assets/01.Scripts/00.Common/Data/AnimationCatalogSO.cs` / `Geumeum.Data` | `ScriptableObject` | Controller, 무기 시각 prefab, 표현 조정값 | `GameDatabaseSO`, `GameDB`, `GeumeumCheckMenu` |

Combat와 AI는 서로 참조하지 않는다. `AnimationActionCue`/`DamageAppliedEvent` 값 계약만 Common에 놓고 각 파트의 `NetworkBehaviour`가 전송, 자기 `MonoBehaviour` bridge가 표현한다. Art/VFX는 기존 Common `ICombatFeedback`을 우선 사용한다. 별도 cue가 필요하면 Common 값 타입을 bridge가 로컬 C# event로 노출하고, Art가 AI/Combat event 타입을 직접 참조하지 않게 한다.

### 3.4 모델·무기 하이어라키

Unity 공식 리타게팅 가이드는 gameplay component를 둔 루트 아래에 Avatar/Animator 모델을 자식으로 두고, 스크립트가 자식 Animator를 찾는 구조를 제시한다. 현재 `PlayerAnimationController.RefreshAnimator()`와 `PlayerAppearance`의 모델 교체가 이미 이 구조다. [Unity Humanoid Retargeting](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html)

```text
Player (NetworkIdentity, 판정/이동 컴포넌트)
└ VisualPivot
  ├ Appearance_Jeonrip (Animator + valid Humanoid Avatar)
  │ └ .../RightHand
  │   └ WeaponSocket (authored local offset)
  ├ Appearance_...
  └ Appearance_...
```

각 외형의 `WeaponSocket`은 프리팹에서 손 본 자식으로 직접 배치하고, 코드가 매번 이름 검색으로 만들지 않는다. 현재 private인 `PlayerAppearance.AppearanceModelBinding`을 `Appearance`, `Model`, `Animator`, `WeaponSocket`의 직렬화 바인딩으로 확장하고, 읽기 전용 `ActiveAnimator`, `ActiveWeaponSocket`과 `event Action<Animator, Transform> ActiveVisualChanged`를 공개한다. 외형 적용은 모델 활성화→active 참조 커밋→event 1회 순서다. `PlayerWeaponVisual.RefreshBinding(activeAnimator, activeSocket)`은 socket이 `Animator.GetBoneTransform(HumanBodyBones.RightHand)`의 자손인지 한 번 검증한 뒤 사용한다. 무기 시각은 외형이 아니라 `WeaponKind`로 선택한다.

손가락 본이 없으므로 손가락 애니메이션을 전제로 한 클립은 고르지 않는다. 검·방울은 손을 덮는 그립/장갑으로 접점을 숨기고, 활은 왼손 고정이 자연스러운 클립을 우선 구매한다. 손 IK 패키지는 화살 손 이탈이 플레이 테스트에서 눈에 띄는 경우에만 후속 도입한다.

## 4. 데이터

### 4.1 공용 action cue

`AnimationActionId : byte`는 `None=0`, `PlayerAttackA=1`, `PlayerAttackB=2`, `PlayerCast=3`, `PlayerParry=4`, `PlayerParrySuccess=5`, `EnemyTelegraph=16`, `EnemyAttack=17`, `BossMeleeArc=32`, `BossTargetedDash=33`, `BossMarkedCircle=34`, `BossConeBurst=35`, `BossPhaseTransition=36`으로 시작한다. 직렬화 값은 재배열하지 않고 새 값만 뒤에 추가한다.

`AnimationActionCue`는 다음 고정 필드만 가진다.

| 필드 | 타입 | 규칙 |
|---|---|---|
| `ActorNetId` | `uint` | 0 금지 |
| `RequestId` | `uint` | 플레이어 예측 dedupe 키, AI/Boss는 0 |
| `Sequence` | `uint` | actor별 서버 단조 증가, 0 건너뜀; 플레이어는 02의 `AbilityResult.ExecutionId`와 같은 값 |
| `ActionId` | `AnimationActionId` | 위 명시값 enum |
| `StartServerTime` | `double` | finite, `NetworkTime.time` 기준 |
| `Duration` | `float` | finite, 0보다 큰 서버 action snapshot |

이 cue는 RPC용이므로 Mirror Weaver가 읽어 쓸 수 있는 **public 값 필드의 mutable `struct`**로 선언하고, 서버는 생성 뒤 복사본을 변경하지 않는다. get-only property나 `readonly struct` 자동 역직렬화를 가정하지 않는다. `PlaybackSpeed`는 cue에 넣지 않는다. 서버는 판정/action duration만 소유하고, 각 클라이언트가 현재 Override clip 길이를 읽어 `clipLength / Duration`으로 `ActionSpeed`를 계산한다. `RequestId`와 `Sequence`는 별도 필드이며 slash 표기로 합치지 않는다.

### 4.2 `AnimationCatalogSO`

| 필드 | 타입 | 기본값 후보 | 용도 |
|---|---|---:|---|
| `_playerBaseController` | `RuntimeAnimatorController` | PlayerBase | 공통 구조 검증 |
| `_playerWeapons` | `PlayerWeaponAnimationSet[]` | 3개 | 무기 Override/시각 |
| `_enemies` | `EnemyAnimationSet[]` | 3개 | 잡귀 Override |
| `_boss` | `BossAnimationSet` | 1개 | 보스 Controller와 네 템플릿/phase state 매핑 |
| `_defaultFixedTransitionSeconds` | `float` | 0.08초 | 일반 `CrossFadeInFixedTime` 전이 |
| `_hitFixedTransitionSeconds` | `float` | 0.04초 | 피격 `CrossFadeInFixedTime` 전이 |
| `_lateCueSkipThreshold` | `float` | 1.0 normalized | 만료 cue 생략 |
| `_offscreenCullingMode` | `AnimatorCullingMode` | `CullUpdateTransforms` | 비가시 bone/IK 쓰기 생략 후보 |

`PlayerWeaponAnimationSet`:

| 필드 | 타입 |
|---|---|
| `WeaponKind` | `WeaponKind` |
| `Controller` | `AnimatorOverrideController` |
| `WeaponVisualPrefab` | `GameObject` |
| `AttackAStateName`, `AttackBStateName`, `CastStateName`, `ParryStateName`, `ParrySuccessStateName` | `string` — 부트 시 hash 캐시 |
| `ExpectedAttackClipCount` | `int` — 에디터 검사 전용 |

`ParryStateName`과 `ParrySuccessStateName`은 Sword에서만 필수이며 Bow/Bell은 빈 값을 허용한다. `CastStateName`은 Bell에서 필수다.

`EnemyAnimationSet`은 `EnemyKind Kind`, `AnimatorOverrideController Controller`, `string TelegraphStateName`, `AttackStateName`, `HitStateName`, `StaggerStateName`, `DeathStateName`, `float DeathPresentationDuration`을 가진다. 보스는 `RuntimeAnimatorController Controller`, 네 템플릿/phase transition의 상태 이름, `float DeathPresentationDuration`을 가진 `BossAnimationSet` 하나로 둔다. 시작 후보는 잡귀 1.2초, 보스 2.5초이며 둘 다 표현용 값이다. 상태 이름은 부트 시 hash로 바꾸고 중복 kind, null controller, 빈 필수 상태, base clip override 누락, death duration 비양수/비유한 값을 `GeumeumCheckMenu`에서 거부한다.

실제 공격 피해·패링 창·action duration은 애니메이션 clip이 아니라 서버 공격 실행 snapshot이 소유한다. 플레이어는 02가 확정한 `WeaponAbilityConfigSO.StartupDuration/ActiveDuration/RecoveryDuration` 세 필드를 공격 시작 때 snapshot하고, cue `Duration = StartupDuration + ActiveDuration + RecoveryDuration`, impact offset은 `StartupDuration`으로 파생한다. 합계 duration이나 impact offset용 SO 필드는 중복 추가하지 않는다. `EnemySO.StatEntry`에는 현재 없는 `AttackExecutionDuration: float`을 추가하며 시작 후보는 허깨비 0.7초, 액귀 0.8초다. 두 종류의 cue `Duration = WindupDuration + AttackExecutionDuration`, impact offset은 `WindupDuration`이다. 창귀 돌진은 별도 값을 복제하지 않고 최대 실행 시간을 `WindupDuration + ChargeDistance / max(ChargeSpeed, epsilon)`으로 snapshot하며 실제 impact는 서버 충돌 시각이다. AI는 공격 시작 때 이 값과 windup, charge 거리/속도를 immutable 실행 snapshot으로 잡아 cue와 서버 impact에 함께 쓴다. `AttackCooldown`은 다음 공격 gate이며 cue duration에 더하지 않는다. 보스 cue는 `Duration = TelegraphDuration + ActiveDuration + RecoveryDuration`, impact offset은 `TelegraphDuration`으로 snapshot한다. `AnimationCatalogSO`는 이 판정 수치를 복제하지 않는다.

`Assets/01.Scripts/00.Common/Data/GameDatabaseSO.cs`에 `_animation` 필드/property를, `GameDB.cs`에 `Animation` property를 추가하고 `CollectMissingReferences`와 `Assets/01.Scripts/00.Common/Editor/GeumeumCheckMenu.cs` 검사에 포함한다. `PlayerAnimationController.Awake`는 component/현재 Animator만 캐싱하고 `GameDB`를 읽지 않는다. `Start`에서 `GameDB.IsInitialized`를 확인해 catalog/state hash/clip length를 초기화한 뒤, 그 전에 도착한 appearance/weapon hook은 현재값 하나만 보류했다가 적용한다. Unity는 ScriptableObject를 인스턴스와 분리된 공유 데이터 자산으로 제공한다. Controller·프리팹 참조와 표현 조정값을 `GameDB` 한 곳에서 검증하기에 현재 구조와 맞는다. [Unity ScriptableObject](https://docs.unity3d.com/6000.1/Documentation/Manual/class-ScriptableObject.html)

### 4.3 임포트 프리셋

| 항목 | 설정 |
|---|---|
| Rig | `Humanoid`, Avatar 유효성 초록 표시 확인 |
| Animation Type | Humanoid, 동일 캐릭터 세트는 가능한 한 동일 Avatar 정의 재사용 |
| Root Transform Rotation / Position Y / XZ | 판정 이동 클립은 `Bake Into Pose`; 코드의 `applyRootMotion=false` 유지 |
| Loop Time / Loop Pose | Idle/Walk/Run만 켬; 공격/피격/사망은 끔 |
| Animation Compression | 우선 `Optimal`, 손/무기 떨림이 보이면 해당 클립만 낮춤 |
| Resample Curves | 소스 문제 없으면 기본 유지; 일괄 토글 금지 |
| Scale | 모든 소스 1m 기준 실측, 무기 socket에서 최종 보정 |

Unity는 root motion을 애니메이션이 GameObject를 이동시키는 방식으로 설명한다. 이 프로젝트는 Rigidbody/서버 이동이 원본이므로 `applyRootMotion=false`와 XZ bake를 유지한다. [Unity Root Motion](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html), [Animator.applyRootMotion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-applyRootMotion.html)

압축은 빌드 크기와 메모리를 줄이지만 artifact를 만들 수 있으므로, 모든 클립을 무압축으로 두거나 최고 압축을 일괄 적용하지 않는다. Unity도 압축과 artifact의 교환관계를 명시한다. [Unity animation compression API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-animationCompression.html)

## 5. 흐름

### 5.1 플레이어 공격

1. 02 공격 설계의 `[계획] PlayerAttack` 수직 슬라이스가 먼저 완료되어 있어야 한다. 로컬 소유자가 공격 입력을 누르면 `PlayerAttack`이 0을 건너뛰는 request ID를 발급하고 `PlayerAnimationController.ClientPredictAction`을 호출한다.
2. 같은 프레임 02의 `CmdRequestAttack(AbilityRequest request)`를 보낸다. 클라이언트는 weapon kind·damage·duration을 보내지 않는다.
3. 서버가 phase, 생명, 현재 `PlayerWeapon`, 행동 중 여부, 쿨다운, 자원, 후보 거리/시야를 검증한다.
4. 거부하면 02의 `PlayerAttack.TargetAttackResult(AbilityResult)`로 소유자에게 구체 code를 보내 예측 모션을 `CrossFadeInFixedTime`으로 취소한다.
5. 승인하면 서버가 action 시작/impact/end 시각과 actor별 sequence를 확정하고 소유자에게 `TargetAttackResult(Accepted)`, 관찰자에게 `[ClientRpc(includeOwner = false)] PlayerAttack.RpcAttackStarted(cue)`를 보낸다. 소유자는 같은 request ID의 예측을 유지하고, 관찰자는 clip length와 cue duration으로 `ActionSpeed`를 먼저 설정한 뒤 지연만큼 fixed-time offset으로 시작한다.
6. 서버 판정 시각에 hitbox/투사체/치유/버프를 실행한다. Animation Event는 서버 판정에 관여하지 않는다.
7. 로컬 Animation Event는 발소리, 무기 trail on/off, 발사 VFX 같은 **표현 전용** callback만 허용한다.

### 5.2 피격·경직·사망

1. 서버 `PlayerStatus.TakeDamage`는 현재 `void` 계약을 유지하되 실제 `_health`를 감소시킨 직후에만 actor별 damage sequence를 증가시키고 `DamageAppliedEvent`를 발행한다. 무적·이미 사망·0 damage early return에서는 발행하지 않는다.
2. 플레이어 피격 RPC 소유자는 02 선행의 sibling `PlayerAttack` 하나로 고정한다. `PlayerAttack.OnStartServer`가 같은 플레이어 netId의 `DamageAppliedEvent`와 `StaggerAppliedEvent`를 구독하고, 비치명 피해면 `RpcPlayHit(HitStrength, sequence, startServerTime)`, 경직이면 `RpcPlayStagger(sequence, startServerTime, duration)`를 보낸 뒤 `OnStopServer`에서 해제한다. 잡귀는 `EnemyController`, 보스는 `BossController`가 같은 역할을 한다. `MonoBehaviour` controller/bridge는 RPC를 선언하지 않는다.
3. 현재 hook 없는 `PlayerStatus._lifeState`를 `[SyncVar(hook=nameof(OnLifeStateChanged))]`로 바꾸고, hook이 Downed/Spectating durable state를 재생한다. transient RPC만으로 사망 상태를 보관하지 않는다. 초기 spawn 값도 `OnStartClient`에서 같은 apply 메서드로 한 번 적용하고 hook과 dedupe한다.
4. 부활 시 서버 상태가 Alive/ReviveInvulnerable로 바뀌고 hook은 pending attack/hit/stagger trigger를 지운 뒤 Locomotion으로 복귀한다.
5. 잡귀·보스 Status의 fatal 순서는 `HP=0/IsDead 커밋 → DamageAppliedEvent(WasFatal=true) → 기존 Died/EnemyDiedEvent 보상`으로 고정한다. fatal event는 hit 대신 각 소유 `EnemyController`/`BossController`가 `RpcPlayDeath(sequence, startServerTime, duration)`를 보낸다. 서버는 같은 틱에 사망·보상·AI/agent/서버 collider 비활성화를 확정하고, 기존 잡귀의 즉시 `NetworkServer.Destroy`만 `GameDB.Animation`의 해당 `DeathPresentationDuration` 뒤로 미룬다. scheduler의 dead 분기가 파괴 시각을 먼저 검사하며 `OnStopServer`에서 예약을 해제한다. 클립/Event가 보상이나 사망 시점을 늦추지는 않는다. Result/scene teardown이 더 먼저 오면 즉시 파괴해도 중복 보상은 없다.

### 5.3 외형·무기 변경

1. `PlayerAppearance`가 활성 모델을 바꾸고 `ActiveAnimator/ActiveWeaponSocket`을 커밋한 뒤 `ActiveVisualChanged`를 한 번 호출한다.
2. `PlayerAnimationController`가 새 Animator/Avatar를 검증하고 현재 `WeaponCombatSnapshot.WeaponKind`의 Override Controller를 배정한다.
3. `PlayerWeaponVisual`이 전달받은 authored socket에 해당 무기 시각 1개를 붙인다.
4. 현재 hook 없는 `PlayerWeapon` snapshot을 `[SyncVar(hook=nameof(OnWeaponSnapshotChanged))]`로 보강한다. hook은 외형을 유지한 채 Override와 무기 시각만 바꾸며, 초기 spawn snapshot도 `OnStartClient`에서 같은 apply 메서드로 한 번 적용한다.
5. Controller 교체 후 현재 상태가 초기화되는지 M2에서 확인한다. 같은 base Controller 기반 Override 교체는 Unity API상 상태를 유지할 수 있지만, 실제 모델 활성/비활성 순서까지 포함해 검증한다. [Unity AnimatorOverrideController API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorOverrideController.html)

## 6. 단계별 구현 순서

1. **에셋 감사·소싱 고정(6~10시간):** 7개 Controller와 모든 clip/GUID/라이선스를 표로 만들고, 필수 clip matrix의 빈칸·실제 상품/무료 출처·현금 구매 상한을 확정한다. `AnimationCatalogSO`의 GameDB 연결과 부트 validator도 만든다.
2. **플레이어 수직 슬라이스(12~18시간):** 02의 `PlayerAttack` 승인/거부 수직 슬라이스와 04의 weapon snapshot 계약을 선행 확인한다. PlayerBase + Sword Override, `PlayerAttack` RPC 소유, `DamageAppliedEvent`, life/weapon hook, Idle/Walk/Run/Roll/AttackA/Hit을 차사 외형 1개에서 연결한다.
3. **3외형 리타게팅(6~10시간):** 같은 Sword Override를 세 Humanoid Avatar에 적용하고 발 미끄러짐·관절 뒤틀림·socket을 수정한다.
4. **활·방울(10~16시간):** Bow/Bell Override와 무기 시각, projectile/cast cue를 연결한다. 방울 클립만 최소 수작업 보정한다.
5. **잡귀·보스(14~20시간):** `EnemySO.AttackExecutionDuration`, 공격 실행 snapshot, Enemy/Boss `NetworkBehaviour` RPC 소유자와 bridge, Telegraph/Attack/Hit/Stagger/Death를 붙인다.
6. **네트워크·성능 검증(10~16시간):** cue dedupe/거부/지연 offset, 3인, 다수 잡귀, culling·메모리 프로파일.

2단계 완료 정의는 “M2에서 소유자는 즉시 검 공격을 보고, 관찰자 둘은 같은 서버 공격을 한 번씩 보며, 승인/거부와 실제 HP 감소 cue가 중복되지 않고 피해는 서버 impact에 한 번만 발생”이다. 4단계부터 직업별 기본 공격 루프가 동작한다. 최종 완료는 필수 matrix에 빈칸이 없고 M3에서 모션 지연이 판정 불일치로 이어지지 않으며 GTX 1060 3GB 정량 기준을 통과한 상태다.

### 필수 clip matrix

| 대상 | 필수 | 후순위 |
|---|---|---|
| 플레이어 공통 | Idle, Walk, Run, Roll, Hit Light/Heavy, Stagger, Downed/Death | Revive 전용, Turn-in-place |
| 검 | Attack A/B, Parry, Parry Success | 3타 combo, finisher 전용 |
| 활 | Draw/Release 또는 Attack A, Hit | Aim offset/손 IK |
| 방울 | Attack/Cast A, Buff/Heal | 다중 cast 변형 |
| 잡귀 각 종 | Idle, Move, Telegraph, Attack, Hit, Death | 공격 변형 2개 이상 |
| 보스 | Idle/Move, 페이즈별 Telegraph/Attack, Hit, Stagger, Death | 8패턴 전용 clip 8개; 템플릿 재사용 우선 |

## 7. 검증

| 등급 | 상황 | 통과 기준 |
|---|---|---|
| M0 | Avatar/Controller 검사 | 모든 모델 `isHuman`, Avatar valid, 필수 파라미터/상태/Override 빈칸 0 |
| M0 | cue/catalog 부트 검사 | enum/상태 매핑 누락, sequence 0, duration 0/NaN/Infinity, 중복 EnemyKind, null controller/prefab을 각각 거부; `Awake` GameDB 접근 0 |
| M0 | root motion | 60초 Idle/Walk/Attack 반복 후 VisualPivot local position drift 1cm 이하 |
| M2 | 승인·거부 | 3인에서 정상 공격 20회당 소유 예측 20회·관찰자 재생 각 20회·피해 20회, 자원 부족 거부 20회는 0.08초 fixed transition 복귀·피해 0회 |
| M2 | 3외형×3무기 | 9조합 각 3회, Controller/무기 socket/공격 clip 오바인딩 0 |
| M2 | cue 중복 | 호스트·관찰자 각 공격 50회, 모션 재시작/이중 trail/이중 사운드 0 |
| M2 | 실제 피해 cue | 무적·0 damage·이미 Downed 각 20회 hit 모션 0, 실제 HP 감소 20회 hit 모션 각 클라이언트 20회 |
| M2 | 잡귀·보스 사망 표현 | 각 10회, 사망/보상은 서버에서 즉시 1회, hit 대신 death RPC 1회, 네트워크 오브젝트는 설정 duration 허용오차 1 tick 뒤 파괴 |
| M2 | durable hook | 초기 spawn, 무기 교체, Downed→Spectating→ReviveInvulnerable 각 20회, hook/apply 중복 0·죽은 뒤 action 재생 0 |
| M3 | 서로 다른 3PC 30/50/150ms + 2% loss/jitter | 각 20회, 관찰자 늦은 cue가 fixed-time offset으로 시작하고 중복/역순 재생 0, 서버 판정 3인 동일 |
| M2 | 피격 경쟁 | 공격·구르기·피격·사망이 같은 0.2초 안에 겹치는 조합 각 10회, 죽은 뒤 공격 상태 잔류 0 |
| 시각 | 손·발 | 손 관통, 활 반전, 무기 이탈, 발 미끄러짐을 캡처해 조합별 체크. 첨부물만으로는 미검증 |
| 성능 | GTX 1060 3GB, 3인+잡귀 22+보스 | Development Build 1080p에서 p95 frame 16.67ms 이하, CPU Animation p95 4ms 이하, animation steady-state GC.Alloc 0B/frame, VRAM peak 2.5GB 이하 |

비가시 Animator에 `CullUpdateTransforms`를 적용하면 Unity는 state machine/root motion은 평가하되 bone transform/IK 쓰기를 생략한다. 서버 판정은 Animator에 의존하지 않으므로 후보 설정으로 적합하지만, 화면 밖 VFX socket이 필요한 캐릭터는 개별 예외로 둔다. [Unity Animator culling](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorCullingMode.CullUpdateTransforms.html)

## 8. 대안과 선택 근거

### 채택: Humanoid retarget + 기본 Controller + 무기별 Override

Unity 공식 문서가 유효 Avatar를 가진 Humanoid 모델 간 동일 애니메이션 세트 재사용을 지원하며, Override Controller는 상태 머신을 유지한 채 clip variation을 만드는 용도다. 첨부 설명상 모든 캐릭터 Avatar가 유효하므로 이 표준 경로의 전제도 충족한다. [Unity Humanoid Retargeting](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html), [Unity Animator Override Controller](https://docs.unity3d.com/6000.0/Documentation/Manual/AnimatorOverrideController.html)

3인 리슨 서버에서는 서버 판정과 렌더링을 호스트 한 대가 함께 처리하고, 최소 GPU는 GTX 1060 3GB이며, 세 사람이 11주 안에 콘텐츠까지 끝내야 한다. 판정은 기존 서버 SO/action snapshot에 남기고 클라이언트 Animator는 cue만 표현하면 호스트 판정 비용이 clip 평가에 묶이지 않는다. Humanoid clip 공유와 base 1개+Override 3개는 외형별 9개 Controller 복제를 피하고 메모리·검증 조합을 제한하므로 3GB VRAM과 11주 모두에 맞으며, Unity 일반 자료로 검색할 수 있어 독자 그래프보다 학습 비용도 낮다.

### 소싱 우선순위

1. **현재 보유 clip 재사용:** 라이선스와 품질이 확인된 것은 가장 싸다.
2. **Mixamo:** Idle/Walk/Run/Roll/Hit/Death와 범용 cast 후보. Adobe는 Mixamo 캐릭터·애니메이션을 게임을 포함한 개인·상업·비영리 프로젝트에서 royalty-free로 사용할 수 있다고 안내하며, 자동 리깅/라이브러리는 이족 Humanoid 대상이다. 현재 Avatar 전제와 맞는다. [Adobe Mixamo FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html)
3. **Unity Asset Store 애니메이션 팩:** 검·활처럼 무기 접점 품질이 중요한 공격. 상품 페이지의 Non-standard/Restricted 표기를 확인하고 좌석 수를 맞춘다. Unity FAQ는 적절한 라이선스의 에셋을 게임에 통합한 상업 이용을 허용한다고 설명한다. [Unity Asset Store EULA FAQ](https://assetstore.unity.com/ko-KR/browse/eula-faq)
4. **최소 제작:** 방울 고유 동작과 보스 key pose만 Blender/Unity Animation Window에서 기존 clip을 수정한다. 처음부터 전 clip을 제작하지 않는다.

각 에셋은 출처, 상품/FAQ URL, 취득일, 버전, 라이선스 유형, 사용 파일 경로를 `docs/licenses/THIRD_PARTY_ASSETS.md`에 기록한다. 원본 FBX를 재배포 가능한 형태로 노출하지 않는다. 이는 법률 의견이 아니라 프로젝트 증빙 절차다.

### 기각: 외형별 독립 Controller 3~9개

현재 차사/신궁/무녀 Controller처럼 복제하면 외형과 무기 개념이 뒤섞이고 전이 수정이 갈라진다. 동일한 구조를 공유한다는 공식 Override 용도와 반대이며 유지비만 늘어난다.

### 기각: `NetworkAnimator`를 판정 원본으로 사용

NetworkAnimator는 표현 동기화 도구일 뿐 서버 impact, target, damage 계약을 대신하지 않는다. 파라미터 전체 동기화는 공격 request ID의 승인/거부와 지연 offset을 설명하기도 어렵다. 현재는 승인 cue RPC + durable SyncVar가 더 작은 표면이다. [Mirror GameObjects](https://mirror-networking.gitbook.io/docs/manual/guides/gameobjects)

### 기각: Animation Event에서 피해·투사체 스폰

클라이언트 clip 재생 시각, 압축, 프레임 드롭, Override 길이에 따라 결과가 달라진다. 서버 SO 타이밍이 판정하고 Event는 trail/음향처럼 결과를 바꾸지 않는 표현에만 쓴다.

### 보류: Animation Rigging/IK

활의 보조손과 조준 품질에는 유용하지만 패키지 추가, rig constraint, 캐릭터별 보정과 성능 검증이 필요하다. 9조합에서 socket+적합 clip으로 감출 수 없는 오차가 캡처될 때 활 하나에만 제한 도입한다.

## 9. 비용과 위험

**엔지니어링 부분합: 대, 58~90시간.** 필요한 공격 clip을 기존/Mixamo/구매 에셋에서 확보한다는 전제다. 현금 구매 상한 후보는 팀 합계 **30만원**이며 1단계의 상품 URL·라이선스 좌석 수·필수 clip matrix를 팀이 승인하기 전에는 0원을 집행한다. 신규 외세 침입군 병사 원혼 모델, 전용 8개 모션 제작, 고급 IK, 최종 VFX·사운드, 02 `PlayerAttack`과 04 장비 snapshot 본체 구현 시간은 제외한다.

크리티컬 패스는 `PlayerAttack 승인/거부 계약 → WeaponCombatSnapshot hook → AnimationCatalogSO/GameDB 부트 → 검 1외형 M2 수직 슬라이스 → 3외형×3무기 → 잡귀/보스 RPC bridge → M3/GTX 1060` 순서다. 03 보스와 04 무기 교체가 같은 catalog/hook 작업을 다시 비용에 넣지 않게 공용 이슈로 추적한다.

| 위험 | 영향 | 완화 |
|---|---|---|
| 애니메이션 에셋을 아직 확보하지 않음 | 일정 예측이 흔들림 | 1단계에서 1일 안에 clip matrix와 구매 상한을 고정; 빈칸은 placeholder로 수직 슬라이스 진행 |
| 외형×무기 9조합의 손 위치 차이 | 무기 이탈·손 관통 | 외형별 authored socket, 무기별 prefab offset, M2 9조합 캡처 |
| 손가락 본 없음 | 활시위·그립 품질 저하 | 손가락 의존 clip 제외, 닫힌 손/장갑/그립으로 은폐, 활만 필요 시 제한 IK |
| 예측 모션과 서버 거부 | 유령 공격처럼 보임 | request ID dedupe, 구체 거부 code, 초 단위 `CrossFadeInFixedTime` 복귀; 판정/VFX 확정은 서버 후 |
| Override clip 길이 차이 | impact와 모션 불일치 | gameplay duration은 서버 실행 snapshot 원본, `ActionSpeed`로 맞춤, normalized exit time + fixed-time 코드 transition 사용 |
| root motion이 기존 Rigidbody 이동과 충돌 | 순간이동·재조정 증가 | import Bake Into Pose, `applyRootMotion=false`, drift 자동 검사 |
| Controller 자산 통합 중 GUID 참조 손실 | 프리팹 Animator 누락 | 기존 Controller를 즉시 삭제하지 않고 새 base/override를 병렬 생성·프리팹 교체 후 참조 검사 |
| 압축·culling artifact | 손 떨림/화면 밖 socket VFX 누락 | 문제가 있는 clip/캐릭터만 예외, Development Build 프로파일 증거 기록 |
| Mixamo/Asset Store 라이선스 기록 누락 | 출품·배포 리스크 | 취득 시점에 `THIRD_PARTY_ASSETS.md` 갱신 없이는 import 완료로 보지 않음 |
| `PlayerAttack`/weapon snapshot보다 먼저 착수 | RPC 소유자·hook 재작업 | 2단계 진입 게이트에서 두 선행 계약의 컴파일/API 검사를 통과해야 함 |
