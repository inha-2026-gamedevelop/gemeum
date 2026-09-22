# 보스 파트 코드 읽기 순서

2026-09-22 현재 구현 기준. 보스 착수 전 학습 목록을 실제 클래스와 데이터 흐름으로 갱신했다.

## 1. 실행과 상태

`Assets/01.Scripts/03.AI/Boss/`에서 `BossController`, `BossPhaseStateBase`, `Phase1State`, `Phase2State`를 먼저 읽는다. 페이즈 상태가 선택을 담당하고 `BossPatternRunner`, `BossAttackExecution`, `BossPatternExecutionSpec`이 실행·시각·판정을 연결한다. 플레이어 봇의 Unity Behavior 도입과 보스 FSM을 혼동하지 않는다.

## 2. 데이터

`Assets/09.Data/Boss/BossPatternSO.asset`은 HP 55,000, 2페이즈·10패턴, 꼬리 3구간×1,350, 본체 25% 게이트 등 현재 데이터를 갖는다. `DerivedFromId`, `IsConditional`, `SelectionWeight`로 파생·조건·추첨을 구분한다. 값은 SO에서 읽고 코드 상수로 복제하지 않는다.

## 3. 권위·공용 계약

- 서버가 패턴 선택·피해·그로기·꼬리 파괴·최종 기믹을 확정한다. UI와 연출은 상태·cue를 표현한다.
- `BossAttackExecution`이 `IParryable`을 구현한다. 패링 창과 결과는 보스 전체가 아닌 해당 공격 실행 단위로 추적한다.
- `DamageTargetResolver`로 실제 피해 대상을 해석하고 공격별 중복 적중을 제어한다.
- `00.Common/Events/RunPhaseChangedEvent.cs`는 런 단계 값 계약이다.
- 네트워크 멤버와 `98.Servers/03.AI/` partial은 네트워크 담당 경계를 따른다. AI·패턴 규칙 작성 책임과 서버에서 실행한다는 사실은 별개다.

## 4. 성능·수명

대상 탐색은 재사용 버퍼·`OverlapSphereNonAlloc`, 초기화 때 계산하는 LayerMask를 사용한다. 페이즈 교체·그로기·사망·Result·종료에서 활성 공격, 패링 창, 예고, 코루틴을 정리하는 경로를 읽는다. 취소 후 늦은 콜백이 피해를 다시 적용하지 않아야 한다.

## 5. 개발과 검증

개발 씬은 `Assets/00.Scenes/Dev_AI.unity`다. 런타임은 `Release/BossEntrance.unity`, `Release/Boss_Arena.unity`에서 확인한다. 순서는 관련 EditMode 검사 → Dev_AI 패턴/부위/취소 기능 확인 → 외부 PC 3인 L3다. 코드 존재를 연출 품질·고지연 검증 완료로 해석하지 않는다.

## 기준 문서

[패턴 계약](../unity/BOSS_PATTERN_CONTRACT.md), [보스 설계](../unity/BOSS_DESIGN.md), [서버 협업](../unity/SERVER_CLIENT_WORKFLOW.md), [네트워크 권위](../unity/NETWORK_AUTHORITY.md), [멀티플레이 검증](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md)을 따른다.
