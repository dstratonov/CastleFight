using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [Header("UI")]
    public AudioClip buttonClick;
    public AudioClip buildingPlaced;
    public AudioClip goldReceived;
    public AudioClip error;

    [Header("Combat")]
    public AudioClip swordHit;
    public AudioClip arrowFire;
    public AudioClip magicCast;
    public AudioClip unitDeath;
    public AudioClip buildingDestroyed;

    [Header("Game Events")]
    public AudioClip matchStart;
    public AudioClip victory;
    public AudioClip defeat;
    public AudioClip castleUnderAttack;

    [Header("Music")]
    public AudioClip menuMusic;
    public AudioClip battleMusic;
    public AudioClip victoryMusic;
}
