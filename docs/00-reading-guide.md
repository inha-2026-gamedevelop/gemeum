# 어디부터 읽을까

> 그믐 저장소에는 문서가 여러 개 있다. **전부 읽을 필요는 없다.**
> 이 문서는 "지금 내가 뭘 읽어야 하는가"에만 답한다.

---

## 결론부터

| 상황 | 읽을 것 | 대략 |
|---|---|---|
| 오늘 팀에 합류했다 | 아래 **1단계** 3개 | 30분 |
| 첫 코드를 쓰려고 한다 | **2단계** 2개 + 내 파트 문서 | 30분 |
| 그 외 전부 | **읽지 않는다.** 필요해질 때 3단계 표에서 찾아 연다 | — |

현재 구현 여부는 [구현 현황](IMPLEMENTATION_STATUS.md), 문서 분류와 갱신 규칙은 [문서 관리 기준](DOCUMENTATION_POLICY.md)을 따른다. `docs/ai/`는 도구 운영 규칙이며 팀 검증·인계에도 함께 적용한다.

---

## 1단계 — 합류 첫날

이 순서대로 읽는다.

| 순서 | 문서 | 얻는 것 |
|---|---|---|
| 1 | [README](../README.md) | 무슨 게임인지, 내 브랜치와 씬이 무엇인지 |
| 2 | [처음 프로젝트를 열 때](unity/UNITY_START_HERE.md) | 클론부터 Play 버튼까지. **막히면 여기 맨 아래 "자주 겪는 문제"를 먼저 본다** |
| 3 | [협업 규칙](../CONTRIBUTING.md) | 브랜치 · 커밋 · 씬 규칙 · PR |

세 번째를 건너뛰지 않는다. 이 프로젝트에서 되돌릴 수 없는 사고는 대부분 여기 적힌 규칙을 몰라서 난다.

---

## 2단계 — 첫 코드를 쓰기 전

| 문서 | 얻는 것 |
|---|---|
| [코딩 컨벤션](coding-convention.md) | 네이밍 · 중괄호 · 주석 · 수치를 어디에 둘지 |
| [프로젝트 구조 설계서](04-architecture.md) | 내 클래스를 어느 폴더 · 네임스페이스에 놓을지 |
| [주간 계획](06-weekly-plan.md) | 이번 주에 무엇을 닫아야 하는지 — 파트별 남은 일 · 주차 게이트 · 절단선 |
| [구현 로드맵](05-implementation-roadmap.md) | 남은 것(U01~U45)을 어떻게 만들지 — 완료 표 · 현재 코드 · 권위 경계 · 권장 접근 · 위험 |

컨벤션은 길지만 **맨 아래 "요약 카드"만 봐도 대부분 통과한다.** 세부는 걸릴 때 찾아본다.

### 파트별로 하나 더

| 파트 | 추가 필독 |
|---|---|
| 네트워크 · 전투 | [네트워크 권위](unity/NETWORK_AUTHORITY.md) · [서버·클라이언트 협업](unity/SERVER_CLIENT_WORKFLOW.md) |
| 몬스터 · 보스 | [네트워크 권위](unity/NETWORK_AUTHORITY.md) · [서버·클라이언트 협업](unity/SERVER_CLIENT_WORKFLOW.md) · [게임 디자인 5·9절](02-game-design.md) |
| 게임플레이 시스템 · 결계 | [밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md) · [클라이언트 API 안내](unity/CLIENT_API_REFERENCE.md) · [게임 디자인 2·6~8·11절](02-game-design.md) |
| 테크니컬 아트 (팀장 겸임) | [씬 · 에셋 안전](unity/SCENE_AND_ASSET_SAFETY.md) · [기술 설계 4.4](03-technical-design.md) |

---

## 3단계 — 필요할 때만 연다

미리 읽지 말고, 아래 상황이 실제로 닥쳤을 때 연다.

| 이럴 때 | 이 문서 |
|---|---|
| Steam 없이 자체 계정·Discord 관리·멀티플레이로 전환한다 | [Steam 독립 서비스 전환 계획](unity/STEAM_INDEPENDENT_SERVICE_PLAN.md) — PostgreSQL·인증·리슨 호스트·AWS 릴레이·업데이트 알림 |
| 로컬 Python 계정 API를 실행·검증한다 | [백엔드 실행 안내](../backend/README.md) — PostgreSQL 시작·테스트 계정 생성·API 문서·중지 |
| 서버가 판정할지 클라가 할지 모르겠다 | [네트워크 권위](unity/NETWORK_AUTHORITY.md) |
| `Net/` · `98.Servers/` 파일을 건드리거나 누가 작성할지 헷갈린다 | [서버·클라이언트 협업](unity/SERVER_CLIENT_WORKFLOW.md) |
| UI · 표현에서 서버에 요청하거나 상태를 읽는다 | [클라이언트 API 안내](unity/CLIENT_API_REFERENCE.md) |
| Git에 올릴 파일을 구분한다 | [저장소 포함·제외 기준](REPOSITORY_HYGIENE.md) |
| 씬 · 프리팹 · `.meta`를 건드린다 | [씬 · 에셋 안전](unity/SCENE_AND_ASSET_SAFETY.md) |
| 수치를 만들거나 바꾼다 | [밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md) |
| 3인 접속 테스트를 해야 한다 | [멀티플레이 테스트](ai/04_MULTIPLAYER_TEST_PROTOCOL.md) |
| 프레임이 떨어지거나 코루틴이 꼬였다 | [엔지니어링 패턴](unity/UNITY_ENGINEERING_PATTERNS.md) |
| LOD · 오클루전 컬링이 켜져 있는지 확인한다 | [LOD · 컬링 현황](unity/LOD_AND_CULLING.md) |
| 밸런스 수치가 궁금하다 | [게임 디자인](02-game-design.md) |
| 전투의 무게감·역동성과 협동 보스전 방향을 검토한다 | [전투 감각·보스 방향 검토](unity/COMBAT_FEEL_AND_BOSS_DIRECTION.md) — 참고 게임 분석·도입 대안·비교 실험안 |
| 타격감을 액티브·스타일리시하게 만든다 (젠존제·마영전 참고) | [액티브·스타일 전투 표현 설계](unity/COMBAT_STYLE_DESIGN.md) — 타격 강도 사다리·적 히트스톱·타이밍 섬광·극한 회피·데미지 숫자와 서버 결과 계약 (제안) |
| 활·방울의 고유 능력과 타격감을 재설계한다 | [신궁·무녀 상세 재설계](unity/BOW_AND_BELL_REDESIGN.md) — 쐐기 파열·잔향 회수·모션/음향 지시·기존 계약 이관 후보 |
| 왜 이렇게 설계했는지 궁금하다 | [기술 설계](03-technical-design.md) |
| 2026-08-29 서버·플레이어 문서가 무엇 때문에 바뀌었는지 본다 | [프로젝트 구조 설계서 13장](04-architecture.md) |
| 일정 · 스코프가 궁금하다 | [주간 계획](06-weekly-plan.md) · [시스템 기획서](01-system-design.md) |
| 특성 UI를 개편한다 | [담당자 인계](unity/TALENT_UI_HANDOVER.md) |
| 게임·런처를 게시한다 | [패치 운영](unity/PATCH_SYSTEM_DESIGN.md) · [AWS 운영](../backend/deploy/aws/README.md) |
| **내가 맡은 기능을 이제 구현한다** | 아래 "기능별 상세 설계" 표 |
| "이거 어느 문서에 있더라" | [프로젝트 지식 맵](ai/07_PROJECT_KNOWLEDGE_MAP.md) |
| AI에게 작업을 시킨다 | [작업 브리프](ai/02_TASK_BRIEF_TEMPLATE.md) |
| AI가 "다 됐다"고 했다 | [검증 플레이북](ai/03_VERIFICATION_PLAYBOOK.md) |

---

## 기능별 상세 설계

현재 구현 계약과 후속 기획을 구분한 상세 문서다. 담당 기능의 문서와 실제 코드·SO를 함께 읽는다.

| 문서 | 무엇을 확정하나 | 담당 파트 |
|---|---|---|
| [패링 시스템](unity/PARRY_SYSTEM_DESIGN.md) | 판정 시각 모델, `ParryController` 계약, 최대 3개 반격 표식과 R 소비 | 네트워크 · 전투 |
| [몬스터 공격 → 패링 창 연결](unity/MONSTER_PARRY_LINK_DESIGN.md) | 창귀 돌진이 패링 창을 여는 수직 슬라이스와 최소 텔레그래프 | 몬스터 · 보스 |
| [PlayerAttack 공통 계약](unity/PLAYER_ATTACK_DESIGN.md) | 검 · 활 · 방울 공통 공격 계약, 서버 권위 화살 | 네트워크 · 전투 |
| [보스 설계](unity/BOSS_DESIGN.md) | 보스 1마리 2페이즈, 실행 템플릿 4개와 데이터 항목 10개 | 몬스터 · 보스 |
| [보스 패턴 계약](unity/BOSS_PATTERN_CONTRACT.md) | 패턴 10개 · 선택기 · 엇박 · **그로기 게이지** · 보스 독립 14분 시계 · **꼬리 3덩이와 25% 게이트** · **최종 구간 원혼 셋** · 현재 SO 대응 | 몬스터·보스 |
| [보스 연출 설계](unity/BOSS_STAGING_DESIGN.md) | 페이즈 전환 15초의 컷신·먹 커튼·색값 타임라인 | 테크니컬 아트 · 몬스터·보스 |
| [티저 제작 설계](unity/TEASER_PRODUCTION.md) | After Effects 없이 코드로 만드는 티저 파이프라인 — Recorder 녹화 · Remotion 조립 · 샷 데이터 · 벨가르딘 실측 | 테크니컬 아트 · 통합 |
| [보스 트라이 모드](unity/BOSS_TRIAL_MODE.md) | 30분을 건너뛰고 보스만 반복하는 진입 경로, 편성 예산, 리더보드 미등록 | 네트워크 · 게임플레이 시스템 |
| [카메라와 락온](unity/CAMERA_AND_LOCKON.md) | **카메라 리그 · 락온 · 이동/조준/구르기 · 스트레이프 애니메이션 · 셰이크 · 컷신 · 화살 유도.** 값까지 정한 구현 사양 | 네트워크 · 전투 · 테크니컬 아트 |
| [결계 · 인벤토리 · 상점](unity/BARRIER_INVENTORY_SHOP_DESIGN.md) | 결계 축소 · 피해, 12칸 통합 인벤토리, 드랍 · 노잣돈 · 객주 | 게임플레이 시스템 |
| [애니메이션 파이프라인](unity/ANIMATION_PIPELINE_DESIGN.md) | PlayerActionCue, 무기별 모션, 서버 시각과 표현 계층 | 테크니컬 아트 |

문서의 구현·제안·미검증 표기를 구분한다. 실제 구현 현황은 [현재 구현](IMPLEMENTATION_STATUS.md)과 코드·SO를 기준으로 확인한다.

---

## 읽지 않아도 되는 것

| 문서 | 왜 |
|---|---|
| [CLAUDE.md](../CLAUDE.md) · [AGENTS.md](../AGENTS.md) | AI 도구가 자동으로 읽는다. 사람이 먼저 볼 필요 없다 |
| `docs/ai/`의 운영 문서 | AI 협업 운영 문서. 필요해지면 [AI 작업 문서 안내](ai/README.md)가 골라준다 |

---

## 이 세 가지만은

문서를 안 읽었어도 이건 알아야 한다. **되돌릴 수 없는 사고가 나는 지점이다.**

| 하지 말 것 | 왜 |
|---|---|
| 남의 씬 · `Main.unity` 열어서 저장 | Unity 씬은 머지가 안 된다. 한쪽 작업이 통째로 사라진다 |
| `.meta` 파일 따로 지우거나 커밋 누락 | GUID가 새로 발급돼 프리팹 · 씬 참조가 전부 끊긴다. 원인 추적이 매우 어렵다 |
| 클라이언트에서 게임 결과를 판정 | 사람마다 결과가 달라진다. 증상이 늦게 나타나 디버깅 비용이 크다 |

판단 기준 한 줄: **이 값이 사람마다 달라도 게임 결과가 같은가?** 같으면 클라이언트, 다르면 서버.

`git lfs pull`도 잊지 않는다. 안 하면 이미지와 모델이 텍스트 한 줄로 보인다.

---

## 문서끼리 다를 때

| 무엇과 무엇이 | 무엇을 믿나 |
|---|---|
| 문서 vs 코드 | **코드.** 그리고 차이를 팀에 알린다 |
| 기획 문서 vs ScriptableObject 값 | **ScriptableObject.** 문서는 설계 의도를, 데이터는 현재 값을 담는다 |
| 문서 vs 문서 | 더 좁은 범위의 문서. 예를 들어 네트워크는 [네트워크 권위](unity/NETWORK_AUTHORITY.md)가 기준이다 |

문서의 수치를 현재값으로 안내한다면 SO와 함께 갱신한다. 변경 전 수치는 결정 이력으로 분리하고 구현 원본처럼 두지 않는다. 후속 제안은 현재값과 명시적으로 구분한다.

---

## 그래도 모르겠으면

1. [프로젝트 지식 맵](ai/07_PROJECT_KNOWLEDGE_MAP.md)의 질문 라우팅 표에서 키워드로 찾는다
2. 그 표에 소유자가 적혀 있으니 그 사람에게 묻는다
3. 문서를 못 찾겠으면 그냥 팀에 묻는다. **문서를 뒤지는 시간이 물어보는 시간보다 길면 문서가 잘못된 것이다**

읽다가 틀렸거나 낡은 내용을 발견하면 고쳐서 PR을 보낸다. `docs/`는 승인 없이 고쳐도 되는 영역이다.
