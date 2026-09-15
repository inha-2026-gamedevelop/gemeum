# 서버·클라이언트 분리 실행 기록

2026-09-15. 기준: [협업 경계](SERVER_CLIENT_WORKFLOW.md), [클라이언트 API](CLIENT_API_REFERENCE.md).

## 중앙 서버 폴더 이름 변경 (2026-09-15)

`Assets/01.Scripts/Servers/`를 `Assets/01.Scripts/98.Servers/`로 바꿨다(폴더 `.meta` 함께 이동, `.server.cs` 20개와 각 `.meta` GUID 유지). 아래 기록의 `Servers/`는 이 변경 전 이름이다.

## 판정·AI 22개 도메인 폴더 복귀 (2026-09-15)

팀장 결정으로 실행 위치와 작성 담당을 분리했다. 보스 페이즈 상태 머신·패턴 선택·잡귀 AI·스폰/타깃 규칙과 경제·드랍·성장·상호작용·결계 규칙 22개를 `Servers/`에서 원래 도메인 폴더로 `.meta`와 함께 되돌렸다. 작성은 원래 파트 담당, 실행은 계속 서버 경로에서만 한다. `Servers/`에는 `.server.cs` partial 20개만 남는다.

| 검증 | 결과 |
|---|---|
| 파일 복귀 | 22개 `.cs`·`.meta` 모두 `git diff HEAD` 차이 없음(내용·GUID가 이동 전과 동일) |
| C# 컴파일 | `Assembly-CSharp.csproj` 구성으로 Roslyn 직접 컴파일. 소스 596개, **오류 0**. Mirror weaver 미포함 |
| 미실행 | Unity EditMode·weaver 포함 컴파일·빌드·M2/M3(에디터가 열려 있음) |

## Servers 하위 폴더명 정렬 · Net 클라 코드 원본 합류 (2026-09-15)

`Servers/AI`·`Servers/Systems`를 기존 도메인 폴더명과 같은 `Servers/03.AI`·`Servers/04.Systems`로 바꿨다(폴더 `.meta` 함께 이동, 스크립트 42개 경로만 변경).
`Net/`에는 SyncVar·동기화 컬렉션 선언과 Command만 남기고, SyncVar hook·`[ClientRpc]`·`[TargetRpc]`·클라 수명 콜백은 원본 파일로 합쳤다. 클라 수명 콜백만 있던 `RunResultRecorder.network.cs`는 `.meta`·빈 `Net/` 폴더와 함께 없앴다. Net 파일은 17개가 남는다.

| 검증 | 결과 |
|---|---|
| 멤버 보존 | Net 파일이 있던 18개 클래스의 원본·Net·Servers 합을 HEAD와 대조. 15개 동일, 3개(EnemyController·DroppedItem·InteractionController)는 앞 단계의 의도된 기능 변경만 차이. 합류 작업 자체는 클래스마다 편집 전 스냅샷과 동일 |
| Net 잔존 | Net 파일에 `[ClientRpc]`·`[TargetRpc]`·클라 수명 override·hook 메서드 정의 0 |
| meta | 짝 없는 `.meta` 0, `.meta` 없는 스크립트·폴더 0 |
| C# 컴파일 | 에디터가 열려 있어 Unity 배치 대신 `Assembly-CSharp.csproj` 구성으로 Roslyn 직접 컴파일. 소스 596개, **오류 0**. Mirror weaver는 포함하지 않음 |
| 미실행 | Unity EditMode·weaver 포함 컴파일·빌드·M2/M3. 코드 이동만 했으나 네트워크 코드라 L3 대상 |

## 중앙 Servers 폴더로 이동한 뒤 재검증 (2026-09-15)

서버 partial 20개와 전용 AI·규칙 22개를 `Assets/01.Scripts/Servers/`로 이동했다. 아래는 이 이동 이후 새로 실행한 결과다. 그 뒤의 본문은 앞선 API 분리 단계 기록이다.

| 검증 | 결과 |
|---|---|
| 이동 무결성 | 42개 `.cs`와 42개 `.meta`의 SHA-256이 이동 전후 동일. 메타 누락·중복 스크립트 GUID 없음 |
| Unity C# 컴파일·EditMode | C# 컴파일 오류 없음. **472개 중 471 통과·0 실패·기존 NavMesh 1 제외**. job `4b2b0d734cef40c8971c66333501b1a0` |
| Windows 개발 빌드 | **성공**, 29.98초. job `build-23b630f38a`, `Builds/ServerClientRefactor/gemeum.exe`. 빌드 보고서에는 기존 WeatherMaker 물 tessellation 셰이더 오류 1·경고 32가 남음 |
| M2 동일 최신 빌드 3개 | KCP 17917, 신원 91601/91602/91603. Boot→Lobby→Field, 세 플레이어가 RunId=1·Farming·HP100에 진입. 각각 0·자기 ID 요청 거절, 다른 플레이어·미존재 ID 요청 전송 뒤 인벤토리 revision·내용과 HP 보존 |
| 문서 | 신규 파일을 포함하고 삭제 경로를 제외한 제안 파일 집합으로 61개 문서·665개 링크 통과. 아직 stage·commit하지 않았으므로 기본 추적 파일 검사와 구별함 |
| 편집 환경 | 씬·프리팹·빌드 설정 변경 없음. 테스트 프로세스 3개 종료, 원래 편집 씬 1개·dirty=false·재생 정지 |

M2는 요청 경계와 접속의 제한된 확인이다. 요청 전송 성공은 서버 승인 성공을 뜻하지 않는다. 정상 아이템 줍기·G키·전체 보스/결계 전투·시각 결과·이번 이동 뒤 재접속·M3는 이 재검증에 포함하지 않았다. KCP 로그에는 Steam 미초기화에 따른 기존 relay 오류가 있다. WeatherMaker 오류는 `WeatherMakerTesselationShaderInclude.cginc(55)`의 `UnityTessellationFactors` 식별자 문제이며 외부 원본은 수정하지 않았다.

로컬 증거: `.claude/docs/2026-09-15/servers-move-manifest.json`, `servers-integrity.json`, `servers-tests-result.json`, `servers-build-result.json`, `servers-build-errors.json`, `servers-doc-check.json`, `servers-editor-final.json`, `servers-m2/`.

## 구현

- 기존 보스·시스템 16개 서버 partial 분리를 보존하고, EnemyController·EnemyStatus·CurseProjectile·SpawnManager 4개를 추가 분리했다.
- 추가 분리한 네 클래스의 서버 메서드·비직렬화 필드 130개를 이동했다. 원본 컴포넌트와 직렬화 필드·GUID는 유지한다.
- `IEnemyPresentationSource`로 현재 상태·공격 변형·패링 강조 상태를 조회한다. 애니메이션·강조 표현은 구독 직후 현재값을 적용한다.
- `IInteractionRequestSource`와 `Client/InteractionClient`로 요청 전송과 후보 탐색·입력을 분리했다. 소유자·입력 허용 여부는 요청 창구에서도 확인한다.
- DroppedItem의 원격 후보 조회가 `[Server] ServerEvaluate`를 호출하던 경로를 수정했다. 클라이언트는 복제된 장비의 존재만 후보 표시로 사용하고 실제 판정은 기존 서버 검증을 유지한다.
- 개발 검증 큐에 `interaction_request`와 상호작용 안내 snapshot을 추가했다. 기존 개발 빌드 전용 조건·명시적 큐 경로 조건을 유지한다.

## 정적 대조

서버 partial 대상 20개 클래스의 원본·Net·Server 멤버를 HEAD와 Roslyn 토큰 단위로 대조했다. 서버 이동 자체의 누락·본문 변경은 없었다. 의도적으로 추가한 몬스터 조회 프로퍼티 3개와 이후 보완한 DroppedItem 후보 조회는 기능 변경으로 별도 검증한다.
partial은 파일 정리 수단이며 접근 제한·클라이언트 빌드 제외 수단이 아니다.

## 실행 검증

| 검증 | 결과 | 범위 |
|---|---|---|
| Unity EditMode | 472개 중 **471 통과·0 실패·기존 1 제외** | 새 표현 복원 3개·원격 줍기 후보 3개 포함. 제외는 기존 실제 NavMesh 이동 테스트 |
| 최신 Windows 개발 빌드 | **성공, 오류 0·경고 12**, 13.71초 | `Builds/ServerClientRefactor/gemeum.exe`; job `build-4bbb321891` |
| M2 동일 빌드 3개 | **대상 경로 조건부 통과** | KCP 17916, 신원 91501/91502/91503, Boot→Lobby→Field, RunId=1·Farming·HP100·무기 3종 일치 |
| 요청 경계 | 3인 × 4종 요청 관찰 | 0·자기 자신은 로컬 전송 거절. 다른 플레이어·없는 대상은 전송 후 HP·인벤토리 변경 없음 |
| 재접속 | 1회 통과 | 클라이언트3 netId 21→23, 같은 RunId=1·HP100·인벤토리 유지, 새 자기 ID 요청 거절 |
| 자산 참조 | GUID 20개 보존·신규 meta 누락 없음 | 원본 컴포넌트 GUID 대조. 씬·프리팹 파일 변경 없음 |
| 소유·문서 | C# 131개와 meta 소유 검사, 문서 61개·링크 658개 통과 | 신규 파일을 포함한 제안 파일 집합. 아직 stage/commit하지 않음 |

M2는 개발용 검증 큐에서 실제 공개 `RequestInteract`를 호출했다. 반환값 true는 전송 여부이며 서버 승인 증거로 사용하지 않았다. 거절 대상 요청 후 상태 보존을 관찰했으며 **유효한 드랍의 줍기 성공·실제 G키 입력까지 통과한 것으로 해석하지 않는다.** 재접속은 이전 연결의 KCP timeout을 확인한 뒤 실행했다. timeout 전에 재시도한 첫 연결은 정원 때문에 들어오지 못했으므로 통과 횟수에 포함하지 않는다.

초기 에디터/개발 씬 조합은 클라이언트 시작 씬 및 영속 매니저 배선이 달라 씬 객체를 찾지 못했다. 로비 전환을 직접 호출한 구성도 실패했으므로 해당 실행은 통합 통과 근거에서 제외했다. 최종 근거는 동일 최신 빌드 세 개의 실제 Boot 시작 경로다. 테스트 후 실행한 프로세스를 종료했고 에디터는 원래의 씬 1개·정지·dirty=false 상태로 복원했다.

중간 빌드는 성공했지만 기존 WeatherMaker 물 셰이더의 `UnityTessellationFactors` 오류를 함께 보고했다. 최종 증분 빌드는 오류 0이었으며, 외부 셰이더 자체를 수정·검증한 것은 아니다. KCP 검사 중 Steam 미초기화 로그도 별도로 남았다.

로컬 증거는 `.claude/docs/2026-09-15/`의 `tests-final-result.json`(job `5a129f63eb384e29a11020acf8d2ec94`), `build-final-result.json`, `server-member-audit.json`, `meta-validation.json`과 `m2/native-*.json`, `m2/native-*.log`에 있다. 이 경로는 gitignore 대상이며 팀 공유용 결과는 이 문서다.

## 남은 범위

유효한 드랍의 줍기 성공과 동시 줍기, 몬스터 표현의 실제 화면 검수, 전체 보스·결계 회귀, 서로 다른 PC의 M3·실제 지연·손실·사용자 키 입력, GitHub 보호 규칙 적용은 별도로 확인한다. 현재 상태는 **구현됨·대상 M2 경로 조건부 통과**다. 커밋·푸시는 수행하지 않았다.
