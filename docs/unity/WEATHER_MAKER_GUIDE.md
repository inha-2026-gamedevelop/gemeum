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
