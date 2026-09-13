# 갓·너울 PhysioBrush 사용

객주와 보스 1페이즈용 갓·너울의 표현 에셋이다. 설치된 PhysioBrush의 **Original Bone Chain** 모드와 `PhysioWindVolume`으로 너울·늘어진 턱끈·매듭 고리를 움직인다. 게임 판정이나 네트워크 상태를 변경하지 않는다.

## 에셋

| 용도 | 경로 |
|---|---|
| 캐릭터 부착용 | `Assets/02.Prefabs/Art/GatNeoul_Physio.prefab` |
| 바람 포함 미리보기 | `Assets/02.Prefabs/Art/GatNeoul_WindPreview.prefab` |
| 물리 조절값 | `Assets/09.Data/Presentation/GatNeoul_PhysioSettings.asset` |
| 리깅 모델·텍스처 | `Assets/06.Model/Monster/GatNeoul/` |
| URP 재질 | `Assets/12.Materials/Monster/GatNeoul/` |

모델은 Blender에서 직접 작성한 갓을 리깅한 것이다. 이전 Meshy 결과는 형상 탐색용이며, 구매한 보스 모델을 포함하거나 외부 서비스에 업로드하지 않았다. 작업용 Blend·작성 스크립트·검증 영상은 Git 제외 경로 `.claude/art/boss-gat/2026-09-13/`에 있다. 다른 PC에서는 위의 Assets 에셋으로 재생한다.

## 빠른 확인

1. 작업하지 않는 빈 씬에 `GatNeoul_WindPreview`를 배치한다. 기존 작업 씬을 덮어쓰지 않는다.
2. 갓을 선택해 `F`로 화면에 맞춘다. 카메라와 조명을 배치하고 Play한다. 물리는 편집 모드에서 재생되지 않는다.
3. `Wind_Direction_BlueZ`의 회전으로 방향을 바꾼다. 파란색 **+Z**가 바람 방향이다.
4. `Physio Wind Volume`의 Power(`strength`)를 0으로 하면 바람이 멈춘다. 중력·관성은 유지된다.

미리보기 볼륨의 기본값은 Power 8, Amplitude 0.9, Frequency 1, Turbulence 0.4, Cloth Power 1, Hair Power 2.5, Tail Power 0이다. 크기는 2 × 2 × 2m다. Power는 풍속 m/s가 아니라 물리에 더하는 가속도 m/s²다. 이 작은 소품의 가시적인 움직임을 위해 조절한 값이다.

## 캐릭터 부착

- 바람 볼륨이 없는 **`GatNeoul_Physio`**를 Head 부착 지점의 자식으로 둔다. 원점은 챙의 머리 구멍 중심이며 Unity +Y 위, +Z 앞이다. 크기는 약 0.644 × 0.542 × 0.644m, 단위 배율 1이다.
- 위치·회전·크기는 바깥 `GatNeoul_Physio` 루트에서 맞춘다. 내부 `GatNeoul_Simulation` 이름은 유지한다. PhysioBrush가 이 이름으로 생성 체인을 찾는다. 바깥 루트와 내부 Runner를 분리했으므로 런타임 `Instantiate`의 `(Clone)` 이름도 바람 검색을 깨뜨리지 않는다.
- 캐릭터 주변에 `PhysioWindVolume`을 둔다. **볼륨 안의 모든 PhysioBrush 대상이 영향을 받는다.** 미리보기용 볼륨을 캐릭터마다 복사하면 중첩된 바람이 합산된다.
- WeatherMaker 풍속을 자동으로 연결하는 코드는 넣지 않았다. 현재는 PhysioBrush 볼륨의 값으로 조절한다.
- 보스 드러내기 연출에서는 바깥 갓 루트를 `SetActive(false)` 한다. 갓을 Rigidbody로 떨어뜨리는 구조가 아니다. 귀·꼬리 노출 이벤트 연결은 기존 보스 연출 작업에서 처리한다.
- 이전 Yuna 정지 자세의 Blender 월드 부착 위치 `(0, -0.012, 1.736)m`는 **Head local 값이 아니다.** 실제 Head 소켓과 애니메이션에서 위치를 맞춘다.

## 움직이는 부분과 고정부

| 부분 | 설정 |
|---|---|
| 갓 몸통·챙·테두리 | `Gat_Attachment`에 완전 고정 |
| 너울 윗단·헤더 | 완전 고정, 아래로 갈수록 가동 체인에 보간 |
| 너울·밑단 장식 | 둘레 12개 × 4본, Cloth 12 islands, 공통 가중치로 연결 |
| 턱에 묶인 윗끈·매듭 중심 | 고정 |
| 늘어진 끈 끝 | 좌우 각 3본, Hair 2 islands |
| 매듭 고리 | 좌우 각 2본, Hair 2 islands; 매듭 접점 고정 |

총 58개 가동 본, 16개 고정 앵커, 58개 ConfigurableJoint다. 피부 가중치는 정점당 최대 4개다. `Collider Scale = 0`으로 충돌체는 0개이며 전투·이동 충돌에 참여하지 않는다. 체인은 표면 표현용이므로 몸·어깨를 밀어내지 않는다.

PhysioBrush 창에서 수정할 때는 **독립 갓 프리팹 모드**에서 `GatNeoul_Simulation`을 선택한다. 기존 본 체인 모드와 스킨 가중치를 유지한다. 보스 Animator 아래에 부착한 상태에서 본을 재생성하면 플러그인의 최상위 Animator 기준 경로가 바뀔 수 있다.

Runner의 Wind Response는 전체 반응량, 볼륨의 Hair/Cloth Power는 부위별 반응량이다. 공유 Settings 에셋을 수정하면 같은 에셋을 쓰는 모든 갓에 반영된다. 개별 변형은 Settings를 복제하고 해당 Runner에 할당한다.

## 재질 결정

너울은 얼굴을 가리는 **양면·불투명 URP Lit 직물**이다. 원본 BaseColorAlpha 텍스처는 유지하되 알파 블렌딩은 사용하지 않는다. 현재 URP 환경의 양면 투명 너울에서 넓은 면이 비어 보이는 현상을 관찰하여, 얼굴 가림과 안정적인 표면 표현을 우선했다. 세부 렌더 파이프라인 원인까지 확정한 것은 아니다. 반투명으로 바꾸려면 정렬·깊이와 얼굴 노출을 다시 검사한다.

## 검증과 남은 통합

2026-09-13 Unity 6000.4.7f1의 임시 빈 씬에서 실제 Play Mode, `Object.Instantiate` 생성본으로 검사했다. 파일로 저장된 기존 씬은 변경하지 않았다.

- 바람 0 → 8: 기준 프레임 대비 너울 최대 47.58mm, 끈 끝 좌우 27.48/35.58mm 이동. 연속 캡처 중 너울 최대 55.65mm.
- 갓·헤더·너울 고정부·매듭 위 끈 변위는 검사 정밀도에서 0mm.
- 바람을 180° 돌리면 밑단 평균 변위가 원래 바람 축 기준 +44.74mm → -54.49mm로 바뀜.
- 비활성화·재활성화 후 물리 재개, 고정부 유지, 유한한 정점 좌표 확인. 비활성화 중 순간이동·풀링 재배치는 별도 검증 대상이다.
- 머리 회전·무릎 꿇기·어깨 관통, 실제 보스/객주 프리팹 부착, 컷신 이벤트, 목표 플랫폼 성능은 아직 통합 검증하지 않았다. 3인 네트워크 검증 완료로 세지 않는다.
- 빈 씬 캡처에서는 기존 URP WeatherMaker 기능이 WeatherMaker 매니저 부재 경고를 남겼다. 이를 없애기 위해 공유 렌더러나 구매 플러그인을 수정하지 않았다.

설계 근거: [보스 연출](BOSS_STAGING_DESIGN.md), [씬·에셋 안전](SCENE_AND_ASSET_SAFETY.md), [구현 로드맵](../05-implementation-roadmap.md). 플러그인 사용 기준은 설치본 `Assets/99.ThirdParty/Tools/PhysioBrush/Documentation/EasyPhysics_Documentation.pdf`다.
