using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/UI Theme Data")]
public class UIThemeData : ScriptableObject
{
    [Header("Top Bar")]
    public Sprite topBarBackground;
    public Sprite resourceBar;

    [Header("Info Panel")]
    public Sprite infoPanelBackground;
    public Sprite portraitFrame;
    public Sprite hpFrame;
    public Sprite hpFill;
    public Sprite hpEnemyFrame;

    [Header("Build Panel")]
    public Sprite buildPanelBackground;
    public Sprite buildButtonNormal;
    public Sprite buildButtonHighlight;
    public Sprite buildButtonRed;

    [Header("Icons")]
    public Sprite iconGold;
    public Sprite iconBuild;
    public Sprite iconSword;
    public Sprite iconArmor;

    [Header("Portrait Icons")]
    public Sprite iconHero;
    public Sprite iconUnit;
    public Sprite iconCastle;

    [Header("Misc")]
    public Sprite squareFrame;
    public Sprite popupFrame;
    public Sprite tileBackground;

    [Header("Font")]
    public Font medievalFont;
}
