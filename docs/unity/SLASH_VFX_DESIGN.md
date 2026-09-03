# 검기(슬래시 VFX) 개발 설계

> 상태: 설계 초안, 구현 전
> 기준일: 2026-09-03
> 기준 코드: `23d979b` (클래스 구조변경 — `PlayerAppearance` → `PlayerCharacter`)
> 대상: `PlayerAttack`, `PlayerAnimationController`, `05.Art/Shaders` · `05.Art/VFX`(현재 비어 있음), 검기 프리팹

검을 휘두를 때 궤적을 따라 나오는 초승달 이펙트를 만드는 방법과 순서를 정한다.
연출 자체보다 **판정과 표현을 어긋나지 않게 붙이는 것**이 이 문서의 목적이다.

- 권위 원칙: [`NETWORK_AUTHORITY.md`](NETWORK_AUTHORITY.md)
- 공격 구현 현황과 빈 곳: [`PLAYER_ATTACK_DESIGN.md`](PLAYER_ATTACK_DESIGN.md) 2장 · 7.2 · 7.3 · 7.5
- 모션 cue와 Animation Event 규칙: [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 3.3 · 6장 · 8장
- 파일 배치: [`../04-architecture.md`](../04-architecture.md) 4.5 · 7장
- 판정 수치 원본: `CombatConfigSO` (코드 상수로 복제하지 않는다)

---

## 0. 이 문서가 확정하는 핵심 결정

1. **검기는 파티클이 아니라 메시 한 장이다.** 휘어진 판 메시에 셰이더로 쓸어 그린다. 파티클 시스템으로는 두꺼운 초승달이 나오지 않는다.
2. **검기 크기는 코드 상수가 아니라 `CombatConfigSO`에서 유도한다.** `MeleeRange`와 `MeleeAngle`로 스케일을 정해, 밸런싱으로 사거리가 바뀌면 이펙트가 따라간다 — 5장.
3. **전파 RPC의 이름은 `RpcAttackStarted`로 통일한다.** [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 3.3이 이미 `PlayerAttack`을 `RpcAttackStarted`의 소유자로 계획해 두었다. 검기가 다른 이름으로 하나 더 만들면 애니메이션 파이프라인이 붙을 때 전파 경로가 둘이 된다 — 3.2.
4. **트리거는 Animation Event가 본선이다.** 모션 프레임에 붙어야 타이밍이 맞는다. 공격 clip이 아직 없으므로 그때까지는 RPC 수신 시각 기준으로 재생한다 — 3.4.
5. **`ICombatFeedback`에는 지금 검기를 붙일 창구가 없다.** `PlayHit`·`PlayParrySuccess`·`PlayCritical`뿐이고 "휘두르기 시작"이 없다. 인터페이스를 넓히지 않고 `PlayerAnimationController` 쪽에 붙인다 — 3.3.
6. **시각 계수 같은 표현 조정값은 `CombatConfigSO`에 넣지 않는다.** 판정 수치 SO에 표현값을 섞으면 밸런스 담당이 이펙트 값을 건드리게 된다. [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 3.3이 규정한 `AnimationCatalogSO`(표현 조정값 소유)가 자리다 — 5.3.
7. **검기는 판정에 일절 관여하지 않는다.** 검기 프리팹에 `NetworkIdentity`, 콜라이더, `IDamageable` 참조를 두지 않는다.

---

## 1. 목표와 범위

### 1.1 만드는 것

검(차사)의 기본 공격이 나갈 때 궤적을 따라 한 번 재생되고 사라지는 초승달 이펙트.
**세 클라이언트 모두에서** 자기 것과 남의 것이 보여야 한다.

### 1.2 범위 밖

이번에 하지 않는다. 하나씩 별도 작업이다.

| 항목 | 왜 미루나 |
|---|---|
| 히트 이펙트, 히트스톱, 화면 흔들림 | `ICombatFeedback.PlayHit` 구현체(`05.Art/CombatFeedbackImpl`)가 필요하다. 검기와 트리거 시점이 다르다(명중 시점) |
| 치명타 전용 연출 | `PlayCritical`이 별도 창구다. 검기 색만 바꿀지 다른 이펙트를 낼지 미정 |
| 화살 궤적, 방울 이펙트 | 무기별 연출은 검이 자리를 잡은 뒤 같은 틀로 복제한다. 방울은 애초에 기본 공격이 없다 — 7장 5단계 |
| 사운드 | 오디오 파트 미착수 |

### 1.3 일정에 대한 주의

[`../04-architecture.md`](../04-architecture.md) 8.3절은 **연출을 집중개발 4~5주차 착수**로 잡고 있고, 지금(2026-09-03)은 프로토타입 기간(08.30–09.19)이다.
이 문서는 그때 바로 착수할 수 있게 미리 정리해 두는 것이며, 지금 구현하자는 제안이 아니다.

`CombatFeedbackLocator`가 구현체 없이도 내부 `NullCombatFeedback`으로 돌기 때문에 미착수 상태로 두어도 전투 코드는 그대로 돈다.

---

## 2. 권위 경계

검기는 **전부 클라이언트 로컬 표현**이다. 서버는 검기를 모른다.

| 항목 | 누가 | 근거 |
|---|---|---|
| 공격 수락, 피해 판정 | 서버 | 이미 `PlayerAttack.CmdRequestAttack` · `ServerUpdateState`가 한다 |
| 검기 재생 여부, 위치, 크기, 색 | 각 클라이언트 | 사람마다 달라도 게임 결과가 같다 |
| 검기가 맞았는지 | **아무도 판정하지 않는다** | 검기는 그림이다 |

### 2.1 지금 이 표의 마지막 줄은 빈칸이다

[`PLAYER_ATTACK_DESIGN.md`](PLAYER_ATTACK_DESIGN.md) 2장 권위 표의 마지막 행 "공격 시작 표현"은 **서버·클라이언트 양쪽 모두 "없음"**이고 근거로 7.2절(공격 표현이 전혀 없다)을 가리킨다.
즉 이 문서가 만드는 것은 이미 정해진 규칙을 구현하는 것이 아니라 **그 빈칸을 채우는 결정**이다.

채우는 방식은 권위를 옮기지 않는다. 판정은 그대로 서버에 있고, 서버가 **수락한 사실만** 관찰자에게 알린다.
새로 생기는 네트워크 트래픽은 `RpcAttackStarted` 하나뿐이다. 권위 경계 변경은 아니지만, `PlayerAttack`에 RPC를 추가하는 것은 동기화 대역폭에 걸리므로 [`../04-architecture.md`](../04-architecture.md) 7장 기준 팀장 인지 대상이다.

### 2.2 절대 하지 않는 것

- 검기 프리팹에 `NetworkIdentity`를 붙이는 것 ([`../04-architecture.md`](../04-architecture.md) 7장 `02.Prefabs/Art/`에 명시된 금지)
- `05.Art`의 클래스가 `NetworkBehaviour`를 상속하는 것 (같은 문서 4.5, "설계 위반")
- 검기 콜라이더로 피해를 주는 것
- Animation Event에서 피해나 투사체를 스폰하는 것 ([`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 8장에서 이미 기각)

---

## 3. 트리거 경로

### 3.1 지금 없는 것

검기를 붙이려면 두 가지가 먼저 있어야 하는데 **둘 다 없다.**

| 없는 것 | 현재 상태 |
|---|---|
| 공격 애니메이션 | `PlayerAnimationController`의 공개 API는 `PlayRoll`과 `RefreshAnimator` 둘뿐이다. 공격 트리거가 없다 |
| 공격을 모든 클라이언트에 알리는 이벤트 | `02.Combat` 전체에 `ClientRpc`가 **0개**다. 지금 상태로 붙이면 **아무에게도 안 보인다** |

두 번째가 특히 중요하다. 구르기는 `PlayerRollController._isRolling` SyncVar 훅이 `_animation.PlayRoll()`을 불러 관찰자에게 전파되지만, 공격에는 대응물이 없다.

### 3.2 만들 것

```
[서버] PlayerAttack.CmdRequestAttack 수락 (스태미나 차감 직후)
          ↓
       RpcAttackStarted(...)            ← 신규, 관찰자 전원
          ↓
[각 클라] PlayerAnimationController.PlayAttack(...)
          ↓
       공격 clip 재생 → Animation Event → 검기 스폰
```

- 이름은 `RpcAttackStarted`로 쓴다. [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 3.3이 이 이름으로 `PlayerAttack`을 RPC 소유자로 계획해 두었다.
- **페이로드는 1단계에서 최소로 둔다**(무기 종류 정도). 애니메이션 파이프라인은 최종적으로 `AnimationActionCue`(actor, request, sequence, action, 시작 시각, 판정 duration) 값 계약으로 통일할 계획이므로, 검기 때문에 지금 그 구조를 앞당겨 만들지 않는다. **다만 cue가 들어오는 순간 검기 전용 페이로드는 흡수하고 지운다 — 전파 경로를 둘로 두지 않는다.**
- RPC는 **서버가 공격을 수락한 직후**(`_status.TrySpendStamina` 통과 뒤, `_isSwinging = true` 시점) 보낸다. 거부된 요청에 검기가 나가면 유령 공격으로 보인다.
- 소유 클라이언트 예측 재생은 [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 6장 2단계 완료 정의("소유자는 즉시 검 공격을 보고, 관찰자 둘은 같은 서버 공격을 한 번씩 본다")가 목표로 잡고 있다. 다만 서버가 거부하면 되돌려야 하므로 **이 문서의 1단계에서는 예측을 넣지 않는다.** 왕복 지연만큼 늦게 나오는 편이 유령 공격보다 낫다 — 9장 보류.

### 3.3 왜 `ICombatFeedback`에 넣지 않나

[`ICombatFeedback`](../../Assets/01.Scripts/00.Common/Interfaces/ICombatFeedback.cs)에는 `PlayHit`·`PlayParrySuccess`·`PlayCritical` 셋뿐이고 **"휘두르기 시작"에 해당하는 것이 없다.**

`PlaySwing`을 추가하는 안을 검토했으나 기각한다.

- `ICombatFeedback`은 `00.Common` 인터페이스라 넓히면 모든 구현체와 파트가 영향을 받는다
- 셋은 전부 **결과가 확정된 뒤**의 표현이다(명중, 패링 성공, 치명타). 검기는 결과가 아니라 **동작 시작** 표현이라 성격이 다르다
- [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 6장이 "무기 trail on/off, 발사 VFX 같은 표현 전용 callback"을 Animation Event에 허용하고 있다. 검기는 정확히 그 범주다

따라서 검기는 `PlayerAnimationController` 경로에 붙이고, `ICombatFeedback`은 명중·치명타 연출을 붙일 때 그대로 쓴다.

### 3.4 공격 clip이 없는 동안

clip이 들어오기 전에는 Animation Event를 걸 곳이 없다. 임시로 RPC 수신 시점에 바로 스폰한다.

- 판정은 선딜 `MeleeStartupDuration`(현재 0.12초) 뒤에 일어난다. 검기 재생도 **같은 값을 `GameDB.Combat`에서 읽어** 정점이 그 시점에 오게 맞춘다
- clip이 들어오면 Animation Event로 옮기고 이 임시 경로는 지운다. **두 경로를 동시에 두지 않는다** — 검기가 두 번 나온다

### 3.5 수락 뒤 취소되는 경로가 있다

`ServerUpdateState`는 선딜 중 `_status.IsDead`이거나 런이 멈추면 판정 없이 `Cancel()`한다.
RPC는 수락 직후에 이미 나갔으므로 **이 경우 피해 없는 검기가 그려진다.**

1단계에서는 그대로 둔다. 죽는 순간의 헛스윙은 오히려 자연스럽고, 취소를 알리려면 RPC를 하나 더 만들어야 한다.
다만 **8.1의 L3 관찰 항목에 넣어 실제로 눈에 거슬리는지 확인한 뒤 판단한다.**

---

## 4. 에셋 제작

### 4.1 구성 — 세 겹

| 겹 | 만드는 법 | 없어도 되나 |
|---|---|---|
| 몸통 (어두운 초승달) | 휘어진 판 메시 + 그라디언트 | 필수 |
| 앞날 (흰 발광 띠) | 마스크 경계를 뽑아 HDR 색 | 필수. 이게 없으면 밋밋하다 |
| 부스러기 (파편) | 파티클 소수 | 선택 |

**앞날 발광은 지금 상태로는 나오지 않는다.** `Assets/Settings/DefaultVolumeProfile.asset`의 Bloom은 `active: 1`이지만 `intensity`가 **0**이고, `Main`·`Dev_Combat` 어느 씬에도 자체 Volume 프로파일이 없어 이 기본값이 그대로 적용된다.
HDR 색만 넣고 그대로 두면 그냥 흰색이다. 2단계 전에 Bloom intensity를 올릴지 결정해야 한다 — 화면 전체 톤을 바꾸는 변경이라 아트 방향 판단이 필요하다(10장).

### 4.2 메시

Blender에서 원호를 따라 휘어진 판을 만든다. 평면 Quad로도 되지만 두께감이 안 나온다.

**UV 방향이 이 작업에서 가장 많이 틀리는 곳이다.** 호를 따라가는 방향이 U(0→1)가 되게 펴야 한다. 어긋나면 검기가 휘두르는 방향이 아니라 엉뚱한 쪽에서 그려진다.

- 삼각형 200개 이하. 화면에 잠깐 뜨는 물건이라 더 쓸 이유가 없다
- 기준 반경 1m, 호 90도로 만들어 둔다. 런타임 스케일 계산이 단순해진다(5장)

### 4.3 셰이더

Shader Graph는 **이미 프로젝트에 들어와 있다.** `Packages/manifest.json`에는 직접 적혀 있지 않지만 URP 17.4.0의 의존 패키지로 `packages-lock.json`에 `com.unity.shadergraph 17.4.0`으로 잠겨 있다. 별도 패키지 추가가 필요 없다.
**VFX Graph(`com.unity.visualeffectgraph`)는 lock 파일에도 없다 — 설치되어 있지 않고, 이 작업에 필요하지도 않다.**

URP Unlit / Transparent / `Cull Off`(양면). 셰이더 자산은 `Assets/01.Scripts/05.Art/Shaders/`(현재 비어 있음)에 둔다.

| 프로퍼티 | 역할 |
|---|---|
| `_SweepProgress` (0→1) | U축 `smoothstep` 마스크. 휘두르는 방향으로 **그려지듯** 나타난다 |
| `_NoiseTex`, `_ErosionAmount` | 가장자리를 갉아 너덜너덜하게 |
| `_EdgeColor` (HDR), `_EdgeWidth` | 마스크 경계 근처만 뽑아 앞날 발광 |
| `_BodyGradient` | 몸통 색과 알파 |

`_SweepProgress`를 통째로 0/1로 켜고 끄면 싸구려로 보인다. **쓸어 그리는 것이 이 이펙트의 전부다.**

지면에 파묻히면 살짝 띄우거나 soft particle 처리한다.

### 4.4 더 빠른 길 — 플립북

슬래시 스프라이트 시트를 Quad에 UV 애니메이션으로 돌리는 방식. 퀄리티는 떨어지지만 훨씬 빠르다.
**프로토타입에는 이쪽을 권한다.**

**`Assets/98.FreeAssets/`에는 쓸 수 있는 슬래시 시트가 없다.** 지금 들어 있는 것은 `Human Archer Animations` 하나뿐이다. 새로 소싱해야 하고, 취득하면 [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 8장 기준대로 출처·URL·취득일·라이선스 유형·사용 경로를 `docs/licenses/THIRD_PARTY_ASSETS.md`에 기록한다. 구매 에셋이면 `98.FreeAssets`가 아니라 `99.ThirdParty`(커밋 제외)다.

### 4.5 Trail Renderer는 대안이 아니다

칼끝 궤적용 보조다. 두꺼운 초승달은 나오지 않는다. 검기와 함께 쓰면 좋지만 검기를 대체하지는 못한다.

---

## 5. 판정과 크기를 맞추는 것

**이 문서에서 가장 중요한 절이다.**

서버 근접 판정은 `MeleeRange`(현재 2.2m) 거리, `MeleeAngle`(현재 90도) 정면 부채꼴이다.
`MeleeAngle`은 `CombatConfigSO` 주석대로 **정면 기준 좌우 합산 각도**다. 90도는 좌우 ±45도이지 ±90도가 아니다. 여기를 잘못 읽으면 검기가 판정의 두 배로 그려진다.

검기 이펙트가 판정보다 크면 "분명 닿았는데 안 맞았다"가 되고, 작으면 판정이 후한 것이 안 보인다.

### 5.1 규칙

검기 프리팹의 스케일과 각도를 **인스펙터에 손으로 넣지 않는다.** 스폰할 때 `GameDB.Combat`에서 읽어 맞춘다.

```
스케일  = MeleeRange × 시각 계수 ÷ 메시 기준 반경(1m)
호 각도 = MeleeAngle   (좌우 합산 그대로)
```

### 5.2 왜 이렇게 하나

[`BALANCE_DATA_WORKFLOW.md`](BALANCE_DATA_WORKFLOW.md)의 원칙 그대로다. 10월 밸런싱에서 `MeleeRange`를 2.2 → 2.6으로 바꾸는 일이 반드시 생기는데, 그때 이펙트 프리팹을 따로 고치는 것을 기억할 사람이 없다. **한 번 어긋나면 그 뒤로 계속 어긋난 채로 간다.**

### 5.3 완전히 일치시키지는 않는다 — 시각 계수는 어디 두나

시각적으로는 판정보다 **살짝 작게** 그리는 편이 낫다. 이펙트가 판정보다 크면 빗나감이 눈에 띄고, 작으면 "생각보다 잘 맞네"가 된다. 후자가 체감이 좋다. 시작값 0.9를 제안한다.

**이 계수를 `CombatConfigSO`에 넣지 않는다.** `CombatConfigSO`는 서버가 판정에 읽는 수치 원본이고, 여기에 표현값을 섞으면 밸런스 담당이 이펙트 값까지 만지게 된다.
[`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 3.3이 `AnimationCatalogSO`를 "Controller, 무기 시각 prefab, **표현 조정값**" 소유자로 규정했으므로 그쪽이 자리다. 다만 `AnimationCatalogSO`는 아직 없다 — 2단계에서는 검기 프리팹의 `[SerializeField]`에 두고, `AnimationCatalogSO`가 생기면 옮긴다.

---

## 6. 성능

호스트 프레임 저하가 이미 추적 중인 문제다. 검기는 3인이 계속 휘두르는 물건이라 여기서 새 부하를 만들면 안 된다.

| 항목 | 규칙 |
|---|---|
| 생성 | **풀링 필수.** 공격마다 `Instantiate`/`Destroy`하면 GC가 튄다 |
| 동시 상한 | 플레이어당 3개. 넘으면 가장 오래된 것을 재사용한다 |
| 수명 | 0.25~0.3초. 길면 화면이 지저분해지고 겹친다 |
| 드로우콜 | 재질 하나를 3인이 공유한다. 무기별 색은 머티리얼 프로퍼티 블록으로 |
| 그림자 | 끈다. 이펙트가 그림자를 만들 이유가 없다 |
| 라이팅 | Unlit. 시간대(`DayCycleController`)에 따라 검기 밝기가 변하면 안 된다 |

측정은 [`../ai/03_VERIFICATION_PLAYBOOK.md`](../ai/03_VERIFICATION_PLAYBOOK.md)의 Profiler 절차를 따르고, **3인이 동시에 휘두르는 상황**을 기준으로 잡는다.

---

## 7. 단계별 구현 순서

각 단계가 끝날 때마다 눈으로 확인할 수 있는 상태가 된다.

### 1단계 — 전파 경로 (코드만, 에셋 불필요)

`PlayerAttack`에 `RpcAttackStarted`를 추가하고, `PlayerAnimationController`에 `PlayAttack(...)`을 추가한다. 내용은 `Debug.Log` 한 줄이어도 된다.

**여기까지 하면** 3인 접속에서 남이 휘두른 것이 내 화면 로그에 찍힌다. 검기 없이도 전파가 되는지 먼저 확인된다.

### 2단계 — 임시 이펙트

플립북이나 소싱한 슬래시 시트를 Quad에 얹어 스폰한다. 5장의 크기 유도 규칙을 여기서부터 지킨다.
프리팹은 `Assets/02.Prefabs/Art/`에 둔다 — `NetworkIdentity` 없음.

**여기까지 하면** 판정 범위와 눈에 보이는 것이 맞는지 판단할 수 있다. 모양이 임시라도 크기 감각은 이때 잡힌다.

### 3단계 — 본 셰이더와 메시

4.2~4.3의 메시와 Shader Graph를 만들어 교체한다. 스폰 코드는 그대로 두고 프리팹만 바꾼다.

### 4단계 — 공격 clip 연결

공격 애니메이션이 들어오면 Animation Event로 트리거를 옮기고 3.4의 임시 경로를 **지운다.**
`PlayerAnimationController`는 `RefreshAnimator()`로 활성 모델의 Animator를 다시 캐싱하므로, 검기 부착 지점도 모델 교체(`PlayerCharacter`)에 따라 다시 잡혀야 한다.

### 5단계 — 무기별 확장

화살 발사 이펙트를 같은 틀로 만든다. **방울은 대상이 아니다** — `WeaponKind.ToReach()`가 `None`을 돌려주고 `PlayerAttack`이 요청 자체를 받지 않으므로, 방울 연출은 치유·버프 설계가 끝난 뒤 별도 작업이다.

### 선행 조건

| 조건 | 상태 |
|---|---|
| 공격 애니메이션 clip | **없음.** 4단계의 선행. 1~3단계는 clip 없이 진행 가능 |
| Bloom | **확인 완료 — 지금은 꺼진 것과 같다.** `DefaultVolumeProfile`의 Bloom intensity가 0이고 씬 오버라이드가 없다(4.1). 앞날 발광은 이 값 결정에 달려 있다 |
| 무기 손 위치 보정 | **미조정.** `Player.prefab → PlayerWeaponVisual → _bindings` 3개가 전부 0이라 무기가 몸통에 파묻힌다([`PLAYER_ATTACK_DESIGN.md`](PLAYER_ATTACK_DESIGN.md) 7.3). 검기 부착 위치를 잡을 수 없어 2단계 전에 필요 |
| `Player.prefab` 재저장 주의 | 이 프리팹은 아직 옛 필드명(`Appearance: 0/1/2`)으로 저장돼 있고 `[FormerlySerializedAs("Appearance")]`가 읽어 주고 있다([`PLAYER_ATTACK_DESIGN.md`](PLAYER_ATTACK_DESIGN.md) 7.5). **손 위치 보정으로 프리팹을 저장하면 새 필드명으로 다시 써지므로, 저장한 사실을 팀에 알리고 어트리뷰트 제거 시점을 그때 판단한다.** 씬·프리팹은 머지가 사실상 불가능하니 다른 사람과 동시에 건드리지 않는다 |

---

## 8. 검증

| 수준 | 무엇 |
|---|---|
| L1 | 컴파일 에러 0, 신규 경고 0 |
| L2 | `Dev_Combat` 단일 호스트 — 검기가 스윙 방향으로 그려지는가, 0.3초 안에 사라지는가, 판정 범위와 크기가 맞는가 |
| **L3** | **3인 접속 — 남의 검기가 내 화면에 보이는가.** `ClientRpc`가 걸린 변경이라 예외 없이 필요 → [`../ai/04_MULTIPLAYER_TEST_PROTOCOL.md`](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md) |

### 8.1 L3에서 특히 볼 것

- 거부된 공격(스태미나 부족, 구르기·패링 중, 후딜 중)에 검기가 나가지 않는가 — 유령 공격
- **수락 뒤 취소된 공격**(선딜 중 사망, 런 중단)의 헛스윙이 거슬리는가 — 3.5
- 셋이 동시에 휘두를 때 프레임이 떨어지지 않는가 — 6장 상한이 먹는가
- 지연이 있는 클라이언트에서 검기가 모션과 어긋나지 않는가

### 8.2 시각 결과는 캡처 없이 통과로 적지 않는다

이 문서의 산출물은 대부분 눈으로 판단하는 것이다. 캡처나 사용자 확인이 없으면 `미검증`으로 남긴다.

---

## 9. 대안과 기각 근거

### 채택: 메시 + Shader Graph

궤적이 정확하고, 크기를 코드로 유도할 수 있고, Bloom과 잘 맞는다. 드로우콜 하나. 패키지 추가도 필요 없다(4.3).

### 기각: 파티클 시스템으로 초승달 구성

파티클을 호 모양으로 배치해 흉내낼 수는 있으나 두께와 앞날 발광이 나오지 않고, 파티클 수가 늘어 호스트 부하가 커진다.

### 기각: VFX Graph 도입

패키지 추가는 팀 전체에 영향이 가는 변경이고, 이 이펙트에 GPU 파티클이 필요하지 않다. Shader Graph만으로 충분하다.

### 기각: 검기 전용 새 RPC 이름

`RpcOnAttackStarted` 같은 다른 이름을 새로 만들면 [`ANIMATION_PIPELINE_DESIGN.md`](ANIMATION_PIPELINE_DESIGN.md) 3.3의 `RpcAttackStarted`와 나중에 겹친다. 이름을 맞춰 두면 애니메이션 파이프라인이 그대로 이어받는다.

### 기각: `ICombatFeedback`에 `PlaySwing` 추가

3.3에 근거를 적었다. 결과 표현과 동작 시작 표현은 성격이 다르고, `00.Common` 인터페이스를 넓히는 비용이 크다.

### 기각: 시각 계수를 `CombatConfigSO`에 추가

5.3. 판정 수치 SO에 표현값을 섞지 않는다.

### 기각: 검기 콜라이더로 판정

이 프로젝트에서 가장 하면 안 되는 것이다. 클라이언트마다 이펙트 위치가 미세하게 달라 사람마다 다른 적이 죽는다.

### 보류: 소유자 예측 재생

체감은 좋아지지만 서버 거부 시 되돌리는 처리가 붙는다. 1단계에서는 넣지 않고, 3인 테스트에서 지연이 실제로 거슬릴 때 다시 판단한다.

---

## 10. 미결정

| 항목 | 정해야 할 사람 |
|---|---|
| **Bloom intensity를 올릴 것인가** — 화면 전체 톤이 바뀐다. 안 올리면 앞날 발광이 없다 | 아트 방향, 팀장 |
| 검기 색 — 세계관상 차사의 검이 어떤 빛을 내는가 | 아트 방향, 팀장 |
| 치명타 때 검기를 바꿀 것인가, 별도 이펙트를 얹을 것인가 | 팀장 |
| 5.3의 시각 계수 시작값과 최종 소유 SO(`AnimationCatalogSO` 신설 시점) | 플레이 테스트, 팀장 |
| 슬래시 소스를 새로 소싱할 것인가 직접 만들 것인가 (`98.FreeAssets`에 없음) | 팀장 |
| 3.5의 취소 헛스윙을 그대로 둘 것인가 | L3 관찰 후 판단 |
