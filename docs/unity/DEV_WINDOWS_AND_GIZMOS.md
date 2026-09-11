# 개발 패널과 런타임 기즈모

게임을 멈추지 않고 값을 눈으로 확인하고 만지는 도구다.
에디터 플레이와 개발 빌드(Development Build)에서만 동작하고, 릴리스 빌드에서는 호출부까지 사라진다.

**조작은 게임 창이 아니라 브라우저 창에서 한다.** 게임 화면에는 월드에 붙는 표시만 남는다.

관련 코드는 `Assets/01.Scripts/99.Dev/`(공용, namespace `Geumeum.Dev`)와 각 파트의 `Dev/` 폴더에 있다.

---

## 1. 여는 방법

게임을 실행하면 Unity 콘솔에 주소가 한 줄 찍힌다.

```
[DevWebServer] 개발 패널을 브라우저에서 연다: http://127.0.0.1:8787/
```

이 주소를 브라우저에서 열면 패널이 뜬다. 창 크기·위치는 브라우저 창이니 마음대로다.

- 포트가 이미 쓰이면 8788, 8789… 로 하나씩 올려 잡는다. **한 PC에서 여러 인스턴스를 띄우면 각자 다른 포트다** — 콘솔 줄을 보고 구분한다.
- 패널 맨 위에 이 프로세스가 `호스트 / 서버 / 클라이언트 / 오프라인` 중 무엇인지 뜬다. 3대를 나란히 열어 두고 구분하는 용도다.
- 기본은 이 PC(루프백)만 접속된다. 다른 기기(노트북·폰)에서 열려면 `-devpanellan`으로 실행한다. 포트를 바꾸려면 `-devpanelport 9000`.

### 왜 브라우저인가

Unity 플레이어는 자기 프로세스에서 두 번째 OS 창을 열 수 없다.
게임 화면 위에 IMGUI로 그리면 전투 화면을 덮고, 창 크기가 게임 창 안에 갇힌다.
그래서 게임이 로컬 HTTP 주소를 하나 열고 브라우저 창을 조작 창으로 쓴다.

---

## 2. 패널 목록

| 패널 | 내용 |
|---|---|
| 테스트 실행 | 1인 실행, 시작 인원, 자동 준비, 런 모드, 캐릭터 희망, 호스트 시작·중지, 씬 전환, 전체 부활, 런 포기 |
| 로그 | `GDebug`로 찍은 줄 — 줄마다 `HH:mm:ss` 시각, 태그(정보·성공·실패·주의·서버·보스)별 켜고 끄기와 건수, `서버만/보스만/전부 보기`, 비우기, Unity 콘솔에도 남기기 |
| 기즈모 | 기즈모 켜기·끄기, 갈래별 표시, 지금 값, 값 그래프 |
| 카메라 조정 | `CameraConfigSO` 수치를 슬라이더로 실시간 조정, 파일 저장·불러오기 |
| 파티 | 인게임 파티 바 토글, 머리 위 이름표 토글, 파티원 체력·스태미나·신력, 남은 단축키 목록 |
| 프레임 기록 | 현재 ms·FPS·표본 수, 지금 파일로 저장 |
| Steam 로비 | 방 만들기·참가·초대·호스트 시작 (Steam 전송일 때) |
| 보스 페이즈 | 1페이즈 산속 사당 / 2페이즈 저승 물길 전환 |
| 콜라이더 | 콜라이더 그리기, 갈래별(플레이어·잡귀·보스·결계·지형) 표시, 트리거, 타겟 부위 번호, 내 근접 판정 범위, 표시 반경 |
| 플레이테스트 | 시간 배속(0.25·0.5·1·2배와 슬라이더), 일시정지, 커서 풀기, 스크린샷 저장 |

게임 화면에 남는 것은 **기즈모 선·월드 글자**, **머리 위 이름표**, 그리고 **파티 바**다.
체력·스태미나·신력은 왼쪽 아래에 그린다 — 3인 테스트에서 서로의 상태를 보며 치유와 부활을 판단하는데,
그걸 브라우저 창에서 볼 수는 없다. 호스트든 클라이언트든 자기 스폰 목록을 읽으므로 세 화면 모두에 뜬다.
Mirror 기본 HUD는 시작할 때 꺼진다 — 호스트 시작·중지는 테스트 실행 패널에 있고, 필요하면 그 패널에서 다시 켠다.

### 컴포넌트 컨텍스트 메뉴

패널에 없는 것도 있다. 결계는 `@Managers`의 `BarrierController`를 인스펙터에서 우클릭해 부른다.
셋 다 호스트로 실행 중일 때만 듣는다.

| 메뉴 | 하는 일 |
|---|---|
| `Debug: Force Circle Without Map (Test)` | 현재 단계의 원을 즉시 세운다. 보간이 없어 반경이 튄다 |
| `Debug: Force Next Phase (Test)` | 단계만 다음으로 넘긴다. **반경은 바뀌지 않는다** |
| `Debug: Force Next Phase Animated (Test)` | 다음 단계로 넘기면서 예고 시간 뒤 정상 축소 시간에 걸쳐 반경을 줄인다 |

축소가 눈에 보이길 원하면 세 번째를 쓴다. `Farming`에서 세 번 누르면 400m에서 60m까지 내려온다.

정상 경로(`ServerPrepareTarget`)는 `BarrierMapBinding`과 베이크된 NavMesh를 요구하는데,
그 둘은 `Release/Field.unity`에만 있다. 그래서 Dev 씬에서는 단계가 저절로 넘어가도 반경이 0에 머문다.
위 메뉴들은 랜드마크 검증과 NavMesh 검증만 건너뛴다. 보간, SyncVar, 결계 밖 피해는 정상 경로 그대로다.
**검증을 건너뛰므로 이걸로 확인한 것은 결계 배치의 정합성 근거가 되지 않는다.** 배치는 `Field.unity`에서 본다.

---

### 1인 실행

두 가지 방법이 있고 결과는 같다.

1. **패널에서 `1인`을 누르고, 게임 화면의 `시작`을 누른다.** 인원을 정원보다 낮게 고르면 자동 준비도 함께 켜지므로,
   시작 한 번으로 로비를 지나 런까지 이어진다.
2. 패널의 `1인 실행` 버튼 하나로 인원 1 · 자동 준비 · 호스트 열기까지 한번에 한다.

예전에는 씬의 `LobbyController._devMinPlayers`·`_devAutoReady`를 고치거나 `-minplayers 1`로 다시 띄워야 했다.
씬 파일은 머지가 안 되고 실행 인자는 에디터에서 못 쓴다. 그래서 런타임 오버라이드를 뒀다.

- `LobbyController.DevRequiredPlayers`(0이면 오버라이드 없음)·`DevForceAutoReady`·`DevRunMode`가 그 값이다.
- **Steam 전송에서도 통한다.** 게임 화면의 `시작` 버튼은 `SteamSessionCoordinator.CanStartHost`로 켜지고,
  그 게이트가 이 인원을 본다 — 그래서 로비에 혼자 있어도 시작이 눌린다.
- 씬 값·실행 인자로는 여전히 3인으로 강제된다. 사고로 1인이 되면 런 시작과 동시에 입장이 잠겨
  나머지 둘이 거부되기 때문이다. **패널에서 사람이 직접 누른 값만** 예외로 통과시킨다.
- 판정은 그대로 서버(`ResolveRequiredPlayers`·`CanStartHost`)에서만 한다. 클라이언트가 정하는 값이 아니다.
- 1인·2인으로 시작한 런은 3인 검증에 쓸 수 없다. 서버가 그때마다 경고를 한 줄 남긴다.

---

## 3. 패널을 하나 더 만들 때

씬이나 프리팹을 건드리지 않는다. 자기 도구 파일에서 등록만 한다.

```csharp
private void Awake()
{
    DevPanelHost.Register("monster", "몬스터", Build, Handle);
}

private void OnDestroy()
{
    DevPanelHost.Unregister("monster");
}

// 주기마다 지금 값을 다시 적는다. GUILayout이 아니라 무엇이 있는지만 적는다
private void Build(DevPanelBuilder builder)
{
    builder.Header("스폰");
    builder.Label($"살아 있는 잡귀 {_alive}마리");
    builder.Toggle("freeze", "행동 정지", _isFrozen);
    builder.Slider("speed", "이동 속도 __ m/s", _speed, 0f, 10f);
    builder.Choice("phase", "페이즈", _phase, "1페이즈", "2페이즈");
    builder.Button("kill", "전부 제거");
}

// 브라우저가 누른 것을 받는다. Build와 함께 메인 스레드에서만 불린다
private void Handle(DevCommand command)
{
    switch (command.Control)
    {
        case "freeze": _isFrozen = command.IsOn; return;
        case "speed":  if (command.TryGetFloat(out float speed)) { _speed = speed; } return;
        case "phase":  SetPhase(command.Value); return;
        case "kill":   KillAll(); return;
    }
}
```

조작 종류는 `Label · Header · Toggle · Button · Slider · Text · Choice · Log · Plot`이다.

---

## 3.5 로그 태그

`GDebug`의 태그는 `Info · Success · Fail · Warn · Server · Boss`다.
보스 흐름은 전투 로그에 묻히므로 따로 뒀다 — `GDebug.Boss("2페이즈 진입")`.

- 줄마다 **시계 시각(`HH:mm:ss`)** 이 붙는다. 세 프로세스의 로그를 맞춰 보려면 켜진 시점부터의 시간으로는 안 된다.
- 버퍼는 200줄이다. 예전에는 화면에 겹치지 않는 줄 수(18)에 묶여 있었다.
- 패널에서 태그별로 껐다 켤 수 있고, `서버만 보기` · `보스만 보기` 버튼으로 한 번에 좁힌다.

---

## 4. 기즈모

`OnDrawGizmos`는 에디터 Scene 뷰 전용이라 개발 빌드에서 보이지 않는다.
`GGizmo`는 URP의 카메라 렌더링이 끝난 시점에 GL로 직접 선을 얹으므로 **게임 뷰와 개발 빌드에서 그대로 보인다.**

```csharp
// 마지막 인자는 수명(초). 0이면 이 프레임만 보인다
GGizmo.Sphere(GGizmoChannel.Combat,  center, radius, Color.red);
GGizmo.Arc   (GGizmoChannel.Barrier, origin, forward, halfAngle: 45f, radius: 3f, Color.cyan);
GGizmo.Box   (GGizmoChannel.Monster, center, size, rotation, Color.yellow, 2f);
GGizmo.Arrow (GGizmoChannel.Camera,  origin, direction, Color.green);
GGizmo.Line  (GGizmoChannel.Misc,    from, to, Color.white);
GGizmo.Point (GGizmoChannel.Misc,    position, size: 0.3f, Color.magenta);

// 월드 좌표에 글자를 붙인다. 값이 어느 물체의 것인지 헷갈리지 않게 한다
GGizmo.Label3D(GGizmoChannel.Combat, head, $"HP {hp}", Color.white);

// 기즈모 패널에 이름 붙은 값 한 줄. 같은 이름은 덮어쓰고, 3초 갱신이 없으면 사라진다
GGizmo.Value(GGizmoChannel.Camera, "yaw", yaw.ToString("0.###"));

// 여러 프레임에 걸쳐 변하는 값. 패널에서 꺾은선으로 본다.
// "느낌이 이상한" 구간은 브레이크포인트로 못 찾고 흔들리는 모양을 봐야 찾는다
GGizmo.PlotValue("yaw", yaw);
```

- 갈래(`GGizmoChannel`)는 카메라·전투·결계·몬스터·네트워크·기타다. 패널에서 갈래별로 끈다.
  **꺼진 갈래는 아예 쌓지 않으므로**, 꺼 두면 판정 루프의 비용도 사라진다.
- 지형에 가려지게 할지는 기즈모 패널의 `지형에 가려지게`로 고른다. 기본은 다 보이게다.
- 한 프레임 예산은 선 6000개·글자 256개다. 넘기면 패널에 `(예산 초과)`가 뜬다.

### 지금 무엇을 그리고 있나

기즈모는 **호출부가 있는 곳만** 그려진다. 패널의 `선 0개 · 글자 0개`는 고장이 아니라 아직 아무도 안 찍었다는 뜻이다.

| 어디 | 무엇 | 갈래 |
|---|---|---|
| `CameraRig.ApplyTransform` | 목표 피벗과 따라간 피벗, 둘을 잇는 선, 붐(원하는 거리 옅게·실제 진하게), 락온 대상선, `yaw`·`pitch`·거리·락온 값, `yaw`·거리 그래프 | 카메라 |
| `PlayerAttack.ApplySweepHit` | 근접·방울 판정 부채꼴(360도면 원), 맞았을 때만 `hit x3` 글자. 1.5초 남는다 | 전투 |
| `ArrowProjectile` 서버 이동 | 이번 틱에 쓸어 간 구간(막히면 빨강). 화살은 콜라이더로 부딪히지 않고 이 구간으로 판정한다 | 전투 |
| `ColliderGizmoView` (콜라이더 패널) | 카메라 주변 콜라이더 실제 모양 — 박스·구·캡슐은 그대로, 메시·지형은 감싸는 상자. 트리거는 노란색, 타겟 부위 번호 글자, 내 근접 판정 범위 | 콜라이더의 레이어에 따라 전투·몬스터·결계·기타 |

콜라이더는 **기본으로 꺼져 있다.** 패널의 `콜라이더 그리기`를 켜면 그려진다 — 켜 둔 채로 두면 화면이 선으로 덮인다.
씬 전체를 훑지 않고 카메라에서 정한 반경 안만 `Physics.OverlapSphereNonAlloc`으로 모으며, 한 번에 512개까지다.
꺼 놓은 갈래는 레이어 마스크에서 빠지므로 물리 조회 자체를 하지 않는다.

- 카메라 붐 선은 **게임 카메라에서 보면 시선 방향과 겹쳐 거의 점으로 눌린다.** 관전 카메라나 다른 시점에서 봐야 길이가 보인다.
  게임 화면에서 눈에 들어오는 것은 피벗 십자와 락온 선이다.
- 공격 판정은 서버에서만 하므로 그 부채꼴도 **호스트 화면에만** 뜬다.
- **월드 글자는 줄바꿈을 끈다.** `CalcSize` 폭에 딱 맞춰 그리면 1px이 모자라 두 줄로 접히고,
  높이는 한 줄만큼이라 아래가 잘려 깨진 글자처럼 보인다(2026-09-10 3인 테스트에서 실제로 그렇게 보였다).
  같은 이유로 파티 바 이름과 머리 위 이름표도 줄바꿈을 껐다.

### 왜 [Conditional]인가

`GGizmo`와 `GDebug`의 그리기 함수에는 `[Conditional("UNITY_EDITOR")]`, `[Conditional("DEVELOPMENT_BUILD")]`가 붙어 있다.
릴리스 빌드에서는 **호출부가 컴파일 단계에서 사라진다** — 인자 계산 비용까지 없어지므로
게임플레이 코드에 `#if`를 두르지 않아도 된다.

다만 함수 본문은 컴파일되므로, 릴리스에서 빠지는 코드를 `GGizmo` 본문에서 참조하면 안 된다.

---

## 5. 구조

| 파일 | 역할 |
|---|---|
| `DevPanel.cs` | 조작 종류와 `DevPanelBuilder`, `DevCommand` |
| `DevPanelHost.cs` | 패널 등록, 스냅샷 JSON 생성, 브라우저 조작 적용 |
| `DevWebServer.cs` | TcpListener 기반 작은 HTTP 서버 |
| `DevWebPage.cs` | 브라우저 화면(HTML·CSS·JS) 문자열 |
| `GGizmo.cs` · `GGizmoRenderer.cs` | 기즈모 수집과 GL 렌더링, 기즈모 패널 |

- 브라우저는 `GET /api/state`를 200ms마다 읽고, 누르면 `POST /api/command`로 `{panel, control, value}`를 보낸다.
- 여럿 중 하나를 고르는 조작(`Choice`)은 **라벨이 아니라 순번**을 보낸다. 라벨을 고쳐도 게임 쪽 분기가 깨지지 않는다.
- 서버는 배경 스레드다. **Unity API를 그 스레드에서 부르면 그 자리에서 예외가 난다** — 그래서 조작은 큐에 넣고 메인 스레드의 `Update`에서 적용한다.
- HttpListener를 쓰지 않았다. Windows에서 주소 등록(URL ACL)이 없으면 관리자 권한을 요구해 팀원 PC마다 다르게 실패한다.

---

## 6. 검증 기록

2026-09-10, Boot 씬 에디터 플레이(KCP)에서 확인했다.

- `http://127.0.0.1:8787/` 페이지 200 응답, 패널 8개가 스냅샷에 실렸다.
- 브라우저 밖에서 보낸 `POST /api/command`(`runtest`/`solo`) 한 번으로 호스트가 열리고 런이 시작됐다 —
  단계 `Lobby → Farming`, 씬 `Field/Idle`, 슬롯 1명, 인원 표시 `1인 필요`.
- 로그·파티·카메라·기즈모 패널이 실시간 값을 표시했다(체력 100/100, 카메라 슬라이더 32개, 로그 색 구분).
- 게임 화면에는 이름표만 남았다. Mirror 기본 HUD는 꺼진 것을 확인했다(`hud.enabled == false`).
- 기즈모 선·월드 글자 렌더링은 2026-09-10 같은 날 IMGUI 단계에서 캡처로 확인했다(구·부채꼴·상자·화살표·글자).
- 패널에서 `1인`을 고른 뒤 게임 시작 경로(`NetworkManager.StartHost`)를 밟으면 혼자 런이 시작됐다 —
  단계 `Farming`, 씬 `Field/Idle`, 슬롯 1명, 자동 준비 자동 점등.
- Steam 시작 게이트는 값으로 확인했다: 오버라이드 없음 → 3, `1인` → 1, `2인` → 2, 범위 밖 → 3.
- 호출부를 넣은 뒤 게임 화면에서 피벗 십자가 그려지는 것을 캡처로 확인했고, 기즈모 패널에
  `선 9개`와 `yaw 322.275 · pitch 39.76 · 카메라 거리 4.5 · 락온 꺼짐`, 표본 240개짜리 그래프 2개가 떴다.
- 콜라이더 패널을 브라우저에서 켜서 게임 화면에 실제로 그려지는 것을 캡처로 확인했다 —
  플레이어 캡슐(초록), 잡귀 캡슐(빨강), 내 근접 판정 부채꼴(주황), 구조물 상자와 트리거(노랑). 선 867개.
### 2026-09-10 3인 테스트 반영

- 인게임 파티 바(체력·스태미나·신력)를 되살렸다. 캡처로 확인 — 왼쪽 아래에 `▶ 이름 무기`와 막대 3개.
- 로그에 시각과 태그가 붙었다. 캡처·API로 확인 — `12:46:37  [보스] …`, `보스만 보기`를 누르면 1줄만 남았다.
- 조준 방향 계산을 값으로 확인했다: 카메라 방위각 0도 → `(0, 1)`, 90도 → `(1, 0)`.
- **공격이 마우스 방향으로 나가는 것**은 클라이언트가 `CmdRequestAttack`에 조준을 실어 보내고
  서버가 검증해 쓴다. 락온 중에는 락온 방향이 이긴다.
- 모델 방향은 **두 곳을 다 돌려야 한다.** 서버는 `ServerSetFacing`으로 `_serverFacing`을,
  소유 클라이언트는 `ClientPredictFacing`으로 `_localFacing`을 돌린다 -
  `PlayerPresentation.FacingDirection`이 소유 클라이언트에서는 로컬 예측값을 쓰므로,
  서버만 돌리면 호스트 화면에서만 캐릭터가 조준 방향을 본다(2026-09-10 3인 테스트에서 그렇게 보였다).
- **방울이 없는 사람의 우클릭**은 요청 자체를 보내지 않는다. 서버 왕복과 `치유 거부: NoWeapon` 로그가 사라진다.
  서버 검증은 그대로다.
- **미검증**: 3인 접속(L3), 실제 Steam 로비에서의 `시작` 버튼(Steam 클라이언트와 로비가 필요),
  `-devpanellan`으로 다른 기기에서 접속, 공격 판정 부채꼴(실제로 공격을 입력해 봐야 한다),
  타겟 부위 번호 글자(`ITargetPartIdSource`를 가진 보스 부위로 확인해야 한다 — 잡귀에는 없다),
  마우스 방향 공격과 방울 없는 우클릭(마우스 입력을 실제로 눌러 봐야 한다),
  화살 궤적 기즈모(활을 쏴 봐야 한다).
