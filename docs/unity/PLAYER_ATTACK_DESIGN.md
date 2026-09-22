# 플레이어 공격 — 현재 구현 계약

2026-09-22 `PlayerAttack` 및 관련 partial, SO와 표현 코드 기준. 과거의 requestId만 전송/방울 미구현/화살 유도 없음 설명은 현재 계약이 아니다.

## 1. 구조와 책임

코드 위치는 `Assets/01.Scripts/02.Combat/Player/`다. `PlayerAttack.cs`와 `.abilities.cs`, `.counter.cs`, `.skills.cs`, `.placement.cs`가 기본 공격·무기 능력·R·설치 요청을 처리한다. HP·스태미나·영력·명중·피해·표식은 서버 판정이며 클라이언트는 입력과 표현을 담당한다.

| 무기 | 현재 행동 | 관련 상태 |
|---|---|---|
| 검 | 연격 기본 공격, 일반 R, 흘려내기로 쌓은 반격 표식 소비 R | `ParryGauge`, `ParryController` |
| 활 | 일반/정밀 화살, R 접근·쐐기 설치, 일반2회 또는 정밀1회 파열 | `PrecisionShotController`, 쐐기 처리 |
| 방울 | 기본 공격·잔향 회수·지원 능력, R 위치 미리보기→확정/취소 | `SupportBuffController`, placement partial |

일반 R과 검 반격 R의 자원·쿨다운 경계를 같다고 가정하지 않는다. 방울 R은 눌렀을 때 즉시 설치하는 스마트키가 아니다. 상세 계약은 [검·활](SWORD_BOW_REWORK_IMPLEMENTATION.md), [방울](BELL_RESONANCE_IMPLEMENTATION.md)을 따른다.

## 2. 입력과 서버 수락

기본 공격은 `CmdRequestAttack(requestId, aim, cameraAim)`으로 조준 의도를 전달한다. 서버가 요청 순서, 상태·무기·자원·조준을 검증하고 실행한다. 피해나 상대 HP, 임의 타깃 성공을 클라이언트가 확정하지 않는다. 수락된 발사 방향은 모델 표현에도 반영한다.

후딜 종료 직전 입력은 제한된 한 슬롯 예약과 우선순위를 사용한다. 피격·무기 변경·런/씬 전환·정지에 따른 취소 경계를 보존한다. UI 열림과 설치 조준 중 일반 공격 입력이 동시에 새지 않도록 로컬 입력 게이트를 사용한다.

## 3. 조준·화살

자유 조준 방향과 카메라 조준 정보를 서버가 검증한다. 락온 대상은 `LockOnController`가 서버에서 승인한 대상을 사용하며, 발사 시점에 다시 확인해 발사 스냅샷에 담는다. 화살은 제한된 시간·회전 속도·시야 조건으로 유도한다.

`ArrowProjectile`은 발사선과 비행 sweep에서 `ArrowConfigSO.WorldMask`를 사용한다. 장애물과 적 명중은 서버에서 처리하고 관통 상한은 ArrowConfigSO에 둔다. 발사 후 무기 변경으로 이전 화살의 특성 권한이 되살아나지 않도록 무기/로드아웃 세대를 확인한다. [조준 상세](ARROW_AIM_ASSIST_DESIGN.md).

## 4. 전투 수치와 표현

- `CombatConfigSO`, `ArrowConfigSO`, `TalentTreeSO`, `EquipmentSO`가 수치 원본이다. UI 표시를 위해 별도 밸런스 상수를 만들지 않는다.
- `CombatStatCalculator`는 장비·특성 효과를 해석한다. 발사 snapshot과 현재 상태를 무조건 혼용하지 않는다.
- 서버 수락 후 `PlayerActionCue`와 `RpcAttackStarted`로 표현 시점을 전파한다. 명중·취소 표현도 RPC 경로로 전달한다.
- `PlayerAnimationController`와 `PlayerCombatPresentation`이 모션·VFX를 재생한다. 애니메이션 이벤트가 서버 피해의 원본이 아니다.
- `WeaponAbilityHUD`는 R 상태와 무기별 표식/공명 상태를 읽는다. 공격 입력을 HUD가 서버 판정으로 대체하지 않는다.

## 5. 특성·UI 연결

현재 39노드의 해금 규칙·ID·수치·초기화·UI 요청은 [특성 UI 인계](TALENT_UI_HANDOVER.md)에 정리돼 있다. 특성 변경 요청은 서버가 전투 상태와 revision까지 확인한다. UI의 가능 여부 미리보기는 서버 수락을 보장하지 않는다.

## 6. 검증

규칙/수치 EditMode 검증과 검·활·방울 대표 3프로세스 동작 기록이 있다. 현재 문서 정리에서 새 플레이 테스트를 수행한 것은 아니다. 후속 변경 시 일반 공격·정밀·R·설치 취소·피격 취소·무기 변경·원격 표현을 확인한다. 실제 보스 입력 타이밍, 고지연/손실, 외부PC3대 및 모든 특성 조합은 별도 검증한다.
