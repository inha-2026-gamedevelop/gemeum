# 신궁 화살 조준·명중 보조 — 현재 계약

2026-09-22 코드 기준. `PlayerAttack`, `LockOnController`, `PrecisionShotController`, `ArrowProjectile`과 `ArrowConfigSO`를 함께 읽는다.

## 입력과 대상

- 공격 요청은 requestId뿐 아니라 aim과 cameraAim을 전달한다. 서버가 조준을 검증한다.
- 락온 대상 요청은 `CmdSetLockOn`에서 서버가 확인한다. 발사 시 서버가 현재 `TargetNetId`와 부위를 재검증해 snapshot에 담는다.
- 자유 조준은 실제 요청 방향을 사용한다. 락온은 승인된 대상에 대한 제한 보정이며 클라이언트가 목표의 피해 판정을 확정하는 기능이 아니다.
- 정밀 사격은 `PrecisionShotController`의 시전/발사 경로가 구현돼 있다. 일반 공격과 구분해 취소·자원·특수 카운터를 검증한다.

## 발사와 비행

`PlayerAttack.TryResolveLockedAim`은 발사 시 보정 가능한 각도를 적용한다. `ArrowProjectile.UpdateGuidance`는 유도 시간·초당 회전각·LOS 조건에 따라 궤도를 조절한다. 대상 소실·시야 차단이 있을 수 있으므로 락온 UI 표시만으로 명중을 보장하지 않는다.

자기 쐐기가 활성 상태인 일반 사격은 발사 스냅과 유도를 적용하지 않고 수동 조준으로 쐐기 중심 궤적을 맞힌다. 락온 표시가 있더라도 이 예외를 유지한다.

발사선 및 비행 sweep은 `ArrowConfigSO.WorldMask`로 벽·환경 충돌을 처리한다. WorldMask가 0이면 장애물 판정이 없으므로 생산 에셋의 실제 레이어를 확인한다. 적 충돌과 월드 충돌의 순서, 같은 대상 중복 명중, 관통 횟수는 서버 처리다.

## 데이터 원본

| 데이터 | 의미 |
|---|---|
| ArrowSpeed / ArrowLifetime | 비행 속도와 최대 수명 |
| UnlockedHitRadius | 자유 조준의 구형 명중 보조 반경 |
| AimAssistAngle | 발사 순간 보정 최대 각도 |
| GuidanceDegreesPerSecond / GuidanceDuration | 발사 후 제한 유도 |
| WorldMask | 화살을 막는 레이어 |
| BasePierceCount / MaxPierceCount | 기본 관통과 상한 |

값은 `Assets/09.Data/`의 실제 ArrowConfigSO 에셋을 기준으로 읽는다. SO 클래스 기본값과 직렬화된 생산 값을 혼동하지 않는다. 일반·정밀 발사 snapshot에는 장비/특성 수치와 세대가 포함된다. 무기 변경 뒤 남은 화살이 옛 특성 효과를 다시 적용하지 않게 한다.

## 쐐기와 특성

R로 만든 쐐기는 일반 화살 2회 또는 정밀 1회에 파열하는 현재 구현을 따른다. 노드 2108의 추가 관통은 현재 일반·정밀 화살 모두 적용된다. 노드 2109의 0.4는 표시 시 공격력 40%이며 +0.4%가 아니다. 상세는 [검·활 구현](SWORD_BOW_REWORK_IMPLEMENTATION.md), [노드 원본 표](TALENT_UI_HANDOVER.md)를 따른다.

## 표현·검증

발사 방향으로 모델을 정렬하고, 수락된 `PlayerActionCue`를 기준으로 모션과 발사 표현을 맞춘다. 카메라 계약은 [카메라와 락온](CAMERA_AND_LOCKON.md)에 있다.

변경 시 무락온/락온/대상 소실/부위 변경/가까운 벽/관통/일반 및 정밀 파열/무기 교체 뒤 잔존 화살을 확인한다. 3인에서 서버 HP·파열 결과가 일치하고 소유자/관찰자 표현이 맞아야 한다. 이번 문서 갱신은 정적 대조이며 새 실전·고지연 검증은 아니다.
