# 플레이어 무기 부착과 손 파지

## 적용 범위

차사·신궁·무녀가 검·활·방울을 사용하는 9개 조합은 `Player.prefab`의
`PlayerWeaponVisual._bindings`에서 각각 위치·회전·배율을 가진다.
무기 모델은 오른손 `RightHand`에 하나만 붙인다. 활은 미러된 모션을 사용하므로
오른손이 활을 잡고 왼손이 줄·화살 표현의 기준점이다.

## 손 리그

기존 캐릭터에는 손가락 본이 없어 애니메이션의 손가락 muscle 곡선이 표현되지 않았다.
각 `Chasa_Art`, `Singung_Art`, `Munyeo_Art`에는 양손에 15개씩, 총 30개의
`RightGrip*` / `LeftGrip*` 본을 추가했다. 엄지부터 소지까지 각각 3관절이다.
몸체 스킨의 본은 기존 24개에 추가 본을 더한 54개다.

- 원본 FBX, Avatar, 전신 애니메이션, 정점 위치, 삼각형, UV는 유지한다.
- `*_GripSkin.asset`은 원본 메시에서 손의 스킨 가중치와 bind pose만 확장한 파생 에셋이다.
- 손목 및 몸체의 기존 가중치는 보존하고, 손에 할당됐던 가중치를 손가락 관절에 분배한다.
- 추가 본은 Humanoid Avatar의 손가락 매핑으로 구동하지 않는다. `PlayerWeaponGrip`이 직접 구동한다.
- 큰 파생 메시 3개만 `.gitattributes`의 `*_GripSkin.asset` 규칙으로 Git LFS에서 관리한다.

## 런타임 적용

`PlayerWeaponVisual.Refresh()`가 모델과 무기를 확정한 뒤 모델 루트의
`PlayerWeaponGrip.ConfigureWeapon()`을 호출한다. 교체·무기 해제 시 이전 파지를 복원한다.
개발용 `CharacterPreviewRig`도 같은 파지 컴포넌트를 사용한다.

`PlayerWeaponGrip`은 Animator 평가 뒤 `LateUpdate`에서 관절 회전을 적용한다.
실행 순서는 `PlayerAnimationController`(0) → `PlayerWeaponGrip`(50) →
`PlayerCombatPresentation`(100)이다. 정지 프레임의 `Animator.Update(0)`가 손목 보정을 덮어쓰지 않게 한다.
검·활·방울의 각도는 관절별 `WeaponCurlDegrees`의 X·Y·Z에 저장한다.
왼손 관절은 활에서만 굽히며 검·방울에서는 원래 포즈를 유지한다.

활의 손목 보정은 `BowAttack`, `BowDraw`, `BowHold`, `BowRelease`에서만 적용하며
Animator 전환 진행도에 맞춰 섞는다. 대기·이동·슬라이딩·구르기에는 적용하지 않는다.
다음 `Update`에 이전 보정을 복원한 뒤 새 Animator 결과에 적용하므로 회전이 누적되지 않는다.
무기 교체 및 비활성화에서도 복원한다. 전투 판정이나 서버 상태는 이 컴포넌트에서 변경하지 않는다.

## 재생성 및 조정

1. `Assets/09.Data/Presentation/Grip/{캐릭터}-rig.json`에서 오른손 관절 위치·축·각도와 정점 가중치를 조정한다.
2. Unity 메뉴 `Geumeum/Art/Rebuild Player Hand Rigs`를 실행한다.
3. 베이커는 원본 메시를 읽고 왼손을 모델 중심면 반사와 근접 정점 대응으로 생성한다.
4. 기존 파생 메시 GUID를 유지하면서 3개 아트 프리팹의 본·스킨·파지 컴포넌트를 갱신한다.
5. `Player.prefab`의 9개 부착값은 별도로 조정한다. 리그 재생성은 이 값을 덮어쓰지 않는다.

JSON의 위치와 회전축은 손 본의 로컬 좌표다. `weights.vertex`는 **원본 메시의 정점 인덱스**다.
원본을 리토폴로지하거나 정점 순서를 바꾸면 가중치 데이터를 다시 작성해야 한다.
무녀의 몸체 원본은 Avatar FBX가 아니라 `DetailV3/Munyeo_Game_Body_Bound.asset`이다.

## 검증 기준

- `PlayerWeaponGripTests`: 무기별 회전, 복원, 100프레임 비누적, Animator 결과 반영, 조준 상태 제한.
- `WeaponGripAssetTests`: 생산 9개 바인딩, 양손 30관절, 54본·bind pose, 원본 형상·UV 유지,
  가중치 합·인덱스, 비손 정점 불변, 휴식 자세 무변형.
- `WeaponMotionTests`, `CombatHitFeedbackTests`: 무기 모션 연결과 칼날/피격 표현 회귀.
- 9개 조합 각각 대기·이동·일반 공격·R·보조 동작·구르기를 렌더링해 손잡이 정렬을 확인한다.
- 실제 호스트와 클라이언트에서 같은 부착값·손가락 포즈를 확인하고 교체 후 무기 모델이 하나인지 검사한다.

손의 원본 형상·텍스처와 의상은 유지하므로, 손가락 리토폴로지나 고해상도 손 텍스처 제작과는 별개다.
