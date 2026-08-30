# Unity 엔지니어링 패턴

> 실제로 문제가 생겼을 때 해당 절만 읽는다. 처음부터 전부 읽지 않는다.

---

## 1. 상시 Tick을 줄인다

`Update`, `LateUpdate`, `FixedUpdate`는 **매 Tick 처리가 실제로 필요할 때만** 쓴다.

| 상황 | 권장 |
|---|---|
| 상태가 바뀔 때만 반응 | 이벤트 · 콜백 |
| 한정된 기간의 감시 | 코루틴 또는 타이머, 끝나면 중지 |
| 매 프레임 위치 갱신 | `Update` 정당 |
| 물리 연산 | `FixedUpdate` 정당 |

### 그믐에서 자주 걸리는 것

```csharp
// 잘못 — 결계 밖 판정을 매 프레임
void Update() {
    if (Vector3.Distance(transform.position, center) > radius)
        TakeDamage(damagePerSecond * Time.deltaTime);
}

// 맞음 — 1초 주기 타이머, 서버에서만
// 매 초 판정이 기획 요구사항이므로 매 프레임 계산할 이유가 없다
```

**코루틴을 썼다는 사실만으로 Tick 최적화가 됐다고 판정하지 않는다.**

### 같은 판정을 여러 개체가 돌면 프레임을 나눈다

개활 구간에는 잡귀가 최대 22마리 동시에 존재하고, 그 판정을 전부 호스트 한 대가 돈다.
경로 재계산처럼 무거운 것을 매 프레임 전원이 돌 이유는 없다.

```csharp
// 잘못 - 22마리가 매 프레임 전원 경로 재계산
void Update() { _agent.SetDestination(_target.position); }

// 맞음 - 프레임마다 정해진 수만 갱신하고 나머지는 이전 경로로 이동
// 갱신 주기가 늘어져도 추적 체감은 거의 그대로다
```

리스트를 인덱스로 돌며 프레임당 처리 수를 상한으로 두면, 개체가 늘어도 호스트 부하는 상한 안에 머문다.
반응이 즉시 필요한 것(피격, 사망, 패링 창)은 분산 대상이 아니다. 이벤트로 그 자리에서 처리한다.

---

## 2. 반복 탐색을 캐싱한다

`GetComponent`와 `Find` 계열은 초기화 · 생성 · 바인딩 시점에 한 번 수행하고 캐싱한다.

```csharp
// 잘못
void Update() {
    GetComponent<Rigidbody>().AddForce(...);
}

// 맞음
private Rigidbody rb;
void Awake() { rb = GetComponent<Rigidbody>(); }
void Update() { rb.AddForce(...); }
```

동적 생성물이나 교체된 참조를 **관계 변경 시점에 한 번** 다시 바인딩하는 것은 위반이 아니다.

`Find`, `FindObjectOfType`, `GameObject.FindWithTag` 는 씬 전체를 순회하므로 Tick 루프에서 절대 쓰지 않는다.

---

## 3. 코루틴 수명

코루틴은 시작보다 **종료**가 어렵다.

- 중복 시작을 막는다 (기존 핸들을 저장하고 `StopCoroutine`)
- 완료 · 취소 · 비활성화 · 파괴 시점의 종료를 명시한다
- 종료 시 상태를 복원한다

```csharp
private Coroutine barrierRoutine;

void StartShrink() {
    if (barrierRoutine != null) StopCoroutine(barrierRoutine);
    barrierRoutine = StartCoroutine(ShrinkOverTime());
}

void OnDisable() {
    if (barrierRoutine != null) { StopCoroutine(barrierRoutine); barrierRoutine = null; }
}
```

`OnDisable`이나 `OnDestroy`에서 정리하지 않으면 상태가 어긋난 채 남는다.

---

## 4. 이벤트 구독 해제

이벤트 구독에는 명확한 소유권과 해제 시점을 둔다.
해제하지 않으면 파괴된 오브젝트가 계속 호출되어 `MissingReferenceException`이 난다.

**정적 이벤트는 쓰지 않는다.** 구독자 목록이 씬 수명과 무관하게 살아남는다.
Domain Reload를 끄면 플레이를 멈춰도 핸들러가 남아 다음 플레이에서 중복 호출된다.

### 같은 파트 안 — 인스턴스 이벤트를 직접 쓴다

```csharp
public override void OnStartServer()
{
    _status.Died += HandleDied;
}

public override void OnStopServer()
{
    _status.Died -= HandleDied;
}
```

### 파트 경계를 넘을 때 — GameEventBus를 지난다

```csharp
private void OnEnable()
{
    if (!GameEventBus.HasInstance)
    {
        return;
    }

    GameEventBus.Instance.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
}

private void OnDisable()
{
    // 씬이 끝날 때 버스가 먼저 파괴되면 Instance가 null이다
    if (!GameEventBus.HasInstance)
    {
        return;
    }

    GameEventBus.Instance.Unsubscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
}
```

다른 파트의 구체 클래스를 참조하면 통합할 때 한 파트를 고칠 때마다 다른 파트가 깨진다.
배치 규칙은 [프로젝트 구조 설계서](../04-architecture.md) 8장에 있다.

### 해제 시점은 컴포넌트 종류로 정한다

| 컴포넌트 | 구독 | 해제 |
|---|---|---|
| 서버 전용 NetworkBehaviour | `OnStartServer` | `OnStopServer` |
| 씬 로컬 MonoBehaviour | `OnEnable` | `OnDisable` |

서버 이벤트를 클라이언트에서 구독하지 않는다. 호스트에서는 한 프로세스가 서버와 클라이언트를
겸하므로, 구독 시점을 잘못 잡아도 호스트 혼자서는 증상이 드러나지 않는다.

### 람다로 구독하면 해제되지 않는다

```csharp
// 해제되지 않는다, 두 람다가 서로 다른 델리게이트다
bus.Subscribe<RunPhaseChangedEvent>(e => HandlePhaseChanged(e));
bus.Unsubscribe<RunPhaseChangedEvent>(e => HandlePhaseChanged(e));

// 메서드 그룹을 넘긴다
bus.Subscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
bus.Unsubscribe<RunPhaseChangedEvent>(HandlePhaseChanged);
```

`Unsubscribe`는 델리게이트 비교로 지운다. 이 절이 막으려는 누수를 이 실수가 그대로 만든다.

---

## 5. 직렬화된 enum

`EquipGrade`, `EnemyType` 같은 직렬화 대상 enum은 **명시값을 유지**하고 기존 값을 재배열하지 않는다.

```csharp
public enum EquipGrade {
    Common  = 0,
    Talisman = 1,
    Sacred  = 2,
    Relic   = 3,
}
```

값을 재배열하면 이미 저장된 ScriptableObject 에셋의 값이 다른 것을 가리키게 된다.
새 항목은 **끝에 추가**한다.

---

## 6. 오브젝트 풀링

잡귀와 투사체는 풀링을 검토한다.

| 대상 | 이유 |
|---|---|
| 화살 | 연사 특성이면 초당 여러 발 |
| 잡귀 | 구간별 18~22마리, 리스폰 반복 |
| 히트 이펙트 | 전투 중 지속 생성 |

Unity 6에는 `UnityEngine.Pool.ObjectPool<T>`가 내장되어 있다. 직접 구현하지 않는다.

**단, 프로토타입 단계에서는 하지 않는다.** 성능 문제가 실제로 관찰된 뒤에 적용한다.

---

## 7. 오류 처리

오류를 조용히 삼키지 않는다.

```csharp
// 잘못
try { DoSomething(); } catch { }

// 맞음
try { DoSomething(); }
catch (Exception e) { Debug.LogError($"[Barrier] 축소 실패: {e.Message}"); }
```

로그에 **어느 시스템인지 접두사**를 붙인다. Console Pro 3에서 필터링할 수 있다.

---

## 8. 성능 확인

문제가 있다고 느끼면 추측하지 말고 측정한다.

| 도구 | 용도 |
|---|---|
| Profiler | CPU · GPU · 메모리 |
| Frame Debugger | 드로우콜 |
| Memory Profiler | 할당 추적 |

**리슨 서버 구조에서는 호스트 PC가 게임 로직과 렌더링을 동시에 부담한다.**
성능 측정은 호스트에서 한다. 클라이언트만 보면 실제 병목을 놓친다.

---

## 9. UI 작업 — 씬 우선, 코드는 최소한

UI(`06.UI`)는 게임플레이 시스템 담당(@kjw1237)이 전담한다 ([프로젝트 구조 설계서 4.6](../04-architecture.md#46-06ui--게임플레이-시스템kjw1237이-전담)).

**Unity 에디터에서 직접 만들고 눈으로 확인할 수 있는 것은 전부 씬 · 프리팹에서 만든다.** 코드로는 에디터에서 할 수 없는 것만 작성한다.

| 씬/프리팹에서 한다 | 코드로 한다 |
|---|---|
| 레이아웃 · 앵커 · 정렬 (RectTransform, Layout Group) | 게임 상태 → 텍스트 · 값 · 활성화 여부 바인딩 |
| 색상 · 폰트 · 스타일 (컴포넌트 인스펙터) | 이벤트 구독 (버튼 클릭 → Command 호출 등) |
| 패널 계층 구조, 버튼 · 슬라이더 등 컴포넌트 배치 | 데이터 기반 반복 생성 (인벤토리 슬롯처럼 개수가 가변인 목록) |

```csharp
// 잘못 — 인스펙터로 되는 배치를 코드로 조립
void Awake() {
    var panel = new GameObject("HpBar");
    panel.transform.SetParent(canvas.transform);
    var image = panel.AddComponent<Image>();
    image.color = Color.red; // 이런 값도 인스펙터에서 정한다
}

// 맞음 — 프리팹에 이미 배치된 참조를 코드는 갱신만 한다
[SerializeField] private Image hpFillImage;
void OnHpChanged(float ratio) { hpFillImage.fillAmount = ratio; }
```

개수가 실행 중에 정해지는 목록(인벤토리 슬롯, 리더보드 행 등)처럼 씬에서 미리 배치할 수 없는 경우에만 코드로 인스턴스화한다. 이때도 슬롯 자체의 레이아웃 · 스타일은 프리팹으로 만들고, 코드는 그 프리팹을 개수만큼 생성 · 바인딩하는 역할만 한다.
