// Auto-converts Built-in Standard materials under Vefects/Vexa to URP Lit.
// Runs once on editor load (marker file), or via menu: Tools/Vexa/Convert Standard Mats to URP.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ConvertVexaMaterialsToURP
{
    const string VexaRoot = "Assets/Vefects/Stylized Female Character - Vexa";
    const string MarkerPath = "Assets/Vefects/Stylized Female Character - Vexa/_urp_standard_converted.marker";
    const string LogPath = "Assets/Vefects/Stylized Female Character - Vexa/_urp_convert_log.txt";

    [InitializeOnLoadMethod]
    static void AutoRun()
    {
        if (File.Exists(MarkerPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath)) return;
            ConvertAll(false);
        };
    }

    [MenuItem("Tools/Vexa/Convert Standard Mats to URP")]
    static void MenuConvert()
    {
        ConvertAll(true);
    }

    static void ConvertAll(bool force)
    {
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[VexaURP] URP Lit shader not found. Is URP installed?");
            File.WriteAllText(LogPath, "ERROR: URP Lit not found\n");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { VexaRoot });
        int converted = 0, skipped = 0, failed = 0;
        var log = new System.Text.StringBuilder();
        log.AppendLine("Vexa Standard->URP Lit conversion " + System.DateTime.Now.ToString("o"));

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { skipped++; continue; }

            string shaderName = mat.shader != null ? mat.shader.name : "";
            // Built-in Standard / Standard (Specular) / Autodesk Interactive often pink under URP
            bool isBuiltinStandard =
                shaderName == "Standard" ||
                shaderName == "Standard (Specular setup)" ||
                shaderName == "Autodesk Interactive" ||
                (mat.shader != null && mat.shader.name.Contains("Legacy Shaders"));

            // Also catch magenta/error shaders that still have Standard props
            bool looksBroken = mat.shader == null || shaderName == "Hidden/InternalErrorShader";

            if (!isBuiltinStandard && !looksBroken)
            {
                skipped++;
                continue;
            }

            try
            {
                // Capture Built-in props before swap
                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                Texture occ = mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;
                Texture emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
                Texture metallicMap = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;

                Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                Color emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                float gloss = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
                float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;
                float occStr = mat.HasProperty("_OcclusionStrength") ? mat.GetFloat("_OcclusionStrength") : 1f;
                float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
                float mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f; // 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent

                Undo.RecordObject(mat, "Convert Vexa material to URP Lit");
                mat.shader = urpLit;

                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", bump);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", bumpScale);
                if (mat.HasProperty("_OcclusionMap")) mat.SetTexture("_OcclusionMap", occ);
                if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", occStr);
                if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", metallicMap);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", gloss);
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emissionMap);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);

                // Surface type
                if (mode >= 0.5f && mode < 1.5f)
                {
                    // Alpha clip / cutout
                    if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                    if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = 2450;
                }
                else if (mode >= 2.5f)
                {
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                }
                else
                {
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
                    if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "Opaque");
                    mat.renderQueue = -1;
                }

                EditorUtility.SetDirty(mat);
                converted++;
                log.AppendLine("OK " + path + " (" + shaderName + " -> URP/Lit)");
            }
            catch (System.Exception ex)
            {
                failed++;
                log.AppendLine("FAIL " + path + " " + ex.Message);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        File.WriteAllText(MarkerPath, "converted=" + converted + " skipped=" + skipped + " failed=" + failed + "\n" + System.DateTime.Now.ToString("o") + "\n");
        File.WriteAllText(LogPath, log.ToString());
        AssetDatabase.Refresh();

        Debug.Log("[VexaURP] Converted " + converted + " Standard materials to URP Lit. Skipped " + skipped + ", failed " + failed + ". Log: " + LogPath);
        if (force)
            EditorUtility.DisplayDialog("Vexa URP", "Converted " + converted + " materials.\nSkipped " + skipped + "\nFailed " + failed, "OK");
    }
}
