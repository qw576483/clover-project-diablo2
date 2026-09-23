# -*- coding: utf-8 -*-
"""StormLib（`storm.dll`）的最小 ctypes 封装 —— 只做 `export_chars.py` 需要的那三件事。

为什么需要这个文件：`export_chars.py` 的 `Archive` 只依赖 `storm.open_archive /
storm.list_files / storm.read_file` 三个函数；本机原先用的是 `_assets_src/storm.py`
（已不在仓内），而 `storm.dll` 在本机是 `.ai-tmp/test/storm/storm.dll`（**不入仓**）。

⛔ 四个坑（前三条与 `tools/probes/mpq/extract_wanted.py` 文件头一致，第四条是本片实测）：
  1. `storm.dll` 是 **ANSI** 接口 ⇒ `SFileOpenArchive` 只吃 ANSI 路径。本机 MPQ 在
     `原版资源/_mpq_incoming/`（**含中文**）⇒ `open_archive()` 先 `os.chdir(包所在目录)`
     再用 **ASCII 相对名**调 DLL（`SFileOpenArchive("d2char.mpq")`）。
     ⚠️ 因此本模块会改进程 cwd —— 调用方不要再假设 cwd。
  2. ⛔ **不用 `SFileFindFirstFile` 全量枚举**：实测这份 DLL 上会吃到 8+ GB 内存且不返回。
     `list_files()` 改为读 MPQ **自带的 `(listfile)`**（按名解出后逐行切），
     实测 `d2char.mpq` = 10034 条（完整）；⚠️ `D2data.mpq` 只有 **4 条**（该包的
     `(listfile)` 是个 stub）⇒ 调用方**不许**把 `list_files()` 当"包里有没有这个文件"的判据。
  3. `SFileHasFile` 在这份 DLL 上**不可信**（对不存在的名字也返回非 0）⇒ `has_file()`
     只能当提示，**判据永远是"能读回非空字节"**。
  4. **本机这份 `storm.dll` 的 `SFileOpenFileEx` 也是坏的**（实测：对 `amnu1hs.cof`
     返回一个垃圾值且 `phFile` 留空；只有对 `(listfile)` 偶然成功）⇒ 本模块**一律**用
     `SFileExtractFile`（extract_wanted.py 已验证可解的**唯一**可靠原语）解到临时文件再读回。
     ⛔ 因此 `read_file()` 会落一个临时文件；请用 `set_work_dir()` 把它指到
     `<项目根>/.ai-tmp/test/` 下（默认即如此）。

`storm.dll` 的定位顺序：
  1. 环境变量 `D2_STORM_DLL` 指定的完整路径；
  2. 本项目实际位置 `<repo>/.ai-tmp/test/storm/storm.dll`（**不入仓**）；
  3. 兼容历史布局 `<repo>/原版资源/storm.dll`、`<本文件目录>/storm.dll`。
"""
import ctypes
import os
from ctypes import c_char_p, c_int, c_uint32, c_void_p, POINTER, byref

#: MPQ 打开标志（StormLib）：只读。
MPQ_OPEN_READONLY = 0x0100

_SIG = {
    'SFileOpenArchive': ([c_char_p, c_uint32, c_uint32, POINTER(c_void_p)], c_int),
    'SFileOpenPatchArchive': ([c_void_p, c_char_p, c_char_p, c_uint32], c_int),
    'SFileCloseArchive': ([c_void_p], c_int),
    'SFileExtractFile': ([c_void_p, c_char_p, c_char_p, c_uint32], c_int),
    'SFileHasFile': ([c_void_p, c_char_p], c_int),
}

_LIB = None
_WORK_DIR = None
#: 中转文件名**按进程区分**：两个导出进程同时跑时共用同一个文件会互相覆盖
#: （截断 → 解 → 读 之间被对方插进来 ⇒ 读到别人的字节，静默产出错图）。
_TMP_NAME = 'storm-read-%d.tmp' % os.getpid()


def _repo_root():
    """本文件 = <repo>/tools/d2codec/storm.py ⇒ 仓库根 = 上两级。"""
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def _dll_candidates():
    here = os.path.dirname(os.path.abspath(__file__))
    return [
        os.environ.get('D2_STORM_DLL') or '',
        # 本项目实际位置（`extract_wanted.py` 文件头约定的落点；不入仓）
        os.path.join(_repo_root(), '.ai-tmp', 'test', 'storm', 'storm.dll'),
        # 兼容历史布局
        os.path.join(_repo_root(), '原版资源', 'storm.dll'),
        os.path.join(here, 'storm.dll'),
    ]


def load():
    """载入 `storm.dll` 并绑定签名（幂等）。返回 WinDLL 对象。"""
    global _LIB
    if _LIB is not None:
        return _LIB
    tried = []
    for p in _dll_candidates():
        if not p:
            continue
        tried.append(p)
        if os.path.exists(p):
            d = os.path.dirname(p)
            # 让 DLL 的依赖（若有）也能被找到
            try:
                os.add_dll_directory(d)
            except Exception:
                pass
            lib = ctypes.WinDLL(p)
            for n, (a, r) in _SIG.items():
                fn = getattr(lib, n)
                fn.argtypes = a
                fn.restype = r
            _LIB = lib
            _LIB._dll_path = p
            return _LIB
    raise OSError('找不到 storm.dll；试过：%s\n'
                  '（可用环境变量 D2_STORM_DLL 指定完整路径）' % tried)


def _A(s):
    """ANSI 接口只吃 ASCII 字节（坑 1）。

    ⚠️ MPQ 内部名一律用**反斜杠**；调用方常常写正斜杠（如 `PALETTE_REL`）⇒ 这里统一归一化。
    实测代价：直接把 `data/global/palette/units/Pal.dat` 交给 `SFileExtractFile` **解不出来**
    （返回空产物），换反斜杠才出 768 字节。
    """
    if isinstance(s, str):
        s = s.replace('/', '\\')
    return s.encode('ascii')


def open_archive(path, patch=None):
    """打开一个 MPQ，返回 handle。

    ⚠️ 会 `os.chdir` 到该 MPQ 所在目录（见文件头 坑 1）—— 因为 ANSI 接口 + 本仓库
    的 `原版资源/` 路径含中文。`patch` 非空时叠加补丁归档（同目录、ASCII 名）。
    """
    lib = load()
    ap = os.path.abspath(path)
    if not os.path.exists(ap):
        raise OSError('MPQ 不存在：%s' % ap)
    d, base = os.path.dirname(ap), os.path.basename(ap)
    os.chdir(d)
    h = c_void_p()
    if not lib.SFileOpenArchive(_A(base), 0, MPQ_OPEN_READONLY, byref(h)) or not h.value:
        raise OSError('SFileOpenArchive 失败：%s' % ap)
    if patch:
        lib.SFileOpenPatchArchive(h, _A(patch), b'', 0)
    return h


def close_archive(h):
    load().SFileCloseArchive(h)


def has_file(handle, name):
    """⚠️ 仅供参考（坑 3：这份 DLL 对不存在的名字也返回非 0）。"""
    return bool(load().SFileHasFile(handle, _A(name)))


def set_work_dir(path):
    """设置 `read_file()` 落临时文件的目录（默认 `<repo>/.ai-tmp/test/storm-tmp`）。"""
    global _WORK_DIR
    _WORK_DIR = os.path.abspath(path)
    os.makedirs(_WORK_DIR, exist_ok=True)


def _work_dir():
    global _WORK_DIR
    if _WORK_DIR is None:
        set_work_dir(os.path.join(_repo_root(), '.ai-tmp', 'test', 'storm-tmp'))
    return _WORK_DIR


def read_file(handle, name):
    """按名读回 `bytes`；读不到 / 0 字节 ⇒ `None`（这才是"存在"的判据）。

    实现 = `SFileExtractFile` 解到一个**固定**临时文件（见文件头 坑 4；这份 DLL 的
    `SFileOpenFileEx` 不可用）。每次先**截断**目标文件再解 ⇒ 解不出来时它是 0 字节，
    因此不存在"读到上一次的残留"这种假阳性。
    """
    lib = load()
    dst = os.path.join(_work_dir(), _TMP_NAME)
    # ⚠️ 中转**目录**可能被外部清掉（实测：一个 228 s 的导出跑到第 7 套时
    #    `.ai-tmp/test/storm-tmp/` 被别的片删了 ⇒ `FileNotFoundError` 直接中断整跑）
    #    ⇒ 打开失败就地重建再试一次。
    for attempt in (0, 1):
        try:
            with open(dst, 'wb'):    # 截断（⛔ 不用 os.remove：宿主 safe-delete 守门）
                pass
            break
        except FileNotFoundError:
            if attempt:
                raise
            os.makedirs(_work_dir(), exist_ok=True)
    lib.SFileExtractFile(handle, _A(name), _A(dst), 0)
    try:
        if os.path.getsize(dst) <= 0:
            return None
        with open(dst, 'rb') as fh:
            return fh.read()
    except OSError:
        return None


def list_files(handle):
    """返回归档内**全部**名字（原件拼法，含反斜杠）。

    实现 = 读 MPQ 自带的 `(listfile)`（⛔ 不用 `SFileFindFirstFile`，见文件头 坑 2）。
    没有 `(listfile)` ⇒ 抛 `OSError`（**不许静默返回空表**：空表会让 `has_prefix`
    恒 False，导出器会把所有单位当"包里没有"跳过 —— 那是静默失效）。
    """
    for name in ('(listfile)', 'listfile', '(LISTFILE)'):
        raw = read_file(handle, name)
        if raw:
            txt = raw.decode('latin1')
            out = [ln.strip() for ln in txt.replace('\x00', '\n').splitlines() if ln.strip()]
            if out:
                return out
    raise OSError('该 MPQ 没有可读的 (listfile) ⇒ 本封装无法枚举名字（见 storm.py 文件头 坑 2）')
