using UnityEngine;

[System.Serializable]
public class Team
{
    public int teamId;
    public string teamName;
    public Color teamColor = Color.white;
    public Transform castleTransform;
    public GameObject castleObject;
    public Transform[] spawnPoints;
}
