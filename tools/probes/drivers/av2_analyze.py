#!/usr/bin/env python3
# =============================================================================
# av2_analyze.py -- audio-verify2 measurement script (NOT a product-code file).
#
# Reads the two TSVs written by av2_probe.cs and answers the two questions:
#   Q1  did the monster's OWN gethit sound (monster_hit_*) really start, and is it
#       staggered from the player-side impact sound (`hit`) by HitDelay/25 s?
#   Q2  how many frames did the monster HURT animation really render (E28 = "only 3 of 7")?
#
# usage:
#   python tools/probes/drivers/av2_analyze.py \
#       .ai-tmp/test/av2_events.tsv .ai-tmp/test/av2_anim_frames.tsv
# =============================================================================
import sys
from collections import defaultdict

HIT_KINDS = ("hit", "monster_hit_")


def rows(path):
    out = []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            p = line.split("\t")
            if len(p) >= 4:
                out.append(p)
    return out


def main():
    ev_path, anim_path = sys.argv[1], sys.argv[2]
    ev = rows(ev_path)
    anim = rows(anim_path)

    print("=" * 72)
    print("Q1  SFX rows (kind=SFX): t / phase / clip")
    print("=" * 72)
    sfx = [(float(p[1]), p[2], p[3].split("\t")[0]) for p in ev if p[0] == "SFX"]
    for t, ph, clip in sfx:
        mark = ""
        if clip == "hit":
            mark = "   <== PLAYER-SIDE IMPACT"
        elif clip.startswith("monster_hit_"):
            mark = "   <== MONSTER OWN GETHIT"
        print(f"  {t:9.3f}  {ph:12s}  {clip}{mark}")
    print(f"  total SFX starts = {len(sfx)}")
    counts = defaultdict(int)
    for _, _, c in sfx:
        counts[c] += 1
    print("  clip histogram:")
    for c in sorted(counts):
        print(f"    {counts[c]:3d}  {c}")

    print()
    print("=" * 72)
    print("Q1b two-sound stagger (impact `hit`  ->  monster_hit_*)")
    print("=" * 72)
    for i, (t, ph, clip) in enumerate(sfx):
        if not clip.startswith("monster_hit_"):
            continue
        # nearest preceding `hit` within 1.0 s
        prev = None
        for j in range(i - 1, -1, -1):
            if t - sfx[j][0] > 1.0:
                break
            if sfx[j][2] == "hit" and sfx[j][1] == ph:
                prev = sfx[j]
                break
        if prev is None:
            print(f"  t={t:9.3f} phase={ph:12s} {clip}: no `hit` in the same phase window (route = ApplyDamage)")
        else:
            d = t - prev[0]
            print(f"  t={t:9.3f} phase={ph:12s} {clip}: +{d*1000:6.1f} ms after `hit` "
                  f"(2 frames/25fps = 80 ms for zm/fa; 5/25 = 200 ms for si)")

    print()
    print("=" * 72)
    print("Q2  monster hurt-animation frames (contiguous runs per action)")
    print("=" * 72)
    runs = []
    cur = None
    for p in anim:
        t = float(p[0])
        sprite, rect, act, idx, n = p[1], p[2], p[3], p[4], int(p[5])
        if cur is None or act != cur["act"]:
            if cur is not None:
                runs.append(cur)
            cur = {"act": act, "start": t, "end": t, "n": 0, "idx": [], "sprites": []}
        cur["end"] = t
        cur["n"] = n
        cur["idx"].append(idx)
        cur["sprites"].append(sprite)
    if cur is not None:
        runs.append(cur)

    for r in runs:
        dur = r["end"] - r["start"]
        fps = (r["n"] / dur) if dur > 0 else 0.0
        print(f"  action={r['act']:8s} frames={r['n']:3d} idx=[{','.join(r['idx'])}] "
              f"t={r['start']:.3f}..{r['end']:.3f} ({dur:.3f}s, ~{fps:.1f} fps)")
        if r["act"] == "hit":
            print(f"      sprites={r['sprites']}")

    hits = [r for r in runs if r["act"] == "hit"]
    print()
    if hits:
        best = max(h["n"] for h in hits)
        print(f"  ===> HURT runs = {len(hits)}, largest rendered frame count = {best}")
        print(f"       E28 baseline: only 3 of the 7 frames of `fa` (fallen) were rendered.")
        if best >= 7:
            print("       VERDICT: the full hurt animation is rendered (>= 7 frames).")
        elif best >= 4:
            print("       VERDICT: better than 3 frames but the animation is NOT complete.")
        else:
            print("       VERDICT: still <= 3 frames -- E28 NOT fixed.")
    else:
        print("  ===> no hurt-animation run captured (no `hit` action frames in the sample)")


if __name__ == "__main__":
    main()
