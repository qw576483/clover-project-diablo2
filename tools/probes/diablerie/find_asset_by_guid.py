# 按 guid 反查参考工程 Diablerie 里的资产（走 jsDelivr，不需要 git clone / 不需要本地 Git）
#
# 用途：本项目多次从 mofr/Diablerie 取原版 UI 资产。当手上只有一个 Unity guid
#      （例如某 prefab 的 `m_Sprite: {fileID: ..., guid: ...}`）时，用它定位该 guid
#      属于哪个资产文件，再由调用方只取回那一个文件。
#
# 口径（不要改成"整包下载"）：先用文件清单 API 取全仓路径，再并发取回所有 `*.png.meta`
#      并 grep guid —— 只下载 meta（每个几百字节）；命中后再取目标资产。
#      子 sprite 的 `fileID` 是 `21300000 + 2×索引`（Multiple 导入），
#      所以拿到 meta 后可以直接算出命中资产里的**帧索引**。
#
# 用法：
#   python tools/probes/diablerie/find_asset_by_guid.py <guid> [--repo <user/repo>] [--ref <ref>]
# 退出码：0 = 命中 >= 1；1 = 未命中；2 = 清单取不到（网络 / 仓库名或 ref 变了）
import argparse
import concurrent.futures as cf
import json
import sys
import urllib.parse
import urllib.request

# 输出一律 ASCII：Windows 控制台默认 GBK，`print()` 撞到非 GBK 字符会抛 UnicodeEncodeError
# ⇒ 脚本崩掉的退出码与"未命中"混在一起，读数就不可信了（本项目已踩过同型的坑）。
try:
    sys.stdout.reconfigure(errors="replace")
except Exception:
    pass

LIST_API = "https://data.jsdelivr.com/v1/packages/gh/{repo}@{ref}?structure=flat"
RAW = "https://cdn.jsdelivr.net/gh/{repo}@{ref}/{path}"


def fetch(url: str) -> str:
    with urllib.request.urlopen(url, timeout=60) as r:
        return r.read().decode("utf-8", "replace")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("guid")
    ap.add_argument("--repo", default="mofr/Diablerie")
    ap.add_argument("--ref", default="master")
    args = ap.parse_args()

    try:
        listing = json.loads(fetch(LIST_API.format(repo=args.repo, ref=args.ref)))
    except Exception as e:                       # 清单拿不到 = BLOCKED，不是"没命中"
        print(f"LIST-FAIL {type(e).__name__} {e}")
        return 2

    files = listing.get("files", [])
    metas = [x["name"] for x in files if x["name"].lower().endswith(".png.meta")]
    sizes = {x["name"]: x.get("size") for x in files}
    print(f"REPO={args.repo}@{args.ref} FILES={len(files)} PNG_META={len(metas)}")

    hits = []
    fails = []

    def get(name):
        try:
            return name, fetch(RAW.format(repo=args.repo, ref=args.ref, path=urllib.parse.quote(name)))
        except Exception as e:
            return name, "ERR:" + type(e).__name__

    with cf.ThreadPoolExecutor(16) as ex:
        for name, text in ex.map(get, metas):
            if text.startswith("ERR:"):
                fails.append((name, text))
            elif args.guid in text:
                hits.append((name, text))

    for name, text in fails:
        print(f"FETCH-FAIL {name} {text}")

    print(f"SCANNED={len(metas)} HITS={len(hits)} FETCH_FAIL={len(fails)}")
    for name, text in hits:
        asset = name[:-5]                              # 去掉 .meta
        print(f"ASSET={asset} BYTES={sizes.get(asset)}")
        for line in text.splitlines():
            s = line.strip()
            if s.startswith("guid:") or s.startswith("spriteMode:"):
                print(f"  {s}")
        # Multiple 导入：fileID 21300000 + 2×索引 ⇒ 直接报出这个资产里"有没有子 sprite"
        for i, line in enumerate(text.splitlines()):
            if "internalID: 21300000" in line:
                print("  NOTE=Multiple sprite: sub-sprite index = (fileID - 21300000) / 2")
                break
    return 0 if hits else 1


if __name__ == "__main__":
    sys.exit(main())
