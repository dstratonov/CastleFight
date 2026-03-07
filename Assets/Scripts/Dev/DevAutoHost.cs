using UnityEngine;
using Mirror;

public class DevAutoHost : MonoBehaviour
{
    [SerializeField] private bool autoStartHost = true;

    private void Start()
    {
        if (!autoStartHost) return;
        if (NetworkManager.singleton == null) return;
        if (NetworkServer.active || NetworkClient.active) return;

        Debug.Log("[DevAutoHost] Auto-starting as Host...");
        NetworkManager.singleton.StartHost();
    }
}
