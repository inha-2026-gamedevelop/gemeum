# 씬 · 에셋 안전 규칙

> Unity 프로젝트에서 되돌릴 수 없는 사고는 대부분 씬 충돌과 `.meta` 손실에서 나온다.
> 이 문서의 규칙은 협상 대상이 아니다.

---

## 1. 씬 소유권

**Unity 씬 파일은 머지가 사실상 불가능하다.** 두 사람이 같은 씬을 고치면 한쪽 작업이 통째로 사라진다.

| 씬 | 수정 권한 |
|---|---|
| `Assets/00.Scenes/Dev_Combat.unity` | 네트워크 · 전투 (@AidenKim923) |
| `Assets/00.Scenes/Dev_AI.unity` | 몬스터 · 보스 (@oname36-creator) |
| `Assets/00.Scenes/Dev_System.unity` **(미생성)** | 게임플레이 시스템 (@kjw1237) |
| `Assets/00.Scenes/Dev_Art.unity` **(미생성)** | 테크니컬 아트 (@AidenKim923 겸임) |
| `Assets/00.Scenes/Main.unity` | **통합 담당자만** |

### 규칙

- 남의 씬은 **열어도 되지만 저장하지 않는다.** 열기만 해도 Unity가 미세한 변경을 기록할 수 있다
- 실수로 저장했으면 즉시 `git checkout -- Assets/00.Scenes/Dev_XXX.unity`
- 통합은 주 1회, 통합 담당자가 진행한다
- 한 PR에 씬 파일이 2개 이상 들어가면 CI가 경고한다. 경고가 뜨면 이유를 PR에 적는다

### AI에게

씬 파일을 수정하기 전에 **어느 씬인지, 그 씬의 소유자가 누구인지** 확인한다.
`Main.unity`를 건드려야 하면 반드시 사전 승인을 받는다.

---

## 2. `.meta` 파일

Unity는 모든 에셋에 `.meta`를 만들고, 그 안에 GUID를 넣는다.
프리팹과 씬은 **경로가 아니라 GUID로** 에셋을 참조한다.

`.meta`를 빼고 커밋하면:
- 다른 사람의 에디터에서 GUID가 새로 발급된다
- 프리팹과 씬의 참조가 전부 끊긴다 (Missing 표시)
- 원인이 드러나기까지 시간이 오래 걸린다

### 규칙

- 에셋을 추가하면 `.meta`를 **함께** 커밋한다
- 에디터 밖에서 파일을 옮길 때는 `.meta`도 같이 옮긴다
- 에셋을 지우면 `.meta`도 지운다
- Unity 에디터 안에서 이동 · 삭제하면 자동으로 처리된다. **가능하면 에디터 안에서 한다**

### 필수 프로젝트 설정

```
Edit > Project Settings > Version Control > Mode
  → Visible Meta Files

Edit > Project Settings > Editor > Asset Serialization
  → Force Text
```

이 두 설정이 없으면 `.meta`가 생성되지 않거나 씬이 바이너리로 저장되어 diff가 불가능해진다.

### 자동 검사

`.github/scripts/check_meta.py`가 PR마다 검사한다. 실패하면 병합할 수 없다.

---

## 3. Git LFS

### 클론 직후 반드시

```bash
git lfs install
git lfs pull
```

이걸 안 하면 이미지와 모델이 텍스트 한 줄로 보인다.

### 왜 중요한가

한 번 일반 파일로 커밋된 대용량 에셋은 **히스토리에 영구히 남는다.**
나중에 LFS로 옮겨도 리포 용량은 줄지 않는다. 커밋 시점에 막아야 한다.

### 대상

`.gitattributes` 참조. 이미지, 3D 모델, 오디오, 영상, 지형 데이터, 폰트.

새 확장자를 추가할 일이 생기면 **`.gitattributes`를 고치기 전에 팀에 알린다.** 이미 커밋된 파일에는 소급 적용되지 않는다.

### 자동 검사

`.github/scripts/check_lfs.py`가 PR마다 검사한다.

---

## 4. 외부 에셋

외부에서 가져온 에셋은 원본을 **수정하지 않는다.** 두 폴더가 대상이다.

| 폴더 | 내용 | git |
|---|---|---|
| `Assets/98.FreeAssets/` | 무료 에셋 (플러그인 · 애니메이션 등) | **커밋한다** |
| `Assets/99.ThirdParty/` | 구매 에셋 | `.gitignore` 제외 |

고쳐야 하면:
1. 필요한 파일만 프로젝트 폴더로 복사
2. 복사본을 수정
3. 원본은 그대로 유지

원본을 고치면 에셋 업데이트 시 변경분이 날아간다. 그리고 무엇을 고쳤는지 추적할 수 없다.

### 무료 에셋을 커밋할 때

`98.FreeAssets/`는 `99.ThirdParty/`와 달리 저장소에 올라간다. 팀원이 각자 임포트하면 GUID가 갈려 참조가 끊어지기 때문이다. 넣기 전에 확인할 것:

- `.meta`를 **하나도 빠짐없이** 같이 커밋한다. 하나라도 빠지면 2장의 사고가 그대로 재현된다.
- 바이너리가 `.gitattributes`의 LFS 대상 확장자인지 확인한다 (3장). `.fbx` · `.png` 등은 자동으로 LFS로 간다.
- 에셋 하나당 폴더 하나로 담는다.
- 딸려 온 데모 씬을 `EditorBuildSettings`에 추가하지 않는다.

배분 기준과 이유는 [프로젝트 구조 설계서 2.1](../04-architecture.md).

### 라이선스

구매 · 무료 에셋은 상업 이용 가능 범위로 관리한다.
2027년 인디게임 출품전 제출을 대비해 근거를 `docs/licenses/`에 보관한다.

| 기록할 것 |
|---|
| 에셋명, 제작자 |
| 출처 URL |
| 라이선스 종류 |
| 상업 이용 가능 여부 |
| 취득일 |

---

## 5. Terrain 데이터

Gaia로 생성한 Terrain은 **테크니컬 아트 담당자만 수정한다.**

여러 명이 건드리면 머지 충돌 시 복구가 불가능하다. Terrain 데이터는 diff를 읽을 수 없다.

---

## 6. UnityYAMLMerge

`.gitattributes`에 `merge=unityyamlmerge`를 지정해뒀지만, **로컬 git config가 없으면 무시된다.**

각자 한 번씩 설정한다 (Windows 기준):

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver \
  '"C:/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
```

경로는 실제 설치 위치에 맞춘다.

**주의**: SmartMerge가 있어도 씬 동시 편집은 여전히 위험하다. 이건 최후의 안전망이지 허가증이 아니다.

---

## 7. 사고가 났을 때

### 씬을 덮어썼다

1. 즉시 작업을 멈춘다. 추가 커밋을 하지 않는다
2. `git reflog`로 덮어쓰기 이전 커밋을 찾는다
3. 해당 커밋의 씬 파일만 꺼낸다: `git checkout <해시> -- Assets/00.Scenes/XXX.unity`
4. 팀에 알린다. 상대의 작업이 사라졌을 수 있다

### `.meta`가 날아가 참조가 끊겼다

1. `git log --diff-filter=D -- "*.meta"` 로 삭제된 `.meta`를 찾는다
2. 복구: `git checkout <해시>^ -- <경로>.meta`
3. 복구가 안 되면 프리팹과 씬에서 참조를 수동으로 다시 연결해야 한다

### 대용량 파일이 LFS 없이 들어갔다

**커밋 직후라면** amend로 고칠 수 있다:

```bash
git rm --cached <파일>
git add <파일>
git commit --amend
```

**이미 푸시했다면** 히스토리 재작성이 필요하다. 혼자 하지 말고 팀장과 상의한다.

---

## 8. AI 작업 시 추가 규칙

- 씬 · 프리팹 · 에셋을 수정한 뒤 **임포트와 Console 오류를 확인**한다
- 파일을 되돌리기 전에 diff를 확인한다. 변경 전체가 자기 작업임을 확인하지 못하면 파일 단위 복원을 실행하지 않는다
- 삭제 · 대량 이동 · 강제 덮어쓰기는 대상을 명시하고 사전 승인을 받는다
- 시각 결과는 캡처나 사용자 확인 없이는 `미검증`으로 표시한다
