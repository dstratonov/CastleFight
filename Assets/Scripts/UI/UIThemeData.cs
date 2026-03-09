using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/UI Theme Data")]
public class UIThemeData : ScriptableObject
{
    [Header("Top Bar")]
    public Sprite topBarBackground;
    public Sprite resourceBar;

    [Header("Info Panel")]
    public Sprite infoPanelBackground;
    public Sprite infoPanelFrame;
    public Sprite portraitFrame;
    public Sprite portraitBackground;
    public Sprite portraitUnderlay;
    public Sprite hpFrame;
    public Sprite hpFill;
    public Sprite hpEnemyFrame;

    [Header("Build Panel")]
    public Sprite buildPanelBackground;
    public Sprite buildButtonNormal;
    public Sprite buildButtonHighlight;
    public Sprite buildButtonRed;

    [Header("Icons - HUD")]
    public Sprite iconGold;
    public Sprite iconBuild;
    public Sprite iconSword;
    public Sprite iconArmor;
    public Sprite iconSpeed;

    [Header("Portrait Icons")]
    public Sprite iconHero;
    public Sprite iconUnit;
    public Sprite iconCastle;

    [Header("Status Icons")]
    public Sprite iconHealth;
    public Sprite iconAttack;
    public Sprite iconDefense;
    public Sprite iconFortified;

    [Header("Misc")]
    public Sprite squareFrame;
    public Sprite popupFrame;
    public Sprite tileBackground;
    public Sprite tooltipBackground;
    public Sprite gradientHorizontal;
    public Sprite gradientVertical;

    [Header("Font")]
    public Font medievalFont;
}
