// p41_camprobe.cs -- throws-away mechanics pre-check for pass 5 (NOT part of the tour).
// NOT shipped; lives in <project>/.ai-tmp/drivers/ and is deleted before delivery. ASCII ONLY.
//
// WHY: the tour captures its world tiles with a camera RenderTexture (Camera.Render() = exactly what
// `capture_game_view --source camera` does) because `capture_game_view --save_path` resolves the path
// under Assets/ (measured: save_path "Temp/x.png" landed in "Assets/Temp/x.png") and writing under
// Assets/ during Play forces StopAssetImportingV2(ForceSynchronousImport|ForceDomainReload), which
// half-initialises the running Play session. This probe proves the RenderTexture + ReadPixels +
// EncodeToPNG + absolute-path write chain works in this editor BEFORE spending a Play session on it.
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace P41Cam
{
    public static class Probe
    {
        public static string Main(string outPath)
        {
            var sb = new StringBuilder();
            RenderTexture rt = null;
            Texture2D tex = null;
            GameObject go = null;
            try
            {
                var w = 320;
                var h = 180;
                go = new GameObject("P41CamProbeCamera");
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 3.75f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.42f, 0.30f, 1f);
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.transform.rotation = Quaternion.identity;

                rt = RenderTexture.GetTemporary(w, h, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;

                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                var bytes = tex.EncodeToPNG();
                sb.Append("pngBytes=").Append(bytes.Length);
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(outPath, bytes);
                sb.Append(" written=").Append(File.Exists(outPath) ? 1 : 0);
                sb.Append(" path=").Append(outPath);
                var px = tex.GetPixel(w / 2, h / 2);
                sb.Append(" centerColor=").Append(px.r.ToString("0.00")).Append(',')
                  .Append(px.g.ToString("0.00")).Append(',').Append(px.b.ToString("0.00"));
                sb.Append(" camAspect=").Append(cam.aspect.ToString("0.###"));
            }
            catch (Exception ex)
            {
                sb.Append("EX=").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            }
            finally
            {
                RenderTexture.active = null;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            return sb.ToString();
        }
    }
}
