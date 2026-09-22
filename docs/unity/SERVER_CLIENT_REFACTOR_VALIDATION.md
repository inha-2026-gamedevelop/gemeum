# 서버 partial 분리 검증

현재 판정 구현은 `Assets/01.Scripts/98.Servers/`의 서버 partial과 원래 기능 폴더의 클라이언트·공용 선언으로 나뉜다. 파일 개수는 구현 추가에 따라 달라지므로 계약으로 고정하지 않는다.

## 변경 시 확인

1. 원래 클래스와 partial의 namespace·이름·조건부 컴파일이 일치하는지 확인한다.
2. `Net/` 요청 창구의 호출자·소유권 검증과 서버 판정 경계가 유지되는지 확인한다.
3. 이동한 스크립트의 `.meta` GUID를 보존하고 씬·프리팹 참조가 끊기지 않았는지 검사한다.
4. 관련 EditMode 테스트와 빌드를 수행한다.
5. 입력→Command→판정→동기화→UI를 호스트와 원격에서 확인하고, 네트워크 변경은 서로 다른 PC 3인 L3를 수행한다.

[협업 규칙](SERVER_CLIENT_WORKFLOW.md), [서버 API](SERVER_API_REFERENCE.md), [검증 프로토콜](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md)을 따른다. 이전 작업의 job ID·로컬 로그 경로는 새 팀원의 검증 근거가 아니다. 결과 보고에는 실제 실행한 빌드와 재현 절차·관찰 결과·미검증 범위를 함께 적는다.
