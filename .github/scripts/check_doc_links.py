#!/usr/bin/env python3
"""문서 안의 상대 경로 링크가 실제로 존재하는지 검사."""
import os
import re
import sys

LINK = re.compile(r'\[[^\]]*\]\(([^)]+)\)')
SKIP = ('http://', 'https://', 'mailto:', '#')

# 구매 에셋과 Unity 생성물. 저장소에 커밋되지 않으므로(.gitignore) CI
# 체크아웃에는 존재하지 않는다. 로컬에는 있어서 로컬 검사만 통과하고 CI에서
# 깨지는 일이 생기므로, 걷는 쪽과 링크 대상 쪽 모두에서 제외한다.
SKIP_DIRS = {'99.ThirdParty', '.git', 'Library', 'Temp', 'obj', 'Build', 'Logs'}


def in_skipped_dir(path: str) -> bool:
    """링크 대상이 저장소에 없는 폴더를 가리키는지 판정."""
    return any(part in SKIP_DIRS for part in path.replace(os.sep, '/').split('/'))


def main() -> int:
    broken = []
    for root, dirs, files in os.walk('.'):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            if not name.endswith('.md'):
                continue
            path = os.path.join(root, name)
            with open(path, encoding='utf-8') as f:
                text = f.read()
            for target in LINK.findall(text):
                target = target.split('#')[0].strip()
                if not target or target.startswith(SKIP):
                    continue
                resolved = os.path.normpath(os.path.join(root, target))
                if in_skipped_dir(resolved):
                    continue
                if not os.path.exists(resolved):
                    broken.append((path, target))

    if broken:
        print('::error::존재하지 않는 파일을 가리키는 링크가 있습니다.')
        for path, target in broken:
            print(f'::error file={path}::{target}')
        return 1

    print('문서 링크 검사 통과')
    return 0


if __name__ == '__main__':
    sys.exit(main())
