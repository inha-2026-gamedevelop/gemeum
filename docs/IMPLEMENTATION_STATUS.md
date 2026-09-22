# 현재 구현과 확인 범위

기준: 2026-09-22 저장소 작업 트리. 미커밋 변경을 포함하므로 특정 커밋만 체크아웃한 상태와 다를 수 있다. 아래는 코드·설정 대조이며 전체 게임의 새 플레이 테스트 결과가 아니다.

| 영역 | 현재 구현 | 원본 |
|---|---|---|
| 엔진 | Unity 6000.4.7f1 / URP | `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` |
| 일반 접속 | WPF 런처 자체 계정 로그인, 파티 편성·봇·준비·출발, Unity 방장 StartHost | `launcher/Launcher.UI/`, `01.Network/Identity/AccountGameServer.cs` |
| 통신 | Mirror + AccountRelayTransport, AWS 암호화 UDP 릴레이. KCP와 Steam 코드는 개발/호환 경로로 남음 | `01.Network/Identity/AccountRelayTransport.cs`, `backend/src/geumeum_backend/relay.py` |
| 계정 서비스 | FastAPI·PostgreSQL·Discord 봇. 게임 시뮬레이션은 사용자 방장 PC | `backend/src/geumeum_backend/`, `backend/deploy/aws/` |
| 배포 | R2 서명 패치·런처 자기 업데이트. 게임 releaseId와 API 허용 빌드 일치 필요 | `launcher/scripts/publish-build.ps1`, `launcher-config.json` |
| 비밀번호 | 새 설정 8~15 Unicode code point. 기존 로그인/current password 1~128 유지 | `backend/src/geumeum_backend/schemas.py` |
| 전투 | 검 반격 표식과 R 연계, 활 쐐기 일반/정밀 파열, 방울 R 조준 후 확정하는 공격 지원 진 | `02.Combat/Player/PlayerAttack` partial, `ParryGauge`, `SupportBuffController` |
| 특성 | 무기당 13/전체 39노드. 뿌리 1+3갈래, 총 9포인트, 서버 배분·무료 전체 초기화 | `09.Data/Talent/TalentTreeSO.asset`, `04.Systems/Growth/` |
| 장비·경제 | 장비 생성·인벤토리·드롭·재화·상점 거래·양도 | `04.Systems/Equipment/`, `Inventory/`, `Economy/`, `98.Servers/04.Systems/` |
| 결과 | 런 점수 집계와 호스트 로컬 리더보드 JSON | `04.Systems/Results/RunStatistics.cs`, `RunLeaderboardStore.cs` |
| 봇 | Unity Behavior 기반 규칙형 동료. 런 시작 전 빈자리 보충 | `02.Combat/Player/`의 PlayerBrain 관련 코드, `Resources/BT/PlayerBehavior.asset` |
| 학습 봇 | ML-Agents 학습·모델 운용은 제안 단계 | [학습형 봇 설계](unity/PLAYER_BT_ML_AGENTS_DESIGN.md) |

Assets 내부 경로는 `Assets/01.Scripts/` 기준이며 SO 경로는 `Assets/` 기준이다. 실제 파일 경로·클래스명을 검색해 확인한다.

## 검증 범위와 남은 일

- AWS 경유 실제 게임 호스트1+참가자2 및 봇 포함 검증 기록은 같은 PC 다중 프로세스다. 다른 회선 PC3대 실전, 장시간 부하, 고지연·패킷 손실, 모든 특성 조합은 미검증이다.
- 공개 패키지 서명/해시 검증과 사용자 설치·실전 플레이 검증은 별개다. 최신 배포 버전은 원격 서명 채널과 로컬 build-identity를 다시 확인한다.
- 자동 호스트 이전과 UDP 차단 회선의 TCP/WSS 대체 경로는 없다. API와 릴레이가 같은 프로세스이므로 API 재시작은 진행 방에 영향을 준다.
- 새 특성 UI는 담당자 인계 단계다. [인계 계약](unity/TALENT_UI_HANDOVER.md)을 기준으로 작업한다.

## 읽는 순서

팀 공통 구조는 [기술 설계](03-technical-design.md), 기능 진입점은 [프로젝트 지식 맵](ai/07_PROJECT_KNOWLEDGE_MAP.md), 실제 값은 SO, 배포 구조는 [자체 계정 서비스](unity/STEAM_INDEPENDENT_SERVICE_PLAN.md)를 따른다. 제안 문서는 명시된 현재 상태 절을 제외하면 구현 완료 증거가 아니다.
