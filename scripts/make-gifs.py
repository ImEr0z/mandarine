"""Assembles the README animations from the frame folders written by tools/Mandarine.Shots.

    python scripts/make-gifs.py <frames folder> <output folder>

Every sub-folder of frames (001.png, 002.png...) becomes <name>.gif: shrunk to a README-friendly width,
reduced to an adaptive palette and looping forever.
"""
import glob
import os
import sys

from PIL import Image

WIDTH = 820
FRAME_MS = 85
LAST_FRAME_MS = 1400


def build(folder: str, target: str) -> None:
    paths = sorted(glob.glob(os.path.join(folder, '*.png')))
    if not paths:
        return
    frames = []
    for path in paths:
        image = Image.open(path).convert('RGB')
        if image.width > WIDTH:
            image = image.resize((WIDTH, round(image.height * WIDTH / image.width)), Image.LANCZOS)
        frames.append(image.quantize(colors=160, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG))
    durations = [FRAME_MS] * len(frames)
    durations[-1] = LAST_FRAME_MS
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True, disposal=1)
    print(f'{os.path.basename(target)}: {len(frames)} frames, {os.path.getsize(target) / 1024:.0f} KB')


if __name__ == '__main__':
    source, output = sys.argv[1], sys.argv[2]
    os.makedirs(output, exist_ok=True)
    for name in sorted(os.listdir(source)):
        build(os.path.join(source, name), os.path.join(output, name + '.gif'))
