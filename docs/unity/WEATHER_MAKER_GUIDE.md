# Weather Maker 사용 가이드

> 이 문서는 담당자가 원본 매뉴얼을 처음부터 다 읽지 않고도 그믐 프로젝트 기준으로
> 바로 세팅·사용할 수 있도록 요약한 것이다. 값의 근거나 상세 파라미터가 더 필요하면
> 원본 문서를 연다: [Readme.txt](../../Assets/99.ThirdParty/Background/WeatherMaker/Readme.txt),
> [UserManual.md](../../Assets/99.ThirdParty/Background/WeatherMaker/UserManual.md)
> (전체 목차가 있으니 필요한 장만 찾아 읽으면 된다).

---

## 0. 소유권 먼저 확인

- 에셋 원본(`Assets/99.ThirdParty/Background/WeatherMaker/`)은 **수정 금지** — [씬·에셋 안전 §4](SCENE_AND_ASSET_SAFETY.md#4-구매-에셋) 참조. 설정은 프리팹 인스턴스와 프로필(ScriptableObject)로만 한다.
- 실제 세팅 작업이 `Assets/00.Scenes/Main.unity`(통합 씬)를 건드린다. Main.unity는 **통합 담당자만** 수정 가능 — 자기 파트 `Dev_*.unity`에서 먼저 검증하고, Main.unity 반영은 통합 담당자 승인 후 진행한다. 자세한 절차는 [씬·에셋 안전 §1](SCENE_AND_ASSET_SAFETY.md#1-씬-소유권).

---

## 1. 요구사항 (그믐 프로젝트 기준 — 이미 충족)

| 항목 | 그믐 값 | Weather Maker 요구 |
|---|---|---|
| Unity 버전 | 6.4.7f1 | URP 사용 시 6000 이상 |
| 렌더 파이프라인 | URP | URP 17.3 이상, **HDRP 미지원** |
| 그래픽 API | Windows | DirectX 12 권장 (URP 사용 시) |
| 플랫폼 | PC (Steam 출시 목표) | WebGL / OpenGL / OpenGL ES / DX9 **미지원** |

Rain Maker 등 경쟁 에셋이 프로젝트에 같이 있으면 충돌하니 제거한다 (그믐에는 해당 없음, 확인만).

---

## 2. 최초 활성화 (한 번만)

1. **Window → Weather Maker → Enable URP** 클릭.
2. [Demo/Scenes/DemoScene.unity](../../Assets/99.ThirdParty/Background/WeatherMaker/Demo/Scenes/DemoScene.unity)를 열어 Play — 하늘·구름·태양·달·기본 오디오가 보이면 정상.

이 단계는 씬을 건드리지 않으므로 승인 없이 진행 가능.

---

## 3. Main.unity 세팅 체크리스트 (승인 후 진행)

### 프리팹 배치
- [Prefab/WeatherMakerPrefab.prefab](../../Assets/99.ThirdParty/Background/WeatherMaker/Prefab/WeatherMakerPrefab.prefab)을 씬 **루트**에 배치한다. (2D 프로젝트면 `WeatherMakerPrefab2D`)
- 하위 오브젝트 중 다음은 **비활성화해도 되지만 삭제하면 안 된다**: `AudioManager`, `DayNightCycle`, `LightManager`, `SkySphere`, `FullScreenEffects/Clouds` — 이 다섯은 항상 활성 상태를 유지해야 한다.
- `IsPermanent`(WeatherMakerScript, 기본 true) — 그믐은 로그라이크라 런 중 씬 전환이 잦다. 런처/영속 씬 하나에만 넣고 `IsPermanent = true`(기본값) 유지를 권장. 씬마다 새로 넣으려면 `false`로 바꾼다.

### 카메라
- Main Camera → **Clear Flags: Solid Color**, 색상 `(0,0,0,0)`.
  - 대안: Skybox를 유지하려면 `FullScreenEffects/Clouds → Render Queue`를 After Skybox로 설정하고 SkySphere 렌더러를 끈다.
- Far Plane ≥ 10,000 (일반), ≥ 100,000 (높은 고도/장거리 시야가 필요하면).
- HDR 켜기, MainCamera 태그 확인.

### Lighting 창 (Window → Rendering → Lighting → Environment)
- Skybox Material → `WeatherMakerSkyboxMaterial`
- Sun Source → Weather Maker 프리팹의 Sun
- Environment Lighting Source → Color 또는 Gradient
- Environment Reflections Source → Custom

### 기타
- Graphics 설정에서 **Screen Space Shadows** 켜기 (구름 그림자, 눈 오버레이 그림자에 필요).
- 빌드 전 `WeatherMakerScript.ResourceContainer`를 확인 — 여기 들어있는 프로필/텍스처/사운드만 빌드에 포함된다. 안 쓰는 건 빼서 용량 절감.

---

## 4. 플레이어 오브젝트 요건

날씨 존 · 사운드 존 · 댐프닝 존 · 널 존은 전부 **트리거 콜라이더 진입**으로 동작한다.
플레이어의 `AudioListener`가 붙은 오브젝트(같은 GameObject)에 아래 세 가지가 반드시 있어야 한다:

1. 활성화된 **AudioListener** — Weather Maker가 이걸로 "로컬 플레이어"를 식별한다.
2. **Kinematic Rigidbody**
3. 반지름 `0.001`의 트리거 **Sphere Collider**

네트워크 환경(Mirror)에서 **로컬이 아닌 플레이어**는 카메라·AudioListener·SoundZone 스크립트를 **비활성화**한다(삭제 금지). 참고 스크립트: [WeatherMakerNetworkPlayerScript.cs](../../Assets/99.ThirdParty/Background/WeatherMaker/Prefab/Scripts/Other/WeatherMakerNetworkPlayerScript.cs), 예시 프리팹: [WeatherMakerPlayer.prefab](../../Assets/99.ThirdParty/Background/WeatherMaker/Prefab/WeatherMakerPlayer.prefab).

---

## 5. Mirror 네트워크 연동

`WeatherMakerMirrorNetworkScript`가 동기화하는 건 **시간대(TimeOfDay)와 글로벌 날씨 존**뿐이다. 그 외(번개, 세부 파티클 등)는 클라이언트마다 랜덤 — [네트워크 권위](NETWORK_AUTHORITY.md) 기준으로도 판정에 안 쓰는 순수 표현이라 문제없다.

1. 씬 루트에 빈 GameObject를 만들고 `WeatherMakerMirrorNetworkScript` **하나만** 붙인다 (다른 컴포넌트와 같이 두지 않는다).
2. 헤드리스 서버 빌드 시 `UNITY_SERVER` 스크립팅 심볼을 정의한다 — 서버에서 렌더링용 커맨드 버퍼 실행을 건너뛰게 한다.
3. **주의**: 전 클라이언트가 전부 에디터거나 전부 빌드여야 한다. 에디터와 빌드를 섞으면 ScriptableObject 직렬화 차이로 동기화가 깨진다. [멀티플레이 테스트 절차](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md)로 검증할 때 이 조건을 지킬 것.
4. 참고 씬: [Demo/Scenes/DemoSceneMirror.unity](../../Assets/99.ThirdParty/Background/WeatherMaker/Demo/Scenes/DemoSceneMirror.unity)

---

## 6. 날씨를 바꾸는 법 (프로필 기반, 코드 수정 없음)

모든 기능이 ScriptableObject 프로필로 구성된다. 밸런스 수치와 마찬가지로 [밸런스 데이터 작업](BALANCE_DATA_WORKFLOW.md) 원칙과 동일하게 — **코드로 박지 않고 에셋으로 관리**한다.

- **새 프로필 만들기**: Project 뷰 우클릭 → Create → Weather Maker → (원하는 종류). 원본을 바로 고치지 말고 항상 **복제 후 편집**한다.
- **전역 날씨**: `WeatherMakerPrefab → GlobalWeatherZone`에 Weather Profile(또는 Group)을 할당.
- **구역별 날씨**: 콜라이더(Is Trigger) + `Component → Weather Maker → Weather Zone` 추가 후 프로필 할당.
- **실내/차폐 구역에서 비/눈 끄기**: [WeatherMakerNullZone.prefab](../../Assets/99.ThirdParty/Background/WeatherMaker/Prefab/WeatherMakerNullZone.prefab) 계열을 배치 (구·박스 콜라이더만 지원).
- **런타임 중 편집**: Play 모드에서 프로필을 인스펙터에 드래그 → 값 수정. `AutoCloneProfiles`가 켜져 있으면 원본 에셋은 안 건드리고 클론에만 반영된다.

---

## 7. 자주 쓰는 기능 빠른 참조

| 하고 싶은 것 | 어디를 본다 |
|---|---|
| 비/눈/우박/진눈깨비 강도 조절 | `Precipitation` 하위 프리팹의 Intensity, Base Emission Rate (UserManual §9) |
| 낮/밤 주기 속도, 위경도, 시각 | `DayNightCycle` 프로필 (UserManual §12) |
| 구름 모양·밀도 커스텀 | Cloud Profile + Weather Map 텍스처 (UserManual §13) |
| 번개 빈도·밝기 | `ThunderAndLightningPrefab` (UserManual §17) |
| 안개 커스텀 | Fog Profile, `WeatherMakerFullScreenFogScript.TransitionFogDensity()` (UserManual §18) |
| 실외 기상 API 연동 | `WeatherMakerLocationWeatherScript` + OpenWeatherMap API 키 (UserManual §28) |
| 빌드 용량 줄이기 | `ResourceContainer` 프로필 정리 (UserManual §27) |
| 성능이 안 나올 때 | Performance Profile 조정, Quality 레벨별로 하나씩 존재 (UserManual §24) |

---

## 8. 검증 기준

- Weather Maker 자체는 순수 표현(비주얼·오디오)이라 판정 로직이 없다 → 서버 권위 이슈 없음.
- 단, **날씨 존 배치나 Main.unity 변경은 씬 변경**이므로 [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md) 기준 최소 L2(담당 `Dev_*.unity`에서 1회 이상 관찰), Mirror 시간대 동기화를 건드렸다면 L3(3인 접속) 적용.
- 시각적 결과(하늘·구름 모양 등)는 캡처나 실제 확인 없이는 미검증으로 보고한다.

---

## 9. 트러블슈팅

### 화면이 완전히 검게 나온다

카메라 Clear Flags를 Solid Color(0,0,0,0)로 맞췄는데도(§3) 아무것도 안 그려진다면, **현재 프로젝트가 실제로 쓰는 URP Renderer 에셋에 `WeatherMakerURPRenderFeatureScript` 렌더러 피처가 없는 것**이다. URP 파이프라인/렌더러 에셋을 교체하거나 새로 만들면 이 피처가 자동으로 안 붙는다.

확인: `Project Settings → Graphics`에서 가리키는 Render Pipeline Asset을 연 다음, 그 Renderer Data(`m_RendererDataList`)를 열어 Renderer Features 목록에 Weather Maker 항목이 있는지 본다. 없으면 `+` 버튼으로 `Weather Maker URP Render Feature`를 추가한다(구름 그림자가 필요하면 `Screen Space Shadows`도 같이).

### `Window → Weather Maker → Enable Universal Render Pipeline` 메뉴를 누르지 말 것

이 메뉴는 프로젝트의 렌더 파이프라인을 **Weather Maker 자체 번들 파이프라인(`WeatherMakerURPProfile`)으로 통째로 바꿔치기**한다 — 위 문제의 정석 해법처럼 보이지만, 우리 프로젝트 고유 URP 에셋(현재 `PC_RPAsset`)의 설정(SSAO, 렌더링 모드 등)이 전부 무시된다.

더 나쁜 문제: 이 메뉴를 한 번이라도 누르면 EditorPrefs 플래그(`WeatherMaker_AllowAutoEnableURPOnce`)가 켜지는데, 이 플래그가 켜진 상태로는 `GraphicsSettings.defaultRenderPipeline`이 잠깐이라도 null이 되는 순간(Play 모드 진입/종료, 도메인 리로드 등)마다 `WeatherMakerScript.CheckURP()`가 **또다시 자동으로** 파이프라인을 Weather Maker 프로필로 바꿔버린다. 이 플래그는 **로컬 머신(레지스트리) 설정이라 git에 안 잡히고, 프로젝트를 다시 열어도 안 없어진다.**

실수로 눌렀다면:
1. `Project Settings → Graphics`와 `Quality`에서 Render Pipeline Asset을 다시 우리 프로젝트 것(`PC_RPAsset` 등)으로 되돌린다.
2. 아래 코드를 한 번 실행해 플래그를 꺼둔다 (Window → Weather Maker 메뉴가 아니라, Unity 콘솔/에디터 스크립트로):
   ```csharp
   UnityEditor.EditorPrefs.SetBool("WeatherMaker_AllowAutoEnableURPOnce", false);
   UnityEditor.EditorPrefs.SetBool("WeatherMaker_SuppressAutoEnableURP", true);
   ```
3. 렌더러 피처가 필요하면 위 "화면이 완전히 검게 나온다" 항목대로 수동으로만 추가한다.

### `GraphicsSettings`/`QualitySettings` 값을 스크립트로 바꿨는데 저장이 안 된다

`GraphicsSettings.defaultRenderPipeline = X` 같은 런타임 할당은 **에디터 세션 안에서는 바로 적용되지만, 디스크(`ProjectSettings/GraphicsSettings.asset`)에 저장된다는 보장이 없다.** 반드시 해당 오브젝트를 `EditorUtility.SetDirty(...)`로 표시하고 `AssetDatabase.SaveAssets()`를 호출한 다음, 파일을 다시 읽어(또는 `git diff`) 실제로 반영됐는지 확인할 것. "Screen Space Shadows 켜기"(§3)도 같은 이유로 반영이 잘 안 됐던 적이 있다.

### Play 모드인데 날씨·구름이 하나도 안 보인다

`WeatherMakerMirrorNetworkScript`(§5)가 씬에 있으면, Weather Maker는 **서버(호스트)만** 날씨/시간을 적용하도록 게이트를 건다(`WeatherMakerWeatherZoneScript.UpdateProfile` 내부에서 `NetworkConnection.IsServer` 확인). 실제 Mirror 호스트를 안 띄우고 혼자 Play만 누르면 이 조건이 항상 거짓이라 아무것도 안 그려진다 — **버그가 아니라 의도된 동작**이다.

혼자 빠르게 확인하고 싶으면 그 오브젝트를 잠깐 비활성화하고 테스트한 뒤, **반드시 다시 활성화하고 씬을 저장했는지 `git status`로 확인**한다. 진짜 검증(L3)은 [멀티플레이 테스트 절차](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md)대로 호스트를 띄워서 한다.

### 화면 전체(하늘·지형 구분 없이)에 자잘한 그레인/노이즈가 낀다

Main.unity Play 화면 전체에 균일한 speckle 노이즈가 낀다는 보고가 있었다(2026-08-30).
원인 후보를 실측으로 좁힌 기록:

- **아니었던 것**: `_WeatherMakerBlueNoiseTexture`/`_WeatherMakerDitherTexture` 참조 누락 — 텍스처는 정상 연결돼 있음. `UNITY_URP` 스크립팅 심볼 누락 — `ProjectSettings.asset`에 정상 정의돼 있어 `WeatherMakerCommandBufferManagerScript.URP.cs`(RenderPipelineManager 콜백 기반)가 정상적으로 컴파일됨.
- **테스트했지만 결론 안 남**: `VolumetricCloudTemporalReprojectionSize`를 `None(0)`으로 꺼서 시간적 리프로젝션을 완전히 우회해봤다 — 그레인은 거의 그대로였고, 대신 프레임 타임이 수백~수천 ms로 폭락했다(GTX 1060 기준 실사용 불가). 즉 리프로젝션이 원인이라는 가설은 이 테스트로 반증됐다.
- **의심되는 것(미확정)**: `Assets/09.Data/Environment/WM_PerformanceProfile_Geumeum.asset`을 `_Simple` 프리셋에서 복제할 때 `VolumetricCloudRayDither`가 원본 `_Default`(Min 0.01/Max 0.03)보다 훨씬 공격적인 값(Min 0.15/Max 1)으로 따라왔다 — 이 값을 다시 `_Default` 수준으로 낮춰봤지만, 편집 → refresh → Play 재진입을 반복하는 동안 프레임 타임 표시가 계속 비정상(수천 ms)으로 나와 셰이더 재컴파일 스파이크와 실제 정상 상태를 구분하지 못했다. 즉 이 수정이 실제로 그레인을 줄였는지 **확인하지 못한 채** 값만 되돌려 둔 상태다.

- **시도했지만 명확히 개선되지 않음**: UserManual §13.7("Temporal Reprojection")에 `_Blender`(1=크리스프하지만 밤에 고스팅/아티팩트 발생) · `_TemporalReprojection_BlendMode`(0=sharp, 1=blue noise·"artifact cleanup") 두 머티리얼 프로퍼티가 문서화돼 있다. 원본 `WeatherMakerTemporalReprojectionCloudsMaterial.mat`은 `_Blender=1`, `_TemporalReprojection_BlendMode=0`(sharp)로 세팅돼 있었는데, 그믐은 야간(새벽/그믐) 장면이 많아 매뉴얼이 경고하는 "밤에 고스팅" 조건과 정확히 겹친다. 구매 에셋 원본을 고치지 않고 `Assets/09.Data/Environment/WM_TemporalReprojectionCloudsMaterial_Geumeum.mat`을 만들어 `_Blender=0`, `_TemporalReprojection_BlendMode=1`(blue noise)로 바꾸고 `Main.unity`의 `WeatherMakerFullScreenCloudsScript.TemporalReprojectionMaterial`을 이걸로 재지정했다. 프레임 타임은 20ms(50FPS)로 정상 복귀했지만, 하늘의 speckle 노이즈는 육안상 뚜렷하게 줄지 않았다 — **이 조정도 근본 원인은 아니었을 가능성이 높다.**

**다음에 이 문제를 다시 볼 사람에게**: 원격 도구로 Play를 반복 진입시키며 스크린샷을 비교하는 방식은 매번 셰이더 워밍업 비용이 섞여 들어가 신뢰할 수 없었다. 에디터에 사람이 직접 붙어서 Play를 한 번만 켜고 수 초 이상 안정화를 기다린 뒤 눈으로 확인하는 게 가장 빠르다. `_Blender`/`BlendMode` 조정으로도 안 잡히면 다음을 시도할 것:
1. Frame Debugger에서 `WeatherMaker BeforeForwardOpaque`/`AfterImageEffectsOpaque` Blit 각각을 픽셀 단위로 열어 노이즈가 정확히 어느 패스(클라우드 raymarch vs 안개 vs 대기)에서 생기는지 특정한다.
2. WeatherMaker 지원 포럼에서 Unity 6 Render Graph 호환성 이슈(구버전 SRP 콜백 기반 임시 리프로젝션 버퍼가 Render Graph의 텍스처 풀링과 안 맞는 경우)를 검색한다.
3. 벤더에 8.1.0 이후 Unity 6 Render Graph 패치가 있는지 확인한다.

**격리 테스트 결과 (2026-08-30, 결정적)**: `Assets/00.Scenes/Dev_WeatherMakerIsolation.unity`를 새로 만들어
WeatherMaker 매뉴얼 §2~§3 순서 그대로 순정 `WeatherMakerPrefab` + 기본 Performance Profile만 넣고(그믐 커스텀
프로필도, Gaia도, Mirror도 전부 뺀 상태) Play해보니 **하늘이 완전히 매끈했다 — 노이즈가 전혀 없다.**

즉 **WeatherMaker 자체의 버그가 아니다.** 원인은 `Main.unity` 쪽 구성 — Gaia(지형·식생, `QuadTreeBatchNode.Render`로
렌더링됨), 우리가 만든 커스텀 Performance Profile/TemporalReprojectionMaterial, 또는 프로젝트의 URP Renderer
Feature 조합 중 하나(또는 여럿의 상호작용)에 있다. **개발자에게 "WeatherMaker 버그"로 문의하기 전에, 이 격리
씬에 Main.unity의 구성 요소(우리 커스텀 프로필 → Gaia 지형 → 다른 렌더러 피처 순으로)를 하나씩 추가해가며
어느 시점에 노이즈가 나타나는지 이분 탐색하는 게 먼저다.** `Dev_WeatherMakerIsolation.unity`는 Build Settings에
넣지 않은 순수 진단용 씬이다.

**격리 씬 + 낮밤 주기 테스트 (2026-08-30, Gaia보다 먼저 좁혀짐)**: 같은 격리 씬에 그믐이 쓰는
`WM_DayNightCycleProfile_Geumeum`(Speed 24)만 추가해서 `TimeOfDay`를 08:00 → 13:00 → 18:00 → 20:00 순서로
넘겨봤다.

| 시각 | 결과 |
|---|---|
| 08:00 (여명) | 노이즈 있음 |
| 13:00 (한낮) | **깨끗함** |
| 18:00 (늦은 오후, 아직 밝음) | **깨끗함** |
| 20:00 (황혼~밤) | 노이즈 있음 |

Gaia도 없는 상태에서 이미 재현됐다 — Gaia 문제는 아니다.

**정정 (2026-08-30)**: 위 표는 하늘(화면 위쪽)만 보고 판단한 것이었다. 13:00 스크린샷을 픽셀 단위로 다시
보니 화면 아래쪽(카메라 Clear Flags가 Solid Color(0,0,0,0)라 지형 없이 그냥 검게 비는 배경 영역)에도
노이즈가 있었다 — 하늘 영역(평균 밝기 174)은 표준편차 2.68로 거의 안 보이고, 바닥 빈 배경(평균 밝기 12)은
표준편차 6.58로 절대값도 크고 밝기 대비로는 훨씬 두드러졌다.

**즉 원인은 "시간대"가 아니라 "화면의 어두운 영역"이다.** 여명·황혼·밤은 화면 전체가 어두워서 노이즈가
다 보이고, 한낮은 하늘만 밝아서 하늘에서는 안 보였을 뿐 어두운 배경 영역에는 그때도 있었다. 디더링(또는
레이마치 노이즈)이 저휘도 영역에서 상대적으로 과도하게 드러나는, 훨씬 흔하고 특정하기 쉬운 유형의 문제로
좁혀졌다 — HDR/톤매핑이 어두운 값을 밀어올리며 절대적으로는 작은 디더 진폭을 상대적으로 크게 보이게
만드는 것으로 추정된다(이 프로젝트 격리 씬엔 Volume/톤맵도 없으니, 톤맵이 원인의 전부는 아니고 디더 자체가
저휘도에서 상대적으로 크게 설계돼 있을 가능성이 크다).

**`AutoSetPerformanceProfile` 콘솔 경고 확인 및 수정 (2026-08-30)**: Unity 콘솔에 다음 경고가 실제로 떴다.

> Weather Maker auto-performance profile expects between 3 to 6 quality settings, but you have 1.
> Please click stop and set a performance profile manually on WeatherMakerScript, and set
> 'Auto Set Performance Profile' to false.

확인해보니 실제 버그였다 — `Dev_WeatherMakerIsolation.unity`는 `WeatherMakerPrefab` 기본값을 그대로 썼는데
프리팹 기본값이 `AutoSetPerformanceProfile: 1`(true)이고, 그믐 프로젝트의 `ProjectSettings/QualitySettings.asset`엔
품질 등급이 2개뿐이다(권장 3~6개). 그 결과 런타임에 `WeatherMakerScript._PerformanceProfile`이 **`null`인 채로
방치**돼 있었다 — 격리 씬에서 지금까지 본 노이즈 중 일부는 프로필이 아예 적용 안 된 상태에서 나온 것일
수 있다.

`AutoSetPerformanceProfile = false`로 끄고 `_PerformanceProfile`을 `WeatherMakerPerformanceProfile_Default`로
수동 지정한 뒤 20:00(황혼)에서 다시 스크린샷을 찍었다 — **노이즈는 거의 그대로였다.** 즉 이 버그는 실존하고
고칠 가치는 있지만(콘솔 경고 자체를 없애야 하니), **그레인 노이즈의 원인은 아니었다.**

Main.unity 쪽은 `AutoSetPerformanceProfile`이 프리팹 오버라이드로 이미 `0`(false)로 꺼져 있고
`_PerformanceProfile`도 수동 지정돼 있어(9절 참조) 이 버그의 영향을 안 받는 것으로 보이지만, 실제 Play
중 런타임 값으로 재확인은 안 했다 — 다음에 볼 때 `WeatherMakerScript` 인스펙터에서 `Auto Set Performance
Profile` 체크박스가 꺼져 있고 `Performance Profile` 필드가 비어있지 않은지 눈으로 한 번 확인할 것.

**현재까지 종합**: 리프로젝션 on/off, RayDither 값, `_Blender`/`BlendMode`, `AutoSetPerformanceProfile` 전부
바꿔봤지만 "한낮엔 깨끗하고 여명·황혼·밤엔 노이즈"라는 패턴 자체는 변하지 않았다. 설정값 하나를 잘못
바꿔서 생기는 문제라기보다, 이 Unity 6 + URP 조합에서 WeatherMaker의 야간(저조도) 하늘 렌더링 자체에
내재된 특성/한계일 가능성이 높다. Gaia도, 그믐 커스텀 프로필도 없는 순수 격리 씬에서 재현되므로, 지금이
개발자에게 문의하기 가장 깨끗한 시점이다 — 이전 대화에서 준비한 영문 질문 초안에 이 표(08:00/13:00/18:00/
20:00 결과)를 그대로 붙이면 된다.

**개발자 답변과 적용 (2026-08-31)**: WeatherMaker 개발자(Digital Ruby)가 Discord로 답변했다.

> Try changing WeatherMakerPrefab -> SkySphere -> SkySphereProfile -> Dither to 0.002
> It's not perfect but is much better from what I'm seeing.

즉 원인 지점은 **Sky Sphere 프로파일의 디더 진폭**이고, 개발자 본인도 완전한 해결이 아니라 완화라고 밝혔다.
앞선 조사에서 "저휘도 영역에서 디더가 상대적으로 과하게 드러난다"고 좁힌 결론과 일치한다.

해당 필드는 `WeatherMakerSkyProfileScript.DitherLevel`이고, `WeatherMakerPrefab`이 참조하는 프로파일은
`WeatherMakerSkyProfile_ProceduralMilkyWayPhysicallyBased.asset`(구매 에셋 원본, `DitherLevel: 0.01`)이다.
원본을 직접 고치면 에셋 업데이트 때 덮어써지고 다른 씬에도 한꺼번에 반영되므로, Performance Profile·
TemporalReprojection 머티리얼과 같은 방식으로 복제본을 만들어 적용했다.

- `Assets/09.Data/Environment/WM_SkyProfile_Geumeum.asset` — 원본 복제 후 `DitherLevel: 0.01 → 0.002`
- `WeatherMakerPrefab`이 있는 씬 3개 모두 `SkySphere`가 이 복제본을 쓰도록 프리팹 오버라이드 추가 —
  `Main.unity`, `Dev_Combat.unity`, `Dev_WeatherMakerIsolation.unity`

격리 씬과 `Main.unity`에서 육안 확인 결과 노이즈가 눈에 띄게 줄었다(2026-08-31, 사용자 확인).

**왜 씬마다 오버라이드를 거는가 — 프리팹 한 곳에서 바꾸면 안 되는 이유**: `WeatherMakerPrefab`의
`SkySphereProfile` 필드를 복제본으로 바꾸면 씬별 오버라이드 없이 전부 반영되지만, `Assets/99.ThirdParty/`는
`.gitignore` 대상(`.gitignore:21`)이라 **그 변경이 저장소에 올라가지 않는다.** 고친 사람 로컬에서만 적용되고
다른 팀원은 여전히 원본 `DitherLevel: 0.01`을 보게 되는, 눈에 잘 띄지 않는 divergence가 생긴다.
같은 이유로 구매 에셋 원본 프로파일의 값을 직접 고치는 것도 안 된다.

**WeatherMaker가 들어가는 씬을 새로 만들 때는 이 오버라이드를 손으로 걸어야 한다.** 위 세 씬 중 하나를
열어 `SkySphere`의 `Sky Sphere Profile` 필드를 확인하면 된다.
