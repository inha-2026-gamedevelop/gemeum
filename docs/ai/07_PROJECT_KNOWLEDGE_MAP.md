# 프로젝트 지식 맵

> 질문을 받았을 때 기준 문서, 코드 진입점, 검증 방법으로 빠르게 이동하기 위한 색인이다.
> 상세 규칙을 여기에 복제하지 않고 원본을 링크한다.

---

## 1. 기준 정보 계층

충돌할 때 아래 순서로 현재성을 판단한다.

1. 실행 중인 코드 · 실제 로드된 설정 · 콘솔 출력. 밸런스 값은 `Assets/09.Data/`의 해당 에셋을 먼저 확인하고, YAML에 필드가 없으면 SO 초기값과 Unity 역직렬화 결과를 구분한다
2. `CLAUDE.md`와 `CONTRIBUTING.md`
3. 기획 문서 (`docs/01~03`)
4. 인수인계 기록
5. 과거 대화

하위 계층이 상위와 다르면 조용히 섞지 말고 차이를 기록한다.

---

## 2. 질문 라우팅

| 질문 · 키워드 | 기준 문서 | 코드 진입점 | 검증 방법 | 소유자 |
|---|---|---|---|---|
| 코어 루프, 결계 단계, 시간대 | [시스템 2.1](../01-system-design.md) · [결계 계약](../unity/BARRIER_INVENTORY_SHOP_DESIGN.md) | `BarrierPhaseSO.asset` → `RunTimelineBuilder` → `RunPhaseTimeline.Validate` → `RunManager` → `BarrierController` | L1 데이터 검사 + L3 3인 시간·반경·정지 일치 | @AidenKim923 / @kjw1237 |
| 패링, 판정 창, 패링 게이지 | [패링 계약](../unity/PARRY_SYSTEM_DESIGN.md) | `ParryController` · `ParryJudge` · `ParryGauge` | L3 지연·중복 요청·공격 취소 | @AidenKim923 |
| 화살, 명중 보조, 정밀 사격 | [조준 보정](../unity/ARROW_AIM_ASSIST_DESIGN.md) · [공격 계약](../unity/PLAYER_ATTACK_DESIGN.md) | `PlayerAttack` · `ArrowProjectile` · `LockOnController`; 정밀 사격 전용 시전/카운터는 미구현 | L3 서버 명중·원격 표현·대상 소실 | @AidenKim923 |
| 락온에 무엇이 연결되는가 | [카메라·락온](../unity/CAMERA_AND_LOCKON.md) | 아래 2.1의 다섯 의존 관계 | L3 대상 선택·바라보는 방향·화살 + L2 카메라 | @AidenKim923 |
| 잡귀, 스폰, 강화 접두사 | [게임 디자인 5절](../02-game-design.md) | `SpawnManager` · `EnemySO` · `EnemyStatus`; 접두사 행동 modifier는 미구현 | L3 스폰·리셋·강화값 | @oname36-creator |
| 장비, 등급, 스탯 롤 | [결계·인벤토리·상점](../unity/BARRIER_INVENTORY_SHOP_DESIGN.md) | 현재 `EquipmentSO` · `DropTableSO` · `ICombatLoadoutProvider`; `EquipmentInstance`와 생성/인벤토리 구현은 없음 | 향후 L3 동시 획득·교환·재접속 | @kjw1237 |
| 노잣돈, 객주, 상점 | [게임 디자인 7절](../02-game-design.md) | 현재 `ShopSO`; `CurrencyManager` · `ShopKeeper`는 미구현 | 향후 L3 잔액·동시 구매·부활 차감 | @kjw1237 |
| 레벨, 특성 트리 | [게임 디자인 8절](../02-game-design.md) | `TalentTreeSO` · `LevelCurveSO` · `PlayerStatus`; 성장·특성 분배 서비스는 미구현 | 향후 L3 경험치·특성·상태 복구 | @kjw1237 |
| 보스 페이즈, 패턴 | [보스 패턴 계약](../unity/BOSS_PATTERN_CONTRACT.md) | `BossController` · `BossPhaseStateBase` · `Phase1State` · `Phase2State`; 패턴 10은 데이터만 있음 | L3 전환·취소·그로기·승패 | @oname36-creator |
| 사망, 부활, 전멸 | [게임 디자인 10절](../02-game-design.md) | `PlayerStatus` · `ReviveController` · `RunOutcomeArbiter` · `RunManager` | L3 동시 구조·전환 중 전멸 방지·재접속 | @AidenKim923 |
| 점수, 리더보드 | [게임 디자인 11절](../02-game-design.md) | `ScoreRuleSO` · `IRunResultProvider` · `RunResultProviderLocator`; 실제 정산·저장은 미구현(Null provider는 0점) | 향후 L3 1회 정산·전원 같은 결과·트라이 제외 | @kjw1237 |
| 서버냐 클라냐 | [네트워크 권위](../unity/NETWORK_AUTHORITY.md) | [서버 API](../unity/SERVER_API_REFERENCE.md) | 코드 검토 + 네트워크 변경은 L3 | @AidenKim923 |
| 씬 충돌, .meta, LFS | [씬·에셋 안전](../unity/SCENE_AND_ASSET_SAFETY.md) | `.gitattributes` · `.github/`는 실제 저장소에서 확인 | 발췌본에는 CI·meta가 없어 통과 여부 미확인 | @AidenKim923 |
| 밸런스 수치 변경 | [밸런스 데이터](../unity/BALANCE_DATA_WORKFLOW.md) | `Assets/09.Data/` · `GameDBBootstrap` · `GameDatabaseSO` | L1 데이터 + L2 반영 + 판정 관련은 L3 | CODEOWNERS상 @AidenKim923, 도메인 담당 협의 |
| 조명, 시간대 연출 | [WeatherMaker](../unity/WEATHER_MAKER_GUIDE.md) · [보스 연출](../unity/BOSS_STAGING_DESIGN.md) | `BossArenaPhaseView` · `WeatherRainScheduleScript`; `DayCycleSO` 데이터는 있으나 런 시계 연결은 확인 필요 | 표현은 로컬. 런 시간은 서버 값; 외부 WeatherMaker 네트워크 연결은 실제 프로젝트에서 검증 | @AidenKim923 |
| 브랜치, PR, 통합 | [협업 규칙](../../CONTRIBUTING.md) | `.github/CODEOWNERS` · 실제 저장소 CI | 발췌본만으로 CI 통과를 주장하지 않는다 | @AidenKim923 |
| Mirror API, SyncVar, Command | [네트워크 권위 5.5](../unity/NETWORK_AUTHORITY.md) | `GeumeumNetworkManager` · `GeumeumAuthenticator` · `SteamSessionCoordinator` | L3 KCP 및 Steam 각 경로 | @AidenKim923 |
| 3인 테스트 방법 | [멀티플레이 테스트](04_MULTIPLAYER_TEST_PROTOCOL.md) | 테스트한 빌드 식별자와 시나리오를 함께 기록 | 과거 접속 성공을 현재 전투/씬 전환 통과로 승격하지 않는다 | 3인 전원 |

### 2.1 락온의 다섯 의존 관계 (2026-09-07 제공 코드 기준)

| 연결 | 현재 경로 | 경계 |
|---|---|---|
| 화살 유도 | `LockOnController` → `PlayerAttack.TryResolveLockedAim` → `ArrowProjectile.UpdateGuidance` | 서버가 발사 시 대상 재검증·유도·명중 |
| 스트레이프 이동·모델 방향 | `LockOnController.ApplyPresentation` → `PlayerPresentation`; `CameraFrame` → `PlayerController`의 월드 입력 변환 → `PlayerPrediction`; 표현 상태 → `PlayerAnimationController` | 예측기가 CameraFrame을 직접 읽는 관계가 아니다. 로컬 입력/예측과 서버 이동·동기화 표현을 구분 |
| 카메라 붐 길이 | `LockOnController.ResolveCameraDistance` → `CameraFrame` → `CameraRig` | 로컬, 값은 `CameraConfigSO` |
| 정밀 사격 | 락온 조준은 있으나 정밀 사격의 시전 상태·보스 카운터 연결은 없음 | 향후 서버 판정, 현재 기능으로 표시하지 않음 |
| 조준점 | `AimAssistPoint` / `IAimAssistTarget` → `ResolveAimPoint` 및 `TargetAimPoint` | 프리팹의 부위 순서·배치는 별도 검증; 발사 시 서버 재검증 |

코드가 있다는 사실과 런타임 검증은 다르다. 씬·프리팹·외부 패키지가 없는 발췌본에서는 참조 배선과 L1/L2/L3를 검증할 수 없다.

---

## 3. 시스템 소유권

| 시스템 | 파트 | 씬 | 상태 소유 |
|---|---|---|---|
| 세션 · 동기화 | 네트워크 · 전투 | `Dev_Combat` | 서버 |
| 플레이어 조작 · 패링 · 화살 | 네트워크 · 전투 | `Dev_Combat` | 입력·예측은 로컬, 결과 판정은 서버 |
| 잡귀 · 보스 | 몬스터 · 보스 | `Dev_AI` | 서버 |
| 결계 | 게임플레이 시스템 | `Dev_System/` (Dev_Barrier · Dev_Lobby · Dev_Title) | 서버 |
| 장비 · 경제 · 특성 · 점수 | 게임플레이 시스템 | `Dev_System/` (Dev_Barrier · Dev_Lobby · Dev_Title) | 서버 |
| 셰이더 · VFX · 맵 · 조명 | 테크니컬 아트 | `Dev_Art` (미생성으로 문서화됨) | 클라이언트 |

---

## 4. 결정 기록

중요한 결정은 이유 · 대안 · 영향과 함께 기록한다. 결정이 바뀌면 이전 행을 지우지 말고 `대체됨`으로 표시한다.

| ID | 결정 | 이유 | 대안 | 상태 |
|---|---|---|---|---|
| D-001 | 호스트 권위 리슨 서버 | 3인 협동에 충분, 구현 단순 | 데디케이티드 (비용·복잡도) | 유효 |
| D-008 | 네트워크 라이브러리 **Mirror** | 자료·커뮤니티 풍부, 무료, Steam 트랜스포트 교체 가능 | Netcode for GameObjects, Photon | 유효 |
| D-009 | 브랜치 전략 `feature/{이름}` | 관리 단순, 3인 소규모 | 기능별 브랜치 (PR 범위는 명확하나 브랜치 난립) | 유효 |
| D-002 | 보스 1마리 확정 | 2026-11-16 마감 안에 완성도 확보 | 기존 다수 보스안 | 유효 |
| D-003 | 고정 맵 + 랜덤 스폰 | 절차적 생성 비용 회피 | 절차적 지형 | 유효 |
| D-004 | 장비는 공격력만 상승 | 생존력은 레벨업으로 분리 | 방어력·HP 옵션 포함 | 유효 |
| D-005 | 부활 지점 보정 없음 | 당시 결계 밖 체류 억제안 | 결계 안쪽으로 이동 | 대체됨 — 현재 `ReviveController.ResolveRevivePosition`은 결계 안쪽 후보와 지면/충돌을 검사하고 실패 시 사망 지점 사용 |
| D-006 | 패링 게이지를 플레이어가 보유 | 3회 패링에 보상, 사망해도 유지. 무기 교체 시 0으로 초기화 | 적 자세 게이지 방식 | 유효 |
| D-007 | 객주는 2차 결계에만 | 구매 타이밍에 긴장감 | 각 단계마다 배치 | 유효 |

---

## 5. 현재성 관리

아래 계기가 생기면 관련 항목을 다시 확인한다.

- Unity 버전 · 패키지 변경
- 씬 이름 · 폴더 구조 이동
- 네트워크 라이브러리 확정 또는 변경
- 밸런스 수치 대규모 조정
- 문서의 클래스명이 검색되지 않음
- 인수인계와 현재 diff가 불일치

갱신할 때 날짜만 바꾸지 말고 실제 코드 근거를 다시 확인한다.

---

## 6. 미확정 항목

| 항목 | 상태 | 결정 필요 시점 |
|---|---|---|
| 보스 페이즈 수와 패턴 | 2페이즈·10패턴 데이터 존재. 실행은 1~9, 꼬리·25% 게이트·10번은 미구현. 값 정본은 `BossPatternSO.asset` | 후속 구현 전 |
| 특성 노드별 구체 수치 | `TalentTreeSO.asset`의 `_nodes`가 비어 있음; 열 이름과 현행 직업 설계도 재대조 필요 | 성장/장비 연결 전 |
| 맵 레이아웃과 랜드마크 | 미정 | 프로토타입 3주차 |
| 리더보드 저장 방식 (로컬 / 서버) | 호스트 로컬 JSON은 계획. 제공 코드에 저장 서비스가 없고 출시 방식도 미정 | 결과 시스템 구현 전 |
| 1차 결계 객주 추가 여부 | 없음. 객주는 2차 안전지대에서만 운영 | 결정 완료 |
| 사운드 방향 | [사운드 설계](../unity/SOUND_DESIGN.md)에 방향 확정, `00.Common/Sound/` 재생 계층 존재. 클립·믹서·3인 청취는 별도 검증 | 아트 통합 때 |

**네트워크 라이브러리는 Mirror로 확정됐다.** 남은 미확정 중에서는 맵 레이아웃이 가장 급하다 — 잡귀 거점 배치와 결계 중심 좌표가 여기에 의존한다.
