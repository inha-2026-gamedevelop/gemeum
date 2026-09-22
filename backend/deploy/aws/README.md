# AWS 계정 API·Discord 봇·릴레이 배포

2026-09-22 코드 기준 공유 운영 절차. 개인 계정 잔액·자원 ID·배포 이력·인증 파일은 이 문서의 의존 자료가 아니다.

## 구성

Ubuntu에서 FastAPI, Discord 봇, PostgreSQL, Caddy와 UDP 릴레이를 운영한다. Unity 게임 서버는 AWS에 띄우지 않고 방장 PC가 리슨 호스트가 된다. game-worker 서비스는 현재 리슨 운영에 사용하지 않는다.

- HTTPS API 주소는 런처 `launcher-config.json`의 accountApiUrl과 운영 service.env를 대조한다.
- 공개 포트는 HTTP/HTTPS 및 설정된 릴레이 UDP 포트(현재 기본18001). DB/API 내부 수신은 loopback, SSH는 운영자 주소로 제한한다.
- service.env와 discord.env는 `/etc/geumeum/`에서 root 소유600으로 보관한다. 예제 파일만 저장소에 둔다.

## 설치·갱신

1. `bootstrap-ubuntu.sh`와 `install_fresh_service.py`, 서비스·환경 예제를 읽는다. 최초 설치 도구를 기존 DB 갱신 도구로 무조건 재사용하지 않는다.
2. 서버에 `/opt/geumeum/backend`와 서비스 사용자·가상환경을 준비하고 lock 기준으로 의존성을 설치한다.
3. PostgreSQL과 운영 환경을 설정하고, API와 같은 환경으로 `alembic upgrade head`를 실행한다. 기존 데이터 백업 없이 DB를 초기화하지 않는다.
4. API·Discord systemd 서비스 외에 백업·인증서 갱신 timer/service도 설치 스크립트 기준으로 확인한다.
5. HTTPS는 보유 도메인 또는 공인 IP 인증서 구성에 맞춘다. Caddy 설정 검증 후 실행한다. IP 인증서 구성에서는 짧은 수명의 자동 갱신 timer를 유지한다.
6. API와 Discord 봇을 활성화한다. 같은 토큰의 로컬 봇이 중복 실행되지 않도록 한다.
7. `/health/ready`, Discord 계정 설정, 런처 로그인, 호스트+참가자2명 출발·종료를 검증한다. 원시 토큰·비밀번호를 로그에 남기지 않는다.

## 게임 게시와 허용 빌드

R2 게임 게시가 API의 `GEUMEUM_GAME_BUILD_ID`까지 자동으로 바꾸지는 않는다. 서명 채널 releaseId를 검증한 뒤 운영 API의 허용 빌드와 일치시킨다. 런처만 갱신할 때는 게임 빌드를 바꾸지 않는다.

활성 방이0인지 기존 가드로 확인하고 환경 파일을 백업한 뒤 해당 값만 변경한다. API 재시작 후 실행값·HTTPS ready·UDP 리스닝을 확인한다. API 재시작은 진행 게임을 끊으므로 활성 방이 있으면 실행하지 않는다.

## 백업·검증

서버 내 일일 PostgreSQL 백업과 보관 정책은 배포 스크립트·timer에 있다. 백업 파일 생성과 별도 DB 복구 검증을 구분한다. 서버 밖 자동 백업은 별도 구축 대상이다.

기존 AWS 경유 동일PC3프로세스 검증 기록을 외부회선 PC3대 검증으로 취급하지 않는다. 최신 게임은 실제 R2 채널과 build-identity를 다시 확인한다. 배포 성공·사용자 설치·실전 플레이는 각각 보고한다.

## 운영 범위

- API: 로그인·계정·파티 준비·접속 티켓·호스트 임대. 방장 게임 세션이 폐기되면 호스트 권한도 폐기한다.
- 릴레이 활성화 시 방 목록에 AWS IPv4를 게시한다. 연결별 키는 HTTPS 게임 세션에만 발급하고 AES-GCM으로 중계한다. 방장의 공인 IP를 참가자에게 게시하지 않는다.
- 릴레이는 API의 단일 프로세스에서 실행한다. Uvicorn workers를 늘리지 않는다. API 재시작 시 진행 중인 파티는 닫힌다. 별도 EC2 증설은 없지만 게임 중계의 AWS 전송량 비용이 추가된다.
- 호스트 시작 제한 180초, heartbeat 소실 40초에 방을 닫는다. 자동 호스트 이전은 없다.
- 봇은 Discord Gateway로 나가는 연결을 사용한다. 업데이트 공지는 기존 outbox를 사용하며, 배포 시 공지 등록은 별도 절차다.

근거: [AWS 보안 그룹 규칙](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/security-group-rules-reference.html), [Uvicorn 신뢰 프록시 설정](https://www.uvicorn.org/settings/).
