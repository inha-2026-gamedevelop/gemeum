# 서버 참가자 복구 보완 — 2026-09-12

씬 전환이나 일반 재접속에서 한 참가자의 복구가 실패하면 원본 상태를 보존하고 해당 원격 연결만 분리한다. 같은 인증 신원으로 돌아와 복구를 마칠 때까지 런을 정지한다.

최신 후속 결과: 말단 종료의 연결 번호 재사용 결함을 추가 수정했다. 전체 EditMode 461 통과·0 실패·기존 1 제외, `RecoveryTerminal` 개발/릴리스 빌드 오류 0·경고 각각 14. empty-carrier 복구·재접속 M2 10/10과 캡처 실패·대기 포기를 확인했다. 아래의 과거 빌드 결과와 구분한다.

커밋 코드는 `RecoveryTerminal` 빌드 이후의 코딩 컨벤션 수정(복합 조건 괄호, 중복 null 검사 제거, 반복 밖 인스턴스 조회)을 더 포함한다. 동작을 바꾸지 않는 수정이며 전체 EditMode 462개 중 461 통과·실패 0·기존 1 제외를 job `b494634261bf4fb98ac1733687974c3f`로 다시 확인했다. M2 기록은 모두 이 수정 이전 빌드 기준이다.

## 구현

- `RunParticipantRegistry`: 소비하지 않는 `TryPeek`, 성공한 원본만 제거하는 `ServerCompleteRestore` 추가. 이전 완료 요청이 새 snapshot을 제거하지 못한다.
- `RunSceneDirector`: 정상 플레이어 복구 결과를 기억하고 실패 원격만 재접속시킨다. 전원 복구·바인딩이 끝나야 snapshot을 소비하고 정지를 해제한다. 바인딩 사이 연결이나 플레이어가 바뀌면 새 대상으로 복구한다.
- `LobbyRoster`·`LobbyController`: 일반 재접속 실패를 초기 상태로 허용하지 않는다. 실패한 플레이어의 임시 상태가 원본을 덮어쓰는 것을 막고, 씬 전환 snapshot은 Director가 복구하도록 단일 소유를 유지한다. 새 런에서는 빈자리와 복구 실패 상태를 정리한다.
- `RunManager`: 복구 대기 중에도 기존 호스트 전용 대기 포기를 통해 Result로 나간다.
- 서버 종료와 Director 비활성화에서 코루틴·전환 상태를 정리한다.

호스트 자체의 복구 실패, 캡처 불가, 필수 snapshot 소실은 명시적인 전환 실패로 남긴다. 상태를 임의 생성하거나 인원을 줄여 성공 처리하지 않는다.

### 구현 검토에서 고친 것

- `LobbyRoster.ServerRestoreVacatedSeat`의 `false`는 **복구에 실패해 연결을 끊었을 때만**을 뜻한다. 되돌릴 자리가 없는 신규 접속과 신원 미상(`PlatformId == 0`)은 `true`다. 이 구분이 없으면 인증 신원이 기록되지 않은 접속에서 `LobbyController`가 일찍 반환해 자동 준비와 로비 씬 진입이 일어나지 않는다.
- 도착·거절 상태를 연결 번호가 아닌 연결 객체로 추적한다. 번호가 재사용돼도 다른 연결이 이전 상태를 물려받지 않는다. 자세한 내용은 아래 "연결 번호 재사용 보완과 재검증"에 있다.
- `RunSceneDirector.OnDisable`은 `NetworkServer.active`일 때만 전환 실패를 남긴다. 서버가 내려간 뒤의 정상 종료를 실패로 기록하지 않는다.
- 복구 대기 중에는 폴링마다 전환 상태를 `Restoring`으로 덮어쓰지 않는다. 대기 상태가 그대로 보인다.

## 검증

| 대상 | 관찰 결과 |
|---|---|
| 첫 검토 시점 EditMode | 449개 중 448 통과, 실패 0, 기존 NavMesh 런타임 전용 검사 1 제외. job `01fb5dec0157483aa68ca5e97534fafc`. 이후 결과는 아래 후속 절을 따른다 |
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

## 연결 번호 재사용 보완과 재검증 (2026-09-12 후속)

`03f21a8` 이후 추가 검토에서 timeout의 정상 참가자 오분리와 같은 연결 번호로 재접속한 참가자의 복구 차단을 회귀 테스트로 재현했다. 도착과 거절 상태를 연결 객체로 추적하고, timeout에서는 인증 신원으로 다시 찾은 현재 미도착 연결만 분리하도록 보완했다. 이전 연결의 늦은 도착 보고도 새 연결에 적용하지 않는다.

- 전체 EditMode: 456개 중 455 통과, 실패 0, 기존 NavMesh 검사 1 제외. job `98ca1668f0f74b839600313e28fcae47`.
- 최신 개발 빌드로 원격 복구 실패·재접속 10/10, 실제 30초 씬 도착 timeout·재접속 10/10 확인. 별도 추가 검사에서 HP 73/73/57과 보스 시계 6.43485355초, 독립 BossCinematic 정지 보존을 확인했다.
- 도착 timeout은 실제 원격 프로세스의 Ready/AddPlayer/Command 메시지만 테스트용으로 소비하고 적용을 보류했다. KCP 연결은 유지했으며 30초 후 Director가 분리했다. 원래 메시지 처리기는 자동 복원했다. 실제 네트워크 손실 시험과는 다르다.
- 일반 재접속 실패의 원본 보존, 실제 호스트 대기 포기 Command, 같은 매니저의 StopHost → StartHost 후 새 3인 런도 재확인했다.
- 개발 빌드 `build-a43dc8bf6c` 성공, 40.659초, 오류 1·경고 34. 오류는 기존 WeatherMaker 셰이더이며 경고에는 기존 API 폐기 경고·폰트 importer·미컴파일 후처리 경고가 포함됐다. 산출물 DLL에서 새 연결 객체 추적 필드가 컴파일됐음을 별도로 확인했다.

후속 증거는 `.claude/docs/2026-09-12/recheck-*`, 로그는 `Builds/RecoveryRecheck/Development/logs/`에 있다. 이전 증거 파일은 덮어쓰지 않았다. 배포 파일 372개의 전체 SHA256 manifest를 남겼다.

## 서버 종료 정리 보완

최종 개발 빌드는 `Builds/RecoveryRecheck/FinalDevelopment/gemeum.exe`(오류 0·경고 14), 최종 릴리스는 `Builds/RecoveryRecheck/FinalRelease/gemeum.exe`(job `build-99b723a69e`, 19.432초, 오류 0·경고 13)다. 기존 셰이더 결함을 수정한 작업은 아니며 빌드 오류 0을 그 결함의 해결 근거로 사용하지 않는다.

종료 보완 후 `FinalDevelopment`(job `build-4b10d3ddfe`, 오류 0·경고 14)의 M2에서 일반 이탈 snapshot 보존·재접속과 서버 종료 직후 registry=0을 확인했다. 같은 매니저 재시작 후 3인 새 런과 두 번째 종료도 통과했다. 증거는 `recheck-final-shutdown.json`, 최종 전체 검사 결과는 `recheck-final-editmode.json`이다. 증거 파일은 로컬 인수인계 폴더에 있다.

최종 종료 검사에서는 서버 비활성 후 snapshot 3개 잔존을 추가로 발견했다. Mirror가 먼저 실행한 이탈 콜백의 저장분을 `RunManager.OnStopServer`에서 비우고, `LobbyRoster.OnStopServer`에서 빈자리·실패 정보를 정리하도록 보완했다. 보완 후 전체 EditMode는 457개 중 456 통과·실패 0·기존 1 제외(job `91df6ef0648f4ce3bafe5da5b5490ac5`). 앞의 복구/timeout 10회 반복은 이 종료 보완 이전 빌드이며, 최종 종료·재시작 검증은 별도 `FinalDevelopment` 빌드와 기록으로 구분한다.

- 앞 절의 최초 M2 기록은 구현 검토 수정 이전이며, 현재 작업 트리의 검증은 후속 `recheck-*` 기록을 사용한다.
- 최신 변경의 Steam 3PC, 지연·손실 환경, 실제 패링·구조 입력은 미검증이다.
- 보스 트라이(U35)는 구현 로드맵의 P2·핵심 루프 통과 후 항목으로 남는다. 이번 필수 복구 보완과 별도다.
- 연결 번호 재사용 자체는 결정적 EditMode fixture로 확인했다. 실제 트랜스포트가 같은 번호를 발급하는 상황을 강제한 M2는 아니다.

## 말단 종료와 empty-carrier 추가 검사

`ServerArmTerminalExit`에 남아 있던 번호 기반 분리 때문에 실패 연결의 번호를 재사용한 새 연결까지 종료되는 것을 재현했다. 실패 당시 연결 객체가 현재 연결과 같은 경우만 종료하도록 바꾸고, 캡처 실패도 같은 연결 목록에 기록한다. 정상 참가자나 같은 신원의 재접속 연결은 과거 실패 때문에 종료되지 않는다.

- 수정 전 신규 5개 중 연결 교체 4개 실패, 현재 실패 연결 종료 1개 통과. 수정 후 전체 **462개 중 461 통과·실패 0·기존 NavMesh 1 제외**, job `1103ec0d47984fd598b77aabdfd02c2b`.
- 최신 개발 빌드 `build-07b7a9340c`: `Builds/RecoveryTerminal/Development/gemeum.exe`, 22.825초, 오류 0·경고 14. 릴리스 `build-4c8f9fee9e`: `Builds/RecoveryTerminal/Release/gemeum.exe`, 20.249초, 오류 0·경고 14. 이전 `RecoveryRecheck/Final*`는 이번 수정을 포함하지 않는다.
- 캡처 전에 원격 carrier 목록을 비운 검사 1회: Field에서 Failed·3연결, 성공한 다른 참가자의 snapshot 2개 유지. 원래 목록 자동 복원 후 공개 대기 포기를 호출하자 실패 원격만 종료, 정상 원격은 Result → Lobby 이동. 다시 3인이 모인 새 RunId=2에서 snapshot 0.
- 정상 캡처 후 새 복구 객체의 carrier 목록만 비운 검사 **10/10**: 실패 원격만 분리, WaitingForParticipant·snapshot 3개 유지. 저장 상태 수정 없이 같은 신원 재접속 후 HP 100/100/57·3연결·snapshot 0. 필드 시계 10.8806171초와 독립 BossCinematic 정지 유지. 원격 로그에서도 HP 57 복제를 확인했다.
- 별도 11번째 복구 실패에서 호스트 대기 포기 수락, 정상 원격 Boss → Result → Lobby, 로비 snapshot 0과 StopHost 후 snapshot 0 확인.

증거는 로컬 `.claude/docs/2026-09-12/server-terminal.md`, `terminal-*`, `empty-carrier-*`, 원격 로그는 `Builds/RecoveryTerminal/Development/logs/`에 있다. 배포 전체 372개 파일 manifest를 남겼다. 코드·씬 저장 없는 검사 주입은 정리했고 실행한 원격 프로세스와 Play를 종료했다.

이번 M2는 Editor 호스트와 headless 원격 2개를 사용했다. 연결 번호 재사용 강제는 EditMode 증거이며 실제 트랜스포트 재사용 M2라고 주장하지 않는다. 이전 NaN/timeout 10회씩을 이 빌드로 다시 실행한 것은 아니다.

## 남은 범위

- 최신 변경의 Steam 3PC, 실제 지연·손실, 패링·구조 입력과 화면 확인.
- U35 보스 트라이는 P2·핵심 루프 통과 후 착수 조건 유지.
- 기존 WeatherMaker 셰이더와 빌드 경고의 별도 진단·화면 검증. 이번 빌드 오류 0은 셰이더 수정 완료 증거가 아니다.
