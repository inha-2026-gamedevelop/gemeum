# 플레이어 애니메이션·행동 표현 파이프라인

2026-09-22 코드·자산 기준. 예전의 단일 Controller + 무기별 Override 3개 제안은 현재 구조가 아니다.

## 실제 구성

| 구성 | 현재 책임 |
|---|---|
| `PlayerActionCue` | ExecutionId, Kind, Weapon, ComboStage, StartGameplayTime, ImpactGameplayTime, EndGameplayTime |
| `PlayerAnimationController` 및 `weaponMotions` partial | 무기별 RuntimeAnimatorController·파라미터·행동 모션 |
| `PlayerCombatPresentation` | 수락된 행동의 VFX·SFX |
| `PresentationConfigSO` | 검기 등 표현 설정 |
| `MeleeSlashView` | 실제 무기 궤적 기반 검기 |
| `PlayerPresentation` | 외형·장착 표현 |
| `PlayerWeaponGrip` | 양손 손가락 파지와 조준 중 손목 보정. 애니메이션 뒤, VFX 앞에서 적용 |

실제 컨트롤러는 `Assets/05.Animator/`의 `PlayerLocomotion.controller`, `ChasaLocomotion.controller`, `SingungLocomotion.controller`, `MunyeoLocomotion.controller` 등이다. `AnimationActionCue`, `AnimationCatalogSO`, `EnemyAnimationBridge`, `BossAnimationBridge`라는 예정 타입을 현재 API로 사용하지 않는다.

## 권위와 시간

서버가 수락한 행동을 cue로 전달하고 클라이언트가 그 시각에 맞춰 재생한다. 모션·VFX·로컬 표현 정지는 클라이언트 책임이다. Animation Event, 검 메시, TrailRenderer를 피해·자원·쿨다운 확정의 원본으로 쓰지 않는다.

공격 방향은 이동 방향과 독립적이다. 마우스로 조준한 공격과 서버 확정 방향이 몸 방향·무기 궤적에 반영되어야 한다. 단순히 왼쪽 이동 모션을 유지한 채 오른쪽으로 발사하는 결과는 회귀 대상으로 취급한다.

## 수정 작업 순서

### 구르기 클립

`Assets/04.Animations/Player_Roll.anim`은 `Stand To Roll.fbx` 원본의 10~60프레임을 사용한 Humanoid 파생 클립이다. 준비·회복 자세를 포함하고, 중간 자세의 몸 높이를 보정했다. 원본 FBX와 기존 임포트 범위는 보존한다.

Player·Chasa·Singung·Munyeo·Bow 컨트롤러의 `Roll` 상태가 이 클립을 공유한다. X/Z 루트 이동은 제거했으며 실제 이동·무적 시간은 기존 서버 구르기 규칙을 따른다. 재생 속도는 `RollSpeed`로 `CombatConfigSO.RollTotalDuration`에 맞춘다. 복귀 전환은 정규화 시각 0.94에서 시작하고 고정 0.06초 동안 진행해 일어서는 동작을 보존한다.

클립 교체 시 세 캐릭터의 바닥 관통, 9개 무기 조합의 파지, 락온 중 전후좌우 이동과 원격 관찰을 함께 확인한다. 구르기 표현을 고치면서 이동 거리·소모·무적 시간을 바꾸지 않는다.

### 공통 순서

1. 기존 cue 종류와 `PlayerAnimationController`의 파라미터·무기별 모션 경로를 읽는다.
2. 외형과 장착 무기를 독립적으로 조합해 Avatar·클립·손 소켓을 확인한다.
3. 준비·타격·후딜을 서버 행동 시각과 맞추고 중복 ExecutionId 재생을 확인한다.
4. 피격·회피·사망·무기 변경에서 이전 행동과 로컬 VFX를 정리한다.
5. 정지·보행·달리기에서 전후좌우 조준, 활 장전/발사, 검 반격, 방울 조준/설치를 비교한다.

## 연출과 후속 작업

[무기 부착과 손 파지](PLAYER_WEAPON_GRIP.md)는 3캐릭터 × 3무기의 바인딩, 추가 손 본, 스킨 재생성 및 검증 기준을 설명한다.

[검기](SLASH_VFX_DESIGN.md), [무기 스킬](WEAPON_ACTIVE_SKILLS.md), [사운드](SOUND_DESIGN.md)를 함께 적용한다. 잡귀·보스는 각각 기존 AI/패턴 실행 경로와 Animator를 확인하며 플레이어 cue 계약을 자동으로 공유한다고 가정하지 않는다.

현재 코드와 자산이 존재하는 것은 시각적 품질 완료를 의미하지 않는다. L2 캡처와 서로 다른 PC 3인 L3에서 타격 시점·방향·취소·원격 재생을 확인해야 한다. 반입 에셋은 [라이선스 대장](../licenses/THIRD_PARTY_ASSETS.md)에 기록한다.
