# 런처 · 게임 패치 운영 계약

2026-09-22 저장소 기준. Windows x64 WPF 런처와 Unity 게임은 별도로 빌드·게시한다. 정적 배포는 Cloudflare R2, 계정·방 API와 Discord 봇은 AWS에서 운영한다. 게임 판정은 방장 PC의 Mirror 리슨 서버다.

## 구조

Unity 빌드 → 파일 해시·메타데이터 서명 → R2 게시 → 런처 다운로드·검증 → 새 버전 디렉터리 완성 → 실행 포인터 전환 순서다. 파일별 SHA-256을 비교하고 변경된 파일 전체를 받는다. 바이너리 내부 차분 패치는 현재 방식이 아니다.

런처의 패치 주소 `baseUrl`과 HTTPS 계정 주소 `accountApiUrl`은 서로 다른 서비스다. `launcher/Launcher.UI/launcher-config.json`의 `enableGameAccounts: true`가 현재 배포 기본이다. Steam은 별도 호환 경로다.

## 코드 위치

| 위치 | 책임 |
|---|---|
| `launcher/Launcher.UI` | WPF 화면·설정·파티·실행 |
| `launcher/Account.Client` | 계정 API·세션 |
| `launcher/Release.Packager` | 패키징·해시·서명 |
| `launcher/scripts/publish-build.ps1` | 게임 패키징·채널 게시·선택적 업로드 |
| `launcher/scripts/publish-launcher.ps1` | 런처 자체 업데이트 게시 |
| `Assets/01.Scripts/00.Common/Editor/GeumeumBuildMenu.cs` | 게임 빌드와 build identity |
| `backend/deploy/aws/` | 계정 API·봇 운영 배포 |

서명 개인키와 R2 자격증명은 로컬 비밀 저장소/환경 변수에서 읽는다. 실제 계정 ID·키·개인 PC 주소를 공유 가이드에 넣지 않는다. 공개키·배포 메타데이터는 검증에 사용한다.

## 게임 게시

프로젝트 루트 PowerShell에서 이미 만든 빌드를 게시한다. 아래 첫 명령은 로컬 저장소만 갱신한다.

```powershell
.\launcher\scripts\publish-build.ps1 -Build Builds\Development -Channel internal
```

원격 게시에는 `-Upload -Url <배포 HTTPS 주소>`와 `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET` 환경 변수가 필요하다. 실제 값을 채팅·로그·문서에 출력하지 않는다. 운영 승격은 스크립트의 `-Promote -ReleaseId ... -Channel stable`을 사용한다.

1. 빌드의 `gemeum_Data/StreamingAssets/build-identity.json`이 존재하는지 확인한다.
2. 버전·releaseId·채널 sequence·서명을 확인하고 파일 업로드 및 원격 검증을 완료한다.
3. API의 허용 게임 build ID를 같은 배포로 맞춘다. 단순히 런처 버전만 올리면 방의 빌드 불일치는 해결되지 않는다.
4. 런처에서 새로고침·업데이트 후 설치 identity와 API 허용 build ID, 출발 파티의 build ID를 비교한다.
5. 실제 로그인→파티→호스트/참가자 실행→접속을 확인한다.

현재 버전 번호는 `Launcher.UI.csproj`, 배포 메타데이터와 빌드 identity에서 조회한다. 문서에 특정 배포 번호를 영구 최신값으로 고정하지 않는다.

## 런처 게시

`Launcher.UI.csproj` 버전을 조정한 뒤 다음 스크립트를 사용한다. 런처 업데이트와 게임 업데이트는 별도다.

```powershell
.\launcher\scripts\publish-launcher.ps1 -Upload
```

스크립트의 `-ServerUrl`은 패치 `baseUrl` 재정의다. 계정 API 주소를 바꾸는 옵션으로 사용하지 않는다. Release 계정 주소는 내장 `launcher-config.json`을 따른다.

## 설치·실행·계정

다운로드는 임시 파일과 새 버전 폴더에서 검증하고 성공한 뒤 활성 버전을 바꾼다. 실패해도 기존 설치를 보존한다. 복구·재시도·롤백에서 사용자의 계정 저장과 리더보드 데이터를 삭제하지 않는다.

로그인 전에는 게임/원정대 진입을 제한하며 설치·업데이트는 가능하다. 계정 세션은 Windows Named Pipe로 게임에 전달한다. 게임 인자는 채널 이름·런처 PID이며 비밀번호와 토큰을 인자로 넣지 않는다. Steam 호환 실행은 별도 `-steam` 경로다.

방 목록은 사용자가 새로고침한다. 파티 참가 후 heartbeat·출발 상태 확인은 자동으로 유지한다. 목록 자동 갱신과 참가 중 세션 유지 루프를 혼동하지 않는다.

## 장애와 검증

- 다운로드/해시/서명 실패: 새 버전을 활성화하지 않고 원인과 재시도 경로를 표시한다.
- 게임과 서버 버전 불일치: 설치 identity, 원격 채널, API 허용 build ID를 차례로 비교한다.
- API/릴레이 재시작: 진행 방이 영향을 받으므로 게임 파일 게시와 별개로 운영 판단한다.
- 서명키 교체·권한·백업은 배포 담당자가 관리하며 팀 문서에 비밀 값을 기록하지 않는다.

단위 테스트와 실제 업데이트/실행 검증은 구분한다. [계정 런처](../../launcher/ACCOUNT_LOCAL.md), [AWS 운영 안내](../../backend/deploy/aws/README.md), [릴레이](ACCOUNT_RELAY.md)를 함께 확인한다.
