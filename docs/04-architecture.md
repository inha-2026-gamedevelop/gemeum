# 그믐 (晦) — 프로젝트 구조 설계서

> 기획서([시스템](01-system-design.md) · [게임 디자인](02-game-design.md) · [기술 구현](03-technical-design.md))를 실제 Unity 프로젝트 구조로 옮기는 설계 문서.
> **이 문서 하나만 읽으면 폴더 · 씬 · 클래스 · 데이터가 어디에 어떻게 놓이는지 알 수 있어야 한다.**
> 문서 버전 0.3 · 최종 수정 2026-08-23

- 밸런스 수치는 여기 적지 않는다 → [게임 디자인](02-game-design.md)
- 서버/클라이언트 판정 기준과 Mirror API는 여기서 요약만 한다 → [네트워크 권위](unity/NETWORK_AUTHORITY.md)
- 네이밍 · 스타일은 → [코딩 컨벤션](coding-convention.md)
- 문서와 코드가 어긋나면 **코드가 우선**이고, 이 문서를 갱신한다.

---

> 2026-09-07 발췌 코드 정적 대조: 아래 `[구현완료]`는 코드 존재/호출 경로를 뜻한다. 씬·프리팹·meta·외부 패키지가 없는 발췌본으로 컴파일/배선/3인 동작을 검증한 것은 아니다.

## 1. 한눈에 보는 구조

```
런 전체를 지배하는 축 = "경과 시간" 하나
서버(호스트)가 경과 시간(초)을 관리 · 동기화하고,
결계 단계 · 스폰 밀도 · 드랍 가중치 · 객주 개방 · 조명 시각이 전부 이 값에서 파생된다.

  RunManager (경과 시간, 런 상태 머신)          ← Geumeum.Network
      ├→ BarrierController   결계 반경 · 범위 밖 피해   ← Geumeum.Systems
      ├→ SpawnManager        구간별 밀집도 · 리스폰      ← Geumeum.AI
      ├→ DropDirector        필드 레벨 → 등급 가중치     ← Geumeum.Systems
      ├→ ShopKeeper          2차 결계 구간에만 개방      ← Geumeum.Systems
      └→ DayCycleController  시각 → 조명 (로컬 전용)     ← Geumeum.Art
```

파트 4개가 각자의 폴더 · 네임스페이스 · 작업 씬을 소유하고(3인 팀이므로 테크니컬 아트는 팀장이 겸임한다), 파트 간 결합은
**공용 인터페이스(`00.Common`)와 이벤트**로만 한다. Combat·AI·Systems 사이의 구체 클래스 직접 참조는 금지하고, Art·UI도 읽기 전용 뷰·이벤트를 통해서만 상태를 읽는다.

| 파트 | 담당자 | 네임스페이스 | 폴더 (`Assets/01.Scripts/`) | 작업 씬 |
|---|---|---|---|---|
| 네트워크 코드 · 전투 · 통합 (팀장) | @AidenKim923 | `Geumeum.Network` `Geumeum.Combat` (네트워크 partial은 `Geumeum.AI` `Geumeum.Systems` 유지) | `01.Network/` `02.Combat/` `99.Dev/`, `03.AI/` · `04.Systems/`의 `Net/`, 중앙 `98.Servers/`(`.server.cs`) | `Dev_Combat.unity` |
| 몬스터 · 보스 | @oname36-creator | `Geumeum.AI` | `03.AI/` (AI·패턴 판정 포함, `Net/`·`98.Servers/` 제외) | `Dev_AI.unity` |
| 게임플레이 시스템 | @kjw1237 | `Geumeum.Systems` | `04.Systems/` (판정 규칙 포함, `Net/`·`98.Servers/` 제외) | `Dev_System/` **(씬 셋 → 3.1)** |
| 테크니컬 아트 | @AidenKim923 (겸임) | `Geumeum.Art` | `05.Art/` | `Dev_Art.unity` **(미생성 → 3.1)** |
| UI (전담) | @kjw1237 (게임플레이 시스템 겸임) | `Geumeum.UI` | `06.UI/` | `Dev_System/` **(씬 셋 → 3.1)** |
| (공용) | @AidenKim923 | `Geumeum.Data` `Geumeum.Common` | `00.Common/` | — |

---

## 2. Assets 최상위 폴더

이미 만들어져 있는 번호 폴더 체계를 그대로 쓴다. 새 최상위 폴더를 임의로 만들지 않는다.

```
Assets/
├ 00.Scenes/        씬 (런타임 7개 + Dev_*)                    → 3장
├ 01.Scripts/       모든 C# 스크립트                           → 4장
├ 02.Prefabs/       프리팹 (파트별 하위 폴더)                  → 7장
├ 03.Image/         텍스처, 스프라이트, UI 이미지
├ 04.Animations/    애니메이션 클립
├ 05.Animator/      애니메이터 컨트롤러
├ 06.Model/         자체 제작 모델 (장승 · 솟대 등 한국 상징물)
├ 07.Sounds/        BGM · SFX (국악 라이브러리 포함)
├ 09.Data/          ScriptableObject 에셋 (GameDB)             → 6장
├ 10.InputSystem/   Input Action 에셋 (입력은 Input Manager로 받는다, 5.6장)
├ 11.Timeline/      연출 타임라인 (집중개발 4~5주차부터)
├ 12.Materials/     머티리얼 (모델 · 캐릭터별 하위 폴더)
├ 13.UI/           UI 폰트 · 이미지 리소스 (HUD · 인게임 메뉴)
├ 98.FreeAssets/    무료 에셋 원본 (플러그인 · 애니메이션 등) — 수정 금지, 커밋 대상
├ 99.ThirdParty/    구매 에셋 원본 — 수정 금지, 커밋 제외(.gitignore)
└ Mirror/           Mirror 패키지 — 수정 금지
```

- `98.FreeAssets/`, `99.ThirdParty/`, `Mirror/`는 읽기 전용으로 취급한다. 외부 에셋의 머티리얼 · 프리팹을 고치고 싶으면 자기 파트 폴더로 **복사한 뒤** 수정한다 ([씬·에셋 안전](unity/SCENE_AND_ASSET_SAFETY.md)).

### 2.1 98과 99를 나누는 이유

둘 다 외부 에셋이지만 **git 취급이 정반대**라서 폴더를 분리한다.

| | `98.FreeAssets/` | `99.ThirdParty/` |
|---|---|---|
| 취득 | 무료 배포 | 유료 구매 |
| git | **커밋한다** (바이너리는 LFS) | `.gitignore` 제외 |
| 팀원 준비 | clone하면 끝 | 각자 자기 계정으로 내려받아 설치 |

유료 에셋은 라이선스가 구매 계정에 묶여 저장소에 올릴 수 없다. 그래서 받지 않은 팀원은 씬이 깨진 채로 열린다 — Gaia 지형에서 실제로 겪은 문제다. 무료 에셋은 그 제약이 없으므로 **커밋해서 전원이 같은 GUID를 갖게 한다.** 각자 따로 임포트하면 GUID가 갈려 프리팹 · 씬 참조가 끊어질 수 있다.

무료 에셋을 넣을 때 지킬 것:

- **에셋 하나당 폴더 하나.** 파일을 `98.FreeAssets/` 바로 아래 흩뿌리지 않는다.
- **`.meta`를 통째로 같이 커밋한다.** 하나라도 빠지면 GUID가 새로 발급돼 참조가 끊어진다.
- **바이너리가 `.gitattributes`의 LFS 대상 확장자인지 먼저 확인한다.** 대상이 아니면서 5MB를 넘으면 LFS 대상에 추가할지 검토한다 — `.github/scripts/check_lfs.py`가 PR에서 검사한다.
- **라이선스 근거를 `docs/licenses/`에 남긴다** ([씬·에셋 안전 4장](unity/SCENE_AND_ASSET_SAFETY.md)). 무료라고 상업 이용까지 무료인 것은 아니다.
- 데모 씬이 딸려 와도 `ProjectSettings/EditorBuildSettings.asset`에 추가하지 않는다.
- Gaia Terrain 데이터는 테크니컬 아트만 수정한다 ([기술 구현 2.3](03-technical-design.md)).

---

## 3. 씬 구성

### 3.1 씬 목록

**`현재`는 저장소에 실제로 있는 씬이고, `계획`은 아직 없는 씬이다.** 계획 씬을 있는 것처럼 참조하지 않는다 — 2026-09-04 실측으로 정정했다.

| 씬 | 상태 | 소유 |
|---|---|---|
| `Main.unity` | **현재** | 통합 씬(개활 맵). 통합 담당자(팀장)만 수정 |
| `Dev_Combat.unity` | **현재** | 네트워크·전투 작업 씬 |
| `Dev_BossTrial.unity` | **현재(개발용)** | 보스 트라이 설정 씬. Boot 복사본에 편성 화면을 얹었다. 레벨 · 무기 · 장비 등급/공격력/옵션 · 특성 · 물약을 정하고 그대로 런을 시작한다 |
| `Dev_AI.unity` | **현재** | 몬스터·보스 작업 씬 |
| `Dev_Character.unity` | **현재** | 캐릭터·무기 부착 확인용. 네트워크 없음. 빌드 설정 미포함 |
| `Dev_WeatherMakerIsolation.unity` | **현재** | WeatherMaker 격리 확인용(임시). 빌드 설정 미포함 |
| `SampleScene.unity` | **현재** | Unity 기본 생성물. 사용하지 않는다 |
| **`Dev_System/` (폴더)** | **현재** | 게임플레이 시스템 작업 씬 **다섯**. 소유자 @kjw1237 — `Dev_Barrier` · `Dev_Lobby` · `Dev_Title` · `Dev_HUD` · `Dev_Shop` |
| `Dev_System/Dev_Shop.unity` | **현재(구매 불가)** | 객주 단독 테스트 씬. `Dev_Combat` 복사본에서 몬스터를 빼고 `@SessionManagers` · 임시 객주 큐브를 얹었다. 빌드 설정 미포함. **1인 설정(`RunTestPanel` 1인 실행 = 자동 준비)으로 호스트를 열어야** 로비로 끌려가지 않고 이 씬에서 런이 시작된다. 상점 열기까지는 되지만 씬 전환이 없어 씬 revision이 0이라 **구매 · 권 사용 요청은 서버가 거부한다.** 구매는 정식 흐름(`Boot` → `Lobby` → `Field`)에서 확인한다 |
| `Dev_Art.unity` | **미생성** | 테크니컬 아트 작업 씬. 소유자 @AidenKim923 |

**런타임 씬은 일곱 개다 (2026-09-06 신설).** `Assets/00.Scenes/Release/`에 **빈 껍데기로 만들어 빌드 목록에 등록했다.** 내용물은 아직 비어 있고, 전환 골격을 먼저 검증한 뒤 채운다. `Main.unity`와 `Dev_System/`의 기존 씬을 어디에 흡수할지는 내용물을 채울 때 정한다.

| 씬 (`00.Scenes/Release/`) | 상태 | 네트워크 | 역할 · 소유 |
|---|---|---|---|
| `Boot.unity` 프리워밍 | **현재(세션 매니저 배치)** | 오프라인 | `@Network` · `@Managers` · `@Diagnostics` 프리팹 인스턴스. 부트 검증기 Main 프로필 통과. 셰이더·프리팹 워밍은 미착수 |
| `Title.unity` 게임시작 | **현재(빈 껍데기)** | 오프라인. Mirror `offlineScene` | 타이틀 UI, 호스트·참가 진입. 현재 `Dev_Title`이 원형 |
| `Lobby.unity` 로비 | **현재(빈 껍데기)** | 온라인. Mirror `onlineScene` | `LobbyController` · `LobbyRoster`. 현재 `Dev_Lobby`가 원형 |
| `Field.unity` 파밍 | **현재(빈 껍데기)** | 온라인 | `SpawnManager` · `BarrierController` · 클러스터 · 객주. **`Main.unity`가 이 역할을 이어받는다** |
| `BossEntrance.unity` 보스입장 | **현재(빈 껍데기)** | 온라인 | 전실. 전원 로드 확인과 트라이 편성 UI. 판정 없음 |
| `Boss_Arena.unity` 보스 | **현재(그레이박스)** | 온라인 | 아레나 두 벌 · 전실 · 날씨 · `BossSpawner`(미배치). 팀장 + 보스 담당자 공동 소유 |
| `Result.unity` 게임종료 | **현재(빈 껍데기)** | 온라인 | 정산 UI. 런 결과 스냅샷만 읽는다 |

**빌드 목록의 0번이 `Boot`으로 바뀌었다.** 이전에는 `Dev_Combat`이 0번이라 빌드한 실행 파일이 개발 씬에서 시작했다. 기존 Dev 씬 넷은 목록 뒤쪽에 그대로 남아 있어 이름으로 여는 경로가 끊기지 않는다.

`.github/CODEOWNERS`에 `/Assets/00.Scenes/Release/` 폴더 규칙을 함께 넣었다. 존재하지 않는 경로를 GitHub이 조용히 무시하는 성질 때문에, 폴더로 잡지 않으면 새 씬에 리뷰 담당자가 비게 된다.

**전실은 별도 씬이다.** 보스 씬 안의 루트로 합치면 큰 씬을 한 번만 읽고 전실이 곧 대기실이 되어 전환이 하나 줄지만, **그 최적화는 보류한다**(2026-09-06 팀장 판단). 합치더라도 아래 전환 계약은 그대로다.

> 🔧 **2026-09-05 정정 — `Dev_System.unity`는 "미생성"이 아니었다.**
> @kjw1237이 씬 하나가 아니라 **`Dev_System/` 폴더에 씬 셋**(`Dev_Barrier` · `Dev_Lobby` · `Dev_Title`)을 만들었고,
> 이 표와 `.github/CODEOWNERS`가 둘 다 단일 파일 `Dev_System.unity`를 가리키고 있었다.
> **CODEOWNERS는 존재하지 않는 경로를 조용히 무시하므로 그 세 씬에 리뷰 담당자가 없었다.**
> CODEOWNERS를 폴더 규칙(`/Assets/00.Scenes/Dev_System/`)으로 고쳤고, `Dev_Character`·`Dev_WeatherMakerIsolation`도
> 빠져 있어 함께 등록했다. `Dev_Art.unity`만 실제로 미생성이라 주석으로 남겼다.

> **`미생성` 씬은 소유자만 등록해 둔 것이다. 통합 담당자가 미리 만들어 두지 않는다.**
>
> 씬은 만든 사람의 작업 흔적이 그대로 들어가는 파일이라, 쓰지도 않을 사람이 먼저 만들어 두면 빈 껍데기에 남의 초기 설정이 박힌다. **필요해진 시점에 소유자가 직접 만든다.**
>
> 다만 **씬을 새로 만들면 씬 목록이 바뀌므로 [CLAUDE.md](../CLAUDE.md)상 구조 변경에 해당한다 — 만든 뒤 팀에 알린다.** 다른 사람의 씬은 열어도 되지만 저장하지 않는다.

#### 씬 전환은 런당 다섯 번이다 (2026-09-06 개정)

초판은 "씬 전환 없음 — 런 전체가 `Main.unity` 하나에서 돈다"였고, 2026-09-03에 보스맵을 분리하며 한 번이 되었다. **2026-09-06에 흐름 전체를 씬으로 나누면서 다섯 번이 됐다.**

```
로비 -> 파밍 -> 전실 -> 보스 -> 결과 -> 로비
```

| 경계 | 방식 |
|---|---|
| 프리워밍 → 게임시작 → 로비 | 접속 전. Mirror의 `offlineScene`/`onlineScene`이 처리한다 |
| 로비 → 파밍 | **씬 전환.** 3인 준비 완료로 런이 시작될 때 |
| 파밍 → 전실 | **씬 전환.** 게이트 카운트다운 종료 또는 필드 30:00 |
| 전실 → 보스 | **씬 전환.** 전원 도착 확인 뒤 |
| 보스 1페이즈 → 2페이즈 | **씬 전환이 아니다.** 같은 씬 안에서 아레나 루트를 토글한다 |
| 보스 → 결과 | **씬 전환.** 보스 사망 · 보스 시계 14:00 · 전멸 |
| 결과 → 로비 | **씬 전환.** 참가자 상태를 복구할 필요가 없어 가장 싸다 |

**분리한 이유** — 보스맵에는 잡귀도 결계도 Gaia 지형도 없어 화면에 캐릭터 넷뿐이다. 고폴리 보스 모델이 성능 문제가 되지 않고, 조명을 개활 맵과 독립으로 잡을 수 있다. 파밍 씬과 정산 화면을 나눈 것은 각 씬이 들고 있어야 할 것이 서로 겹치지 않기 때문이다.

**치르는 비용 — 런 상태의 수명이 씬 수명에서 세션 수명으로 바뀐다.** 아래를 씬 수명에서 꺼내야 한다.

| 수명 | 대상 | 이유 |
|---|---|---|
| **세션 (영속)** | `RunManager` · `LobbyController` · `LobbyRoster` · `AuthorityTickScheduler` · 참가자 상태 보관소 · 씬 전환 소유자 | 런 상태와 명부는 씬보다 오래 산다. **프리워밍 씬에서 만든다** — 로비 씬에 두면 로비로 돌아올 때 두 번째 인스턴스가 생긴다 |
| **씬** | `SpawnManager` · `BarrierController` · 클러스터 | 필드 마커와 클러스터를 캐싱한다. 따라가면 보스 씬에서 유령 참조가 된다 |
| **씬** | `BossSpawner` · 보스 상태 머신 | 보스 씬에만 존재한다 |
| **씬 (재구독)** | `GameEventBus` | `SceneSingleton`이라 전환에 구독이 전부 사라진다. **영속화하지 않고** 각 매니저가 `OnStartServer`에서 다시 구독한다. 영속화하면 파괴된 오브젝트의 핸들러가 남는다 |

**전환 순서를 한 객체가 소유한다.** 다섯 곳에서 각각 구현하면 다섯 곳에서 각각 다르게 틀린다.

```
전환 요청 -> 입력·판정 차단, 이탈·전멸 처리 보류 -> 참가자 상태 Capture
         -> ServerChangeScene -> 전원 로드 확인 -> 재스폰 후 Restore
         -> 차단 해제, 보류했던 이탈·전멸을 한 번 재평가
```

**전멸 오탐이 이 구조의 가장 위험한 지점이다.** `PlayerStatus.OnStopServer`가 전멸을 검사하는데, 씬 전환마다 세 명이 차례로 despawn된다. 전환 중임을 검사하지 않으면 **전환 다섯 번이 전부 허위 전멸 후보**다. 상세는 [직업 정체성과 보스 설계 8장](class-identity-and-boss-2026-09-03.md).

**보스맵 씬 편집 규칙 — 보스를 씬에 배치하지 않는다.** `BossSpawner`가 마커(빈 `Transform`)만 두고 런타임 스폰한다. 그래야 씬 편집이 개설 시 한 번으로 끝나고 이후 보스 작업이 전부 프리팹·SO 안에서 일어나, 공동 소유여도 충돌하지 않는다. 잡귀가 이미 이 구조다.

`Boss_Arena.unity` 내부는 아레나 두 벌과 공유부로 나눈다. **바닥 콜라이더와 NavMesh는 반드시 공유부에 둔다** — 페이즈 전환에 걸을 수 있는 영역이 바뀌면 런타임 재베이크가 필요해진다.

```
Boss_Arena.unity
├ Shared         바닥(R=22) · NavMesh · 스폰 마커 · 게이트 · Staging(BossArenaPhaseView)
├ Antechamber    전실 참배로 6x15 · 등불 2 · 빈 갓걸이 · 도착 마커 3
├ Arena_Phase1   산속 사당 · 등불 12 · 담장 · Volume_P1      (시작 시 활성)
├ Arena_Phase2   저승 물길 · 여우불 14 · 무너진 돌 · Volume_P2 (시작 시 비활성)
└ BossNightWeather  WeatherMaker 구동체(`02.Prefabs/Art/BossNightWeather.prefab`).
                    프리팹이라 부모를 가질 수 없어 루트에 둔다
```

**카메라는 이 씬에 두지 않는다.** 진입 씬의 `Main Camera`가 `SessionPersistence`로
`DontDestroyOnLoad`되어 따라온다. 여기에 또 두면 `MainCamera` 태그가 둘이 된다.

**WeatherMaker 구동체는 씬마다 두고 중복은 런타임에 정리한다.** `Dev_Combat`에도 같은 프리팹이
있어 씬 전환 시 둘이 되는데, WeatherMaker 자신은 중복을 감지해 에러 로그만 남기고 정리하지 않는다.
프리팹 루트의 `WeatherMakerSingleInstanceGuard`(`00.Common/Environment/`)가 나중에 온 쪽을
끄고 파괴해 하나만 남긴다.

**2026-09-06 현재 그레이박스다.** 지오메트리는 프리미티브이고 사당 · 돌무더기 · 갓걸이 모델은 미조달이다.
치수 · 조명 · 안개 · 포스트 프로세싱은 [보스 설계 14.1](unity/BOSS_DESIGN.md)과
[보스 연출 설계 4장](unity/BOSS_STAGING_DESIGN.md) 기준으로 들어가 있고 Play 모드에서 관찰했다.
`BossSpawner`는 아직 배치하지 않았다 — 보스 담당자 몫이다.

**페이즈 전환은 루트 토글만으로 끝나지 않는다.** 안개 프로파일 · 하늘 프로파일 · 바닥 머티리얼 ·
WeatherMaker 달 원반이 공용 루트에 있어 함께 갈아 끼워야 한다. `Shared/Staging`의
`BossArenaPhaseView.ShowPhase()`가 그 다섯을 한 번에 처리한다(4.5장).
개발 확인용으로 `BossPhaseDebugSwitcher`가 부른다(F6 · F7 · F8). 런에서는 BossPhaseSourceLocator와 PhaseChanged 구독으로 연출 상태를 반영한다.

**안개 · 하늘 교체는 씬에 WeatherMaker 구동체가 있을 때만 동작한다.** `ShowPhase()`의 대입은
전부 null 가드 안에 있어, 구동체가 없으면 프로파일 두 벌이 인스펙터에 꽂혀 있어도 **에러 없이
조용히 넘어간다.** 증상이 "프로파일이 안 먹는다"로만 보이므로 먼저 구동체 유무를 확인한다.

**전실은 두 진입 경로를 하나로 받는다.**

| | 정식 런 | [보스 트라이 모드](unity/BOSS_TRIAL_MODE.md) |
|---|---|---|
| 도착 | 게이트 30초 카운트다운 후 전원 강제 이동 | 로비에서 직행 |
| 전실에서 | **3인 도착 확인 → 자동 진행** | 편성 UI → 전원 준비 시 진행 |

**정식 런에도 필요하다** — 씬 전환 직후 세 사람이 모두 로드되었는지 확인하는 구간이 없으면 한 명이 느릴 때 혼자 보스 앞에 떨어진다.

### 3.2 Main.unity 하이어라키 설계

각자 Dev 씬도 이 골격을 따라 만든다. 통합 시 "내 루트 오브젝트를 Main에 프리팹으로 옮기는" 작업이 되도록, **파트별 루트는 반드시 프리팹으로 관리**한다.

```
Main.unity
├ @Network              GeumeumNetworkManager, AccountRelayTransport / 개발 KCP / Steam FizzySteamworks
├ @Managers             GameDBBootstrap, GameEventBus, LobbyController, RunManager, ReviveController, DropDirector,
│                       CurrencyManager, GrowthManager, ScoreManager (GameDBBootstrap/EventBus는 로컬 초기화 오브젝트,
│                       나머지는 NetworkBehaviour — 씬 배치 네트워크 오브젝트)
├ @Stage                Gaia Terrain, 건축물, 라이트, 내비메시  ← 아트 소유
│  ├ SpawnClusters      잡귀 거점 배치 지점 (빈 오브젝트 마커)   ← 아트가 배치, AI가 사용
│  ├ BarrierCenter       결계 중심 고정 마커                     ← 아트가 배치, 서버가 읽기
│  ├ Barrier            결계 비주얼 루트
│  └ BossArea           보스맵 영역
├ @AI                   SpawnManager, BarrierController, BossController
├ @Gameplay             ShopKeeper(객주 NPC), 드랍 아이템 스폰 루트
├ @UI                   HUD / Inventory / Shop / Talent / Barrier경고 / 관전 / 정산 캔버스
└ @Local                CameraRig, DayCycleController, CombatFeedbackImpl
                        (네트워크를 전혀 타지 않는 로컬 전용)
```

`@` 접두사는 "씬 구조 고정 루트"라는 표시다. 루트 오브젝트를 추가 · 삭제하는 변경은 통합 담당자 승인이 필요하다.

공통 관리자 조립은 구현돼 있다. 최종 콘텐츠와 외부 3인 검증은 별도 완료 조건이다.unity`에서 확인됐고, 위 `Main.unity` 최종 골격의 통합·L3 검증은 아직 남아 있다.

**`@Stage` 배치의 필수 제약** ([시스템 기획 2.1](01-system-design.md) · [기술 구현 4.4](03-technical-design.md)):

| 오브젝트 | 제약 |
|---|---|
| `BossArea` 입구 | `BarrierCenter`로부터의 거리가 **3차 결계 최종 반경보다 작아야 한다.** 3차 구간 30초 × 초당 5 = 누적 150 피해이고 회복 봉쇄까지 걸리므로, 결계 밖을 경유하는 배치는 보스전 진입을 전멸 구간으로 만든다 |
| `ShopKeeper` 위치 | `BarrierCenter`로부터의 거리가 **2차 결계 최종 반경보다 작아야 한다.** 이용 가능 시간이 12:00–15:00뿐이라 결계 밖이면 피해를 맞으며 구매하게 된다 |
| `SpawnClusters` | 각 클러스터가 어느 결계 단계까지 살아남는지 배치 시 확인한다. `SpawnManager`가 범위 밖 클러스터를 비활성화한다 |

---

## 4. 스크립트 폴더 · 클래스 배치

**서버 partial의 중앙 위치는 `Assets/01.Scripts/98.Servers/`다.** AI·Systems의 `.server.cs` 파일을 두며, 판정·AI 클래스와 요청·조회 창구·표현은 아래 도메인 폴더에 있다. [전체 목록과 담당 규칙](unity/SERVER_CLIENT_WORKFLOW.md#5-현재-적용-범위).

`Assets/01.Scripts/` 아래 전체 구조. 폴더와 네임스페이스는 기능 기준으로 유지한다. **구현 소유는 파트 담당과 `CODEOWNERS`를 따른다.** `03.AI`는 몬스터·보스 담당, `04.Systems`는 게임플레이 시스템 담당이 판정·AI 코드까지 소유하고, `Net/`·`98.Servers/`의 네트워크 코드는 팀장이 맡는다 → [협업 경계](unity/SERVER_CLIENT_WORKFLOW.md).
클래스 옆 표기: `NM` = Mirror NetworkManager, `NB` = NetworkBehaviour, `MB` = MonoBehaviour, `C#` = 순수 C# 클래스, `SO` = ScriptableObject, `enum` = 열거형, `static` = 정적 클래스.

> **`Dev/` 하위폴더 규약 (2026-09-05 신설, 2026-09-10 개정).** 개발 전용 도구 — 플레이어에게 제공되는 기능이 아니라 개발자가 관찰·조작·측정하기 위해 존재하고, 지워도 게임 규칙이 달라지지 않는 것 — 은 소속 파트의 `Dev/` 아래 둔다.
> **네임스페이스는 소속 파트를 그대로 따른다**(`Player/`·`Lobby/`가 그렇듯 `Dev/`도 네임스페이스에 반영하지 않는다). 별도 어셈블리가 아니므로 **폴더만으로는 빌드에서 빠지지 않는다** — 출시 빌드에서 빼려면 `#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS`를 쓴다.
> **파트에 속하지 않는 공용 도구는 `99.Dev/`(namespace `Geumeum.Dev`)에 둔다** — 2026-09-10 `00.Common/Dev/`에서 옮겼다(4.7). 공용 도구가 `Geumeum.Common`에 있으면 게임 코드와 도구가 같은 네임스페이스에 섞여, 출시 빌드에서 무엇을 빼야 하는지가 폴더로 드러나지 않았다.
> **원칙: 프로덕션 코드는 `Dev/`의 타입에 의존하지 않는다.** 현재 예외가 하나 있다 — `GeumeumBootValidator`가 `RequireInScene<FrameRecorder>`로 `Main` 씬에 측정기가 있을 것을 강제한다(의도 여부 미확인). 새 예외를 만들지 않는다.

`[예정]` 항목의 구현 계약은 기능별 상세 설계에 있다 — [PlayerAttack](unity/PLAYER_ATTACK_DESIGN.md) · [보스](unity/BOSS_DESIGN.md) · [결계 · 인벤토리 · 상점](unity/BARRIER_INVENTORY_SHOP_DESIGN.md) · [몬스터 패링 연결](unity/MONSTER_PARRY_LINK_DESIGN.md) · [애니메이션 파이프라인](unity/ANIMATION_PIPELINE_DESIGN.md).

### 4.0 00.Common — 공용 (소유: 팀장, 변경은 팀 공지 후)

```
00.Common/
├ Data/                    namespace Geumeum.Data — GameDB 정의 → 6장
│  ├ GameDatabaseSO.cs     SO  루트 DB. 모든 *SO 참조를 모은다
│  ├ GameDB.cs             C#  정적 접근점 (GameDB.Enemy, GameDB.Barrier …)
│  ├ GameDBBootstrap.cs    MB  루트 DB를 한 번 초기화하고 누락 시 즉시 실패시킨다
│  ├ CombatConfigSO.cs     SO  스태미나 · 신력 · 이동 · 구르기 · 패링 (HP는 제외)
│  ├ ArrowConfigSO.cs      SO  화살 발사각 보정 · 유도 각도 · 지속 시간
│  ├ EquipmentSO.cs        SO  등급별 롤 범위, 무기별 특수옵션 풀
│  ├ DropTableSO.cs        SO  필드 레벨별 등급 가중치
│  ├ EnemySO.cs            SO  잡귀 3종 스탯, 결계 단계별 강화 배수
│  ├ BarrierPhaseSO.cs     SO  결계 단계별 시간 · 반경 · 피해량
│  ├ SpawnConfigSO.cs      SO  구간별 동시 존재 수 · 구성 비율 · 리스폰 주기
│  ├ BossPatternSO.cs      SO  보스 페이즈별 패턴 · 쿨다운 · 예고·판정 정보
│  ├ TalentTreeSO.cs       SO  특성 노드 구성과 수치
│  ├ ShopSO.cs             SO  객주 상품 목록과 가격
│  ├ LevelCurveSO.cs       SO  레벨별 필요 경험치 + 최대 HP 전체 (1레벨 기본값 포함)
│  ├ ScoreRuleSO.cs        SO  점수 항목별 배점
│  ├ DayCycleSO.cs         SO  실시간↔게임 내 시각 매핑, 조명 프리셋 (로컬 전용)
│  ├ SoundCatalogSO.cs     SO  [구현완료] SoundKey → 클립 · 믹서 그룹 · 볼륨 · 중복 제거 간격.
│  │                            믹서 그룹 5개 참조를 여기가 들고 있다 (로컬 전용) → 사운드 설계 4.2
│  └ CameraConfigSO.cs     SO  [구현완료] 카메라 거리 · 높이 · 오버숄더 · 감도 기본값 · pitch 한계 ·
│                               충돌 레이어와 반경 · FOV · 셰이크 6종 · 락온 거리 3종
│                               (로컬 전용) → 카메라와 락온 2장 · 3.1 · 7장
├ Constants/               namespace Geumeum.Common — 코드 계약값 (partial class)
│  ├ Constants.cs          공통: 레이어, 태그, 씬 이름
│  ├ Constants.player.cs   입력 키/축, 판정 epsilon
│  ├ Constants.combat.cs   히트스톱 등 전투 계약값
│  ├ Constants.network.cs  틱 레이트, 동기화 상수
│  └ Constants.sound.cs    [구현완료] 효과음 소스 수, 믹서 노출 파라미터 이름, dB 범위
├ Utility/                 namespace Geumeum.Common
│  ├ PersistentSingleton.cs  MB  DDOL 싱글턴 베이스 (제네릭 MonoBehaviour — 코딩 컨벤션 18)
│  ├ SceneSingleton.cs       MB  씬 로컬 싱글턴 베이스 (제네릭 MonoBehaviour)
│  ├ DamageTargetResolver.cs C#  [구현완료] 콜라이더 → IDamageable 해석 창구. 부위 콜라이더와
│  │                             몸통이 같은 NetworkIdentity일 때의 중복 제거를 여기서만 한다
│  ├ CombatFeedbackLocator.cs static ICombatFeedback 등록·조회. 기본값은 아무것도 하지
│  │                             않는 NullCombatFeedback → 8.3
│  ├ BarrierStateLocator.cs  static [구현완료] IBarrierState 등록·조회. 기본값은 결계가 꺼진 것으로
│  │                             답하는 NullBarrierState라, 결계 구현 전에도 전투·AI·아트가 돈다
│  ├ RunTimeLocator.cs       static [구현완료] IRunTimeSource 등록·조회. RunManager가 OnEnable에서 등록하고
│  │                             기본값은 0초에 멈춘 NullRunTimeSource다
│  └ RendererTintRegistry.cs static [구현완료] 렌더러 색 덮기(MaterialPropertyBlock)를 한곳에서 관리.
│                                원래 블록은 첫 요청 때 한 번만 저장하고 RendererTintLayer 우선순위
│                                (ParryWindow < Damage)로 적용한다. 패링 창 강조와 피격 플래시가
│                                겹쳐도 색이 굳지 않는다 (2026-09-18. 회귀: 05.Art/Editor/CombatHitFeedbackTests)
├ Events/                  namespace Geumeum.Common — 파트 간 이벤트 계약 → 8장
│  ├ IGameEventBus.cs      C#  타입이 지정된 이벤트 발행 · 구독 계약
│  ├ GameEventBus.cs       MB  @Managers가 소유하는 서버/로컬 이벤트 허브.
│                              SceneSingleton<GameEventBus>를 상속한다
│  ├ RunResumedEvent.cs    struct [구현완료] 얼었던 런이 다시 도는 순간의 정지 시간(초).
│  │                           절대 시각으로 잡아 둔 타이머는 각자 이만큼 민다
│  └ RunPhaseChangedEvent.cs struct [구현완료] 구간 전환 통지
├ Interfaces/              namespace Geumeum.Common — 파트 경계 계약 → 8.2
│  ├ IRunTimeSource.cs     C#  [구현완료] 두 시계와 정지 사유를 읽는 창구. RunManager가 구현한다
│  ├ IRunStateCarrier.cs   C#  [구현완료] 참가자 값을 담고 되붓는 계약. 이탈·재접속·씬 전환이 같은 것을 쓴다
│  ├ IRunSceneBinding.cs   C#  [구현완료] 씬이 바뀔 때 참조와 구독을 다시 거는 계약
│  ├ ICombatLoadoutProvider.cs C# [구현완료] 장비·특성 → 유효 전투 수치 계약. InventoryController가 구현하고 CombatStatCalculator가 장비·성장·39특성을 합성한다
│  ├ IRunResultProvider.cs C#  [구현완료] 점수 계산. Systems가 순수 함수로 구현한다
│  └ (그 밖에 IDamageable · IBarrierState · IRecoveryTarget · ICombatFeedback 등)
├ Types/                   namespace Geumeum.Common — 파트 공용 열거형·값 타입
│  ├ DamageInfo.cs         struct 서버가 확정한 피해량 · 공격자 netId · HitStrength
│  ├ HitStrength.cs        enum 타격감 강도 (Light · Heavy · Critical)
│  ├ WeaponKind.cs         enum 무기 종류 = 직업 (Sword · Bow · Bell)
│  ├ WeaponKindExtensions.cs C# 무기 → AttackReach 변환. 무기가 판정 방식을 정한다
│  ├ AttackReach.cs        enum 기본 공격의 판정 방식 (None · Melee · Ranged)
│  ├ CharacterKind.cs      enum 캐릭터 = 직업 (Chasa · Singung · Munyeo). WeaponKind와 1:1
│  ├ EquipmentOptionKind.cs enum 장비 옵션 종류. 화살 속도 · 유도 시간 · 관통 수 포함
│  ├ EnemyKind.cs / EnemyVisualState.cs / ItemGrade.cs / ShopItemKind.cs
│  ├ RunPhase.cs / RunOutcome.cs / ParryWindowKey.cs
│  ├ SoundKey.cs / SoundChannel.cs / BgmTrack.cs [구현완료] 사운드 키 → 사운드 설계 2.2 · 3.1
│  ├ PlayerPlatformId.cs   struct 재접속에도 유지되는 신원. Steam은 SteamID64, 개발은 실행 인자값
│  │
│  │  ── 아래는 2026-09-06 TL-01에서 낸 런 수명 계약 ──
│  ├ RunId.cs              struct 런마다 새로 발급. 이전 판의 늦은 요청을 가른다
│  ├ ParticipantId.cs      struct 참가자의 영속 키. connectionId·netId를 키로 쓰지 않는다
│  ├ SceneRevision.cs      struct 씬 전환 세대. 이전 전환의 늦은 로드 통지를 막는다
│  ├ BindingGeneration.cs  struct 플레이어 오브젝트 세대. 옛 몸의 요청을 막는다
│  ├ RunSceneId.cs         enum 런 흐름의 씬 일곱 개
│  ├ RunMode.cs            enum Standard · BossTrial · Dev. 리더보드 등록 여부가 갈린다
│  ├ RunPauseReason.cs     enum [Flags] 정지 사유. 하나라도 남으면 재개하지 않는다
│  ├ RunClockState.cs      struct 시계 하나의 상태. 필드와 보스가 각각 갖는다
│  ├ TargetRef.cs          struct 런·씬 세대까지 실은 대상 참조. 본체와 부위를 같은 방식으로 지목
│  ├ PlayerLifeSnapshotState.cs enum 보관용 생명 상태. 변환은 Combat의 어댑터가 한다
│  ├ WeaponCombatSnapshot.cs struct 지금 무기의 확정 값 → 결계·인벤토리·상점 설계 4.3
│  ├ CombatStatsSnapshot.cs struct 레벨·장비·특성을 반영한 유효 전투 수치
│  ├ RunParticipantSnapshot.cs class 씬 전환·재접속 사이에 참가자 값을 담는 그릇 (서버 전용)
│  ├ RunResultSnapshot.cs  struct 확정된 런 결과. 정산과 리더보드는 이것만 읽는다
│  └ SceneTransitionSnapshot.cs struct 전환 한 번의 내용. 참가자 복구 여부를 여기서 정한다
├ Pooling/                 namespace Geumeum.Common — 로컬 표현 오브젝트 풀
│  ├ LocalObjectPoolService.cs MB [구현완료] 프리팹별 ObjectPool<GameObject>.
│  │                              PersistentSingleton을 상속한다. 플레이어 공격 연출(PlayerCombatPresentation)이 쓴다.
│  │                              부모가 꺼지는 중 반납이 거부되면 다음 프레임에 옮기고, 파괴된 인스턴스는 대여에서 버린다
│  │                              (2026-09-16, 보스 입장 후 호스트 연결 끊김 수정. 회귀: 00.Common/Editor/LocalObjectPoolServiceTests)
│  └ LocalPoolRoot.cs      MB  [구현완료] 풀에 넣어도 되는 프리팹임을 선언하는 표식.
│                              네트워크 개체를 걸러내는 화이트리스트다
├ CameraFrame.cs           static [구현완료] 카메라 yaw · 입력 허용 플래그 · 락온 상태 셋 ·
│                                 관리 창 열림 진행도.
│                                 yaw는 카메라(05.Art)가 밀고 전투(02.Combat)가 읽으며,
│                                 락온은 반대로 전투가 밀고 카메라가 읽는다 — 방향은 반대여도
│                                 "오브젝트가 아니라 값"이라는 원칙은 같다.
│                                 관리 창 진행도도 UI(06.UI)가 밀고 카메라가 읽는 같은 꼴이다.
│                                 카메라가 없으면 yaw 0이라 지금과 같이 동작한다
├ AimAssistPoint.cs        MB  [구현완료] IAimAssistTarget의 기본 구현. 로컬 오프셋 하나로
│                                 조준점을 만든다. 부위마다 하나씩 붙인다 —
│                                 안 붙이면 락온이 콜라이더 bounds.center로 폴백한다
│                                 → 카메라와 락온 3.0
├ Sound/                   namespace Geumeum.Common — 사운드 재생 (전부 로컬 전용)
│  ├ SfxPlayer.cs          static [구현완료] SoundKey + isLocalPlayer만 받아 재생한다.
│  │                              AudioSource 16개 라운드로빈 · 같은 프레임 중복 제거 ·
│  │                              일시정지 · 프리로드. 아트 구현체가 아니라 GameDB와 같은
│  │                              위상의 공용 서비스라 05.Art가 아니라 여기 둔다 → 사운드 설계 4.1
│  ├ BgmPlayer.cs          MB  [구현완료] PersistentSingleton. 소스 둘로 크로스페이드,
│  │                              무음을 둔 뒤 다음 트랙 재생(9.8의 무음 3초), 일시정지
│  ├ RunSoundDirector.cs   static [구현완료] RunPhase → BgmTrack 매핑과 일시정지 전파.
│  │                              RunManager의 SyncVar 훅이 부르고, 반대 방향 호출은 없다
│  ├ SoundBootstrap.cs     MB  [구현완료] 저장된 볼륨을 첫 프레임에 믹서로 민 뒤 스스로 지운다.
│  │                              씬 로드 직후에 넣으면 믹서가 안 깨어나 값이 덮인다
│  └ SoundVolume.cs        static [구현완료] 마스터 · BGM · 효과음 볼륨. 0~1 ↔ dB 로그 변환,
│                                 PlayerPrefs 저장, 시작 시 자동 적용. 믹서 노출 파라미터로만
│                                 바꿔 SFX_Remote의 상대 오프셋이 유지된다. UI는 Set만 부른다
└ Interfaces/              namespace Geumeum.Common — 파트 간 경계 → 8장
   ├ IDamageable.cs        피해를 받을 수 있는 대상 (플레이어 · 잡귀 · 보스)
   ├ IRecoveryTarget.cs    [구현완료] 회복을 받을 수 있는 대상. 무녀 치유(전투)와 회복 물약(시스템)이
                              쇠약 · 회복 봉쇄를 각자 계산하지 않게 하는 단일 창구
   ├ IBarrierState.cs      [구현완료] 결계의 현재 중심 · 반경 · 밖 판정 · 회복 효율.
                              시스템이 구현하고 전투 · AI · 아트 · UI가 읽는다
   ├ IParryable.cs         패링당할 수 있는 공격의 계약
   ├ IInteractable.cs      상호작용 대상 (객주, 드랍 아이템)
   ├ ICombatFeedback.cs    타격감 호출부 (전투가 호출, 아트가 구현) → 8.3
   ├ IPlayerRoleAssignment.cs 무기 · 외형을 원자적으로 확정하는 계약. 로비가 Combat 타입을
                              직접 참조하지 않도록 하는 경계다
   ├ IPlayerRunLifecycle.cs   런 경계에서 플레이어를 옮기고 상태를 되돌리는 계약
   ├ IPoolable.cs             풀에서 재사용될 때 파티클 · Trail · 코루틴을 되돌리는 계약
   ├ IParryWindowTarget.cs    패링 창을 여닫을 수 있는 대상의 계약
   ├ IDisplayNamed.cs      [구현완료] 로그·화면에 쓸 사람이 읽는 이름. PlayerNameTag가 구현하고
   │                          개발 도구(Geumeum.Dev)와 네트워크 계층이 전투 타입을 모른 채 읽는다 → 4.7
   ├ IBossTelegraphSource.cs  [구현완료] 보스 예고의 현재 값과 변경 통지 창구. 표현이 AI 구현을
   │                          참조하지 않고 예고를 읽게 하는 경계 → 4.3
   ├ IBossTrialGate.cs     [구현완료] 보스 트라이 전실이 보스 개시를 막고 있는지, 다음 런을 트라이로 시작할지.
   │                          BossTrialDirector가 구현하고 RunManager와 LobbyController가 읽는다
   ├ IWeaponBladeSource.cs [구현완료] 손에 든 무기의 손잡이 끝 · 칼끝 월드 위치. PlayerWeaponVisual이 구현하고
   │                          검기(MeleeSlashView)가 읽는다
   └ IDamagePresentationSource.cs [구현완료] 동기화된 체력이 줄었다는 알림(HealthDecreased).
                              PlayerStatus · EnemyStatus · BossStatus가 체력 SyncVar 훅에서 발생시키고
                              아트의 DamageFlashView가 구독한다. 판정 정보는 싣지 않는다
```

`Interfaces/`에는 인터페이스만 둔다. 파트가 공유하는 열거형은 `Types/`로 보낸다 — 폴더명과 내용이 어긋나면 다음 사람이 찾지 못한다.

### 4.1 01.Network — 세션 · 런 진행 (소유: 팀장)

```
01.Network/                namespace Geumeum.Network
├ GeumeumNetworkManager.cs NM  Mirror NetworkManager 상속. 호스트 생성 · 접속 · 해제,
│                              최대 3명. 플레이어 프리팹 등록과 연결 수명만 담당
├ ServerBootGate.cs        static GameDB · 런 타임라인 · Transport · 네트워크 프리팹 검사
├ LobbyController.cs       NB  준비 상태와 런 시작 게이트. 슬롯 저장과 역할 배분을 아래 두 곳에
│                              맡기고 외부에는 기존 API를 그대로 노출하는 파사드를 겸한다
├ GeumeumAuthenticator.cs  NA  접속 승인 게이트. 빌드, 신원, 입장 잠금, 중복 신원을 한 번에 본다.
│                              Mirror 96은 인증 성공 후에만 OnServerConnect를 부르므로
│                              입장 검증이 여기 있다 [구현완료]
├ Identity/
│  ├ IPlayerIdentitySource.cs  원격 연결 -> PlatformId. 트랜스포트마다 신원 출처가 달라 여기서 갈린다
│  ├ DevIdentitySource.cs   MB 개발 KCP 경로. 클라가 보낸 값을 그대로 믿으므로 개발 빌드 밖에서 거부한다
│  └ AuthMessages.cs        struct 인증 요청, 응답, 거부 사유. 형식은 2단계에서 확정했다
├ Lobby/
│  ├ LobbyRoster.cs        NB  슬롯 목록(SyncList) 소유, 신원 해석, 이탈 자리 보존과 반환
│  ├ LobbyRoleCoordinator.cs NB 배분기 호출, 역할 확정, 런 리셋, 일시정지 재개 시 역할 복원
│  └ LobbyRoster.cs 안의 LobbySlot struct  네트워크 직렬화 값 타입. PlatformId가 슬롯의 키다
├ LobbyRoleAllocator.cs    C#  무기 셔플과 외형 충돌 해결만 담는 순수 계산기. Mirror와
│                              UnityEngine에 의존하지 않아 접속 없이 결과를 검증할 수 있다.
│                              이미 가진 무기·외형은 보존해 재접속 후에도 직업이 바뀌지 않는다
├ RunSceneDirector.cs      NB  [구현완료] 씬 전환 소유자. Freezing -> Capturing -> Loading ->
│                              WaitingForParticipant -> Binding -> Restoring -> Committing.
│                              전환 중에는 SceneTransition 정지 사유를 걸어 판정을 멈춘다.
│                              참가자 식별은 연결 번호가 아니라 인증기의 PlatformId를 쓴다
├ SessionPersistence.cs    MB  [구현완료] 붙은 오브젝트를 DontDestroyOnLoad로 옮긴다.
│                              이것이 없으면 씬이 바뀔 때마다 런 매니저가 새로 생겨 런 상태를 잃는다
├ RunManager.cs            NB  런 상태 머신 + 경과 시간 → 5장. 프로젝트의 심장
├ RunPhase.cs              C#  enum { Lobby, Farming, Shrink1, Shrink2, Shrink3, Boss, Result }
│                              직렬화되므로 명시값 고정, 재배열 금지
├ RunPhaseChangedEvent.cs  struct 서버 프로세스 내부 phase 전환 payload
├ RunPhaseTimeline.cs      C#  phase 경계 계산 · 순서/연속성/런 길이 불변식 검사
├ RunTimelineBuilder.cs    static BarrierPhaseSO를 순수 타임라인으로 변환
├ ReviveController.cs      NB  [구현완료] 실물은 02.Combat/Player/에 있다. 관전 30초와 협동 부활(3m·3초),
│                              부활 위치 보정. PartyWalletLocator.TryApplyReviveLoss로 사망 세대별 차감.
│                              생명 상태와 구조는 PlayerStatus/ReviveController, 참가자 집계와 런 종료는 RunManager가 담당한다
├ NetworkPeerLabel.cs      static [구현완료] 서버 로그에 사람을 적는 방법 한 곳.
│                              IDisplayNamed로 스팀 이름을 찾고 connectionId를 뒤에 남긴다 -
│                              번호만 적으면 3인 테스트에서 누구였는지 매 판 다시 맞춰야 한다
├ Dev/                     개발 전용 도구 (namespace Geumeum.Network 유지)
│  ├ FrameRecorder.cs      MB  [구현완료] 이름과 달리 영상 녹화가 아니라 **성능 측정기**다.
│  │                           F9/F10 · 전체 #if 가드. Main · Dev_Combat · Dev_Barrier에 배치.
│  │                           ⚠️ GeumeumBootValidator가 Main 씬에 이것을 강제한다(4장 머리말)
│  └ SteamLobbyHUD.cs      MB  [구현완료] 로비 ID 표시·참가용 개발 오버레이. 출시 로비 UI가 아니다.
│                              전체 #if 가드. Dev_Combat에 배치
├ Steam/                   [구현완료] FizzySteamworks 경로
│  ├ SteamManager.cs       MB  SteamAPI 초기화와 수명 (Steamworks.NET 표준 구현)
│  ├ SteamLobbyService.cs  MB  로비 생성·참가·멤버·데이터·초대. 로비 상태의 단일 출처
│  ├ SteamSessionCoordinator.cs MB 로비와 Mirror 접속을 잇는 세션 단계
│  ├ SteamMirrorConnector.cs MB 세션 게시 → StartHost → 접속 신호 발송
│  └ SteamTransportSelector.cs MB 실행 인자로 KCP/Steam 트랜스포트를 고른다
└ AuthorityTickScheduler.cs NB [구현완료] 서버 전용 공용 시뮬레이션 틱. `FixedUpdate`에서
                               등록 순서(우선순위 정수)대로 콜백 실행:
                               입력 적용(이동) → AI → 투사체 → 능력 판정 → 자원 회복.
                               Rigidbody 조작이 물리 스텝 안에서만 안전하므로 물리 주기를
                               틱 경계로 삼는다 (Fixed Timestep = `SERVER_TICK_INTERVAL`)
```

현재 ReviveController를 포함한 네트워크·복구 경로가 구현되어 있다.
`RunManager.CoServerTick`은 `NetworkTime.time`으로 경과 시간과 phase만 갱신하는 런 시계다.
플레이어·AI·투사체를 함께 실행하는 공용 시뮬레이션 스케줄러로 간주하지 않는다 — 그 역할은
`AuthorityTickScheduler`로 분리한다(2026-08-30 결정). `RunManager`는 phase·타이머 책임만
유지하고, 시스템이 5개 미만이라 별도 이벤트 큐 같은 과설계는 하지 않는다.

### 4.2 02.Combat — 플레이어 · 패링 · 화살 (소유: 팀장)

```
02.Combat/                 namespace Geumeum.Combat
├ Player/
│  ├ PlayerController.cs   NB  [구현완료] 이동 입력 Command, sequence 검증, 구르기.
│  │                           서버가 Rigidbody 속도로 이동시키고 Y는 중력에 맡긴다
│  ├ PlayerMotionModel.cs  C#  [구현완료] 서버와 클라 예측이 공유하는 순수 이동 계산
│  ├ PlayerMoveInput.cs    C#  [구현완료] 순서 · 추정 서버 시각을 포함한 입력 스냅샷
│  ├ PlayerPrediction.cs   MB  [구현완료] 매 프레임 로컬 예측 · 미확인 입력 버퍼 · 3단계 재조정
│  │                           (데드존 · 지수 수렴 · 즉시 스냅)
│  ├ PlayerActionGate.cs   C#  [구현완료] phase · 생명 상태 · 무기 · 진행 중 행동 · 쿨다운 · 자원 공통 검증
│  ├ PlayerActionPipeline.cs C# [구현완료] 단발 행동의 유일한 서버 경로. 중복 검사 → 게이트 →
│  │                           자원 차감 → 커밋을 이 순서로만 밟게 하고 거부 사유를 돌려준다.
│  │                           Mirror 비의존이라 테스트에서 그대로 부른다. 패링은 지나지 않는다
│  ├ IPlayerAction.cs      C#  [구현완료] 행동 하나가 파이프라인에 제공하는 세 조각.
│  │                           구현체는 NetworkBehaviour 자신이다(요청마다 객체를 만들지 않는다)
│  ├ ActionRejection.cs    enum [구현완료] 거부 사유 7종. 조용한 return과 "입력이 씹혔다"를 가른다
│  ├ LocalCombatState.cs   static [구현완료] 로컬 전투 상태를 읽는 창구. HUDController의 자원 바는 현재 PlayerStatus.Local을 직접 구독한다.
│  │                           이산 전이는 이벤트, 연속값은 프로퍼티. 자기 캐릭터 확보 ·
│  │                           늦은 구독 재생 · 재접속 시 구독 이관을 여기서 한 번에 푼다
│  ├ PlayerPresentation.cs NB  [구현완료] 방향 · 속도를 명시적으로 보관하는 표현 상태.
│  │                           서버는 수락한 이동, 소유 클라는 입력, 원격은 SyncVar
│  ├ PlayerAnimationController.cs MB [구현완료] PlayerPresentation을 읽어 LateUpdate에서
│  │                           VisualPivot을 회전시키는 표현 전용. 위치 차분을 쓰지 않는다.
│  │                           무기별 Animator를 골라 씌우고(짝이 없으면 모델 기본값 폴백)
│  │                           구르기 · 공격 모션 재생 진입점을 제공한다
│  ├ PlayerLifeState.cs    enum [구현완료] Alive · Downed · Spectating · ReviveInvulnerable
│  ├ PlayerStatus.cs       NB  [구현완료] HP · 스태미나 · 신력 · 생명 상태 SyncVar, IDamageable · IRecoveryTarget
│  ├ PlayerWeapon.cs       NB  [구현완료] 현재 무기 SyncVar(초기 1회 배분). 훅이 무기 모델과
│  │                           애니메이터를 함께 갱신한다. IRunStateCarrier(순서 11) —
│  │                           씬 전환 뒤 무기를 되붓는다. [구현됨] ICombatLoadoutProvider·CombatStatCalculator로 장비/특성 반영
│  │                           (CombatStatsSnapshot · WeaponCombatSnapshot · ICombatLoadoutProvider 계약은 이미 있음)
│  ├ PlayerWeaponVisual.cs MB  [구현완료] 활성 캐릭터 모델의 오른손 뼈에 무기 모델을 붙이는 표현 전용.
│  │                           캐릭터 x 무기별 부착 보정은 Player.prefab에 있다. IWeaponBladeSource 구현:
│  │                           부착할 때 가장 큰 메시의 긴 축에서 손에 먼 끝을 칼끝으로 한 번 찾아 둔다
│  ├ PlayerCharacter.cs    NB  [구현완료] CharacterKind SyncVar 훅으로 캐릭터 모델을 켜는 표현 전용.
│  │                           차사 · 신궁 · 무녀 3종 바인딩 완료. 캐릭터가 무기를 정한다.
│  │                           IRunStateCarrier(순서 10) — 씬 전환 뒤 캐릭터를 되붓는다
│  ├ HealController.cs     NB  [구현완료] 물약 사용 입력과 서버 회복 요청
│  ├ PlayerRollController.cs NB [구현완료] 구르기 Command와 서버 실행 · 무적 구간
│  ├ PlayerRunAgent.cs     NB  [구현완료] 런 경계에서 플레이어 상태를 되돌리는 창구(IPlayerRunLifecycle)
│  ├ PlayerActionContext.cs C# [구현완료] PlayerActionGate가 읽는 검증 입력 묶음
│  ├ RequestIdTracker.cs   struct [구현완료] 요청 ID의 중복 · 역순을 막는다. 가변 struct이므로
│  │                           readonly 필드로 잡으면 방어 복사본이 생겨 동작하지 않는다
│  ├ PlayerAttack.cs       NB  [구현완료] 공격의 유일한 클라이언트 Command 진입점. 근접 부채꼴과
│  │                           화살 발사를 AttackReach로 갈라 서버가 판정하고, 수락 시점에
│  │                           RpcAttackStarted로 공격 시작을 모든 화면에 전파한다.
│  │                           [구현완료] 방울 범위 공격·신력 획득·표식 부여와 HealController의 범위 치유
│  │                           [구현됨] 장비·특성에 따른 전투 스탯 계산과 행동 적용
│  ├ 상호작용 요청은 04.Systems/Interaction/InteractionController와 Net partial 참조
│  └ LockOnController.cs  NB  [구현완료] 락온 대상 선택(로컬)과 CmdSetLockOn 서버 검증.
│                             대상은 (netId + 부위 번호) SyncVar 둘로 확정하고,
│                             PlayerPresentation에 대상 Transform을 넘겨 FacingDirection을
│                             덮어쓴다. 카메라에는 CameraFrame으로 값만 민다 → 아래 정정 참고
├ Weapon/                  [예정] 직업별 실행 계약. Mirror 비의존
│  ├ IWeaponAbility.cs / IAbilityRuntime.cs
│  ├ AbilityRequest.cs / AbilityResult.cs
│  ├ TargetQuery.cs / ProjectileSpawnRequest.cs
│  └ [예정] SwordAbility.cs / BowAbility.cs / BellAbility.cs / SwordSwingJudge.cs
├ Parry/
│  ├ ParryController.cs    NB  [구현완료] 추정 서버 시각 Command, request/window 검증·소비·수신 마감 처리.
│  │                           window ID는 공격자별 단조 증가 uint, 대상당 활성 창 1개
│  │                           (새 창이 이전 미소비 창을 무효화)
│  ├ ParryJudge.cs         C#  [구현완료] 시간 창 참·거짓만 계산하는 순수 판정
│  ├ ParryRequest.cs / ParryWindow.cs / ParryResultCode.cs [구현완료] 요청 · 창 · 결과 코드 값 타입
│  └ ParryGauge.cs         NB  [구현완료] 반격 표식·대상·만료와 R 예약·소비. 구 게이지 API는 활성 규칙 아님.
│                              치명 배율 roll은 PlayerAttack이 읽어 적용한다.
│                              빗나가면 소비하지 않는다(2026-09-03 확정)
├ Arrow/
│  ├ ArrowProjectile.cs    NB  [구현완료] 서버 물리 틱마다 SphereCast로 훑는 궤적 · 명중 · 관통.
│  │                           netId로 대상 중복을 막는다. [구현완료] 장애물 판정 · 락온 유도
│  │                           (TryFindWorldBlock · UpdateGuidance; 활성 WorldMask는 ArrowConfigSO.asset에서 확인)
│  ├ ArrowAimGeometry.cs   static [구현완료] 활 조준 수렴 계산. 카메라 중심 ray에서 총구보다 가까운 충돌은
│  │                           조준점에서 빼고, 조준점을 못 구하면 카메라 방향 폴백을 정한다.
│  │                           PlayerAttack 기본 공격과 정밀 사격이 공유 (회귀: 02.Combat/Player/Editor/ArrowAimGeometryTests)
│  ├ ArrowLauncher.cs      NB  [예정] 별도 스폰 실행기. 지금은 PlayerAttack이 직접 띄운다
│  └ ArrowGuidance         [구현완료] 독립 클래스는 없고 ArrowProjectile.UpdateGuidance에 구현되어 있다.
│                              별도 클래스가 아니라 ArrowProjectile의 서버 틱 안에서 처리한다.
│                              유도가 락온의 공통 기능이라 연사 · 관통 특성의 축이 되지 않는다
│                              → 카메라와 락온 12장 · 화살 조준 보정 설계 5.4
└ Dev/                     개발 전용 도구 (namespace Geumeum.Combat 유지)
   ├ CharacterPreviewRig.cs MB [구현완료] 네트워크 없이 캐릭터 3종 × 무기 3종과 애니메이션을 확인하고
   │                           무기 부착 보정을 조정해 Player.prefab에 저장하는 리그.
   │                           Dev_Character 씬 전용. 판정 · 스태미나 · 동기화는 확인할 수 없다
   ├ ParryTestDummy.cs     MB  [구현완료] 패링 검증용 더미. Dev_Combat에 배치.
   │                           IParryable 구현이라 #if로 감싸면 계약이 깨진다 — 감싸지 않는다
   └ DevCheatPanel.cs      MB  [구현완료] 무한 자원(체력·스태미나·신력) 치트 패널.
                               내 캐릭터에만 걸고 판정은 서버가 한다 (PlayerStatus의 #if 구간)
```

> 🔧 **2026-09-05 정정** — 이 트리에 `02.Combat/Camera/`가 있었고 그 아래 `CameraRig.cs [예정]`과
> 폐기 표시된 `LockOnSystem.cs`가 있었다. **둘 다 틀렸다.** 카메라 리그는 `05.Art/Camera/`가 소유하고
> (`02.Combat`이 `05.Art`를 참조하지 않도록 `CameraFrame`의 yaw 값만 읽는다), 락온은 2026-09-05에
> 부활해 `Player/`의 `LockOnController.cs`가 된다 — 대상 선택은 로컬이지만 `CmdSetLockOn`은
> 서버가 검증하고, 대상 방향이 `PlayerPresentation.FacingDirection`을 덮어써 남의 화면에서도 옳게 선다
> → [카메라와 락온](unity/CAMERA_AND_LOCKON.md) 1장 · 3장 · 11장
>
> **2026-09-05 구현 완료.** `[예정]` 표시를 지웠다. 함께 들어온 것은 `00.Common/AimAssistPoint.cs`
> (계약의 기본 구현)와 `CameraFrame`의 락온 값 셋이다. **`CameraFrame`은 이제 양방향이다** —
> yaw는 `05.Art → 00.Common → 02.Combat`, 락온은 `02.Combat → 00.Common → 05.Art`로 흐른다.
> 어느 쪽도 상대 어셈블리를 참조하지 않으므로 8.1 의존 방향은 그대로다

2026-09-07 제공 코드에는 PlayerAttack·HealController·LockOnController·ParryController·ArrowProjectile가 있다.
파일 수는 구현 완료율이 아니며, 현재 목록은 실제 폴더를 검색한다.
아직 없는 것은 `InteractionController`, 별도 `Weapon/` 실행 클래스와 `ArrowLauncher`다.
카메라 클래스는 `05.Art/Camera/`에 있고, 화살 유도는 ArrowProjectile 내부에 있다.
나머지는 구현 계약이다. **판정 수식은 순수 C# 클래스에 두고, NetworkBehaviour는 입력·서버 실행·
동기화 경계를 담당한다.** 활 공격도 `PlayerAttack`의 공통 Command를 거치며, `ArrowLauncher`가
별도의 클라이언트 요청 경로를 만들지 않는다.

**현재 공격 값은 `CombatConfigSO`·`ArrowConfigSO`를 읽는다.** `WeaponAbilityConfigSO`와
장비/특성 provider의 실제 구현은 계획이다. 스키마가 있는 것과 실행에 연결된 것은 구분한다.
현재 `HealController`는 범위 치유이며, 종전 단일 대상 치유·버프 임시값 표는 현재값의 근거로 쓰지 않는다.

| 항목 | 현재 근거 |
|---|---|
| 검·활·방울 자원/범위 | CombatConfigSO 및 PlayerAttack의 소비 경로 |
| 치유 자원·범위·회복량·쿨다운 | CombatConfigSO 및 HealController. 신규 필드의 YAML 저장 여부와 실제 로드값은 별도 확인 |
| 화살 속도·판정 반경·유도 | ArrowConfigSO 및 ArrowProjectile |
| 버프·장비/특성 변형 | 계획. 상세 계약은 PLAYER_ATTACK_DESIGN을 참조 |
| 입력 발신·만료·재조정 | PlayerController / PlayerPrediction의 현재 상수를 참조. 밸런스 SO와 성격이 다름 |

무기 교체는 `ReviveInvulnerable` 중에도 허용한다 — 공격·구르기 중이 아니면 막을 이유가 없고,
그 외 상태에서는 애초에 교체 요청 자체가 게이트에서 거부되므로 진행 중 능력을 취소하는
로직은 따로 두지 않는다. `WeaponCombatSnapshot`의 직렬화 계약은 4.4절 참조.

### 4.3 03.AI — 잡귀 · 보스 · 스폰 (소유: 몬스터·보스 파트 @oname36-creator, 네트워크 코드: @AidenKim923)

```
03.AI/                     namespace Geumeum.AI
├ Enemy/
│  ├ EnemyAnimationController.cs MB [구현완료] 잡귀 애니메이션 파라미터 갱신 (로컬 전용)
│  ├ EnemyParryWindowFlash.cs MB [구현완료] 실제 패링 판정 구간(근사치) 동안 렌더러 강조색 표시 (로컬 전용, 이슈 #52).
│  │                           색 덮기와 복원은 RendererTintRegistry(ParryWindow 층)에 맡긴다
│  ├ EnemyController.cs    NB  잡귀 공통: 추적 · 공격 실행 · 사망 → 드랍/경험치 이벤트 발행.
│  │                           EnemySO의 종류 필드로 구동 (종류별 상속 대신 데이터 구동)
│  ├ EnemyStatus.cs        NB  HP · 최대 HP SyncVar + 강화 배수 적용. IDamageable 구현.
│  │                           HitByLocalPlayer TargetRpc는 정의만 남아 있고 지금은 보내지 않는다(2026-09-25, 아래 EnemyHpBar)
│  │                           HP 감소 훅이 IDamagePresentationSource.HealthDecreased를 발생시킨다 (각 화면 피격 플래시)
│  │                           배수는 스폰 시점의 결계 단계로 확정하고 이후 재계산하지 않는다
│  │                           (교전 중 최대 HP가 변하면 체력바가 어긋난다 — 게임 디자인 5.2)
│  ├ EnemyBrainBase.cs     C#  행동 결정 베이스 (서버에서만 Tick)
│  ├ HeokkaebiBrain.cs     C#  허깨비: 근접 추적, 무리 행동, 긴 선딜
│  ├ ChanggwiBrain.cs      C#  창귀: 돌진 → 경직. 선딜이 읽히는 패링 교보재 — IParryable
│  ├ AekgwiBrain.cs        C#  액귀: 사거리 유지 · 후퇴, 저주 투사체 시전
│  ├ CurseProjectile.cs    NB  액귀 투사체. 서버 스폰 · 서버 판정 (표현만 클라)
│  └ Editor/EnemyPresentationBindingTests.cs  표현 컴포넌트가 IEnemyPresentationSource 현재값을 구독·재활성화 때 복원하는지 EditMode 검증
├ Spawn/
│  ├ SpawnManager.cs       NB  RunPhase별 동시 존재 수 · 리스폰 타이머 · 구성 비율 관리.
│  │                           결계 축소 시 범위 밖 클러스터 비활성화
│  └ SpawnCluster.cs       MB  씬에 배치되는 거점 마커 (3~5마리). 위치 고정 · 구성 랜덤
└ Boss/
   ├ BossController.cs        NB [구현완료] 페이즈 전환(EnterHealthRatio) · 대상 스캔(RefreshTarget) ·
   │                              공격 instance ID 발급 · 그로기 게이지 상태머신(Active/Groggy/Immune)
   │                              관리, 판정 자체는 현재 페이즈 상태에 위임.
   │                              [구현완료] 예고는 BossTelegraphCue SyncVar 하나로 전달하고
   │                              IBossTelegraphSource로 공개한다(빈 RPC 두 개를 대체).
   │                              [구현완료] 전환 잠금(TransitionLockDuration) · 그로기 본체 피해 배율 ·
   │                              정지(비활성화, 재개 가능)와 종료(사망 · 런 이탈 · 스폰 해제) 분리.
   │                              [구현완료] 무녀 표식 공명 카운터(패턴 종료 시 1회).
   │                              [구현됨] 신궁 정밀 카운터. 예고 표현·동시 cue 품질과
   │                              동기화 주기는 현재 구현과 L3에서 확인
   ├ BossStatus.cs            NB [구현완료] 보스 HP SyncVar + 현재 본체 피해 배율. IDamageable.
   │                              HP 감소 훅이 IDamagePresentationSource.HealthDecreased를 발생시킨다
   ├ BossPhaseStateBase.cs    C#  [구현완료] 페이즈 추상 클래스 (enum+switch 금지 — 기술 구현 4.2).
   │                              CancelActiveAttack()로 진행 중 판정 · 패링 창 · 예고를 정리하고,
   │                              Exit()는 멱등이라 종료된 페이즈는 다시 공격하지 않는다
   ├ Phase1State.cs           C#  [구현완료] 1페이즈 MVP: 오프닝 고정 시퀀스(1→2→4) 이후 거리 3단
   │                              필터 + SO 가중치 추첨. 템플릿 넷(MeleeArc·TargetedDash·
   │                              MarkedCircle·ConeBurst) SO 실행 시퀀스로 연타·엇박을 처리
   ├ Phase2State.cs           C#  [구현완료] 2페이즈: 패턴 6~10, 꼬리 파괴·25% 게이트·최종 원혼·삼켜짐 구현
   ├ BossPatternRunner.cs     C#  [구현완료] MeleeArc 부채꼴 판정(NonAlloc), netId 중복 제거
   ├ BossAttackExecution.cs   C#  [구현완료] 보스 공격 1회의 IParryable 구현체. 공격마다 새 인스턴스로
   │                              늦은 패링 통지를 차단(창귀와 동일 패턴)
   ├ BossSpawner.cs           NB [구현완료] Boss_Proxy.prefab 스폰
   └ Editor/
      └ BossExecutionTests.cs C# [구현완료] EditMode. 취소 · 종료 · 정지/재개 · 그로기 배율 ·
                                 예고 만료의 서버 계약을 고정한다
```

**`Net/` 하위 폴더 (2026-09-11, 2026-09-15 범위 축소)** — 위 NB 클래스의 서버 통신 계약(SyncVar · 동기화 컬렉션 선언, Command, 직렬화)은 같은 폴더 아래
`Net/{클래스명}.network.cs`(partial)에 있다. hook · Rpc · 클라 수명 콜백은 원본 `{클래스명}.cs`에 둔다. 규칙은 [코딩 컨벤션 9장 "네트워크 표면 분리"](coding-convention.md).
대상: `Boss/Net/` — BossController · BossPartController · BossSoulController · BossStatus,
`Enemy/Net/` — CurseProjectile · EnemyController · EnemyStatus. `BossSpawner` · `SpawnManager`는 `Net/`에 남을 멤버가 없어 해당 없음.

**중앙 `98.Servers/03.AI/` (2026-09-15)** — 서버 수명 콜백·`[Server]` 메서드·요청 검증·동기화 연결은 `98.Servers/03.AI/{Boss|Enemy|Spawn}/{클래스명}.server.cs`(partial)에 있다.
BossController·BossPartController·BossSoulController·BossStatus·BossSpawner·EnemyController·EnemyStatus·CurseProjectile·SpawnManager의 9개 partial만 둔다. 서버 경로에서 호출되는 AI·패턴·스폰 규칙 14개는 위 도메인 트리 위치에 있다(같은 날 중앙으로 옮겼다가 되돌렸다). 파일별 현재 위치는 [협업 문서의 목록](unity/SERVER_CLIENT_WORKFLOW.md#5-현재-적용-범위)을 따른다.
원본 컴포넌트의 GUID·직렬화 필드는 유지한다. **`Net/`·중앙 `98.Servers/`는 팀장(@AidenKim923), AI·패턴 판정 클래스와 원본 컴포넌트·표현은 몬스터·보스 담당(@oname36-creator) 소유다.** 원본과 `.server.cs`는 private 필드를 공유하므로 서버 상태 필드나 서버 partial이 쓰는 멤버 이름을 바꿀 때는 팀장 리뷰를 받는다. 표현 컴포넌트는 `IEnemyPresentationSource`를 읽는다.

`BossTelegraphCue.cs`(서버→클라 공격 예고 값 타입, struct)는 공용 스키마라 `00.Common/Types/`에 있다 → 4.1절.
`Boss/`의 서버 실행부(전환 · 취소 · 예고 · 그로기 판정)는 서버에서만 실행되며, 판정 로직은 **몬스터·보스 담당 @oname36-creator가, `Net/`·`.server.cs` 연결은 @AidenKim923이 작성한다.**
**갱신** — 위 문단은 "아직 파일이 없다"였으나 아래 파일이 생겼다. 다만 **실제 3인 검증은 아직이라
[구현완료]로 올리지 않았다.** EditMode와 코드 존재만 확인한 상태다.

```
03.AI/Boss/  (추가분)
├ BossPartController.cs   NB  꼬리 부위 HP와 파괴 상태. 부위별 판정과 본체 하한을 서버가 관리
├ BossPartHitbox.cs       NB  부위 콜라이더 → 부위 ID 대응
├ BossSoulController.cs   NB  원혼 생명 주기와 카운터
├ BossSoulHitbox.cs       NB  원혼 피격 판정
├ BossPatternSelector.cs  C#  ★거리 · 개별 쿨다운 · 직전 패턴 필터 후 균등 추첨
└ BossPatternExecutionSpec.cs C# SO 실행값 snapshot. ⚠️ ResolveLegacyShape에 낡은 형상 상수가
                              남아 있다(1.37/10.26 등). SO 검증기가 막아 도달 불가한 죽은 코드다
```

25% 하드 게이트·백귀야행(패턴 10)은 구현되어 있다 — BOSS_PATTERN_CONTRACT.md 8.2·4.1절 참조.

`02.Combat/Player/PrecisionShotController.cs`·`PrecisionShotRules.cs`(신궁 정밀 사격)와
`SupportBuffController.cs`·`ReviveResolutionRules.cs`도 추가됐다. 같은 이유로 미검증이다.

### 4.4 04.Systems — 장비 · 경제 · 성장 · 결과 · 상호작용 · 결계 (소유: 게임플레이 시스템 파트 @kjw1237, 네트워크 코드: @AidenKim923)

```
04.Systems/                namespace Geumeum.Systems
├ Equipment/
│  ├ 장비 결과는 00.Common/Types/WeaponCombatSnapshot과 InventorySlotSnapshot 사용
│  ├ EquipmentGenerator.cs C#  ★2단계 랜덤: 필드 레벨 → 등급 룰렛 → 롤. 서버에서만 호출.
│  │                           재조정권 = 재생성, 등급 상승권 = 상위 EquipmentSO로 교체 후 재생성
│  ├ DropDirector.cs       NB  잡귀 사망 이벤트 구독 → Generator 호출 → DroppedItem 스폰.
│  │                           보스 사망 시 FieldLevel 3 팀 보상 1회 생성
│  └ DroppedItem.cs        NB  필드에 떨어진 장비. IInteractable, 습득은 [Command] → 서버 확정
├ Inventory/
│  ├ InventoryController.cs NB 4x3 12칸 통합 인벤토리(D12). 장비 · 물약 · 권을 함께 담는다.
│  │                           SyncList로 바뀐 칸만 전송(SyncMode.Owner), 장착 · 보관 ·
│  │                           파티원 양도 전부 호스트 권위로 소유권 이전
│  │                           (동시 습득 경합은 서버 도착 순서로 해소)
│  ├ InventoryRules.cs     C#  ★Mirror 비의존 순수 규칙. 검증 후 바꿀 칸 하나만 계산해
│  │                           InventoryWrite로 돌려준다. EditMode 20건
│  ├ EquipmentTransferService.cs NB 파티원 간 장비 양도. 제안 · 수락 · 거절 · 취소를
│  │                           무권한 Command로 받고 서버가 호출자와 양쪽 revision을 검증
│  │                           [구현완료]
│  ├ Dev/InventoryTestSetup.cs  [구현완료] 개발 전용. 호스트 화면에서 참가자별로 장비를
│  │                           지급하거나 칸을 비운다. 양도 규칙을 보려면 받을 사람과
│  │                           가득 찬 사람이 필요한데 드랍이 15퍼센트라 손으로 모으기 어렵다.
│  │                           단일 씬은 SceneRevision이 0에 머물러 거래 헤더가 무효가 되므로
│  │                           서버 시작 시 1로 올린다. 이 때문에 개발 씬에는
│  │                           RunSceneDirector가 함께 있어야 한다
│  └ Editor/InventoryRulesTests.cs · InventoryControllerTests.cs · EquipmentSwapRulesTests.cs
├ Interaction/             [구현완료]
│  ├ InteractionController.cs NB 상호작용 입력과 줍기 · 장착 요청. 판정은 전부 서버
│  ├ InteractionRules.cs   C#  ★거리 · 생사 · 씬 · 비전투 유예 판정 전용 순수 C#
│  ├ DroppedItem.cs        NB  필드에 떨어진 장비. IInteractable, 습득은 [Command] → 서버 확정
│  ├ ShopInteractionPoint.cs NB 객주 지점. 서버가 개방 상태와 거리를 검사한다
│  ├ Client/InteractionClient.cs C# 로컬 후보 탐색 · G키 입력. IInteractionRequestSource로 요청만 보낸다.
│  │                          문을 열기 전 객주는 후보가 아니라 키 없는 안내(NoticeText)로 남긴다
│  └ Editor/InteractionClientBoundaryTests.cs  원격 줍기 후보가 서버 판정을 호출하지 않는지 EditMode 검증
├ Economy/                 [구현완료]
│  ├ CurrencyManager.cs    NB  팀 공용 노잣돈 단일 SyncVar. 구매 · 부활 차감 요청을
│  │                           서버 큐로 순차 처리 — 잔액 음수 방지
│  ├ CurrencyRules.cs      C#  ★지출 계획과 잔액 판정 전용 순수 C#
│  ├ ShopTransactionService.cs NB 구매 · 물약 사용 · 재조정권 · 상승권. 무권한 Command에
│  │                           호출자 검증 + revision + 재전송 방지를 걸어 원자로 처리
│  ├ TransactionRequestRules.cs C# ★요청 헤더 수명과 거부 사유 판정. run/scene/세대 대조
│  └ TransactionServiceUtility.cs C# 상점과 양도가 공유하는 호출자 · 생사 · 거리 검증
├ Growth/                  [구현완료]
│  ├ GrowthManager.cs      NB  경험치(파티 공유) · 레벨 · 특성 포인트. LevelCurveSO 참조
│  ├ GrowthRules.cs        C#  ★누적 경험치 → 레벨 · 다중 레벨업 · HP 증가분 판정
│  ├ TalentController.cs   NB  노드 찍기 · 무료 초기화 · 무기 교체 시 자동 초기화.
│  │                           효과 적용은 이벤트로 각 파트에 통지 (예: 패링 판정 창 확대)
│  ├ TalentRules.cs        C#  ★선행 노드 · 포인트 비용 · 효과 종류 판정
│  └ CombatStatCalculator.cs C# ★장비 · 성장 · 특성을 합쳐 전투 수치를 산출
├ Results/                 [구현완료] (구 Score/)
│  ├ RunStatistics.cs      NB  런 동안 서버가 확정한 처치 · 사망 · 피해 · 회복 사실 수집
│  ├ RunResultRecorder.cs  NB  결과 확정 시점에 사실을 동결해 점수 provider로 넘긴다
│  └ RunLeaderboardStore.cs C# 호스트 로컬 JSON 기록. 배타 락 · 원자적 쓰기 · 중복 제거 ·
│                             스키마 버전. **이 프로젝트에서 유일하게 영속되는 데이터**
└ Barrier/
   ├ BarrierController.cs  NB  현재 단계 · 중심 · 반경 SyncVar. 중심은 씬의 BarrierCenter 고정,
   │                          30초 반경 보간, 10초 전 경고 이벤트,
   │                          매 초 범위 밖 IDamageable에 피해 + 쇠약/회복 봉쇄 플래그
   └ BarrierJudge.cs       C#  ★"반경 밖인가 · 얼마의 피해인가" 계산 전용 순수 C#
```

**`Net/` 하위 폴더 (2026-09-11, 2026-09-15 범위 축소)** — 위 NB 클래스의 서버 통신 계약(SyncVar · 동기화 컬렉션 선언, Command, 직렬화)은 같은 폴더 아래
`Net/{클래스명}.network.cs`(partial)에 있다. hook · Rpc · 클라 수명 콜백은 원본 `{클래스명}.cs`에 둔다. 규칙은 [코딩 컨벤션 9장 "네트워크 표면 분리"](coding-convention.md).
대상: `Barrier/Net/` BarrierController · `Economy/Net/` CurrencyManager · ShopTransactionService ·
`Growth/Net/` GrowthManager · TalentController · `Interaction/Net/` DroppedItem · InteractionController · ShopInteractionPoint ·
`Inventory/Net/` InventoryController · EquipmentTransferService.
`DropDirector` · `RunStatistics`는 `Net/`에 남을 멤버가 없어 해당 없음. `RunResultRecorder`는 클라 수명 콜백만 있어 2026-09-15에 `Results/Net/` 파일을 없애고 원본에 합쳤다.

**중앙 `98.Servers/04.Systems/` (2026-09-15)** — 서버 수명 콜백·`[Server]` 메서드·요청 검증·동기화 연결은 `98.Servers/04.Systems/{기능}/{클래스명}.server.cs`(partial)에 있다.
BarrierController·CurrencyManager·ShopTransactionService·DropDirector·GrowthManager·TalentController·DroppedItem·ShopInteractionPoint·InventoryController·EquipmentTransferService·RunStatistics의 11개 partial만 둔다. 서버 경로에서 호출되는 규칙·생성기 8개는 위 도메인 트리 위치에 있다(같은 날 중앙으로 옮겼다가 되돌렸다). 파일별 현재 위치는 [협업 문서의 목록](unity/SERVER_CLIENT_WORKFLOW.md#5-현재-적용-범위)을 따른다.
**`Net/`·중앙 `98.Servers/`는 팀장(@AidenKim923), 판정 규칙·생성기와 원본 컴포넌트·`Client/`·`Presentation/` 소비자·`06.UI/`는 게임플레이 시스템 담당(@kjw1237) 소유다.** 원본과 `.server.cs`는 private 필드를 공유하므로 서버 상태 필드나 서버 partial이 쓰는 멤버 이름을 바꿀 때는 팀장 리뷰를 받는다. 상호작용의 입력·후보 탐색은 `Interaction/Client/InteractionClient.cs`로 추출했고, 기존 컴포넌트가 로컬에서 구동한다. 요청 계약 `IInteractionRequestSource`는 `00.Common/Interfaces/`에 있다.

결계는 잡귀 난이도가 아니라 런 진행에 걸린 시스템이라 이 폴더에 둔다. `SpawnManager`(4.3)와 `BarrierVisual`(4.5)은 `BarrierController`의 SyncVar를 **읽기만** 한다.

### 4.5 05.Art — 연출 · 시간대 · 결계 비주얼 (소유: 테크니컬 아트 @AidenKim923 겸임)

전부 **로컬 전용**이다. 이 폴더의 클래스가 NetworkBehaviour를 상속하면 설계 위반이다.

**재사용 참조 VFX(2026-09-23)** — `VFX/BowChargeVfxView.cs`, `VFX/BowLaserVfxView.cs`는 외부 시계로 충전·짧은 화살을 평가하고, `Boss/BeamReferenceVfxDriver.cs`는 흑심 빔의 충전·발사·종료를 구동한다. 프리팹·그래프·재질·텍스처와 함께 사용할 수 있는 현재 구현이다. 에셋 묶음에 제작 도구·게임 공격/RPC 배선은 포함하지 않는다. [사용법과 의존성](unity/vfx/REFERENCE_VFX_USAGE.md).

```
05.Art/                    namespace Geumeum.Art
├ DayCycleController.cs    MB  RunManager의 경과 시간 → DayCycleSO로 조명 · 스카이박스 · 안개
├ BarrierVisual.cs         MB  [구현됨] BarrierController의 반경 SyncVar → 원통 셰이더 · 경계 파티클
├ CombatFeedbackImpl.cs    MB  ICombatFeedback 구현체: 히트 이펙트, 셰이크, 패링 슬로우모션.
│                              OnEnable/OnDisable에서 CombatFeedbackLocator에 등록·해제 → 8.3
├ ScreenEffects.cs         MB  [예정] 저체력 · 결계 밖 · 쇠약 화면 효과
├ Boss/                    [구현완료] 보스 아레나 연출 · 애니메이션
│  ├ BossArenaPhaseView.cs MB  페이즈 루트 · 안개 · 하늘 · 바닥 머티리얼 · 달 원반을 한 번에 교체.
│  │                           판정에 관여하지 않는다. 서버 페이즈 신호에 연결하는 것은 미착수
│  ├ BossAnimationView.cs  MB  IBossTelegraphSource · IBossPhaseSource · BossStatus.Died를 구독해
│  │                           BossLocomotion.controller(05.Animator/)의 파라미터로 옮긴다.
│  │                           IsGroggy는 BossPhaseSnapshot.Stage(Groggy·WakeWarning)로 판정 —
│  │                           꼬리 표식(2페이즈 전용)에 얹지 않아 1페이즈 그로기도 자세가 나온다
│  ├ BossTelegraphView.cs  MB  [구현완료] IBossTelegraphSource.TelegraphCues(최대 8개 동시)를
│  │                           읽어 바닥 장판(부채꼴·원·레인)을 그린다. BossPatternExecutionSpec으로
│  │                           판정과 같은 반경·각도 값을 써서 눈에 보이는 범위와 실제 판정이 어긋나지
│  │                           않는다. 전용 셰이더는 BossTelegraph.shader(같은 폴더)
│  ├ BossPatternImpactVfxView.cs MB  [러프] BossController.PatternImpact(타격)·
│  │                           PatternTelegraphVfx(예고) 신호로 패턴별 VFX(ThirdParty SB_* 계열)를
│  │                           Instantiate+Destroy. 템플릿→프리팹 매핑은 인스펙터 배열 2개
│  │                           (_vfxByTemplate·_vfxByTemplateOnTelegraph)로 관리, 낙인(MarkedCircle)만
│  │                           예고·타격 둘 다 씀. 정식 프로덕션 파이프라인(PresentationConfigSO·풀링)
│  │                           미적용 — 느낌 보며 계속 교체 중
│  ├ BossPartMarkerView.cs MB  [구현완료] IBossPartSource.PartChanged(ActivePartId)를 읽어 그로기 중
│  │                           파괴 대상 꼬리 위에 빨간 마름모(대각선 1:2) 표식을 띄운다. 표식 전용
│  │                           자식 오브젝트를 만들어 그쪽만 옮기고 카메라 방향으로 돌린다 — 이
│  │                           스크립트 자신의 transform(=BossPartHitbox의 판정 콜라이더)은 안 건드린다
│  ├ BossKitsuneFormView.cs MB  [구현완료] IBossPhaseSource로 PhaseIndex를 읽어 여우 귀·꼬리
│  │                           (Model/SK_KITSUNEYUNA_EARS·TAIL)를 1페이즈엔 숨기고 2페이즈
│  │                           진입 시 드러낸다 — 정체가 드러나는 연출. 꼬리 판정 콜라이더는
│  │                           별개 오브젝트라 관여하지 않는다
│  └ BossScratchVfxDriver.cs MB  [구현완료·미연결] BossScratch.vfx(VFX Graph 7시스템)에 할퀴기
│                              이벤트를 보내는 표현 전용 구동기. 타격 횟수·간격·반경을 _strikes
│                              배열이나 SetUniformStrikes로 주입받아 1·2·3타를 같은 그래프로 낸다.
│                              판정·콜라이더·서버 통신 없음. 아직 보스 패턴에 연결하지 않았다 —
│                              서버 패턴은 1타/3타뿐이고 BossPatternImpactVfxView는 impact마다
│                              생성·-forward 회전이라 방향·횟수 계약을 먼저 정해야 한다
│                              (→ docs/unity/vfx/BOSS_DOUBLE_SCRATCH_VFX.md)
├ Camera/                  [구현완료] → 카메라와 락온 사양
│  ├ CameraRig.cs          MB  추적 · 충돌 · 셰이크 · 줌. LateUpdate.
│  │                           Target이 바뀔 수 있어야 한다 — 관전 카메라가 같은 물건이다.
│  │                           커서 잠금은 Update가 소유한다 — ESC로 CameraFrame.InputEnabled를
│  │                           뒤집는다. LateUpdate는 대상이 없으면 먼저 빠져나가 로비에서 안 먹힌다
│  └ CutsceneDirector.cs   MB  [예정] 페이즈 전환 8초. Timeline이 카메라를 잡고 끝나면 리그에 돌려준다
├ Dev/                     [구현완료] 개발 전용 도구 (namespace Geumeum.Art 유지)
│  ├ CameraTuneOverlay.cs  MB  게임을 끄지 않고 카메라 값을 만지는 F11 패널. 조정 항목은
│  │                           CameraConfigSO.Tunables가 소유한다 — 설명 문구가 필드에서
│  │                           멀어지면 값을 고칠 때 같이 안 고쳐져 거짓말이 된다.
│  │                           씬 배치 없이 RuntimeInitializeOnLoadMethod로 스스로 생긴다
│  └ CameraTuneFile.cs     static 조정값 camera-tune.json 저장 · 불러오기. 빌드에서 만진 값을
│                              에디터가 읽어 SO 정본에 반영하는 통로 → 그믐/카메라 메뉴
├ Editor/                  에디터 전용
│  ├ ArtProductionWiringHelper.cs  static 표현 에셋 생성 · 열린 씬 배선 메뉴 (Geumeum/Production/Art)
│  ├ ArtPresentationTests.cs       표현 계층 EditMode 테스트
│  ├ CombatHitFeedbackTests.cs     검기 시간표 · 초승달 호 각도와 반경 · 색 덮기 겹침 복원 · 검기 설정값 검증
│  ├ TeaserPreparationWindow.cs    [현재] 티저 준비 창 (Geumeum/Art/Teaser Preparation). 샷과 프레임별 편집 지시를 보여 주고,
│  │                               같은 씬 카메라의 시작/종료 포즈를 JSON에만 저장한다. 녹화 · 카메라 적용은 없다
│  ├ TeaserPlanDocument.cs         [현재] teaser-shots.json 읽기 · 구간 검증 · 포즈 저장. 다른 곳에서 바뀐 파일은 덮어쓰지 않는다
│  └ TeaserPlanDocumentTests.cs    포즈 왕복 · 외부 변경 거부 · 씬 불일치 · 전투 샷 배속 검증.
│                                  조립 · 편집 그래픽 · 음량 마감은 저장소 루트 tools/teaser(Remotion) → 티저 제작 설계
├ VFX/                     [구현완료] 플레이어 행동 연출과 피격 표현 (풀링, 판정 관여 없음)
│  ├ PlayerCombatPresentation.cs MB 서버가 수락한 PlayerActionCue를 받아 무기별 연출 프리팹을 풀에서 빌리고 소리를 낸다.
│  │                              검기만 플레이어에 붙이지 않고 월드에 두며, 다음 행동 때 반납하지 않고 기록만 멈춘다
│  ├ PooledVisualLifetime.cs  MB  풀 연출의 수명 · 일시정지 · 늦은 cue 되감기. 자식의 IActionCueVisual에 cue와 무기를 넘긴다
│  ├ IActionCueVisual.cs      C#  cue 시각표로 스스로 진행하는 연출의 계약. PlayCue(cue, 무기) / ReleaseCue(기록 중단)
│  ├ MeleeSlashView.cs        MB  [2026-09-18] 검 기본 공격 검기. 판정 시각 앞뒤 구간 동안 IWeaponBladeSource의 실제 검날
│  │                              (손잡이 쪽 ~ 칼끝)을 매 프레임 기록해 월드 공간 리본으로 잇는다. 애니메이션이 휘두른
│  │                              방향과 경로를 그대로 따르고, 점마다 나이를 먹여 잔상으로 지운 뒤 스스로 풀에 돌아간다.
│  │                              프레임 사이는 centripetal Catmull-Rom으로 보간 → 검기 설계(unity/SLASH_VFX_DESIGN.md)
│  └ DamageFlashView.cs       MB  [2026-09-18] IDamagePresentationSource 체력 감소에 렌더러를 잠깐 덮는다.
│                                 플레이어 · 잡귀 3종 · Boss_Proxy(_visualRoot = Model)에 붙는다.
│                                 옛 PlayerDamageFlash를 GUID 그대로 일반화했다
└ Shaders/                 셰이더
   ├ BarrierWall.shader    결계 벽 셸
   └ MeleeSlash.shader     [2026-09-18] 검기 리본. 주묵 몸통 + 한지 칼끝 테두리(색 문법 아군). 나이에 따라 옅어지고
                           수명 후반부터 노이즈로 갉아먹힌다
```

**카메라와 전투는 `CameraFrame`(`00.Common`)의 방향 및 락온 값을 교환한다.** 카메라 오브젝트를 참조하지 않으므로 `02.Combat` → `05.Art` 의존이 생기지 않는다 — 로케이터를 하나 더 만들 이유도 없다.

### 4.6 06.UI — 게임플레이 시스템(@kjw1237)이 전담

기존에는 각 파트가 자기 화면의 UI를 각자 담당했으나, 2026-08-30부로 **UI 전담자를 둔다.** 담당은 게임플레이 시스템(@kjw1237)이 겸임한다. 화면이 어느 파트 소재인지와 무관하게 UI 구현·수정은 전담자를 거친다.

```
06.UI/                     namespace Geumeum.UI
├ Combat/    HUD(HP · 스태미나 · 신력 · 반격 표식), 정밀 조준 상태, 무녀 표식 슬롯, 관전 화면
│  └ HUD/
│     ├ AllyTransferPromptView.cs [구현완료] 파티원 3m 안에 들면 그 사람 머리 위에 `양도 [G]`를 띄운다.
│     │                           거리는 GameDB.Combat.InteractRange를 읽어 서버 판정과 같은 값을 쓴다.
│     │                           줍기·객주 후보가 잡혀 있거나 로비·정산 단계면 비킨다.
│     │                           G를 받으면 FunctionalManagementPanel.OpenTransferTo로 배낭을
│     │                           양도 모드로 열 뿐이고 서버에 아무 요청도 보내지 않는다.
│     │                           호스트+봇 2인 필드 런에서 표시·드래그·양도 완료까지 확인했다.
│     │                           **전투 중에도 안내가 뜨는 것은 아직 막지 못한다** - CombatClock이
│     │                           서버에만 쌓여 순수 클라이언트가 전투 상태를 모른다. 팀장께 SyncVar를
│     │                           요청해 둔 상태다 [계획]. 3인 접속은 미검증
│     ├ HUDController.cs         [구현완료] HP · 신력 · 스태미나 바를 PlayerStatus.Local에 바인딩
│     ├ MinimapPlayerDots.cs     [구현완료] 파티원 위치를 점으로, 결계를 판과 원으로 그린다. 캐릭터별 색.
│     │                           결계 밖을 위험 색으로 덮는다 - UI에 역마스크가 없어 판 하나를 칠하고
│     │                           결계 자리만 비운다. 원이 움직인 만큼만 다시 칠한다.
│     │                           다음 축소의 목표 원은 경고 시점부터 축소가 끝날 때까지 보인다.
│     │                           _rangeMeters는 접힌 상태 200m다. 파티원 위치를 읽기 쉽게 2026-09-15에
│     │                           400에서 줄였다. BarrierPhaseSO의 최대 반경(400)보다 작아서
│     │                           결계 초반에는 결계 원이 미니맵 밖으로 나간다. 맵 배경, 회전은 아직 없다.
│     │                           펼친 지도는 MinimapExpandToggle이 500m로 바꾼다. 2026-09-21부터
│     │                           접힌 지도는 화면에 나오지 않으므로 200m는 되돌릴 값으로만 쓰인다
│     ├ MinimapExpandToggle.cs   [구현완료] Tab키로 지도를 화면 정중앙에 펼쳤다 되돌린다.
│     │                           2026-09-21 기획 변경 - 평시에는 지도를 아예 숨기고 여는 키만 알린다.
│     │                           접힌 상태 = 숨김이다. CanvasGroup 알파를 0으로 두고
│     │                           MinimapPlayerDots를 꺼서 숨은 동안 점 · 결계를 그리지 않는다.
│     │                           GameObject를 끄지 않는 이유는 이 컴포넌트가 여는 키를 계속 받아야 해서다.
│     │                           접힌 동안 좌측 상단에 HUD_MapHint(키캡 Tab + 지도)를 띄운다 -
│     │                           _closedHint에 넣은 오브젝트이고, 지도 바깥에 두어야 같이 숨지 않는다.
│     │                           확대판을 따로 두지 않고 같은 MinimapPlayerDots의 배치 · 표시 반경 ·
│     │                           점 크기만 바꾼다. 점과 결계를 두 벌 그리면 둘이 어긋날 수 있다.
│     │                           접힌 모습은 Awake에서 씬 값을 받아 두고 접을 때 그대로 되돌린다 -
│     │                           코드에 위치나 크기를 적지 않아 씬에서 미니맵을 옮겨도 따라간다.
│     │                           펼치면 배율 5배(180 -> 900px), 반경 500m, 점은 12px다.
│     │                           1080 세로 화면에서 위아래 90px만 남으므로 이보다 키우지 않는다.
│     │                           게임 입력은 막지 않는다. 지도를 펼친 채 이동 · 카메라가 동작한다.
│     │                           관리 창이 열려 HUD 캔버스가 꺼지면 접고 키를 먹지 않는다.
│     │                           지도 배경이 아직 없어 읽히게 하려고 검은 반투명 판을 임시로 깐다 -
│     │                           Backdrop 오브젝트이고, 배경이 들어오면 지운다.
│     │                           접힌 지도의 링 테두리를 숨기던 _compactOnly는 지웠다 -
│     │                           접힌 상태가 숨김이 되어 할 일이 없어졌다. 링(Ring) 오브젝트 자체도
│     │                           2026-09-21에 씬과 프리팹에서 제거했다 - 원형 링과 사각 Backdrop이
│     │                           어긋나 보였다. Minimap_Ring.png는 참조 없이 파일만 남아 있다.
│     │                           Dev_HUD 플레이 모드에서 숨김 · 확대 · 복귀 · 안내 표시를 확인했고,
│     │                           호스트 1인 접속에서 펼친 지도에 로컬 점이 찍히는 것까지 봤다.
│     │                           Tab 키 입력 경로와 3인 접속은 미확인
│     ├ LockOnReticle.cs         [구현완료] 락온한 대상의 중앙에 흰 점을 띄운다.
│     │                           LockOnController가 확정한 좌표를 화면 좌표로 옮겨 그리기만 하고
│     │                           락온 판정에는 관여하지 않는다. 대상이 카메라 뒤로 가면 숨긴다.
│     │                           호스트 1인에서 표식이 대상 중앙에 붙는 것을 확인했다.
│     │                           표식은 흰 도트와 그 바깥 번짐 두 장이다. 순수 흰색으로 빼면
│     │                           밝은 대상 위에서 묻혀서, 그라데이션이 바깥으로 갈수록 어두워진다.
│     │                           흰색, 회색, 검정 배경에서 각각 읽히는 것을 확인했다
│     ├ LockOnReticleAura.cs     [구현완료] 표식 바깥 번짐을 돌리고 숨쉬게 한다.
│     │                           HUD 캔버스가 ScreenSpaceOverlay라 ParticleSystem이 UI 위에
│     │                           그려지지 않는다. 그래서 UI 이미지를 코드로 직접 움직인다.
│     │                           크기, 회전, 투명도가 실제로 변하는 것을 확인했다
│     ├ BarrierWarningBanner.cs  [미검증] 결계 축소 예고 · 축소 중 · 결계 밖 배너.
│     │                           BarrierStateLocator snapshot만 읽고 판정은 하지 않는다.
│     │                           RunHUD.prefab(SessionManagers 안)에 배선했다. 화면 동작은 아직 관찰하지 못했다
│     ├ BossTimeWarningToast.cs  [미검증] 보스 남은 시간이 03:00 아래로 내려가는 순간만 한 번 알린다.
│     │                           조건이 참인 동안 떠 있지 않는다 - 지속 표시는 HudTimerController의 붉은 숫자가
│     │                           맡고, 여기는 "지금 막 3분이 됐다"는 사건만 다룬다. 계속 띄우면 보스 패턴을 가린다.
│     │                           임계를 넘는 순간을 직접 본 경우에만 띄운다 - 런 도중 들어온 사람에게 남은 30초에
│     │                           "3분" 문구가 뜨면 거짓말이 된다(ThresholdSide.NotObserved).
│     │                           코루틴 없이 값 하나로 세어 중복 시작·취소 문제를 만들지 않는다.
│     │                           런타임으로 오브젝트를 붙여 1인에서 표시·자동 소멸·임계 아래 재발 억제·
│     │                           도중 접속 무시·보스 재진입 시 재발동을 확인했다.
│     │                           RunHUD.prefab의 BarrierWarningBanner 아래(y=-200)에 배선했다. 화면에 뜨는 것까지 확인했다.
│     │                           3인은 아직이다 [미검증]
│     ├ HudTimerController.cs    [구현완료] 우상단 시간 슬롯의 숫자를 mm:ss로 적는다. 구간마다 방향이 다르다 -
│     │                           필드는 00:00에서 30:00까지 올라가고, 보스(RunPhase.Boss)는 14:00에서 내려간다.
│     │                           필드에 강제 종료가 없어 내려가는 숫자는 없는 마감을 만든다(2026-09-20 되돌림).
│     │                           카운트업은 내림, 카운트다운은 올림이다 - 반대로 하면 1초를 건너뛴 것처럼 보인다.
│     │                           두 상한 모두 GameDB(Barrier · Boss)가 갖고 있어 코드에 박은 수치가 아니다.
│     │                           2026-09-19 결정 "보스 씬에서는 보스 시계 14:00 카운트다운으로 교체,
│     │                           44:00은 결과창에서만"을 그대로 따른다 [구현완료].
│     │                           축소까지 남은 시간이 아니다 - 그 경고는 배너와 부적이 맡는다(HUD_DESIGN 2.2).
│     │                           보스 경과가 0인지로 구간을 가르지 않고 RunManager.Phase를 읽는다 -
│     │                           보스 시계 첫 프레임이 0이라 필드 00:00이 한 번 튀기 때문이다.
│     │                           Dev_BossTrial에서 보스 14:00 → 13:43 감소를, 일반 런에서 필드
│     │                           00:10 → 00:18 증가와 1800초 지점의 30:00 고정을 확인했다(1인).
│     │                           3인 확인은 아직이다 [미검증].
│     │                           Result 단계에서 숫자가 필드 잔여로 되돌아가던 것은
│     │                           HudVisibilityGate가 그 단계에 캔버스를 꺼서 해결했다 [구현완료].
│     │                           결계 경고·축소 구간에는 숫자를 주묵 붉은색으로 바꾼다(2026-09-20).
│     │                           축소 시각을 직접 세지 않고 BarrierStateLocator 스냅샷을 배너와
│     │                           똑같이 읽는다 - 그래야 배너와 숫자가 같은 순간에 반응한다.
│     │                           색 값은 SerializeField이고 경계 시각은 BarrierPhaseSO가 갖는다.
│     │                           스냅샷을 주입해 붉은색 전환과 흰색 복귀를 확인했다. 정상 경로의
│     │                           10:00 도달과 3인은 아직이다 [미검증].
│     │                           보스 구간에서는 남은 시간이 03:00 이하면 같은 붉은색으로 바꾼다(2026-09-21).
│     │                           결계 스냅샷을 보지 않는다 - 결계 타임라인은 보스 진입 시점에 멈추는 필드
│     │                           시계 기준이라, 축소 구간 중에 입장하면 보스전 내내 붉게 굳는다.
│     │                           임계 180초는 SerializeField, 상한 840초는 GameDB.Boss가 갖는다.
│     │                           Dev_BossTrial 1인에서 남은 189.6초 흰색 → 175.7초 붉은색을 확인했다.
│     │                           3인과 정확한 경계 시각은 아직이다 [미검증]
│     ├ HudVisibilityGate.cs     [구현완료] 관리 창이 열려 있는 동안과 RunPhase.Result에서
│     │                           HUD 캔버스를 끈다. GameObject가 아니라 Canvas만 꺼서
│     │                           숨은 동안에도 바인딩과 값 갱신은 계속된다.
│     │                           HUD는 SessionManagers 안이라 DontDestroyOnLoad로 Result 씬까지
│     │                           따라간다. 끄지 않으면 결과창 위에 전투 HUD가 남는다.
│     │                           씬이 아니라 단계로 판단한다 - 런이 끝나도 씬 전환이 늦게 온다.
│     │                           Dev_BossTrial 1인 플레이에서 Result 전환 시 씬이 아직
│     │                           Boss_Arena인 채로 캔버스가 꺼지는 것을 확인했다. 3인은 아직이다 [미검증].
│     │                           단계는 이벤트가 없어 Update에서 읽는다 - RunManager가 단계 변경
│     │                           이벤트를 공개하면 관리 창처럼 구독으로 바꾼다 [계획]
│     ├ PartyHudController.cs    [구현완료] 파티 플레이트에 누구를 그릴지 정하고 값을 물린다.
│     │                           스폰 목록만 주기로 훑고 값은 PlayerStatus 이벤트로 받는다.
│     │                           3인 접속에서 본인을 뺀 2줄이 뜨고, 한 명이 나가면 그 줄이 사라지는 것을 확인했다.
│     │                           파티창에 본인을 넣을지는 아직 정해지지 않아 _includeLocalPlayer로 빼 두었다
│     ├ PartyMemberPlate.cs      [구현완료] 파티 한 줄의 뷰. 이름 · 직업 아이콘 · 자원 바 3개.
│     │                           값만 받고 PlayerStatus도 네트워크도 참조하지 않는다.
│     │                           이름과 자원 바가 원격 플레이어의 값을 따라가는 것을 확인했다
│     ├ Editor/RunHudProductionWiringHelper.cs  Dev_HUD의 HUD · 관리 창 · 결계 벽을 SessionManagers · Field로 옮기는 도구
│     └ Dev/
│        └ HudResourceDecayTester.cs  [현재] 개발 전용. 서버가 모든 플레이어의 자원을
│                                     주기마다 조금씩 깎아 HUD가 값을 따라가는지 보게 한다.
│                                     DevToolHost에 스스로 붙고 기본은 꺼짐.
│                                     좌하단 개인 바가 움직이는 것까지 관찰했다
├ AI/        보스 바(체력 · 그로기) · 광폭화 경고 · 잡귀 머리 위 체력 바
│  ├ BossHudController.cs   [구현완료] 스폰된 보스를 찾아 체력 바 · 그로기 저항 바 · 광폭화 문구에 물린다.
│  │                        보스가 없거나 죽으면 바를 숨긴다. 스폰 목록은 주기로만 훑는다.
│  │                        최대 체력과 그로기 최대치가 서버 전용 값이라 클라이언트는
│  │                        GameDB.Boss의 MaxHealth · GroggyGaugeMax를 쓴다.
│  │                        RunHUD.prefab(SessionManagers 안)의 루트에 배선했다 -
│  │                        보스 바는 기본 비활성이라 바 위에 붙이면 스스로 켜지 못한다.
│  │                        그로기 저항이 차오를 때는 바로 대입하지 않고 1.5초에 걸쳐 따라간다 -
│  │                        서버가 기상 순간 최대치를 한 번에 되돌리는 데다 그 프레임에 기상 패턴이
│  │                        함께 시작돼 화면이 끊기므로, 한 프레임이 옮길 양에도 상한을 둔다.
│  │                        깎이는 쪽은 타격 결과라 즉시 반영한다.
│  │                        호스트 1인에서 표시, 체력 · 그로기 비율 갱신, 그로기 진입 시 빈 바와
│  │                        기상 시 차오름, 광폭화 예고 · 발동 문구, 보스 제거 시 숨김까지 확인했다.
│  │                        3인 접속(L3)은 아직 확인하지 못했다 [미검증]
│  ├ EnemyHpBar.cs          [꺼짐 2026-09-25] 잡귀 체력 바는 06.UI/Combat/EnemyHealthBars/EnemyHealthBarLayer(화면 오버레이,
│  │                        누가 때렸든 모두의 화면, 깎인 부분 잔상)로 대체했다. 서버가 HitByLocalPlayer를 보내지 않으므로
│  │                        이 바는 뜨지 않는다. 설정 창 "적 체력 바" 토글은 새 층을 켜고 끈다. 아래는 꺼지기 전 설명이다.
│  │                        내가 때린 잡귀의 머리 위에만 체력 바를 띄우고 3초 뒤 흐려진다.
│  │                        파티원이 때린 잡귀의 바는 내 화면에 뜨지 않는다. 띄우는 입구는 EnemyStatus.HitByLocalPlayer
│  │                        (서버가 공격자 연결에만 보내는 TargetRpc) 하나이고, HealthDecreased는 떠 있는 바의 값만 맞춘다 -
│  │                        RPC가 체력 SyncVar보다 먼저 오면 옛 체력으로 뜨므로 뒤따르는 훅이 바로잡는다.
│  │                        비율은 Health / MaxHealth. 최대 체력은 결계 강화 · 몰이 강화가 바꿔 밸런스 원본으로
│  │                        대신할 수 없으므로 EnemyStatus.network.cs의 SyncVar로 받는다. 처치되면 바로 숨긴다.
│  │                        잡귀 프리팹 자식(중첩 프리팹 02.Prefabs/UI/HUD/EnemyHpBar.prefab)으로 붙는다.
│  │                        그림은 파티 줄 체력 바(Bar_Track · Bar_Fill_Health)와 같다. 그림의 위아래 투명 여백이
│  │                        커서 캔버스를 70 x 165로 잡아야 띠가 읽힌다.
│  │                        Dev_Combat 호스트 1인에서 내 타격만 표시 · 연결 없는 공격자 타격 미표시 · 떠 있는 바의
│  │                        값 갱신 · 비율 · 3초 뒤 사라짐 · 처치 시 숨김을 확인했다.
│  │                        원격 클라이언트에서의 TargetRpc · SyncVar 도착(L3)은 아직이다 [미검증]
│  └ Editor/EnemyHpBarWiringHelper.cs  메뉴 그믐/Production/UI/Wire enemy HP bars.
│                           바 프리팹이 없으면 만들고 허깨비 · 창귀 · 액귀 프리팹의 콜라이더 꼭대기 0.1m 위에 붙인다.
│                           이미 붙어 있으면 건너뛴다. LoadPrefabContents로 저장하면 Mirror가 NetworkIdentity의
│                           assetId를 0으로 지우므로 저장 뒤 GUID 기준 값을 되살린다 - 0이면 빌드에서 스폰이 깨진다
├ Lobby/     [구현완료]
│  ├ TitleScreenController.cs · TitleMenuController.cs  개발용 Dev_Title 시작화면 (네트워크 미연결)
│  ├ AccountTitleMenu.cs  MB  [현재] Release/Title 안내·원정대 찾기·생성·종료. 로그인은 런처에서 유지
│  ├ AccountLobbyPanel.cs MB  [현재] Release/Lobby 방 목록·파티 준비·출발. 연결 전 FrontendEventSystem을 활성화
│  ├ UiPointerRoute.cs       [현재] 검증용 레이캐스트·포인터·submit 경로. 가림·비활성·입력 시스템 누락 시 거절
│  ├ SteamLobbyPanel.cs    MB  Steam 로비 화면. 방 만들기 · 로비 ID 참가 · 정원이 차면 시작.
│  │                           런이 시작되면 접히고, 열려 있는 동안 커서 소유권을 가진다. 대기방에서 무기 선호를 고른다
│  ├ LobbyWeaponPanel.cs   MB  3인 로비 무기 선호 · 준비 UI. 서버 로스터만 표시하고 LobbyController 요청 API를 부른다
│  ├ Editor/SteamLobbyUiBuilder.cs  Steam 화면을 SessionNetwork 프리팹 안에 짓는 에디터 도구
│  └ Editor/AccountLobbyUiBuilder.cs · ReleaseTitleBuilder.cs  계정 UI 프리팹 배선·Dev_Title 기반 출시 Title 생성
├ Systems/   [구현완료] 인벤토리, 특성 트리, 객주 상점, 장비 양도, 정산 · 리더보드
│  ├ FunctionalManagementPanel.cs · RunStatusPanel.cs · RunResultPanel.cs ·
│  │  RunResultViewAdapter.cs · FunctionalUiIndexedButton.cs · FunctionalUiText.cs
│  ├ TalentConstellationLayout.cs  [현재] 특성 탭 별자리 배치 계산(직렬화 클래스). 가운데 그믐에서
│  │                           파티 순번(netId 순, 1P 위 · 2P 좌 · 3P 우)마다 가지가 뻗고 뿌리에서 갈래가 갈라진다.
│  │                           내 가지만 고를 수 있고 남의 가지는 그 사람 무기 트리 모양만 그린다.
│  │                           남이 고른 노드 표시는 [계획] - TalentController가 소유자에게만 복제해 아직 받지 못한다.
│  │                           한 PC 3인(M2)에서 배정, 원격 선택, 무기 교체 초기화, 재접속 복구를 확인했다.
│  │                           결함: 재접속하면 새 netId를 받아 자리 순서가 바뀐다(3P가 2P로 올라감).
│  │                           파티 HUD도 같은 netId 순이라 함께 바뀔 것으로 보인다(미확인). 서로 다른 PC(M3)는 미검증
│  ├ TalentConstellationDepth.cs  [현재] 특성 별자리 가상 깊이(직렬화 클래스). 노드마다 -1 ~ +1 깊이 하나로
│  │                           크기 · 밝기 · 알파 · 선 굵기 · 반짝임 · 마우스 시차를 함께 정한다. 흩뿌림은 노드 ID에서 뽑아
│  │                           세 화면이 같다. 상태는 빛의 세기로 가른다(잠김 = 반짝이지 않음, 고를 수 있음 = 간헐 맥박,
│  │                           가리킴 = 다가옴). 호스트 1인 화면에서 확인, 마우스 실조작 체감은 미검증
│  ├ TalentConstellationGlow.cs   [현재] 별 헤일로 두 겹과 가운데 그믐(축소 · 뒤 헤일로 · 숨쉬기) 설정(직렬화 클래스).
│  │                           URP Bloom 대신 겹친 그림으로 광원처럼 보이게 한다 - Bloom은 글자 · 버튼까지 번진다
│  ├ TalentSkyAnimator.cs  MB  [현재] 특성 탭 별하늘 연출. 은하수 두 층을 흘리고 별을 반짝인다.
│  │                           특성 탭이 켜져 있을 때만 돌고 별도 Canvas로 묶어 노드 재구성을 막는다.
│  │                           마우스 + 느린 떠다님을 시차 입력(Pointer) 하나로 내보내 층마다 다른 거리로 밀고,
│  │                           별자리 앞 먼지를 자체 Canvas로 띄운다. 별자리 노드도 같은 Pointer로 민다
│  ├ EscapeMenuPanel.cs    MB  ESC로 여닫는 중앙 상단 가로 바. 배낭 · 특성 · 도감 · 설정 입구만 맡는다.
│  │                           도감과 설정은 같은 설정 창을 각각 도감 탭 · 옵션 탭으로 연다.
│  │                           ESC 입력을 단독으로 읽어 관리창 → 설정 → 메뉴 순으로 하나씩 닫는다
│  ├ SettingsPanel.cs      MB  [현재] 설정 창. 위 가로 탭(도감 · 옵션 · 시스템)을 Q · E로 넘기고, 옵션 탭은 왼쪽 분류
│  │                           (게임플레이 · 그래픽 · 오디오)로 오른쪽 줄을 바꾼다. 열 때 값을 다시 읽고 닫을 때 저장한다.
│  │                           ESC는 읽지 않는다 - EscapeMenuPanel이 설정 → 메뉴 순으로 닫는다
│  ├ Guide/                [현재] 도감 탭. 게임 상태를 읽지 않는 정적 안내다
│  │  ├ GuideCatalogSO.cs  SO  분류 → 소제목 → 항목(제목 · 본문). 에셋은 09.Data/Guide/GuideCatalog.asset.
│  │  │                    판정 값의 원본이 아니므로 본문에 적은 수치는 밸런스가 바뀌면 같이 고친다.
│  │  │                    UI 전용 글이라 00.Common/Data(팀장 소유)가 아니라 여기 둔다
│  │  ├ GuidePage.cs       MB  왼쪽 분류 · 가운데 목록(스크롤) · 오른쪽 설명. 줄은 템플릿을 복제해 재사용한다.
│  │  │                    TMP가 한글을 글자마다 끊어 본문만 어절을 <nobr>로 묶는다(전역 TMP 설정은 건드리지 않음)
│  │  └ GuideListItem.cs   MB  분류 버튼 · 항목 한 줄. 누를 때 할 일은 GuidePage가 넘긴다
│  ├ Settings/             [현재] 설정 창 부품. 전부 이 기기에서만 쓰는 값이라 서버를 거치지 않는다
│  │  ├ ClientPreferences.cs   static  몬스터 체력 바 표시 · 큰 지도 여는 방식(토글/누르는 동안) ·
│  │  │                        수직 동기화 · 최대 프레임을 PlayerPrefs에 둔다. 볼륨은 SoundVolume, 화면 모드 ·
│  │  │                        해상도는 Unity가 스스로 저장한다. 키를 Constants.ui가 아니라 여기 둔 것은
│  │  │                        00.Common(팀장 소유)을 건드리지 않으려는 것이다
│  │  ├ SettingsOptionsBinder.cs  MB  옵션 줄들을 위 설정 · SoundVolume · Screen에 잇는다
│  │  ├ SettingsStepperRow.cs · SettingsSliderRow.cs  MB  ◀ 값 ▶ 줄과 슬라이더 줄. 값 목록은 모른다
│  │  └ SettingsSystemPage.cs  MB  로비로 돌아가기 · 게임 종료. 누르면 오른쪽에 확인을 띄운다.
│  │                           로비 복귀는 계정 경로(LauncherGameSession.IsFrontendMode)에서만 열린다
│  ├ RailRowVisual.cs      MB  좌측 레일 한 줄(박스 · 아이콘 · 글자 · 밑줄)의 활성 표시를 바꾼다.
│  │                           레일을 ESC 메뉴로 대체해 코드에서는 더 쓰지 않는다.
│  │                           꺼 둔 _bak_ 레일 오브젝트가 아직 참조하므로 파일은 남겨 둔다
│  ├ Editor/RunManagementUiBuilder.cs  위 화면을 SessionManagers 프리팹 안에 짓는 에디터 도구
│  └ Editor/EscapeMenuUiBuilder.cs  레일을 _bak_로 꺼 두고 ESC 메뉴를 RunManagementUI 프리팹 안에
│                              짓는 에디터 도구. 다시 돌려도 된다. 설정 창은 프리팹에서 만든 최종 화면이라
│                              있으면 지우지 않고 잇기만 한다(없을 때만 빈 골격을 세운다)
└ Art/       페이드 · 트랜지션, 연출성 UI
```

> ⚠️ **`*UiBuilder` 세 개는 임시 개발용이다.** `SteamLobbyUiBuilder`와 `RunManagementUiBuilder`와 `EscapeMenuUiBuilder`는
> `new GameObject`로 uGUI 계층을 만든다 — 아래 "씬·프리팹 우선" 규칙에 어긋난다.
> 3인 접속을 당장 돌려 보려고 만든 것이고, **UI 담당(@kjw1237)이 만들 최종 화면은 이 방식이 아니다.**
> 최종 UI가 들어오면 세 빌더와 그 산출물은 걷어낸다. 새 UI를 이 방식으로 만들지 않는다. [예정]

폴더는 화면이 참조하는 게임 상태의 출처를 기준으로 그대로 나눠 두되(코드가 어느 시스템의 SyncVar · 이벤트를 읽는지 한눈에 보이도록), 소유자는 폴더 구분과 무관하게 @kjw1237 한 명이다.

UI는 게임 상태를 **읽기만** 한다. UI에서 상태를 직접 바꾸는 코드(예: 버튼에서 HP 조작)는 금지 — 반드시 해당 시스템의 Command 경유.

**UI 작업은 씬 · 프리팹 작업을 우선한다.** 레이아웃 · 앵커 · 색상 · 폰트 · 계층 구조처럼 Unity 에디터에서 직접 만들고 눈으로 확인할 수 있는 것은 전부 씬/프리팹에서 만든다. 코드로는 **에디터에서 할 수 없는 것만** 작성한다 — 게임 상태를 읽어 텍스트 · 값 · 활성화 여부를 갱신하는 바인딩, 이벤트 구독, 데이터 기반 반복 생성(슬롯 리스트 등) 같은 것들이다. 코드로 UI 계층을 만들거나(`new GameObject`로 패널 구성 등) 인스펙터에서 되는 값을 코드 상수로 박아 넣지 않는다. 상세 패턴은 [Unity 엔지니어링 패턴 9장](unity/UNITY_ENGINEERING_PATTERNS.md).

---

### 4.7 99.Dev — 공용 개발 도구 (소유: 팀장) — 2026-09-10 신설

`00.Common/Dev/`에 있던 파트 무관 도구를 옮긴 곳이다. 네임스페이스는 `Geumeum.Dev`이고,
전 파트가 `using Geumeum.Dev;`로 가져다 쓴다. 파트에 딸린 도구는 여기가 아니라
그 파트의 `Dev/`에 그대로 남는다(`01.Network/Dev/`, `02.Combat/Dev/`, `05.Art/Dev/`).

```
99.Dev/                    namespace Geumeum.Dev
├ DevAccess.cs             static [구현완료] 개발 도구 잠금. 에디터·개발 빌드는 처음부터 열려 있고,
│                                 릴리스 빌드는 Home → PageUp → PageDown → End를 차례로 눌러야 열린다.
│                                 도구는 RunWhenUnlocked · GateComponent로 여기에 매달린다
├ DevToolHost.cs           static [구현완료] 도구가 씬 배치 없이 스스로 뜨게 하는 공용 호스트.
│                                 잠겨 있으면 붙이지 않고 기억해 뒀다가 열릴 때 붙인다
├ DevPanelHost.cs          MB  [구현완료] 브라우저 패널 상태를 만들고 조작을 받는다
├ DevPanel.cs              C#  [구현완료] 패널 컨트롤 정의(DevPanelBuilder · DevCommand)
├ DevWebServer.cs          MB  [구현완료] 패널을 브라우저로 내보내는 TcpListener.
│                                 잠긴 동안에는 포트를 열지 않는다
├ DevWebPage.cs            static [구현완료] 브라우저가 받는 HTML·JS
├ GDebug.cs                static [구현완료] 화면 로그. 기본으로 Unity 콘솔(빌드에서는 Player.log)에도 남긴다
├ GDebugOverlay.cs         MB  [구현완료] GDebug가 쌓은 줄을 로그 패널로 보낸다
├ GGizmo.cs                static [구현완료] 런타임 기즈모 API
├ GGizmoRenderer.cs        MB  [구현완료] 기즈모를 GL로 그린다
├ PlaytestPanel.cs         MB  [구현완료] 배속 · 커서 · 스크린샷
└ WeatherTimeDebugScript.cs MB [구현완료] 시간대 배속(Tab) · 즉시 이동(1~4).
                                 Main · Dev_Barrier에 배치. 날씨 스케줄(Environment/)과는 별개다
```

**빌드에서 빼는 방법.** 도구 전체가 `#if UNITY_EDITOR || DEVELOPMENT_BUILD || GEUMEUM_DEVTOOLS`로
묶여 있다. `GEUMEUM_DEVTOOLS`는 현재 `ProjectSettings`의 Standalone 정의에 들어 있어
릴리스 빌드에도 도구가 포함된다 — 잠겨 있을 뿐이다. 진짜 출시 빌드는 이 정의를 빼고 낸다.
그러면 `GDebug`·`GGizmo` 호출부까지 `[Conditional]`로 사라진다.

---

## 5. RunManager — 런 상태 머신 (프로젝트의 심장)

모든 파트가 이것 하나에 의존하고, 이것은 어느 파트에도 의존하지 않는다.

> **2026-09-06 — 시계가 둘이 되고, 참가자 상태 보관소가 옆에 선다.**
> 필드와 보스가 독립 시계이므로 경과 시간 원본이 둘이다. 소유자는 여전히 `RunManager` 하나이고 일시정지는 두 시계에 동시에 걸린다. 점수의 기준 시간은 둘의 합이다.
> 그리고 씬 전환이 다섯 번이 되면서 **HP · 자원 · 게이지 · 인벤토리를 씬 밖에 보관하는 자리**가 필요하다. `RunManager` 옆에 참가자 상태 보관소를 두고 이탈 · 재접속 · 씬 전환이 **같은 담고 붓는 계약**을 쓴다. 플레이어 쪽 창구는 지금처럼 `PlayerRunAgent` 하나다.

```
[호스트 권위]
RunManager : NetworkBehaviour
├ SyncVar  RunClock FieldClock      필드 경과 시간. 0 ~ 30:00
├ SyncVar  RunClock BossClock      보스 경과 시간. 마당 진입에서 시작, 14:00 하드 종료
│                                   두 시계가 독립이고 소유자는 여기 하나다 (2026-09-06)
├ SyncVar  RunPhase Phase           Lobby → Farming → Shrink1 → Shrink2 → Shrink3 → Boss → Result
│                                   Result → Lobby 만 역방향 (런 재시작, 2026-09-02)
├ SyncVar  bool IsPaused            이탈자를 기다리며 얼어 있다. phase와 직교한다
├ local    RunPhaseChangedEvent     GameEventBus로 서버 프로세스 내부 구간 전환 통지
└ int      FieldLevel               Phase에서 파생 — 드랍 가중치 · 잡귀 강화 배수의 키
```

`ElapsedSeconds`와 `Phase`는 서버만 변경한다. 서버는 `RunManager.CoServerTick`에서
`NetworkTime.time`으로 경과 시간을 다시 계산하고, 클라이언트는 수신한 값을 보간해 표시한다.
클라이언트가 자기 시계로 `Phase`를 전환하지 않는다. 구간의 시작 시각·종료 시각·반경·피해량은
`BarrierPhaseSO`에서 읽는다. 이 코루틴은 현재 런 시계·phase 전용이며 공용 전투 시뮬레이션
스케줄러가 아니다.

### 5.1 전환 계약

| 단계 | 진입 조건 | 종료 조건 | 서버 처리 |
|---|---|---|---|
| `Lobby` | 씬 로드 | 3명 준비 완료 | 런 시계 정지, 역할 배분 준비 |
| `Farming` | 런 시작 | 필드 시계 10:00 | 개활 스폰, FieldLevel 0 |
| `Shrink1` | 필드 시계 10:00 | 필드 시계 **20:00** | 결계 축소·초당 1 피해, FieldLevel 1 |
| `Shrink2` | 필드 시계 **20:00** | 필드 시계 **29:30** | 결계 축소·객주 개방, FieldLevel 2 |
| `Shrink3` | 필드 시계 **29:30** | 필드 시계 **30:00** | 강제 이동 카운트다운 창, 신규 잡귀 스폰 중단, FieldLevel 3 |
| `Boss` | 전실을 지나 마당에 진입한 시점 | 보스 사망 또는 **보스 시계 14:00** | **보스 시계는 단계 진입이 아니라 아레나 전환·바인딩이 끝난 뒤 시작한다**(2026-09-08, 로딩 중 제한 시간 감소 방지). 결계 피해 종료 |
| `Result` | 승리·실패·전멸·이탈 대기 종료 | 정산 완료 후 `Lobby` 복귀 | 점수 계산, 성공 런만 리더보드 등록 |

> **이 표의 시각은 [시스템 기획 2.1](01-system-design.md)이 규범이다.** 여기에는 복제본을 두지 않고, 값이 바뀌면 그쪽을 고친 뒤 이 표를 맞춘다.
> 런타임 값은 `BarrierPhaseSO`에서 읽으며 구간이 겹치거나 끊기면 안 된다 — `phase[i].End == phase[i+1].Start`.
> *(2026-09-06 정정: 09-04 시간표(10/19/25)가 `Shrink1`·`Shrink2`에만 남아 있어, `Shrink2` 종료 25:00과 `Shrink3` 시작 29:30 사이에 **4분 30초 구멍**이 생겨 있었다. 위 불변식을 표 스스로 위반한 상태였다. 두 시계 분리(필드 30분 + 보스 독립 14분) 확정에 맞춰 10 / 20 / 29:30으로 통일한다.)*

`RunStatistics`, `IRunResultFactsProvider`, `IRunResultProvider`와 RunManager 정산 경로가 구현되어 있다. RunResultRecorder/RunLeaderboardStore가 결과와 호스트 로컬 저장을 담당한다. 실제 현재 빌드의 통합 검증은 별도로 수행한다.

#### 슬롯의 키는 connectionId가 아니라 PlatformId다 [구현완료]

`connectionId`는 재접속마다 새로 발급되어 같은 사람을 알아볼 수 없다. 그래서 슬롯과 이탈 자리 보존은
`PlayerPlatformId`로 키를 잡는다. Steam 빌드는 FizzySteamworks가 `conn.address`로 주는 SteamID64를,
개발 KCP 경로는 실행 인자 `-platformid`를 쓴다.

**개발 경로에서 `-platformid`를 주지 않으면 자리와 역할이 복구되지 않는다.** 호스트는 `connectionId`가
항상 0이라 고정값을 쓸 수 있지만 원격은 그렇지 않다. `DevIdentitySource`가 이 경우 경고를 남기고,
`tools/run-3clients.ps1`과 `run-client.bat`이 창마다 고정 신원을 넘긴다.

신원으로 키를 잡으면서 두 명 이상이 동시에 이탈해도 각자 자기 자리로 돌아온다.
예전 선입선출 큐에서는 돌아오는 순서에 따라 자리가 뒤바뀔 수 있었다.

#### 이탈은 런을 끝내지 않는다 - 일시정지 [구현완료]

런 도중 누가 빠지면 `Result`로 확정하지 않고 `RunManager.PauseRun`이 세계를 얼린다.
보스전 30분을 진행하다 한 명이 튕겼다고 런을 버리면 복구가 아니라 손실이기 때문이다.
멈춰 있는 동안 phase와 경과 시간이 그대로 보존되고, 입장 잠금이 풀려 이탈자가 돌아올 수 있다.

일시정지는 `RunPhase` 값이 아니라 **별도 SyncVar**다. 보스전에서 멈췄다 보스전으로 돌아오므로
phase와 직교하고, `RunPhase`에 값을 더하면 전진 전용 가드와 타임라인 순서 의미가 깨진다.

- 정지: 서버 시뮬레이션(`AuthorityTickScheduler`)과 런 시계를 멈춘다
- 입력: `PlayerActionGate.CanAct`가 `isPaused`를 함께 보므로 남은 사람도 움직이지 않는다
- 예측: `IsPaused`가 SyncVar라 소유 클라이언트도 읽고 로컬 예측을 중단한다. 안 그러면 재개 때 되돌아간다
- 재개: 돌아온 사람이 두고 간 자리와 역할을 물려받고, 멈춰 있던 시간만큼 시계를 밀어 경과 시간을 보존한다
- 포기: 대기를 끊으면 그때 `PlayerLeft`로 확정하고 아래 복귀 경로를 탄다

기다리는 시간에 제한은 없다 [구현완료]. 남은 인원은 RunStatusPanel의 대기 포기 요청을 사용할 수 있다.

#### Result → Lobby 복귀 [구현완료]

`Result`는 종착점이 아니다. 도달하고 `Constants.Network.RESULT_TO_LOBBY_SECONDS` 뒤에
`RunManager.ServerReturnToLobby`가 `Lobby`로 되돌린다. 같은 프레임에 되돌리지 않는 이유는
SyncVar가 마지막 값만 보내 클라이언트가 `Result`를 아예 수신하지 못하기 때문이다.

복귀 시 서버가 하는 일은 셋이다. 입장 잠금 해제, 로비 슬롯의 준비·무기·외형 플래그 초기화,
플레이어의 `PlayerWeapon`·`PlayerCharacter` 역할 확정과 `PlayerStatus` 시작값 복원.
플레이어 컴포넌트까지 되돌리지 않으면 다음 런의 역할 배분이 "이미 확정됨"으로 거부된다.

전환 자체는 `RunManager` 안에서 전진 전용 가드(`CommitPhase`)와 실제 커밋(`SetPhase`)을
나눠 처리한다. 역방향은 `SetPhase`만 허용하므로 런 시계가 지나간 경계를 되돌릴 수는 없다.
outcome은 여전히 `TryFinishRun`이 한 번만 커밋한다.

`SpawnManager`의 생존 잡귀 정리는 [구현완료] 구현되어 있다. RunResetEvent를 구독하고
ServerDespawnAll로 자신이 스폰한 개체를 지운 뒤 ServerResetState를 호출한다. 두 판 연속의
런타임 검증은 별도다. 보스 등 다른 스포너의 개체 정리를 뜻하지 않는다.

`FieldLevel 3`은 일반 잡귀 드랍에 사용하지 않고 보스 처치 후 팀 보상에 한 번만 사용한다. `Result`로 전환하면 모든 입력·드랍·구매 요청을 거부한다.

| 구독자 | ElapsedSeconds / Phase로 하는 일 |
|---|---|
| BarrierController | 단계별 목표 반경으로 축소 시작, 범위 밖 피해량 결정 |
| SpawnManager | 동시 존재 수 · 리스폰 주기 · 구성 비율 전환 |
| DropDirector | FieldLevel → DropTableSO 가중치 선택. 3차는 보스 보상에만 적용 |
| ShopKeeper | Shrink2에서만 상호작용 활성 |
| ScoreManager | 클리어 시간 보너스 계산 |
| DayCycleController | 시각 환산 → 조명 (클라 로컬, 네트워크 무관) |
| UI 각종 | 남은 시간 · 축소 10초 전 경고 표시 |

구간 전환 시각과 배율은 코드가 아니라 `BarrierPhaseSO` · `DayCycleSO`가 가진다. RunManager는 시계를 진행하고 전환 이벤트를 발행하며, 각 파트는 시간을 다시 계산해 자기 상태를 변경하지 않는다.

---

## 6. 데이터 에셋 (GameDB)

정의 클래스는 `01.Scripts/00.Common/Data/`(4.0장), 에셋 인스턴스는 `09.Data/`.
접근은 항상 `GameDB.<도메인>` 정적 프로퍼티 — 개별 컴포넌트가 SO를 SerializeField로 물지 않는다 ([코딩 컨벤션 16](coding-convention.md)).

```
Assets/09.Data/
├ Resources/GameDB.asset        루트 GameDatabaseSO (Resources.Load 진입점)
├ Combat/CombatConfigSO.asset 스태미나 · 신력 · 이동 · 구르기 · 패링 (HP 제외)
├ Arrow/ArrowConfigSO.asset   화살 발사각 보정 · 유도 각도 · 지속 시간
├ Weapon/WeaponAbilityConfigSO.asset [예정] 직업별 공격 능력의 사거리 · 범위 · 각도 ·
│                             쿨다운 · 자원 비용 · 치유/버프 환산 비율 (공격력 자체는 미보유)
├ Equipment/EquipmentSO.asset
├ DropTable/DropTableSO.asset
├ Enemy/EnemySO.asset
├ Barrier/BarrierPhaseSO.asset
├ Spawn/SpawnConfigSO.asset  구간별 동시 존재 수 · 구성 비율 · 리스폰 주기
├ Boss/BossPatternSO.asset   보스 페이즈별 패턴 · 쿨다운 · 예고·판정 정보
├ Talent/TalentTreeSO.asset
├ Shop/ShopSO.asset
├ LevelCurve/LevelCurveSO.asset  레벨별 필요 경험치 + 최대 HP (단일 소스)
├ Score/ScoreRuleSO.asset
├ DayCycle/DayCycleSO.asset
├ Sound/SoundCatalogSO.asset
├ Camera/CameraConfigSO.asset ← 위 셋이 네트워크와 무관한 순수 로컬 리소스다
├ BossTrial/BossTrialLoadoutSO.asset          보스 트라이 시작 자원 (시작 레벨 · 편성 포인트 · 등급별 비용 · 물약). 실측 전 임시값
├ Presentation/PresentationConfigSO.asset     전투와 아레나 표현 조정값 (판정 범위 · 서버 규칙 제외).
│                                              히트스톱 · 피격 플래시 · 검기(잔상 시간 · 판정 전후 기록 구간 · 손잡이 비율 · 색) · 임시 VFX 프리팹
├ Presentation/GatNeoul_PhysioSettings.asset  갓 · 너울 PhysioBrush 설정 → 갓·너울 PhysioBrush 사용
├ Teaser/teaser-shots.json     티저 샷 · 편집 · 소리 지시의 단일 원천. GameDB가 아니다 (Unity 준비 창과 tools/teaser가 같이 읽는다)
└ Weather/                   보스 아레나 연출 프로파일. GameDB가 아니라 렌더링 리소스다
   ├ WM_Profile_Shrine.asset       1페이즈 안개 (WeatherMaker)
   ├ WM_Profile_Underworld.asset   2페이즈 안개
   ├ WM_Sky_BossNight.asset        1페이즈 하늘 (별 있음)
   ├ WM_Sky_BossUnderworld.asset   2페이즈 하늘 (별 없음)
   ├ WM_DayNight_BossNight.asset   자정 고정, 시간 속도 0
   ├ VP_BossArena_P1.asset         1페이즈 포스트 프로세싱 Volume
   └ VP_BossArena_P2.asset         2페이즈 포스트 프로세싱 Volume
```

`Weather/`는 `GameDB` 대상이 아니다. 밸런스 수치가 아니라 렌더링 리소스이고 `GameDatabaseSO`가 참조하지 않는다.
**WeatherMaker 프로파일은 `99.ThirdParty`의 스크립트와 텍스처를 GUID로 참조한다** — 구글 드라이브를
동기화하지 않은 사람은 안개와 별이 빠진 채로 열린다.

`GameDBBootstrap`만 `Resources/GameDB.asset`을 로드해 `GameDB.Initialize()`를 호출한다.
호출 시점은 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`로 **씬 로드보다 앞이다.** `@Managers`에는
`NetworkIdentity`가 있어 Mirror가 서버를 열 때까지 오브젝트를 비활성으로 두는데, 그 위의 `Awake`에서
초기화하면 `ServerBootGate`가 서버를 열기 전에 빈 GameDB를 보게 되어 순환이 된다(실행 인자로 호스트를
자동 시작하는 개발 빌드에서 드러났다). 컴포넌트의 `Awake`도 같은 경로를 호출하지만 이미 채워져 있으면
아무것도 하지 않는다. 소비 컴포넌트는 `Awake`의 필드 초기화식이 아니라 `Start`·`OnStartServer` 이후에 GameDB를 읽는다. 루트 DB나 필수 참조가 없으면 조용히 기본값으로 진행하지 않고 초기화 오류로 중단한다. 서버와 클라이언트는 같은 빌드의 GameDB를 사용한다.

새 데이터 도메인 추가 절차: [기술 구현 3.1](03-technical-design.md)에 항목 추가 → `*SO` 클래스 → `GameDatabaseSO` 참조 → `GameDB` 프로퍼티 → 에셋 생성 ([밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md)).

---

## 7. 프리팹 구성

```
Assets/02.Prefabs/
├ Network/
│  ├ Player.prefab            NetworkManager 등록 플레이어 프리팹 → 구성 아래 참조
│  ├ SessionNetwork.prefab    @Network. NetworkManager · 트랜스포트 · 인증기 · Steam
│  ├ SessionManagers.prefab   @Managers. NetworkIdentity 하나에 GameDBBootstrap · GameEventBus ·
│  │                          LobbyController · RunManager · AuthorityTickScheduler · LobbyRoster ·
│  │                          LobbyRoleCoordinator · RunSceneDirector · SessionPersistence ·
│  │                          DropDirector · CurrencyManager · GrowthManager ·
│  │                          ShopTransactionService · EquipmentTransferService ·
│  │                          RunStatistics · RunResultRecorder.
│  │                          자식으로 인벤토리·특성·객주 화면(FunctionalManagementUI)과
│  │                          런 상태·결과 화면을 품는다. Boot · Dev_HUD가 씬 인스턴스로 쓴다
│  └ SessionDiagnostics.prefab @Diagnostics. FrameRecorder · SessionPersistence
├ Enemy/
│  ├ Heokkaebi / Changgwi / Aekgwi.prefab    잡귀 3종 (서버 스폰)
│  ├ CurseProjectile.prefab   액귀 투사체 (서버 스폰)
│  └ Boss.prefab              보스 (서버 스폰)
├ Combat/
│  └ Arrow.prefab             화살 투사체 (서버 스폰)
├ Systems/
│  └ DroppedItem.prefab       필드 드랍 장비 (서버 스폰)
├ Managers/
│  └ @Managers.prefab, @Network.prefab, @AI.prefab …   씬 골격 루트 (3.2장)
├ UI/
│  └ MinimapPlayer.prefab     미니맵 점 하나. MinimapPlayerDots가 인원만큼 복제한다
└ Art/
   └ VFX · 연출 프리팹 (로컬 재생 전용, NetworkIdentity 없음)
```

**Player.prefab 컴포넌트 구성**:

```
[현재 — 2026-09-01]
Player.prefab
├ NetworkIdentity / NetworkTransformReliable(syncRotation 꺼짐)
├ PlayerController / ParryController / ParryGauge
├ PlayerStatus / PlayerWeapon / PlayerCharacter
├ PlayerPrediction / PlayerPresentation / PlayerAnimationController / PlayerRollController
├ CapsuleCollider / Rigidbody
└ VisualPivot                   표현용 Yaw만 적용, 물리 · 네트워크 루트는 회전하지 않는다
   ├ Chasa_Art · Singung_Art · Munyeo_Art   모델별 축 보정을 로컬 회전으로 유지
   └ Chisa(비활성)                          교체 전 외부 에셋, 바인딩하지 않는다

[코드 존재 — 프리팹 배선은 별도 확인]
├ PlayerAttack / HealController / LockOnController

[계획 — 단계별 추가]
├ InteractionController
├ ArrowLauncher(서버 전용)
├ InventoryController / TalentController
└ CameraRig 앵커는 CAMERA_AND_LOCKON의 독립 카메라 리그 계약을 따른다 (VisualPivot 회전에 종속시키지 않음)
```

`ArrowLauncher`는 클라이언트 Command를 받지 않고 `BowAbility`의 서버 스폰 요청만 수행한다.
플레이어에 새 NetworkBehaviour를 붙이는 변경은 동기화 대역폭과 파트 경계에 걸리므로 팀장 승인 대상이다.

---

## 8. 파트 간 경계 — 결합 규칙

### 8.1 허용되는 의존 방향

```
Geumeum.Art  ─┐
Geumeum.UI   ─┼──읽기──▶  각 파트의 SyncVar · 이벤트
              │
Combat / AI / Systems ──▶ Geumeum.Network(RunManager) · Geumeum.Common · Geumeum.Data
Combat ✕ AI ✕ Systems 상호 직접 참조 금지 — Common의 인터페이스 · 이벤트 경유
```

예: 잡귀 사망 → 드랍은 `EnemyController`(AI)가 `DropDirector`(Systems)를 호출하는 게 아니라, **AI가 사망 이벤트를 발행하고 Systems가 구독**한다. 반대 방향도 같다.

파트 간 이벤트는 `@Managers`가 소유하는 `GameEventBus`를 통해 전달한다. 이벤트 payload는 결과를 바꾸는 서버 이벤트와 표현 전용 클라이언트 이벤트를 구분하고, 구독자는 `OnEnable`에서 구독한 뒤 `OnDisable`에서 해제한다. 정적 전역 이벤트나 다른 파트의 구체 클래스를 payload 타입으로 사용하지 않는다.

### 8.2 공용 인터페이스가 경계다

| 인터페이스 | 구현 | 호출 |
|---|---|---|
| `IDamageable` | PlayerStatus, EnemyStatus, BossStatus | 공격 판정 · 결계 피해 · 투사체 |
| `IRecoveryTarget` | PlayerStatus | 무녀 치유 · 회복 물약 |
| `IBarrierState` | BarrierController *(구현됨 — RollTargetCenter와 BarrierTransitionState로 중심·반경을 계산. RequiredLandmarks·BarrierMapBinding의 랜드마크 제약 적용)* | 무녀 치유 · 결계 밖 잡귀 이동 · BarrierVisual · UI |
| `IParryable` | 창귀 돌진, 보스 패링 가능 패턴 | ParryController |
| `IInteractable` | ShopKeeper, DroppedItem | 플레이어 상호작용 입력 |
| `ICombatFeedback` | CombatFeedbackImpl | 전투 타이밍 → 로컬 연출 |
| `IDamagePresentationSource` | PlayerStatus, EnemyStatus, BossStatus | DamageFlashView (동기화된 체력 감소 → 로컬 피격 플래시) |
| `IWeaponBladeSource` | PlayerWeaponVisual | MeleeSlashView (실제 검날 경로 → 로컬 검기) |
| `IBossTrialGate` | BossTrialDirector | RunManager (보스 개시 보류) · LobbyController (런 모드 결정) |

### 8.3 타격감 (전투 ↔ 아트)

전투 파트는 타이밍에서 `ICombatFeedback` 호출만 하고, 구현은 아트가 `CombatFeedbackImpl`로 채운다 ([기술 구현 3.3](03-technical-design.md)).

**연결 방식** — Combat이 Art의 클래스를 직접 참조하면 8.1의 의존 방향을 어기므로, `00.Common`의 정적 로케이터를 경유한다.

```
아트 측 — @Local의 CombatFeedbackImpl
    OnEnable  → CombatFeedbackLocator.Register(this)
    OnDisable → CombatFeedbackLocator.Unregister(this)

전투 측 — Player.prefab의 전투 컴포넌트
    Start 에서 1회 캐싱: _combatFeedback = CombatFeedbackLocator.Current;
    이후 호출만 한다
```

```csharp
_combatFeedback.PlayHit(strength, duration);
_combatFeedback.PlayParrySuccess();
_combatFeedback.PlayCritical(multiplier);
```

- `CombatFeedbackLocator.Current`는 **등록된 구현이 없으면 아무것도 하지 않는 `NullCombatFeedback`을 돌려준다.** null 체크를 호출부마다 흩뿌리지 않기 위해서다.
- 따라서 프로토타입(08.30–09.19)에는 `CombatFeedbackImpl`이 씬에 없어도 전투 코드가 그대로 돈다 — 연출은 집중개발 4~5주차 착수.
- 연출은 로컬 표현이므로 **서버에서 호출하지 않는다.** 판정은 서버가 하고, 그 결과 이벤트를 받은 각 클라이언트가 자기 화면에서 호출한다.

---

## 9. 네트워크 권위 요약

판단 기준 한 줄: **이 값이 사람마다 달라지면 게임 결과가 달라지는가.** 상세 · API 매핑은 [네트워크 권위](unity/NETWORK_AUTHORITY.md).

| 항목 | 권위 | 담당 클래스 |
|---|---|---|
| 경과 시간 · 런 상태 | 서버 | RunManager |
| HP · 스태미나 · 신력 | 서버 | PlayerStatus |
| 공격 · 패링 · 화살 판정 | 서버 (판정 수식은 순수 C# ★클래스) | `ParryJudge`, `BarrierJudge` 등 |
| 잡귀 · 보스 AI, 결계 | 서버 | EnemyBrain*, BossPhase*, BarrierController |
| 드랍 · 노잣돈 · 소유권 · 경험치 | 서버 | EquipmentGenerator, CurrencyManager, GrowthManager |
| 사망 · 부활 · 전멸 감지 | 서버 | ReviveController |
| 런 종료 · `Result` 커밋 · 일시정지 · `Lobby` 복귀 | 서버 | RunManager |
| 조명 · 카메라 · 이펙트 · 사운드 · UI | 클라 로컬 | 05.Art, 06.UI, CameraRig |
| 보스 예고 장판 · 결계선 · 액귀 투사체 | **서버가 이벤트, 클라가 렌더링** | BossPatternTelegraph 등 |

네트워크가 걸린 변경은 예외 없이 **3인 접속 L3 검증** 대상이다 ([검증 플레이북](ai/03_VERIFICATION_PLAYBOOK.md)).

---

## 10. 현재 구현과 후속 작업

계정·릴레이, 전투·무기 R, 장비·경제·39특성, 보스·결과·규칙형 동료 봇이 구현되어 있다. 실제 상태와 미검증 범위는 [구현 현황](IMPLEMENTATION_STATUS.md), 후속 작업은 [로드맵](05-implementation-roadmap.md)을 따른다. 초기 프로토타입의 폴더 생성 순서를 현재 미구현 목록으로 사용하지 않는다.

## 11. 이 구조가 지키는 기획 요구 (추적표)

| 기획 요구 | 출처 | 구조상 대응 |
|---|---|---|
| 데디케이티드 전환 여지 | 기술 1.2 | 판정 수식을 순수 C# ★클래스로 분리 (4.2 · 4.3) |
| 밸런스 수치 코드 금지 | 기술 3 | GameDB 단일 소스 (6장) |
| 씬 머지 충돌 방지 | 기술 2.1 | Dev 씬 4개 + Main 통합 전용 + 파트 루트 프리팹화 (3장) |
| 서버는 원본 값 하나만 | 기술 3.2 | RunManager의 ElapsedSeconds 허브 (5장) |
| UI 전담자 지정 (2026-08-30~) | 시스템 5 | 06.UI를 @kjw1237 전담, 씬 작업 우선 원칙 (4.6) |
| 페이즈마다 패링 패턴 | 게임 9.2 | BossPhaseBase + IParryable 계약 (4.3) |
| 아이템 양도 · 재조정권 재사용 | 기술 4.3 | EquipmentSO(설계도) / WeaponCombatSnapshot(결과) 분리 (4.4) |
| 호스트 PC 부하 | 기술 4.4 | 05.Art 로컬 전용 강제, VFX 프리팹에 NetworkIdentity 금지 (4.5 · 7장) |
| 스코프 잠금 | 시스템 6 | 보스 1 · 고정 맵 · 제작 없음 — 해당 확장용 폴더 · 클래스를 미리 만들지 않는다 |
| 3인 고정 · 역할 무작위 배분 | 시스템 2.3 | LobbyController가 런 시작을 게이트하고 무기를 중복 없이 배분 (4.1 · 10장) |
| 모든 `[Command]`에 서버 검증 | 기술 3.2 | 판정 수식은 순수 C# ★클래스, NB는 호출자 · 상태 · 거리 검증 후 위임 (4.2 · 9장) |
| 보스맵 · 객주가 결계 안 | 시스템 2.1 | `@Stage` 배치 제약표 — `BarrierCenter` 기준 거리 조건 (3.2) |
| 강화 배수는 스폰 시 확정 | 게임 5.2 | EnemyStatus가 스폰 시점 단계로 고정, 재계산 금지 (4.3) |

---

## 12. 미결정 사항 (구조에 영향 있는 것만)

| 항목 | 현재 구조의 가정 | 결정 시 바뀌는 곳 |
|---|---|---|
| 리더보드 저장 방식 | 로컬 JSON | LeaderboardService 구현체만 교체 |
| 보스 페이즈 수 | 2페이즈 데이터와 Phase1State/Phase2State 있음 | 남은 부위·최종 구간·전환 잠금 계약 구현; 새 페이즈 추가 계획 아님 |
| 데디케이티드 전환 | 리슨 서버 | ★순수 C# 판정 클래스는 그대로, NB 레이어만 이식 |

---

## 13. 변경 이력 — 2026-08-29 아키텍처 동기화

> **이 장은 통합 기록이다.** 2026-09-07에 `architecture-sync-2026-08-29.md`를 폐기하면서, 그 문서에만 있던 **경계 선택의 이유**를 이 장으로 옮겼다. 당시의 구현 현황 표는 이미 낡았으므로 옮기지 않았다 — 현재 상태는 4·5장이 정본이다.

2026-08-29(기준 커밋 `57cb59d`)에 문서가 설계 초안 상태에 머물러 실제 코드보다 뒤처진 부분을 현재 구현 기준으로 갱신했다. 그때 확정한 경계와 그 이유는 다음과 같다.

### 13.1 런 시계와 공용 시뮬레이션 틱

`RunManager`의 서버 틱은 **경과 시간과 phase만 소유한다.** 이를 플레이어·AI·투사체 전체의 공용 고정 틱으로 문서화하지 않기로 했다.

공용 권위 시뮬레이션 스케줄러를 런 시계 루프의 확장으로 둘지 별도 스케줄러로 둘지는 당시 미정으로 남겼다. **시스템 실행 순서와 입력·피해 catch-up 정책을 정하지 않은 채 런 시계에 얹으면 되돌리기 어렵다**는 판단이었다. (현재는 `AuthorityTickScheduler`가 이 자리를 맡는다.)

### 13.2 서버 부트 게이트는 preflight가 아니라 post-listen이다

Mirror의 `StartHost`/`StartServer` 호출을 가로챌 수 없어, 서버가 열린 직후에 검사하고 실패하면 닫는 구조다. **"서버를 열기 전에 차단한다"는 기존 문구는 실제 동작이 아니었으므로 수정했다.**

이 구분은 지금도 유효하다 — 부트 검사 실패는 "열리지 않음"이 아니라 "열렸다가 닫힘"으로 관측된다.

### 13.3 플레이어 공격 진입점은 하나다

클라이언트 공격 Command는 `PlayerAttack` 하나로 고정한다. 활 공격도 무기 선택을 거쳐 같은 Command로 들어오고, **별도의 활 Command를 만들지 않는다.** 진입점이 갈라지면 서버 검증·게이트·거부 사유를 두 벌 유지해야 한다.

### 13.4 사망·전멸·런 종료의 소유권

`PlayerStatus`가 플레이어 생명 상태의 원본이고, `ReviveController`가 관전·부활 타이머와 전멸 조건을 감지한다. 전멸 시 `RunManager`에 종료를 **요청**하며, **`RunManager`만 `RunPhase.Result`를 커밋한다.** 종료 결과를 커밋하는 곳이 하나여야 사람마다 다른 결과가 나오지 않는다.

### 13.5 `Starting`은 phase가 아니라 트랜잭션이다

`Starting`은 `RunPhase` enum 값이 아니라 **연결 수명의 개념적 트랜잭션**이다. 입장 잠금 → 로비 슬롯 무기 배분 → 역할 적용 → 런 시작이 하나의 단위로 성공하거나 실패해야 하며, 중간 상태를 phase로 노출하면 클라이언트가 그 상태를 렌더링하려 든다.

### 13.6 데이터 명칭과 HP 단일 원본

문서의 오래된 `*Data` 명칭을 실제 클래스·에셋명인 `*SO`로 통일했다. **최대 HP의 단일 원본은 `LevelCurveSO`이며 `CombatConfigSO`에는 HP를 두지 않는다.** 두 SO가 같은 값을 가지면 어느 쪽이 적용됐는지 런타임에 구분할 수 없다.

### 13.7 중앙 서버 partial 폴더 (2026-09-15)

`Assets/01.Scripts/98.Servers/`는 팀장이 맡는 네트워크 코드 중 서버 partial(`.server.cs`)의 위치다. 기존 요청·조회 컴포넌트, 판정·AI 클래스, Mirror `Net/`는 도메인 폴더에 유지하고, 서버 partial을 중앙에 모았다(판정·AI 22개는 같은 날 도메인 폴더로 되돌렸다). 하위 경로는 기존 도메인 폴더를 그대로 비춘다(`98.Servers/{03.AI|04.Systems}/{기능}/`). 새 `.server.cs`는 이 규칙대로 기능별로 작성하고, 새 판정·AI 코드는 도메인 폴더에 파트 담당이 작성한다.
기존 partial과 호환되도록 `Geumeum.AI`·`Geumeum.Systems` 네임스페이스와 어셈블리는 유지한다. 클라이언트 소비자는 [클라이언트 API](unity/CLIENT_API_REFERENCE.md)를 사용한다. 공용 계산과 `01.Network`·`02.Combat`의 기존 혼합 컴포넌트는 이번 물리 이동에서 제외했다. `01.Network`·`02.Combat` 담당은 팀장이다. 전체 서버 코드를 클라이언트 빌드에서 제외하는 구조는 아니다.

## 현행 추가 진입점

- 자체 계정: `01.Network/Identity/AccountGameServer`, `LauncherGameSession`, `AccountRoom`, `AccountRelayTransport`. API는 신원·방, 게임 호스트는 시뮬레이션 권위다. 런처가 방 없는 게임 자식 세션을 전달하면 게임의 Title→Lobby에서 방을 찾고 준비한다. `01.Network/Dev/VerificationCommandDriver.frontend.cs`는 실제 uGUI 이벤트 경로를 검사한다.
- 플레이어: `PlayerBrain`, `PlayerBotSpawner`, `PlayerAttack`의 counter/skills/placement/abilities partial과 `03.AI/PlayerBot`.
- UI·표현: `WeaponAbilityHUD`, `FixedAspectViewport`, `InteractionPromptView`, `BotInventoryView`.
