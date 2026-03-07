using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;

public class GridMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float repathInterval = 0.5f;

    private Unit unit;
    private GridSystem grid;
    private Vector2Int currentCell;
    private Vector2Int? targetCell;
    private List<Vector2Int> currentPath;
    private int pathIndex;
    private bool isMovingBetweenCells;
    private Vector3 moveFrom;
    private Vector3 moveTo;
    private float moveProgress;
    private float repathTimer;

    public bool IsMoving => isMovingBetweenCells || (currentPath != null && pathIndex < currentPath.Count);
    public Vector2Int CurrentCell => currentCell;
    public bool HasPath => currentPath != null && pathIndex < currentPath.Count;
    public Vector2Int? TargetCell => targetCell;

    public event Action OnPathBlocked;
    public event Action OnReachedDestination;

    private void Awake()
    {
        unit = GetComponent<Unit>();
    }

    public override void OnStartServer()
    {
        grid = GridSystem.Instance;
        if (grid == null)
        {
            Debug.LogError($"[GridMovement] GridSystem.Instance is NULL on {gameObject.name}! Unit will not move.");
            return;
        }

        currentCell = grid.WorldToCell(transform.position);
        transform.position = grid.CellToWorld(currentCell);
        bool occupied = grid.TryOccupyCell(currentCell, gameObject);
        Debug.Log($"[GridMovement] {gameObject.name} started at cell {currentCell}, occupy={occupied}, worldPos={transform.position}");

        DisableRootMotion();
    }

    private void DisableRootMotion()
    {
        var animators = GetComponentsInChildren<Animator>();
        foreach (var anim in animators)
        {
            anim.applyRootMotion = false;
        }
    }

    private void Update()
    {
        if (!isServer || grid == null) return;
        if (unit != null && unit.IsDead) return;

        if (isMovingBetweenCells)
        {
            UpdateCellMovement();
            return;
        }

        repathTimer -= Time.deltaTime;

        if (currentPath != null && pathIndex < currentPath.Count)
        {
            TryMoveToNextCell();
        }
        else if (targetCell.HasValue && currentCell != targetCell.Value)
        {
            if (repathTimer <= 0f)
            {
                repathTimer = repathInterval;
                RecalculatePath();
            }
        }
    }

    [Server]
    public void SetDestination(Vector2Int cell)
    {
        targetCell = cell;
        RecalculatePath();
    }

    [Server]
    public void SetDestinationWorld(Vector3 worldPos)
    {
        if (grid == null) return;
        SetDestination(grid.WorldToCell(worldPos));
    }

    [Server]
    public void SetDestinationToEnemyCastle()
    {
        if (unit == null || grid == null)
        {
            Debug.LogWarning($"[GridMovement] SetDestinationToEnemyCastle aborted: unit={unit != null}, grid={grid != null}");
            return;
        }

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(unit.TeamId)
            : (unit.TeamId == 0 ? 1 : 0);

        Castle[] castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var c in castles)
        {
            if (c.TeamId == enemyTeam)
            {
                Vector2Int castleCell = grid.WorldToCell(c.transform.position);
                if (targetCell.HasValue && targetCell.Value == castleCell)
                    return;

                Debug.Log($"[GridMovement] {gameObject.name} heading to enemy castle at {c.transform.position} (cell {castleCell})");
                SetDestinationWorld(c.transform.position);
                return;
            }
        }
        if (Time.frameCount % 300 == 0)
            Debug.LogWarning($"[GridMovement] No enemy castle found for team {unit.TeamId} (enemy team {enemyTeam}). Found {castles.Length} castles total.");
    }

    [Server]
    public void Stop()
    {
        currentPath = null;
        pathIndex = 0;
        targetCell = null;
    }

    [Server]
    public void Resume()
    {
        if (targetCell.HasValue)
            RecalculatePath();
    }

    [Server]
    public void SetPathDirect(List<Vector2Int> path)
    {
        currentPath = path;
        pathIndex = 1; // Skip index 0 which is the current cell
    }

    [Server]
    private void RecalculatePath()
    {
        if (!targetCell.HasValue || grid == null) return;

        if (isMovingBetweenCells)
            return;

        var result = GridPathfinding.FindPath(currentCell, targetCell.Value, grid, gameObject);

        if (result.HasPath)
        {
            currentPath = result.Path;
            pathIndex = 1;
            Debug.Log($"[GridMovement] {gameObject.name} path found: {result.Path.Count} cells, complete={result.IsComplete}, from {currentCell} to {targetCell.Value}");

            if (!result.IsComplete)
                OnPathBlocked?.Invoke();
        }
        else
        {
            currentPath = null;
            Debug.LogWarning($"[GridMovement] {gameObject.name} NO path from {currentCell} to {targetCell.Value}");
            OnPathBlocked?.Invoke();
        }
    }

    private void TryMoveToNextCell()
    {
        if (currentPath == null || pathIndex >= currentPath.Count) return;

        Vector2Int nextCell = currentPath[pathIndex];

        if (grid.TryReserveCell(nextCell, gameObject))
        {
            isMovingBetweenCells = true;
            moveFrom = grid.CellToWorld(currentCell);
            moveTo = grid.CellToWorld(nextCell);
            moveProgress = 0f;

            float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;
            RpcSyncMovement(moveFrom, moveTo, speed);
        }
        else
        {
            repathTimer = 0f; // Force repath next frame
        }
    }

    private void UpdateCellMovement()
    {
        float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;
        float cellDistance = grid.CellSize;
        moveProgress += (speed / cellDistance) * Time.deltaTime;

        transform.position = Vector3.Lerp(moveFrom, moveTo, moveProgress);

        if (moveTo != moveFrom)
        {
            Vector3 dir = (moveTo - moveFrom).normalized;
            if (dir != Vector3.zero)
                transform.forward = dir;
        }

        if (moveProgress >= 1f)
        {
            Vector2Int previousCell = currentCell;
            Vector2Int arrivedCell = grid.WorldToCell(moveTo);

            grid.ReleaseCell(previousCell, gameObject);
            grid.SetCellOccupied(arrivedCell, gameObject);
            currentCell = arrivedCell;
            transform.position = grid.CellToWorld(currentCell);

            pathIndex++;
            isMovingBetweenCells = false;

            if (targetCell.HasValue && currentCell == targetCell.Value)
            {
                currentPath = null;
                OnReachedDestination?.Invoke();
            }
        }
    }

    [ClientRpc]
    private void RpcSyncMovement(Vector3 from, Vector3 to, float speed)
    {
        if (isServer) return;
        moveFrom = from;
        moveTo = to;
        moveProgress = 0f;
        isMovingBetweenCells = true;
        moveSpeed = speed;
    }

    private void OnDestroy()
    {
        if (grid != null)
            grid.ReleaseCell(currentCell, gameObject);
    }
}
