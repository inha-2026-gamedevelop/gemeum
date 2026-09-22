# LOD · 컬링 현황

이 문서는 2026-09-01 `Main.unity` 측정 스냅샷이다. 현재 런타임 기준은 `Release/Field.unity`이므로 아래 수량과 병목 결론은 재측정 전까지 참고값으로만 사용한다.
성능 최적화 일반론은 [엔지니어링 패턴](UNITY_ENGINEERING_PATTERNS.md), 씬·에셋 규칙은 [씬·에셋 안전](SCENE_AND_ASSET_SAFETY.md).

---

## 1. 한눈에

| 기능 | 상태 | 비고 |
|---|---|---|
| LODGroup | **적용** | 씬에 275개. Map Decor 바위 전부 |
| SRP Batcher | **켜짐** | 실제 병목은 현재 런타임 씬에서 측정해야 함 |
| 지형 나무 빌보드 | **켜짐** | 90m 밖 빌보드 전환 |
| 지형 풀 컬링 | **켜짐** | 40m 밖 미표시 |
| GPU 스키닝 | **켜짐** | |
| 오클루전 컬링 | **꺼짐** | 정적 플래그는 준비됨, 베이크만 안 됨 |
| GPU Resident Drawer | **꺼짐** | Unity 6 URP 기능, 미검토 |
| 정적 배칭 | **꺼짐** | `BatchingStatic` 플래그 0개 |
| `lodBias` | **2** | 기본값 1의 2배. LOD 효과를 절반으로 깎는다 |

한 줄 요약: **LOD는 붙어 있지만 `lodBias=2`가 그 효과를 깎고 있고, 오클루전 컬링은 준비만 되고 켜지지 않았다.**

---

## 2. LOD

### 현황

| 항목 | 값 |
|---|---|
| 씬 LODGroup | 275개 |
| `QualitySettings.lodBias` | **2** |
| `QualitySettings.maximumLODLevel` | 0 |
| LODGroup 없는 MeshRenderer | Map Markers 82, WeatherMaker 7, 캐릭터 1 |

Map Markers는 구역 표시용 원통이라 LOD 대상이 아니다. WeatherMaker의 7개는 하늘·전체화면 효과용이다.

지형 나무 3901그루는 `LODGroup`이 아니라 **Terrain 자체의 LOD·빌보드 체계**를 쓴다. LODGroup 집계에 잡히지 않는다고 LOD가 없는 것이 아니다.

### `lodBias = 2`가 무슨 뜻인가

LODGroup의 전환 기준은 "화면에서 차지하는 높이 비율"이다. 예로 `PW_Stone_01`은

```
LOD0 → LOD1 : 화면 높이 64.7%
LOD1 → LOD2 : 39.8%
LOD2 → LOD3 : 10.5%
LOD3 → 컬링 : 1.7%
```

`lodBias`는 이 기준에 곱해지는 값이다. **2면 전환이 2배 먼 거리에서 일어난다** — 즉 가장 무거운 LOD0을 필요 이상으로 오래 유지한다.

바위 275개와 나무 3901그루에 동시에 걸리므로 단일 설정 중 영향이 가장 크다.

### 주의

`lodBias`는 `ProjectSettings/QualitySettings.asset`에 있다. **바꾸면 팀 전원에게 적용된다.** 값을 만지기 전에 팀 합의가 필요하다 ([실행 정책](../ai/01_AGENT_EXECUTION_POLICY.md)).

---

## 3. 오클루전 컬링

### 현황

| 항목 | 값 | 의미 |
|---|---|---|
| 베이크 데이터 크기 | **0 byte** | 베이크된 적 없음 |
| `Camera.useOcclusionCulling` | True | 켜져 있으나 데이터가 없어 무효 |
| OccluderStatic | 1310개 중 387개 | 가릴 수 있는 오브젝트 |
| OccludeeStatic | 1310개 중 1203개 | 가려질 수 있는 오브젝트 |

정적 플래그는 Gaia가 배치하면서 이미 설정해뒀다. **베이크만 하면 바로 동작하는 상태다.**

지금은 **프러스텀 컬링(화면 밖 제거)만 동작한다.** 바위 뒤에 가려진 나무는 그대로 그려진다.

### 켤 때 알아야 할 것

- `Window > Rendering > Occlusion Culling`에서 Bake.
- 베이크하면 `OcclusionCullingData` 에셋이 새로 생기고 **`Main.unity`가 변경된다.** 통합 씬이라 통합 담당자가 진행한다 ([씬·에셋 안전 1장](SCENE_AND_ASSET_SAFETY.md)).
- **정적 오브젝트만 대상이다.** 플레이어·몬스터처럼 런타임에 움직이는 것은 가려지는 쪽(Occludee)으로만 처리되고 가리는 쪽(Occluder)이 되지 못한다.
- 768m 야외 맵이라 실내만큼 극적이지는 않다. 다만 `FlatRock_01`(높이 40m) 41개는 실질적인 오클루더라 결계 구역처럼 시야가 막히는 곳에서 이득이 있다.
- 지형을 다시 만들거나 바위를 크게 옮기면 **베이크가 낡는다.** 다시 구워야 한다.

현재 파라미터: `smallestOccluder=5`, `smallestHole=0.25`, `backfaceThreshold=100`.

---

## 4. 렌더 파이프라인 설정 (`PC_RPAsset`)

| 항목 | 값 |
|---|---|
| SRP Batcher | True |
| Dynamic Batching | False |
| GPU Resident Drawer | **Disabled** |
| Shadow Distance | 120m |
| Shadow Cascade | 2 |
| Main Light Shadowmap | 2048 |
| MSAA | Disabled |
| Render Scale | 1 |
| HDR | True |

퀄리티 레벨은 `Mobile`·`PC` 두 개이고 현재 `PC`다.

### SRP Batcher가 켜져 있다는 것의 의미

바위는 셰이더 1종(`Shader Graphs/PW_General_ShaderGraph_URP`)에 머티리얼 6종이다. SRP Batcher는 켜져 있지만, 드로우콜과 LOD0 삼각형 중 무엇이 현재 병목인지는 `Release/Field`의 Profiler와 Frame Debugger 측정으로 판단한다.

### GPU Resident Drawer

Unity 6 URP의 기능으로, 컬링과 인스턴싱을 GPU로 넘긴다. 나무 3901그루·바위 275개처럼 정적 지오메트리가 많은 야외 맵에 이득이 크다. **꺼져 있고 아직 검토된 적이 없다.**

켜기 전에 확인할 것: PW 셰이더그래프 계열이 GPU Resident Drawer와 호환되는지, 그리고 URP 에셋 변경은 팀 전체에 걸린다는 점.

### 정적 배칭

`BatchingStatic` 플래그가 붙은 렌더러가 **0개**다. 켜면 메시가 복제되어 빌드 크기·메모리가 늘어나므로, SRP Batcher가 이미 도는 상황에서 이득이 있는지 먼저 재보고 결정한다.

---

## 5. 지형 컬링 거리

| 항목 | 값 | 메모 |
|---|---|---|
| `treeDistance` | 1000m | 768m 맵이라 사실상 전부 |
| `treeBillboardDistance` | 90m | 90m 밖은 빌보드 |
| `treeCrossFadeLength` | 50m | |
| `treeMaximumFullLODCount` | 50 | 풀 메시로 그릴 나무 최대 개수 |
| `detailObjectDistance` | 40m | 풀 표시 거리 |
| `detailObjectDensity` | 0.3 | |
| `heightmapPixelError` | 5 | 지형 메시 단순화 허용 오차 |
| `basemapDistance` | 1024m | 이 밖은 저해상도 지형 텍스처 |

`treeDistance`가 1000m라 맵 전체의 나무가 항상 컬링 대상 밖이다. 다만 90m부터 빌보드라 실제 부담은 그보다 작다.

---

## 6. 지형 디테일(풀)이 흰 판으로 보일 때

`DetailPrototype`이 `renderMode = VertexLit` + `useInstancing = false`면 Unity가 프로토타입의 머티리얼을 쓰지 않고 내장 vertex-lit 디테일 경로로 그린다. 그 경로에 URP 변형이 없어 **텍스처가 빠진 흰 판**으로 나온다.

`useInstancing = true`로 두면 프로토타입 머티리얼(`PWS/Details/PW_Details_Foliage_URP`)로 정상 렌더된다. 2026-09-01에 이 문제를 겪고 고쳤다.

디테일 설정은 TerrainData 에셋에 저장된다. 이 에셋은 `Assets/09.Data/Terrain/`에 있고 **LFS 대상**이다 — `.gitattributes`의 `Assets/09.Data/Terrain/*.asset` 규칙. 14MB 텍스트라 LFS 없이 커밋하면 편집할 때마다 히스토리에 통째로 쌓인다.

---

## 7. 손대기 전에

이 문서의 항목은 대부분 **`ProjectSettings` 또는 통합 씬**을 건드린다. 즉 팀 전체에 영향이 간다.

| 바꾸려는 것 | 어디에 저장되나 | 필요한 것 |
|---|---|---|
| `lodBias` | `ProjectSettings/QualitySettings.asset` | 팀 합의 |
| 오클루전 베이크 | `Release/Field.unity` + 신규 에셋 | 통합 담당자 |
| GPU Resident Drawer | `PC_RPAsset` | 팀 합의 + 셰이더 호환 확인 |
| 지형 컬링 거리 | TerrainData (LFS) | 테크니컬 아트 |

값을 바꿨으면 **이 문서의 표도 같이 고친다.** 현황 문서는 틀리면 없느니만 못하다.
