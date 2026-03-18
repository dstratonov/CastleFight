#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;

public static class AssetStandardizerEditor
{
    const string BaseControllerPath = "Assets/Animation/Base_Unit.controller";
    const string PlaceholderDir = "Assets/Animation/Placeholders";
    const string OverrideDir = "Assets/Animation/Overrides";
    const string UnitPrefabDir = "Assets/Prefabs/Units";
    const string UnitDataDir = "Assets/Data/Units";

    static readonly string[] PlaceholderNames = { "idle_placeholder", "walk_placeholder", "attack_placeholder", "death_placeholder", "hit_placeholder" };
    static readonly string[] StateNames = { "Idle", "Walk", "Attack", "Death", "Hit" };

    struct CreatureInfo
    {
        public string unitId;
        public string modelPath;
        public string creatureFolder;
    }

    // ========================================================================
    // MAIN ENTRY POINT
    // ========================================================================

    [MenuItem("CastleFight/Standardize All Assets")]
    public static void StandardizeAll()
    {
        Debug.Log("[Standardizer] === Starting full asset standardization ===");

        EnsureFolders();

        var placeholderClips = CreatePlaceholderClips();
        var baseController = CreateBaseController(placeholderClips);
        var creatures = GatherCreatures();

        int animCount = StandardizeAnimations(creatures, baseController, placeholderClips);
        int colliderCount = StandardizeColliders(creatures);
        int matCount = StandardizeMaterials();
        int iconCount = StandardizeIcons(creatures);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Standardizer] === Complete === Animations: {animCount}, Colliders: {colliderCount}, Materials: {matCount}, Icons: {iconCount}");
    }

    [MenuItem("CastleFight/Standardize Animations Only")]
    public static void StandardizeAnimationsOnly()
    {
        EnsureFolders();
        var placeholderClips = CreatePlaceholderClips();
        var baseController = CreateBaseController(placeholderClips);
        var creatures = GatherCreatures();
        int count = StandardizeAnimations(creatures, baseController, placeholderClips);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Standardizer] Animations standardized: {count} overrides created");
    }

    [MenuItem("CastleFight/Fix Animation Loop Settings")]
    public static void FixAnimationLoopSettings()
    {
        var creatures = GatherCreatures();
        var placeholders = CreatePlaceholderClips();
        var baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath);

        int fixCount = 0;
        var pathsToReimport = new HashSet<string>();

        foreach (var creature in creatures)
        {
            var clips = FindAnimationClips(creature.creatureFolder);
            if (clips.Count == 0) continue;

            var bestIdle = FindBestClip(clips, IdleKeywords);
            var bestWalk = FindBestClip(clips, WalkKeywords);
            if (bestWalk == null) bestWalk = bestIdle;
            if (bestIdle == null) bestIdle = bestWalk;

            fixCount += CollectLoopFix(bestIdle, true, pathsToReimport);
            fixCount += CollectLoopFix(bestWalk, true, pathsToReimport);
        }

        Debug.Log($"[Standardizer] Queued loop fixes for {fixCount} clips across {pathsToReimport.Count} files. Reimporting...");

        int progress = 0;
        foreach (var path in pathsToReimport)
        {
            progress++;
            EditorUtility.DisplayProgressBar("Fixing Animation Loops", $"Reimporting {progress}/{pathsToReimport.Count}: {System.IO.Path.GetFileName(path)}", (float)progress / pathsToReimport.Count);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        EditorUtility.ClearProgressBar();

        Debug.Log($"[Standardizer] Done. Fixed loop settings on {fixCount} clips across {pathsToReimport.Count} files");

        if (pathsToReimport.Count > 0)
        {
            Debug.Log("[Standardizer] Regenerating override controllers to match updated clip IDs...");
            StandardizeAnimationsOnly();
        }
    }

    static int CollectLoopFix(AnimationClip clip, bool shouldLoop, HashSet<string> pathsToReimport)
    {
        if (clip == null) return 0;

        string clipPath = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(clipPath) || pathsToReimport.Contains(clipPath)) return 0;

        var importer = AssetImporter.GetAtPath(clipPath) as ModelImporter;
        if (importer == null) return 0;

        var clips = importer.clipAnimations;
        ModelImporterClipAnimation[] toApply;

        if (clips == null || clips.Length == 0)
        {
            toApply = importer.defaultClipAnimations;
            if (toApply == null || toApply.Length == 0) return 0;
        }
        else
        {
            toApply = clips;
        }

        bool needsUpdate = false;
        foreach (var c in toApply)
        {
            if (c.loopTime != shouldLoop)
            {
                c.loopTime = shouldLoop;
                needsUpdate = true;
            }
        }

        if (needsUpdate)
        {
            importer.clipAnimations = toApply;
            pathsToReimport.Add(clipPath);
            Debug.Log($"[Standardizer] Queued loop={shouldLoop} for {clip.name} in {clipPath}");
            return 1;
        }

        return 0;
    }

    [MenuItem("CastleFight/Standardize Colliders Only")]
    public static void StandardizeCollidersOnly()
    {
        var creatures = GatherCreatures();
        int count = StandardizeColliders(creatures);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Standardizer] Colliders standardized: {count}");
    }

    [MenuItem("CastleFight/Standardize Materials Only")]
    public static void StandardizeMaterialsOnly()
    {
        int count = StandardizeMaterials();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Standardizer] Materials checked/upgraded: {count}");
    }

    // ========================================================================
    // FOLDER SETUP
    // ========================================================================

    static void EnsureFolders()
    {
        EnsureFolder(PlaceholderDir);
        EnsureFolder(OverrideDir);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    // ========================================================================
    // PLACEHOLDER CLIPS + BASE CONTROLLER
    // ========================================================================

    static AnimationClip[] CreatePlaceholderClips()
    {
        var clips = new AnimationClip[PlaceholderNames.Length];
        for (int i = 0; i < PlaceholderNames.Length; i++)
        {
            string path = $"{PlaceholderDir}/{PlaceholderNames[i]}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                clips[i] = existing;
                continue;
            }

            var clip = new AnimationClip();
            clip.name = PlaceholderNames[i];
            var curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.01f, 0));
            clip.SetCurve("", typeof(Transform), "localPosition.x", curve);
            AssetDatabase.CreateAsset(clip, path);
            clips[i] = clip;
        }
        return clips;
    }

    static AnimatorController CreateBaseController(AnimationClip[] placeholders)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath);
        if (existing != null)
        {
            EnsureSpeedParameter(existing);
            Debug.Log("[Standardizer] Base controller already exists, reusing");
            return existing;
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(BaseControllerPath);
        var rootSM = controller.layers[0].stateMachine;

        controller.AddParameter("SpeedMultiplier", AnimatorControllerParameterType.Float);
        var param = controller.parameters[controller.parameters.Length - 1];
        param.defaultFloat = 1f;

        for (int i = 0; i < StateNames.Length; i++)
        {
            var state = rootSM.AddState(StateNames[i]);
            state.motion = placeholders[i];
            state.speedParameter = "SpeedMultiplier";
            state.speedParameterActive = true;
        }

        rootSM.defaultState = rootSM.states[0].state;
        EditorUtility.SetDirty(controller);
        Debug.Log("[Standardizer] Created Base_Unit.controller with 5 states + SpeedMultiplier parameter");
        return controller;
    }

    /// <summary>
    /// Ensures existing base controller has SpeedMultiplier parameter and states use it.
    /// Upgrades older controllers created without per-state speed control.
    /// </summary>
    static void EnsureSpeedParameter(AnimatorController controller)
    {
        bool hasParam = false;
        foreach (var p in controller.parameters)
        {
            if (p.name == "SpeedMultiplier") { hasParam = true; break; }
        }

        if (!hasParam)
        {
            controller.AddParameter("SpeedMultiplier", AnimatorControllerParameterType.Float);
            Debug.Log("[Standardizer] Added SpeedMultiplier parameter to existing controller");
        }

        var rootSM = controller.layers[0].stateMachine;
        foreach (var childState in rootSM.states)
        {
            var state = childState.state;
            if (!state.speedParameterActive)
            {
                state.speedParameter = "SpeedMultiplier";
                state.speedParameterActive = true;
            }
        }
        EditorUtility.SetDirty(controller);
    }

    // ========================================================================
    // CREATURE DISCOVERY
    // ========================================================================

    static List<CreatureInfo> GatherCreatures()
    {
        var list = new List<CreatureInfo>();
        string[] guids = AssetDatabase.FindAssets("t:UnitData", new[] { UnitDataDir });

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var unitData = AssetDatabase.LoadAssetAtPath<UnitData>(assetPath);
            if (unitData == null || unitData.prefab == null) continue;

            string prefabPath = AssetDatabase.GetAssetPath(unitData.prefab);
            string modelPath = FindModelPrefabPath(prefabPath);
            if (string.IsNullOrEmpty(modelPath)) continue;

            string creatureFolder = DerivecreatureFolder(modelPath);
            if (string.IsNullOrEmpty(creatureFolder)) continue;

            list.Add(new CreatureInfo
            {
                unitId = unitData.unitName,
                modelPath = modelPath,
                creatureFolder = creatureFolder
            });
        }

        Debug.Log($"[Standardizer] Found {list.Count} creatures");
        return list;
    }

    static string FindModelPrefabPath(string unitPrefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(unitPrefabPath);
        if (prefab == null) return null;

        var model = prefab.transform.Find("Model");
        if (model == null) return null;

        var animator = model.GetComponentInChildren<Animator>();
        if (animator == null) return null;

        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model.gameObject);
        if (!string.IsNullOrEmpty(path) && path != unitPrefabPath)
            return path;

        var go = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
        if (go != null)
        {
            path = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(path))
                return path;
        }

        return null;
    }

    static string DerivecreatureFolder(string modelPath)
    {
        int prefabsIdx = modelPath.IndexOf("/Prefabs/");
        if (prefabsIdx < 0) prefabsIdx = modelPath.IndexOf("/Prefabs\\");
        if (prefabsIdx < 0) return null;
        return modelPath.Substring(0, prefabsIdx);
    }

    // ========================================================================
    // PHASE 2: ANIMATION STANDARDIZATION
    // ========================================================================

    static readonly string[][] IdleKeywords = {
        new[] { "idle", "breathe", "lookaround", "rest", "stand" }
    };
    static readonly string[][] WalkKeywords = {
        new[] { "walk", "move forward", "crawl", "gallop", "fly forward", "fly", "run forward", "run" }
    };
    static readonly string[][] AttackKeywords = {
        new[] { "attack", "bite", "claw", "slash", "combo", "spit", "sting", "stomp", "smash", "throw", "cast", "projectile", "head attack", "pounce" }
    };
    static readonly string[][] DeathKeywords = {
        new[] { "die", "death" }
    };
    static readonly string[][] HitKeywords = {
        new[] { "hit", "damage", "take damage" }
    };

    static int StandardizeAnimations(List<CreatureInfo> creatures, AnimatorController baseController, AnimationClip[] placeholders)
    {
        int count = 0;
        foreach (var creature in creatures)
        {
            var clips = FindAnimationClips(creature.creatureFolder);
            if (clips.Count == 0)
            {
                Debug.LogWarning($"[Standardizer] No animation clips found for {creature.unitId} in {creature.creatureFolder}");
                continue;
            }

            var bestIdle = FindBestClip(clips, IdleKeywords);
            var bestWalk = FindBestClip(clips, WalkKeywords);
            var bestAttack = FindBestClip(clips, AttackKeywords);
            var bestDeath = FindBestClip(clips, DeathKeywords);
            var bestHit = FindBestClip(clips, HitKeywords);

            if (bestWalk == null) bestWalk = bestIdle;
            if (bestIdle == null) bestIdle = bestWalk;

            var selected = new AnimationClip[] { bestIdle, bestWalk, bestAttack, bestDeath, bestHit };
            string overridePath = $"{OverrideDir}/{creature.unitId}_override.overrideController";

            var overrideCtrl = CreateOverrideController(baseController, placeholders, selected, overridePath);
            if (overrideCtrl != null)
            {
                AssignControllerToModel(creature.modelPath, overrideCtrl);
                AssignControllerToUnitPrefab(creature.unitId, overrideCtrl);
                count++;

                string mappingLog = $"[Standardizer] {creature.unitId}:";
                for (int i = 0; i < StateNames.Length; i++)
                    mappingLog += $"\n  {StateNames[i]} -> {(selected[i] != null ? selected[i].name : "MISSING")}";
                Debug.Log(mappingLog);
            }
        }
        return count;
    }

    static List<AnimationClip> FindAnimationClips(string folder)
    {
        var result = new List<AnimationClip>();
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    result.Add(clip);
            }
        }
        return result;
    }

    static AnimationClip FindBestClip(List<AnimationClip> clips, string[][] keywordGroups)
    {
        var candidates = new List<(AnimationClip clip, int score)>();

        foreach (var clip in clips)
        {
            string name = clip.name.ToLowerInvariant();

            if (name.EndsWith("_rm")) continue;
            if (name.Contains("w root")) continue;

            bool matches = false;
            foreach (var group in keywordGroups)
            {
                foreach (var keyword in group)
                {
                    if (name.Contains(keyword))
                    {
                        matches = true;
                        break;
                    }
                }
                if (matches) break;
            }

            if (!matches) continue;

            int score = 100;
            if (name.Contains("in place")) score += 20;
            if (name.Contains("walk")) score += 15;
            else if (name.Contains("fly")) score += 5;
            else if (name.Contains("run")) score -= 5;
            if (name.Contains("swordshield") || name.Contains("daggers") || name.Contains("slingshot") ||
                name.Contains("spear") || name.Contains("weapon") || name.Contains("unarmed"))
                score -= 30;
            if (name.Contains("forward")) score -= 5;
            if (name.Contains("combo")) score -= 10;
            score -= name.Length;

            candidates.Add((clip, score));
        }

        if (candidates.Count == 0) return null;
        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        return candidates[0].clip;
    }

    static AnimatorOverrideController CreateOverrideController(
        AnimatorController baseController, AnimationClip[] placeholders, AnimationClip[] selected, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
        if (existing != null)
            AssetDatabase.DeleteAsset(path);

        var overrideCtrl = new AnimatorOverrideController(baseController);
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideCtrl.GetOverrides(overrides);

        for (int i = 0; i < overrides.Count; i++)
        {
            var placeholder = overrides[i].Key;
            AnimationClip replacement = null;

            for (int j = 0; j < placeholders.Length; j++)
            {
                if (placeholder == placeholders[j] && j < selected.Length)
                {
                    replacement = selected[j];
                    break;
                }
            }

            if (replacement != null)
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(placeholder, replacement);
        }

        overrideCtrl.ApplyOverrides(overrides);
        AssetDatabase.CreateAsset(overrideCtrl, path);
        return overrideCtrl;
    }

    static void AssignControllerToModel(string modelPrefabPath, AnimatorOverrideController overrideCtrl)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPrefabPath);
        if (prefab == null) return;

        var animator = prefab.GetComponent<Animator>();
        if (animator == null)
            animator = prefab.GetComponentInChildren<Animator>();
        if (animator == null) return;

        using (var editScope = new PrefabUtility.EditPrefabContentsScope(modelPrefabPath))
        {
            var root = editScope.prefabContentsRoot;
            var anim = root.GetComponent<Animator>();
            if (anim == null) anim = root.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = overrideCtrl;
                anim.applyRootMotion = false;
            }
        }
    }

    static void AssignControllerToUnitPrefab(string unitId, AnimatorOverrideController overrideCtrl)
    {
        string unitPrefabPath = $"{UnitPrefabDir}/Unit_{unitId}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(unitPrefabPath);
        if (prefab == null) return;

        using (var editScope = new PrefabUtility.EditPrefabContentsScope(unitPrefabPath))
        {
            var root = editScope.prefabContentsRoot;
            var anim = root.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = overrideCtrl;
                anim.applyRootMotion = false;
            }
        }
    }

    // ========================================================================
    // PHASE 3: COLLIDER & RADIUS STANDARDIZATION
    // ========================================================================

    static int StandardizeColliders(List<CreatureInfo> creatures)
    {
        int count = 0;
        foreach (var creature in creatures)
        {
            string unitPrefabPath = $"{UnitPrefabDir}/Unit_{creature.unitId}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(unitPrefabPath);
            if (prefab == null) continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                if (!BoundsHelper.TryGetCombinedBounds(instance, out Bounds bounds))
                {
                    Object.DestroyImmediate(instance);
                    continue;
                }

                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                float height = bounds.size.y;
                radius = Mathf.Max(radius, 0.3f);
                height = Mathf.Max(height, 0.5f);

                var capsule = instance.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    capsule.radius = radius;
                    capsule.height = height;
                    capsule.center = new Vector3(0, height / 2f, 0);
                }

                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);

                string unitDataPath = $"{UnitDataDir}/{creature.unitId}.asset";
                var unitData = AssetDatabase.LoadAssetAtPath<UnitData>(unitDataPath);
                if (unitData != null)
                {
                    unitData.unitRadius = radius;
                    EditorUtility.SetDirty(unitData);
                }

                Debug.Log($"[Standardizer] Collider {creature.unitId}: r={radius:F2} h={height:F2}");
                count++;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
        return count;
    }

    // ========================================================================
    // PHASE 4: MATERIAL VERIFICATION
    // ========================================================================

    static int StandardizeMaterials()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[Standardizer] URP/Lit shader not found");
            return 0;
        }

        string[] folders = {
            "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1",
            "Assets/Monsters Ultimate Pack 11"
        };

        int upgraded = 0;
        int alreadyCorrect = 0;

        foreach (var folder in folders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                if (mat.shader.name.StartsWith("Universal Render Pipeline/") ||
                    mat.shader.name == "Sprites/Default" ||
                    mat.shader.name == "UI/Default")
                {
                    alreadyCorrect++;
                    continue;
                }

                UpgradeMaterialToURP(mat, urpLit);
                EditorUtility.SetDirty(mat);
                upgraded++;
            }
        }

        Debug.Log($"[Standardizer] Materials: {alreadyCorrect} already URP, {upgraded} upgraded");
        return upgraded;
    }

    static void UpgradeMaterialToURP(Material mat, Shader urpLit)
    {
        Color albedo = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") :
                        mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        Texture mainTex = mat.HasProperty("_BaseColorMap") ? mat.GetTexture("_BaseColorMap") :
                          mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        Texture normalMap = mat.HasProperty("_NormalMap") ? mat.GetTexture("_NormalMap") :
                            mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
        float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
        float smoothness = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") :
                           mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;

        mat.shader = urpLit;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", albedo);
        if (mat.HasProperty("_BaseMap") && mainTex != null) mat.SetTexture("_BaseMap", mainTex);
        if (mat.HasProperty("_BumpMap") && normalMap != null) mat.SetTexture("_BumpMap", normalMap);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
    }

    // ========================================================================
    // PHASE 5: ICON STANDARDIZATION
    // ========================================================================

    static int StandardizeIcons(List<CreatureInfo> creatures)
    {
        int count = 0;
        foreach (var creature in creatures)
        {
            string unitDataPath = $"{UnitDataDir}/{creature.unitId}.asset";
            var unitData = AssetDatabase.LoadAssetAtPath<UnitData>(unitDataPath);
            if (unitData == null) continue;
            if (unitData.icon != null) continue;

            Sprite icon = FindCreatureIcon(creature.creatureFolder);
            if (icon != null)
            {
                unitData.icon = icon;
                EditorUtility.SetDirty(unitData);
                Debug.Log($"[Standardizer] Icon set for {creature.unitId}: {AssetDatabase.GetAssetPath(icon)}");
                count++;
            }
            else
            {
                Debug.LogWarning($"[Standardizer] No icon texture found for {creature.unitId} in {creature.creatureFolder}");
            }
        }
        return count;
    }

    static Sprite FindCreatureIcon(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        string creatureName = Path.GetFileName(folder).ToLowerInvariant().Replace(" ", "");

        string bestPath = null;
        int bestScore = -1;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string filename = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            if (!filename.Contains("diffuse") && !filename.Contains("color") &&
                !filename.Contains("albedo") && !filename.Contains("basecolor"))
                continue;
            if (filename.Contains("normal") || filename.Contains("mask") ||
                filename.Contains("specular") || filename.Contains("emission") ||
                filename.Contains("ao") || filename.Contains("height"))
                continue;

            bool isWeaponOrAccessory = filename.Contains("axe") || filename.Contains("sword") ||
                filename.Contains("shield") || filename.Contains("weapon") ||
                filename.Contains("dagger") || filename.Contains("spear") ||
                filename.Contains("staff") || filename.Contains("bow") ||
                filename.Contains("helmet") || filename.Contains("armor") ||
                filename.Contains("1handed") || filename.Contains("2handed");
            if (isWeaponOrAccessory) continue;

            int score = 0;
            if (filename.Contains("body")) score += 30;
            if (filename.Contains(creatureName)) score += 20;
            if (filename.Contains("diffuse")) score += 10;
            if (filename.Contains("basecolor")) score += 10;
            if (filename.Contains("albedo")) score += 10;

            if (score > bestScore)
            {
                bestScore = score;
                bestPath = path;
            }
        }

        if (bestPath == null) return null;

        var importer = AssetImporter.GetAtPath(bestPath) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(bestPath);
    }
}
#endif
