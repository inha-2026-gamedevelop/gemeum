#!/usr/bin/env python3
"""문서 안의 상대 경로 링크가 실제로 존재하는지 검사."""
import os
import re
import sys

LINK = re.compile(r'\[[^\]]*\]\(([^)]+)\)')
SKIP = ('http://', 'https://', 'mailto:', '#')


def main() -> int:
    broken = []
    for root, dirs, files in os.walk('.'):
        if '/.git' in root:
            continue
        dirs[:] = [d for d in dirs if d != '99.ThirdParty']
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
