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

전역 상태와 이벤트 구독에는 명확한 소유권과 해제 시점을 둔다.

```csharp
void OnEnable()  { BarrierController.OnPhaseChanged += HandlePhase; }
void OnDisable() { BarrierController.OnPhaseChanged -= HandlePhase; }
```

해제하지 않으면 파괴된 오브젝트가 계속 호출되어 `MissingReferenceException`이 난다.
씬 전환이 잦은 게임에서 특히 자주 터진다.

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
