#!/usr/bin/env python3
"""LFS 대상인데 일반 파일로 커밋된 대용량 파일 검사.

한 번 일반 파일로 들어간 대용량 에셋은 히스토리에 영구히 남는다.
나중에 LFS로 옮겨도 리포 용량은 줄지 않는다. 커밋 시점에 막아야 한다.
"""
import sys
import os
import subprocess

# .gitattributes에서 LFS로 지정한 확장자
LFS_EXT = {
    '.png', '.jpg', '.jpeg', '.tga', '.tif', '.tiff', '.psd', '.exr', '.hdr',
    '.fbx', '.obj', '.blend', '.dae', '.3ds', '.max',
    '.wav', '.mp3', '.ogg', '.aif',
    '.mp4', '.mov', '.webm',
    '.terrainlayer', '.raw',
    '.ttf', '.otf', '.zip', '.7z',
}

SIZE_WARN = 5 * 1024 * 1024   # 5MB


def is_lfs_pointer(path: str) -> bool:
    try:
        with open(path, 'rb') as f:
            head = f.read(120)
        return head.startswith(b'version https://git-lfs.github.com/spec/')
    except OSError:
        return False


def main(list_path: str) -> int:
    with open(list_path, encoding='utf-8') as f:
        changed = [ln.strip() for ln in f if ln.strip()]

    bad = []
    big = []

    for path in changed:
        if not os.path.exists(path):
            continue
        ext = os.path.splitext(path)[1].lower()
        size = os.path.getsize(path)

        if ext in LFS_EXT and not is_lfs_pointer(path):
            bad.append((path, size))
        elif ext not in LFS_EXT and size > SIZE_WARN:
            big.append((path, size))

    for path, size in big:
        mb = size / 1024 / 1024
        print(f'::warning file={path}::{mb:.1f}MB — LFS 대상 확장자가 아닙니다. '
              f'.gitattributes에 추가할지 검토하세요.')

    if bad:
        print('::error::LFS로 올라가지 않은 대용량 에셋이 있습니다.')
        for path, size in bad:
            mb = size / 1024 / 1024
            print(f'::error file={path}::{mb:.1f}MB — LFS 포인터가 아닙니다')
        print()
        print('해결:')
        print('  git lfs install')
        print('  git rm --cached <파일>')
        print('  git add <파일>')
        print('  git commit --amend')
        return 1

    print('LFS 검사 통과')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1]))
