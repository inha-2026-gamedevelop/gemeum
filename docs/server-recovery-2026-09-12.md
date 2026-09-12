# 서버 참가자 복구 보완 — 2026-09-12

씬 전환이나 일반 재접속에서 한 참가자의 복구가 실패하면 원본 상태를 보존하고 해당 원격 연결만 분리한다. 같은 인증 신원으로 돌아와 복구를 마칠 때까지 런을 정지한다.

## 구현

- `RunParticipantRegistry`: 소비하지 않는 `TryPeek`, 성공한 원본만 제거하는 `ServerCompleteRestore` 추가. 이전 완료 요청이 새 snapshot을 제거하지 못한다.
- `RunSceneDirector`: 정상 플레이어 복구 결과를 기억하고 실패 원격만 재접속시킨다. 전원 복구·바인딩이 끝나야 snapshot을 소비하고 정지를 해제한다. 바인딩 사이 연결이나 플레이어가 바뀌면 새 대상으로 복구한다.
- `LobbyRoster`·`LobbyController`: 일반 재접속 실패를 초기 상태로 허용하지 않는다. 실패한 플레이어의 임시 상태가 원본을 덮어쓰는 것을 막고, 씬 전환 snapshot은 Director가 복구하도록 단일 소유를 유지한다. 새 런에서는 빈자리와 복구 실패 상태를 정리한다.
- `RunManager`: 복구 대기 중에도 기존 호스트 전용 대기 포기를 통해 Result로 나간다.
- 서버 종료와 Director 비활성화에서 코루틴·전환 상태를 정리한다.

호스트 자체의 복구 실패, 캡처 불가, 필수 snapshot 소실은 명시적인 전환 실패로 남긴다. 상태를 임의 생성하거나 인원을 줄여 성공 처리하지 않는다.

### 구현 검토에서 고친 것

- `LobbyRoster.ServerRestoreVacatedSeat`의 `false`는 **복구에 실패해 연결을 끊었을 때만**을 뜻한다. 되돌릴 자리가 없는 신규 접속과 신원 미상(`PlatformId == 0`)은 `true`다. 이 구분이 없으면 인증 신원이 기록되지 않은 접속에서 `LobbyController`가 일찍 반환해 자동 준비와 로비 씬 진입이 일어나지 않는다.
- 도착 제한 시간 처리에서 끊을 대상을 이 전환의 참가자로 확인된 연결로 제한한다. 캡처 당시의 connectionId가 다른 사람에게 재사용된 경우 엉뚱한 연결을 끊을 수 있다.
- `RunSceneDirector.OnDisable`은 `NetworkServer.active`일 때만 전환 실패를 남긴다. 서버가 내려간 뒤의 정상 종료를 실패로 기록하지 않는다.
- 복구 대기 중에는 폴링마다 전환 상태를 `Restoring`으로 덮어쓰지 않는다. 대기 상태가 그대로 보인다.

## 검증

| 대상 | 관찰 결과 |
|---|---|
| 최종 EditMode | 449개 중 448 통과, 실패 0, 기존 NavMesh 런타임 전용 검사 1 제외. 위 검토 수정까지 반영한 job `01fb5dec0157483aa68ca5e97534fafc` |
| 로컬 M2 기본 경로 | 에디터 호스트 + 최신 개발 빌드 원격 2개, Boot → Lobby → Field → Boss_Arena 진입 |
| 복구 실패·재접속 | 10/10 통과. 캡처 뒤 결함을 주입하고 정상 snapshot 값으로 복구한 후 같은 신원의 실제 프로세스를 다시 접속시킴. 대기 시 2연결·snapshot 3개, 완료 시 3연결·snapshot 0개 |
| 정상 참가자와 시계 | 반복 2~10회에서 정상 2인 HP 48/48, 재접속 HP 57 유지. 별도 BossCinematic 정지와 보스 시계 77.74818초 유지. 첫 회에서는 추가 정지 없이 복구 후 None 확인 |
| 원격 수신 | 첫 재접속 클라이언트에서 localPlayerBound=true, netId=34, HP=57, Bow, RunId=1 확인 |
| 일반 재접속 실패 | 1회 실패 후 자리·원본 snapshot 유지, 초기 상태 접속 거절. 다시 접속해 HP 57 복구, snapshot 0개 |
| 호스트 대기 포기 | 실제 호스트 Command 수락, PlayerLeft → Result → Lobby. 원격 로그에서도 두 phase 전환 확인 |
| 동일 매니저 재시작 | NetworkManager 인스턴스 90474 유지. StopHost → StartHost 뒤 RunId=2, 3연결, Field/Farming/Idle, 정지 없음, HP 100/100/100 |

M2 원격 프로세스는 `-batchmode -nographics`로 실행했다. 사람의 입력·화면·Steam 지연 검증으로 보지 않는다. 실패 주입에 사용한 임시 콜백과 실행 상태는 종료했고 씬·프리팹을 저장하지 않았다.

개발 빌드: `Builds/RecoveryAudit/Development/gemeum.exe`, job `build-dd4f614ab2`, 성공 34.756초. 기존 WeatherMaker 물 tessellation shader 오류 1건과 경고 16건은 남아 있다.

릴리스 빌드: `Builds/RecoveryAudit/Release/gemeum.exe`, job `build-4ad9a590ed`, 성공 18.383초, 오류 0·경고 13건. 이 결과만으로 개발 빌드의 기존 물 셰이더 결함이 해결됐다고 보지는 않는다.

검증 중 최근 도입된 UnityChanSpringBone의 editor asmdef가 모든 플랫폼에 포함돼 빌드가 실패했다. `includePlatforms`를 `Editor`로 제한했다. 외부 에셋 C# 소스·런타임 어셈블리·GUID는 유지했다. 업데이트 시 이 프로젝트 설정을 재확인해야 한다.

릴리스에는 기존 `GEUMEUM_DEVTOOLS` 정의로 RunTestPanel이 포함되고 있었지만 LobbyController의 개발 API는 제외돼 컴파일이 실패했다. LobbyController도 같은 정의를 따르도록 조건을 맞췄다. 프로젝트 정의는 변경하지 않았으며, 개발 도구를 포함하지 않은 빌드는 계속 정식 런만 선택한다.

실행 증거는 로컬 `.claude/docs/2026-09-12/server-recovery-*`와 `Builds/RecoveryAudit/Development/logs/`에 보관했다. 테스트를 위해 실행한 프로세스는 모두 종료했다.

## 남은 범위

- 위 M2 실행 기록은 구현 검토 수정 **이전** 코드의 관찰값이다. 수정 이후로는 EditMode 회귀와 컴파일만 다시 확인했고 M2를 다시 돌리지 않았다.
- 최신 변경의 Steam 3PC, 지연·손실 환경, 실제 패링·구조 입력은 미검증이다.
- 보스 트라이(U35)는 구현 로드맵의 P2·핵심 루프 통과 후 항목으로 남는다. 이번 필수 복구 보완과 별도다.
- 도착 제한 시간 초과와 호스트 복구 실패는 EditMode 회귀로 확인했으며, 실제 프로세스의 제한 시간 초과 주입은 이번 M2 범위에 포함하지 않았다.
