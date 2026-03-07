using UnityEngine;
using Mirror;

public class PlayerUISetup : NetworkBehaviour
{
    public override void OnStartLocalPlayer()
    {
        var networkPlayer = GetComponent<NetworkPlayer>();
        if (networkPlayer == null) return;

        var uiBuilder = GameUIBuilder.Instance;

        var buildMenu = uiBuilder != null ? uiBuilder.BuildMenu : FindAnyObjectByType<BuildMenuUI>();
        if (buildMenu != null)
        {
            buildMenu.Initialize(networkPlayer);
            Debug.Log("[PlayerUISetup] BuildMenuUI initialized");
        }

        var hud = uiBuilder != null ? uiBuilder.HUD : HUDManager.Instance;
        if (hud != null)
        {
            hud.SetLocalPlayer(networkPlayer);
            Debug.Log("[PlayerUISetup] HUDManager initialized");
        }
    }
}
