# black-why: measure the black fraction of the real Play frames, bucket by bucket.
# Cross-check for the offline geometry number in mapcheck Step29 (off-map share of
# the viewport rect) and for the probe's per-screen-bucket tile counts (BWY-DEEP bAny).
import sys
from PIL import Image

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/screenshots"
NAMES = ["blackwhy_T0", "blackwhy_T1", "blackwhy_T2"]
THRESH = 10          # mean of RGB below this = "black" (the D2 void is pure #000)

for n in NAMES:
    p = f"{ROOT}/{n}.png"
    try:
        im = Image.open(p).convert("RGB")
    except Exception as e:
        print(f"{n}: OPEN-FAIL {e}")
        continue
    w, h = im.size
    px = im.load()
    tot = 0
    blk = 0
    buckets = [0] * 16
    btot = [0] * 16
    step = 4                       # sample every 4th pixel in both axes
    for y in range(0, h, step):
        for x in range(0, w, step):
            r, g, b = px[x, y]
            m = (r + g + b) / 3.0
            isb = 1 if m < THRESH else 0
            tot += 1
            blk += isb
            by = int(y * 4 / h)          # image y=0 is TOP
            bx = int(x * 4 / w)
            bi = bx + (3 - by) * 4       # match probe: index = bx + by*4 with by 0 = BOTTOM
            btot[bi] += 1
            buckets[bi] += isb
    print(f"{n}: {w}x{h} blackFrac={blk/tot:.3f}  n={tot}")
    for row in range(3, -1, -1):
        cells = " ".join(f"{buckets[row*4+c]/max(btot[row*4+c],1):.2f}" for c in range(4))
        print(f"    by={row} (top->bottom) : {cells}")
