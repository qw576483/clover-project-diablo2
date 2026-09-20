# -*- coding: utf-8 -*-
"""
agent-10 · 离线落盘场景 / 预制体 / meta / BuildSettings（**用户尚未打开编辑器，只能离线写**）

为什么需要它：
  Unity 侧的一键生成器是 `Assets/Editor/ProjectBuilder.cs`，但它只能在**活着的编辑器**里跑；
  本项目的硬约束是「用户尚未打开编辑器 ⇒ 禁止 unity run / Unity.exe -batchmode」。
  而交付验收要求 `Assets/Scenes/*.unity` 与 16 个 `Assets/Resources/UI/*.prefab` **真实存在**，
  所以先用本脚本把这些资产**按 Unity 6 的确切序列化格式**离线落盘，
  `ProjectBuilder.cs` 负责「幂等校验 + 自愈重生成」（发现资产缺失/坏掉才重建，不覆盖用户手改）。

关键做法（**不猜序列化格式**）：
  · 场景里的 `Main Camera` / `Global Light 2D` 两块**直接从模板自带场景 `SampleScene.unity` 里
    原样取出**（含 URP 的 `UniversalAdditionalCameraData` 与 `Light2D` 的脚本 GUID），
    只做「fileID 重编号 + 少数字段打补丁」—— 这样序列化字段集与模板 100% 一致；
  · 空 GameObject（MapRoot / EntityRoot / Bootstrap）按模板里 Transform 的确切字段集合成；
  · `.prefab` / `.unity` / `.cs` 的 `.meta` 用「路径 → md5」的**确定性 GUID**，
    这样脚本引用（预制体的 `m_Script`）在离线阶段就能写死并稳定。

用法：
  python tools/buildcheck/gen_assets.py
"""

import hashlib
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CLIENT = os.path.join(ROOT, "client")
ASSETS = os.path.join(CLIENT, "Assets")

PANELS = [
    "BootPanel", "MainMenuPanel", "SettingsPanel", "CharSelectPanel", "CharCreatePanel",
    "LoadingPanel", "PausePanel", "HudPanel", "MiniMapPanel", "InventoryPanel",
    "CharacterPanel", "SkillTreePanel", "QuestLogPanel", "NpcDialogPanel", "ShopPanel", "DeathPanel",
]
SCENES = ["Boot", "Menu", "Stage"]


def guid_for(unity_path):
    """确定性 GUID：同一条路径永远得到同一个 guid（离线写 meta 的前提）。"""
    return hashlib.md5(("clover-project-diablo2:" + unity_path).encode("utf-8")).hexdigest()


def read(p):
    with io.open(p, "r", encoding="utf-8-sig") as f:
        return f.read().replace("\r\n", "\n")


def write(p, text):
    d = os.path.dirname(p)
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with io.open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return len(text.encode("utf-8"))


# ─────────────────────────────────────────────────────────────────────────────────
#  1. 反射模板场景：把 SampleScene.unity 拆成 {classId: {fileId: documentText}}
# ─────────────────────────────────────────────────────────────────────────────────
DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)\n", re.M)


def split_scene(text):
    docs = {}
    marks = list(DOC_RE.finditer(text))
    for i, m in enumerate(marks):
        cls, fid = int(m.group(1)), int(m.group(2))
        end = marks[i + 1].start() if i + 1 < len(marks) else len(text)
        body = text[m.end():end]
        if body.endswith("\n"):
            body = body[:-1]
        docs.setdefault(cls, {})[fid] = body
    return docs


def remap(body, idmap):
    """把文档体里的 fileID 引用按 idmap 重编号（含 `&` 声明处，但声明头不在这里）。"""
    def repl(m):
        v = int(m.group(1))
        return "fileID: %d" % idmap.get(v, v)
    return re.sub(r"fileID: (\d+)", repl, body)


def emit_scene(path, docs, header_ids, blocks):
    """header_ids 用模板的 settings 文档；blocks = [(cls, old_obj_id, [组件 old id...], patch_fn)]"""
    out = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
    # settings 文档原样（29/104/157/196）
    for cls, fid in [(29, 1), (104, 2), (157, 3), (196, 4)]:
        out.append("--- !u!%d &%d" % (cls, fid))
        out.append(docs[cls][fid])
    for cls, declared_id, body in blocks:
        out.append("--- !u!%d &%d" % (cls, declared_id))
        out.append(body)
    # 所有对象都是根对象：m_RootOrder 按出现顺序重排（模板里两个根分别是 0/1，别留重复值）
    root = [0]
    def renum(m):
        v = root[0]
        root[0] += 1
        return "m_RootOrder: %d" % v
    text = re.sub(r"m_RootOrder: \d+", renum, "\n".join(out))
    write(path, text + "\n")


def main():
    sample = read(os.path.join(ASSETS, "Scenes", "SampleScene.unity"))
    docs = split_scene(sample)

    GO, TR, CAM, AL, MONO = 1, 4, 20, 81, 114

    cam_go, cam_tr, cam_cam, cam_al, cam_mono = 519420028, 519420032, 519420031, 519420029, 519420030
    light_go, light_tr, light_mono = 619394800, 619394802, 619394801

    def camera_blocks(base, ortho_size, bg, pos):
        idmap = {
            cam_go: base, cam_tr: base + 1, cam_cam: base + 2, cam_al: base + 3, cam_mono: base + 4,
            light_go: base + 5, light_tr: base + 6, light_mono: base + 7,
        }
        go = remap(docs[GO][cam_go], idmap)
        tr = remap(docs[TR][cam_tr], idmap)
        cam = remap(docs[CAM][cam_cam], idmap)
        al = remap(docs[AL][cam_al], idmap)
        mo = remap(docs[MONO][cam_mono], idmap)
        # 打补丁：正交尺寸 / 背景色 / 机位
        cam = re.sub(r"orthographic size: [-\d.]+", "orthographic size: %s" % ortho_size, cam)
        cam = re.sub(r"m_BackGroundColor: \{[^}]*\}",
                     "m_BackGroundColor: {r: %s, g: %s, b: %s, a: 0}" % bg, cam)
        tr = re.sub(r"m_LocalPosition: \{[^}]*\}",
                    "m_LocalPosition: {x: %s, y: %s, z: %s}" % pos, tr)
        # Global Light 2D 三件套（原样，仅重编号）
        lgo = remap(docs[GO][light_go], idmap)
        ltr = remap(docs[TR][light_tr], idmap)
        lmo = remap(docs[MONO][light_mono], idmap)
        return go, tr, cam, al, mo, lgo, ltr, lmo

    def empty_go(declared_id, name, new_transform_id):
        go = (
            "GameObject:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n"
            "  serializedVersion: 6\n"
            "  m_Component:\n"
            "  - component: {fileID: %d}\n"
            "  m_Layer: 0\n"
            "  m_Name: %s\n"
            "  m_TagString: Untagged\n"
            "  m_Icon: {fileID: 0}\n"
            "  m_NavMeshLayer: 0\n"
            "  m_StaticEditorFlags: 0\n"
            "  m_IsActive: 1" % (new_transform_id, name)
        )
        tr = (
            "Transform:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n"
            "  m_GameObject: {fileID: %d}\n"
            "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n"
            "  m_LocalPosition: {x: 0, y: 0, z: 0}\n"
            "  m_LocalScale: {x: 1, y: 1, z: 1}\n"
            "  m_ConstrainProportionsScale: 0\n"
            "  m_Children: []\n"
            "  m_Father: {fileID: 0}\n"
            "  m_RootOrder: 0\n"
            "  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}" % declared_id
        )
        return go, tr

    written = []

    # ── 场景：Boot / Menu / Stage ─────────────────────────────────────────────
    # 相机参数与 CameraRig 对齐：正交、z=-10（`Module/Camera/CameraRig.cs:59` CameraDistance=10）、
    # 正交尺寸 6（`:62` DefaultOrthographicSize=6）。背景近黑（原版暗黑）。
    for scene in SCENES:
        base = 100000 if scene == "Boot" else (101000 if scene == "Menu" else 102000)
        go, tr, cam, al, mo, lgo, ltr, lmo = camera_blocks(
            base, "6", ("0", "0", "0"), ("0", "0", "-10"))
        blocks = [(GO, base, go), (TR, base + 1, tr), (CAM, base + 2, cam),
                  (AL, base + 3, al), (MONO, base + 4, mo)]
        if scene == "Stage":
            # 世界精灵走 URP 2D（Sprite-Lit-Default）⇒ 必须有全局 2D 光，否则一片黑
            blocks += [(GO, base + 5, lgo), (TR, base + 6, ltr), (MONO, base + 7, lmo)]
            for i, name in enumerate(["MapRoot", "EntityRoot"]):
                gid = base + 100 + i * 2
                g, t = empty_go(gid, name, gid + 1)
                blocks += [(GO, gid, g), (TR, gid + 1, t)]
        if scene == "Boot":
            gid = base + 100
            g, t = empty_go(gid, "Bootstrap", gid + 1)
            # Bootstrap 唯一手动挂载的业务脚本
            mb = (
                "MonoBehaviour:\n"
                "  m_ObjectHideFlags: 0\n"
                "  m_CorrespondingSourceObject: {fileID: 0}\n"
                "  m_PrefabInstance: {fileID: 0}\n"
                "  m_PrefabAsset: {fileID: 0}\n"
                "  m_GameObject: {fileID: %d}\n"
                "  m_Enabled: 1\n"
                "  m_EditorHideFlags: 0\n"
                "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n"
                "  m_Name:\n"
                "  m_EditorClassIdentifier:" % (gid, guid_for("Assets/Scripts/App/Bootstrap.cs"))
            )
            g = g.replace("- component: {fileID: %d}" % (gid + 1),
                          "- component: {fileID: %d}\n  - component: {fileID: %d}" % (gid + 1, gid + 2))
            blocks += [(GO, gid, g), (TR, gid + 1, t), (MONO, gid + 2, mb)]

        p = os.path.join(ASSETS, "Scenes", scene + ".unity")
        emit_scene(p, docs, None, blocks)
        written.append(p)
        write(p + ".meta",
              "fileFormatVersion: 2\nguid: %s\nDefaultImporter:\n  externalObjects: {}\n"
              "  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
              % guid_for("Assets/Scenes/%s.unity" % scene))
        written.append(p + ".meta")

    # ── 16 个面板预制体（空壳：根节点铺满，内容由脚本 OnOpen 里搭）─────────────
    for i, name in enumerate(PANELS):
        go_id, rt_id, mb_id = 1000 + i * 10, 1001 + i * 10, 1002 + i * 10
        go = (
            "GameObject:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n"
            "  serializedVersion: 6\n"
            "  m_Component:\n"
            "  - component: {fileID: %d}\n"
            "  - component: {fileID: %d}\n"
            "  m_Layer: 5\n"          # 5 = UI（Unity 内置层）
            "  m_Name: %s\n"
            "  m_TagString: Untagged\n"
            "  m_Icon: {fileID: 0}\n"
            "  m_NavMeshLayer: 0\n"
            "  m_StaticEditorFlags: 0\n"
            "  m_IsActive: 1" % (rt_id, mb_id, name)
        )
        rt = (
            "RectTransform:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n"
            "  m_GameObject: {fileID: %d}\n"
            "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n"
            "  m_LocalPosition: {x: 0, y: 0, z: 0}\n"
            "  m_LocalScale: {x: 1, y: 1, z: 1}\n"
            "  m_ConstrainProportionsScale: 0\n"
            "  m_Children: []\n"
            "  m_Father: {fileID: 0}\n"
            "  m_RootOrder: 0\n"
            "  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n"
            "  m_AnchorMin: {x: 0, y: 0}\n"
            "  m_AnchorMax: {x: 1, y: 1}\n"
            "  m_AnchoredPosition: {x: 0, y: 0}\n"
            "  m_SizeDelta: {x: 0, y: 0}\n"
            "  m_Pivot: {x: 0.5, y: 0.5}" % go_id
        )
        mb = (
            "MonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n"
            "  m_GameObject: {fileID: %d}\n"
            "  m_Enabled: 1\n"
            "  m_EditorHideFlags: 0\n"
            "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n"
            "  m_Name:\n"
            "  m_EditorClassIdentifier:" % (go_id, guid_for("Assets/Scripts/UI/%s.cs" % name))
        )
        p = os.path.join(ASSETS, "Resources", "UI", name + ".prefab")
        write(p, "\n".join(["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:",
                            "--- !u!1 &%d" % go_id, go,
                            "--- !u!224 &%d" % rt_id, rt,
                            "--- !u!114 &%d" % mb_id, mb]) + "\n")
        written.append(p)
        write(p + ".meta",
              "fileFormatVersion: 2\nguid: %s\nPrefabImporter:\n  externalObjects: {}\n"
              "  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
              % guid_for("Assets/Resources/UI/%s.prefab" % name))
        written.append(p + ".meta")

    # ── 被引用的脚本 .meta（离线写死 GUID，否则预制体/场景的 m_Script 解析不到）──
    #    ⚠️ 只新增 .meta 边车文件，**不碰任何 .cs 内容**（详见回报「未决/说明」）。
    for p in ["Assets/Scripts/App/Bootstrap.cs"] + ["Assets/Scripts/UI/%s.cs" % n for n in PANELS]:
        write(os.path.join(ASSETS, p.replace("Assets/", "")) + ".meta",
              "fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n"
              "  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n"
              "  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
              % guid_for(p))
        written.append(os.path.join(ASSETS, p.replace("Assets/", "")) + ".meta")

    # ── Build Settings：Boot(0) → Menu(1) → Stage(2)，保留其它配置对象 ──────────
    ebs = os.path.join(CLIENT, "ProjectSettings", "EditorBuildSettings.asset")
    old = read(ebs)
    lines = []
    for s in SCENES:
        lines.append("  - enabled: 1")
        lines.append("    path: Assets/Scenes/%s.unity" % s)
        lines.append("    guid: %s" % guid_for("Assets/Scenes/%s.unity" % s))
    pat = re.compile(r"  m_Scenes:\n(?:  - enabled: \d+\n    path: [^\n]*\n    guid: [^\n]*\n)+")
    assert pat.search(old), "EditorBuildSettings.asset 里找不到 m_Scenes 段（文件被改过？）"
    new = pat.sub("  m_Scenes:\n" + "\n".join(lines) + "\n", old, count=1)
    # 幂等：第二次跑得到的内容与第一次完全相同（本脚本可重复执行）
    write(ebs, new)
    written.append(ebs)

    print("已落盘 %d 个文件：" % len(written))
    for p in written:
        print("  %8d B  %s" % (os.path.getsize(p), os.path.relpath(p, ROOT)))


if __name__ == "__main__":
    main()
