# 그믐 계정·파티 API

FastAPI + PostgreSQL 기반 계정·Discord 계정 설정·파티·리슨 호스트 임대·접속 티켓·공지 대기열·UDP 릴레이 서비스다. 로컬 개발과 AWS 운영을 지원한다. 게임 판정은 사용자 방장 PC가 담당한다. 아래는 로컬 실행법이며 운영은 [AWS 안내](deploy/aws/README.md)를 따른다.

## 현재 구현

- 로컬 관리 명령으로 테스트 계정 생성·활성/정지 처리
- 아이디/비밀번호 로그인, 내 정보, 닉네임 변경
- 접근 토큰·갱신 토큰, 갱신 토큰 회전 및 재사용 시 세션 폐기
- 현재 비밀번호 확인 후 변경, 현재 기기 로그아웃·전체 로그아웃
- 런처 세션보다 권한과 수명이 제한된 게임 세션 발급
- Argon2id 비밀번호 해시, 토큰 SHA-256 해시 저장, DB 기반 로그인 시도 제한
- Alembic 마이그레이션, 실제 PostgreSQL 통합 테스트, 시작/중지 스크립트

WPF 런처의 로그인·자동 로그인·닉네임/비밀번호 변경도 연결했다. [런처 실행 안내](../launcher/ACCOUNT_LOCAL.md)를 따른다.

Unity는 게임 전용 세션을 API에서 검증·갱신한다. 배포 런처는 launcher-config.json의 enableGameAccounts 설정을 사용한다.

**구현됨:** 방/일회용 접속 티켓/리슨 호스트·릴레이, 게임 계정 검증, Discord 봇과 웹 계정 설정, 업데이트 공지 대기열과 발송 모듈. 일반 가입은 Discord 일회 티켓을 사용한다. 실제 공지 발송은 별도 요청이 필요하다.

## 시작

Windows PowerShell에서 실행한다. `uv`와 Python 3.12가 필요하다.

```powershell
# 프로젝트 루트에서 실행
cd backend
.\scripts\start-local.ps1 -Background
```

처음 실행하면 의존성을 lock 파일로 설치하고 EDB 공식 PostgreSQL 18.6 Windows 바이너리를 `backend/.local/`에 내려받는다(압축 약 328 MiB). 운영체제 서비스 등록·전역 PATH 변경·방화벽 개방은 하지 않는다. 다운로드 아카이브는 저장소에 고정한 SHA-256과 대조한다. 이 해시는 최초 HTTPS 다운로드 파일의 고정값이며 별도 EDB 서명 검증을 대체하는 것은 아니다.

- API 문서: <http://127.0.0.1:8766/docs>
- 준비 상태: <http://127.0.0.1:8766/health/ready>
- PostgreSQL: `127.0.0.1:55432`
- 앱 DB: `geumeum`, 테스트 DB: `geumeum_test`
- API 로그: `.local/api.stdout.log`, `.local/api.stderr.log`
- DB 로그·데이터: `.local/postgres.log`, `.local/pgdata/`

`-Background`를 생략하면 터미널에서 실행하고 Ctrl+C로 API를 종료한다. 백그라운드로 이미 실행 중이면 다시 시작하기 전에 중지한다. 설정의 포트는 `.env`에서 변경할 수 있다. 로컬 DB 도구는 55432를 사용한다.

다른 운영체제에서는 PostgreSQL 18을 별도로 로컬에 실행하고 `.env.example`을 참고해 `.env`를 설정한 다음 `uv sync --locked`, `uv run alembic upgrade head`, `uv run geumeum-api`를 사용한다.

## 테스트 계정 만들기

```powershell
uv run geumeum-admin create-user myaccount "테스트유저"
```

비밀번호는 콘솔에서 숨김 입력으로 두 번 받는다. 8~15자를 사용하며 명령행 인자로 넘기지 않는다. 초기 기본 계정/공유 비밀번호는 만들지 않는다.

아이디는 영문 소문자·숫자·밑줄 4~24자이며 대문자를 소문자로 정규화한다. 닉네임은 중복 가능하고 NFC 정규화 후 Unicode 코드포인트 2~16개다. 리치텍스트 구분자·제어 문자는 거부한다. 복합 이모지의 화면상 문자 수와 코드포인트 수는 다를 수 있다.

Swagger에서 `POST /v1/auth/login`에 계정을 입력한다. 반환된 `access_token`을 우측 **Authorize**에 넣으면 `GET /v1/me`와 인증된 API를 실행할 수 있다. 응답에 토큰이 있으므로 화면/응답을 공유하지 않는다. 자동 토큰 저장은 하지 않는다.

## 인증 계약

| 요청 | 기능 |
|---|---|
| `POST /v1/auth/login` | 아이디·비밀번호로 런처 세션 발급 |
| `POST /v1/auth/refresh` | 갱신 토큰 교환. 이전 접근 토큰도 즉시 무효화 |
| `GET /v1/me` | ID를 문자열로 반환하는 계정 정보 |
| `PATCH /v1/me/nickname` | 런처 세션만 닉네임 변경 |
| `POST /v1/auth/password` | 현재·새 비밀번호 제출. 성공 시 모든 세션 폐기 |
| `POST /v1/auth/logout` | 현재 세션 폐기. 부모 런처 세션 로그아웃 시 자식 게임 세션도 거부 |
| `POST /v1/auth/logout-all` | 런처 세션으로 모든 기기 세션 폐기 |
| `POST /v1/auth/game-sessions` | 런처 세션으로 게임용 세션 생성 |

접근 토큰은 15분, 런처 세션은 최대 30일, 게임 세션은 최대 24시간이며 부모 세션을 넘지 않는다. 갱신으로 절대 수명을 연장하지 않는다. 갱신 토큰은 한 번만 쓸 수 있다. **응답을 잃었다고 같은 갱신 토큰을 자동 재시도하면 세션이 폐기된다.** 클라이언트는 갱신을 직렬화하고 결과를 잃으면 다시 로그인해야 한다.

비밀번호 원문과 토큰 원문을 DB/로그에 저장하지 않는다. 요청 유효성 오류에서 원본 입력값도 제외한다. 접근 토큰은 `Authorization: Bearer` 헤더로만 받는다. 로컬 API는 비밀값이 없는 접근 로그만 선택적으로 추가할 수 있으며 현재 HTTP access log는 꺼져 있다.

DB 기반 로그인 제한 기본값은 5분 동안 계정별 10회·IP별 60회다. 고정 시간 창이므로 경계에서는 연속 요청량이 늘어날 수 있다. 공개 운영용 공격 방어가 완료되었다는 의미는 아니다. 토큰·로그 테이블 보존 정리 워커, 유출 비밀번호 검사, 운영자 복구는 후속이다.

계정 정지/재활성화는 로컬에서만 수행한다. 두 동작 모두 기존 세션을 폐기한다.

```powershell
uv run geumeum-admin set-status myaccount disabled
uv run geumeum-admin set-status myaccount active
```

## 중지와 재시작

```powershell
.\scripts\stop-local.ps1            # 백그라운드 API만 중지
.\scripts\stop-local.ps1 -Database  # API와 PostgreSQL 중지
.\scripts\start-local.ps1 -Background
```

시작/중지로 계정 데이터는 지워지지 않는다. `.local/pgdata`나 `.env`를 삭제하지 않는다. 둘 다 Git에서 제외된다. 백그라운드 중지 스크립트는 기록된 PID의 경로와 자식 프로세스를 검증해 이 API만 종료한다.

## 검증

```powershell
uv run ruff check .
uv run ruff format --check .
uv run alembic check
uv run pytest -q
uv run python scripts/smoke_local.py  # API가 실행 중이어야 함
uv run python scripts/smoke_local.py --launcher  # Windows/.NET: 실제 런처 계정 흐름과 WPF 캡처
uv run python scripts/smoke_local.py --unity     # 계정 연결 검증 빌드 필요: Unity 3프로세스 연결·갱신·폐기
```

pytest는 명시된 로컬 `geumeum_test` DB만 초기화한다. 실제 계정 DB를 테스트 URL에 넣으면 중단한다. HTTP smoke는 앱 DB에 임의 비밀번호의 임시 계정을 만들고 검증 후 그 계정만 삭제한다. 사용자 계정에 영향을 주지 않는다.

## 로컬과 운영의 경계

로컬은 loopback 개발 설정을 사용한다. production은 HTTPS 공개 주소·신뢰 프록시·DB·Discord·릴레이 환경값을 검증한다. 운영 설정과 설치 절차는 [AWS 배포](deploy/aws/README.md), 접속 구조는 [팀 서비스 계약](../docs/unity/STEAM_INDEPENDENT_SERVICE_PLAN.md)을 따른다.

신규/변경 비밀번호는8~15자이고 기존 로그인/current 입력은1~128자를 유지한다. 토큰 회전·일회 티켓·방 임대는 UI에 표시만 구현해서 대체할 수 없다. 실제 다른 회선 PC3대, 장시간 부하, 서버 밖 자동 백업과 UDP 차단 회선 대체 경로는 별도 검증/구축 대상이다.
