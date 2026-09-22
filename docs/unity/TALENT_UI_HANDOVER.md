# 인수인계 — 현재 특성 기반 UI 전면 개편

경로: `docs/unity/TALENT_UI_HANDOVER.md`  
작성일: 2026-09-22  
인계: 김민성 @AidenKim923 / 인수 대상: 게임플레이 시스템·UI 담당 @kjw1237  
상태: **현행 구현 인계. 기존 화면에 39노드 hover 상세 설명을 추가했다. 전체 UI 개편은 미착수.**

[세션 인수인계 양식](../ai/05_SESSION_HANDOFF.md)의 1~8항을 따른다. 담당자에게 필요한 계약을 이 문서에 담는다. 새 디자인을 확정하는 기획안이 아니라 현재 코드·데이터·연동 계약을 인계하는 문서다.

## 1. 목표

- 최종 상태: 현재 특성의 연결 관계와 효과를 쉽게 이해하고, 포인트 배분·초기화 결과를 정확히 확인할 수 있도록 **인게임 특성 UI를 전면 개편**한다.
- 사용자 요구: 엠버 앤 블레이드처럼 **연결된 노드를 해금하는 성장 트리 중심**. 외부 게임의 화면·색·배치를 그대로 복제하라는 요구는 아니다. 런처 파티 UI 개편과는 별도 작업이다.
- 이번 인계 범위: 실제 39노드, 선행 관계, 포인트/초기화/무기 변경 규칙, UI 접점, 교체 범위, 완료 기준.
- 후속 담당자 결정: 전체 배치, 아이콘, 연결선 표현, 상세 설명 영역, 선택/확정 인터랙션, 해상도 대응. 이 문서는 특정 시안을 승인한 것으로 취급하지 않는다.
- 근거: [전투 개편안](PLAYER_COMBAT_REWORK.md), [검·활 구현 기록](SWORD_BOW_REWORK_IMPLEMENTATION.md), [방울 구현 기록](BELL_RESONANCE_IMPLEMENTATION.md). 과거 기획과 차이가 있으면 현재 SO 및 코드를 우선한다.

### 현재 특성 구조와 규칙

| 항목 | 현행 계약 |
|---|---|
| 무기 | 검(WeaponKind 0), 활(1), 방울(2) |
| 노드 수 | 무기당 13개, 전체 39개. 공통 뿌리 1개 + 3갈래 × 4개 |
| 화면 좌표 | ColumnIndex/RowIndex. 최대 4열×5행은 용량이며 20개 실제 노드를 뜻하지 않는다 |
| 비용/등급 | 현재 모든 노드 PointCost=1, 각 노드 1회 해금. 다단계 강화 아님 |
| 포인트 | LevelCurveSO에서 레벨당 1, 시작 레벨1에서 0, 레벨10에서 총9. UI는 고정 숫자 대신 Growth/AvailablePoints 읽기 |
| 연결 | EnumeratePrerequisites가 반환하는 선행 **모두** 필요. 현재 데이터는 뿌리에서 갈라지는 3개 직선 가지 |
| 갈래 선택 | 상호 배타 규칙 없음. 포인트와 선행을 충족하면 여러 갈래 혼합 가능 |
| 초기화 | RequestReset으로 전체 무료 초기화. 단일 노드 환불·부분 취소·가격·횟수 제한은 현재 계약에 없음 |
| 무기 변경 | 무기 종류가 바뀌면 기존 배분 초기화. 같은 종류 교체는 유지. UI가 자체적으로 초기화하지 않음 |
| 수명 | 런 단위 상태. 새 런에서 초기화. 계정 영구 성장/디스코드 계정 DB 저장 아님 |
| 상태 소유 | 호스트 서버 권위, 소유 클라이언트에 동기화. 다른 파티원 특성 열람 API로 취급하지 않음 |

명시 선행 배열이 비어 있으면 **같은 열의 낮은 행 전체**를 선행으로 해석하는 호환 규칙이 있다. 따라서 빈 배열을 언제나 뿌리로 간주하면 안 된다. 현재 공통 노드는 행0이라 실제 선행이 없다. 연결선도 SO의 유효 선행을 기준으로 그린다.

| 무기 | 뿌리 열 | 갈래1 | 갈래2 | 갈래3 |
|---|---|---|---|---|
| 검 | 칼의 뿌리 | 받아치는 길 | 몰아치는 길 | 물러서는 길 |
| 활 | 시위의 뿌리 | 새기는 길 | 꿰뚫는 길 | 흐르는 길 |
| 방울 | 울림의 뿌리 | 거두는 길 | 지키는 길 | 퍼뜨리는 길 |

## 2. 완료한 것

| 항목 | 증거 | 상태 |
|---|---|---|
| 39개 노드 정의·연결 | `Assets/09.Data/Talent/TalentTreeSO.asset`, 아래 부록 | 정적 확인 완료 |
| 배분·초기화·서버 검증 | `TalentRules`, `TalentController` 및 server/network partial | 구현 확인 |
| 기존 화면의 노드·연결선 표시 | `FunctionalManagementPanel.RefreshTalentView/RefreshTalentEdges` | 구현 확인, 새 디자인 아님 |
| 검·활·방울 효과 적용 | `CombatStatCalculator` 및 무기별 런타임, 구현 기록 | 기존 기능 검증 기록 있음 |
| 인수 계약 정리 | 이 문서와 원본 소스 대조 | 문서 검수 완료 |

### 기존 화면의 상세 설명 계약 (2026-09-22)

- 특성 노드 hover 또는 키보드 선택 시 이름, 실제 효과와 발동 조건, 필요/남은 포인트, 선행 특성, 해금 상태를 표시한다. 잠긴 버튼도 포인터 이벤트를 받아야 한다.
- 설명창은 FunctionalManagementPanel이 런타임에 만들며 기존 폰트를 재사용한다. 설명창 자체의 raycast는 꺼서 노드 클릭을 막지 않는다.
- 0.2초 스냅샷 갱신 때 같은 노드를 비활성화했다 다시 켜면 hover가 끊긴다. 노드의 활성 상태를 유지하고 내용과 상태만 갱신한다.
- 탭 전환, 닫기, 소유 플레이어/무기 변경 때 남은 설명을 숨긴다. 설명은 부모 영역 안에 배치하고 긴 한국어 문장은 줄바꿈한다.
- 잔울림은 시전자가 자기 진 안에서 실제 회수 피해를 냈을 때 공명진을 한 번 연장한다. 일선 관통은 같은 화살이 다음으로 관통한 적에 적용한다. 함께 걷는 울림은 반경 증가와 지속시간 감소를 함께 안내한다.

### 담당자가 읽을 파일

아래 경로는 저장소 루트 기준이다.

| 경로 | 역할 / 인수 시 주의 |
|---|---|
| `Assets/09.Data/Talent/TalentTreeSO.asset` | 현재 이름·ID·효과·수치·선행의 원본 |
| `Assets/01.Scripts/00.Common/Data/TalentTreeSO.cs` | NodeEntry, ColumnEntry, 조회와 선행 열거 |
| `Assets/09.Data/LevelCurve/LevelCurveSO.asset` | 레벨과 포인트 공급 |
| `Assets/01.Scripts/04.Systems/Growth/TalentRules.cs` | 배분 가능 여부. UI에 별도 규칙 복제 금지 |
| `Assets/01.Scripts/04.Systems/Growth/TalentController.cs` | 공개 요청·스냅샷·이벤트 |
| `Assets/01.Scripts/04.Systems/Growth/Net/TalentController.network.cs` | 소유자 Command, revision 전달, 동기화 |
| `Assets/01.Scripts/98.Servers/04.Systems/Growth/TalentController.server.cs` | 최종 승인·포인트 소비·초기화·무기 변경 |
| `Assets/01.Scripts/04.Systems/Growth/CombatStatCalculator.cs` | 실제 효과 수치 해석 |
| `Assets/01.Scripts/00.Common/Types/TalentEffectKind.cs` | enum 숫자 계약. 은퇴한 효과도 있으므로 enum 전체를 노드 목록으로 쓰지 말 것 |
| `Assets/01.Scripts/06.UI/Systems/FunctionalManagementPanel.cs` | 기존 특성 페이지, 입력 게이트, 소유 플레이어 바인딩 |
| `Assets/01.Scripts/06.UI/Systems/FunctionalUiText.cs` | 효과 값·서버 결과 문구, GetTalentEffectDetail/BuildTalentTooltip의 39노드 발동 조건·비용·선행·해금 상태 |
| `Assets/01.Scripts/06.UI/Systems/FunctionalUiIndexedButton.cs` | 화면 인덱스→NodeId 연결, 포인터/키보드 선택의 HoverChanged 통지 |
| `Assets/01.Scripts/06.UI/Systems/Editor/RunManagementUiBuilder.cs` | 기존 생성 레이아웃 4×5, 기준1280×720. 새 프리팹과 구 생성기가 충돌하지 않게 정리 |
| `Assets/02.Prefabs/UI/RunManagementUI.prefab` | 관리 UI 프리팹. 다른 관리 페이지 보존 |

### UI가 유지해야 할 연동 계약

| 목적 | 기존 창구 | 주의 |
|---|---|---|
| 현재 배분 읽기 | `TryGetOwnerAllocation(out TalentAllocationSnapshot)` | false면 로딩/미준비 상태. 이전 무기 화면을 활성 상태로 남기지 않기 |
| 잔여 포인트 | `TalentController.AvailablePoints` | 스냅샷 SpentPoints 및 Growth 총포인트와 구분 |
| 노드 선택 요청 | `RequestAllocate(int nodeId)` | 인덱스 아닌 실제 NodeId. 내부에서 revision 전송 |
| 전체 초기화 | `RequestReset()` | 서버 승인 전 실제 배분을 지우지 않기 |
| 상태 변경 | `RevisionChanged` | 최신 스냅샷 재조회. 무기/배분 전환 시 선택 상세도 재검사 |
| 요청 결과 | `AllocationResult` | 성공/거절 결과를 표시하고 최신 상태와 대조 |
| 가능 여부 미리보기 | `TalentRules.CanAllocate(tree, allocation, totalPoints, nodeId, out node)` | 세 번째 인수는 **총 포인트**. AvailablePoints를 넣으면 사용량을 이중 차감하게 됨 |

요청 성공 문구만으로 로컬 수치를 확정하지 않는다. 서버 스냅샷과 revision이 기준이다. 연속 클릭·지연 응답·창 닫기 후 응답에서도 중복 소비나 오래된 표시가 없어야 한다. `StaleRevision`은 최신 상태를 읽고 다시 선택하도록 안내한다. 요청 대기 표시는 UI 상태이며 서버의 새 배분 상태가 아니다.

`InvalidState`, `InvalidNode`, `WeaponMismatch`, `MissingPrerequisite`, `AlreadyAllocated`, `InsufficientPoints`, `StaleRevision`, `RevisionExhausted`를 성공과 구별한다. 공격 동작 중 `BlocksTalentMutation`에 의해 요청이 거절될 수 있다. `CanAllocate` 통과가 서버의 최종 수락 보장은 아니다.

## 3. 완료하지 못한 것

| 항목 | 어디까지 했나 | 막힌 지점 / 후속 담당 |
|---|---|---|
| 새 화면 시안·구현 | 현행 계약 정리만 완료 | UI 담당자가 시안 제시 후 구현 |
| 39개 상세 설명·아이콘 | SO 이름/EffectKind/Value, FunctionalUiText 상세 설명 구현. 별도 설명·아이콘 SO 필드는 없음 | 효과 수치는 SO, 조건 시간은 CombatConfig를 읽는다. 새 화면에서도 기존 상세 설명을 재사용하고 아이콘은 별도 설계 |
| 새 UI 캡처·해상도 검증 | 미착수 | 새 화면 구현 이후 |
| 모든 특성 조합 실전 밸런스 | 대표 기능/규칙 검증 기록 있음 | UI 완료와 전체 밸런스 확정을 혼동하지 않기 |
| 서로 다른 회선 PC3대 검증 | 기존 AWS 경유 동일PC3프로세스 기록 있음 | 외부PC3대 실전은 별도 수행 |

기존 화면은 20개 버튼 용량과 4열 배치를 사용하며 실제 13개만 노출한다. 런타임 `EnsureTalentViewCapacity`, `TalentPosition`, `RefreshTalentView`가 버튼 수·좌표·크기를 다시 설정한다. **프리팹 배치만 바꾸면 실행 시 기존 좌표로 덮일 수 있다.** UI 개편 시 생성기·런타임 배치·프리팹 연결을 함께 점검해야 한다.

## 4. 현재 저장소 상태

- 기준: `feature/Minsung` 작업 트리. 최근 특성·전투·UI 변경을 포함해 광범위한 미커밋 변경이 존재한다. 현재 HEAD만 받아서는 같은 상태라는 보장이 없다.
- 수정한 씬: 이번 작업 없음. 새 UI는 담당자 소유 Dev 씬에서 검증하고 Main 통합은 팀장과 조율한다. 기존 생성기 주석의 Dev_HUD가 자동 편집 권한을 부여하지 않는다.
- 신규 ScriptableObject: 이번 작업 없음. 현행 TalentTreeSO/LevelCurveSO를 읽기 원본으로 사용한다.
- UI 담당 수정 범위: `Assets/01.Scripts/06.UI/Systems/`의 특성 표현 및 연결된 프리팹/표현 에셋. 인벤토리·객주·양도 기능 회귀 방지.
- 별도 협의 범위: `00.Common` 공용 스키마/enum, `Net/`, `98.Servers/`, 전투 효과 판정, SO 수치·노드ID·선행 변경. 이번 요청은 기존 특성을 기반으로 한 UI 교체이며 밸런스 재설계가 아니다.

## 5. 검증 현황

- 이번 작업: 코드/SO/인수 문서 정적 대조만 수행. 실행 코드 변경이 없어 새 Unity 테스트는 수행하지 않았다.
- 기존 검·활 기록: 관련 전체 EditMode 658 PASS 후 최종 수정 관련47 PASS, 3프로세스 검·활·방울 대표 기능 검증 기록. **새 UI 통과 증거는 아님.**
- 기존 배포 기준: 게임0.0.7 / `win64-20260922-0020`, 런처1.2.11. 실제 설치 버전은 인수 시 재확인.
- 새 UI 최소 검증: L1 + L2, 서버 요청/바인딩/초기화 연동 변경 시 L3(M2 이상). M3는 외부PC3대 결과가 있을 때만 표시.

### 완료 기준과 확인 방법

- [ ] 검·활·방울 각각 13개만 표시한다. 빈 20칸이나 은퇴한 노드는 노출하지 않는다. 부록 ID 목록과 대조한다.
- [ ] 각 무기 뿌리 및 3갈래 연결이 SO 선행과 일치한다. 다중 선행을 지원한다면 AND 의미를 보존한다.
- [ ] 미준비/미획득/획득 가능/선행 부족/포인트 부족/획득 완료/요청 중·거절을 사용자가 구별한다. 색 외의 문구·아이콘 등으로도 확인한다.
- [ ] 노드 이름·비용·효과·조건·선행과 총/사용/잔여 포인트가 맞는다. 소수 비율, 초, m, 해금 효과를 모두 %로 표기하지 않는다.
- [ ] 포인트0, 뿌리 선택, 가지 혼합, 선행 없는 말단 클릭, 중복 클릭, 총9포인트 배분을 확인한다.
- [ ] 전체 초기화, 같은 종류 장비 교체, 다른 무기 종류 변경, 새 런에서 스냅샷·연결선·상세 선택이 맞게 갱신된다.
- [ ] 요청 거절·지연 응답·StaleRevision에서 가짜 해금/포인트 차감이 남지 않는다. 3인에서 다른 플레이어 배분에 영향이 없다.
- [ ] 기존 I키 관리 화면 진입 및 특성 탭, Escape/닫기, 로컬 입력 게이트와 커서 복구가 유지된다. 창 위 클릭이 공격/방울 설치로 전달되지 않는다. UI 열기로 전체 게임을 임의 일시정지하지 않는다.
- [ ] 레벨업 중 갱신, 플레이어 재바인딩, 비활성화·씬 전환 후 이벤트 중복 구독/이전 플레이어 참조가 없다.
- [ ] 1280×720·1920×1080 및 넓은 화면에서 글자/연결선/클릭 영역·긴 한글 설명을 캡처해 확인한다.
- [ ] 인벤토리·객주·양도 등 같은 패널의 다른 페이지를 회귀 확인한다.
- [ ] 변경 파일, 시안/실행 캡처, L1/L2/L3 결과와 미검증 항목을 첨부해 인계한다.

관련 기존 테스트: `04.Systems/Growth/Editor/TalentRulesTests.cs`, `TalentControllerTests.cs`, `CombatStatCalculatorTests.cs`, `06.UI/Systems/Editor/FunctionalUiTests.cs`(모두 `Assets/01.Scripts/` 기준). 기존 내부 필드/버튼에 묶인 UI 테스트는 새 구조에 맞추되 기능 보장을 제거하지 않는다.

## 6. 차단 조건

- [ ] **공유된 코드 기준 확보:** 미커밋 최신 특성 구현을 받지 못했다면 오래된 2열 트리를 기준으로 작업하지 않는다.
- [ ] **화면 시안 확정:** 시각 방향·상세 영역·확정 방식은 아직 미정. 기존 기능 인수 조사에는 차단 아님.
- [ ] **공용 계약 추가:** 상세 설명/아이콘을 위한 SO 필드 또는 요청 API 변경이 필요할 때 팀장과 범위를 맞춘다. 기존 API를 그대로 쓰는 표현 작업까지 대기할 필요는 없다.
- [ ] **최종 통합 검증:** 새 UI L2 캡처와 필요한 L3 증거 확보 전 완료(검증됨)로 보고하지 않는다.

## 7. 다음 세션의 첫 행동

1. CLAUDE.md → 이 문서 → 실제 TalentTreeSO → TalentController/TalentRules → 기존 FunctionalManagementPanel 순으로 읽는다.
2. 최신 작업 트리를 확보하고 대상 diff·씬 소유권을 확인한다. 무기별 실제 노드 수/포인트/선행을 다시 대조한다.
3. 기존 특성 화면을 실행·캡처해 교체 전 상태를 남긴다. 실패하면 원인과 현재 증거를 기록한다.
4. 연결 트리를 중심으로 시안을 만들고, 기존 계약에서 바뀌는 점과 유지되는 점을 명시한다. 세부 디자인 검토는 별도 단계다.
5. UI를 교체하고 위 완료 기준으로 검증한다. 새 스킨이 기존 런타임 배치 코드에 덮이지 않는지 먼저 확인한다.
6. 변경 파일·캡처·검증 결과·남은 문제를 같은 1~8항 양식으로 후속 기록한다. 커밋/푸시는 별도 요청에 따른다.

## 8. 주의

- NodeId, WeaponKind, EffectKind 숫자를 재배열/재사용하지 않는다. 화면 인덱스와 식별자를 분리한다.
- 부록은 작성 시점 스냅샷이며 런타임에 복사해 하드코딩할 데이터가 아니다. 실제 원본은 SO다.
- 새 UI를 위해 서버 판정을 클라이언트로 옮기지 않는다. 포인트·효과를 UI에서 직접 수정하지 않는다.
- 상세 설명에는 단순 값뿐 아니라 실제 조건을 적는다. 예: 활 노드2108은 이름과 달리 현재 일반·정밀 화살 모두에 추가 관통이 적용된다. 이름만 보고 조건을 추정하지 않는다.
- 효과 enum에는 구 노드용 예약 값이 남아 있다. SO에 없는 구 방울 효과 등을 신규 트리에 표시하지 않는다.
- `.meta`와 프리팹 직렬화 참조를 보존한다. 다른 사람의 Dev 씬·Main·외부 에셋 원본을 임의 수정하지 않는다.

## 부록 A. 작성 시점의 실제 노드 목록

다음 표는 SO에서 추출한 원시 값이다. EffectKind의 적용 조건과 표시 단위는 `CombatStatCalculator`, 무기 런타임 및 `FunctionalUiText.DescribeTalentValue`를 함께 확인한다. `Value=1`이라고 모두 +1%가 아니며 해금 플래그일 수도 있다. 좌표는 열/행이다.


### Sword (WeaponKind 0)

| NodeId | DisplayName | Column/Row | Prerequisites | PointCost | EffectKind | Value |
|---|---|---|---|---|---|---|
| 1101 | 고른 호흡 | 0/0 | - | 1 | BasicStaminaReduction | 1 |
| 1102 | 남기는 틈 | 1/0 | 1101 | 1 | CounterDurationBonus | 0.3 |
| 1103 | 받아낸 호흡 | 1/1 | 1102 | 1 | ParryStaminaBonus | 3 |
| 1104 | 빠른 회수 | 1/2 | 1103 | 1 | CounterRecoveryReduction | 0.1 |
| 1105 | 되받는 칼 | 1/3 | 1104 | 1 | CounterThirdCombo | 1 |
| 1106 | 겹베기 | 2/0 | 1101 | 1 | SecondComboDamagePercent | 10 |
| 1107 | 이어가는 칼 | 2/1 | 1106 | 1 | ComboWindowBonus | 0.2 |
| 1108 | 재촉하는 칼 | 2/2 | 1107 | 1 | ThirdHitSkillCooldown | 1 |
| 1109 | 끊기지 않는 칼 | 2/3 | 1108 | 1 | SkillSecondCombo | 1 |
| 1110 | 물러선 호흡 | 3/0 | 1101 | 1 | RollBasicStaminaReduction | 2 |
| 1111 | 빈 칼 회수 | 3/1 | 1110 | 1 | MissSkillRecoveryReduction | 0.1 |
| 1112 | 돌아드는 베기 | 3/2 | 1111 | 1 | RollSkillDamagePercent | 10 |
| 1113 | 돌아드는 칼 | 3/3 | 1112 | 1 | SideStepSkill | 1 |

### Bow (WeaponKind 1)

| NodeId | DisplayName | Column/Row | Prerequisites | PointCost | EffectKind | Value |
|---|---|---|---|---|---|---|
| 2101 | 고른 시위 | 0/0 | - | 1 | BowBasicStaminaReduction | 1 |
| 2102 | 가벼운 긁기 | 1/0 | 2101 | 1 | BowSkillStaminaReduction | 2 |
| 2103 | 남기는 상처 | 1/1 | 2102 | 1 | WedgeDurationBonus | 1 |
| 2104 | 균열의 박자 | 1/2 | 2103 | 1 | WedgeFirstHitCooldown | 1 |
| 2105 | 깊은 상처 | 1/3 | 2104 | 1 | WedgeNormalBurstPercent | 20 |
| 2106 | 고요한 호흡 | 2/0 | 2101 | 1 | PrecisionStaminaReduction | 2 |
| 2107 | 정밀 파열 | 2/1 | 2106 | 1 | WedgePrecisionBurstPercent | 10 |
| 2108 | 꿰뚫는 화살 | 2/2 | 2107 | 1 | PrecisionPierceBonus | 1 |
| 2109 | 일선 관통 | 2/3 | 2108 | 1 | PrecisionFollowThrough | 0.4 |
| 2110 | 흐르는 호흡 | 3/0 | 2101 | 1 | BowRollBasicReduction | 2 |
| 2111 | 여유로운 시위 | 3/1 | 2110 | 1 | BowComboWindowBonus | 0.1 |
| 2112 | 새기고 물러나기 | 3/2 | 2111 | 1 | WedgeRollReduction | 3 |
| 2113 | 사냥의 발걸음 | 3/3 | 2112 | 1 | RollShotRecoveryReduction | 0.08 |

### Bell (WeaponKind 2)

| NodeId | DisplayName | Column/Row | Prerequisites | PointCost | EffectKind | Value |
|---|---|---|---|---|---|---|
| 3101 | 울림의 시작 | 0/0 | - | 1 | BellFirstHitSpiritGain | 1 |
| 3102 | 가벼운 회수 | 1/0 | 3101 | 1 | EchoRecallSpiritCost | 1 |
| 3103 | 긴 잔향 | 1/1 | 3102 | 1 | EchoDurationSeconds | 1 |
| 3104 | 진 안의 회수 | 1/2 | 3103 | 1 | EchoFieldDamagePercent | 10 |
| 3105 | 잔울림 | 1/3 | 3104 | 1 | FieldEchoExtendSeconds | 1 |
| 3106 | 아낀 손길 | 2/0 | 3101 | 1 | HealSpiritCost | 2 |
| 3107 | 깊은 손길 | 2/1 | 3106 | 1 | HealAmountPercent | 10 |
| 3108 | 고른 숨 | 2/2 | 3107 | 1 | FieldHealEchoDiscount | 2 |
| 3109 | 따뜻한 여운 | 2/3 | 3108 | 1 | FieldHealAfterglowPercent | 15 |
| 3110 | 가벼운 울림 | 3/0 | 3101 | 1 | SkillSpiritCost | 2 |
| 3111 | 넓은 울림 | 3/1 | 3110 | 1 | FieldRadiusBonus | 0.3 |
| 3112 | 머무는 울림 | 3/2 | 3111 | 1 | FieldTailSeconds | 0.5 |
| 3113 | 함께 걷는 울림 | 3/3 | 3112 | 1 | FieldWideningRadiusBonus | 0.7 |
