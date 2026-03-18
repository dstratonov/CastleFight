#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// One-time upgrade tool for adding SpeedMultiplier parameter to the base controller.
/// Can also be run manually via CastleFight menu.
/// </summary>
public static class UpgradeAnimatorController
{
    [MenuItem("CastleFight/Upgrade Base Controller Speed Param")]
    public static void Upgrade()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animation/Base_Unit.controller");
        if (controller == null) return;

        bool hasParam = false;
        foreach (var p in controller.parameters)
        {
            if (p.name == "SpeedMultiplier") { hasParam = true; break; }
        }

        bool changed = false;

        if (!hasParam)
        {
            controller.AddParameter("SpeedMultiplier", AnimatorControllerParameterType.Float);
            changed = true;
        }

        var parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == "SpeedMultiplier" && parameters[i].defaultFloat < 0.99f)
            {
                parameters[i].defaultFloat = 1f;
                controller.parameters = parameters;
                changed = true;
                break;
            }
        }

        var rootSM = controller.layers[0].stateMachine;
        foreach (var childState in rootSM.states)
        {
            var state = childState.state;
            if (!state.speedParameterActive || state.speedParameter != "SpeedMultiplier")
            {
                state.speedParameter = "SpeedMultiplier";
                state.speedParameterActive = true;
                changed = true;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[UpgradeController] Base_Unit.controller upgraded with SpeedMultiplier (default=1) on all states");
        }
    }
}
#endif
