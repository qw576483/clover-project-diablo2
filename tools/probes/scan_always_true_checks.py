#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""scan_always_true_checks.py -- find "always-green" Check(...) assertions in the
offline host sources (tools/probes/**), i.e. assertions whose CONDITION is a
hardcoded truth value, so the host prints [ OK ] no matter what the program did.

Why this is a Python char-scanner and NOT a one-line grep
--------------------------------------------------------
Measured on this checkout (assert-audit, 2026-09-24): the earlier single-line regex
    Check\\([^;]*,\\s*true\\s*,
reported 10 hits and MISSED 6, because a real call can be split across lines:
    Check(
        "what",
        true,
        "detail");
Those 6 were combatcheck:1104, fullcheck:510/1229, savecheck:313/349/762.

And a grep cannot see this at all: measured 2026-09-24 (this pass), the earlier
PS scanner (.ai-tmp/test/scan_checks.ps1) SILENTLY TRUNCATED two files -- it
reported 112/270 calls in hosts/mapcheck/Program.cs and 46/262 in
hosts/uicheck/Program.cs. Cause: C# 11 interpolated strings that contain a nested
string literal inside a hole, e.g.
    $"实测 {CountIn(body, "EnsurePool().Take(")} / ..."
Its lexer ended `$"` at the first inner quote, desynced, then a later paren-balance
scan jumped far ahead and swallowed the rest of the file. Such a truncation is
invisible: the tool still prints a confident total. That is exactly the failure
mode this item exists to prevent, so the lexer below models `{...}` holes
(ICODE frames, nested strings, nested interpolations, verbatim `$@"` and `{{`/`}}`).

So: walk the file with a character-level C# lexer (code / // / /* */ / "str" /
@"verbatim" / $"interp" + holes / 'char'), then do balanced-paren matching on the
CODE positions only. A `Check(` commented out or inside a string must NOT count; a
call whose arguments span lines MUST count.

This script is a JUDGED ARTIFACT (tools/probes/, committed) and is wired into
tools/verify.ps1 as the `always-true-asserts` item. Do not "simplify" it back to a
grep -- the grep is the bug it exists to catch.

Usage
-----
    python tools/probes/scan_always_true_checks.py [--root <dir>] [--quiet]

Default root = <repo>/tools/probes (same scope the original scanner used).
Output (stdout, machine-readable):
    SCAN_ROOT=<dir>
    CHECK_FILES=<n>
    CHECK_INVOCATIONS=<n>
    COND_INDEX_SOURCES=def:<a> dir:<b> heuristic:<c>
    ALWAYS_TRUE_HITS=<n>
    HIT<TAB>rel:line<TAB>kind<TAB>condition<TAB>message     (one per hit)
Exit code: 0 = scan ran (hits are reported; the caller decides PASS/FAIL),
           3 = scan could not run (never claim PASS off a missing number).
"""

import argparse
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))

# C# lexer states
CODE, LINE, BLOCK, STR, VERB, CHAR, INTERP, ICODE = range(8)
CODE_STATES = (CODE, ICODE)

# "condition is a hardcoded truth value"
LITERAL_TRUE = re.compile(r'^(true|!false|1\s*==\s*1|0\s*==\s*0|!\s*\(\s*false\s*\))$')

# message text that admits the assertion is not really being made
SKIP_WORDS = ('跳过', '未实现', '留给', '略过', '占位', '不覆盖', '无法',
              'todo', 'skip', 'not covered')


def compute_states(text):
    """state[i] for every character of text (see the state constants above)."""
    n = len(text)
    st = [CODE] * n
    # frames: [kind, brace_depth, verbatim_flag]; stack[-1] is the current mode
    stack = [[CODE, 0, 0]]
    i = 0
    while i < n:
        kind = stack[-1][0]
        c = text[i]

        if kind == CODE or kind == ICODE:
            if c == '/' and i + 1 < n and text[i + 1] == '/':
                st[i] = kind
                st[i + 1] = LINE
                stack.append([LINE, 0, 0])
                i += 2
                continue
            if c == '/' and i + 1 < n and text[i + 1] == '*':
                st[i] = kind
                st[i + 1] = BLOCK
                stack.append([BLOCK, 0, 0])
                i += 2
                continue
            if c == '$' and i + 2 < n and text[i + 1] == '@' and text[i + 2] == '"':
                st[i] = kind
                st[i + 1] = kind
                st[i + 2] = INTERP
                stack.append([INTERP, 0, 1])
                i += 3
                continue
            if c == '@' and i + 2 < n and text[i + 1] == '$' and text[i + 2] == '"':
                st[i] = kind
                st[i + 1] = kind
                st[i + 2] = INTERP
                stack.append([INTERP, 0, 1])
                i += 3
                continue
            if c == '@' and i + 1 < n and text[i + 1] == '"':
                st[i] = kind
                st[i + 1] = VERB
                stack.append([VERB, 0, 1])
                i += 2
                continue
            if c == '$' and i + 1 < n and text[i + 1] == '"':
                st[i] = kind
                st[i + 1] = INTERP
                stack.append([INTERP, 0, 0])
                i += 2
                continue
            if c == '"':
                st[i] = STR
                stack.append([STR, 0, 0])
                i += 1
                continue
            if c == "'":
                st[i] = CHAR
                stack.append([CHAR, 0, 0])
                i += 1
                continue
            if kind == ICODE:
                if c == '{':
                    stack[-1][1] += 1
                elif c == '}':
                    stack[-1][1] -= 1
                    if stack[-1][1] <= 0:
                        stack.pop()
                        st[i] = INTERP
                        i += 1
                        continue
            st[i] = kind
            i += 1
            continue

        if kind == LINE:
            st[i] = LINE
            if c == '\n':
                stack.pop()
            i += 1
            continue

        if kind == BLOCK:
            st[i] = BLOCK
            if c == '*' and i + 1 < n and text[i + 1] == '/':
                st[i + 1] = BLOCK
                stack.pop()
                i += 2
                continue
            i += 1
            continue

        if kind == STR:
            st[i] = STR
            if c == '\\':
                if i + 1 < n:
                    st[i + 1] = STR
                    i += 2
                    continue
                i += 1
                continue
            if c == '"':
                stack.pop()
            i += 1
            continue

        if kind == CHAR:
            st[i] = CHAR
            if c == '\\':
                if i + 1 < n:
                    st[i + 1] = CHAR
                    i += 2
                    continue
                i += 1
                continue
            if c == "'":
                stack.pop()
            i += 1
            continue

        if kind == VERB:
            st[i] = VERB
            if c == '"':
                if i + 1 < n and text[i + 1] == '"':
                    st[i + 1] = VERB
                    i += 2
                    continue
                stack.pop()
            i += 1
            continue

        if kind == INTERP:
            verb = stack[-1][2] == 1
            if c == '\\' and not verb:
                st[i] = INTERP
                if i + 1 < n:
                    st[i + 1] = INTERP
                    i += 2
                    continue
                i += 1
                continue
            if c == '{':
                if i + 1 < n and text[i + 1] == '{':
                    st[i] = INTERP
                    st[i + 1] = INTERP
                    i += 2
                    continue
                st[i] = ICODE
                stack.append([ICODE, 1, 0])
                i += 1
                continue
            if c == '}':
                st[i] = INTERP
                if i + 1 < n and text[i + 1] == '}':
                    st[i + 1] = INTERP
                    i += 2
                    continue
                i += 1
                continue
            if c == '"':
                if verb and i + 1 < n and text[i + 1] == '"':
                    st[i] = INTERP
                    st[i + 1] = INTERP
                    i += 2
                    continue
                st[i] = INTERP
                stack.pop()
                i += 1
                continue
            st[i] = INTERP
            i += 1
            continue

        i += 1
    return st


IDENT_CH = re.compile(r'[A-Za-z0-9_]')


def split_top_level(text, states, lo, hi):
    """Split text[lo:hi] on commas at bracket depth 0 (CODE/ICODE positions)."""
    parts = []
    cur = []
    depth = 0
    for i in range(lo, hi):
        if states[i] in CODE_STATES:
            c = text[i]
            if c in '([{':
                depth += 1
            elif c in ')]}':
                depth -= 1
            elif c == ',' and depth == 0:
                parts.append(''.join(cur))
                cur = []
                continue
        cur.append(text[i])
    parts.append(''.join(cur))
    return parts


DEF_RE = re.compile(r'void\s+Check\s*\(([^)]*)\)')


def cond_index_from_defs(text, states):
    """Index of the bool parameter in a local `void Check(...)` definition."""
    found = set()
    for m in DEF_RE.finditer(text):
        if states[m.start()] not in CODE_STATES:
            continue
        params = [p.strip() for p in m.group(1).split(',')]
        for idx, p in enumerate(params):
            if re.search(r'\bbool\b', p):
                found.add(idx)
    return found


def looks_like_string_literal(arg):
    return bool(re.match(r'^[@$]*"', arg.strip()))


COMMENT_RE = re.compile(r'//[^\n]*|/\*.*?\*/', re.S)


def strip_comments_of(s):
    return COMMENT_RE.sub(' ', s)


def scan_file(text, states):
    """Return (invocations, hits) for one file's text + lexer states."""
    n = len(text)
    defs = cond_index_from_defs(text, states)
    hits = []
    invocations = 0
    i = 0
    while i < n:
        if states[i] in CODE_STATES and text.startswith('Check', i):
            prev = text[i - 1] if i > 0 else ' '
            if IDENT_CH.match(prev) or prev in '_.':
                i += 1
                continue
            # skip the definition site: `void Check(`
            k = i - 1
            while k >= 0 and text[k] in ' \t\r\n':
                k -= 1
            w = k
            while w >= 0 and text[w].isalpha():
                w -= 1
            if text[w + 1:k + 1] == 'void':
                i += 4
                continue
            # optional whitespace, then the opening paren (code positions only)
            j = i + 5
            while j < n and text[j] in ' \t\r\n' and states[j] in CODE_STATES:
                j += 1
            if j >= n or text[j] != '(' or states[j] not in CODE_STATES:
                i += 4
                continue
            depth = 1
            k = j + 1
            while k < n and depth > 0:
                if states[k] in CODE_STATES:
                    if text[k] == '(':
                        depth += 1
                    elif text[k] == ')':
                        depth -= 1
                k += 1
            if depth != 0:
                i = j + 1
                continue
            close = k - 1
            invocations += 1
            args = split_top_level(text, states, j + 1, close)
            idx_src = 'def' if len(defs) == 1 else None
            if len(defs) == 1:
                ci = next(iter(defs))
            else:
                ci = 0 if not looks_like_string_literal(args[0]) else 1
                idx_src = 'heuristic'
            if ci < len(args):
                cond = strip_comments_of(args[ci]).strip()
                msg_idx = 1 if ci == 0 else 0
                msg = args[msg_idx] if msg_idx < len(args) else ''
                if LITERAL_TRUE.match(cond):
                    low = msg.lower()
                    sw = any((w in msg) or (w in low) for w in SKIP_WORDS)
                    kind = 'literal-true/skipword' if sw else 'literal-true'
                    line = text.count('\n', 0, i) + 1
                    hits.append((line, kind, cond, msg.strip(), idx_src))
            i = k
            continue
        i += 1
    return invocations, hits, defs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default=os.path.join(REPO_ROOT, 'tools', 'probes'))
    ap.add_argument('--quiet', action='store_true')
    args = ap.parse_args()
    root = os.path.abspath(args.root)
    if not os.path.isdir(root):
        print('SCAN_ERROR=root not found: ' + root)
        return 3

    files = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin', '.git')]
        for fn in filenames:
            if fn.endswith('.cs'):
                files.append(os.path.join(dirpath, fn))
    files.sort()

    texts = {}
    states_by_file = {}
    defs_by_dir = {}
    for p in files:
        with open(p, 'r', encoding='utf-8-sig', errors='replace') as fh:
            t = fh.read()
        texts[p] = t
        states_by_file[p] = compute_states(t)
        d = os.path.dirname(p)
        defs_by_dir.setdefault(d, set())
        defs_by_dir[d] |= cond_index_from_defs(t, states_by_file[p])

    src_count = {'def': 0, 'dir': 0, 'heuristic': 0}
    total_inv = 0
    all_hits = []
    for p in files:
        rel = os.path.relpath(p, root).replace('\\', '/')
        inv, hits, defs = scan_file(texts[p], states_by_file[p])
        if len(defs) == 1:
            src_count['def'] += 1
        elif len(defs_by_dir.get(os.path.dirname(p), set())) == 1:
            src_count['dir'] += 1
        else:
            src_count['heuristic'] += 1
        total_inv += inv
        for (line, kind, cond, msg, _s) in hits:
            all_hits.append((rel, line, kind, cond, msg))

    print('SCAN_ROOT=' + root)
    print('CHECK_FILES=%d' % len(files))
    print('CHECK_INVOCATIONS=%d' % total_inv)
    print('COND_INDEX_SOURCES=def:%d dir:%d heuristic:%d'
          % (src_count['def'], src_count['dir'], src_count['heuristic']))
    print('ALWAYS_TRUE_HITS=%d' % len(all_hits))
    if not args.quiet:
        for (rel, line, kind, cond, msg) in all_hits:
            print('HIT\t%s:%d\t%s\t%s\t%s'
                  % (rel, line, kind, re.sub(r'\s+', ' ', cond)[:80],
                     re.sub(r'\s+', ' ', msg)[:120]))
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as exc:  # never let a crash look like "0 hits"
        print('SCAN_ERROR=%s: %s' % (type(exc).__name__, exc))
        sys.exit(3)
