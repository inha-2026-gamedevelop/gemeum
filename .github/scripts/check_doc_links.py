#!/usr/bin/env python3
"""문서 안의 상대 경로 링크가 실제로 존재하는지 검사.

무엇이 저장소에 있는지는 git에게 묻는다. 예전에는 제외할 폴더 이름을 코드에 적어 뒀는데,
.gitignore에 새 항목이 생길 때마다 이 목록도 같이 고쳐야 했고 잊으면 두 가지로 깨졌다.

- 올라가지 않는 문서(.claude/docs 인수인계 등)까지 걸어서 로컬에서만 실패한다
- 올라가지 않는 파일을 가리키는 링크를 CI가 깨진 링크로 잡는다

그래서 검사 대상은 추적되는 문서만, 링크 대상은 추적 여부로 판정한다.
로컬 결과와 CI 결과가 같아지는 것이 이 스크립트의 목적이다.
"""
import os
import re
import subprocess
import sys

# Windows 콘솔은 기본이 cp949라 한글 메시지에서 죽는다. CI(리눅스)와 같게 맞춘다
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

LINK = re.compile(r'\[[^\]]*\]\(([^)]+)\)')
SKIP = ('http://', 'https://', 'mailto:', '#')

# 검사할 문서의 범위. 워크플로의 paths 트리거와 같게 맞춘다.
# 루트의 md(CLAUDE.md, CONTRIBUTING.md, README.md)와 docs/ 아래가 우리 문서다.
#
# .claude/skills 아래는 커밋되어 있지만 서드파티 도구 번들이다.
# 템플릿 자리표시자({{path}})와 정규식 예시를 링크로 적어 둔 곳이 많아
# 검사에 넣으면 우리가 고칠 수 없는 실패로 모든 PR이 막힌다
SCOPE_FOLDERS = ('docs/',)


def git(args: list, stdin: str = None) -> str:
    """git을 부르고 표준 출력을 돌려준다. 실패하면 빈 문자열."""
    try:
        done = subprocess.run(
            ['git'] + args,
            input=stdin,
            capture_output=True,
            text=True,
            encoding='utf-8')
    except FileNotFoundError:
        return ''

    # check-ignore는 무시 대상이 하나도 없으면 1을 돌려준다. 그것은 실패가 아니다
    if done.returncode > 1:
        return ''

    return done.stdout


def repo_root() -> str:
    root = git(['rev-parse', '--show-toplevel']).strip()
    return root if root else ''


def tracked_paths() -> set:
    """저장소에 올라가 있는 파일 목록. 여기 없는 것은 GitHub에도 없다."""
    return {p for p in git(['ls-files', '-z']).split('\0') if p}


def tracked_markdown(tracked: set) -> list:
    """추적되는 md 중 우리 문서만. 범위 밖은 애초에 열지 않는다."""
    documents = []

    for path in tracked:
        if not path.lower().endswith('.md'):
            continue

        is_root  = ('/' not in path)
        is_scope = path.startswith(SCOPE_FOLDERS)

        if (is_root) or (is_scope):
            documents.append(path)

    return sorted(documents)


def ignored_paths(targets: list) -> set:
    """.gitignore가 무시하는 대상. 존재하지 않는 경로도 판정된다."""
    if not targets:
        return set()

    # 대상이 많아도 git을 한 번만 부른다
    out = git(['check-ignore', '-z', '--stdin'], stdin='\0'.join(targets) + '\0')
    return {p for p in out.split('\0') if p}


def is_tracked(target: str, tracked: set) -> bool:
    """파일이면 목록에 있는지, 폴더면 그 아래에 올라간 파일이 하나라도 있는지."""
    if target in tracked:
        return True

    prefix = target.rstrip('/') + '/'
    return any(path.startswith(prefix) for path in tracked)


def collect_links(documents: list) -> list:
    """(문서, 링크 원문, 해석된 경로) 목록을 모은다."""
    links = []

    for path in documents:
        with open(path, encoding='utf-8') as f:
            text = f.read()

        folder = os.path.dirname(path)

        for target in LINK.findall(text):
            target = target.split('#')[0].strip()
            if not target or target.startswith(SKIP):
                continue

            resolved = os.path.normpath(os.path.join(folder, target)).replace(os.sep, '/')
            links.append((path, target, resolved))

    return links


def main() -> int:
    root = repo_root()
    if not root:
        print('::error::git 저장소가 아니어서 무엇이 올라가는지 판정할 수 없습니다.')
        return 1

    os.chdir(root)

    tracked = tracked_paths()
    documents = tracked_markdown(tracked)

    links = collect_links(documents)

    # 무시 대상 판정은 한 번에 한다
    ignored = ignored_paths([resolved for _, _, resolved in links])

    broken = []
    uncommitted = []
    skipped = 0

    for path, target, resolved in links:
        # .gitignore 대상이면 GitHub에 없는 것이 정상이다. 링크가 깨진 것이 아니다
        if resolved in ignored:
            skipped += 1
            continue

        if not os.path.exists(resolved):
            broken.append((path, target))
            continue

        # 로컬에는 있는데 커밋되지 않은 경우. 이대로 올리면 CI에서만 깨진다
        if not is_tracked(resolved, tracked):
            uncommitted.append((path, target))

    if broken or uncommitted:
        if broken:
            print('::error::존재하지 않는 파일을 가리키는 링크가 있습니다.')
            for path, target in broken:
                print(f'::error file={path}::{target}')

        if uncommitted:
            print('::error::링크 대상이 저장소에 커밋되지 않았습니다. 함께 커밋하거나 링크를 지우세요.')
            for path, target in uncommitted:
                print(f'::error file={path}::{target}')

        return 1

    print(f'문서 링크 검사 통과: 문서 {len(documents)}개, 링크 {len(links)}개'
          f' (무시 대상 링크 {skipped}개는 건너뜀)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
