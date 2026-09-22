# 기존 저장소를 새 PC에서 준비하기

이 프로젝트는 이미 초기화된 저장소다. 새 팀원은 기존 저장소를 클론해 설정하며 다시 git init을 하거나 main/develop을 새로 만들지 않는다.

1. [시작 안내](docs/unity/UNITY_START_HERE.md)에 따라 Git LFS 파일과 Unity 6000.4.7f1을 준비한다.
2. 구매 에셋은 [씬·에셋 안전](docs/unity/SCENE_AND_ASSET_SAFETY.md)의 별도 반입 절차를 따른다. 현재 Mirror와 패키지 정의를 임의로 재설치하지 않는다.
3. 자기 담당 브랜치와 Dev 씬을 확인한다. 통합 Release 씬은 소유권 규칙을 따른다.
4. 로컬 API는 [백엔드 안내](backend/README.md), 계정 런처는 [런처 안내](launcher/ACCOUNT_LOCAL.md), 운영 배포는 [AWS 안내](backend/deploy/aws/README.md)를 따른다.
5. 환경 예제만 복사해 로컬 값을 채운다. 토큰·비밀번호·개인키는 커밋하지 않는다. [저장소 포함·제외 기준](docs/REPOSITORY_HYGIENE.md)을 확인한다.
6. UnityYAMLMerge, Visible Meta Files, Force Text와 LFS 상태를 확인한다. 실제 배선·런타임 검증은 [검증 프로토콜](docs/ai/04_MULTIPLAYER_TEST_PROTOCOL.md)을 따른다.

GitHub 브랜치 보호의 목표와 실제 적용 상태는 구분한다. 담당자는 GitHub 설정에서 필수 리뷰·검사·우회 권한을 확인하며 로컬 문서의 체크 표시를 적용 증거로 사용하지 않는다.
