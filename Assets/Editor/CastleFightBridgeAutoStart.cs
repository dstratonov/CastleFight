// __auto_reload_marker__ 1775802526945
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace CastleFight.EditorTools
{
    /// <summary>
    /// Ensures the MCP for Unity bridge (StdioBridgeHost) is always running.
    /// Runs on every domain reload so that restarting the editor, recompiling,
    /// or manually ending the session all end up with a live bridge shortly after.
    /// Claude connects to the bridge over TCP and needs it up without user clicks.
    /// </summary>
    [InitializeOnLoad]
    public static class CastleFightBridgeAutoStart
    {
        static CastleFightBridgeAutoStart()
        {
            // Defer so that this runs after the MCP package's own InitializeOnLoad
            // and avoids racing with domain reload initialization.
            EditorApplication.delayCall += EnsureBridgeRunning;
        }

        private static void EnsureBridgeRunning()
        {
            try
            {
                // StdioBridgeHost lives in MCPForUnity.Editor.Services.Transport.Transports.
                // Reference via reflection so this script compiles even if the package
                // is (temporarily) missing — we don't want a hard dependency that breaks
                // the project when someone updates or removes the package.
                var type = Type.GetType(
                    "MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost, MCPForUnity.Editor",
                    throwOnError: false);

                if (type == null)
                {
                    // Fall back to scanning loaded assemblies for the type.
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType("MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost");
                        if (type != null) break;
                    }
                }

                if (type == null)
                {
                    Debug.LogWarning("[CastleFightBridgeAutoStart] StdioBridgeHost type not found. Is the MCP for Unity package installed?");
                    return;
                }

                var isRunningProp = type.GetProperty("IsRunning");
                bool alreadyRunning = isRunningProp != null && (bool)isRunningProp.GetValue(null);
                if (alreadyRunning)
                {
                    return;
                }

                var startMethod = type.GetMethod("StartAutoConnect");
                if (startMethod == null)
                {
                    Debug.LogWarning("[CastleFightBridgeAutoStart] StartAutoConnect method not found on StdioBridgeHost.");
                    return;
                }

                startMethod.Invoke(null, null);
                Debug.Log("[CastleFightBridgeAutoStart] MCP bridge auto-started.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CastleFightBridgeAutoStart] Failed to auto-start bridge: {e.Message}");
            }
        }
    }
}
#endif
