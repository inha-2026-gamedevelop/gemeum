# 자체 계정 런처

Python API에 로그인·계정 관리·파티 준비·Unity 리슨 서버 접속을 연결했다. `--account-game` 모드는 Steam 없이 동작한다. AWS API·Discord 봇·암호화 릴레이가 운영되며 사용자 포트포워딩은 필요 없다. 실행은 `backend/scripts/start-aws-launcher.ps1`, 개발 설정은 [로컬 플레이 안내](../backend/README.md), 연결 구조는 [릴레이 안내](../docs/unity/ACCOUNT_RELAY.md)를 따른다.

## 일반 사용자 가입

런처 상단 **공식 Discord** 또는 로그인 창의 **공식 Discord 열기**를 누르면 [공식 서버 초대](https://discord.gg/8tzFAqQNvc)가 기본 브라우저에서 열린다. 로그인 여부나 계정 서버 연결 상태와 관계없이 버튼을 사용할 수 있다.

서버 참여 후 `/계정` 명령을 실행하고, 본인에게만 표시되는 **계정 설정 열기** 링크에서 아이디·비밀번호·닉네임을 설정한다. 링크는 5분·1회용이며, 가입 후 런처로 돌아와 로그인한다. 비밀번호는 Discord 채팅에 입력하지 않는다. 아래 CLI 계정 생성은 개발용 절차다.

## 로컬 개발 실행

Windows에서 .NET SDK 8 이상과 [백엔드 실행 환경](../backend/README.md)이 필요하다. 현재 런처 대상은 .NET 8이며 .NET 10 전환은 후속이다.

```powershell
cd backend
.\scripts\start-local.ps1 -Background
uv run geumeum-admin create-user myaccount "테스트유저"
```

계정 생성 시 콘솔에서 비밀번호를 숨김 입력한다. 이미 만든 계정이 있으면 생성 단계를 생략한다. 새 PowerShell에서:

```powershell
# 프로젝트 루트에서 실행
dotnet run --project launcher/Launcher.UI/Launcher.UI.csproj -c Debug -- --account-api http://127.0.0.1:8766
```

런처 우측 상단 **계정 로그인**을 누른다. 로그인 뒤 같은 버튼으로 닉네임과 비밀번호를 변경하거나 로그아웃한다. 계정 창을 닫고 게임 시작을 누르면 서버에서 로그인 상태를 다시 확인한다. 위 명령은 계정 관리만 연결하며, Steam 없는 게임 실행은 아래 `--account-game` 또는 로컬 실행 스크립트를 사용한다.

## Unity 세션 연결

로그인하지 않은 상태에서는 **게임 시작**이 비활성화된다. 설치·업데이트와 로그인·계정 관리는 런처에 유지한다. 게임 시작 후 **Boot 프리워밍 → 로비 → 방 찾기·생성 → 파티 준비 → 게임**으로 진행한다. 파티 생성·참가·무기 선택·준비·방장 전용 봇 추가는 게임 안에서 조작한다. 다른 방을 둘러봐도 현재 파티는 유지된다. 전원 준비 후 방장이 **원정 출발**을 누르면 서버가 준비된다. 준비 상태와 출발 명단은 계정 API가 검증하며, 게임 서버는 예정된 전원의 접속을 확인한 뒤 필드로 진입한다. 구현 및 배포 조건은 [인게임 로비 흐름](../docs/unity/INGAME_LOBBY_FLOW.md)을 따른다.

새 게임 소스로 만든 **개발 빌드가 런처에 설치된 경우** 다음 옵션을 추가한다. 이전 게임 빌드는 세션 수신 코드가 없으므로 사용하지 않는다.

```powershell
dotnet run --project launcher/Launcher.UI/Launcher.UI.csproj -c Debug -- --account-api http://127.0.0.1:8766 --account-game
```

배포 기본은 `launcher-config.json`의 `enableGameAccounts: true`다. `--account-api` 재정의는 Debug 전용이다. Release는 내장 `accountApiUrl`을 사용한다.

런처가 게임 전용 세션을 발급받고 Windows Named Pipe로 전달한다. 같은 Windows 사용자 제한, 상호 프로세스 ID 확인, 임의 채널 이름, 한 번의 전달과 수신 확인, 최대 120초 제한을 적용한다. 실행 인자에는 채널 이름과 런처 PID만 들어간다. 같은 사용자 권한의 악성 프로그램이나 관리자를 완전히 차단하는 보안 경계는 아니다. [Windows PID 조회 API](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid)

현재 `--account-game`은 방 지정 없이 게임을 실행하고 인게임 로비에서 방을 선택한다. Steam 없이 방장 PC의 리슨 서버에 접속하며 출발 시 방장이 호스트를 열고 참가자가 준비된 호스트에 접속한다. 서버의 challenge에 묶인 일회용 티켓을 발급받고 서버가 API에서 교환해 계정 ID·닉네임·세션 ID를 확인한다. 장기 계정 토큰은 Mirror 메시지에 넣지 않는다. 로컬 서버 준비와 설치는 [로컬 플레이 안내](../backend/README.md)를 따른다. 아래 Dev_Combat 검증은 IPC·인증 회귀 경로이며 실제 파티 전체 검증과 구분한다.

게임은 30초마다 세션 상태를 확인하고 만료 직전에 갱신한다. 갱신 요청은 재시도하지 않는다. 부모 런처 세션 폐기·비밀번호 변경·네트워크 오류로 검증에 실패하면 인게임 연결을 정리하고 런처 재로그인을 안내한다. 이전 방 지정 IPC 경로는 기존 종료 코드 12 정책을 유지한다. 정상 동작 중 부모 폐기는 다음 30초 확인과 최대 10초 HTTP 제한 내에 감지한다. 런처 종료 자체는 로그아웃이 아니므로 게임 세션은 계속 갱신할 수 있다. 게임 토큰은 메모리에만 둔다.

전달에 실패하면 런처는 자신이 실행한 게임을 종료하고 게임 세션 폐기를 시도한다. 전용 서버는 개발 신원으로 우회하는 인증 요청을 거부한다. 외부 배포용 API는 HTTPS를 사용한다. 운영 게임은 AWS UDP 릴레이를 통해 방장 리슨 서버에 연결하므로 사용자 포트포워딩이 필요 없다. 직접 KCP는 로컬 개발 경로다. 서로 다른 회선 3인 L3 결과는 실행한 빌드별로 별도 기록한다.

방 목록은 사용자가 새로고침하며 자동 목록 조회 루프는 돌리지 않는다. 참가 중 파티 heartbeat와 출발 상태 확인은 계속 동작한다.

## 설정과 저장

- `--account-api`는 Debug에서만 읽는다. 로컬 HTTP는 `127.0.0.1`, `localhost`, `[::1]`만 허용한다. 그 외 계정 서버는 HTTPS가 필요하다.
- Release는 빌드에 포함된 `launcher-config.json`의 `accountApiUrl`만 사용한다. 현재 배포 설정에는 AWS HTTPS 주소가 들어 있다. 환경 변경 시 내장 설정을 갱신해 다시 게시한다. 경로·쿼리·사용자정보가 붙은 주소는 거부한다.
- 자동 로그인을 선택하면 갱신 토큰만 `%LOCALAPPDATA%\Geumeum\Accounts`에 Windows DPAPI(CurrentUser)로 암호화한다. 비밀번호와 접근 토큰은 파일에 저장하지 않는다.
- 서버 주소별로 저장을 분리한다. 같은 Windows 사용자/계정 서버에 대해 런처 하나만 저장소를 점유한다. 중복 실행 시 다른 런처를 닫고 다시 실행한다.
- 갱신은 직렬화한다. 이전 저장 토큰을 지운 뒤 한 번만 교환하고 새 토큰을 저장한다. 응답 유실·프로세스 종료 시 다음 실행에서 다시 로그인할 수 있으며, 이전 토큰을 재전송하지 않는다.
- 서버 연결에 실패한 로그아웃은 로컬 정보를 지우고 원격 세션 종료를 확인하지 못했다고 표시한다. 비밀번호 변경 시 모든 세션이 폐기된다. 변경 응답을 잃거나 현재 비밀번호가 틀려도 로컬에서는 재로그인을 요구한다.
- 계정 서버 리디렉션은 따르지 않는다. 계정 토큰을 게임 실행 인자나 패치 로그에 넣지 않는다.

## 검증

```powershell
dotnet test launcher/Geumeum.Launcher.slnx
cd backend
uv run python scripts/smoke_local.py --launcher
```

첫 명령은 계정 단위 테스트와 기존 패치 테스트를 실행한다. 실제 API 테스트 하나는 환경이 없으면 건너뛰며 두 번째 명령이 임시 계정을 만들어 실행한다. 비밀번호는 자식 프로세스 환경으로만 전달하고 명령행/출력에는 넣지 않는다. 종료 시 해당 임시 계정과 인증 저장 파일을 정리한다.

실제 HTTP 테스트는 게임 세션의 부모 로그아웃 반영, 잘못된 비밀번호, 로그인, 닉네임 변경, 암호화된 자동 로그인 복원, 비밀번호 변경, 재로그인 및 로그아웃을 확인한다. WPF 계정 화면 캡처는 `backend/.local/launcher-check/`에 남긴다. 이는 Unity 접속/3인 플레이 검증을 포함하지 않는다.

Unity 검증은 에디터 메뉴 **그믐 → 빌드 → 계정 연결 검증 빌드** 후 다음을 실행한다. Build Settings와 씬 원본은 바꾸지 않고 `Builds/AccountVerification/`에 Dev_Combat 시작 빌드를 만든다.

```powershell
cd backend
uv run python scripts/smoke_local.py --unity
```

같은 PC의 게임 프로세스 3개에 별도 게임 세션을 전달하고 API 확인·KCP 승인 3건·실제 갱신·부모 로그아웃 후 종료를 검사한다. 폐기된 세션으로 새 게임을 시작했을 때 인증 승인이 없는지도 검사한다. 테스트 계정 하나의 세션 3개와 개발용 ID 7101~7103을 사용하므로 운영 계정 중복 접속 정책 검증은 아니다. 테스트가 띄운 프로세스만 종료하고 임시 계정을 정리한다. 로그는 `backend/.local/launcher-check/unity-*/`에 남긴다.

이 검증은 계정 연결·접속 승인 M2이며 정상 플레이 전체나 서로 다른 PC의 M3 검증을 의미하지 않는다. 예전 실행에서 발견한 오류가 현재도 남아 있다고 가정하지 말고 대상 빌드로 재현한다.
