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
