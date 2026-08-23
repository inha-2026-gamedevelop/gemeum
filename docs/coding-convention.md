# 그믐 (晦) — 코딩 컨벤션

> 기준: C# / Unity 6 / Mirror
> 네임스페이스는 파트(도메인) 기준으로 나눈다. 팀원 개인 이름이 아니라 담당 시스템을 따른다.

**이 문서를 강제하는 파일**
- [`.editorconfig`](../.editorconfig) — 중괄호 위치(Allman), 멤버 변수/상수/인터페이스 명명 규칙을 IDE 경고로 표시한다. VS/Rider 모두 자동 인식.
- `Assets/ScriptTemplates/1-Scripting__MonoBehaviour Script-NewMonoBehaviourScript.cs.txt` — Unity `Create > MonoBehaviour Script`의 기본 템플릿을 9장 파일 구조로 덮어쓴다.
- `Assets/ScriptTemplates/2-Scripting__ScriptableObject Script-NewScriptableObjectScript.cs.txt` — `Create > ScriptableObject Script` 템플릿을 16장 GameDB `*Data` 규칙(메뉴 경로 `Geumeum/...`)에 맞춘다.

두 템플릿 모두 프로젝트 내장 파일이라 각자 에디터를 새로 열면 바로 적용된다 (에디터별 전역 설정이 아니다).

---

## 1. 네임스페이스 (파트 기준)

루트 네임스페이스는 `Geumeum`, 그 아래 담당 파트로 나눈다. `Assets/01.Scripts/` 하위 폴더 구조와 [CODEOWNERS](../.github/CODEOWNERS)의 파트 구분을 그대로 따른다.

```csharp
namespace Geumeum.Network      // 세션, 동기화 — 네트워크·전투 파트
namespace Geumeum.Combat       // 플레이어, 패링, 화살 — 네트워크·전투 파트
namespace Geumeum.AI           // 잡귀, 보스, 스폰 — AI·보스 파트
namespace Geumeum.Systems      // 장비, 경제, 특성, 점수 — 게임플레이 시스템 파트
namespace Geumeum.UI           // 각 파트가 자기 UI를 여기에
namespace Geumeum.Data         // ScriptableObject 정의
// 폴더 구조와 일치시킨다 — namespace가 Assets/01.Scripts/ 하위 경로와 어긋나면 잘못된 것
```

---

## 2. 명명 규칙

| 대상 | 규칙 | 예시 |
|---|---|---|
| 클래스 / 구조체 | PascalCase | `PlayerController`, `BossPhaseState` |
| 인터페이스 | I + PascalCase | `IDamageable`, `IInteractable` |
| public 메서드 | PascalCase | `TakeDamage()`, `SpawnBoss()` |
| private 메서드 | PascalCase | `CheckGrounded()`, `ApplyVisual()` |
| 로컬 변수 | camelCase | `hitPoint`, `frameCount` |
| 멤버 변수 | 언더스코어 prefix + camelCase | `_health`, `_moveSpeed` |
| 상수 | UPPER_SNAKE_CASE | `MAX_PARTY_SIZE`, `BASE_DAMAGE` |
| 정적 배열 | G_ prefix | `G_SpawnTable` |
| SerializeField | 멤버 변수와 동일 | `_jumpForce` |
| enum | PascalCase (값도 PascalCase) | `ItemGrade.Epic` |
| ScriptableObject 클래스 | PascalCase + Data 접미사 (루트 DB는 예외적으로 GameDatabaseSO) — [기술 설계 3.1](03-technical-design.md) 목록을 따른다 | `EquipmentData`, `EnemyData` |
| ScriptableObject 캐시 변수 | 멤버 변수 규칙과 동일 (`_camelCase`) | `_equipmentData`, `_enemyData` |

---

## 3. 매직넘버 절대 사용 금지

```csharp
// ❌ 절대 금지
if (_health > 100) { }
_rb.linearVelocity = new Vector2(5f, 9f);
for (int i = 0; i < 3; ++i) { }

// ✅ 반드시 이렇게
private const float MAX_HEALTH      = 100f;
private const float MOVE_SPEED      = 5f;
private const float JUMP_FORCE      = 9f;
private const int   MAX_PARTY_SIZE  = 3;

if (_health > MAX_HEALTH) { }
_rb.linearVelocity = new Vector2(MOVE_SPEED, JUMP_FORCE);
for (int i = 0; i < MAX_PARTY_SIZE; ++i) { }
```

**Inspector에서 조정할 값은 `[SerializeField]`로, 코드 내부 고정값은 `const`로.**

```csharp
// Inspector 조정용 → SerializeField
[SerializeField] private float _moveSpeed = 5f;

// 코드 내부 고정값 → const
private const float GROUND_CHECK_EXTRA = 0.05f;
private const float ATTACK_FLASH_TIME  = 0.1f;
```

밸런스 수치(체력, 피해량, 쿨다운 등)는 여기 대상이 아니다 — 코드 상수로 박지 말고 [밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md) 절차를 따라 GameDB로 뺀다.

---

## 4. 중괄호 스타일 (Allman / BSD)

여는 중괄호는 **항상 다음 줄**에 온다.
**모든 제어문(if/for/while/foreach)은 중괄호 필수 — 단일 문장이라도 예외 없음.**

```csharp
// ✅
private void Move()
{
    if (_grounded)
    {
        _rb.linearVelocity = new Vector2(_moveSpeed, 0f);
    }
}

// ❌ 중괄호 생략 금지 (단일 문장이어도)
if (_isDead) return;
for (int i = 0; i < n; ++i) DoThing(i);

// ✅ 이렇게
if (_isDead)
{
    return;
}
```

**한 줄짜리 함수는 예외적으로 한 줄 허용:**

```csharp
public void Reset() { _health = MAX_HEALTH; }
public bool IsAlive() { return _health > 0f; }
```

---

## 5. 증감 연산자

**전위 증가만 사용 (`++i`, `--i`)**

```csharp
// ✅
for (int i = 0; i < MAX_PARTY_SIZE; ++i) { }
++_frameIndex;
--_health;

// ❌
i++;
_health--;
```

---

## 6. 조건문 괄호

**복합 조건은 각 조건을 괄호로 묶는다.**

```csharp
// ✅
if ((a > b) && (c > d)) { }
if ((_health <= 0f) || (_isDead)) { }

// ❌
if (a > b && c > d) { }
```

---

## 7. 필드 정렬

**`=` 기준 정렬. 가독성 최우선.**

```csharp
// ✅ const 정렬
private const float MAX_HEALTH      = 100f;
private const float MOVE_SPEED      = 5f;
private const float JUMP_FORCE      = 9f;
private const int   MAX_PARTY_SIZE  = 3;

// ✅ SerializeField 정렬
[SerializeField] private float _moveSpeed = 5f;
[SerializeField] private float _jumpForce = 9f;
[SerializeField] private int   _maxTargets = 3;

// ✅ 일반 멤버 정렬
private float _health   = 0f;
private bool  _isDead   = false;
private int   _frameIdx = 0;
```

---

## 8. using 그룹 순서

```csharp
// System 계열
using System;
using System.Collections;
using System.Collections.Generic;

// Unity 계열
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;
using DG.Tweening;

// 프로젝트 내부 (파트 네임스페이스)
using Geumeum.Combat;
```

---

## 9. 파일 구조 (배너 주석)

### 기본 구조

```csharp
// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Combat
{
    public class ClassName : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MAX_HEALTH = 100f;

        [Header("이동")]
        [SerializeField] private float _moveSpeed = 5f;

        private float _health;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake() { }
        private void Start()  { }
        private void Update() { }

        /****************************************
        *                Methods
        ****************************************/

        public void TakeDamage(float damage) { }
        private void Die() { }
    }
}
```

## 10. 참조 타입 매개변수

```csharp
// null 가능성 없으면 참조
private void SetData(ItemData data) { }

// null 가능성 있으면 nullable 명시
private void SetTarget(Transform target) { }

// 값 자체 수정이 필요하면 ref
private void ModifySnapshot(ref Snapshot snap) { }
```

---

## 11. switch 들여쓰기

```csharp
switch (_state)
{
    case State.Idle:
        HandleIdle();
        break;
    case State.Attack:
        HandleAttack();
        break;
    default:
        break;
}
```

---

## 12. 주석

**꼭 필요한 곳에만, 무엇을 위해 만들었는지(의도·이유)를 적는다. 코드를 읽어서 알 수 있는 내용이면 주석을 달지 않는다.**

```csharp
// ✅ 이유가 있는 주석
_rb.isKinematic = true; // 넉백 중 물리 끄기

// ❌ 코드가 말하는 것을 반복
_health -= damage; // 체력 감소
```

**마침표(`.`)로 끝내지 않는다.** 짧은 구절로 쓴다.

```csharp
// ✅
_rb.isKinematic = true; // 넉백 중 물리 끄기

// ❌
_rb.isKinematic = true; // 넉백 중 물리 끄기.
```

**public API는 일반 주석이 아니라 XML 문서 주석(`<summary>`)을 쓴다. 그 외(내부 로직, private 멤버)는 일반 주석(`//`).**

```csharp
// ✅ public API — summary
/// <summary> 피해를 입힌다, 체력이 0이 되면 사망 처리 </summary>
public void TakeDamage(float damage) { }

// ✅ 내부 로직 — 일반 주석
private void ApplyKnockback()
{
    _rb.isKinematic = true; // 넉백 중 물리 끄기
}
```

**남은 작업은 TODO 주석으로 적극 남긴다.** 담당자를 알 수 있게 이름을 붙인다.

```csharp
// TODO(민성): 서버 재접속 시 스냅샷 복구 처리
```

---

## 13. null 체크

```csharp
// Unity 오브젝트는 == null 사용 (?. / ?? 금지 - fake null 미탐지)
if (_renderer == null)
{
    return;
}

// 컴포넌트 취득 + null 체크는 TryGetComponent로
if (other.TryGetComponent(out PlayerHealth health))
{
    health.TakeDamage(damage);
}

// 일반 C# 객체(델리게이트/콜백)는 ?. 사용 가능
_callback?.Invoke();
onSuccess?.Invoke();
```

---

## 14. SerializeField vs public

```csharp
// Inspector 노출은 SerializeField. public 필드는 사용하지 않는다.
// ✅
[SerializeField] private float _moveSpeed = 5f;

// ❌
public float moveSpeed = 5f;

// 외부에서 읽기만 필요하면 프로퍼티
public float MoveSpeed => _moveSpeed;
```

---

## 15. FixedUpdate 지양, 코루틴 지향

**물리 동기화가 반드시 필요한 경우가 아니면 `FixedUpdate` 사용을 지양하고, 코루틴(`IEnumerator` + `StartCoroutine`)으로 대체한다.**

```csharp
// ❌ 지양
private void FixedUpdate()
{
    _elapsed += Time.fixedDeltaTime;
    if (_elapsed >= _duration)
    {
        ApplyEffect();
    }
}

// ✅ 지향
private IEnumerator CoApplyEffect()
{
    yield return new WaitForSeconds(_duration);
    ApplyEffect();
}
```

- `Rigidbody`/`Rigidbody2D` 물리 연산(힘, 속도 적용 등)처럼 물리 스텝과 동기화가 필수인 경우에만 `FixedUpdate` 예외 허용.
- 타이머, 딜레이, 연출, 상태 전환 등 시간 기반 로직은 코루틴으로 작성한다.
- Tick 수명 관리와 캐싱 규칙은 [Unity 엔지니어링 패턴](unity/UNITY_ENGINEERING_PATTERNS.md)을 따른다.

---

## 16. 데이터 관리 (GameDB / Constants)

코드에 숫자를 직접 쓰지 않는 것은 동일하다. 값의 성격에 따라 두 곳으로 나눠 관리한다.

- **밸런스/기획 수치** (속도, 피해, 쿨다운, 패턴 간격, 연출 값 등) → **GameDB (ScriptableObject DB)**
- **코드 계약값** (입력 키/축, 태그/레이어/씬 이름, 판정 epsilon, enum 연동 개수, 배칭 상수) → **Constants**

### GameDB — ScriptableObject 기반 밸런스 DB

SO 대상 목록은 코딩 컨벤션이 아니라 [기술 설계 3.1 ScriptableObject 대상](03-technical-design.md)이 기준이다 — 새 데이터 종류를 추가할 때는 그 문서부터 갱신한다.

```
스크립트: Assets/01.Scripts/00.Common/Data/     (namespace Geumeum.Data)
에셋:     Assets/09.Data/Resources/GameDB.asset      <- 루트 GameDatabaseSO (Resources 아래 로드)
          Assets/09.Data/Equipment/EquipmentData.asset  <- 등급별 스탯 롤 범위, 특수옵션 풀
          Assets/09.Data/DropTable/DropTableData.asset  <- 필드 레벨별 등급 가중치
          Assets/09.Data/Enemy/EnemyData.asset          <- 잡귀 3종 스탯, 강화 배수
          Assets/09.Data/Barrier/BarrierPhaseData.asset <- 결계 단계별 시간·반경·피해량
          Assets/09.Data/Talent/TalentTreeData.asset    <- 특성 노드 구성과 수치
          Assets/09.Data/Shop/ShopData.asset            <- 객주 상품 목록과 가격
          Assets/09.Data/LevelCurve/LevelCurveData.asset <- 레벨별 필요 경험치, HP 증가량
          Assets/09.Data/Score/ScoreRuleData.asset      <- 점수 항목별 배점
          Assets/09.Data/DayCycle/DayCycleData.asset    <- 실시간·게임 내 시각 매핑, 조명 프리셋
```

```csharp
// 읽기 — 정적 접근은 GameDB
float dropWeight = GameDB.DropTable.GetWeight(fieldLevel);
_waitPhaseInterval = new WaitForSeconds(GameDB.Barrier.PhaseInterval);

// 매 프레임 접근 코드는 Awake에서 SO 참조를 캐싱
private EnemyData _enemyData;
private void Awake() { _enemyData = GameDB.Enemy; }
```

**GameDB 규칙**
- SO 필드는 `[SerializeField] private` + 읽기 전용 프로퍼티. 필드에 단위/용도 주석
- **MonoBehaviour 필드 초기화식/생성자에서 GameDB 호출 금지** (Resources.Load 앞섬) — Awake 이후 사용
- 컴포넌트에 밸런스 값을 SerializeField 미러로 두지 않는다 — DB 원본이 단일 소스
- 새 도메인은 [기술 설계 3.1](03-technical-design.md)에 항목 추가 → `*Data` 클래스 생성 → `GameDatabaseSO`에 참조 추가 → `GameDB` 프로퍼티 추가 → `09.Data/<도메인>/`에 에셋 생성
- 밸런스 수치 변경은 DB 에셋 인스턴스에서 — 코드는 건드리지 않는다
- `DayCycleData`는 네트워크를 타지 않는 로컬 리소스다 (서버가 경과 시간만 보내고, 조명·스카이박스는 각자 계산 — [기술 설계 3.2](03-technical-design.md))

### Constants — 코드 계약값 (partial class)

```
Constants.cs             — 공통 (레이어, 태그, 씬 이름)
Constants.player.cs      — 입력 키/축, 판정 epsilon, ANIM_DIR_*
Constants.combat.cs      — 히트스탑, 몬스터 SerializeField 기본값, 기믹 enum 연동 개수
Constants.network.cs     — 틱 레이트, 동기화 관련 상수 (Mirror)
Constants.ui/audio/...   — 각 시스템 계약값
```

```csharp
if (Input.GetKeyDown(Constants.Player.KEY_ROLL)) { }
float dist = _col.bounds.extents.y + Constants.Player.GROUND_CHECK_EXTRA;
```

**Constants 규칙**
- 해당 시스템 파일에만 추가, UPPER_SNAKE_CASE, 반드시 단위/용도 주석
- 파일 필드가 전부 밸런스 값이 되면 GameDB의 `*Data`로 승격을 검토한다

---

## 17. 런타임 GC 최소화 (프레임 드랍 방지)

**게임 루프에서 도는 코드는 프레임당 할당 0을 목표로 한다.**

```csharp
// ✅ WaitForSeconds 캐싱 — 코루틴이 반복 실행되면 필드로 1회 생성
private WaitForSeconds _waitInvincible;
private void Awake() { _waitInvincible = new WaitForSeconds(_invincibleDuration); }
// 코루틴 안에서: yield return _waitInvincible;

// ❌ 반복 코루틴 안에서 매번 new
yield return new WaitForSeconds(1f);

// ✅ renderer.material 캐싱 — Awake 1회 취득 후 재사용
private Material _material;
private void Awake() { _material = _renderer.material; }

// ❌ 매 프레임 접근 (프로퍼티 호출 비용 + 최초 인스턴스 생성 위험 불명확)
_renderer.material.color = Color.white;

// ✅ 물리 쿼리는 재사용 버퍼 + ContactFilter2D
private static readonly Collider2D[] _results = new Collider2D[16];
int count = Physics2D.OverlapCircle(origin, radius, _filter, _results);

// ❌ 호출마다 배열 할당
Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius);
```

**추가 규칙**
- 컴포넌트/참조 취득은 Awake/Start 1회 캐싱. Update/공격 루프에서 `GetComponent`/`Find` 금지
- 람다/클로저 캡처가 있으면 힙 할당 → 루프 안에서 만들지 말고, 필요 시 대상에 대해 1회만 생성
- 반복 사용하는 List는 필드로 두고 `Clear()` 후 재사용
- 오브젝트 탐색 타입은 O(1) 자료구조(Stack/Queue) 사용, `List.Contains` 루프 금지

---

## 18. 싱글턴

**DontDestroyOnLoad 싱글턴은 `Utility/PersistentSingleton<T>`, 씬 로컬 싱글턴은
`Utility/SceneSingleton<T>`를 상속한다.** Awake를 직접 만들지 말고
`OnSingletonAwake()`를 오버라이드한다.

```csharp
// ✅
public class GameManager : PersistentSingleton<GameManager>
{
    protected override void OnSingletonAwake()
    {
        // 최초 인스턴스 확정 후 1회 초기화
    }
}

public class RoundManager : SceneSingleton<RoundManager>
{
    protected override void OnSingletonAwake()
    {
        // 현재 씬 최초 인스턴스 확정 후 초기화
    }
}

// ❌ 클래스마다 Instance/Awake 중복 구현 금지
```

- 씬 배치 없이도 동작해야 하면 대상 클래스에 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 아래서 `EnsureCreated("이름")` 호출
- 도메인 리로드 OFF 대비: 대상 클래스에 `SubsystemRegistration` 아래서 `ResetStatic()` 호출
- 클래스 고유 정리는 `OnSingletonDestroy()`에서 수행한다

멀티플레이 세션에서 `NetworkManager` 등 Mirror가 자체적으로 DontDestroyOnLoad를 관리하는 오브젝트는 이 패턴을 강제하지 않는다 — [네트워크 권위](unity/NETWORK_AUTHORITY.md) 문서를 우선한다.

---

## 19. 네트워크 코드 규칙

서버 권위 판정, `[Command]`/`[ClientRpc]` 사용법, Mirror API 매핑은 이 문서에서 중복하지 않는다 — [네트워크 권위](unity/NETWORK_AUTHORITY.md)를 기준으로 한다. 이 문서의 스타일 규칙(네이밍 · 중괄호 · null 체크 등)은 네트워크 코드에도 동일하게 적용된다.

---

## 20. 여러 줄 시그니처/호출의 들여쓰기 (TABWIDTH 4)

**매개변수가 길어 줄바꿈하는 메서드 시그니처/호출은, 여는 괄호 뒤 컬럼에 맞춰 정렬하지 않는다.
이어지는 줄은 원본 줄의 들여쓰기에서 TABWIDTH(4) 만큼만 추가로 들여쓴다.**

```csharp
// ❌ 여는 괄호 뒤에 맞춤 (TABWIDTH 배수가 아닌 임의 칸수)
public BossHazardPool(int count, string namePrefix, Sprite customSprite = null,
                      bool attachParticle = false, float particleSize = 0.2f)

// ✅ 원본 줄 들여쓰기 + TABWIDTH(4)
public BossHazardPool(int count, string namePrefix, Sprite customSprite = null,
    bool attachParticle = false, float particleSize = 0.2f)
```

메서드 호출도 동일하게 적용한다.

```csharp
// ✅
Configure(i, rec.Position, rec.Scale, rec.RotationDeg, rec.Color,
    rec.HasCollider, rec.DamageHalves, rec.StunDuration, rec.InstantKill);
```

---

## 요약 카드 (팀 공유용)

```
✅ 클래스/메서드       PascalCase
✅ 멤버 변수           _camelCase
✅ 상수                UPPER_SNAKE_CASE
✅ 중괄호              다음 줄 (Allman) + 모든 제어문 필수
✅ 증감                ++i, --i (전위)
✅ 복합 조건           ((a > b) && (c < d))
✅ 매직넘버            밸런스는 GameDB(SO DB), 계약값은 Constants.*.cs
✅ SO 네이밍           클래스 *Data (기술 설계 3.1 기준), 캐시 변수는 일반 _camelCase
✅ 필드 정렬           = 기준 정렬
✅ 여러 줄 시그니처    여는 괄호 정렬 금지, 원본 줄 + TABWIDTH(4)만 추가 들여쓰기
✅ Inspector 노출      [SerializeField] private
✅ 네임스페이스        파트 기준 (Geumeum.Network/Combat/AI/Systems/UI/Data)
✅ FixedUpdate         지양, 코루틴 지향 (물리 동기화 예외)
✅ GC                  WaitForSeconds/material/참조 캐싱, NonAlloc 쿼리
✅ null 체크           Unity 오브젝트 == null, 컴포넌트는 TryGetComponent
✅ 싱글턴              DDOL=PersistentSingleton<T>, 씬 로컬=SceneSingleton<T>
✅ 네트워크            스타일은 이 문서, 권위 판정은 네트워크 권위 문서
```
