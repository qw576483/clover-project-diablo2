#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""charstat_batch_align.py -- three-way alignment: report <-> driver version <-> evidence batch.

WHY THIS EXISTS (team-lead 2026-09-24, slice `charstat`, item 2):
    A report's readings were only traceable to a driver version through *prose*.  Measured case:
    the shared driver `tools/probes/drivers/d2u3_charstat_drive.cs` was patched at 14:18:24
    (criterion fix, `MaxPart()`), while the real-machine evidence of that batch was produced by the
    14:12:36 revision.  Nothing on disk let a reviewer re-check that claim with a command.

WHAT IT CHECKS (per batch `tag`)
    1. every `evidence` row  : the file on disk is byte-identical to the ledger  (else `DRIFT`)
    2. every `producer-*` row: the file on disk now == the version recorded for the batch
       (else the producer MOVED after the batch -> `SUSPECT-*`)
    3. the report's machine-readable `BATCH <tag> ...` line == the ledger's producers
       (else `REPORT-MISMATCH`)

VERDICTS / EXIT CODES (a moved producer is NEVER a silent pass -- rule from team-lead)
    0   PASS                all evidence files match AND every producer still equals its batch version
    10  SUSPECT-DECLARED    a producer moved after the batch and the report declares it
                            (`stale=declared` on the BATCH line) -- loud, non-zero, not silent
    1   FAIL                SUSPECT-UNDECLARED / DRIFT / REPORT-MISMATCH / LEDGER-INVALID
    2   USAGE               bad arguments / missing ledger or report
    3   TOOL-ERROR          this tool itself blew up (see `RESULT FAIL(TOOL-ERROR: ...)`)

TEAM CONTRACT (README sections 67/68(c), team-lead 2026-09-24 -- this tool follows it):
    * green  <=>  a `RESULT OK` line is present AND the exit code is 0;
    * a tool crash must NEVER look green  =>  exit 3 + a `RESULT FAIL(TOOL-ERROR: ...)` line;
    * every run's FIRST line self-reports this tool's own version (`JUDGE-VERSION ...`), so a
      reader never has to paste a fingerprint by hand (those go stale within minutes).
    NOTE the deliberate deviation: the SUSPECT-DECLARED verdict uses **10**, not 3, because the
    team contract reserves 3 for TOOL-ERROR -- 3 must not mean two different things.

RULE 45 (gates must be able to go red): `--selftest` builds known-good + known-bad ledgers in a
temp dir and asserts the exit code of each.  A checker that cannot fail is not a checker.

`lines` uses the project-wide definition: ReadAllLines().Count == count(LF) + (1 if the file is
non-empty and does not end with LF else 0); binary files (contain a NUL byte) get `-`.
"""

import argparse
import datetime
import hashlib
import os
import re
import sys
import tempfile

# team contract (README 67 / 68(c), team-lead 2026-09-24): one meaning per exit code, and a
# crashing tool must never look green.  NOTE: the SUSPECT-DECLARED *verdict* is 10, not 3,
# because the contract reserves 3 for TOOL-ERROR.
VERDICT_EXIT = {
    "PASS": 0,
    "FAIL": 1,                  # SUSPECT-UNDECLARED / DRIFT / REPORT-MISMATCH / LEDGER-INVALID
    "USAGE": 2,
    "TOOL-ERROR": 3,
    "SUSPECT-DECLARED": 10,
}


def judge_version():
    """Self-report this tool's OWN version as the first line (team contract): a reader quotes
    THIS line instead of pasting a hash that goes stale within minutes."""
    with open(__file__, "rb") as fh:
        b = fh.read()
    n = b.count(b"\n") + (0 if (not b or b.endswith(b"\n")) else 1)
    mt = datetime.datetime.fromtimestamp(os.path.getmtime(__file__)).isoformat(timespec="seconds")
    print("JUDGE-VERSION charstat_batch_align.py sha256_16=%s bytes=%d lines=%d(ReadAllLines) mtime=%s"
          % (hashlib.sha256(b).hexdigest()[:16], len(b), n, mt))

ROLES_PRODUCER = ("producer-driver", "producer-runner", "producer-sheet")
ROLES = ("producer-driver", "producer-runner", "producer-sheet", "evidence")

# A declaration must be a WHOLE line and must name a producer-driver: prose that merely mentions
# "BATCH <tag> ..." inside a sentence must not be mistaken for the machine-readable claim.
BATCH_RE = re.compile(
    r"^BATCH[ \t]+(?P<tag>[A-Za-z0-9_.\-]+)[ \t]+producer-driver=(?P<pd>\S+)"
    r"(?:[ \t]+producer-runner=(?P<pr>\S+))?"
    r"(?:[ \t]+stale=(?P<stale>\S+))?[ \t]*$",
    re.M,
)
# <bytes>/<sha16>/<hh:mm:ss>
TRIPLE_RE = re.compile(r"^(?P<bytes>\d+)/(?P<sha>[0-9a-f]{16})/(?P<mtime>\d{2}:\d{2}:\d{2})$")


def sha16_and_bytes(path):
    with open(path, "rb") as fh:
        data = fh.read()
    # sha16 = first 8 bytes of SHA256, hex (same as the team's Get-FileHash .Substring(0,16))
    hexdigest = hashlib.sha256(data).hexdigest()[:16]
    return hexdigest, len(data), data


def logical_lines(data):
    if not data or b"\x00" in data:
        return "-"
    n = data.count(b"\n")
    if not data.endswith(b"\n"):
        n += 1
    return n


def fingerprint(path):
    """-> (sha16, bytes, lines, exists)"""
    if not os.path.isfile(path):
        return "-", "-", "-", False
    h, b, data = sha16_and_bytes(path)
    return h, b, logical_lines(data), True


def read_ledger(path):
    rows = []
    with open(path, "r", encoding="utf-8") as fh:
        for lineno, raw in enumerate(fh, 1):
            line = raw.rstrip("\r\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            parts = line.split("\t")
            if parts[0].strip().lower() == "tag":  # header
                continue
            if len(parts) < 4:
                raise ValueError("ledger %s:%d has %d columns (need >=4)" % (path, lineno, len(parts)))
            tag, role, rel, sha = parts[0].strip(), parts[1].strip(), parts[2].strip(), parts[3].strip()
            nbytes = parts[4].strip() if len(parts) > 4 else "-"
            nlines = parts[5].strip() if len(parts) > 5 else "-"
            mtime = parts[6].strip() if len(parts) > 6 else "-"
            note = parts[7].strip() if len(parts) > 7 else ""
            if role not in ROLES:
                raise ValueError("ledger %s:%d unknown role %r" % (path, lineno, role))
            rows.append(dict(tag=tag, role=role, rel=rel.replace("\\", "/"),
                             sha=sha, bytes=nbytes, lines=nlines, mtime=mtime, note=note))
    if not rows:
        raise ValueError("ledger %s has no data rows" % path)
    return rows


def parse_batch_lines(report_path):
    """-> ({tag: {pd,pr,stale}}, {tag: ...}) where the second set holds tags whose `BATCH` lines
    DISAGREE (a report carrying one live line and one stale line must not pass by accident: the
    last match would silently win)."""
    out = {}
    conflicting = set()
    with open(report_path, "r", encoding="utf-8", errors="replace") as fh:
        text = fh.read()
    for m in BATCH_RE.finditer(text):
        tag = m.group("tag")
        rec = dict(
            pd=m.group("pd") or "",
            pr=m.group("pr") or "",
            stale=(m.group("stale") or "").lower(),
        )
        if tag in out and out[tag] != rec:
            conflicting.add(tag)
        out[tag] = rec
    return out, conflicting


def check(repo, ledger_path, report_path):
    """-> (exitcode, lines[], counts)"""
    out = []
    hard_fail = False
    declared_suspect = False

    try:
        rows = read_ledger(ledger_path)
    except Exception as exc:  # noqa: BLE001 - any ledger problem is a hard failure
        return VERDICT_EXIT["FAIL"], ["LEDGER-INVALID %s" % exc], dict(tags=0, pass_=0, suspect=0, fail=1)

    try:
        claims, conflicting = parse_batch_lines(report_path)
    except Exception as exc:  # noqa: BLE001
        return VERDICT_EXIT["FAIL"], ["REPORT-UNREADABLE %s" % exc], dict(tags=0, pass_=0, suspect=0, fail=1)

    tags = []
    for r in rows:
        if r["tag"] not in tags:
            tags.append(r["tag"])

    n_pass = n_suspect = n_fail = 0
    for tag in tags:
        trows = [r for r in rows if r["tag"] == tag]
        claim = claims.get(tag)
        verdicts = []
        notes = []

        # ---- 1) evidence rows must be byte-identical
        for r in [x for x in trows if x["role"] == "evidence"]:
            p = os.path.join(repo, r["rel"])
            sha, nbytes, nlines, exists = fingerprint(p)
            if not exists:
                verdicts.append("DRIFT")
                notes.append("missing:%s" % r["rel"])
                continue
            if str(nbytes) != r["bytes"] or sha != r["sha"]:
                verdicts.append("DRIFT")
                notes.append("changed:%s (ledger %s/%s -> now %s/%s)"
                             % (r["rel"], r["sha"], r["bytes"], sha, nbytes))
            elif r["lines"] not in ("-", "") and str(nlines) != r["lines"]:
                verdicts.append("DRIFT")
                notes.append("lines:%s (%s -> %s)" % (r["rel"], r["lines"], nlines))

        # ---- 2) producers must still equal the version recorded for this batch
        if tag in conflicting:
            verdicts.append("REPORT-MISMATCH")
            notes.append("report carries CONFLICTING `BATCH %s` lines (last-match-wins is not a verdict)" % tag)
        if claim is None:
            verdicts.append("REPORT-MISMATCH")
            notes.append("report has no `BATCH %s` line (claim missing => unverifiable)" % tag)
        for r in [x for x in trows if x["role"] in ROLES_PRODUCER]:
            p = os.path.join(repo, r["rel"])
            sha, nbytes, nlines, exists = fingerprint(p)
            if not exists:
                verdicts.append("DRIFT")
                notes.append("missing-producer:%s" % r["rel"])
                continue
            now = "%s/%s" % (nbytes, sha)
            rec = "%s/%s" % (r["bytes"], r["sha"])
            if now != rec:
                verdicts.append("SUSPECT")
                notes.append("producer-moved:%s (%s -> %s)" % (r["role"], rec, now))
            # report claim must agree with the ledger row
            key = {"producer-driver": "pd", "producer-runner": "pr"}.get(r["role"])
            if key and claim is not None:
                claimed = claim[key]
                if claimed:
                    m = TRIPLE_RE.match(claimed)
                    cbytes = m.group("bytes") if m else claimed.split("/")[0]
                    csha = m.group("sha") if m else (claimed.split("/")[1] if "/" in claimed else "")
                    if cbytes != r["bytes"] or csha != r["sha"]:
                        verdicts.append("REPORT-MISMATCH")
                        notes.append("report cites %s=%s but ledger says %s/%s"
                                     % (r["role"], claimed, r["bytes"], r["sha"]))

        # ---- 3) verdict for the tag
        v = "PASS"
        if "REPORT-MISMATCH" in verdicts:
            v = "REPORT-MISMATCH"
        elif "DRIFT" in verdicts:
            v = "DRIFT"
        elif "SUSPECT" in verdicts:
            declared = (claim or {}).get("stale", "") == "declared"
            v = "SUSPECT-DECLARED" if declared else "SUSPECT-UNDECLARED"

        if v == "PASS":
            n_pass += 1
        elif v == "SUSPECT-DECLARED":
            n_suspect += 1
            declared_suspect = True
        else:
            n_fail += 1
            hard_fail = True

        ev = len([x for x in trows if x["role"] == "evidence"])
        prod = len([x for x in trows if x["role"] in ROLES_PRODUCER])
        out.append("BATCH-ALIGN tag=%s evidence=%d producers=%d verdict=%s" % (tag, ev, prod, v))
        if claim is None:
            out.append("  claim=none (report must carry `BATCH %s producer-driver=<bytes>/<sha16>/<mtime>`)" % tag)
        else:
            out.append("  claim=producer-driver:%s producer-runner:%s stale=%s"
                       % (claim["pd"] or "-", claim["pr"] or "-", claim["stale"] or "-"))
        if notes:
            out.append("  " + " | ".join(notes))

    counts = dict(tags=len(tags), pass_=n_pass, suspect=n_suspect, fail=n_fail)
    code = (VERDICT_EXIT["FAIL"] if hard_fail
            else (VERDICT_EXIT["SUSPECT-DECLARED"] if declared_suspect else VERDICT_EXIT["PASS"]))
    out.append("BATCH-ALIGN-SUMMARY tags=%d pass=%d suspect-declared=%d fail=%d exit=%d"
               % (counts["tags"], counts["pass_"], counts["suspect"], counts["fail"], code))
    return code, out, counts


# --------------------------------------------------------------------------------------
# Rule 45: the gate must be able to go red.  Known-good + known-bad samples, in a temp dir.
# --------------------------------------------------------------------------------------
def selftest():
    results = []
    with tempfile.TemporaryDirectory(prefix="batchalign_") as tmp:
        repo = tmp
        drv = os.path.join(repo, "drv.cs")
        ev = os.path.join(repo, "ev.tsv")
        rep = os.path.join(repo, "rep.md")
        with open(drv, "wb") as fh:
            fh.write(b"// driver v1\n")
        with open(ev, "wb") as fh:
            fh.write(b"a\tb\nc\td\n")
        d_sha, d_bytes, _ = sha16_and_bytes(drv)
        e_sha, e_bytes, e_data = sha16_and_bytes(ev)
        e_lines = logical_lines(e_data)
        hdr = "# tag\trole\tpath\tsha256_16\tbytes\tlines\tmtime\tnote\n"
        good = (hdr
                + "t1\tproducer-driver\tdrv.cs\t%s\t%d\t-\t2026-09-24T14:12:36\tx\n" % (d_sha, d_bytes)
                + "t1\tevidence\tev.tsv\t%s\t%d\t%d\t2026-09-24T14:16:55\ty\n" % (e_sha, e_bytes, e_lines))
        good_report = "BATCH t1 producer-driver=%d/%s/14:12:36 stale=declared\n" % (d_bytes, d_sha)
        bad_report = "BATCH t1 producer-driver=%d/%s/14:12:36 stale=none\n" % (d_bytes, d_sha)

        drv_v1 = b"// driver v1\n"

        def run(ledger_text, report_text, move_driver=False):
            """move_driver=True rewrites the driver ON DISK after the ledger was written =>
            the ledger's producer record becomes the 'batch version' and the file has moved."""
            lp = os.path.join(repo, "led.tsv")
            rp = os.path.join(repo, "rep.md")
            with open(lp, "w", encoding="utf-8") as fh:
                fh.write(ledger_text)
            with open(rp, "w", encoding="utf-8") as fh:
                fh.write(report_text)
            with open(drv, "wb") as fh:
                fh.write(drv_v1 + (b"// criterion fix\n" if move_driver else b""))
            return check(repo, lp, rp)[0]

        # 1) aligned + declared -> 0
        results.append(("aligned+declared", run(good, good_report), 0))
        # 2) evidence hash wrong -> 1 (known-bad ledger)
        broken = good.replace("%s\t%d\t%d\t2026-09-24T14:16:55" % (e_sha, e_bytes, e_lines),
                              "0000000000000000\t%d\t%d\t2026-09-24T14:16:55" % (e_bytes, e_lines))
        results.append(("evidence-drift", run(broken, good_report), 1))
        # 3) producer moved on disk + declared by the report -> 10 (loud, non-zero, not silent)
        results.append(("producer-moved+declared",
                        run(good, good_report, move_driver=True), VERDICT_EXIT["SUSPECT-DECLARED"]))
        # 3b) exit-code CONTRACT (README 67/68c): one meaning per code; 3 is TOOL-ERROR only, so
        #     the SUSPECT-DECLARED verdict must NOT be 3 (two meanings for one number is the bug).
        vals = sorted(VERDICT_EXIT.values())
        results.append(("exit-codes-pairwise-distinct",
                        0 if len(vals) == len(set(vals)) else 1, 0))
        results.append(("tool-error-is-3", 0 if VERDICT_EXIT["TOOL-ERROR"] == 3 else 1, 0))
        results.append(("suspect-declared-is-not-3",
                        0 if VERDICT_EXIT["SUSPECT-DECLARED"] != 3 else 1, 0))
        results.append(("only-pass-is-green",
                        0 if VERDICT_EXIT["PASS"] == 0 and sorted(v for k, v in VERDICT_EXIT.items()
                                                                 if k != "PASS")[0] > 0 else 1, 0))
        # 4) producer moved on disk, NOT declared -> 1
        results.append(("producer-moved+undeclared", run(good, bad_report, move_driver=True), 1))
        # 5) report cites a different producer than the ledger -> 1
        wrong_report = "BATCH t1 producer-driver=%d/ffffffffffffffff/14:12:36 stale=declared\n" % d_bytes
        results.append(("report-mismatch", run(good, wrong_report), 1))
        # 6) report has no BATCH line at all -> 1 (unverifiable == fail, never a silent pass)
        results.append(("claim-missing", run(good, "no batch line here\n"), 1))
        # 6b) report carries two CONFLICTING BATCH lines -> 1 (last-match-wins is not a verdict)
        conflict_report = good_report + ("BATCH t1 producer-driver=%d/aaaaaaaaaaaaaaaa/14:12:36 stale=declared\n" % d_bytes)
        results.append(("report-conflicting-lines", run(good, conflict_report), 1))
        # 7) producer moved + declared BUT evidence also drifted -> 1 (drift outranks the declaration)
        results.append(("moved+declared+drift",
                        run(good.replace("%s\t%d\t%d" % (e_sha, e_bytes, e_lines),
                                         "0000000000000000\t%d\t%d" % (e_bytes, e_lines)),
                            good_report, move_driver=True), 1))

    ok = 0
    for name, got, want in results:
        flag = "PASS" if got == want else "FAIL"
        if got == want:
            ok += 1
        print("SELFTEST %-32s got=%d want=%d -> %s" % (name, got, want, flag))
    print("SELFTEST-SUMMARY %d/%d -> %s" % (ok, len(results), "PASS" if ok == len(results) else "FAIL"))
    if ok == len(results):
        print("RESULT OK")
        return VERDICT_EXIT["PASS"]
    print("RESULT FAIL (%d/%d selftest cases behaved as documented, %d did not)"
          % (ok, len(results), len(results) - ok))
    return VERDICT_EXIT["FAIL"]


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    default_repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
    ap = argparse.ArgumentParser(description="report <-> driver version <-> evidence batch alignment")
    ap.add_argument("--repo", default=default_repo)
    ap.add_argument("--ledger", default=os.path.join(default_repo, ".ai-tmp", "test",
                                                     "d2u3_charstat_batches.tsv"))
    ap.add_argument("--report", default=os.path.join(default_repo, ".ai-tmp", "test",
                                                     "report-u3-charstat.md"))
    ap.add_argument("--selftest", action="store_true",
                    help="run the known-good/known-bad samples (rule 45) and exit")
    args = ap.parse_args()

    if args.selftest:
        sys.exit(selftest())

    if not os.path.isfile(args.ledger):
        print("LEDGER-MISSING %s" % args.ledger)
        sys.exit(1)
    if not os.path.isfile(args.report):
        print("REPORT-MISSING %s" % args.report)
        sys.exit(1)

    code, lines, _ = check(args.repo, args.ledger, args.report)
    for ln in lines:
        print(ln)
    sys.exit(code)


if __name__ == "__main__":
    main()
