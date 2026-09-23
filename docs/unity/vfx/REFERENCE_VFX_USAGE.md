# 활·흑심 빔 VFX 사용법

이 묶음은 완성된 프리팹, 메시, 재질, 텍스처, Shader Graph/VFX Graph와 실행에 필요한 런타임 스크립트만 제공한다. 제작용 빌더·영상 캡처·테스트 도구와 원본 영상은 필요하지 않다. 게임의 공격 판정·RPC·씬 배선은 이 에셋 묶음의 일부가 아니다.

## 포함 효과

| 효과 | 프리팹 | 런타임 |
|---|---|---|
| 활 충전 | `Assets/02.Prefabs/Art/VFX/Player_BowCharge.prefab` | `BowChargeVfxView` |
| 짧은 화살 발사 | `Assets/02.Prefabs/Art/VFX/Player_BowLaserRelease.prefab` | `BowLaserVfxView` |
| 붉은 흑심 빔 | `Assets/02.Prefabs/Art/VFX/BeamRef/Beam_Reference.prefab` | `BeamReferenceVfxDriver` |

필요 환경: Unity 6000.4.7f1에서 제작한 URP 프로젝트, Shader Graph, VFX Graph. 다른 Unity/패키지 버전은 별도 호환 확인이 필요하다. `.meta`를 함께 가져와 GUID를 유지한다.

## 활 충전·발사

활은 **외부 재생 시계로 `Evaluate`를 호출**하는 방식이다. 프리팹을 생성하는 것만으로 발사 애니메이션이 자동 진행되지는 않는다.

1. 충전 프리팹을 손 또는 화살 받침 위치에 생성한다.
2. 충전 중 매 프레임 `BowChargeVfxView.Evaluate(progress, elapsed)`를 호출한다. progress는 0~1, elapsed는 충전 경과 초다.
3. 발사/취소 때 `ResetVisual()` 후 비활성화·반납한다.
4. 발사 프리팹의 위치를 화살 시작점에 두고, 로컬 **+Z가 발사 방향**을 향하도록 회전시킨다.
5. `SetVariation(seed)`를 한 번 호출하고 매 프레임 `Evaluate(elapsed, distance)`로 갱신한다. distance는 월드 단위의 표현 거리이며 호출자가 벽까지의 거리를 제한한다.
6. `elapsed >= Duration`일 때 초기화 후 반납하거나 파괴한다. 일시정지 중에는 elapsed를 증가시키지 않는다.

```csharp
// 기존 발사 제어기의 매 프레임 갱신에 넣는 핵심 호출 예시.
// origin, direction, seed, elapsed, distance는 호출자가 제공한다.
view.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
view.SetVariation(seed);
view.Evaluate(elapsed, Mathf.Min(distance, view.MaxDistance));
// 종료 또는 취소 시:
// view.ResetVisual();
// Destroy(view.gameObject); // 자체 풀이면 반납
```

기본 표현 속도30m/s, 꼬리 최대2.4m, 총 수명0.8초다. 앞끝과 꼬리가 함께 전진하며, 도착 뒤 가는 분기 잔선이 사라진다. 이 효과는 실제 화살 오브젝트를 추적하지 않는다. 실제 투사체 속도·유도 궤적과 자동으로 일치한다고 가정하지 않는다.

활 두 프리팹에는 저장소에 이미 존재하는 `LocalPoolRoot`와 `PooledVisualLifetime`도 붙어 있다. 위의 직접 생성/수동 갱신 예시는 풀 대여를 호출하지 않는다. 다른 프로젝트로 옮길 때에는 이 두 풀 컴포넌트를 **복제 프리팹에서 제거**하고 수명을 직접 관리하거나 해당 프로젝트의 풀 어댑터로 바꾼다. 원본 프리팹을 수정할 필요는 없다. `BowChargeVfxView`와 `BowLaserVfxView` 자체는 UnityEngine만 사용한다.

## 붉은 흑심 빔

기본 프리팹의 Play On Enable이 켜져 있어 활성화 시 충전부터 소멸까지 한 번 재생한다. 게임의 시전 시간에 연결하려면 Inspector에서 Play On Enable을 끄고 다음 API를 사용한다.

```csharp
driver.SetEndpoints(startWorld, endWorld);
driver.BeginCharge();
// 공격이 승인된 시점:
driver.Fire();
// 일시정지 / 재개:
driver.SetPaused(true);
driver.SetPaused(false);
// 취소 시 부드럽게 소멸:
driver.Cancel();
// 즉시 정리해야 하는 경우:
driver.ResetAll();
```

`PlayFull()`은 자동 전체 재생, `SetDurations()`는 충전·드러남·유지·소멸 시간을 조정한다. `Phase`, `TotalDuration`으로 상태를 읽는다. 효과 종료 후 오브젝트의 반납/파괴는 호출자가 담당한다. 빔 중심의 검은색은 의도된 디자인이다.

## 옮길 파일과 화면 설정

- 활: 두 프리팹, `BowLaser/` 의존 에셋, `Assets/12.Materials/Player/M_BowLaser*.mat`, 두 Bow View 스크립트.
- 빔: `BeamRef/` 프리팹·VFX·Shader Graph·메시, `Assets/03.Image/Monster/BeamRef/` 텍스처, `BeamReferenceVfxDriver.cs`.
- 활 Shader Graph가 참조하는 기존 `Assets/03.Image/Player/CommonNoise.png`도 포함한다.
- Unity에서 프리팹의 **Export Package → Include dependencies**로 참조를 포함할 수 있다. 다른 프로젝트에서는 프로젝트 전용 풀 스크립트가 불필요하게 함께 들어오지 않도록 복제 프리팹의 풀 컴포넌트를 먼저 정리한다.
- 카메라 HDR/Bloom 설정에 따라 밝기와 번짐이 달라진다. 패키지는 프로젝트 전역 카메라·Volume을 덮어쓰지 않는다.
- 네트워크 게임에서는 서버가 승인한 공격 이벤트를 받은 뒤 각 클라이언트에서 재생한다. 이 스크립트로 피해·사거리·명중을 판정하지 않는다.

## 확인 범위

활은 이 커밋 전 제작본에서 63개 이동 표본·9개 정리 검사 및 로컬 3개 게임 프로세스의 발사·정지·취소 확인을 수행했다. 이번 에셋 정리에서는 참조 의존성을 조회했으며 테스트/재빌드를 새로 실행하지 않았다. 소스 형식 정리 후 재검증과 다른 프로젝트로의 실제 이식은 미실행이다.

원본 영상과 같은 수준의 시각 품질을 달성했다고 보증하지 않는다. 활은 원본보다 짧게 만들라는 요청을 반영했으며 스파크 밀도·후미 밝기에 차이가 남는다. 흑심 빔도 원본 대비 품질 목표 미달 상태에서 해당 제작 라운드가 종료됐다.
