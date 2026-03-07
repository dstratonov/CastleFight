#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class MaterialUpgradeEditor
{
    [MenuItem("CastleFight/Upgrade Creature Materials to URP")]
    public static void UpgradeCreatureMaterials()
    {
        UpgradeMaterials(new[]
        {
            "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1",
            "Assets/Monsters Ultimate Pack 11"
        }, "Creature");
    }

    [MenuItem("CastleFight/Upgrade TownSmith Materials to URP")]
    public static void UpgradeTownSmithMaterials()
    {
        UpgradeMaterials(new[]
        {
            "Assets/Hivemind/TownSmith"
        }, "TownSmith");
    }

    static void UpgradeMaterials(string[] searchFolders, string label)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[MaterialUpgrade] URP/Lit shader not found. Is URP installed?");
            return;
        }

        int upgraded = 0;
        string[] guids = AssetDatabase.FindAssets("t:Material", searchFolders);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            string shaderName = mat.shader.name;
            if (!NeedsUpgrade(shaderName)) continue;

            bool isHDRP = shaderName.Contains("HDRP") || shaderName.Contains("HDRenderPipeline");

            Color albedo = Color.white;
            Texture mainTex = null;
            Texture normalMap = null;
            float metallic = 0f;
            float smoothness = 0.5f;
            Texture metallicTex = null;
            Texture emissionTex = null;
            Color emissionColor = Color.black;
            bool hasEmission = mat.IsKeywordEnabled("_EMISSION");

            if (isHDRP)
            {
                if (mat.HasProperty("_BaseColor"))
                    albedo = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color"))
                    albedo = mat.GetColor("_Color");

                if (mat.HasProperty("_BaseColorMap"))
                    mainTex = mat.GetTexture("_BaseColorMap");
                else if (mat.HasProperty("_MainTex"))
                    mainTex = mat.GetTexture("_MainTex");

                if (mat.HasProperty("_NormalMap"))
                    normalMap = mat.GetTexture("_NormalMap");
                else if (mat.HasProperty("_BumpMap"))
                    normalMap = mat.GetTexture("_BumpMap");

                if (mat.HasProperty("_Metallic"))
                    metallic = mat.GetFloat("_Metallic");
                if (mat.HasProperty("_Smoothness"))
                    smoothness = mat.GetFloat("_Smoothness");
                if (mat.HasProperty("_MaskMap"))
                    metallicTex = mat.GetTexture("_MaskMap");
                if (mat.HasProperty("_EmissiveColorMap"))
                    emissionTex = mat.GetTexture("_EmissiveColorMap");
                if (mat.HasProperty("_EmissiveColor"))
                    emissionColor = mat.GetColor("_EmissiveColor");
                hasEmission = emissionTex != null || emissionColor != Color.black;
            }
            else
            {
                if (mat.HasProperty("_Color"))
                    albedo = mat.GetColor("_Color");
                if (mat.HasProperty("_MainTex"))
                    mainTex = mat.GetTexture("_MainTex");
                if (mat.HasProperty("_BumpMap"))
                    normalMap = mat.GetTexture("_BumpMap");
                if (mat.HasProperty("_Metallic"))
                    metallic = mat.GetFloat("_Metallic");
                if (mat.HasProperty("_Glossiness"))
                    smoothness = mat.GetFloat("_Glossiness");
                if (mat.HasProperty("_MetallicGlossMap"))
                    metallicTex = mat.GetTexture("_MetallicGlossMap");
                if (mat.HasProperty("_EmissionMap"))
                    emissionTex = mat.GetTexture("_EmissionMap");
                if (mat.HasProperty("_EmissionColor"))
                    emissionColor = mat.GetColor("_EmissionColor");
            }

            mat.shader = urpLit;

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", albedo);
            if (mat.HasProperty("_BaseMap") && mainTex != null)
                mat.SetTexture("_BaseMap", mainTex);
            if (mat.HasProperty("_BumpMap") && normalMap != null)
                mat.SetTexture("_BumpMap", normalMap);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_MetallicGlossMap") && metallicTex != null)
                mat.SetTexture("_MetallicGlossMap", metallicTex);
            if (hasEmission)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionMap") && emissionTex != null)
                    mat.SetTexture("_EmissionMap", emissionTex);
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", emissionColor);
            }

            EditorUtility.SetDirty(mat);
            upgraded++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[MaterialUpgrade] {label}: Upgraded {upgraded} materials to URP/Lit (from {guids.Length} total).");
    }

    static bool NeedsUpgrade(string shaderName)
    {
        if (shaderName.StartsWith("Universal Render Pipeline/")) return false;
        if (shaderName == "Sprites/Default") return false;
        if (shaderName == "UI/Default") return false;
        if (shaderName == "Particles/Standard Unlit") return false;
        return true;
    }
}
#endif
