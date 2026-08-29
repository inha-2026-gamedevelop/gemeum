#!/usr/bin/env python3
"""Unity .meta 파일 누락 검사.

Assets/ 아래에 파일이나 폴더를 추가하면 Unity가 .meta를 함께 만든다.
.meta를 빼고 커밋하면 다른 사람의 에디터에서 GUID가 새로 발급되어
프리팹과 씬의 참조가 통째로 끊긴다. 원인 찾기가 매우 어려운 사고다.
"""
import sys
import os

IGNORE_SUFFIX = ('.meta',)
IGNORE_DIRS = ('Assets/StreamingAssets/aa/',)


def unity_ignores(path: str) -> bool:
    """Unity가 .meta를 만들지 않는 경로인지 판정.

    Unity는 점으로 시작하거나 물결로 끝나는 이름, cvs 폴더, .tmp 파일을
    임포트 대상에서 제외한다. 이런 경로에 .meta를 요구하면 영원히 통과할 수 없다.
    """
    for part in path.split('/'):
        if not part:
            continue
        if part.startswith('.') or part.endswith('~'):
            return True
        if part.lower() == 'cvs' or part.endswith('.tmp'):
            return True
    return False


def main(list_path: str) -> int:
    with open(list_path, encoding='utf-8') as f:
        changed = [ln.strip() for ln in f if ln.strip()]

    changed_set = set(changed)
    missing = []

    for path in changed:
        if not path.startswith('Assets/'):
            continue
        if path.endswith(IGNORE_SUFFIX):
            continue
        if any(path.startswith(d) for d in IGNORE_DIRS):
            continue
        if unity_ignores(path):
            continue

        meta = path + '.meta'
        # 이번 PR에 함께 있거나, 이미 저장소에 있으면 통과
        if meta in changed_set or os.path.exists(meta):
            continue
        missing.append(path)

    if missing:
        print('::error::.meta 파일이 누락된 에셋이 있습니다.')
        for m in missing:
            print(f'::error file={m}::{m}.meta 가 커밋되지 않았습니다')
        print()
        print('해결: Unity 에디터에서 프로젝트를 한 번 연 뒤')
        print('      생성된 .meta 파일을 함께 커밋하세요.')
        return 1

    print('.meta 검사 통과')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1]))
