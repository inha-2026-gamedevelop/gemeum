# 프로젝트 지식 맵

2026-09-22 현재 작업 트리 기준. 실제 코드·SO에서 현재 동작을 확인하고, 게임 기획의 목표와 구분한다. 사용자의 최신 명시 지시가 권한과 요구사항을 정한다.

## 기능별 진입점

코드 경로는 `Assets/01.Scripts/` 기준이다. Python·런처 경로는 저장소 루트 기준이다.

| 찾는 기능 | 공유 문서 | 실제 진입점 | 확인할 것 |
|---|---|---|---|
| 자체 계정·방·출발 | [서비스 구조](../unity/STEAM_INDEPENDENT_SERVICE_PLAN.md) | `backend/src/geumeum_backend/api.py`, `rooms.py`, `launcher/Launcher.UI/RoomWindow.xaml.cs` | 로그인, 수동 목록 새로고침, 파티 heartbeat, 출발 |
| 포트포워딩 없는 접속 | [릴레이](../unity/ACCOUNT_RELAY.md) | `01.Network/Identity/AccountRelayTransport.cs`, `AccountGameServer.cs` | 호스트 권위·계정 티켓·릴레이 전달 |
| 공격·무기 R | [공격](../unity/PLAYER_ATTACK_DESIGN.md), [검·활](../unity/SWORD_BOW_REWORK_IMPLEMENTATION.md), [방울](../unity/BELL_RESONANCE_IMPLEMENTATION.md) | `02.Combat/Player/PlayerAttack` partial, `ParryGauge`, `PrecisionShotController`, `SupportBuffController` | 수락/거절, 표식 소비, 설치/취소, 자원 |
| 패링 | [패링](../unity/PARRY_SYSTEM_DESIGN.md) | `ParryController`, `ParryJudge`, `ParryGauge` | 일반 흘려내기와 특수 카운터 구분 |
| 화살·락온 | [조준](../unity/ARROW_AIM_ASSIST_DESIGN.md), [카메라](../unity/CAMERA_AND_LOCKON.md) | `LockOnController`, `PlayerAttack`, `ArrowProjectile`, `PrecisionShotController` | 요청 조준·서버 대상 검증·LOS·월드 충돌 |
| 성장·특성 | [UI 인계](../unity/TALENT_UI_HANDOVER.md) | `04.Systems/Growth/GrowthManager`, `TalentController`, `TalentRules`, `CombatStatCalculator` | 실제 39노드·선행·9포인트·revision·무기 교체 |
| 장비·경제 | [시스템 계약](../unity/BARRIER_INVENTORY_SHOP_DESIGN.md) | `EquipmentGenerator`, `InventoryController`, `CurrencyManager`, `ShopTransactionService`, `EquipmentTransferService` | 서버 트랜잭션과 중복/동시 요청 |
| 상호작용·드롭 | [획득과 UI](../unity/UI_LOOT_RESOURCE_REQUIREMENTS.md) | `InteractionController`, `DroppedItem`, `DropDirector` | 거리·소유권·가방 여유·중복 획득 |
| 점수·리더보드 | [게임 규칙](../02-game-design.md) | `04.Systems/Results/RunStatistics`, `RunLeaderboardStore` | 1회 정산·호스트 로컬 저장·트라이 제외 |
| 결계·런 시간 | [결계](../unity/BARRIER_INVENTORY_SHOP_DESIGN.md) | `BarrierController`, `RunTimelineBuilder`, `RunManager` | 서버 시각·축소·정지·전환 |
| 보스 | [패턴 계약](../unity/BOSS_PATTERN_CONTRACT.md) | `03.AI/Boss/BossPatternRunner`, `BossAttackExecution`, `BossStatus`, `Phase1State`, `Phase2State` | SO와 실행 템플릿·취소·패링/정밀/지원 반응 |
| 봇 | [규칙형 BT](../unity/PLAYER_BT_AND_BOT_DESIGN.md) | `PlayerBrain`, `Resources/BT/PlayerBehavior.asset` | 사람 분기·서버 봇 판단·빈자리 보충 |
| HUD·관리 창 | [HUD](../unity/HUD_DESIGN.md) | `WeaponAbilityHUD`, `FunctionalManagementPanel`, `RunResultPanel` | 소유 플레이어 바인딩·입력 게이트·서버 결과 |
| 애니메이션·VFX | [애니메이션](../unity/ANIMATION_PIPELINE_DESIGN.md) | `PlayerAnimationController`, `PlayerCombatPresentation`, `PlayerActionCue` | 수락된 이벤트와 표현, 원격 재생 |
| 소리·환경 | [사운드](../unity/SOUND_DESIGN.md), [날씨](../unity/WEATHER_MAKER_GUIDE.md) | `00.Common/Sound/`, `WeatherRainScheduleScript` | 서버 원본→클라이언트 재생·에셋 배선 |
| 서버/클라이언트 경계 | [협업](../unity/SERVER_CLIENT_WORKFLOW.md), [서버 API](../unity/SERVER_API_REFERENCE.md), [클라 API](../unity/CLIENT_API_REFERENCE.md) | 도메인 `Net/`와 중앙 `98.Servers/` partial | 원본 상태는 서버, UI는 요청/조회 |
| 배포 | [패치](../unity/PATCH_SYSTEM_DESIGN.md) | `launcher/scripts/publish-build.ps1` | 서명·해시·releaseId와 API 허용 빌드 |

## 확인 기준

- 코드 존재만으로 기능/시각/네트워크 검증이 끝난 것은 아니다. [테스트 절차](04_MULTIPLAYER_TEST_PROTOCOL.md)에 실제 경로·빌드·조건을 기록한다.
- 역할과 폴더 소유자는 실제 `.github/CODEOWNERS` 및 [협업 규칙](../../CONTRIBUTING.md)을 확인한다.
- 밸런스 값은 `Assets/09.Data/` 원본에서 읽는다. 특성 enum 전체가 활성 노드 목록은 아니다.
- 공유되지 않는 개인 로그 대신 재현 가능한 절차와 코드 근거를 쓴다. [문서 분류](../DOCUMENTATION_POLICY.md)를 따른다.
- 실행 환경, 배포 버전, 테스트 수, 개인 계정 상태는 영구 상수로 취급하지 않는다.
