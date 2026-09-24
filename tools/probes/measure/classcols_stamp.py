# classcols: heartbeat appender that keeps EXACTLY ONE 'REPORT-FINGERPRINT' line, always LAST.
# Usage: write the message into .ai-tmp/test/_hb_msg.txt, then run this.
# Interface (team-lead adopted u52block's proposal): a report must NOT carry its own fingerprint
# (self-reference paradox); the report's pointer = heartbeat LAST line.
#   ① entry shape stays '[<ISO8601>] <msg>' -- do NOT reformat entries to satisfy a gate
#      ("不许为了让判据变绿去改证据的形状"; the line-start gate is the side being fixed).
#   ② REPLACE the previous stamp instead of appending a second one
#      => 'REPORT-FINGERPRINT' appears exactly once and is the last line.
import hashlib, io, os, datetime

ROOT = 'c:/Work/Server/f-v2/clover-project-diablo2'
HB = os.path.join(ROOT, '.ai-tmp', 'test', 'classcols-heartbeat.txt')
RP = os.path.join(ROOT, '.ai-tmp', 'test', 'report-u52-classcols.md')
MSG = os.path.join(ROOT, '.ai-tmp', 'test', '_hb_msg.txt')
STAMP_TAG = 'REPORT-FINGERPRINT '

raw = open(RP, 'rb').read()
lines = raw.count(b'\n') if raw.endswith(b'\n') else raw.count(b'\n') + 1
stamp = STAMP_TAG + 'sha256_16=%s bytes=%d lines=%d(ReadAllLines) mtime=%s' % (
    hashlib.sha256(raw).hexdigest()[:16].upper(), len(raw), lines,
    datetime.datetime.fromtimestamp(os.path.getmtime(RP)).strftime('%Y-%m-%dT%H:%M:%S'))

raw_hb = open(HB, 'rb').read()
bom = raw_hb[:3] == b'\xef\xbb\xbf'
body = (raw_hb[3:] if bom else raw_hb).decode('utf-8')
keep = [l for l in body.rstrip('\n').split('\n') if not l.startswith(STAMP_TAG)]

entry = ''
if os.path.exists(MSG):
    msg = io.open(MSG, encoding='utf-8').read().strip()
    if msg:
        ts = datetime.datetime.now().astimezone().isoformat()
        entry = '[' + ts + '] ' + msg
    os.remove(MSG)
if entry:
    keep.append(entry)
keep.append(stamp)

data = (('\ufeff' if bom else '') + '\n'.join(keep) + '\n').encode('utf-8')
io.open(HB, 'wb').write(data)

print('heartbeat lines = %d (ReadAllLines)' % len(keep))
print('STARTS-WITH-REPORT-FINGERPRINT = %d' % sum(1 for l in keep if l.startswith(STAMP_TAG)))
print('LAST LINE = ' + keep[-1])
