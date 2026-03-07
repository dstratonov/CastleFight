#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Mirror;
using System.IO;

public static class RaceSetupEditor
{
    struct UnitDef
    {
        public string id, displayName, description, modelPath;
        public int hp, bounty;
        public float damage, attackSpeed, attackRange, moveSpeed;
        public AttackType attackType;
        public ArmorType armorType;
        public bool isRanged;
    }

    struct BuildingDef
    {
        public string id, name, description, modelPath;
        public int cost, hp, tier;
        public float spawnInterval, modelScale;
        public UnitDef unit;
    }

    struct RaceDef
    {
        public string id, name, description;
        public Color color;
        public BuildingDef[] buildings;
    }

    const string UnitPrefabDir = "Assets/Prefabs/Units";
    const string BuildingPrefabDir = "Assets/Prefabs/Buildings";
    const string UnitDataDir = "Assets/Data/Units";
    const string BuildingDataDir = "Assets/Data/Buildings";
    const string RaceDataDir = "Assets/Data/Races";
    const string UnitBasePath = UnitPrefabDir + "/Unit_Base.prefab";
    const string BuildingBasePath = BuildingPrefabDir + "/Bld_Base.prefab";

    const string TownSmithBase = "Assets/Hivemind/TownSmith/HDRP(Default)/Art/Prefabs";
    const string House01 = TownSmithBase + "/Drag&Drops/PF_House01.prefab";
    const string House02 = TownSmithBase + "/Drag&Drops/PF_House02.prefab";
    const string House03 = TownSmithBase + "/Drag&Drops/PF_House03.prefab";
    const string Market01 = TownSmithBase + "/Drag&Drops/PF_MarketStand01.prefab";
    const string Market02 = TownSmithBase + "/Drag&Drops/PF_MarketStand02.prefab";
    const string Market03 = TownSmithBase + "/Drag&Drops/PF_MarketStand03.prefab";
    const string StoneTower = TownSmithBase + "/SM_StoneTower.prefab";

    [MenuItem("CastleFight/Generate All Races")]
    public static void GenerateAllRaces()
    {
        EnsureDirectories();
        EnsureBasePrefabs();

        var races = DefineRaces();
        var raceAssets = new RaceData[races.Length];

        for (int r = 0; r < races.Length; r++)
        {
            var raceDef = races[r];
            var buildingAssets = new BuildingData[raceDef.buildings.Length];

            for (int b = 0; b < raceDef.buildings.Length; b++)
            {
                var bDef = raceDef.buildings[b];
                var uDef = bDef.unit;

                var unitPrefab = CreateUnitPrefab(uDef);
                var unitData = CreateUnitData(uDef, unitPrefab);
                var buildingPrefab = CreateBuildingPrefab(bDef);
                buildingAssets[b] = CreateBuildingData(bDef, unitData, buildingPrefab);
            }

            raceAssets[r] = CreateRaceData(raceDef, buildingAssets);
            Debug.Log($"[RaceSetup] Created race: {raceDef.name} with {buildingAssets.Length} buildings");
        }

        WireRaceDatabase(raceAssets);
        EnsureGameConfig();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RaceSetup] All 6 races generated successfully!");
    }

    // ========================================================================
    // BASE PREFABS
    // ========================================================================

    static void EnsureBasePrefabs()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(UnitBasePath) == null)
            CreateUnitBasePrefab();
        else
            UpdateUnitBasePrefab();

        if (AssetDatabase.LoadAssetAtPath<GameObject>(BuildingBasePath) == null)
            CreateBuildingBasePrefab();
        else
            UpdateBuildingBasePrefab();
    }

    static void CreateUnitBasePrefab()
    {
        var root = new GameObject("Unit_Base");
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<Unit>();
        root.AddComponent<Health>();
        root.AddComponent<UnitMovement>();
        root.AddComponent<UnitCombat>();
        root.AddComponent<UnitStateMachine>();

        var col = root.AddComponent<CapsuleCollider>();
        col.radius = 0.5f;
        col.height = 2f;
        col.center = new Vector3(0, 1f, 0);

        var placeholder = new GameObject("Model");
        placeholder.transform.SetParent(root.transform, false);

        PrefabUtility.SaveAsPrefabAsset(root, UnitBasePath);
        Object.DestroyImmediate(root);
        Debug.Log("[RaceSetup] Created Unit_Base.prefab");
    }

    static void UpdateUnitBasePrefab()
    {
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitBasePath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);

        bool dirty = false;
        if (instance.GetComponent<NetworkIdentity>() == null) { instance.AddComponent<NetworkIdentity>(); dirty = true; }
        if (instance.GetComponent<Unit>() == null) { instance.AddComponent<Unit>(); dirty = true; }
        if (instance.GetComponent<Health>() == null) { instance.AddComponent<Health>(); dirty = true; }
        if (instance.GetComponent<UnitMovement>() == null) { instance.AddComponent<UnitMovement>(); dirty = true; }
        if (instance.GetComponent<UnitCombat>() == null) { instance.AddComponent<UnitCombat>(); dirty = true; }
        if (instance.GetComponent<UnitStateMachine>() == null) { instance.AddComponent<UnitStateMachine>(); dirty = true; }

        if (dirty)
        {
            PrefabUtility.SaveAsPrefabAsset(instance, UnitBasePath);
            Debug.Log("[RaceSetup] Updated Unit_Base.prefab with missing components");
        }

        Object.DestroyImmediate(instance);
    }

    static void CreateBuildingBasePrefab()
    {
        var root = new GameObject("Bld_Base");
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<Health>();
        root.AddComponent<Building>();
        root.AddComponent<Spawner>();

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 1.5f, 0);
        col.size = new Vector3(3f, 3f, 3f);

        var modelHolder = new GameObject("Model");
        modelHolder.transform.SetParent(root.transform, false);

        var spawnPoint = new GameObject("SpawnPoint");
        spawnPoint.transform.SetParent(root.transform, false);
        spawnPoint.transform.localPosition = new Vector3(0, 0, 3f);

        var spawner = root.GetComponent<Spawner>();
        var spField = typeof(Spawner).GetField("spawnPoint",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (spField != null)
            spField.SetValue(spawner, spawnPoint.transform);

        PrefabUtility.SaveAsPrefabAsset(root, BuildingBasePath);
        Object.DestroyImmediate(root);
        Debug.Log("[RaceSetup] Created Bld_Base.prefab");
    }

    static void UpdateBuildingBasePrefab()
    {
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingBasePath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);

        bool dirty = false;
        if (instance.GetComponent<NetworkIdentity>() == null) { instance.AddComponent<NetworkIdentity>(); dirty = true; }
        if (instance.GetComponent<Health>() == null) { instance.AddComponent<Health>(); dirty = true; }
        if (instance.GetComponent<Building>() == null) { instance.AddComponent<Building>(); dirty = true; }
        if (instance.GetComponent<Spawner>() == null) { instance.AddComponent<Spawner>(); dirty = true; }

        if (dirty)
        {
            PrefabUtility.SaveAsPrefabAsset(instance, BuildingBasePath);
            Debug.Log("[RaceSetup] Updated Bld_Base.prefab with missing components");
        }

        Object.DestroyImmediate(instance);
    }

    static void EnsureDirectories()
    {
        string[] dirs = { UnitPrefabDir, BuildingPrefabDir, UnitDataDir, BuildingDataDir, RaceDataDir, "Assets/Resources" };
        foreach (var dir in dirs)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                var current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }
        }
    }

    // ========================================================================
    // UNIT PREFAB (Variant of Unit_Base)
    // ========================================================================

    static GameObject CreateUnitPrefab(UnitDef def)
    {
        string path = $"{UnitPrefabDir}/Unit_{def.id}.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(path);
        }

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitBasePath);
        if (basePrefab == null)
        {
            Debug.LogError($"[RaceSetup] Unit base prefab not found at {UnitBasePath}. Run EnsureBasePrefabs first.");
            return null;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = $"Unit_{def.id}";

        var oldModel = instance.transform.Find("Model");
        if (oldModel != null)
            Object.DestroyImmediate(oldModel.gameObject);

        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.modelPath);
        if (modelPrefab != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            model.name = "Model";
            model.transform.SetParent(instance.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogWarning($"[RaceSetup] Unit model not found at {def.modelPath}, creating empty placeholder");
            var placeholder = new GameObject("Model");
            placeholder.transform.SetParent(instance.transform, false);
        }

        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            instance, path, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    // ========================================================================
    // BUILDING PREFAB (Variant of Bld_Base)
    // ========================================================================

    static GameObject CreateBuildingPrefab(BuildingDef def)
    {
        string path = $"{BuildingPrefabDir}/Bld_{def.id}.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(path);
        }

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingBasePath);
        if (basePrefab == null)
        {
            Debug.LogError($"[RaceSetup] Building base prefab not found at {BuildingBasePath}. Run EnsureBasePrefabs first.");
            return null;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = $"Bld_{def.id}";

        var oldModel = instance.transform.Find("Model");
        if (oldModel != null)
            Object.DestroyImmediate(oldModel.gameObject);

        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.modelPath);
        if (modelPrefab != null)
        {
            var model = Object.Instantiate(modelPrefab);
            model.name = "Model";
            model.transform.SetParent(instance.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            float s = def.modelScale;
            model.transform.localScale = new Vector3(s, s, s);
            StripMissingScripts(model);
        }
        else
        {
            Debug.LogWarning($"[RaceSetup] Building model not found at {def.modelPath}, creating cube placeholder");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Model";
            cube.transform.SetParent(instance.transform, false);
            cube.transform.localPosition = new Vector3(0, 1f, 0);
            cube.transform.localScale = new Vector3(2f, 2f, 2f);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
        }

        var spawner = instance.GetComponent<Spawner>();
        var sp = instance.transform.Find("SpawnPoint");
        if (spawner != null && sp != null)
        {
            var spField = typeof(Spawner).GetField("spawnPoint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (spField != null)
                spField.SetValue(spawner, sp);
        }

        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            instance, path, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    static void StripMissingScripts(GameObject root)
    {
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
        foreach (Transform child in root.transform)
            StripMissingScripts(child.gameObject);
    }

    // ========================================================================
    // DATA ASSETS
    // ========================================================================

    static UnitData CreateUnitData(UnitDef def, GameObject prefab)
    {
        string path = $"{UnitDataDir}/{def.id}.asset";

        var existing = AssetDatabase.LoadAssetAtPath<UnitData>(path);
        if (existing != null)
        {
            existing.prefab = prefab;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var data = ScriptableObject.CreateInstance<UnitData>();
        data.unitName = def.id;
        data.displayName = def.displayName;
        data.description = def.description;
        data.prefab = prefab;
        data.maxHealth = def.hp;
        data.moveSpeed = def.moveSpeed;
        data.attackDamage = def.damage;
        data.attackSpeed = def.attackSpeed;
        data.attackRange = def.attackRange;
        data.attackType = def.attackType;
        data.armorType = def.armorType;
        data.isRanged = def.isRanged;
        data.goldBounty = def.bounty;

        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    static BuildingData CreateBuildingData(BuildingDef def, UnitData unitData, GameObject buildingPrefab)
    {
        string path = $"{BuildingDataDir}/{def.id}.asset";

        var existing = AssetDatabase.LoadAssetAtPath<BuildingData>(path);
        if (existing != null)
        {
            existing.spawnedUnit = unitData;
            existing.prefab = buildingPrefab;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var data = ScriptableObject.CreateInstance<BuildingData>();
        data.buildingId = def.id;
        data.buildingName = def.name;
        data.description = def.description;
        data.prefab = buildingPrefab;
        data.cost = def.cost;
        data.maxHealth = def.hp;
        data.armorType = ArmorType.Fortified;
        data.spawnedUnit = unitData;
        data.spawnInterval = def.spawnInterval;
        data.tier = def.tier;

        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    static RaceData CreateRaceData(RaceDef def, BuildingData[] buildings)
    {
        string path = $"{RaceDataDir}/{def.id}.asset";

        var existing = AssetDatabase.LoadAssetAtPath<RaceData>(path);
        if (existing != null)
        {
            existing.raceId = def.id;
            existing.raceName = def.name;
            existing.description = def.description;
            existing.themeColor = def.color;
            existing.buildings = buildings;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var data = ScriptableObject.CreateInstance<RaceData>();
        data.raceId = def.id;
        data.raceName = def.name;
        data.description = def.description;
        data.themeColor = def.color;
        data.buildings = buildings;

        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    static void WireRaceDatabase(RaceData[] raceAssets)
    {
        var db = AssetDatabase.LoadAssetAtPath<RaceDatabase>("Assets/Resources/RaceDatabase.asset");
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<RaceDatabase>();
            AssetDatabase.CreateAsset(db, "Assets/Resources/RaceDatabase.asset");
        }

        var field = typeof(RaceDatabase).GetField("races",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
            field.SetValue(db, raceAssets);
        else
            Debug.LogError("[RaceSetup] Could not find 'races' field on RaceDatabase via reflection");

        EditorUtility.SetDirty(db);
    }

    static void EnsureGameConfig()
    {
        string path = "Assets/Resources/GameConfig.asset";
        if (AssetDatabase.LoadAssetAtPath<GameConfig>(path) != null) return;

        var config = ScriptableObject.CreateInstance<GameConfig>();
        AssetDatabase.CreateAsset(config, path);
        Debug.Log("[RaceSetup] Created GameConfig.asset in Resources");
    }

    // ========================================================================
    // RACE DEFINITIONS
    // ========================================================================

    static RaceDef[] DefineRaces()
    {
        return new RaceDef[]
        {
            DefineHorde(),
            DefineUndead(),
            DefineWild(),
            DefineDragons(),
            DefineMythical(),
            DefineSwarm()
        };
    }

    static RaceDef DefineHorde()
    {
        return new RaceDef
        {
            id = "horde", name = "Horde", description = "Brutal greenskin warriors",
            color = new Color(0.2f, 0.6f, 0.1f),
            buildings = new[]
            {
                Bld("goblin_hut", "Goblin Hut", "Spawns Goblins", Market01, 0.8f, 80, 400, 1, 10f,
                    U("goblin", "Goblin", "Weak but fast greenskin",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Goblin/Prefabs/Goblin_PBR.prefab",
                      70, 8f, 1.4f, 2f, 5f, AttackType.Normal, ArmorType.Light, 6)),

                Bld("kobold_camp", "Kobold Camp", "Spawns Kobolds", House01, 0.9f, 100, 500, 1, 12f,
                    U("kobold", "Kobold", "Balanced melee fighter",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Kobold/Prefabs/Kobold_PBR.prefab",
                      100, 10f, 1.2f, 2f, 4f, AttackType.Normal, ArmorType.Light, 8)),

                Bld("orc_barracks", "Orc Barracks", "Spawns Orcs", House02, 1.0f, 160, 600, 2, 15f,
                    U("orc", "Orc", "Strong melee fighter",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Orc/Prefabs/Orc_PBR.prefab",
                      160, 16f, 1.0f, 2f, 3.5f, AttackType.Normal, ArmorType.Medium, 12)),

                Bld("troll_den", "Troll Den", "Spawns Trolls", House03, 1.1f, 250, 700, 3, 18f,
                    U("troll", "Troll", "Towering bruiser",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Troll/Prefabs/Troll_PBR.prefab",
                      280, 22f, 0.8f, 2.5f, 3f, AttackType.Normal, ArmorType.Heavy, 18)),

                Bld("cyclops_hall", "Cyclops Hall", "Spawns Cyclops", StoneTower, 1.2f, 380, 800, 3, 22f,
                    U("cyclops", "Cyclops", "Massive siege creature",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Cyclops/Prefabs/Cyclops_PBR.prefab",
                      450, 32f, 0.6f, 3f, 2.5f, AttackType.Siege, ArmorType.Fortified, 28))
            }
        };
    }

    static RaceDef DefineUndead()
    {
        return new RaceDef
        {
            id = "undead", name = "Undead", description = "Risen dead and dark creatures",
            color = new Color(0.4f, 0.1f, 0.5f),
            buildings = new[]
            {
                Bld("crypt", "Crypt", "Spawns Ghouls", Market02, 0.8f, 80, 400, 1, 10f,
                    U("ghoul", "Ghoul", "Fast undead attacker",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Ghoul/Prefabs/Ghoul_PBR.prefab",
                      65, 11f, 1.4f, 2f, 5f, AttackType.Normal, ArmorType.Unarmored, 6)),

                Bld("graveyard", "Graveyard", "Spawns Skeleton Knights", House01, 0.9f, 120, 500, 2, 13f,
                    U("skeleton_knight", "Skeleton Knight", "Armored undead warrior",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Skeleton Knight/Prefabs/SkeletonKnight_PBR.prefab",
                      140, 12f, 1.0f, 2f, 3.5f, AttackType.Normal, ArmorType.Heavy, 10)),

                Bld("tomb", "Tomb", "Spawns Mummies", House03, 1.0f, 180, 600, 2, 16f,
                    U("mummy", "Mummy", "Slow but heavily armored",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Mummy/Prefabs/Mummy_PBR.prefab",
                      220, 14f, 0.7f, 2f, 2.5f, AttackType.Normal, ArmorType.Fortified, 14)),

                Bld("blood_sanctum", "Blood Sanctum", "Spawns Vampires", House02, 1.1f, 240, 650, 3, 18f,
                    U("vampire", "Vampire", "Fast elite melee",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Vampire/Prefabs/Vampire_PBR.prefab",
                      150, 20f, 1.3f, 2f, 4.5f, AttackType.Normal, ArmorType.Medium, 18)),

                Bld("necropolis", "Necropolis", "Spawns Undead", StoneTower, 1.3f, 340, 750, 3, 20f,
                    U("undead", "Undead", "Heavy undead abomination",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Undead/Prefabs/Undead_PBR.prefab",
                      350, 26f, 0.7f, 2.5f, 2.5f, AttackType.Normal, ArmorType.Heavy, 24))
            }
        };
    }

    static RaceDef DefineWild()
    {
        return new RaceDef
        {
            id = "wild", name = "Wild", description = "Beasts and ancient forest creatures",
            color = new Color(0.1f, 0.5f, 0.1f),
            buildings = new[]
            {
                Bld("rat_warren", "Rat Warren", "Spawns Giant Rats", Market03, 0.7f, 60, 350, 1, 8f,
                    U("giant_rat", "Giant Rat", "Tiny but numerous",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Giant Rat/Prefabs/GiantRat_PBR.prefab",
                      45, 5f, 1.6f, 1.5f, 6f, AttackType.Normal, ArmorType.Unarmored, 4)),

                Bld("snake_pit", "Snake Pit", "Spawns Giant Vipers", Market01, 0.85f, 100, 450, 1, 12f,
                    U("giant_viper", "Giant Viper", "Venomous ranged attacker",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Giant Viper/Prefabs/GiantViper_PBR.prefab",
                      80, 12f, 1.0f, 6f, 3.5f, AttackType.Pierce, ArmorType.Light, 8, true)),

                Bld("wolf_den", "Wolf Den", "Spawns Fantasy Wolves", House01, 1.0f, 160, 550, 2, 14f,
                    U("fantasy_wolf", "Fantasy Wolf", "Fast pack hunter",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Fantasy Wolf/Prefabs/M_FantasyWolf_PBR.prefab",
                      130, 16f, 1.2f, 2f, 5f, AttackType.Normal, ArmorType.Medium, 12)),

                Bld("spider_lair", "Spider Lair", "Spawns Darkness Spiders", House02, 1.05f, 200, 600, 2, 16f,
                    U("darkness_spider", "Darkness Spider", "Tough venomous predator",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Darkness Spider/Prefabs/DarknessSpider_PBR.prefab",
                      170, 15f, 1.0f, 2f, 3.5f, AttackType.Pierce, ArmorType.Medium, 14)),

                Bld("ancient_grove", "Ancient Grove", "Spawns Oak Tree Ents", House03, 1.2f, 360, 750, 3, 22f,
                    U("oak_tree_ent", "Oak Tree Ent", "Ancient siege tank",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Demonic Creatures Pack/Oak Tree Ent/Prefabs/OakTreeEnt_PBR.prefab",
                      500, 22f, 0.5f, 2.5f, 2f, AttackType.Siege, ArmorType.Fortified, 26))
            }
        };
    }

    static RaceDef DefineDragons()
    {
        return new RaceDef
        {
            id = "dragons", name = "Dragons", description = "Ancient reptilian empire",
            color = new Color(0.8f, 0.4f, 0f),
            buildings = new[]
            {
                Bld("lizard_pit", "Lizard Pit", "Spawns Lizard Warriors", Market01, 0.85f, 100, 450, 1, 12f,
                    U("lizard_warrior", "Lizard Warrior", "Reptilian melee fighter",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Lizard Warrior/Prefabs/LizardWarrior_PBR.prefab",
                      110, 11f, 1.1f, 2f, 3.5f, AttackType.Normal, ArmorType.Medium, 8)),

                Bld("hatchery", "Hatchery", "Spawns Dragonides", Market03, 0.95f, 170, 550, 2, 15f,
                    U("dragonide", "Dragonide", "Agile dragon kin",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Dragonide/Prefabs/Dragonide_PBR.prefab",
                      150, 17f, 1.0f, 2f, 3.5f, AttackType.Normal, ArmorType.Medium, 12)),

                Bld("wyvern_roost", "Wyvern Roost", "Spawns Wyverns", House02, 1.0f, 200, 550, 2, 16f,
                    U("wyvern", "Wyvern", "Fast flying drake",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Wyvern/Prefabs/Wyvern_PBR.prefab",
                      130, 15f, 1.2f, 2f, 4.5f, AttackType.Normal, ArmorType.Light, 14)),

                Bld("hydra_pool", "Hydra Pool", "Spawns Hydras", House03, 1.15f, 300, 700, 3, 20f,
                    U("hydra", "Hydra", "Multi-headed behemoth",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Hydra/Prefabs/Hydra_PBR.prefab",
                      380, 24f, 0.7f, 2.5f, 2.5f, AttackType.Normal, ArmorType.Heavy, 22)),

                Bld("dragon_lair", "Dragon Lair", "Spawns Mountain Dragons", StoneTower, 1.4f, 420, 850, 4, 25f,
                    U("mountain_dragon", "Mountain Dragon", "Devastating elder dragon",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Mountain Dragon/Prefabs/MountainDragon_PBR.prefab",
                      550, 38f, 0.5f, 3f, 2f, AttackType.Chaos, ArmorType.Fortified, 32))
            }
        };
    }

    static RaceDef DefineMythical()
    {
        return new RaceDef
        {
            id = "mythical", name = "Mythical", description = "Legendary beasts of myth",
            color = new Color(0.6f, 0.3f, 0.7f),
            buildings = new[]
            {
                Bld("werewolf_den", "Werewolf Den", "Spawns Werewolves", Market02, 0.85f, 100, 450, 1, 12f,
                    U("werewolf", "Werewolf", "Fast savage melee",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Werewolf/Prefabs/Werewolf_PBR.prefab",
                      100, 14f, 1.3f, 2f, 4.5f, AttackType.Normal, ArmorType.Light, 8)),

                Bld("harpy_aerie", "Harpy Aerie", "Spawns Harpies", House01, 0.95f, 140, 500, 2, 13f,
                    U("harpy", "Harpy", "Swift flying attacker",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Harpy/Prefabs/HarpyBreastsCovered_PBR.prefab",
                      95, 13f, 1.3f, 2f, 5f, AttackType.Normal, ArmorType.Unarmored, 10)),

                Bld("griffin_roost", "Griffin Roost", "Spawns Griffins", House03, 1.0f, 210, 600, 2, 16f,
                    U("griffin", "Griffin", "Powerful flying fighter",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Griffin/Prefabs/Griffin2sidedFeathers_PBR.prefab",
                      175, 19f, 1.0f, 2f, 4f, AttackType.Normal, ArmorType.Medium, 16)),

                Bld("manticora_lair", "Manticora Lair", "Spawns Manticoras", House02, 1.1f, 290, 700, 3, 18f,
                    U("manticora", "Manticora", "Ranged mythical beast",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Manticora/Prefabs/Manticora_PBR.prefab",
                      260, 23f, 0.9f, 6f, 3f, AttackType.Pierce, ArmorType.Heavy, 22, true)),

                Bld("chimera_sanctum", "Chimera Sanctum", "Spawns Chimeras", StoneTower, 1.3f, 400, 800, 4, 22f,
                    U("chimera", "Chimera", "Three-headed elite",
                      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Chimera/Prefabs/Chimera_PBR.prefab",
                      420, 32f, 0.7f, 2.5f, 2.5f, AttackType.Magic, ArmorType.Heavy, 30))
            }
        };
    }

    static RaceDef DefineSwarm()
    {
        return new RaceDef
        {
            id = "swarm", name = "Swarm", description = "Creatures of the deep wilds",
            color = new Color(0.3f, 0.7f, 0.5f),
            buildings = new[]
            {
                Bld("mushroom_patch", "Mushroom Patch", "Spawns Mushrooms", Market03, 0.75f, 60, 350, 1, 9f,
                    U("mushroom", "Mushroom", "Tiny fungal warrior",
                      "Assets/Monsters Ultimate Pack 11/Mushroom/Prefabs/Mushroom Black No Root.prefab",
                      55, 7f, 1.4f, 2f, 4.5f, AttackType.Normal, ArmorType.Unarmored, 4)),

                Bld("hive", "Hive", "Spawns Bees", Market01, 0.85f, 90, 400, 1, 10f,
                    U("bee", "Bee", "Agile flying insect",
                      "Assets/Monsters Ultimate Pack 11/Bee/Prefabs/Bee Black No Root.prefab",
                      70, 10f, 1.4f, 2f, 5f, AttackType.Normal, ArmorType.Light, 6)),

                Bld("thorn_garden", "Thorn Garden", "Spawns Plant Shooters", House01, 1.0f, 150, 550, 2, 14f,
                    U("plant_shooter", "Plant Shooter", "Thorny ranged plant",
                      "Assets/Monsters Ultimate Pack 11/Plant Shooter/Prefabs/No Root/Plant Shooter Red No Root.prefab",
                      110, 14f, 1.0f, 6f, 3f, AttackType.Pierce, ArmorType.Medium, 10, true)),

                Bld("eye_tower", "Eye Tower", "Spawns Eyeball Monsters", House02, 1.05f, 200, 600, 2, 16f,
                    U("eyeball_monster", "Eyeball Monster", "Arcane floating eye",
                      "Assets/Monsters Ultimate Pack 11/Eyeball Monster/Prefabs/No Root/Eyeball Monster 03 NR.prefab",
                      130, 17f, 1.0f, 6f, 3f, AttackType.Magic, ArmorType.Light, 14, true)),

                Bld("dark_sanctum", "Dark Sanctum", "Spawns Dark Wizards", House03, 1.15f, 320, 700, 3, 20f,
                    U("dark_wizard", "Dark Wizard", "Powerful dark caster",
                      "Assets/Monsters Ultimate Pack 11/Modular Dark Wizard/Prefabs/Characters/Dark Wizard Black No Root Setup.prefab",
                      220, 28f, 0.8f, 7f, 3f, AttackType.Magic, ArmorType.Medium, 24, true))
            }
        };
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    static UnitDef U(string id, string displayName, string desc, string modelPath,
        int hp, float dmg, float atkSpd, float atkRange, float moveSpd,
        AttackType atkType, ArmorType armType, int bounty, bool ranged = false)
    {
        return new UnitDef
        {
            id = id, displayName = displayName, description = desc, modelPath = modelPath,
            hp = hp, damage = dmg, attackSpeed = atkSpd, attackRange = atkRange,
            moveSpeed = moveSpd, attackType = atkType, armorType = armType,
            bounty = bounty, isRanged = ranged
        };
    }

    static BuildingDef Bld(string id, string name, string desc, string modelPath, float modelScale,
        int cost, int hp, int tier, float interval, UnitDef unit)
    {
        return new BuildingDef
        {
            id = id, name = name, description = desc,
            modelPath = modelPath, modelScale = modelScale,
            cost = cost, hp = hp, tier = tier, spawnInterval = interval,
            unit = unit
        };
    }
}
#endif
