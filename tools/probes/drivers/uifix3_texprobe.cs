// =============================================================================
// uifix3_texprobe.cs -- edit-mode GPU-side truth for one sprite: load it, blit
// to a RenderTexture, read back pixels, and report the center / border / alpha
// statistics.  Settles "is the imported texture really the PNG on disk".
// ASCII only.  Run: unity command run_script --file uifix3_texprobe.cs --entry UF3.Tex.Probe
// =============================================================================
using System;
using UnityEngine;

namespace UF3
{
    public static class Tex
    {
        public static string ProbeDef() { return Probe(null); }

        public static string Probe(string path)
        {
            if (string.IsNullOrEmpty(path))
                path = "Assets/Resources/Clover/D2/UI/Panel/boxframe_settings.png";
            var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) return "TEX-MISSING " + path;
            var old = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            read.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);

            var px = read.GetPixels32();
            int transparent = 0, opaque = 0;
            for (var i = 0; i < px.Length; i++) { if (px[i].a == 0) transparent++; else if (px[i].a == 255) opaque++; }
            var c = px[px.Length / 2 + tex.width / 2];
            var b = px[2 * tex.width + 2];
            UnityEngine.Object.DestroyImmediate(read);
            return "TEX " + path + " " + tex.width + "x" + tex.height
                + " center=(" + c.r + "," + c.g + "," + c.b + "," + c.a + ")"
                + " border2,2=(" + b.r + "," + b.g + "," + b.b + "," + b.a + ")"
                + " a0=" + transparent + " a255=" + opaque;
        }
    }
}
