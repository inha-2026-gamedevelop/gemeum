# AI 작업 문서

> 그믐 프로젝트에서 AI 도구와 함께 일하기 위한 운영 문서.
> 원본은 프로젝트 중립 템플릿이었고, 이 버전은 그믐에 맞게 값을 채우고 세이브 마이그레이션 절을 네트워크 · 씬 안전으로 교체한 것이다.

---

## 처음이라면

**전부 읽지 않는다.** 지금 하려는 일에 해당하는 것만 연다.

| 상황 | 읽을 것 |
|---|---|
| AI에게 작업을 시킬 때 | [작업 브리프](02_TASK_BRIEF_TEMPLATE.md) |
| AI가 "다 됐다"고 했을 때 | [검증 플레이북](03_VERIFICATION_PLAYBOOK.md) |
| 네트워크 코드를 만질 때 | [네트워크 권위](../unity/NETWORK_AUTHORITY.md) · `Net/` · `98.Servers/` 담당은 [서버·클라이언트 협업](../unity/SERVER_CLIENT_WORKFLOW.md) |
| 3인 테스트를 해야 할 때 | [멀티플레이 테스트](04_MULTIPLAYER_TEST_PROTOCOL.md) |
| 작업을 중단할 때 | [세션 인수인계](05_SESSION_HANDOFF.md) 양식으로 `.claude/docs/{YYYY-MM-DD}/{항목}.md`를 쓰고 `HANDOFF_CURRENT.md` 포인터를 갱신 |
| 기획서를 구현으로 옮길 때 | [기획 문서 → 구현](09_DOCUMENT_ANALYSIS_WORKFLOW.md) |

저장소 루트의 [`CLAUDE.md`](../../CLAUDE.md)는 AI가 자동으로 읽는다. 사람이 먼저 읽을 필요는 없다.

---

## 문서 지도

### AI 작업 운영

| 문서 | 언제 |
|---|---|
| [01 실행 정책](01_AGENT_EXECUTION_POLICY.md) | 승인 경계와 중단 조건을 정할 때 |
| [02 작업 브리프](02_TASK_BRIEF_TEMPLATE.md) | 기능 · 버그 · 조사를 AI에 의뢰할 때 |
| [03 검증 플레이북](03_VERIFICATION_PLAYBOOK.md) | 완료 주장에 증거를 연결할 때 |
| [04 멀티플레이 테스트](04_MULTIPLAYER_TEST_PROTOCOL.md) | **3인 접속 검증이 필요할 때 — 이 프로젝트의 핵심** |
| [05 세션 인수인계](05_SESSION_HANDOFF.md) | 작업을 중단하거나 넘길 때 |
| [06 컨텍스트 관리](06_CONTEXT_HYGIENE.md) | 긴 세션이나 대형 파일을 다룰 때 |
| [07 프로젝트 지식 맵](07_PROJECT_KNOWLEDGE_MAP.md) | "이건 어디 문서에 있나" 싶을 때 |
| [08 병렬 작업 분업](08_SUBAGENT_ORCHESTRATION.md) | 여러 작업을 동시에 돌릴 때 |
| [09 기획 문서 → 구현](09_DOCUMENT_ANALYSIS_WORKFLOW.md) | 기획서를 작업 단위로 쪼갤 때 |
| [10 응답 스타일](10_RESPONSE_STYLE_GUIDE.md) | AI 보고 형식을 통일할 때 |
| [11 다단계 작업 절차](11_MULTI_STAGE_WORKFLOW_SOP.md) | 여러 세션에 걸친 작업을 관리할 때 |
| [12 에셋 · 데이터 정책](12_ASSET_AND_DATA_POLICY.md) | 외부 AI에 무엇을 넣어도 되는지 판단할 때 |

### Unity

| 문서 | 언제 |
|---|---|
| [처음 프로젝트를 열 때](../unity/UNITY_START_HERE.md) | 클론 후 첫 실행까지 |
| [네트워크 권위](../unity/NETWORK_AUTHORITY.md) | 서버냐 클라냐 판단할 때 |
| [서버·클라이언트 협업](../unity/SERVER_CLIENT_WORKFLOW.md) | `Net/` · `98.Servers/` 담당과 판정 · AI 작성 흐름을 확인할 때 |
| [클라이언트 API 안내](../unity/CLIENT_API_REFERENCE.md) | UI · 표현 코드가 서버에 요청 · 조회할 때 |
| [서버·클라이언트 분리 검증 기록](../unity/SERVER_CLIENT_REFACTOR_VALIDATION.md) | 분리 작업에서 무엇을 검증했고 무엇이 남았는지 볼 때 |
| [씬 · 에셋 안전](../unity/SCENE_AND_ASSET_SAFETY.md) | 씬 · `.meta` · LFS를 다룰 때 |
| [밸런스 데이터 작업](../unity/BALANCE_DATA_WORKFLOW.md) | 수치를 만들거나 바꿀 때 |
| [엔지니어링 패턴](../unity/UNITY_ENGINEERING_PATTERNS.md) | 성능 · 수명 문제가 실제로 생겼을 때 |
| [LOD · 컬링 현황](../unity/LOD_AND_CULLING.md) | 컬링 · LOD가 이미 적용됐는지 확인할 때 |

---

## 원본과 달라진 점

| 원본 | 이 버전 |
|---|---|
| `{{PLACEHOLDER}}` 다수 | 그믐 실제 값으로 채움 |
| 세이브 스키마 · 마이그레이션 4개 문서 | **삭제.** 로그라이크라 런 상태를 저장하지 않는다 |
| — | **[멀티플레이 테스트 절차] 신규.** 이 프로젝트의 실제 검증 난제 |
| — | **[네트워크 권위] 신규.** 세이브 마이그레이션 자리를 대체 |
| — | **[씬 · 에셋 안전] 신규.** 이 프로젝트에서 복구 불가능한 사고 지점 |
| — | **[밸런스 데이터 작업] 신규.** ScriptableObject 운용 |
| 개인정보 중심 데이터 정책 | 구매 에셋 라이선스 중심으로 재편 |
| Unity 숙련도 질문 게이트 | 삭제. 팀 3명 전원 프로그래머라 불필요 |
| 네트워크 라이브러리 미정 | **Mirror 확정.** API 매핑을 네트워크 권위 문서에 추가 |
| 기능별 브랜치 전략 | **`feature/{팀원이름}` 담당자별로 변경** |

---

## 유지보수

- 코드와 문서가 다르면 **코드를 우선**하고 차이를 기록한다
- 한 규칙을 여러 문서에 복제하지 않고 원본을 링크한다
- 밸런스 수치는 문서보다 ScriptableObject가 최신이다. 매번 동기화하려 애쓰지 않는다
- 더 이상 유효하지 않은 문서는 지운다. 역사 보존이 필요하면 인수인계로 옮긴다
