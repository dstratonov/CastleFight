#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class UIThemeEditor
{
    const string ArtDir = "Assets/MedievalKingdomUI/Artworks/White_gold_Skin";
    const string UIDir = ArtDir + "/UI_elements_W";
    const string BtnDir = ArtDir + "/Buttons_W";
    const string IconDir = "Assets/MedievalKingdomUI/Artworks/UI_icons/Colored";
    const string FontPath = "Assets/MedievalKingdomUI/Fonts/CENTURY.TTF";
    const string ThemePath = "Assets/Resources/UITheme.asset";

    [MenuItem("CastleFight/Create UI Theme Asset")]
    public static void CreateUITheme()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var theme = AssetDatabase.LoadAssetAtPath<UIThemeData>(ThemePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UIThemeData>();
            AssetDatabase.CreateAsset(theme, ThemePath);
        }

        AssignIfLoaded(ref theme.topBarBackground, $"{UIDir}/RTS_Bar.png");
        AssignIfLoaded(ref theme.resourceBar, $"{UIDir}/res_bar.png");

        AssignIfLoaded(ref theme.infoPanelBackground, $"{UIDir}/square_frame.png");
        AssignIfLoaded(ref theme.portraitFrame, $"{UIDir}/icon_frame_round.png");
        AssignIfLoaded(ref theme.hpFrame, $"{UIDir}/HP_frame.png");
        AssignIfLoaded(ref theme.hpFill, $"{UIDir}/HP_line.png");
        AssignIfLoaded(ref theme.hpEnemyFrame, $"{UIDir}/HP_enemy_frame.png");

        AssignIfLoaded(ref theme.buildPanelBackground, $"{UIDir}/Main_bar.png");
        AssignIfLoaded(ref theme.buildButtonNormal, $"{BtnDir}/Button_long.png");
        AssignIfLoaded(ref theme.buildButtonHighlight, $"{BtnDir}/Button_long_Fr.png");
        AssignIfLoaded(ref theme.buildButtonRed, $"{BtnDir}/Button_long_red.png");

        AssignIfLoaded(ref theme.iconGold, $"{IconDir}/06_money.png");
        AssignIfLoaded(ref theme.iconBuild, $"{IconDir}/30_build.png");
        AssignIfLoaded(ref theme.iconSword, $"{IconDir}/04_sword.png");
        AssignIfLoaded(ref theme.iconArmor, $"{IconDir}/16_armor.png");

        AssignIfLoaded(ref theme.iconHero, $"{IconDir}/26_hero.png");
        AssignIfLoaded(ref theme.iconUnit, $"{IconDir}/87_unit.png");
        AssignIfLoaded(ref theme.iconCastle, $"{IconDir}/40_castle.png");

        AssignIfLoaded(ref theme.squareFrame, $"{UIDir}/square_frame.png");
        AssignIfLoaded(ref theme.popupFrame, $"{UIDir}/Popup_window_frame.png");
        AssignIfLoaded(ref theme.tileBackground, $"{UIDir}/Tile_background_3.png");

        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font != null) theme.medievalFont = font;

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        Debug.Log("[UITheme] UI Theme asset created/updated at " + ThemePath);
    }

    static void AssignIfLoaded(ref Sprite field, string path)
    {
        var sprite = LoadSprite(path);
        if (sprite != null) field = sprite;
    }

    static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
            }
            else
            {
                Debug.LogWarning($"[UITheme] Sprite not found at {path}");
            }
        }
        return sprite;
    }
}
#endif
