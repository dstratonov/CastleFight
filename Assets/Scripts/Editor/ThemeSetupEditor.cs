#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
public static class ThemeSetupEditor
{
    private const string SyntyHUD = "Assets/Synty/InterfaceDarkFantasyHUD/Sprites/HUD/";
    private const string SyntyStatus = "Assets/Synty/InterfaceDarkFantasyHUD/Sprites/Icons_Status/";
    private const string SyntyWeapons = "Assets/Synty/InterfaceDarkFantasyHUD/Sprites/Icons_Weapons/";
    private const string SyntyResources = "Assets/Synty/InterfaceDarkFantasyHUD/Sprites/Icons_Resources/";

    [MenuItem("CastleFight/Setup Dark Fantasy Theme")]
    public static void SetupTheme()
    {
        var theme = Resources.Load<UIThemeData>("UITheme");
        if (theme == null)
        {
            Debug.LogError("[ThemeSetup] UITheme asset not found in Resources!");
            return;
        }

        Undo.RecordObject(theme, "Setup Dark Fantasy Theme");

        theme.topBarBackground = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Gradient_Horizontal_01.png");
        theme.resourceBar = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Bar_Parchment_01.png");

        theme.infoPanelBackground = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Box_Medium_Parchment_01.png");
        theme.infoPanelFrame = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Frame_Box_01.png");
        theme.portraitFrame = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Ring_Medium_01_Clean.png");
        theme.portraitUnderlay = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Ring_Medium_01_Underlay.png");
        theme.portraitBackground = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Gradient_Circle_01.png");

        theme.hpFrame = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Bar_01_Clean.png");
        theme.hpFill = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Box_White_01.png");
        theme.hpEnemyFrame = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Bar_01_Clean.png");

        theme.buildPanelBackground = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Box_Medium_Parchment_02.png");
        theme.buildButtonNormal = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Box_Small_Parchment_01.png");
        theme.buildButtonHighlight = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Box_Small_Parchment_02.png");
        theme.buildButtonRed = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Box_Small_Parchment_03.png");

        theme.iconGold = Load(SyntyResources + "ICON_SM_Item_Coins_01_Dungeon.png");
        theme.iconBuild = Load(SyntyWeapons + "ICON_SM_Wep_Hammer_01_DarkFantasy_Clean.png");
        theme.iconSword = Load(SyntyStatus + "ICON_DarkFantasy_Status_Attack_01_Clean.png");
        theme.iconArmor = Load(SyntyStatus + "ICON_DarkFantasy_Status_Armor_01_Clean.png");
        theme.iconSpeed = Load(SyntyStatus + "ICON_DarkFantasy_Status_SpeedUp_01_Clean.png");

        theme.iconHealth = Load(SyntyStatus + "ICON_DarkFantasy_Status_Health_01_Clean.png");
        theme.iconAttack = Load(SyntyStatus + "ICON_DarkFantasy_Status_Attack_02_Clean.png");
        theme.iconDefense = Load(SyntyStatus + "ICON_DarkFantasy_Status_Defense_01_Clean.png");
        theme.iconFortified = Load(SyntyStatus + "ICON_DarkFantasy_Status_Fortified_01_Clean.png");

        theme.iconHero = Load(SyntyStatus + "ICON_DarkFantasy_Status_FortifiedHealth_01_Clean.png");
        theme.iconUnit = Load(SyntyWeapons + "ICON_SM_Wep_Sword_01_DarkFantasy_Clean.png");
        theme.iconCastle = Load(SyntyStatus + "ICON_DarkFantasy_Status_FortifiedDefense_01_Clean.png");

        theme.squareFrame = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Frame_Box_02.png");
        theme.popupFrame = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Frame_Box_03.png");
        theme.tooltipBackground = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Tooltip_01.png");
        theme.gradientHorizontal = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Gradient_Horizontal_01.png");
        theme.gradientVertical = Load(SyntyHUD + "SPR_HUD_DarkFantasy_Gradient_Vertical_01.png");

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        Debug.Log("[ThemeSetup] Dark Fantasy theme applied successfully!");
    }

    private static Sprite Load(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogWarning($"[ThemeSetup] Sprite not found: {path}");
        return sprite;
    }

}
#endif
