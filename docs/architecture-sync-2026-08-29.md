# 서버·플레이어 아키텍처 문서 동기화 기록

작성일: 2026-08-29  
코드 기준: `57cb59d`  
범위: 서버 런타임, 플레이어 권위 경계, 클래스·데이터 명칭, 인수인계 현재성

> 이 문서는 변경 이력이다. 구현 계약의 원본은
> [기술 설계](03-technical-design.md), [프로젝트 구조 설계서](04-architecture.md),
> [네트워크 권위](unity/NETWORK_AUTHORITY.md)다.

## 1. 결론

문서가 설계 초안 상태에 머물러 실제 코드보다 뒤처진 부분을 현재 구현 기준으로 갱신했다.
부트 게이트·로비·런 시계·phase catch-up은 구현된 것으로, 플레이어 상태·공격·부활·
공용 시뮬레이션 스케줄러는 계획 또는 미정으로 구분했다.

코드·씬·프리팹·ScriptableObject 에셋은 변경하지 않았다.

`.claude/docs/`는 Git 추적 대상이 아니므로, 팀에 남아야 하는 계약 변경은 추적되는
`docs/03-technical-design.md`, `docs/04-architecture.md`,
`docs/unity/NETWORK_AUTHORITY.md`에도 함께 반영했다.

## 2. 확인한 현재 구현

| 영역 | `57cb59d`의 사실 |
|---|---|
| 서버 부트 | `GeumeumNetworkManager.OnStartServer`에서 `ServerBootGate` 실행. 실패 시 한 프레임 뒤 서버/호스트 종료 |
| 부트 검사 범위 | GameDB, `BarrierPhaseSO` 타임라인, Transport, player prefab과 spawn prefab의 기본 `NetworkIdentity` |
| 접속 | 최대 3명, 런 시작 후 `IsAdmissionLocked`로 신규 접속·재접속 거부 |
| 로비 | `SyncList<LobbySlot>`, sender 검증 준비 요청, 정확히 3명 준비, 서버 RNG 무기 중복 없는 배분 |
| 런 시계 | `NetworkTime.time` 기준 `ElapsedSeconds`, `RunPhase` SyncVar, 0.5초 `syncInterval` |
| phase | `RunPhaseTimeline`이 순서·연속성·40분 불변식을 검사하고 다중 경계 catch-up 처리 |
| 서버 이벤트 | phase 커밋 후 프로세스 로컬 `GameEventBus`에 `RunPhaseChangedEvent` 발행 |
| 플레이어 이동 | 매 프레임 `CmdMove`, 방향 크기·Command 도착 간 delta만 제한, 서버에서 즉시 위치 변경 |
| 패링 | 추정 서버 시각과 단일 창만 비교. 시각 clamp·window ID·상태·자원·중복 소비 검증 없음 |
| Player.prefab | `NetworkIdentity`, `NetworkTransformReliable`, `PlayerController`, `ParryController` 연결 |

## 3. 확정·정리한 아키텍처 경계

### 3.1 런 시계와 공용 시뮬레이션 틱

`RunManager.CoServerTick`은 이미 존재하지만 경과 시간과 phase만 소유한다.
이를 플레이어·AI·투사체 전체의 공용 고정 틱으로 문서화하지 않는다.

공용 권위 시뮬레이션 스케줄러는 아직 미정이다. 기존 런 시계 루프를 확장할지 별도
스케줄러를 둘지, 시스템 실행 순서와 입력·피해 catch-up 정책을 플레이어 이동 착수 전에 정한다.

### 3.2 서버 부트 게이트

현재 부트 게이트는 preflight가 아니라 post-listen이다. Mirror의 `StartHost`/`StartServer`
호출을 가로채지 못해 서버가 열린 직후 검사하고 실패하면 닫는다.
“서버를 열기 전에 차단한다”는 기존 문구를 실제 동작으로 수정했다.

필수 씬 매니저, Player 필수 전투 컴포넌트, 호스트 UI 오류 표시와 데디케이티드 preflight는
남은 보완 항목으로 분리했다.

### 3.3 플레이어 공격 진입점

클라이언트 공격 Command는 `PlayerAttack` 하나로 고정한다.
활 공격도 현재 `PlayerWeapon`에서 `BowAbility`를 선택하고, `ArrowLauncher`는
서버 전용 화살 스폰 실행기로만 사용한다. 별도의 활 Command를 만들지 않는다.

### 3.4 사망·전멸·런 종료

`PlayerStatus`가 플레이어 생명 상태의 원본이고, `ReviveController`가 관전·부활 타이머와
전멸 조건을 감지한다. 전멸 시 `RunManager.AbortRun`을 요청하며, `RunManager`만
`RunPhase.Result`를 커밋한다.

### 3.5 Starting과 무기 배분

`Starting`은 `RunPhase` enum 값이 아니라 연결 수명의 개념적 트랜잭션이다.
현재 코드는 입장 잠금·로비 슬롯 무기 배분·런 시작까지 구현되어 있지만,
`PlayerWeapon`이 없어 역할을 실제 플레이어에 적용하고 성공을 확인하는 단계가 빠져 있다.

### 3.6 데이터 명칭

문서의 오래된 `*Data` 명칭을 실제 클래스·에셋명인 `*SO`로 통일했다.
최대 HP의 단일 원본은 `LevelCurveSO`이며 `CombatConfigSO`에는 HP를 두지 않는다.

## 4. 변경한 문서

| 문서 | 변경 내용 |
|---|---|
| [기술 설계](03-technical-design.md) | 현재 서버 구현 상태, 단일 공격 Command, 전멸/Result 소유권, 공용 틱 미정, 실제 SO 명칭 |
| [프로젝트 구조 설계서](04-architecture.md) | 구현된 Network 클래스, 계획 Player/Weapon 구조, 실제 Player.prefab, RunManager 이벤트·시계 역할, 실제 에셋명 |
| [네트워크 권위](unity/NETWORK_AUTHORITY.md) | NetworkTransform 역할, 패링 request/window 검증, GameEventBus의 프로세스 로컬 경계, 데디 전환 범위 |
| [밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md) | 실제 `*SO` 클래스명과 HP 소유권 |
| [코딩 컨벤션](coding-convention.md) | 예제의 실제 `EnemySO`·`DayCycleSO` 명칭 |
| [프로젝트 지식 맵](ai/07_PROJECT_KNOWLEDGE_MAP.md) | 코드 진입점의 `*SO`·`CurrencyManager` 명칭 |
| [작업 브리프](ai/02_TASK_BRIEF_TEMPLATE.md) | 예제의 `BarrierPhaseSO` 명칭 |
| [읽기 가이드](00-reading-guide.md) | 쉽게 낡는 고정 문서 개수 표현 제거, 이 동기화 기록 연결 |
| `.claude/docs/2026-08-29/server-architecture.md` | 부트·시계·요청·연결 수명·현재 구현·미정 항목 전면 동기화 |
| `.claude/docs/2026-08-29/player-architecture-plan.md` | 공격 진입점, 상태 소유권, 전멸 종료, 공용 틱 결정 항목 정정 |
| `.claude/docs/2026-08-29/server-runtime-implementation.md` | PR #5 머지 후 상태와 다음 행동으로 인수인계 갱신 |
| `.claude/docs/HANDOFF_CURRENT.md` | 세 상세 문서의 현재 기준 커밋과 상태 포인터 갱신 |

## 5. 남은 구현·검증

| 항목 | 상태 | 필요한 증거 |
|---|---|---|
| 3명 준비 → 무기 배분 → 런 시작 | 검증 대기 | M2에서 검·활·방울 중복 없음과 전원 동일 결과 |
| 런 중 이탈 → `Result` | 검증 대기 | M2 실제 disconnect |
| PlayerWeapon 초기 역할 적용 | 미구현 | 세 플레이어 적용 성공 후에만 StartRun |
| PlayerStatus·피해·사망 | 미구현 | M2 HP·Downed 동기화 |
| ReviveController·전멸 | 미구현 | M2 부활·무적·전멸 1회 커밋 |
| 이동 sequence·예측·재조정 | 미구현 | M2 입력 유실, M3 50/150ms |
| 패링 window ID·소비 | 미구현 | M2 중복 거부, M3 지연별 판정 |
| 공용 권위 시뮬레이션 스케줄러 | 결정 필요 | 실행 순서·catch-up 단위 검증과 M3 틱 초과 측정 |
| Main.unity 통합 | 미검증 | Main 기준 L3 3인 전체 런 |

## 6. 검증 범위

- 수정·신규 문서 13개 링크 검사: 깨진 상대 링크 0개
- Markdown 코드 펜스 검사: 홀수 개로 닫히지 않은 문서 0개
- 문서에 적힌 14개 `*SO` 클래스명과 실제 `00.Common/Data` 파일 목록 일치
- 문서에 적힌 GameDB·밸런스 에셋이 `Assets/09.Data`에 모두 존재
- 플레이어 아키텍처의 구현 전 결정 목록 9개 확인
- `git diff --check` 통과
- 코드·씬·프리팹·에셋 변경 없음
- Unity 실행과 M2/M3는 문서 동기화 작업 범위에서 수행하지 않음
