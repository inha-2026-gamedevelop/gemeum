# 플레이어 VFX 목록과 자동 프리워밍

## 참고 구현

`D:/TheRemnantCodex`의 `Assets/01.Scripts/Flow/GameBootstrap.cs`와 `Assets/01.Scripts/Editor/BootPrewarmConfigurator.cs`를 참고했다. 해당 구현은 오브젝트 풀 사전 생성, 선택적 `ShaderVariantCollection.WarmUp`, 카메라 앞 실제 렌더 순서로 준비한 뒤 게임 흐름을 진행한다. 풀 개체 생성만으로 첫 렌더 작업까지 준비되지는 않으므로 렌더 단계를 별도로 둔다.

참고 구현의 `PlayParticles`는 ParticleSystem만 처리한다. VFX Graph는 별도 초기화·시뮬레이션·정지 처리가 필요하다. `VisualEffect.Reinit()`은 지정된 초기 이벤트도 발송하므로 재생 직후 `Play()`를 중복 호출하지 않는다. [Unity 초기 이벤트 API](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/vfx/visualeffect/initialeventname)

URP의 별도 렌더 출력은 RenderTexture와 렌더 요청을 사용한다. 화면 밖에 배치하고 렌더하지 않는 방식으로 준비를 완료했다고 처리하지 않는다. [Unity URP 렌더 요청](https://docs.unity3d.com/ja/current/Manual/urp/User-Render-Requests.html)

`Assets/02.Prefabs/Network/Player.prefab` 루트의 `PlayerVfxController`에서 캐릭터 VFX를 관리한다. Boot의 `PresentationPoolPrewarmer`가 `NetworkManager.playerPrefab` 안의 컨트롤러 목록을 읽어 접속 전에 프리워밍한다. 별도 VFX 목록을 중복 작성할 필요가 없다. 다른 캐릭터 프리팹도 사용할 경우 프리워머의 `Additional Player Prefabs`에 해당 캐릭터를 등록한다.

## Inspector 설정

| 필드 | 용도 |
|---|---|
| Key | 재생·조회할 고유 이름. 대소문자를 구분한다. |
| Prefab | 루트에 `LocalPoolRoot`와 `PooledVisualLifetime`이 있는 로컬 VFX 프리팹 |
| Initial Capacity | 미리 확보할 생성 개수. 이미 대여 중인 개체도 포함한다. |
| Max Size | 풀이 보관할 비활성 개체 상한. 동시 재생 수 제한이 아니다. |
| Anchor | `Play`로 재생한 VFX가 따라갈 Transform. 비워 두면 월드에 남는다. |

1. Player 프리팹을 열고 `Player Vfx Controller > Entries`에 항목을 추가한다.
2. 중복되지 않는 Key와 VFX 프리팹, 프리워밍 개수를 지정한다.
3. 손·무기 등을 따라가야 하는 일반 VFX라면 Anchor를 지정한다.
4. 프리팹을 저장한다. 다음 Boot 실행 때 목록을 읽어 자동으로 풀과 렌더를 준비한다.

원본 외부 에셋은 수정하지 않고 프로젝트 소유의 래퍼 프리팹을 만든다. 래퍼 루트에 `PooledVisualLifetime`을 추가하면 `LocalPoolRoot`도 함께 추가된다. 자식에 ParticleSystem/TrailRenderer/VisualEffect 효과를 넣고 `Lifetime Seconds`로 자동 반납 시간을 설정한다. VFX Graph는 `VisualEffect.initialEventName`에 실제 시작 이벤트를 지정한다. 예를 들어 `BossScratch.vfx`는 `SlashA` 또는 `SlashB`를 사용한다. 초기 이벤트가 효과를 발생시키지 않으면 렌더 요청을 해도 해당 출력의 셰이더가 준비된다고 보장할 수 없다.

비어 있는 키, 중복 키, 프리팹 누락, 잘못된 용량, 필수 컴포넌트 누락은 오류다. Boot 수집에서 오류가 있으면 접속 게이트를 실패시킨다. 빈 목록 자체는 허용한다. 실행 중 Inspector 변경은 다음 비활성화·활성화 때 반영되지만 Boot GPU 프리워밍을 다시 실행하지는 않는다.

## 기본 전투 연출 연결

기본 Player 프리팹에는 다음 5종을 등록한다.

| Key | 프리팹 | 초기 개수 / 보관 상한 | 기존 재생 경로 |
|---|---|---|---|
| `melee_slash` | `Player_MeleeSlash` | 3 / 18 | 검 일반 공격 cue |
| `bell_pulse` | `Player_BellPulse` | 3 / 9 | 등록·프리워밍 및 명시적 Play용 |
| `heal_cast` | `Player_HealCast` | 3 / 9 | 치유 cue |
| `parry` | `Player_Parry` | 3 / 9 | 패링 시작 cue |
| `mark` | `Player_MarkApplied` | 6 / 18 | 표식 cue |

`PlayerCombatPresentation`은 검기·치유·패링·표식 프리팹을 컨트롤러 목록에서 우선 조회한다. 이 키의 Prefab을 바꾸면 기존 전투 cue도 교체한 프리팹을 사용한다. 등록하지 않은 키는 `GameDB.Presentation`의 기존 설정을 사용한다. 키를 삭제하는 것만으로 기본 효과를 끄지는 않는다.

기존 전투 cue는 종전의 위치·부착 규칙과 대여 수명을 유지한다. 이 경로의 Anchor 설정은 적용하지 않는다. 검기에는 `IActionCueVisual`과 검날 추적 동작을 갖춘 프리팹이 필요하다. 검기 프리팹을 일반 `Play`만으로 호출하면 공격 cue가 전달되지 않는다.

방울 일반 공격, 활 충전 등 코드로 그리는 능력 연출은 기존 `PlayerCombatPresentation` 동작을 유지한다. `bell_pulse`를 등록했다고 방울 일반 공격 연출이 자동으로 교체되지는 않는다.

## 코드에서 재생

```csharp
// 초기화 시 PlayerVfxController 참조를 캐싱한 뒤 사용한다.
vfxController.Play("heal_cast");
vfxController.Play("mark", hitPosition, Quaternion.identity);
vfxController.Stop("heal_cast");
vfxController.StopAll();
```

`Play(key)`는 Anchor가 있으면 해당 위치, 없으면 캐릭터 위치에서 재생한다. 월드 위치를 전달하는 오버로드도 Anchor가 지정되어 있으면 그 아래에 붙인다. 여러 번 재생한 같은 키는 `Stop(key)`로 함께 정지한다.

이 API는 로컬 표현만 재생한다. 네트워크 메시지·피해 판정을 만들지 않는다. 다른 클라이언트에서도 보여야 하는 효과는 기존 서버 확정 cue를 수신하는 표현 경로에서 호출한다.

## 풀과 수명 규칙

- 같은 프리팹을 여러 캐릭터·여러 키가 등록해도 풀 하나를 공유한다. 초기 개수는 캐릭터 수만큼 누적되지 않는다.
- 기존 공통 `PresentationPoolPrewarmer`와 캐릭터 컨트롤러는 모두 `EnsurePrewarmed`를 사용한다. 이미 만들어진 개체를 불필요하게 다시 만들지 않는다.
- Boot는 공통 5종과 캐릭터 목록을 프리팹별로 합치며 초기 개수와 보관 상한 각각의 최댓값을 사용한다. 이후 늦게 등록된 목록은 이미 생성된 풀의 보관 상한을 바꾸지 않으며, 초기 개수가 기존 상한보다 크면 오류를 반환한다.
- 전용 서버에서는 프리워밍·재생하지 않는다. 클라이언트에서는 로컬 플레이어와 원격 플레이어 모두 대상이다.
- 비활성화 시 초기화 대기를 취소하고, 컨트롤러가 대여한 효과만 정지한다. 자동 반납 후 다른 캐릭터가 재사용한 효과는 대여 세대 번호를 확인하여 건드리지 않는다.
- VFX Graph는 대여 때 `Reinit`으로 초기 이벤트를 한 번 보내고, 지연 재생은 `Simulate`로 진행한다. 반납 때 정지·초기화하며 재대여에 이전 pause 상태를 남기지 않는다.

## Boot 실행 순서

1. 공통 설정과 캐릭터 목록을 병합한다.
2. 프레임마다 풀 개체를 생성한다.
3. 지정된 `Shader Variant Collection`이 있으면 `WarmUp`한다.
4. 각 프리팹의 대표 개체를 대여하고 격리된 카메라의 RenderTexture에 여러 프레임 렌더한다. ParticleSystem과 Graph는 시뮬레이션도 진행한다. 공격 cue로 메시를 만드는 기본 검기는 임시 메시로 기존 소재를 렌더하고 원래 상태를 복구한다.
5. GPU readback 완료를 확인하고 개체를 반납한다. 카메라·RenderTexture와 임시 레이어 변경을 정리한다.
6. 완료 후 자동 접속·개발 호스트·Steam 접속과 NetworkManagerHUD를 허용한다. 실패 시 접속하지 않고 오류를 기록한다.

전용 서버와 그래픽 장치가 없는 실행은 시각 리소스 준비를 생략한다. Boot 없이 Dev 씬을 실행하는 경로는 기다리지 않으며, 캐릭터 활성화 시 풀 사전 생성이 보완한다.

NetworkManager의 `Run In Background`가 켜져 있으면 Boot 시작부터 그 설정을 적용한다. 창이 포커스를 잃어 프리워밍이 멈춘 채 접속을 기다리는 상태를 방지한다. 프로젝트 전역 설정은 변경하지 않는다.

완료 로그의 `prefabs/instances`는 풀 계획, `cooked`는 렌더·readback 완료 프리팹 수, `graphs`는 활성 Graph 컴포넌트 수, `pixels`는 readback의 비어 있지 않은 픽셀 합계다. 개별 프리팹의 픽셀 수도 기록하며 0이면 이벤트·출력 지연·카메라 범위를 점검할 경고를 남긴다. 이 값은 모든 셰이더 변형, 다른 시작 이벤트, 모든 그래픽 드라이버에서 첫 사용 지연이 완전히 제거됐다는 뜻은 아니다. Graph의 비활성화 후 GPU 메모리 보존도 엔진 동작에 따라 달라진다.

## 검증 상태

2026-09-23: 실제 Unity Boot에서 기본 5종과 임시 Graph를 포함해 `prefabs=6 instances=19 cooked=6 graphs=1 pixels=1101` 완료를 확인했다. 기본 5종 각각과 Graph 모두 픽셀이 출력됐다. 임시 Graph는 실제 `BossScratch.vfx`와 초기 이벤트 `SlashA`를 사용했으며 씬에 저장하지 않았다. 별도 격리 Graph 렌더에서도 45픽셀·435입자, 일시정지·정지·풀 재사용을 확인했다. 기본 Player 목록에는 Graph가 없으므로 두 구성을 구분해서 검증한다.

Controller·Boot 게이트·Graph 수명·기존 표현·풀에 관한 EditMode 테스트 36개가 통과했다. 픽셀 검사는 출력 유무 검증이며 연출의 시각적 품질이나 모든 셰이더 변형의 준비를 검증한 것은 아니다.

Boot를 첫 씬으로 한 Windows 개발 빌드에서 직접 KCP 호스트+클라이언트 2명(M2)이 접속했다. 세 프로세스 모두 기본 5종 18개와 렌더를 완료한 뒤 네트워크를 시작했다. 계정 릴레이·Steam 접속 및 다른 PC M3는 이번 실행 범위가 아니다.

빌드 결과는 Succeeded였지만 외부 WeatherMaker 물 테셀레이션 셰이더의 `UnityTessellationFactors` 오류 1건이 기록됐다. KCP 실행에서도 Steam 미초기화 로그가 있었으므로 전체 프로젝트 무오류 검증으로 해석하지 않는다. 해당 외부 에셋과 Steam 초기화는 변경하지 않았다.
