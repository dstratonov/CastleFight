using UnityEngine;
using Mirror;
using System.Collections.Generic;
using Pathfinding;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Runtime stress-test tool for pathfinding and crowd behavior.
/// Toggle GUI with F11. Spawns units, places buildings, runs preset scenarios.
/// Requires Host mode (DevAutoHost handles this automatically).
/// Add to any GameObject in the scene or auto-created by PathfindingDebugToggle.
/// </summary>
public class PathfindingStressTest : MonoBehaviour
{
    private bool showGUI;
    private Vector2 scrollPos;

    // Spawn config
    private int spawnCount = 20;
    private int selectedUnitIndex;
    private int selectedBuildingIndex;
    private int spawnTeam;
    private bool castlesInvincible = true;
    [SerializeField] private bool disableAIOnStart = false;

    // Cached asset lists
    private string[] unitNames;
    private string[] unitPaths;
    private string[] buildingNames;
    private string[] buildingPaths;

    // Tracked spawned objects for cleanup
    private readonly List<GameObject> spawnedUnits = new();
    private readonly List<GameObject> spawnedBuildings = new();

    private void Start()
    {
        LoadAssetLists();
        if (disableAIOnStart)
            DisableAIPlayers();
        if (castlesInvincible)
            SetCastlesInvincible(true);
        // GameDebug.Movement can be toggled via PathfindingDebugToggle (F10)

        // Enable the debug overlay with useful defaults. GameUIBuilder adds
        // the DebugOverlay component at runtime with enabled=false; by the
        // time Invoke() fires (0.5s), Unity has finished scene bootstrap so
        // the component definitely exists and we can enable it without
        // racing the UI builder.
        Invoke(nameof(EnableDebugOverlay), 0.5f);
    }

    private void EnableDebugOverlay()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var overlay = cam.GetComponent<DebugOverlay>();
        if (overlay == null)
            overlay = cam.gameObject.AddComponent<DebugOverlay>();
        overlay.enabled = true;
        overlay.showPaths = true;
        overlay.showAttackRange = true;        // attack reach
        overlay.showAggroRange = false;        // off by default — too much clutter in S7
        overlay.showUnitRadius = true;         // the unified physical radius
        overlay.showAttackSlots = true;
        overlay.showBuildingFootprints = true;
        Debug.Log("[StressTest] DebugOverlay enabled with default test-scene flags");
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current[Key.F11].wasPressedThisFrame)
            showGUI = !showGUI;
#else
        if (Input.GetKeyDown(KeyCode.F11))
            showGUI = !showGUI;
#endif
    }

    private void OnGUI()
    {
        if (!showGUI) return;

        float w = 360f;
        float h = Screen.height - 40f;
        GUILayout.BeginArea(new Rect(Screen.width - w - 10, 20, w, h), "Pathfinding Stress Test (F11)", GUI.skin.window);
        scrollPos = GUILayout.BeginScrollView(scrollPos);

        DrawStatus();
        GUILayout.Space(8);
        DrawSpawnConfig();
        GUILayout.Space(8);
        DrawQuickActions();
        GUILayout.Space(8);
        DrawScenarios();
        GUILayout.Space(8);
        DrawCleanup();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // ================================================================
    //  STATUS
    // ================================================================

    private void DrawStatus()
    {
        GUILayout.Label("--- Status ---");
        bool hostActive = NetworkServer.active;
        bool pfReady = AstarPath.active != null;
        bool unitMgr = UnitManager.Instance != null;

        GUILayout.Label($"Host: {(hostActive ? "OK" : "NOT ACTIVE")}");
        GUILayout.Label($"Pathfinding: {(pfReady ? "OK" : "NOT READY")}  UnitMgr: {(unitMgr ? "OK" : "NO")}");

        int unitCount = 0;
        if (unitMgr)
        {
            var t0 = UnitManager.Instance.GetTeamUnits(0);
            var t1 = UnitManager.Instance.GetTeamUnits(1);
            unitCount = (t0?.Count ?? 0) + (t1?.Count ?? 0);
        }
        GUILayout.Label($"Units alive: {unitCount}  Spawned: {spawnedUnits.Count}  Buildings: {spawnedBuildings.Count}");

        if (!hostActive)
        {
            if (GUILayout.Button("Start Host"))
                NetworkManager.singleton?.StartHost();
        }

        if (hostActive && !pfReady)
        {
            if (GUILayout.Button("Init Pathfinding"))
            {
                if (GameManager.Instance != null)
                    GameManager.Instance.StartMatch();
            }
        }

        GUILayout.BeginHorizontal();
        bool newInvincible = GUILayout.Toggle(castlesInvincible, "Castles Invincible");
        if (newInvincible != castlesInvincible)
        {
            castlesInvincible = newInvincible;
            SetCastlesInvincible(castlesInvincible);
        }
        GUILayout.EndHorizontal();
    }

    // ================================================================
    //  SPAWN CONFIG
    // ================================================================

    private void DrawSpawnConfig()
    {
        GUILayout.Label("--- Config ---");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Count:", GUILayout.Width(50));
        string countStr = GUILayout.TextField(spawnCount.ToString(), GUILayout.Width(50));
        if (int.TryParse(countStr, out int c) && c > 0) spawnCount = c;

        if (GUILayout.Button("5", GUILayout.Width(30))) spawnCount = 5;
        if (GUILayout.Button("20", GUILayout.Width(30))) spawnCount = 20;
        if (GUILayout.Button("50", GUILayout.Width(30))) spawnCount = 50;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Team:", GUILayout.Width(50));
        if (GUILayout.Button("0", GUILayout.Width(40))) spawnTeam = 0;
        if (GUILayout.Button("1", GUILayout.Width(40))) spawnTeam = 1;
        GUILayout.Label($"= {spawnTeam}");
        GUILayout.EndHorizontal();

        if (unitNames != null && unitNames.Length > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Unit:", GUILayout.Width(50));
            if (GUILayout.Button("<", GUILayout.Width(25)))
                selectedUnitIndex = (selectedUnitIndex - 1 + unitNames.Length) % unitNames.Length;
            GUILayout.Label(unitNames[selectedUnitIndex], GUILayout.Width(130));
            if (GUILayout.Button(">", GUILayout.Width(25)))
                selectedUnitIndex = (selectedUnitIndex + 1) % unitNames.Length;
            GUILayout.EndHorizontal();
        }

        if (buildingNames != null && buildingNames.Length > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bldg:", GUILayout.Width(50));
            if (GUILayout.Button("<", GUILayout.Width(25)))
                selectedBuildingIndex = (selectedBuildingIndex - 1 + buildingNames.Length) % buildingNames.Length;
            GUILayout.Label(buildingNames[selectedBuildingIndex], GUILayout.Width(130));
            if (GUILayout.Button(">", GUILayout.Width(25)))
                selectedBuildingIndex = (selectedBuildingIndex + 1) % buildingNames.Length;
            GUILayout.EndHorizontal();
        }
    }

    // ================================================================
    //  QUICK ACTIONS
    // ================================================================

    private void DrawQuickActions()
    {
        GUILayout.Label("--- Quick Actions ---");

        if (GUILayout.Button($"Spawn {spawnCount} units at LEFT -> march RIGHT"))
            SpawnUnitsMarching(new Vector3(-60, 0, 0), new Vector3(60, 0, 0), spawnCount, spawnTeam);

        if (GUILayout.Button($"Spawn {spawnCount} units at RIGHT -> march LEFT"))
            SpawnUnitsMarching(new Vector3(60, 0, 0), new Vector3(-60, 0, 0), spawnCount, spawnTeam);

        if (GUILayout.Button($"Spawn {spawnCount} units -> converge on CENTER"))
            SpawnUnitsConverge(Vector3.zero, 50f, spawnCount, spawnTeam);

        if (GUILayout.Button("Place building at CENTER (0,0,0)"))
            PlaceBuildingAt(Vector3.zero);

        if (GUILayout.Button("Place building at (20, 0, 0)"))
            PlaceBuildingAt(new Vector3(20, 0, 0));

        if (GUILayout.Button("Place 3 buildings (obstacle wall)"))
        {
            PlaceBuildingAt(new Vector3(0, 0, -6));
            PlaceBuildingAt(new Vector3(0, 0, 0));
            PlaceBuildingAt(new Vector3(0, 0, 6));
        }
    }

    // ================================================================
    //  PRESET SCENARIOS
    // ================================================================

    private void DrawScenarios()
    {
        GUILayout.Label("--- Scenarios ---");

        if (GUILayout.Button("S1: March Through Gap"))
            RunScenarioMarchThroughGap();

        if (GUILayout.Button("S2: Building Mid-Route"))
            RunScenarioMidRouteBuilding();

        if (GUILayout.Button("S3: Two Armies Collide"))
            RunScenarioTwoArmies();

        if (GUILayout.Button("S4: Crowd Around Target"))
            RunScenarioCrowdTarget();

        if (GUILayout.Button("S5: Obstacle Maze"))
            RunScenarioMaze();

        if (GUILayout.Button("S6: Units Near Buildings"))
            RunScenarioNearBuildings();

        if (GUILayout.Button("S7: Dogpile Small Target"))
            RunScenarioDogpileSmallTarget();

        if (GUILayout.Button("S8: Mixed Attackers (small/med/large)"))
            RunScenarioMixedAttackers();
    }

    // ================================================================
    //  CLEANUP
    // ================================================================

    private void DrawCleanup()
    {
        GUILayout.Label("--- Cleanup ---");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Kill All Spawned Units"))
            CleanupUnits();
        if (GUILayout.Button("Remove Buildings"))
            CleanupBuildings();
        GUILayout.EndHorizontal();

        if (GUILayout.Button("FULL RESET"))
        {
            CleanupUnits();
            CleanupBuildings();
        }
    }

    // ================================================================
    //  SPAWN LOGIC
    // ================================================================

    private void SpawnUnitsMarching(Vector3 from, Vector3 to, int count, int team)
    {
        if (!ValidateReady()) return;
        var data = LoadUnitData();
        if (data == null) return;
        SpawnUnitsMarchingOfType(data, from, to, count, team);
    }

    /// <summary>
    /// Same as <see cref="SpawnUnitsMarching"/> but takes an explicit
    /// <see cref="UnitData"/> so scenarios can mix multiple unit types in
    /// a single spawn (e.g. S8's small/medium/large attacker test).
    /// </summary>
    private void SpawnUnitsMarchingOfType(UnitData data, Vector3 from, Vector3 to, int count, int team)
    {
        if (!ValidateReady()) return;
        if (data == null) return;

        Vector3 right = Vector3.Cross(Vector3.up, (to - from).normalized);
        // 3× body radius = 1.5× body diameter between centres. Gives a
        // ~0.5-body gap between adjacent spawns so RVO has slack to sort
        // the crowd without units popping through each other as the back
        // rows catch up to the slowing front rows.
        float spacing = data.unitRadius * 3.0f;

        for (int i = 0; i < count; i++)
        {
            int row = i / 5;
            int col = i % 5;
            Vector3 offset = right * (col - 2) * spacing + (to - from).normalized * (-row * spacing);
            Vector3 pos = from + offset;
            pos = SnapToWalkable(pos);

            var obj = UnitManager.Instance.SpawnUnit(data, pos, Quaternion.identity, team);
            if (obj != null)
            {
                spawnedUnits.Add(obj);
                var unit = obj.GetComponent<Unit>();
                unit?.Movement?.SetDestinationWorld(to);
            }
        }

        Debug.Log($"[StressTest] Spawned {count} {data.unitName} from {from:F0} -> {to:F0}");
    }

    /// <summary>
    /// Looks up a UnitData asset by name (via the cached unitNames/
    /// unitPaths discovered in <see cref="LoadAssetLists"/>). Returns
    /// null if no matching asset is found. Editor-only.
    /// </summary>
    private UnitData LoadUnitDataByName(string unitName)
    {
#if UNITY_EDITOR
        if (unitPaths == null || unitNames == null) return null;
        for (int i = 0; i < unitNames.Length; i++)
        {
            if (string.Equals(unitNames[i], unitName, System.StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<UnitData>(unitPaths[i]);
        }
#endif
        return null;
    }

    private void SpawnUnitsConverge(Vector3 target, float radius, int count, int team)
    {
        if (!ValidateReady()) return;

        var data = LoadUnitData();
        if (data == null) return;

        for (int i = 0; i < count; i++)
        {
            float angle = (i / (float)count) * Mathf.PI * 2f;
            Vector3 pos = target + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            pos = SnapToWalkable(pos);

            var obj = UnitManager.Instance.SpawnUnit(data, pos, Quaternion.identity, team);
            if (obj != null)
            {
                spawnedUnits.Add(obj);
                var unit = obj.GetComponent<Unit>();
                unit?.Movement?.SetDestinationWorld(target);
            }
        }

        Debug.Log($"[StressTest] Spawned {count} {data.unitName} converging on {target:F0}");
    }

    private void PlaceBuildingAt(Vector3 pos)
    {
        if (!ValidateReady()) return;

        var data = LoadBuildingData();
        if (data == null) return;

        pos.y = 0f;

        var obj = BuildingManager.Instance?.PlaceBuilding(data, pos, Quaternion.identity, spawnTeam, -1);
        if (obj != null)
        {
            spawnedBuildings.Add(obj);
            Debug.Log($"[StressTest] Placed {data.buildingName} at {pos:F1}");
        }
        else
        {
            Debug.LogWarning($"[StressTest] Failed to place {data.buildingName} at {pos:F1}");
        }
    }

    // ================================================================
    //  SCENARIO IMPLEMENTATIONS
    // ================================================================

    /// <summary>
    /// S1: Place two buildings with a narrow gap, spawn units marching through it.
    /// </summary>
    private void RunScenarioMarchThroughGap()
    {
        CleanupUnits();
        CleanupBuildings();

        PlaceBuildingAt(new Vector3(0, 0, -8));
        PlaceBuildingAt(new Vector3(0, 0, 8));

        SpawnUnitsMarching(new Vector3(-40, 0, 0), new Vector3(40, 0, 0), spawnCount, 0);
        Debug.Log("[StressTest] S1: March Through Gap started");
    }

    /// <summary>
    /// S2: Spawn units marching, then place a building in their path after 3s.
    /// </summary>
    private void RunScenarioMidRouteBuilding()
    {
        CleanupUnits();
        CleanupBuildings();

        SpawnUnitsMarching(new Vector3(-50, 0, 0), new Vector3(50, 0, 0), spawnCount, 0);
        Invoke(nameof(PlaceMidRouteBuilding), 3f);
        Debug.Log("[StressTest] S2: Building Mid-Route — building will appear in 3s at (10,0,0)");
    }

    private void PlaceMidRouteBuilding()
    {
        PlaceBuildingAt(new Vector3(10, 0, 0));
    }

    /// <summary>
    /// S3: Two armies marching at each other from opposite sides.
    /// </summary>
    private void RunScenarioTwoArmies()
    {
        CleanupUnits();
        CleanupBuildings();

        int half = spawnCount / 2;
        SpawnUnitsMarching(new Vector3(-50, 0, 0), new Vector3(50, 0, 0), half, 0);
        SpawnUnitsMarching(new Vector3(50, 0, 0), new Vector3(-50, 0, 0), half, 1);
        Debug.Log("[StressTest] S3: Two Armies Collide started");
    }

    /// <summary>
    /// S4: All units converge on the same point — congestion test.
    /// </summary>
    private void RunScenarioCrowdTarget()
    {
        CleanupUnits();
        CleanupBuildings();

        SpawnUnitsConverge(new Vector3(10, 0, 10), 40f, spawnCount, 0);
        Debug.Log("[StressTest] S4: Crowd Around Target started");
    }

    /// <summary>
    /// S5: Place buildings in a maze pattern, spawn units that must navigate through.
    /// </summary>
    private void RunScenarioMaze()
    {
        CleanupUnits();
        CleanupBuildings();

        PlaceBuildingAt(new Vector3(-10, 0, -10));
        PlaceBuildingAt(new Vector3(-10, 0, -4));
        PlaceBuildingAt(new Vector3(-10, 0, 2));
        PlaceBuildingAt(new Vector3(10, 0, -6));
        PlaceBuildingAt(new Vector3(10, 0, 0));
        PlaceBuildingAt(new Vector3(10, 0, 6));

        SpawnUnitsMarching(new Vector3(-40, 0, 0), new Vector3(40, 0, 0), spawnCount, 0);
        Debug.Log("[StressTest] S5: Obstacle Maze started");
    }

    /// <summary>
    /// S6: Place buildings, spawn units adjacent to them to test edge-case navigation.
    /// </summary>
    private void RunScenarioNearBuildings()
    {
        CleanupUnits();
        CleanupBuildings();

        PlaceBuildingAt(new Vector3(0, 0, 0));
        PlaceBuildingAt(new Vector3(0, 0, 8));

        if (!ValidateReady()) return;
        var data = LoadUnitData();
        if (data == null) return;

        Vector3[] spawnPositions = {
            new(-8, 0, 2), new(-8, 0, 6), new(-6, 0, -4),
            new(8, 0, 2), new(8, 0, 6), new(6, 0, -4),
            new(-4, 0, -8), new(4, 0, -8), new(0, 0, 16)
        };
        Vector3 target = new Vector3(40, 0, 0);

        int count = Mathf.Min(spawnCount, spawnPositions.Length * 3);
        for (int i = 0; i < count; i++)
        {
            Vector3 basePos = spawnPositions[i % spawnPositions.Length];
            float jitter = data.unitRadius * (i / spawnPositions.Length);
            Vector3 pos = basePos + new Vector3(jitter, 0, jitter);
            pos = SnapToWalkable(pos);

            var obj = UnitManager.Instance.SpawnUnit(data, pos, Quaternion.identity, 0);
            if (obj != null)
            {
                spawnedUnits.Add(obj);
                obj.GetComponent<Unit>()?.Movement?.SetDestinationWorld(target);
            }
        }

        Debug.Log($"[StressTest] S6: Units Near Buildings — {count} units spawned near buildings, heading to {target:F0}");
    }

    /// <summary>
    /// S7: Dogpile Small Target - regression test for the RVO clumping bug.
    /// Spawns ONE stationary INVINCIBLE enemy target at origin and spawnCount
    /// attackers 40u west, all using the currently-selected unit type (so
    /// attacker mesh is at least target mesh - worst case for visual overlap).
    /// After the AttackRangeHelper fan-out + RVO radius fix, attackers should
    /// form a crescent on the west side of the target with no mesh
    /// interpenetration.
    /// Combat targeting is automatic via UnitCombat aggro scan - no explicit
    /// attack commands. The target is pinned by Stop() which hard-locks its
    /// RVO agent, and made invincible so it doesn't die to the dogpile and
    /// let attackers auto-resume toward the enemy castle.
    /// </summary>
    private void RunScenarioDogpileSmallTarget()
    {
        CleanupUnits();
        CleanupBuildings();

        if (!ValidateReady()) return;
        var data = LoadUnitData();
        if (data == null) return;

        Vector3 targetPos = SnapToWalkable(Vector3.zero);
        var targetObj = UnitManager.Instance.SpawnUnit(data, targetPos, Quaternion.identity, 1);
        if (targetObj == null)
        {
            Debug.LogWarning("[StressTest] S7: failed to spawn target");
            return;
        }
        spawnedUnits.Add(targetObj);

        // Pin the target in place:
        // - Movement.Stop() hard-locks its RVO agent and halts RichAI
        // - UnitCombat is disabled so it cannot retaliate via MoveToAttackPosition,
        //   which would call SetDestinationWorld and UNLOCK the RVO agent — letting
        //   the target drift away from origin chasing attackers.
        // - Health.Invincible keeps it alive through the dogpile so attackers don't
        //   finish it off in ~1s and auto-resume toward the enemy castle.
        var targetUnit = targetObj.GetComponent<Unit>();
        targetUnit?.Movement?.Stop();
        var targetCombat = targetObj.GetComponent<UnitCombat>();
        if (targetCombat != null) targetCombat.enabled = false;
        var targetHealth = targetObj.GetComponent<Health>();
        if (targetHealth != null) targetHealth.Invincible = true;

        SpawnUnitsMarching(new Vector3(-40, 0, 0), targetPos, spawnCount, 0);

        Debug.Log($"[StressTest] S7: Dogpile - 1 invincible {data.unitName} target at origin, {spawnCount} attackers from (-40,0,0)");
    }

    /// <summary>
    /// S8: Mixed attacker sizes against one target.
    ///
    /// Spawns a medium goblin target at origin, then three waves of
    /// attackers with very different body radii, each from a different
    /// direction:
    ///   - 12 footmen (r ≈ 0.30, small)   from west
    ///   - 10 goblins (r ≈ 1.02, medium)  from south
    ///   -  4 trolls  (r ≈ 3.34, large)   from east
    ///
    /// The arc-based capacity check in UnitCombat.Scan should accept a
    /// MIX of sizes up to the target ring's 2π rad budget: each small
    /// footman takes ~0.46 rad, each medium goblin ~1.05 rad, each large
    /// troll ~1.74 rad. First-come-first-serve — arrival order determines
    /// which types fit, and the rest overflow to the enemy castle.
    ///
    /// The key test is NOT "how many fit" (that's order-dependent) but
    /// "does every committed attacker actually reach and attack", and
    /// "is <see cref="UnitManager"/> ever left with idle units that
    /// neither fight nor walk". A healthy run has noTarget == 0 once the
    /// waves settle.
    /// </summary>
    private void RunScenarioMixedAttackers()
    {
        CleanupUnits();
        CleanupBuildings();

        if (!ValidateReady()) return;

        var smallData = LoadUnitDataByName("footman");
        var mediumData = LoadUnitDataByName("goblin");
        var largeData = LoadUnitDataByName("troll");

        if (mediumData == null)
        {
            Debug.LogWarning("[StressTest] S8: goblin UnitData not found");
            return;
        }

        Vector3 targetPos = SnapToWalkable(Vector3.zero);
        var targetObj = UnitManager.Instance.SpawnUnit(mediumData, targetPos, Quaternion.identity, 1);
        if (targetObj == null)
        {
            Debug.LogWarning("[StressTest] S8: failed to spawn target");
            return;
        }
        spawnedUnits.Add(targetObj);

        // Pin the target (same rules as S7).
        var targetUnit = targetObj.GetComponent<Unit>();
        targetUnit?.Movement?.Stop();
        var targetCombat = targetObj.GetComponent<UnitCombat>();
        if (targetCombat != null) targetCombat.enabled = false;
        var targetHealth = targetObj.GetComponent<Health>();
        if (targetHealth != null) targetHealth.Invincible = true;

        // Three waves from three sides.
        if (smallData != null)
            SpawnUnitsMarchingOfType(smallData, new Vector3(-40, 0, 0), targetPos, 12, 0);
        else
            Debug.LogWarning("[StressTest] S8: footman UnitData not found, skipping small wave");

        SpawnUnitsMarchingOfType(mediumData, new Vector3(0, 0, -40), targetPos, 10, 0);

        if (largeData != null)
            SpawnUnitsMarchingOfType(largeData, new Vector3(40, 0, 0), targetPos, 4, 0);
        else
            Debug.LogWarning("[StressTest] S8: troll UnitData not found, skipping large wave");

        Debug.Log("[StressTest] S8: Mixed attackers - goblin target, 12 footmen/W + 10 goblins/S + 4 trolls/E");
    }

    // ================================================================
    //  CLEANUP
    // ================================================================

    private void CleanupUnits()
    {
        int count = 0;
        for (int i = spawnedUnits.Count - 1; i >= 0; i--)
        {
            var obj = spawnedUnits[i];
            if (obj != null)
            {
                if (NetworkServer.active)
                    NetworkServer.Destroy(obj);
                else
                    Destroy(obj);
                count++;
            }
        }
        spawnedUnits.Clear();
        if (count > 0) Debug.Log($"[StressTest] Destroyed {count} units");
    }

    private void CleanupBuildings()
    {
        int count = 0;
        for (int i = spawnedBuildings.Count - 1; i >= 0; i--)
        {
            var obj = spawnedBuildings[i];
            if (obj != null)
            {
                var health = obj.GetComponent<Health>();
                if (health != null)
                {
                    health.TakeDamage(99999, null);
                }
                else if (NetworkServer.active)
                {
                    EventBus.Raise(new BuildingDestroyedEvent(obj, spawnTeam));
                    NetworkServer.Destroy(obj);
                }
                else
                {
                    Destroy(obj);
                }
                count++;
            }
        }
        spawnedBuildings.Clear();
        if (count > 0) Debug.Log($"[StressTest] Removed {count} buildings");
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private void SetCastlesInvincible(bool invincible)
    {
        foreach (var castle in GameRegistry.Castles)
        {
            if (castle == null) continue;
            var health = castle.GetComponent<Health>();
            if (health != null)
            {
                health.Invincible = invincible;
                if (invincible)
                    health.Heal(health.MaxHealth);
            }
        }
        Debug.Log($"[StressTest] Castles invincible = {invincible}");
    }

    /// <summary>
    /// Destroy any AIPlayer GameObjects already in the scene. Called from Start()
    /// when disableAIOnStart is true. Belt-and-suspenders purge for the case
    /// where SampleScene (dontDestroyOnLoad NetworkGameManager) was opened
    /// first and carried its spawned AIPlayers into the test scene.
    /// Idempotent - safe when no AIPlayers exist.
    /// </summary>
    private void DisableAIPlayers()
    {
        var ais = Object.FindObjectsByType<AIPlayer>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var ai in ais)
        {
            if (ai == null) continue;
            Destroy(ai.gameObject);
            count++;
        }
        if (count > 0)
            Debug.Log($"[StressTest] Disabled {count} AIPlayer(s) for deterministic test scene");
    }

    private bool ValidateReady()
    {
        if (!NetworkServer.active)
        {
            Debug.LogWarning("[StressTest] NetworkServer not active — start as Host first");
            return false;
        }
        if (UnitManager.Instance == null)
        {
            Debug.LogWarning("[StressTest] UnitManager not available");
            return false;
        }
        if (AstarPath.active == null)
        {
            Debug.LogWarning("[StressTest] AstarPath not active — ensure A* Pro is in the scene");
            return false;
        }
        return true;
    }

    private Vector3 SnapToWalkable(Vector3 pos)
    {
        pos.y = 0f;
        if (AstarPath.active != null)
        {
            var nearest = AstarPath.active.GetNearest(pos, NearestNodeConstraint.Walkable);
            if (nearest.node != null) return nearest.position;
        }
        return pos;
    }

    private UnitData LoadUnitData()
    {
#if UNITY_EDITOR
        if (unitPaths != null && selectedUnitIndex < unitPaths.Length)
        {
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(unitPaths[selectedUnitIndex]);
            if (data != null) return data;
        }
#endif
        Debug.LogWarning("[StressTest] Could not load UnitData");
        return null;
    }

    private BuildingData LoadBuildingData()
    {
#if UNITY_EDITOR
        if (buildingPaths != null && selectedBuildingIndex < buildingPaths.Length)
        {
            var data = AssetDatabase.LoadAssetAtPath<BuildingData>(buildingPaths[selectedBuildingIndex]);
            if (data != null) return data;
        }
#endif
        Debug.LogWarning("[StressTest] Could not load BuildingData");
        return null;
    }

    private void LoadAssetLists()
    {
#if UNITY_EDITOR
        var unitGuids = AssetDatabase.FindAssets("t:UnitData", new[] { "Assets/Data/Units" });
        unitNames = new string[unitGuids.Length];
        unitPaths = new string[unitGuids.Length];
        for (int i = 0; i < unitGuids.Length; i++)
        {
            unitPaths[i] = AssetDatabase.GUIDToAssetPath(unitGuids[i]);
            var asset = AssetDatabase.LoadAssetAtPath<UnitData>(unitPaths[i]);
            unitNames[i] = asset != null ? asset.unitName : System.IO.Path.GetFileNameWithoutExtension(unitPaths[i]);
        }

        var bldGuids = AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/Data/Buildings" });
        buildingNames = new string[bldGuids.Length];
        buildingPaths = new string[bldGuids.Length];
        for (int i = 0; i < bldGuids.Length; i++)
        {
            buildingPaths[i] = AssetDatabase.GUIDToAssetPath(bldGuids[i]);
            var asset = AssetDatabase.LoadAssetAtPath<BuildingData>(buildingPaths[i]);
            buildingNames[i] = asset != null ? asset.buildingName : System.IO.Path.GetFileNameWithoutExtension(buildingPaths[i]);
        }

        if (unitNames.Length > 0)
        {
            for (int i = 0; i < unitNames.Length; i++)
            {
                if (unitNames[i] == "goblin" || unitNames[i] == "Goblin")
                { selectedUnitIndex = i; break; }
            }
        }
#else
        unitNames = new string[0];
        unitPaths = new string[0];
        buildingNames = new string[0];
        buildingPaths = new string[0];
#endif
    }
}
