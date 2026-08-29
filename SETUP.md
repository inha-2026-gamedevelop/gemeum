# 리포 초기 설정 순서

이 문서는 **한 번만** 하는 작업이다. 팀장이 진행한 뒤 이 파일은 지워도 된다.

---

## 1. 치환할 것

아래 자리표시자를 실제 값으로 바꾼다.

| 파일 | 자리표시자 | 바꿀 값 |
|---|---|---|
| `.github/CODEOWNERS` | — | 치환 완료 (@AidenKim923 · @oname36-creator · @kjw1237) |
| `.github/ISSUE_TEMPLATE/config.yml` | `OWNER/REPO` | 실제 리포 경로 |
| `README.md` | `<repo-url>` | 클론 주소 |

---

## 2. 리포 생성과 첫 푸시

```bash
git init
git lfs install
git add .
git commit -m "chore: 프로젝트 초기 설정과 기획 문서"
git branch -M main
git remote add origin <repo-url>
git push -u origin main

# 통합 브랜치 생성
git checkout -b develop
git push -u origin develop

# 담당자별 작업 브랜치 생성 (3인)
git checkout -b feature/Minsung && git push -u origin feature/Minsung
git checkout develop
git checkout -b feature/{이름2} && git push -u origin feature/{이름2}
git checkout develop
git checkout -b feature/{이름3} && git push -u origin feature/{이름3}
git checkout develop
```

**중요**: Unity 프로젝트 파일을 추가하기 **전에** `.gitattributes`를 먼저 커밋해야 한다.
LFS 설정보다 먼저 들어간 대용량 파일은 히스토리에 영구히 남는다.

---

## 3. 브랜치 보호 설정

`Settings > Branches > Add rule`

### `main`
- [x] Require a pull request before merging
- [x] Require approvals — 1
- [x] Require status checks to pass
  - `Unity 프로젝트 규칙 검사`
- [x] Do not allow bypassing the above settings

### `develop`
- [x] Require a pull request before merging
- [x] Require status checks to pass

---

## 4. UnityYAMLMerge 설정

각자 로컬에서 한 번씩 실행한다. `.gitattributes`의 `merge=unityyamlmerge`가 동작하려면 필요하다.

**Windows**

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver \
  '"C:/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
```

경로는 실제 설치 위치에 맞게 고친다.

Unity 에디터에서도 설정한다:
`Edit > Project Settings > Version Control > Mode` → **Visible Meta Files**
`Edit > Project Settings > Editor > Asset Serialization` → **Force Text**

이 두 설정이 안 되어 있으면 `.meta`가 생성되지 않거나 씬이 바이너리로 저장되어 diff가 불가능해진다.

---

## 5. Mirror 설치

Package Manager에서 Git URL로 설치하거나 Asset Store에서 받는다.

```
https://github.com/MirrorNetworking/Mirror.git?path=/Assets/Mirror
```

설치 후 확인할 것:

- `NetworkManager` 프리팹을 `Main.unity`에 배치
- Transport는 기본 **KCP Transport** 사용
- `Player` 프리팹에 `NetworkIdentity` 부착
- Player Prefab을 NetworkManager에 등록

Mirror는 `Assets/Mirror/` 에 설치되며, **수정하지 않는다.** 구매 에셋과 동일하게 취급한다.

---

## 6. 라벨 정리

기본 라벨을 지우고 아래로 교체하면 이슈 분류가 편하다.

| 라벨 | 색 | 용도 |
|---|---|---|
| `task` | `#0E8A16` | 작업 항목 |
| `bug` | `#D73A4A` | 버그 |
| `balance` | `#FBCA04` | 수치 조정 |
| `network` | `#1D76DB` | Mirror · 동기화 관련 |
| `blocked` | `#B60205` | 다른 작업 대기 중 |
| `prototype` | `#C5DEF5` | 프로토타입 단계 |
| `focus-dev` | `#BFD4F2` | 집중개발 단계 |

---

## 7. Unity 빌드 CI (선택)

`.github/workflows/build.yml`은 라이선스 등록 전까지 수동 실행만 가능하다.

활성화하려면:

1. https://game.ci/docs/github/activation 에서 `.alf` 파일 발급
2. Unity 라이선스 페이지에서 `.ulf` 받기
3. 리포 `Settings > Secrets and variables > Actions`에 등록
   - `UNITY_LICENSE` — `.ulf` 파일 전체 내용
   - `UNITY_EMAIL`
   - `UNITY_PASSWORD`
4. `build.yml`의 `schedule` 블록 주석 해제

**권장**: 프로토타입 단계에는 굳이 안 해도 된다. 집중개발에 들어가서 주간 빌드가 필요해질 때 설정하는 편이 낫다. 초기에 CI 세팅에 시간을 쓰면 정작 게임을 못 만든다.

---

## 8. AI 도구 연결

`CLAUDE.md`가 저장소 루트에 있으면 Claude Code 등이 자동으로 읽는다.
다른 도구를 쓴다면 그 도구가 읽는 파일명으로 심볼릭 링크를 만들거나 복사한다.

| 도구 | 지침 파일 |
|---|---|
| Claude Code | `CLAUDE.md` (그대로 사용) |
| Codex, Amp 등 AGENTS.md 규약을 따르는 도구 | `AGENTS.md` (루트에 있음, `CLAUDE.md`를 가리키기만 함) |
| Cursor | `.cursorrules` 또는 `.cursor/rules/` |
| GitHub Copilot | `.github/copilot-instructions.md` |

**내용을 복제하지 말고 링크한다.** 두 벌이 되면 반드시 갈라진다.

```bash
# 예시
ln -s ../CLAUDE.md .github/copilot-instructions.md
```

Windows에서 관리자 권한 없이 심볼릭 링크가 안 되면, `AGENTS.md`처럼 `CLAUDE.md`를 가리키기만 하는 한 줄짜리 파일로 대신한다 (내용 복제 금지 원칙은 동일하게 지킨다).

AI 협업 문서 전체는 `docs/ai/`에 있다. 팀원에게는 [docs/ai/README.md](docs/ai/README.md)만 알려주면 된다.

---

## 9. 폴더 구조 (제안)

```
Assets/
  Scenes/
    Main.unity            통합 씬 — 통합 담당자만
    Dev_Combat.unity
    Dev_AI.unity
    Dev_System.unity
    Dev_Art.unity
  Scripts/
    Network/              세션, 동기화
    Combat/               플레이어, 패링, 화살
    AI/                   잡귀, 보스, 스폰
    Systems/              장비, 경제, 특성, 점수
    UI/                   각 파트가 자기 UI를 여기에
    Data/                 ScriptableObject 정의
  Data/                   ScriptableObject 에셋 인스턴스
  Art/
    Models/
    Materials/
    Textures/
    VFX/
  Shaders/
  Audio/
  Mirror/                 Mirror 패키지 — 수정 금지
  ThirdParty/             구매 에셋 — 원본 그대로 유지
```

`ThirdParty/` 안의 구매 에셋은 **수정하지 않는다.** 고쳐야 하면 복사본을 만들어 프로젝트 폴더에 둔다. 원본을 고치면 에셋 업데이트 시 변경분이 날아간다.
