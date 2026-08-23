# 처음 프로젝트를 열 때

> 클론부터 첫 실행까지. 이 문서 하나만 끝내면 개발을 시작할 수 있다.

---

## 1. 클론

```bash
git clone <repo-url>
cd geumeum

git lfs install
git lfs pull
```

**`git lfs pull`을 빼먹지 않는다.** 안 하면 이미지와 모델이 텍스트 한 줄로 보인다.

---

## 2. Unity 버전

**Unity 6.4.7f1** 로 연다. Unity Hub에서 해당 버전을 설치한다.

다른 버전으로 열면 프로젝트 파일이 자동 갱신되고, 되돌리기 어렵다.
버전이 없으면 열기 전에 먼저 설치한다.

---

## 3. 필수 프로젝트 설정 확인

처음 연 뒤 아래 두 가지를 확인한다.

```
Edit > Project Settings > Version Control > Mode
  → Visible Meta Files

Edit > Project Settings > Editor > Asset Serialization
  → Force Text
```

이게 안 되어 있으면 `.meta`가 생성되지 않거나 씬이 바이너리로 저장되어 협업이 불가능해진다.

---

## 4. UnityYAMLMerge 설정

한 번만 하면 된다 (Windows 기준).

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver \
  '"C:/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
```

경로는 실제 설치 위치에 맞춘다.

---

## 5. 자기 씬 열기

**`Main.unity`를 열지 않는다.** 자기 파트의 개발 씬을 연다.

| 파트 | 씬 |
|---|---|
| 네트워크 · 전투 | `Assets/Scenes/Dev_Combat.unity` |
| AI · 보스 설계 | `Assets/Scenes/Dev_AI.unity` |
| 게임플레이 시스템 | `Assets/Scenes/Dev_System.unity` |
| 테크니컬 아트 | `Assets/Scenes/Dev_Art.unity` |

---

## 6. 첫 실행

1. 자기 씬을 연다
2. Play 버튼을 누른다
3. Console에 에러가 없는지 확인한다

에러가 있으면 **고치기 전에 먼저 팀에 공유한다.** 자기 환경 문제인지 코드 문제인지 구분해야 한다.

---

## 7. 여러 인스턴스로 접속 테스트

3인 협동 게임이므로 혼자서도 여러 클라이언트를 띄워야 한다.
방법은 [멀티플레이 테스트 절차](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md) 2절 참조.

---

## 8. 다음에 읽을 것

| 언제 | 문서 |
|---|---|
| 파일이나 폴더를 만들 때 | [씬 · 에셋 안전](SCENE_AND_ASSET_SAFETY.md) |
| 커밋 · 푸시하기 직전 | [협업 규칙](../../CONTRIBUTING.md) |
| 네트워크 코드를 쓸 때 | [네트워크 권위](NETWORK_AUTHORITY.md) |
| 밸런스 수치를 다룰 때 | [밸런스 데이터 작업](BALANCE_DATA_WORKFLOW.md) |
| 성능 문제가 실제로 생겼을 때 | [Unity 엔지니어링 패턴](UNITY_ENGINEERING_PATTERNS.md) |

**처음부터 전부 읽지 않는다.** 필요할 때 해당 문서만 연다.

---

## 자주 겪는 문제

| 증상 | 원인 | 해결 |
|---|---|---|
| 이미지가 텍스트로 보임 | LFS 미실행 | `git lfs pull` |
| 참조가 Missing | `.meta` 누락 | 팀에 공유, 임의로 다시 연결하지 않음 |
| 컴파일 에러가 안 사라짐 | 재컴파일 중이거나 Play Mode | 기다리거나 Play 중지 |
| 씬을 열었는데 비어 있음 | 다른 씬을 연 것 | 경로 확인 |
| 프로젝트가 업그레이드되려 함 | Unity 버전 불일치 | **취소하고** 6.4.7f1로 다시 연다 |
