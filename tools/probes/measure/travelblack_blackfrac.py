# travel-black: black-pixel fraction of the landing frames, plus a 4x4 bucket grid.
#
# Same formula/threshold as the sibling measure script blackwhy_blackfrac.py
# (mean of RGB < 10 = "black"; the D2 void is pure #000) -- only the file list is
# taken from argv so one script can measure any travelblack_* triple.
#
#   python tools/probes/measure/travelblack_blackfrac.py <png> [<png> ...]
import sys
from PIL import Image

THRESH = 10          # mean of RGB below this = "black"


def measure(path):
    try:
        im = Image.open(path).convert("RGB")
    except Exception as e:
        print(f"{path}: OPEN-FAIL {e}")
        return
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
            bi = bx + (3 - by) * 4       # index = bx + by*4 with by 0 = BOTTOM
            btot[bi] += 1
            buckets[bi] += isb
    name = path.replace("\\", "/").split("/")[-1]
    print(f"{name}: {w}x{h} blackFrac={blk/tot:.3f}  n={tot}")
    for row in range(3, -1, -1):
        cells = " ".join(f"{buckets[row*4+c]/max(btot[row*4+c],1):.2f}" for c in range(4))
        print(f"    by={row} (top->bottom) : {cells}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        measure(p)
