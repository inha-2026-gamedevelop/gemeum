# 활 정밀 사격 레이저 VFX 설계안

**상태: 사용자 후속 요청의 짧은 화살형 발사 구현·비교 영상 제작·기능 검증 완료. 최신 개발 빌드, 이동·정리 검사와 실제 3프로세스 검증을 마쳤다. 원본 대비 90% 수준의 시각적 품질은 미검증이며 세부 차이가 남아 있다. 이전 긴 레이저 버전의 영상·검증 기록은 별도 이력으로 보존한다.**

## 후속 요청: 짧은 화살형 발사와 세부 잔상

사용자가 긴 레이저를 화살처럼 짧게 날아가도록 요청했다. 첫 첨부의 발사체·링 세부와 두 번째 첨부의 후반 잔상을 함께 반영한다. 이미지 증거는 `D:/reference/boweffect/analysis/user_arrow_detail.png`, `D:/reference/boweffect/analysis/user_tail_detail.png`다. 아래 긴 레이저의 제작 기록보다 이 절의 후속 목표를 우선한다.

- 발사점과 끝점을 길게 잇는 선을 짧은 발사체와 제한된 꼬리로 바꾼다. 현재 `BowLaserVfxView`의 기본값은 표현 속도 30m/s, 꼬리 길이 2.4m, 수명 0.8초, 끝점 도달 후 소멸 0.08초다. 외형 조정 중 값이므로 최종값은 프리팹과 뷰를 함께 확인한다.
- 링과 중심광·작은 불티의 세부를 첨부에 맞춰 보강한다. 후반 잔선은 이전의 매끈한 S자 한 줄에서 여러 불규칙한 가닥, 세 가닥의 가는 전기 잔상과 작은 잔불이 보이는 방향으로 수정한다.
- 최종 잔선 보정은 약 1.6회 굴곡의 주 가닥과 12–58%·55–93% 구간의 짧은 옆가지로 구성한다. 소멸 중 폭·회전·셰이더 흐름을 미세하게 바꾸며, 링의 주름은 국소 구간에만 배치한다. `SaveMesh`는 기존 에셋 GUID를 유지하면서 메시 버퍼를 명시적으로 다시 올려, 에디터에서 형상 보정이 실제 렌더에도 반영되도록 했다.
- `SetVariation(uint seed)`를 대여 후 첫 `Evaluate` 전에 호출한다. 서버가 승인한 Action ID와 Shot Index를 섞은 결정적 시드를 사용하므로 같은 발사는 세 peer에서 같은 패턴을 사용하고, 다른 발사는 다른 시드를 사용한다. 시각적으로 충분한 변주가 생기는지는 새 캡처에서 확인한다.

실제 `ArrowConfigSO`의 기본 속도도 30m/s지만 특성·장비의 `ArrowSpeedPercent`가 수락된 화살 속도에 적용된다. 기존 표현 RPC는 속도를 전달하지 않으며 이번 후속 작업에서 RPC나 판정은 변경하지 않는다. 따라서 뷰의 30m/s는 **기본 속도에 맞춘 표현값**이고 실제 `ArrowProjectile`을 추적하거나 모든 속도 보정·유도 궤적에 일치하는 계약은 아니다. 기존 서버 원점·방향·발사 식별자와 벽까지의 표현 거리 제한을 유지한다.

이전 `BowLaser_Comparison` 영상과 검증 자료를 보존한다. 새 화살형 출력 `D:/reference/boweffect/output/BowArrow_Comparison.mp4`, `BowArrow_Comparison_Slow.mp4`, `BowArrow_Unity.mp4`, `BowArrow_Comparison_Peak.png` 제작을 완료했다. 비교 영상은 2560×720·60fps·6초이며, 저속 비교본은 11.9833초다. 원시 프레임은 `output/arrow-final/`의 360장이다.

### 화살형 이동·정리 확인

`D:/reference/boweffect/verification/arrow-travel-cleanup.json`에서 이동 표본 63개와 정리 검사 9개를 최신 버전으로 재실행해 통과했다. 시드 100·101·102와 벽까지 거리 0.6m·12m·18m 조건에서 꼬리 길이는 2.4m 이하였고, 발사체 끝은 벽 위치를 넘지 않았다. 예를 들어 경과 0.22초의 앞끝은 6.6m, 꼬리 시작은 4.2m였다. 이는 뷰의 이동·정리 검사이며 실제 화살 충돌 판정을 대신하지 않는다.

### 최신 화살형 빌드·실제 실행 검증

개발 빌드 `build-ff66cc412e`는 57.17초에 `Succeeded`로 종료했다. 증거는 `D:/reference/boweffect/verification/build-arrow-final.json`이다. 두 Bow Shader Graph는 모두 `errorFalse`였고 C# 컴파일 오류는 없었다. 빌드 로그의 오류 1건은 기존 WeatherMaker 셰이더 오류이며, 경고 38건도 있으므로 프로젝트 전체 로그가 깨끗하다고 보고하지 않는다.

최신 `verification/runtime-arrow/`에서 `verify_final.py`로 호스트·클라이언트 2개를 확인했다. 충전, 발사 후 정지, 정지 유지, 수명 종료 정리, 조기 취소의 5개 상태가 세 peer에서 모두 통과했다. 실제 게임 카메라 캡처에서 활 소유자는 peer1, net ID 23이다. 이는 한 명의 활 발사를 세 화면에서 확인한 결과이며, 세 명 동시 활 발사는 계속 미검증이다.

로그에서 VFX의 null·missing·shader 예외는 발견하지 않았다. 검증 응답 파일의 읽기·쓰기 경합으로 `IOException` 2건이 있었으나 검증 명령은 모두 완료됐다. 따라서 예외 전체가 0건인 것으로 표현하지 않는다. 검증 소유 PID `50028`, `77284`, `6436`은 실행 파일 경로를 확인한 뒤 종료했다.

### 최신 외형 검수와 남은 차이

최신 외형 검수 기록은 `D:/reference/boweffect/verification/arrow-visual-review.md`다. 짧은 발사체·제한된 꼬리와 첨부 참조의 링·여러 가닥 잔상 방향을 구현했다. 원본의 불티는 더 조밀하며, 백색·살구색으로 갈라지는 잔선의 가독성에는 아직 차이가 있다. 구현과 기능 검증 완료를 원본 대비 90% 품질 달성으로 확대하지 않는다.

실제 `Dev_Combat` 카메라는 후처리가 꺼져 있어 독립 preview보다 Bloom이 약하다. 작업 씬·공용 후처리를 바꾸지 않았으므로 두 촬영 조건의 밝기 차이를 별도로 판단한다. 이 절 아래의 `BowLaser` 영상과 `build-d4f058253b`, `runtime/` 결과는 **이전 긴 레이저 버전의 제작·검증 이력**이며, 최신 화살형 결과와 혼용하지 않는다.

## 목표와 범위

참조 영상의 Unity 구간을 기준으로, 활을 당길 때 손 주변으로 에너지가 모이고 발사 순간 붉은 레이저와 링 파동이 화살 방향으로 빠르게 뻗는 클라이언트 표현을 만든다. 첫 적용 대상은 홀드와 해제가 이미 있는 **정밀 사격**으로 잡는다. 일반 화살은 기존 연출을 유지한다.

효과는 시각 표현에만 사용한다. 실제 발사·이동·충돌·피해는 기존 서버 권위 `ArrowProjectile` 경로를 유지하며 VFX에 충돌 판정이나 피해를 넣지 않는다. 빔이 화살의 검증된 방향과 어긋나거나 벽을 관통해 실제 명중으로 오해되지 않도록 캡처에서 길이와 노출 시간을 조정한다.

## 참조 영상에서 확인한 것

대상은 `D:\reference\boweffect\BowEffect.mp4`의 **56–62초 Unity 구간**이다. 영상은 1280×720, 60fps이며 여러 엔진의 시연이 이어지므로 다른 Niagara 구간은 기준으로 삼지 않는다. 시간은 영상 시각이고, 아래 구간은 0.125초 간격으로 본 프레임에 따른 근사치다.

| 영상 시각 | 관찰 |
|---|---|
| 56–57.2초 | 궁수가 활을 겨누지만 눈에 띄는 지속 오라는 거의 없다. |
| 57.70초 | 첫 큰 발사. 활 앞에 짧은 백색·적색 섬광과 방사형 불티가 생긴다. |
| 57.70초 이후 | 얇은 분홍·적색 빔이 전방으로 뻗는다. 흰 중심선과 붉은 외곽광이 보이고, 빔 축을 가로지르는 링과 짧은 불티가 함께 이동한다. |
| 약 58.3–59.2초 | 세밀한 후속 프레임에서 발사점이 여러 번 다시 밝아지고 짧은 선분이 전진하는 연속 소형 발사를 확인했다. 단일 빔의 잔광만으로 해석하지 않는다. |
| 60.20초 | 두 번째 큰 발사가 시작된 뒤 소멸한다. 61초 이후에는 눈에 띄는 빔 잔재가 없다. |

참조의 충전 오라는 짧고 약하다. 사용자가 요청한 손으로 모이는 충전 입자와 충전 중 오라는 게임용 보강 요소로 추가하되, 발사 시점의 짧은 섬광과 빠른 전개는 참조처럼 유지한다. 화면 전체를 덮는 플래시보다 활 주변과 빔에 밝기를 모으고, 바닥에 비치는 붉은 빛은 게임 카메라에서 과하지 않게 제한한다.

전체 6초 비교에서 원본 중간의 연속 소형 발사는 별도 차이로 남긴다. 승인된 첫 적용 범위는 정밀 사격이므로, 정밀 화살 한 발에 존재하지 않는 추가 공격을 연상시키는 자동 연사 표현을 붙이지 않는다. 비교 촬영은 두 큰 발사를 57.70초와 60.20초에 맞춘다.

## 목표 연출 구성

| 단계/레이어 | 모양과 움직임 | 구현 방향 |
|---|---|---|
| 충전 | 활시위·화살 받침 주변에 희미한 적색 입자가 생겨 손 쪽으로 수렴한다. 충전 진행에 따라 밀도와 중심광을 올리고 Ready 상태에서는 최대 강도를 유지한다. | 기존 `PlayerCombatPresentation.DrawCharge`와 `EffectHandPoint()`의 왼손 앵커를 사용한다. 외곽에서 중심으로 향하는 짧은 입자는 Particle System으로 추가하고 `ChargeProgress`에 연결한다. |
| 발사 섬광 | 손과 화살 시작점에서 짧은 흰 중심광, 붉은 방사형 조각, 한 번의 강한 반짝임을 만든다. | VFX Graph 또는 짧은 수명의 Particle System. 정밀 사격 승인 이벤트에서 한 번만 재생한다. |
| Main Laser Beam | 가는 흰 중심선, 선명한 분홍·적색 코어, 얇고 넓은 적색 외피로 층을 나눈다. UV 노이즈가 축 방향으로 흐르며 외곽이 약간 찢기고 흔들린다. | 빔 메시와 Unlit Additive Shader Graph. UV Panning Noise로 내부 흐름, Fresnel/Rim으로 외피, HDR 색과 Noise Dissolve로 생성·소멸을 제어한다. Bloom은 기존 카메라 설정을 우선 사용한다. |
| Concentric Ring Wave | 얇은 링이 빔에 수직으로 놓여 축을 따라 전진한다. 가까운 링은 굵고 밝고, 뒤쪽 링은 작고 희미하게 분리한다. | 링 메시와 별도 Shader Graph. Radial UV를 Step/Smoothstep으로 마스킹하고 시간에 따라 반지름을 키우면서 노이즈로 Alpha Erosion을 준다. `BowLaserVfxView`가 링 메시의 `Forward` 방향·간격·반경을 제어한다. |
| Sparks | 발사점에서 몇 개의 짧고 빠른 적·분홍 선이 빔 방향으로 튀며 일부만 약간 퍼진다. 백색·황색 점광은 소량만 섞는다. | Particle System의 Velocity Stretch 라인. 낮은 수량으로 시작해 빔 외곽을 보강하고 화면을 불티로 채우지 않는다. |
| 소멸 | 빔의 길이와 외피가 먼저 사라지고 링은 크기가 커지며 투명해진다. 짧은 불티가 마지막으로 꺼진다. | `BowLaserVfxView`가 빔 길이·`_Dissolve`·알파·입자 정지 시점을 함께 제어하고, 풀 반납 때 모든 값을 초기화한다. |

붉은 빛은 적색·분홍색을 중심으로 하고, 가장 밝은 부분만 백색으로 둔다. 구현에서는 효과 전용 폭·길이·링 간격·색·소멸 구간을 `BowLaserVfxView` 프리팹의 직렬화 필드와 전용 머티리얼에서 조절한다. 기존 충전 시간과 지연 이벤트 허용치는 기존 `CombatConfigSO`·`PresentationConfigSO`를 사용한다. 유동 노이즈는 `Assets/03.Image/Player/CommonNoise.png`의 B 채널을 샘플링하며 원본 텍스처 설정은 보존한다.

## 기존 전투 표현에 연결하는 방법

1. **충전 상태** — `PlayerCombatPresentation`이 이미 `PrecisionShotController.State`의 `Holding/Ready`, `HoldStartGameTime`, `EffectHandPoint()`를 읽어 활 충전 표현을 갱신한다. 이 경로에서 충전 뷰의 진행도와 중심 위치를 갱신하고, Ready에서는 최대광을 고정한다.
2. **발사 승인** — `PlayerAttack.SpawnArrow`가 서버에서 화살을 생성할 때 보내는 `BowReleased` 표현 이벤트를 사용한다. 현재 `count=1`은 정밀 사격, `count=0`은 일반 화살을 구분하므로 첫 범위에서는 `count=1`만 레이저로 바꾼다. 입력 해제 순간을 클라이언트에서 예측해 먼저 재생하지 않는다.
3. **방향과 위치** — 빔 시작점은 서버가 확정한 `ArrowFireSnapshot.Origin`, 축은 `ArrowFireSnapshot.Direction`을 사용한다. 현재 `BowReleased` 이벤트는 방향을 전달하지 않으므로, 정밀 사격 전용 RPC로 이 검증된 방향을 전달하도록 연결했다. 클라이언트 조준값으로 공격 방향이나 판정을 새로 결정하지 않는다.
4. **풀링과 시계** — 발사 프리팹은 `PlayerVfxController`에 `bow_laser_release` 키로 등록하고 `LocalPoolRoot`·`PooledVisualLifetime` 규칙을 따른다. 연출 경과는 기존 `AbilityVisualClock`/게임 시계를 사용해 일시정지와 원격 이벤트 시각에 맞춘다. 홀드 취소, 무기 변경, 피격 취소, 사망, 새 런, 비활성화에서 충전 VFX를 정지하고 풀 재사용 때 입자·셰이더 값을 초기화한다.

`Player_BowCharge.prefab`, `Player_BowLaserRelease.prefab`, `SG_BowLaserBeam`, `SG_BowLaserRing`, 전용 머티리얼, `BowLaserVfxView.cs`를 전용 에셋으로 추가했다. 프리팹은 `Assets/02.Prefabs/Art/VFX/`, 머티리얼은 `Assets/12.Materials/Player/`, 표현 스크립트는 `Assets/01.Scripts/05.Art/VFX/`의 기존 규칙을 따른다.

### C# 재생 순서

- `Holding` 진입부터 기존 `PrecisionShotHoldDuration` 동안 충전 진행도를 갱신한다. 별도의 `WaitForSeconds` 충전 타이머를 두지 않아 입력 예측과 서버 승인 사이에 충전 시간이 어긋나지 않게 한다.
- `Ready`에서는 손의 중심광과 수렴 입자를 최대 강도로 유지한다. 취소·피격·무기 변경이면 충전 프리팹을 정지하고 풀에 돌려보낸다.
- 서버가 정밀 발사를 승인해 `BowReleased`를 보내면 충전 표시를 끝내고 발사 섬광과 빔 프리팹을 한 번 대여한다. 빔 길이를 0에서 목표 길이까지 Scale/길이 파라미터로 빠르게 전개하고, 링·불티도 같은 발사 시각과 방향으로 시작한다.
- 빔 전개가 끝나면 `_Dissolve`와 알파를 올려 외피와 중심선을 지우고, 링의 반지름 확대와 Alpha Erosion을 마친 뒤 입자를 정지하고 풀에 반납한다. 모든 곡선은 일시정지를 따르는 게임 시계로 평가한다.

## 구현 순서와 검증 기준

1. 화면에서 빔 두께·길이와 링의 실루엣을 먼저 만든 뒤, 57.70초 첫 발사를 기준으로 섬광·빔 전개·링 이동·소멸을 상대 경과 시간별로 비교한다. 60.20초 두 번째 큰 발사도 확인해 이전 효과가 새 발사에 남거나 중첩되지 않는지 본다.
2. 셰이더 노이즈와 HDR 밝기, 충전 입자와 Velocity Stretch 불티를 추가한다. Unity 게임 카메라에서 플레이어 실루엣·조준 방향·적 가림과 UI 가독성을 확인한다. 원본의 검은 데모 배경과 게임 배경 차이가 점수를 대신하게 두지 않는다.
3. 로컬 플레이어와 원격 플레이어 화면 모두에서 승인된 정밀 사격 한 번당 효과 한 번, 올바른 방향, 취소·피격·일시정지·풀 재사용 후 잔류 0을 확인한다. 실제 화살 충돌·피해·사거리·관통은 기존 검증된 경로와 동일해야 한다.
4. 세 플레이어가 동시에 쏘는 조건에서 투명 오버드로와 파티클 수를 확인한다. 비교 캡처는 참조 Unity 구간의 상대 시점과 같은 프레임을 포함하고, 정지 프레임뿐 아니라 정상 속도 영상도 확인한다.

완성 목표는 참조와 같은 순간의 형태·화면 점유율·밝기 분포·링 간격·전개 및 소멸 속도를 가깝게 맞추는 것이다. 시각적 90% 유사도는 캡처 비교를 마친 뒤에만 평가하며, 객관적으로 측정된 90% 달성 결과는 아직 없다.

## 구현 구성과 연결 계약

아래는 실제 구현 파일과 동작이다. 앞 절의 계획·후보 명칭보다 이 절의 구현 설명을 우선한다. 검증 완료나 시각적 유사도 달성을 의미하지 않으며, 최종 캡처·실행 결과는 별도 검증 기록으로 판단한다.

| 위치 | 역할 및 조절 항목 |
|---|---|
| `Assets/02.Prefabs/Art/VFX/Player_BowLaserRelease.prefab` | 발사 프리팹. `BowLaserVfxView`에서 최대 표현 거리, 수명, 전개 시간, 빔·링 반경, 링 간격·이동·지연, 근거리·원거리 링 HDR 색, 빔·링 소멸 구간, 발사광, 필라멘트와 불티 수량·속도를 조절한다. |
| `Assets/02.Prefabs/Art/VFX/Player_BowCharge.prefab` | 충전 프리팹. `BowChargeVfxView`의 반경과 연결된 코어·입자 참조를 조절한다. 실제 충전 완료 시간은 기존 `CombatConfigSO.PrecisionShotHoldDuration`을 따른다. |
| `Assets/02.Prefabs/Art/VFX/BowLaser/` | 전용 메시 및 `Shaders/SG_BowLaserBeam.shadergraph`, `Shaders/SG_BowLaserRing.shadergraph`와 셰이더 관련 자산. |
| `Assets/12.Materials/Player/M_BowLaser*.mat` | 빔·링·불티 전용 머티리얼. 형태와 시간 곡선은 프리팹 뷰, 표면 표현은 머티리얼·Shader Graph에서 조절한다. |
| `Assets/01.Scripts/05.Art/VFX/BowLaserVfxView.cs`, `BowChargeVfxView.cs` | 메시·셰이더·Particle System을 전달된 시각으로 평가하는 표현 뷰. 독립적인 `Update`로 시간을 진행하지 않는다. |
| `Assets/01.Scripts/05.Art/VFX/PlayerCombatPresentation.bow.cs` | 충전 상태, 승인 발사, 표현 거리, 수명과 풀 lease를 연결한다. 기존 presentation partial에는 구독·호출·정리 훅만 추가한다. |
| `Assets/01.Scripts/05.Art/Editor/BowLaserVfxBuilder.cs` | 전용 Shader Graph·메시·머티리얼·프리팹 제작 도구. 재생성 시 현재 프리팹 튜닝값과 빌더 기본값의 차이를 먼저 확인한다. |
| `Assets/01.Scripts/05.Art/Editor/BowLaserIntegrationTests.cs` | 이전 charge lease의 재사용 간섭, 비활성화 정리, 일시정지 시계, 지연 도착한 홀드 상태 억제에 대한 회귀 검사. 파일 존재와 실제 테스트 통과는 구분한다. |

### 뷰 API와 풀 수명

- `BowLaserVfxView.Evaluate(elapsed, distance)`는 호출자가 설정한 월드 위치·회전의 로컬 `+Z` 방향으로 표현한다. `MaxDistance`, `Duration`은 대여·반납 제어에서 읽는 값이다.
- `BowChargeVfxView.Evaluate(progress, elapsed)`는 충전 진행도와 경과 시간을 받아 손 주변의 수렴 입자와 코어를 표시한다. 손의 위치·조준 회전은 presentation이 갱신한다.
- 두 뷰의 `ResetVisual()`과 `OnDisable`은 렌더러·입자·발사광을 정리한다. Particle System 입자는 뷰가 결정적으로 배치하며 자동 방출에 의존하지 않는다.
- `PlayerVfxController` 키는 `bow_charge`, `bow_laser_release`이다. 월드 좌표를 직접 전달하므로 등록 항목의 Anchor는 비워 둔다. 두 프리팹 루트는 `LocalPoolRoot`와 `PooledVisualLifetime`을 가진다.
- 대여 직후 자동 반환을 억제하고 presentation이 수명을 관리한다. 반납은 기록한 `LeaseGeneration`과 일치할 때만 실행해 다른 발사가 재사용한 인스턴스를 정지하지 않는다. 충전은 취소·피격·사망·무기 변경으로 종료하고, 이미 발사한 빔은 짧은 수명을 마친다. presentation 비활성화 또는 발사의 런·씬 문맥 변경 시에는 남은 발사 표현도 정리한다.
- 저장 프리팹의 메시가 초기에는 꺼져 있으므로 GPU 프리워밍은 단순 대여만으로 충분하지 않다. 쿡 렌더 전에 뷰를 유효한 진행 시점으로 평가해 빔·링·입자가 실제 렌더 대상에 포함되도록 한다.

### 서버 발사와 클라이언트 표현

`PlayerAttack.SpawnArrow`는 화살 프리팹·설정을 확인하고 기존 `SpawnAcceptedArrow`를 호출한 뒤 정밀 발사에만 `RpcPrecisionShotVisual`을 보낸다. 전달값은 수락된 `ArrowFireSnapshot`의 원점·방향·Action ID·Shot Index·런 ID·씬 revision과 서버 gameplay 발사 시각이다. 서버 피해·화살 이동·유도·관통 판정은 이 VFX 이벤트에 맡기지 않는다.

클라이언트는 `PrecisionShotPresented`에서 유효성과 문맥, 지연 허용치를 확인하고 발사 식별자로 중복 대여를 억제한다. 이어지는 기존 `BowReleased` 이벤트가 발사음·정밀 해제 애니메이션을 유지한다. 새 레이저 대여에 성공한 정밀 발사만 기존 원형 pulse를 생략하며, 일반 사격은 기존 경로를 따른다.

빔 회전은 수신한 서버 방향을 그대로 사용한다. 표현의 끝은 `GameDB.Arrow.WorldMask`에 대한 로컬 raycast로 벽 앞에 제한한다. 이는 시각적 길이 제한이며 실제 화살 명중·피해를 결정하지 않는다. 발사 이후 화살의 유도 궤적 전체를 추적하는 지속 레이저는 이번 구현 범위에 포함하지 않는다.

### 시계와 홀드 상태 도착 순서

충전은 복제된 `Holding/Ready`, `HoldStartGameTime`과 기존 충전 시간을 사용한다. 발사 빔의 최초 경과는 확인된 gameplay 시각에서 발사 시각을 뺀 값으로 정하고, 이후에는 런이 정지하지 않은 프레임에서만 `unscaledDeltaTime`을 누적한다. 앞서 보간된 `AbilityVisualClock` 값을 빔의 시작 경과로 사용하면 원격 화면에서 발사 섬광이 건너뛰어질 수 있어 이를 분리했다. 지연 허용치 판정에는 기존 `PresentationConfigSO.LateOneShotToleranceSeconds`를 사용한다.

발사 RPC가 홀드 SyncVar보다 먼저 도착할 수 있으므로 클라이언트가 당시 알고 있던 홀드 generation만으로 충전 재시작을 막지 않는다. 같은 런·씬에서 `HoldStartGameTime <= 마지막 승인 발사 시각`인 홀드는 이미 해제된 것으로 취급한다. 발사 시각보다 나중에 시작한 새 홀드는 정상적으로 충전한다. 마지막 발사 시각은 프리팹 대여 실패 시에도 기록하고, presentation 비활성화 시 초기화한다.

## 참조 비교 산출물과 확인 기록

- 원본: `D:/reference/boweffect/BowEffect.mp4`, 56–62초.
- 나란히 비교: `D:/reference/boweffect/output/BowLaser_Comparison.mp4` — 2560×720, 60fps, 6초. 왼쪽 원본, 오른쪽 격리된 Unity 미리보기.
- 0.5배속 비교: `D:/reference/boweffect/output/BowLaser_Comparison_Slow.mp4`.
- Unity 단독: `D:/reference/boweffect/output/BowLaser_Unity.mp4`.
- 발사 정점 비교: `D:/reference/boweffect/output/BowLaser_Comparison_Peak.png`.
- 최종 보정본 원시 프레임: `D:/reference/boweffect/output/final/frame_0000.png`부터 `frame_0359.png`까지, 총 360장.

`BowLaserVfxCapture`는 임시 PreviewScene, 복제된 신궁 모델·활 모션, 체크 바닥과 전용 촬영 카메라를 사용한다. 실제 게임 플레이 녹화와 구분한다. 열린 작업 씬이나 공용 Volume을 저장하지 않으며 촬영용 메시·재질·조명은 종료 시 제거한다.

### 비교하며 반영한 수정

첫 촬영의 링 미표시를 RG UV만으로 반경을 계산하도록 수정했다. 빔의 과도한 백색, 균일한 방사형 막대 불티, 끝점에 뭉친 금색 입자를 차례로 조정했다. 구성은 큰 분홍·백색 앞쪽 링과 작아지는 청색 뒤쪽 링, 가는 코어와 흔들리는 보조 선, 부드러운 꼬리의 불규칙 불티, 짧은 지면 적색 반사광이다. 소멸 후반에는 시작점이 전방으로 이동하며 선이 짧아진다. 아래 추가 보정까지 반영한 최종 캡처의 8개 시점을 검수했으며 주요 레이어 누락이나 깨짐은 발견하지 않았다.

두 큰 발사의 시작 시각·빔 축·화면상 길이·링 투영을 맞췄다. 원본에 비해 캐릭터 실루엣, 섬광의 세부 분포, 불티 경로와 지면 밝기에는 차이가 있다. 원본 중간의 작은 연속 발사는 이번 정밀 사격 적용 범위에서 재현하지 않는다. 따라서 전체 6초가 동일하거나 객관적으로 90% 이상 일치한다고 보고하지 않는다.

### 추가 외형 보정과 재촬영

최초 기능 검증 이후 `BowLaserVfxView`와 `BowLaserVfxBuilder`에서 아래 외형을 보정했다. 보정본의 개발 빌드·M2 검증을 다시 수행했으며 그 결과는 다음 절에 기록한다.

- 불티 선 끝에 Gaussian 알파와 HDR 머티리얼을 사용하는 별도 Billboard Particle System을 추가했다. 붉은 불티 36개·금색 불티 10개로 조정하고, 붉은 불티의 42%를 뒤쪽 반동 방향으로 배치했다. 금색 불티는 빔 끝 한 점에 몰리지 않도록 빔 위아래로 분산했다.
- 빔 전개 시간을 0.07초로 줄였다. 링 성장은 발사 후 0–0.13초에 적용하고, 빔 시작점이 앞으로 이동하며 꼬리가 분리되는 구간은 0.55–1.1초로 조정했다.
- 보조 필라멘트 메시의 단면 폭 계수를 0.6으로 두고, 낮은 주파수의 큰 굴곡과 HDR 발광을 보강했다. 직선 코어가 약해진 후에도 굽은 잔광이 남도록 후반 소멸 구간을 0.48–1.05초로 조정했다.
- 발사 지점의 짧은 Point Light는 초기 강도 180, 범위 12로 조정했다. 시간에 따라 빠르게 감쇠하는 표현용 조명이다.

실제 게임 화면의 발광이 독립 preview보다 약했던 요인 중 하나로 `Assets/00.Scenes/Dev_Combat.unity` 카메라의 `m_RenderPostProcessing: 0`을 확인했다. 이번 보정에서 작업 씬이나 공용 후처리 설정은 수정하지 않았다. 따라서 preview의 후처리 조건과 실제 게임 카메라의 발광 결과는 구분해 비교해야 한다.

보정본 360프레임을 `D:/reference/boweffect/output/final/`에 다시 촬영하고 기존 출력 경로를 갱신했다. `BowLaser_Comparison.mp4`는 2560×720·60fps·6초, `BowLaser_Comparison_Slow.mp4`는 12초이며, Unity 단독 6초 영상과 정점 비교 이미지도 갱신했다.

### 기능 검증

EditMode 결과는 외형 보정 직전에 실행한 제어 코드 검사다. 이후 해당 제어 코드는 변경하지 않았다. 다음 개발 빌드·뷰 정리·실제 실행 결과는 추가 외형 보정까지 반영한 최종 버전에서 다시 확인했다.

`BowLaserIntegrationTests`, `PlayerVfxControllerTests`, `AbilityVisualClockTests` EditMode 16개 통과, 실패 0개다. 작업 ID는 `333c1b7413884230974ed57799ab4923`이다. 오래된 풀 lease 보호, 비활성화 초기화, 정지 중 시계 유지, 발사보다 늦게 도착한 충전 상태와 새로운 충전 구분을 포함한다.

최종 개발 빌드 `build-d4f058253b`는 54.543초에 `Succeeded`로 종료했으며 증거는 `build-final.json`이다. 실행 파일은 `D:/gemeum/Builds/BowVfxVerification/gemeum.exe`다. 두 Bow Shader Graph의 오류 상태는 모두 `errorFalse`였다. 빌드 로그에는 기존 `WeatherMakerWaterTesselationShader` 오류 1건과 경고 33건이 별도로 있으므로 프로젝트 전체가 오류·경고 0개인 것으로 해석하지 않는다. 이번 활 작업으로 추가된 컴파일·셰이더 오류는 확인되지 않았다.

`view-reset-final.json`에서는 3개 프리팹을 각각 두 번 재생한 뒤 정리했다. 활성 입자 합계 92개에서 정리 후 0개로 감소했고, 활성 메시도 0개였다. 두 Bow Shader Graph의 `errorFalse`도 함께 확인했다.

### 실제 호스트·클라이언트 검증

최종 개발 빌드로 KCP 포트 `18341`의 호스트 1개와 클라이언트 2개를 실행했다. **활을 가진 한 명(peer0, net ID 25)의 행동이 세 화면에서 동기화되는 조건**을 검증했다. `verify_final.py`는 정상 종료 코드 0으로 끝났고, 아래 5개 상태 모두 세 peer에서 통과했다. 증거 폴더는 `D:/reference/boweffect/verification/runtime/`이며, 각 JSON과 같은 이름의 `-peer0.png`, `-peer1.png`, `-peer2.png`가 상태·화면 증거다. 외형 보정 전 런타임 증거는 `D:/reference/boweffect/verification/runtime-before-energy/`에 별도로 보존했다.

| 증거 파일 | 관찰 결과 |
|---|---|
| `charge-paused.json` | 세 peer에서 `bowCharge=True`. 충전 중 일시정지 상태의 표시를 유지했다. |
| `release-paused.json` | 승인 발사 후 세 peer에서 활성 `bowLasers=1`. 발사 표현이 세 화면에 나타났다. |
| `release-still-paused.json` | 일시정지를 유지하는 동안 세 peer에서 활성 빔 1개를 유지했다. |
| `release-cleanup.json` | 재개 후 빔 수명이 끝나 세 peer에서 활성 `bowLasers=0`으로 정리됐다. |
| `early-cancel.json` | 충전 완료 전 취소 후 세 peer에서 `bowCharge=False`로 정리됐다. |

파티의 무기 종류 배정 때문에 다른 참가자에게 활 동시 발사를 요청한 경로는 `NoWeapon`으로 거부됐다. 따라서 **세 명이 모두 활을 들고 동시에 발사하는 조건의 오버드로·가독성·부하는 미검증**이다. 한 명의 발사를 세 화면에서 확인한 결과를 세 명 동시 활 발사 성공으로 확대하지 않는다.

실제 게임 `CameraRig`를 복구한 뒤의 증거 `gameplay-camera.json`과 `gameplay-camera-peer0.png`, `gameplay-camera-peer1.png`, `gameplay-camera-peer2.png`도 최종 개발 빌드로 갱신했다. 최종 런타임 로그에서 `NullRef`, `Exception`, `Shader error` 검색 결과는 0건이었다. 검증을 위해 실행한 PID `64496`, `88012`, `87504`는 확인 후 종료했다.

### 검증 범위와 남은 차이

비교 영상 `BowLaser_Comparison.mp4`와 `BowLaser_Comparison_Slow.mp4`는 원본과 독립 Unity preview를 나란히 보여준다. 실제 멀티플레이 검증은 위 런타임 JSON·PNG로 구분한다. 원본의 58.3–59.2초 작은 연속 발사는 일반 연사에 해당하는 별도 범위로 남겨 두었다. 최종 잔선은 원본보다 매끈한 S자 형태이며 두 번의 발사에 동일한 입자 패턴이 반복된다. 캐릭터·불티·섬광·지면광의 세부 차이도 남아 있다. 객관적인 유사도 수치를 산출하지 않았으므로 90% 이상 일치했다고 주장하지 않는다.
