# 보스 파트 학습 목록

대상: @oname36-creator (몬스터 · 보스)
목적: 보스를 우리 구조 안에서 만들 수 있게 되는 것. Unity나 C#을 처음부터 배우는 목록이 아니다.

보스 착수는 집중 4주차다. 그 전에 이 목록을 훑어두면 착수할 때 구조를 다시 물어볼 일이 줄어든다.

---

## 0. 먼저 알아둘 것 — 이미 절반은 했다

잡귀에서 만든 구조가 보스의 축소판이다.

| 잡귀에서 만든 것 | 보스에서 대응되는 것 |
|---|---|
| `EnemyBrainBase` (순수 C# 판단) | `BossPhaseBase` (순수 C# 페이즈) |
| `HeokkaebiBrain`의 상태 머신 | 페이즈별 패턴 선택 |
| `EnemyController` (NB 실행부) | `BossController` (NB, 현재 페이즈에 위임만) |
| `EnemyStatus` (`IDamageable`) | `BossStatus` (`IDamageable`) |
| `EnemySO`에서 스탯을 읽는 것 | `BossPatternSO`에서 패턴을 읽는 것 |

**차이는 규모가 아니라 세 가지다.** 이 목록의 대부분은 그 셋을 다루기 위한 것이다.

1. 보스는 페이즈가 있고, 페이즈마다 **패링 가능한 패턴이 최소 1개** 있어야 한다
2. 보스는 공격 **예고(장판)** 를 띄운다 — 서버 판정과 클라 렌더링을 갈라야 하는 지점
3. 보스는 3인을 동시에 상대한다 — 대상 선택이 "가장 가까운 하나"로 끝나지 않는다

---

## 1. 우리 저장소 구조 (최우선)

보스 코드를 어디에 두고 무엇을 참조할 수 있는지부터.

**읽을 것**
- `docs/04-architecture.md` 4.3 — `03.AI/Boss/`에 놓일 클래스 목록. 이미 이름까지 정해져 있다
- `docs/04-architecture.md` 8장 — 파트 간 의존 방향. **`Geumeum.AI`가 `Geumeum.Combat`을 직접 참조하면 안 된다**
- `docs/03-technical-design.md` 4.2 — 페이즈를 왜 enum+switch로 만들지 않는지

**직접 열어볼 코드**
- `Assets/01.Scripts/00.Common/Data/BossPatternSO.cs` — 이미 만들어져 있다. `PatternEntry` · `PhaseEntry`가 무엇을 담는지 보면 페이즈 클래스가 무엇을 하지 **않아도 되는지** 알 수 있다
- `Assets/01.Scripts/01.Network/RunManager.cs` — 보스는 `RunPhase.Boss`에 진입할 때 활성화된다

**스스로 답해볼 것**
- 보스 패턴의 피해량을 바꾸고 싶다. 어느 파일을 고치나?
- `BossController`가 플레이어 HP를 깎으려면 무엇을 통해야 하나?

---

## 2. 서버 권위 — 가장 많이 틀리는 지점

이 프로젝트에서 제일 비싼 버그가 여기서 나온다. 증상이 늦게 드러나기 때문이다.

**읽을 것**
- `docs/unity/NETWORK_AUTHORITY.md` 전체. 특히 **5장 "보이지만 판정에 걸리는 것"** — 보스 장판이 정확히 이 사례다
- 같은 문서 5.5 Mirror 매핑, 8장 코드 리뷰 체크리스트

**핵심 질문 하나로 요약된다**: 이 값이 사람마다 달라도 게임 결과가 같은가?
같으면 클라이언트, 다르면 서버.

**보스에 적용하면**

| 값 | 어디서 |
|---|---|
| 보스 HP, 페이즈 전환 | 서버 |
| 패턴 선택, 쿨다운 경과 | 서버 (`NetworkTime.time`) |
| 장판 **판정** | 서버 |
| 장판 **그림** | 클라 (서버가 패턴 ID · 위치 · 시각만 보낸다) |
| 예고 이펙트, 사운드, 화면 흔들림 | 클라 |

장판을 클라에서 그리고 그 콜라이더로 판정하면, 프레임이 다른 세 사람이 서로 다른 순간에 맞는다.

**스스로 답해볼 것**
- 장판이 0.8초 뒤 터진다. 서버는 무엇을 보내고 클라는 무엇을 계산하나?
- 늦게 접속한 사람은 진행 중인 장판을 봐야 하나? RPC와 SyncVar 중 어느 쪽인가?

---

## 3. Mirror 실무

문법보다 **어느 코드가 어디서 도는지**가 중요하다.

**공부할 것**
- `[Server]` · `[Command]` · `[ClientRpc]` · `[TargetRpc]` · `[SyncVar]`가 각각 어디서 실행되는가
- `OnStartServer` / `OnStopServer` / `OnStartClient`의 호출 시점
- 리슨 서버(호스트)에서는 **한 프로세스가 서버이자 클라이언트**다. `isServer`와 `isClient`가 동시에 참이 되는 상황
- `NetworkServer.Spawn` / `NetworkServer.Destroy`
- `syncInterval` — 오브젝트마다 다르게 잡는 이유

**우리 코드에서 볼 것**
- `EnemyController.cs` — 본인이 쓴 `[Server]` 메서드들
- `Assets/01.Scripts/01.Network/LobbyController.cs` — `[Command(requiresAuthority = false)]`에서 **서버가 호출자를 검증하는** 형태
- `Assets/01.Scripts/01.Network/RunManager.cs` — SyncVar로 상태를 배포하고 이벤트는 따로 발행하는 형태

**주의**: `docs/unity/NETWORK_AUTHORITY.md`의 "주의할 Mirror 함정" 절을 그냥 넘기지 말 것. SyncVar 훅이 호스트에서 언제 도는지가 여기 있다.

---

## 4. 상태 패턴 (페이즈 구조)

`BossController`가 `switch (phase)`로 커지면 손을 못 대게 된다. 그래서 `BossPhaseBase` 상속으로 간다.

**공부할 것**
- 상태 패턴 (State Pattern) — 상태마다 클래스 하나, 컨트롤러는 현재 상태에 위임
- C# 추상 클래스와 가상 메서드
- 진입 · 갱신 · 퇴장(`Enter` / `Tick` / `Exit`)을 나누는 이유

**우리 코드에서 볼 것**
- `HeokkaebiBrain.cs` — 지금은 enum + switch다. 상태가 5개라 아직 괜찮지만, **보스는 이 방식으로 가지 않는다**. 왜 잡귀는 되고 보스는 안 되는지 생각해볼 것 (힌트: 패턴 수와 페이즈별 차이)
- `Assets/01.Scripts/02.Combat/Parry/ParryJudge.cs` — 판정 수식만 순수 C#으로 떼는 형태

**설계 제약 두 가지**
- 페이즈 클래스는 **패턴 선택과 전환만** 한다. 수치 · 쿨다운 · 예고 시간은 `BossPatternSO`가 소유한다
- 페이즈마다 `IsParryable == true`인 패턴이 최소 1개. 없으면 차사 특성이 보스전에서 통째로 무용지물이 된다

---

## 5. 파트 간 계약

다른 파트의 클래스를 직접 부르면 통합 주에 컴파일이 깨진다.

**읽을 것** — 전부 `Assets/01.Scripts/00.Common/`에 있고, 파일 하나가 짧다
- `Interfaces/IDamageable.cs` — 보스가 구현한다. 플레이어를 때릴 때도 이걸로 때린다
- `Interfaces/IParryable.cs` — **보스 패턴이 구현한다.** 판정 창을 열고 `OnParried`로 경직을 처리하는 계약
- `Types/DamageInfo.cs` — 가해자를 `netId`로만 들고 있다. 왜 `GameObject`가 아닌지 주석에 있다
- `Interfaces/ICombatFeedback.cs` — 타격감. **서버에서 호출하지 않는다**
- `Events/IGameEventBus.cs`, `Events/EnemyDiedEvent.cs`
- `01.Network/RunPhaseChangedEvent.cs` — 보스 등장 신호를 여기서 받는다

**규칙**
- 이벤트 payload에 다른 파트의 구체 클래스를 넣지 않는다. 대상은 `netId`로 지칭한다
- 구독은 `OnStartServer`, 해제는 `OnStopServer`. 씬 로컬 컴포넌트면 `OnEnable` / `OnDisable` 쌍

**스스로 답해볼 것**
- 보스가 죽었다. 드랍은 누가 만드나? 보스 코드가 `DropDirector`를 직접 불러도 되나?

---

## 6. Unity 성능 패턴

보스전은 잡귀가 0마리인 구간이라 여유가 있어 보이지만, 장판 · 투사체 · 이펙트가 한꺼번에 뜬다.

**읽을 것**
- `docs/unity/UNITY_ENGINEERING_PATTERNS.md` 전체 (짧다)
- `docs/coding-convention.md` 17장 (GC 최소화), 22장 (Find 계열 지양)

**특히 볼 것**
- **1장 상시 Tick 줄이기** — 매 프레임 돌 필요가 없는 판정을 주기로 나누기
- **2장 반복 탐색 캐싱** — `GetComponent` · `Find`는 초기화 때 한 번
- **3장 코루틴 수명** — 중복 시작 방지, 파괴 시 종료 명시. 장판 타이머를 코루틴으로 만들면 반드시 걸린다
- **6장 오브젝트 풀링** — 장판과 투사체가 대상이다

**지금 코드에서 고칠 거리** (잡귀에서 이미 나온 것들이라 보스에서 반복되기 쉽다)
- `EnemyController.RefreshTarget`이 `Physics.OverlapSphere`를 쓴다 → 호출마다 배열을 새로 만든다. `OverlapSphereNonAlloc` + 재사용 버퍼가 규칙이다 (`Constants.Combat.HIT_QUERY_BUFFER_SIZE`가 이미 있다)
- 같은 곳에서 `LayerMask.GetMask(문자열)`을 매번 부른다 → 초기화 때 한 번 캐싱
- `GameEventBus.Instance`를 null 검사 없이 쓴다 → 씬에 버스가 없으면 거기서 터진다

---

## 7. 데이터 구동 — 수치를 코드에 박지 않는다

10월 집중개발 중 밸런싱을 반복한다. 그때마다 코드를 고치고 빌드하면 팀 전체가 기다린다.

**읽을 것**
- `docs/unity/BALANCE_DATA_WORKFLOW.md`
- `docs/coding-convention.md` 16장 — GameDB(밸런스)와 Constants(코드 계약값)의 경계

**구분**
- 보스 HP, 패턴 피해량, 쿨다운, 예고 시간 → `BossPatternSO`
- 물리 쿼리 버퍼 크기, 레이어 이름 → `Constants`

**주의**: `BossPatternSO`는 만들어져 있지만 값이 비어 있다(`_maxHealth = 0`). 페이즈 수와 패턴은 아직 미정이고, 정하는 것이 보스 파트의 일이다.

---

## 8. NavMesh와 이동

보스는 잡귀보다 이동이 단순할 수도 복잡할 수도 있다 (돌진 · 순간이동 · 고정형).

**공부할 것**
- `NavMeshAgent`의 `SetDestination` · `isStopped` · `speed`
- NavMesh를 벗어나는 이동(도약 · 돌진)을 어떻게 처리하는가 — `Warp`, `NavMeshAgent.enabled` 토글
- **NavMesh 이동은 서버에서만 돌린다.** 클라는 `NetworkTransform`이 보간한 결과만 본다

**우리 코드**: `EnemyController`가 이미 `NavMeshAgent`를 쓴다. 보스도 같은 형태로 시작하면 된다.

---

## 9. 검증 방법

"내 화면에서 됐다"는 이 프로젝트에서 완료가 아니다.

**읽을 것**
- `docs/ai/03_VERIFICATION_PLAYBOOK.md` — L1 / L2 / L3 기준
- `docs/ai/04_MULTIPLAYER_TEST_PROTOCOL.md` — M0~M3

**보스에 적용하면**
- L1: 컴파일 에러 0, 신규 경고 0
- L2: 담당 씬에서 페이즈 전환과 패턴 발동을 눈으로 1회 이상
- L3: **3인 접속에서 확인.** 보스는 3인을 동시에 상대하므로 단일 플레이 결과를 믿을 수 없다

**`Dev_AI.unity`는 아직 만들어져 있지 않다.** 협업 규칙상 이 씬의 수정 권한은 몬스터 · 보스 파트지만 파일 자체가 없다.
씬을 새로 만드는 것은 팀 전체에 영향이 가는 변경이라(빌드 설정 · 씬 목록) 만들기 전에 팀장에게 알릴 것.
지금 잡귀 작업을 어디서 확인하고 있는지도 함께 공유해주면 좋겠다.

**보스에서 특히 3인이 필요한 이유**
- 대상 선택이 사람마다 다르게 보이지 않는지
- 장판 판정 시각이 세 화면에서 같은지
- 한 명이 패링했을 때 나머지 두 명 화면에도 경직이 보이는지

---

## 10. 안 배워도 되는 것

시간은 유한하다. 아래는 다른 파트 소유라 지금 파고들 필요가 없다.

- 결계 축소 · 범위 밖 피해 (`04.Systems/Barrier`, @kjw1237)
- 장비 생성 · 드랍 테이블 · 상점 · 노잣돈 (`04.Systems`, @kjw1237)
- 로비 · 런 시계 · 점수 정산 (`01.Network`, 팀장)
- 셰이더 · 파티클 · 조명 (`05.Art`, 팀장 겸임)
- Mirror의 트랜스포트 내부, 데디케이티드 배포

보스가 이들과 만나는 지점은 **이벤트와 인터페이스 하나씩**이다. 그 계약만 알면 된다.

---

## 순서 제안

한 번에 다 볼 필요는 없다.

| 순서 | 항목 | 이유 |
|---|---|---|
| 1 | 2장 서버 권위 | 여기를 틀리면 나중에 전부 다시 짠다 |
| 2 | 5장 파트 간 계약 | 파일이 짧고, 읽어두면 다른 파트를 안 건드리게 된다 |
| 3 | 3장 Mirror 실무 | 이미 잡귀에서 절반은 쓰고 있다 |
| 4 | 4장 상태 패턴 | 보스 착수 직전에 봐도 늦지 않다 |
| 5 | 6장 성능 · 7장 데이터 구동 | 코드를 쓰면서 같이 |
| 6 | 8장 NavMesh · 9장 검증 | 필요할 때 찾아보는 용도 |

---

## 기준 문서 모음

| 주제 | 문서 |
|---|---|
| 클래스 배치 · 의존 방향 | `docs/04-architecture.md` |
| 무엇을 서버가 판정하는가 | `docs/unity/NETWORK_AUTHORITY.md` |
| Tick · 캐싱 · 코루틴 · 풀링 | `docs/unity/UNITY_ENGINEERING_PATTERNS.md` |
| C# 스타일 | `docs/coding-convention.md` |
| 밸런스 수치 다루기 | `docs/unity/BALANCE_DATA_WORKFLOW.md` |
| 씬 · meta 사고 방지 | `docs/unity/SCENE_AND_ASSET_SAFETY.md` |
| 검증 기준 | `docs/ai/03_VERIFICATION_PLAYBOOK.md` |
| 3인 테스트 절차 | `docs/ai/04_MULTIPLAYER_TEST_PROTOCOL.md` |
| 보스 기획 | `docs/02-game-design.md` 9장 |

막히면 문서를 더 찾기 전에 팀장에게 묻는 편이 빠르다. 특히 **권위 경계가 애매한 경우**는 혼자 판단하지 말 것.
