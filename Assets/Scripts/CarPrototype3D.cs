using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// An isolated 3D translation of the fixed Color Sort boards. Cars preserve the
/// exact positions, colors, and exit directions from the 2D level database;
/// neutral blocks are represented by the imported police-car block.
/// </summary>
public sealed class CarPrototype3D : MonoBehaviour
{
    private const int MaximumTraySlots = 5;
    private const int FixedLevelCount = 60;
    // Temporary switch for the approved post-60 mechanic samples. Keeping the
    // activation here makes the samples easy to remove without touching the
    // validated 60-level database or any of its boards.
    private const bool EnableExperimentalLevels61To66 = false;
    private const int ExperimentalLevelCount = 6;
    private const float BoardRouteLaneX = 4.25f;
    private const float CarDriveSpeed = 13f;
    private const float OutsideCarDriveSpeed = 25f;
    private const float RouteCornerRadius = 0.78f;
    private const float TapAssistRadiusPixels = 88f;

    private static Texture2D asphaltTexture;
    private static Material backgroundAsphaltMaterial;
    private static Material playfieldAsphaltMaterial;
    private static Material parkingLineMaterial;

    internal enum PieceColor { Red, Green, Blue, Yellow, Trash }
    internal enum ExitDirection { Up, Down, Left, Right }

    private struct PieceSpec
    {
        public int row;
        public int col;
        public PieceColor color;
        public ExitDirection direction;
        public int cellLength;

        public PieceSpec(int row, int col, PieceColor color, ExitDirection direction, int cellLength = 1)
        {
            this.row = row;
            this.col = col;
            this.color = color;
            this.direction = direction;
            this.cellLength = Mathf.Max(1, cellLength);
        }
    }

    private struct LimousineCandidate
    {
        public int carIndex;
        public int neutralIndex;
        public int leadingRow;
        public int leadingCol;
        public ExitDirection direction;

        public LimousineCandidate(int carIndex, int neutralIndex, int leadingRow, int leadingCol, ExitDirection direction)
        {
            this.carIndex = carIndex;
            this.neutralIndex = neutralIndex;
            this.leadingRow = leadingRow;
            this.leadingCol = leadingCol;
            this.direction = direction;
        }
    }

    private sealed class PrototypeLevel
    {
        public readonly int boardNumber;
        public readonly int boardSize;
        public readonly int matchTarget;
        public readonly UnityGameManager.LevelConfig source;

        public PrototypeLevel(int boardNumber, UnityGameManager.LevelConfig source)
        {
            this.boardNumber = boardNumber;
            this.source = source;
            boardSize = source.boardSize;
            matchTarget = source.matchTarget;
        }
    }

    private struct ExperimentalLockRule
    {
        public int row;
        public int col;
        public PieceColor unlockAfterColor;

        public ExperimentalLockRule(int row, int col, PieceColor unlockAfterColor)
        {
            this.row = row;
            this.col = col;
            this.unlockAfterColor = unlockAfterColor;
        }
    }

    private sealed class ExperimentalRuleSet
    {
        public readonly string title;
        public readonly string planningHint;
        public readonly int parkingUseLimit;
        public readonly PieceColor[] requiredColorOrder;
        public readonly ExperimentalLockRule[] locks;

        public ExperimentalRuleSet(
            string title,
            string planningHint,
            int parkingUseLimit = -1,
            PieceColor[] requiredColorOrder = null,
            ExperimentalLockRule[] locks = null)
        {
            this.title = title;
            this.planningHint = planningHint;
            this.parkingUseLimit = parkingUseLimit;
            this.requiredColorOrder = requiredColorOrder ?? new PieceColor[0];
            this.locks = locks ?? new ExperimentalLockRule[0];
        }
    }

    private sealed class ActiveExperimentalLock
    {
        public readonly CarPuzzlePiece piece;
        public readonly PieceColor unlockAfterColor;

        public ActiveExperimentalLock(CarPuzzlePiece piece, PieceColor unlockAfterColor)
        {
            this.piece = piece;
            this.unlockAfterColor = unlockAfterColor;
        }
    }

    private readonly List<CarPuzzlePiece> boardPieces = new List<CarPuzzlePiece>();
    private readonly List<CarPuzzlePiece> trayPieces = new List<CarPuzzlePiece>();
    private readonly List<CarPuzzlePiece> allPieces = new List<CarPuzzlePiece>();
    private readonly List<PrototypeLevel> levels = new List<PrototypeLevel>();
    private readonly List<Transform> boardGridCells = new List<Transform>();
    private readonly HashSet<CarPuzzlePiece> queuedBoardPieces = new HashSet<CarPuzzlePiece>();
    private readonly HashSet<CarPuzzlePiece> boardCarsCurrentlyDriving = new HashSet<CarPuzzlePiece>();
    private readonly Transform[] trayDividerLines = new Transform[MaximumTraySlots - 1];
    private readonly Renderer[] trayDividerLineRenderers = new Renderer[MaximumTraySlots - 1];
    private readonly List<Transform> sideParkingHatchLines = new List<Transform>();
    private readonly List<ActiveExperimentalLock> activeExperimentalLocks = new List<ActiveExperimentalLock>();

    private Camera prototypeCamera;
    private CarPrototypeHudLayout sceneLayout;
    private GameObject levelRoot;
    private GameObject boardEnvironmentRoot;
    private Transform asphaltGroundTransform;
    private Transform roadBoardTransform;
    private Transform roadInsetTransform;
    private Transform matchTrayRootTransform;
    private Transform matchTrayTopLine;
    private Transform matchTrayBottomLine;
    private Transform matchTrayLeftLine;
    private Transform matchTrayRightLine;
    private Transform sideParkingRootTransform;
    private Transform sideParkingTopLine;
    private Transform sideParkingBottomLine;
    private Transform sideParkingLeftLine;
    private Transform sideParkingRightLine;
    private Renderer roadBoardRenderer;
    private CarPuzzlePiece parkedPiece;
    private CarPuzzlePiece lastMovedPiece;
    private CarPrototypeHud hud;
    private int levelIndex;
    private int redCleared;
    private int greenCleared;
    private int blueCleared;
    private int yellowCleared;
    private int activeBoardSize = 3;
    private int activeMatchTarget = 3;
    private int activeBlueTarget;
    private int activeYellowTarget;
    private int hearts = 3;
    private int trayCapacity = 3;
    private int parkingUses;
    private bool extraSlotUsed;
    private bool isAnimating;
    private bool isClearingTrayMatch;
    private int boardCarsInTransit;
    private ExperimentalRuleSet activeExperimentalRules;

    public int BoardNumber => levels.Count == 0 ? 1 : levels[levelIndex].boardNumber;
    public int Hearts => hearts;
    public int RedCleared => redCleared;
    public int GreenCleared => greenCleared;
    public int BlueCleared => blueCleared;
    public int YellowCleared => yellowCleared;
    public int MatchGoal => activeMatchTarget;
    public int BlueGoal => activeBlueTarget;
    public int YellowGoal => activeYellowTarget;
    public int TrayCapacity => trayCapacity;
    public bool ExtraSlotUsed => extraSlotUsed;
    public bool CanUseExtraSlot => activeExperimentalRules == null && !isAnimating && !extraSlotUsed && trayCapacity < MaximumTraySlots;
    public bool CanUseUndo => !isAnimating && lastMovedPiece != null;
    public string ExperimentalRuleStatus => BuildExperimentalRuleStatus();

    private void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Screen.orientation = ScreenOrientation.Portrait;
    }

    private void Start()
    {
        sceneLayout = CarPrototypeHudLayout.LoadOrDefault();
        CreateCamera();
        CreateLighting();
        CreateVisualEffects();
        LoadFixedLevels();
        CreateMatchTray();
        CreateParkingSlot();
        hud = gameObject.AddComponent<CarPrototypeHud>();
        hud.Initialize(this);
        LoadLevel(0);
        Apply3DSettingsFromEditor(sceneLayout);
        hud.ShowMainMenu();
    }

    /// <summary>
    /// Applies the scene controls exposed by the 3D layout editor. This is safe
    /// to call repeatedly while Play Mode is running.
    /// </summary>
    public bool Apply3DSettingsFromEditor(CarPrototypeHudLayout editedLayout)
    {
        if (editedLayout == null) return false;
        sceneLayout = editedLayout;

        ApplyCameraSettings();
        ApplyAsphaltMaterialTint(backgroundAsphaltMaterial, sceneLayout.sceneBackgroundAsphaltColor);
        ApplyAsphaltMaterialTint(playfieldAsphaltMaterial, sceneLayout.scenePlayfieldAsphaltColor);

        if (roadBoardRenderer != null && roadBoardRenderer.sharedMaterial != null)
            roadBoardRenderer.sharedMaterial.color = sceneLayout.sceneRoadBorderColor;

        float boardWidth = activeBoardSize >= 4 ? 7.2f : 6.9f;
        float roadDepth = Mathf.Max(1f, sceneLayout.sceneRoadDepth);
        if (asphaltGroundTransform != null)
        {
            asphaltGroundTransform.position = sceneLayout.sceneAsphaltGroundPosition;
            asphaltGroundTransform.localScale = PositiveScale(sceneLayout.sceneAsphaltGroundSize);
        }
        if (roadBoardTransform != null)
        {
            roadBoardTransform.position = new Vector3(0f, -0.25f, sceneLayout.sceneRoadCenterZ);
            roadBoardTransform.localScale = new Vector3(boardWidth, 0.5f, roadDepth);
        }
        if (roadInsetTransform != null)
        {
            roadInsetTransform.position = new Vector3(0f, 0.02f, sceneLayout.sceneRoadCenterZ);
            roadInsetTransform.localScale = new Vector3(boardWidth - 0.65f, 0.06f, Mathf.Max(0.35f, roadDepth - 0.65f));
        }

        UpdateMatchTrayRoadMarkings();
        UpdateParkingHighlight();

        float boardScale = GetBoardPieceScale();
        for (int index = 0; index < trayPieces.Count; index++)
        {
            trayPieces[index].UpdateScaleSettings(boardScale, GetOffBoardPieceScale(trayPieces[index].CellLength));
            trayPieces[index].SetTrayPose(GetTraySlotPosition(index));
        }
        if (parkedPiece != null)
        {
            parkedPiece.UpdateScaleSettings(boardScale, GetOffBoardPieceScale(parkedPiece.CellLength));
            parkedPiece.SetParkingPose(GetParkingSlotPosition());
        }

        for (int index = 0; index < boardGridCells.Count; index++)
        {
            int row = index / activeBoardSize;
            int col = index % activeBoardSize;
            boardGridCells[index].position = GetBoardCellPosition(row, col) + new Vector3(0f, -0.18f, 0f);
        }

        for (int index = 0; index < boardPieces.Count; index++)
        {
            CarPuzzlePiece piece = boardPieces[index];
            if (piece == null) continue;
            piece.UpdateScaleSettings(boardScale, GetOffBoardPieceScale(piece.CellLength));
            piece.SetBoardPose(GetBoardPiecePosition(piece));
        }

        return true;
    }

    private void Update()
    {
        if (Time.timeScale == 0f) return;

#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            TrySelectPiece(Touchscreen.current.primaryTouch.position.ReadValue());
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            TrySelectPiece(Mouse.current.position.ReadValue());

        if (Keyboard.current != null)
        {
            if (Keyboard.current.nKey.wasPressedThisFrame) LoadNextLevel();
            if (Keyboard.current.digit1Key.wasPressedThisFrame) LoadLevel(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) LoadLevel(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) LoadLevel(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) LoadLevel(3);
            if (Keyboard.current.digit5Key.wasPressedThisFrame) LoadLevel(4);
        }
#else
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            TrySelectPiece(Input.GetTouch(0).position);
        else if (Input.GetMouseButtonDown(0))
            TrySelectPiece(Input.mousePosition);
#endif
    }

    [ContextMenu("Load Next Prototype Level")]
    public void LoadNextLevel()
    {
        if (levels.Count == 0) return;
        LoadLevel((levelIndex + 1) % levels.Count);
    }

    [ContextMenu("Load Previous Prototype Level")]
    public void LoadPreviousLevel()
    {
        if (levels.Count == 0) return;
        LoadLevel((levelIndex - 1 + levels.Count) % levels.Count);
    }

    private void TrySelectPiece(Vector2 screenPosition)
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        CarPuzzlePiece piece = PickPiece(screenPosition);
        if (piece == null) return;

        if (boardPieces.Contains(piece))
        {
            // Board cars may be launched while earlier cars are still driving.
            // Other interactions stay locked until all reserved tray cars arrive.
            if (isAnimating && boardCarsInTransit == 0 && !isClearingTrayMatch) return;
            TryMovePiece(piece);
            return;
        }

        if (isAnimating) return;

        if (trayPieces.Contains(piece))
        {
            TryParkTrayPiece(piece);
            return;
        }

        if (parkedPiece == piece) TryReturnParkedPiece();
    }

    private CarPuzzlePiece PickPiece(Vector2 screenPosition)
    {
        Ray ray = prototypeCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        CarPuzzlePiece closestCar = null;
        float closestDistance = float.MaxValue;

        // The road tiles also have colliders. Search every hit so a road collider
        // can never hide a car that the player has tapped.
        for (int index = 0; index < hits.Length; index++)
        {
            CarPuzzlePiece candidate = hits[index].collider.GetComponentInParent<CarPuzzlePiece>();
            if (candidate == null || !candidate.IsTouchable || hits[index].distance >= closestDistance) continue;
            if (boardCarsInTransit > 0 && !boardPieces.Contains(candidate)) continue;

            closestCar = candidate;
            closestDistance = hits[index].distance;
        }

        if (closestCar != null) return closestCar;

        // Mobile touches are imprecise, especially on the small parked and tray cars.
        // If the ray barely misses a collider, choose the nearest visible car in a
        // restrained screen-space radius instead of ignoring the tap.
        CarPuzzlePiece assistedCar = null;
        float nearestScreenDistance = TapAssistRadiusPixels * TapAssistRadiusPixels;
        for (int index = 0; index < allPieces.Count; index++)
        {
            CarPuzzlePiece candidate = allPieces[index];
            if (candidate == null || !candidate.IsTouchable) continue;
            if (boardCarsInTransit > 0 && !boardPieces.Contains(candidate)) continue;

            Vector3 projected = prototypeCamera.WorldToScreenPoint(candidate.transform.position + Vector3.up * 0.42f);
            if (projected.z < 0f) continue;

            float distance = ((Vector2)projected - screenPosition).sqrMagnitude;
            if (distance >= nearestScreenDistance) continue;

            assistedCar = candidate;
            nearestScreenDistance = distance;
        }

        return assistedCar;
    }

    private void TryMovePiece(CarPuzzlePiece piece)
    {
        if (piece.IsLocked)
        {
            piece.Reject();
            RefreshHud();
            return;
        }

        if (!IsExitPathClear(piece))
        {
            piece.Reject();
            RegisterWrongMove();
            return;
        }

        // A fast tap immediately after completing a color used to be judged
        // while the matched cars still occupied the tray during their short
        // celebration. Preserve that tap and launch it as soon as the matched
        // cars release their slots instead of incorrectly removing a heart.
        if (!piece.IsTrash && trayPieces.Count >= trayCapacity)
        {
            if (HasCompletedTrayMatchWaiting())
            {
                QueueBoardPieceAfterTrayMatch(piece);
                return;
            }

            piece.Reject();
            RegisterWrongMove();
            return;
        }

        piece.BeginExitPoliceLights();
        boardPieces.Remove(piece);
        lastMovedPiece = piece;
        Vector3 trayTarget = Vector3.zero;
        if (!piece.IsTrash)
        {
            int trayIndex = trayPieces.Count;
            trayPieces.Add(piece);
            trayTarget = GetTraySlotPosition(trayIndex);
            UpdateTrayHighlights();
        }

        boardCarsInTransit++;
        boardCarsCurrentlyDriving.Add(piece);
        isAnimating = true;
        StartCoroutine(ExitBoard(piece, trayTarget));
    }

    private void QueueBoardPieceAfterTrayMatch(CarPuzzlePiece piece)
    {
        if (piece == null || queuedBoardPieces.Contains(piece)) return;

        // One completed group frees at most the tray's capacity. Limiting the
        // input buffer prevents later unrelated cars from moving automatically.
        if (queuedBoardPieces.Count >= trayCapacity) return;

        queuedBoardPieces.Add(piece);
        StartCoroutine(MoveQueuedBoardPieceWhenTrayIsReady(piece));
    }

    private IEnumerator MoveQueuedBoardPieceWhenTrayIsReady(CarPuzzlePiece piece)
    {
        while (trayPieces.Count >= trayCapacity)
        {
            if (piece == null || !boardPieces.Contains(piece))
            {
                queuedBoardPieces.Remove(piece);
                yield break;
            }

            yield return null;
        }

        queuedBoardPieces.Remove(piece);
        if (piece == null || !boardPieces.Contains(piece)) yield break;
        TryMovePiece(piece);
    }

    private bool HasCompletedTrayMatchWaiting()
    {
        if (TryGetNextOrderedColor(out PieceColor orderedColor))
            return HasCompletedTrayMatchForColor(orderedColor);

        if (redCleared < activeMatchTarget && FindMatchingCars(PieceColor.Red, false).Count >= activeMatchTarget)
            return true;
        if (greenCleared < activeMatchTarget && FindMatchingCars(PieceColor.Green, false).Count >= activeMatchTarget)
            return true;
        if (activeBlueTarget > 0 && blueCleared < activeBlueTarget && FindMatchingCars(PieceColor.Blue, true).Count >= activeBlueTarget)
            return true;
        if (activeYellowTarget > 0 && yellowCleared < activeYellowTarget && FindMatchingCars(PieceColor.Yellow, false).Count >= activeYellowTarget)
            return true;

        return false;
    }

    private bool HasCompletedTrayMatchForColor(PieceColor color)
    {
        int target = GetMatchTarget(color);
        if (target <= 0 || IsColorCleared(color)) return false;
        return FindMatchingCars(color, color == PieceColor.Blue).Count >= target;
    }

    private bool IsExitPathClear(CarPuzzlePiece piece)
    {
        Vector2Int step = DirectionToGridStep(piece.Direction);
        int row = piece.Row + step.y;
        int col = piece.Col + step.x;

        while (row >= 0 && row < activeBoardSize && col >= 0 && col < activeBoardSize)
        {
            for (int index = 0; index < boardPieces.Count; index++)
            {
                CarPuzzlePiece blocker = boardPieces[index];
                if (blocker != piece && blocker.OccupiesCell(row, col))
                    return false;
            }

            row += step.y;
            col += step.x;
        }

        return true;
    }

    private IEnumerator ExitBoard(CarPuzzlePiece piece, Vector3 trayTarget)
    {
        Vector3 boardExit = GetBoardExitPosition(piece);
        yield return StartCoroutine(DriveRouteSegment(piece, boardExit));

        if (piece.IsTrash)
        {
            yield return StartCoroutine(piece.Despawn(0.16f));
            FinishBoardCarTransit(piece);
            yield break;
        }

        yield return StartCoroutine(DriveCarToTray(piece, boardExit, trayTarget));
        FinishBoardCarTransit(piece);
    }

    private void FinishBoardCarTransit(CarPuzzlePiece piece)
    {
        boardCarsCurrentlyDriving.Remove(piece);
        boardCarsInTransit = Mathf.Max(0, boardCarsInTransit - 1);
        if (boardCarsInTransit > 0)
        {
            RefreshHud();
            return;
        }

        // A previous color may still be playing its celebration while newly
        // released cars arrive. Let that clear finish, then evaluate the new
        // tray once so overlapping clear coroutines cannot fight each other.
        if (isClearingTrayMatch)
        {
            RefreshHud();
            return;
        }

        CheckTrayMatches();
        if (!isAnimating) UpdateTrayHighlights();
        RefreshHud();
    }

    private IEnumerator DriveCarToTray(CarPuzzlePiece piece, Vector3 boardExit, Vector3 trayTarget)
    {
        float routeHeight = trayTarget.y;
        var route = new List<Vector3>();

        if (piece.Direction == ExitDirection.Down)
        {
            // A downward-facing car has already left toward the tray. Keep that
            // natural route and only line it up with its bay before entering.
            float stagingZ = Mathf.Min(GetMatchTrayZ() + 0.55f, boardExit.z);
            route.Add(new Vector3(boardExit.x, routeHeight, stagingZ));
            route.Add(new Vector3(trayTarget.x, routeHeight, stagingZ));
        }
        else
        {
            // Cars that leave at the top or sides travel around the outside of
            // the board, so they never cut through cars that are still playing.
            float sideX = GetBoardRouteSideX(piece, boardExit.x);
            route.Add(new Vector3(sideX, routeHeight, boardExit.z));
            route.Add(new Vector3(sideX, routeHeight, GetTrayApproachZ()));
            route.Add(new Vector3(trayTarget.x, routeHeight, GetTrayApproachZ()));
        }

        route.Add(trayTarget);
        float routeSpeed = piece.Direction == ExitDirection.Down ? CarDriveSpeed : OutsideCarDriveSpeed;
        yield return StartCoroutine(DriveRoundedRouteToFinalApproach(piece, route, routeSpeed, RouteCornerRadius));

        float finalDuration = Mathf.Clamp(Vector3.Distance(piece.transform.position, trayTarget) / CarDriveSpeed, 0.14f, 0.32f);
        yield return StartCoroutine(piece.DriveToTraySlot(trayTarget, finalDuration));
    }

    private Vector3 GetBoardExitPosition(CarPuzzlePiece piece)
    {
        float spacing = GetBoardSpacing();
        float topZ = GetSceneLayout().sceneBoardFirstRowZ;
        float bottomZ = topZ - (activeBoardSize - 1) * spacing;
        float extraFootprint = (piece.CellLength - 1) * spacing * 0.5f;
        float verticalClearance = spacing * 0.85f + extraFootprint;
        float horizontalRouteX = BoardRouteLaneX + extraFootprint;

        switch (piece.Direction)
        {
            case ExitDirection.Up:
                return new Vector3(piece.transform.position.x, piece.transform.position.y, topZ + verticalClearance);
            case ExitDirection.Down:
                return new Vector3(piece.transform.position.x, piece.transform.position.y, bottomZ - verticalClearance);
            case ExitDirection.Left:
                return new Vector3(-horizontalRouteX, piece.transform.position.y, piece.transform.position.z);
            default:
                return new Vector3(horizontalRouteX, piece.transform.position.y, piece.transform.position.z);
        }
    }

    private float GetBoardRouteSideX(CarPuzzlePiece piece, float currentX)
    {
        float extraFootprint = (piece.CellLength - 1) * GetBoardSpacing() * 0.5f;
        float routeX = BoardRouteLaneX + extraFootprint;
        if (piece.Direction == ExitDirection.Left) return -routeX;
        if (piece.Direction == ExitDirection.Right) return routeX;
        return currentX < 0f ? -routeX : routeX;
    }

    private IEnumerator DriveRoundedRouteToFinalApproach(
        CarPuzzlePiece piece,
        List<Vector3> waypoints,
        float speed,
        float cornerRadius)
    {
        var cleanWaypoints = new List<Vector3>();
        Vector3 previous = piece.transform.position;
        for (int index = 0; index < waypoints.Count; index++)
        {
            if (Vector3.Distance(previous, waypoints[index]) < 0.04f) continue;
            cleanWaypoints.Add(waypoints[index]);
            previous = waypoints[index];
        }

        // The final point is deliberately left for DriveToTraySlot or
        // DriveToParkingSlot, which eases the car into its exact pose and size.
        for (int index = 0; index < cleanWaypoints.Count - 1; index++)
        {
            Vector3 corner = cleanWaypoints[index];
            Vector3 next = cleanWaypoints[index + 1];
            Vector3 incoming = corner - piece.transform.position;
            Vector3 outgoing = next - corner;

            if (incoming.sqrMagnitude < 0.0025f || outgoing.sqrMagnitude < 0.0025f)
                continue;

            float radius = Mathf.Min(cornerRadius, incoming.magnitude * 0.42f, outgoing.magnitude * 0.42f);
            Vector3 entry = corner - incoming.normalized * radius;
            Vector3 exit = corner + outgoing.normalized * radius;
            yield return StartCoroutine(DriveRouteSegment(piece, entry, speed));

            float curveLength = Vector3.Distance(entry, corner) + Vector3.Distance(corner, exit);
            float curveDuration = Mathf.Clamp(curveLength / speed, 0.07f, 0.26f);
            yield return StartCoroutine(piece.DriveCurve(corner, exit, curveDuration));
        }
    }

    private IEnumerator DriveRouteSegment(CarPuzzlePiece piece, Vector3 target, float speed = CarDriveSpeed)
    {
        float distance = Vector3.Distance(piece.transform.position, target);
        if (distance < 0.035f) yield break;

        float duration = Mathf.Clamp(distance / Mathf.Max(1f, speed), 0.06f, 0.72f);
        yield return StartCoroutine(piece.DriveTo(target, duration));
    }

    private void CheckTrayMatches()
    {
        List<CarPuzzlePiece> redCars = FindMatchingCars(PieceColor.Red, false);
        List<CarPuzzlePiece> greenCars = FindMatchingCars(PieceColor.Green, false);
        List<CarPuzzlePiece> blueCars = FindMatchingCars(PieceColor.Blue, true);
        List<CarPuzzlePiece> yellowCars = FindMatchingCars(PieceColor.Yellow, false);

        if (TryGetNextOrderedColor(out PieceColor orderedColor))
        {
            if (orderedColor == PieceColor.Red && redCars.Count >= activeMatchTarget)
                BeginTrayMatch(PieceColor.Red, redCars);
            else if (orderedColor == PieceColor.Green && greenCars.Count >= activeMatchTarget)
                BeginTrayMatch(PieceColor.Green, greenCars);
            else if (orderedColor == PieceColor.Blue && activeBlueTarget > 0 && blueCars.Count >= activeBlueTarget)
                BeginTrayMatch(PieceColor.Blue, blueCars);
            else if (orderedColor == PieceColor.Yellow && activeYellowTarget > 0 && yellowCars.Count >= activeYellowTarget)
                BeginTrayMatch(PieceColor.Yellow, yellowCars);
            else
            {
                isAnimating = isClearingTrayMatch;
                RefreshHud();
            }
            return;
        }

        if (redCars.Count >= activeMatchTarget)
        {
            BeginTrayMatch(PieceColor.Red, redCars);
            return;
        }

        if (greenCars.Count >= activeMatchTarget)
        {
            BeginTrayMatch(PieceColor.Green, greenCars);
            return;
        }

        if (blueCars.Count >= activeBlueTarget && activeBlueTarget > 0)
        {
            BeginTrayMatch(PieceColor.Blue, blueCars);
            return;
        }

        if (yellowCars.Count >= activeYellowTarget && activeYellowTarget > 0)
        {
            BeginTrayMatch(PieceColor.Yellow, yellowCars);
            return;
        }

        isAnimating = isClearingTrayMatch;
        RefreshHud();
    }

    private void BeginTrayMatch(PieceColor color, List<CarPuzzlePiece> matchingCars)
    {
        switch (color)
        {
            case PieceColor.Red: redCleared = activeMatchTarget; break;
            case PieceColor.Green: greenCleared = activeMatchTarget; break;
            case PieceColor.Blue: blueCleared = activeBlueTarget; break;
            case PieceColor.Yellow: yellowCleared = activeYellowTarget; break;
        }

        UpdateExperimentalLocks();
        lastMovedPiece = null;
        isClearingTrayMatch = true;
        StartCoroutine(ClearMatchedCars(matchingCars));
    }

    private List<CarPuzzlePiece> FindMatchingCars(PieceColor color, bool requiresAdjacency)
    {
        int target = GetMatchTarget(color);
        var result = new List<CarPuzzlePiece>();
        if (target <= 0) return result;

        if (!requiresAdjacency)
        {
            for (int index = 0; index < trayPieces.Count && result.Count < target; index++)
            {
                if (trayPieces[index].PieceColor == color) result.Add(trayPieces[index]);
            }
            return result;
        }

        // Blue cars must occupy consecutive bays, matching the 2D rule.
        for (int start = 0; start <= trayPieces.Count - target; start++)
        {
            bool isMatch = true;
            for (int offset = 0; offset < target; offset++)
            {
                if (trayPieces[start + offset].PieceColor == color) continue;
                isMatch = false;
                break;
            }

            if (!isMatch) continue;
            for (int offset = 0; offset < target; offset++) result.Add(trayPieces[start + offset]);
            return result;
        }

        return result;
    }

    private int GetMatchTarget(PieceColor color)
    {
        if (color == PieceColor.Blue) return activeBlueTarget;
        if (color == PieceColor.Yellow) return activeYellowTarget;
        return color == PieceColor.Trash ? 0 : activeMatchTarget;
    }

    private bool HasCompletedAllColorGoals()
    {
        bool redDone = redCleared >= activeMatchTarget;
        bool greenDone = greenCleared >= activeMatchTarget;
        bool blueDone = activeBlueTarget == 0 || blueCleared >= activeBlueTarget;
        bool yellowDone = activeYellowTarget == 0 || yellowCleared >= activeYellowTarget;
        return redDone && greenDone && blueDone && yellowDone;
    }

    private IEnumerator ClearMatchedCars(List<CarPuzzlePiece> matchingCars)
    {
        yield return new WaitForSeconds(0.08f);
        for (int index = 0; index < matchingCars.Count; index++)
        {
            trayPieces.Remove(matchingCars[index]);
            StartCoroutine(matchingCars[index].CelebrateAndDespawn());
        }

        yield return new WaitForSeconds(0.28f);
        yield return StartCoroutine(AnimateTrayLayout(0.22f, true));
        UpdateTrayHighlights();

        if (HasCompletedAllColorGoals())
        {
            StartCoroutine(CompleteLevel());
            yield break;
        }

        isClearingTrayMatch = false;
        if (boardCarsInTransit == 0)
        {
            CheckTrayMatches();
            if (isClearingTrayMatch)
            {
                RefreshHud();
                yield break;
            }
        }

        isAnimating = boardCarsInTransit > 0;
        RefreshHud();
    }

    private IEnumerator CompleteLevel()
    {
        yield return new WaitForSeconds(0.65f);
        LoadNextLevel();
    }

    private void LoadLevel(int targetIndex)
    {
        if (levels.Count == 0) return;

        StopAllCoroutines();
        isAnimating = false;
        isClearingTrayMatch = false;
        boardCarsInTransit = 0;
        queuedBoardPieces.Clear();
        boardCarsCurrentlyDriving.Clear();
        levelIndex = Mathf.Clamp(targetIndex, 0, levels.Count - 1);
        PrototypeLevel level = levels[levelIndex];
        activeBoardSize = level.boardSize;
        activeMatchTarget = level.matchTarget;
        activeBlueTarget = 0;
        activeYellowTarget = 0;
        redCleared = 0;
        greenCleared = 0;
        blueCleared = 0;
        yellowCleared = 0;
        hearts = 3;
        trayCapacity = activeMatchTarget;
        parkingUses = 0;
        extraSlotUsed = false;
        boardPieces.Clear();
        trayPieces.Clear();
        allPieces.Clear();
        parkedPiece = null;
        lastMovedPiece = null;
        activeExperimentalLocks.Clear();
        activeExperimentalRules = CreateExperimentalRules(level.boardNumber);

        if (levelRoot != null) Destroy(levelRoot);
        levelRoot = new GameObject($"Car Board {level.boardNumber}");
        CreateBoard(activeBoardSize);

        // Build only the selected board. Previously every board's 3D piece
        // specification was generated during startup even though only one
        // board can be visible at a time.
        PieceSpec[] specifications = BuildPrototypePieceSpecs(level.source);
        for (int index = 0; index < specifications.Length; index++)
        {
            if (specifications[index].color == PieceColor.Blue) activeBlueTarget++;
            if (specifications[index].color == PieceColor.Yellow) activeYellowTarget++;
        }
        for (int index = 0; index < specifications.Length; index++)
        {
            PieceSpec spec = specifications[index];
            CarPuzzlePiece piece = CreatePuzzlePiece(spec);
            boardPieces.Add(piece);
            allPieces.Add(piece);
        }

        ApplyExperimentalLocks();

        UpdateTrayHighlights();
        UpdateParkingHighlight();
        RefreshHud();
        Debug.Log($"Loaded fixed 3D car Board {level.boardNumber} of {levels.Count}.");
    }

    private CarPuzzlePiece CreatePuzzlePiece(PieceSpec specification)
    {
        string kind = specification.color == PieceColor.Trash
            ? "Police Car Block"
            : specification.cellLength > 1 ? $"{specification.color} Limousine" : $"{specification.color} Car";
        GameObject pieceObject = new GameObject($"{kind}_{specification.row}_{specification.col}");
        pieceObject.transform.SetParent(levelRoot.transform, true);
        CarPuzzlePiece piece = pieceObject.AddComponent<CarPuzzlePiece>();
        float boardScale = GetBoardPieceScale();
        float offBoardScale = GetOffBoardPieceScale(specification.cellLength);
        piece.Configure(
            specification.row,
            specification.col,
            specification.color,
            specification.direction,
            GetBoardPiecePosition(specification.row, specification.col, specification.direction, specification.cellLength),
            boardScale,
            offBoardScale,
            specification.cellLength);
        return piece;
    }

    private void LoadFixedLevels()
    {
        ColorSortLevelDatabase database = Resources.Load<ColorSortLevelDatabase>("ColorSortLevelDatabase");
        if (database == null || database.levels == null || database.levels.Count < FixedLevelCount)
        {
            Debug.LogError($"The fixed Color Sort level database is missing or has fewer than {FixedLevelCount} boards.");
            return;
        }

        levels.Clear();
        for (int levelIndex = 0; levelIndex < FixedLevelCount; levelIndex++)
        {
            UnityGameManager.LevelConfig source = database.levels[levelIndex];
            levels.Add(CreatePrototypeLevel(source, source.id));
        }

        if (EnableExperimentalLevels61To66)
        {
            for (int sampleIndex = 0; sampleIndex < ExperimentalLevelCount; sampleIndex++)
            {
                UnityGameManager.LevelConfig source = database.levels[50 + sampleIndex];
                levels.Add(CreatePrototypeLevel(source, FixedLevelCount + sampleIndex + 1));
            }
        }

        Debug.Log($"Loaded {FixedLevelCount} fixed boards plus {levels.Count - FixedLevelCount} temporary post-60 samples for the 3D car prototype.");
    }

    private static PrototypeLevel CreatePrototypeLevel(UnityGameManager.LevelConfig source, int boardNumber)
    {
        return new PrototypeLevel(boardNumber, source);
    }

    private static PieceSpec[] BuildPrototypePieceSpecs(UnityGameManager.LevelConfig source)
    {
        var pieces = new List<PieceSpec>();
        bool[] consumed = new bool[source.blocks.Count];
        int limousineTarget = source.id <= 30 ? 0 : source.id <= 40 ? 1 : 2;
        int limousineCount = 0;

        while (limousineCount < limousineTarget)
        {
            LimousineCandidate candidate;
            bool found = TryFindAlignedLimousineCandidate(source, consumed, limousineCount, out candidate)
                || TryFindAdjacentLimousineCandidate(source, consumed, limousineCount, out candidate)
                || TryFindColoredFillerLimousineCandidate(source, consumed, limousineCount, out candidate);
            if (!found) break;

            UnityGameManager.BlockData colorBlock = source.blocks[candidate.carIndex];
            pieces.Add(new PieceSpec(
                candidate.leadingRow,
                candidate.leadingCol,
                ConvertColor(colorBlock.color),
                candidate.direction,
                2));
            consumed[candidate.carIndex] = true;
            consumed[candidate.neutralIndex] = true;
            limousineCount++;
        }

        for (int index = 0; index < source.blocks.Count; index++)
        {
            if (consumed[index]) continue;
            UnityGameManager.BlockData block = source.blocks[index];
            pieces.Add(new PieceSpec(
                block.row,
                block.col,
                ConvertColor(block.color),
                ConvertDirection(block.direction)));
        }

        return pieces.ToArray();
    }

    private static bool TryFindAlignedLimousineCandidate(
        UnityGameManager.LevelConfig source,
        bool[] consumed,
        int limousineNumber,
        out LimousineCandidate candidate)
    {
        int count = source.blocks.Count;
        int start = count == 0 ? 0 : (source.id * 7 + limousineNumber * 5) % count;
        for (int offset = 0; offset < count; offset++)
        {
            int carIndex = (start + offset) % count;
            if (consumed[carIndex] || source.blocks[carIndex].color == UnityGameManager.BlockColor.Neutral) continue;

            UnityGameManager.BlockData car = source.blocks[carIndex];
            ExitDirection direction = ConvertDirection(car.direction);
            Vector2Int step = DirectionToGridStep(direction);

            int neutralBehind = FindAvailableNeutral(source, consumed, car.row - step.y, car.col - step.x);
            if (neutralBehind >= 0)
            {
                candidate = new LimousineCandidate(carIndex, neutralBehind, car.row, car.col, direction);
                return true;
            }

            int neutralAhead = FindAvailableNeutral(source, consumed, car.row + step.y, car.col + step.x);
            if (neutralAhead >= 0)
            {
                UnityGameManager.BlockData leadingCell = source.blocks[neutralAhead];
                candidate = new LimousineCandidate(carIndex, neutralAhead, leadingCell.row, leadingCell.col, direction);
                return true;
            }
        }

        candidate = default;
        return false;
    }

    private static bool TryFindAdjacentLimousineCandidate(
        UnityGameManager.LevelConfig source,
        bool[] consumed,
        int limousineNumber,
        out LimousineCandidate candidate)
    {
        int count = source.blocks.Count;
        int start = count == 0 ? 0 : (source.id * 11 + limousineNumber * 3) % count;
        for (int offset = 0; offset < count; offset++)
        {
            int carIndex = (start + offset) % count;
            if (consumed[carIndex] || source.blocks[carIndex].color == UnityGameManager.BlockColor.Neutral) continue;

            UnityGameManager.BlockData car = source.blocks[carIndex];
            int[,] neighbors = { { -1, 0 }, { 1, 0 }, { 0, -1 }, { 0, 1 } };
            for (int neighborIndex = 0; neighborIndex < 4; neighborIndex++)
            {
                int neutralIndex = FindAvailableNeutral(
                    source,
                    consumed,
                    car.row + neighbors[neighborIndex, 0],
                    car.col + neighbors[neighborIndex, 1]);
                if (neutralIndex < 0) continue;

                UnityGameManager.BlockData neutral = source.blocks[neutralIndex];
                ChooseOutwardLimousineDirection(
                    car.row,
                    car.col,
                    neutral.row,
                    neutral.col,
                    source.boardSize,
                    out int leadingRow,
                    out int leadingCol,
                    out ExitDirection direction);
                candidate = new LimousineCandidate(carIndex, neutralIndex, leadingRow, leadingCol, direction);
                return true;
            }
        }

        candidate = default;
        return false;
    }

    private static int FindAvailableNeutral(UnityGameManager.LevelConfig source, bool[] consumed, int row, int col)
    {
        for (int index = 0; index < source.blocks.Count; index++)
        {
            if (consumed[index]) continue;
            UnityGameManager.BlockData block = source.blocks[index];
            if (block.row == row && block.col == col && block.color == UnityGameManager.BlockColor.Neutral)
                return index;
        }

        return -1;
    }

    private static bool TryFindColoredFillerLimousineCandidate(
        UnityGameManager.LevelConfig source,
        bool[] consumed,
        int limousineNumber,
        out LimousineCandidate candidate)
    {
        int count = source.blocks.Count;
        int start = count == 0 ? 0 : (source.id * 13 + limousineNumber * 7) % count;
        for (int offset = 0; offset < count; offset++)
        {
            int carIndex = (start + offset) % count;
            if (consumed[carIndex]) continue;
            UnityGameManager.BlockData car = source.blocks[carIndex];
            if (car.color != UnityGameManager.BlockColor.Red && car.color != UnityGameManager.BlockColor.Green) continue;

            int[,] neighbors = { { -1, 0 }, { 1, 0 }, { 0, -1 }, { 0, 1 } };
            for (int neighborIndex = 0; neighborIndex < 4; neighborIndex++)
            {
                int fillerIndex = FindAvailableOptionalColor(
                    source,
                    consumed,
                    car.row + neighbors[neighborIndex, 0],
                    car.col + neighbors[neighborIndex, 1]);
                if (fillerIndex < 0) continue;

                UnityGameManager.BlockData filler = source.blocks[fillerIndex];
                ChooseOutwardLimousineDirection(
                    car.row,
                    car.col,
                    filler.row,
                    filler.col,
                    source.boardSize,
                    out int leadingRow,
                    out int leadingCol,
                    out ExitDirection direction);
                candidate = new LimousineCandidate(carIndex, fillerIndex, leadingRow, leadingCol, direction);
                return true;
            }
        }

        candidate = default;
        return false;
    }

    private static int FindAvailableOptionalColor(UnityGameManager.LevelConfig source, bool[] consumed, int row, int col)
    {
        for (int index = 0; index < source.blocks.Count; index++)
        {
            if (consumed[index]) continue;
            UnityGameManager.BlockData block = source.blocks[index];
            if (block.row != row || block.col != col) continue;
            if (block.color == UnityGameManager.BlockColor.Blue || block.color == UnityGameManager.BlockColor.Yellow)
                return index;
        }

        return -1;
    }

    private static void ChooseOutwardLimousineDirection(
        int firstRow,
        int firstCol,
        int secondRow,
        int secondCol,
        int boardSize,
        out int leadingRow,
        out int leadingCol,
        out ExitDirection direction)
    {
        if (firstRow == secondRow)
        {
            int leftCol = Mathf.Min(firstCol, secondCol);
            int rightCol = Mathf.Max(firstCol, secondCol);
            bool exitLeft = leftCol <= boardSize - 1 - rightCol;
            leadingRow = firstRow;
            leadingCol = exitLeft ? leftCol : rightCol;
            direction = exitLeft ? ExitDirection.Left : ExitDirection.Right;
            return;
        }

        int topRow = Mathf.Min(firstRow, secondRow);
        int bottomRow = Mathf.Max(firstRow, secondRow);
        bool exitUp = topRow <= boardSize - 1 - bottomRow;
        leadingRow = exitUp ? topRow : bottomRow;
        leadingCol = firstCol;
        direction = exitUp ? ExitDirection.Up : ExitDirection.Down;
    }

    private static PieceColor ConvertColor(UnityGameManager.BlockColor color)
    {
        switch (color)
        {
            case UnityGameManager.BlockColor.Red: return PieceColor.Red;
            case UnityGameManager.BlockColor.Green: return PieceColor.Green;
            case UnityGameManager.BlockColor.Blue: return PieceColor.Blue;
            case UnityGameManager.BlockColor.Yellow: return PieceColor.Yellow;
            default: return PieceColor.Trash;
        }
    }

    private static ExitDirection ConvertDirection(UnityGameManager.Direction direction)
    {
        switch (direction)
        {
            case UnityGameManager.Direction.Down: return ExitDirection.Down;
            case UnityGameManager.Direction.Left: return ExitDirection.Left;
            case UnityGameManager.Direction.Right: return ExitDirection.Right;
            default: return ExitDirection.Up;
        }
    }

    private void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Prototype Camera");
        prototypeCamera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        prototypeCamera.clearFlags = CameraClearFlags.SolidColor;
        prototypeCamera.backgroundColor = new Color(0.08f, 0.13f, 0.22f);
        prototypeCamera.orthographic = true;
        prototypeCamera.allowHDR = true;
        prototypeCamera.nearClipPlane = 0.1f;
        prototypeCamera.farClipPlane = 60f;
        ApplyCameraSettings();

        UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = true;
        cameraData.requiresDepthOption = CameraOverrideOption.On;
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        UniversalRenderPipelineAsset pipeline = UniversalRenderPipeline.asset;
        if (pipeline == null) return;

        for (int index = 0; index < pipeline.rendererDataList.Length; index++)
        {
            if (pipeline.rendererDataList[index] != null && pipeline.rendererDataList[index].name == "Renderer3D")
            {
                cameraData.SetRenderer(index);
                break;
            }
        }
    }

    private void ApplyCameraSettings()
    {
        if (prototypeCamera == null) return;
        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        prototypeCamera.orthographicSize = Mathf.Max(1f, currentLayout.sceneCameraOrthographicSize);
        prototypeCamera.transform.position = currentLayout.sceneCameraPosition;

        Vector3 lookDirection = currentLayout.sceneCameraLookAt - currentLayout.sceneCameraPosition;
        if (lookDirection.sqrMagnitude < 0.0001f) lookDirection = Vector3.forward;
        prototypeCamera.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    private static void CreateLighting()
    {
        // A cool-to-warm ambient gradient keeps the colorful materials readable
        // while leaving enough contrast for contact shadows and SSAO.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.48f, 0.58f, 0.73f);
        RenderSettings.ambientEquatorColor = new Color(0.31f, 0.38f, 0.50f);
        RenderSettings.ambientGroundColor = new Color(0.17f, 0.20f, 0.27f);
        RenderSettings.ambientIntensity = 0.88f;
        RenderSettings.reflectionIntensity = 0.62f;

        GameObject lightObject = new GameObject("Sun");
        Light sun = lightObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.94f, 0.82f);
        sun.intensity = 1.20f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.72f;
        sun.shadowBias = 0.045f;
        sun.shadowNormalBias = 0.28f;
        sun.shadowNearPlane = 0.2f;
        sun.shadowResolution = LightShadowResolution.High;
        lightObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
        RenderSettings.sun = sun;
    }

    private static void CreateVisualEffects()
    {
        GameObject volumeObject = new GameObject("Prototype Visual Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Prototype Polished Visuals";
        volume.sharedProfile = profile;

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.92f);
        bloom.intensity.Override(0.22f);
        bloom.scatter.Override(0.55f);
        bloom.highQualityFiltering.Override(false);

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.04f);
        color.contrast.Override(6f);
        color.saturation.Override(4f);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);
    }

    private void CreateBoard(int size)
    {
        if (boardEnvironmentRoot != null) Destroy(boardEnvironmentRoot);
        boardEnvironmentRoot = new GameObject("Board Environment");
        boardGridCells.Clear();
        CarPrototypeHudLayout currentLayout = GetSceneLayout();

        float boardWidth = size >= 4 ? 7.2f : 6.9f;
        float roadCenterZ = currentLayout.sceneRoadCenterZ;
        float roadDepth = Mathf.Max(1f, currentLayout.sceneRoadDepth);

        // This broad surface sits beneath every visible world-space element. It
        // replaces the flat camera clear color behind the tray and booster area.
        GameObject asphaltGround = CreateEnvironmentBox(
            "Continuous Asphalt Ground",
            currentLayout.sceneAsphaltGroundPosition,
            PositiveScale(currentLayout.sceneAsphaltGroundSize),
            GetAsphaltMaterial(false),
            false);
        asphaltGroundTransform = asphaltGround.transform;

        GameObject roadBoard = CreateEnvironmentBox("Road Board", new Vector3(0f, -0.25f, roadCenterZ), new Vector3(boardWidth, 0.5f, roadDepth), currentLayout.sceneRoadBorderColor);
        roadBoardTransform = roadBoard.transform;
        roadBoardRenderer = roadBoard.GetComponent<Renderer>();
        GameObject roadInset = CreateEnvironmentBox(
            "Road Inset",
            new Vector3(0f, 0.02f, roadCenterZ),
            new Vector3(boardWidth - 0.65f, 0.06f, Mathf.Max(0.35f, roadDepth - 0.65f)),
            GetAsphaltMaterial(true),
            true);
        roadInsetTransform = roadInset.transform;

        float cellSize = size >= 5 ? 1.15f : size == 4 ? 1.37f : 1.7f;
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                Vector3 cell = GetBoardCellPosition(row, col);
                Transform parkingBay = CreatePaintedParkingBay(row, col, cell, cellSize);
                boardGridCells.Add(parkingBay);
            }
        }
    }

    private Transform CreatePaintedParkingBay(int row, int col, Vector3 cellPosition, float cellSize)
    {
        GameObject bayRoot = new GameObject($"Parking Bay {row + 1}-{col + 1}");
        bayRoot.transform.SetParent(boardEnvironmentRoot.transform, true);
        bayRoot.transform.position = cellPosition + new Vector3(0f, -0.18f, 0f);

        // Each bay is painted directly onto the asphalt instead of sitting on a
        // raised square tile. The open lower edge makes the marking read as a
        // real parking stall while keeping the original gameplay cell clear.
        float bayWidth = cellSize * 0.94f;
        float bayDepth = cellSize * 0.94f;
        float lineWidth = Mathf.Clamp(cellSize * 0.052f, 0.065f, 0.09f);
        float paintHeight = 0.025f;
        float paintY = -0.095f;
        float sideX = bayWidth * 0.5f - lineWidth * 0.5f;
        float backZ = bayDepth * 0.5f - lineWidth * 0.5f;
        Material paint = GetParkingLineMaterial();

        CreateParkingPaintStripe("Left Parking Line", bayRoot.transform,
            new Vector3(-sideX, paintY, 0f), new Vector3(lineWidth, paintHeight, bayDepth), paint);
        CreateParkingPaintStripe("Right Parking Line", bayRoot.transform,
            new Vector3(sideX, paintY, 0f), new Vector3(lineWidth, paintHeight, bayDepth), paint);
        CreateParkingPaintStripe("Back Parking Line", bayRoot.transform,
            new Vector3(0f, paintY, backZ), new Vector3(bayWidth, paintHeight, lineWidth), paint);

        return bayRoot.transform;
    }

    private static Transform CreateParkingPaintStripe(string stripeName, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stripe.name = stripeName;
        stripe.transform.SetParent(parent, false);
        stripe.transform.localPosition = localPosition;
        stripe.transform.localScale = scale;
        stripe.GetComponent<Renderer>().sharedMaterial = material;

        Collider stripeCollider = stripe.GetComponent<Collider>();
        if (stripeCollider != null) Destroy(stripeCollider);
        return stripe.transform;
    }

    private static Material GetParkingLineMaterial()
    {
        if (parkingLineMaterial != null) return parkingLineMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Color paintedIvory = new Color(0.97f, 0.94f, 0.84f, 1f);
        parkingLineMaterial = new Material(shader)
        {
            name = "Runtime Parking Line Paint",
            hideFlags = HideFlags.HideAndDontSave,
            color = paintedIvory
        };

        if (parkingLineMaterial.HasProperty("_BaseColor"))
            parkingLineMaterial.SetColor("_BaseColor", paintedIvory);
        if (parkingLineMaterial.HasProperty("_Smoothness"))
            parkingLineMaterial.SetFloat("_Smoothness", 0.12f);
        if (parkingLineMaterial.HasProperty("_Metallic"))
            parkingLineMaterial.SetFloat("_Metallic", 0f);

        return parkingLineMaterial;
    }

    private GameObject CreateEnvironmentBox(string objectName, Vector3 position, Vector3 scale, Color color)
    {
        GameObject box = CreateBox(objectName, position, scale, color);
        box.transform.SetParent(boardEnvironmentRoot.transform, true);
        return box;
    }

    private GameObject CreateEnvironmentBox(string objectName, Vector3 position, Vector3 scale, Material material, bool keepCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = objectName;
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = material;
        box.transform.SetParent(boardEnvironmentRoot.transform, true);

        if (!keepCollider)
        {
            Collider surfaceCollider = box.GetComponent<Collider>();
            if (surfaceCollider != null) Destroy(surfaceCollider);
        }

        return box;
    }

    private Material GetAsphaltMaterial(bool brighterPlayfield)
    {
        Material cachedMaterial = brighterPlayfield ? playfieldAsphaltMaterial : backgroundAsphaltMaterial;
        Color asphaltTint = brighterPlayfield
            ? GetSceneLayout().scenePlayfieldAsphaltColor
            : GetSceneLayout().sceneBackgroundAsphaltColor;
        if (cachedMaterial != null)
        {
            ApplyAsphaltMaterialTint(cachedMaterial, asphaltTint);
            return cachedMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader)
        {
            name = brighterPlayfield ? "Runtime Playfield Asphalt" : "Runtime Background Asphalt",
            hideFlags = HideFlags.HideAndDontSave
        };

        material.color = asphaltTint;

        Texture2D texture = GetAsphaltTexture();
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", brighterPlayfield ? new Vector2(5f, 14f) : new Vector2(7f, 15f));
            material.SetColor("_BaseColor", asphaltTint);
        }
        else if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", brighterPlayfield ? new Vector2(5f, 14f) : new Vector2(7f, 15f));
        }

        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

        if (brighterPlayfield) playfieldAsphaltMaterial = material;
        else backgroundAsphaltMaterial = material;
        return material;
    }

    private static void ApplyAsphaltMaterialTint(Material material, Color color)
    {
        if (material == null) return;
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
    }

    private CarPrototypeHudLayout GetSceneLayout()
    {
        if (sceneLayout == null) sceneLayout = CarPrototypeHudLayout.LoadOrDefault();
        return sceneLayout;
    }

    private static Vector3 PositiveScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(scale.x)),
            Mathf.Max(0.01f, Mathf.Abs(scale.y)),
            Mathf.Max(0.01f, Mathf.Abs(scale.z)));
    }

    private static Texture2D GetAsphaltTexture()
    {
        if (asphaltTexture != null) return asphaltTexture;

        const int textureSize = 64;
        asphaltTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, true)
        {
            name = "Runtime Asphalt Grain",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                uint hash = (uint)(x * 374761393 + y * 668265263);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                float fineGrain = ((hash & 1023u) / 1023f - 0.5f) * 0.18f;
                float broadGrain = Mathf.Sin((x + y * 0.71f) * 0.42f) * 0.025f;
                float value = Mathf.Clamp01(0.86f + fineGrain + broadGrain);
                pixels[y * textureSize + x] = new Color(value, value * 0.99f, value * 0.97f, 1f);
            }
        }

        asphaltTexture.SetPixels(pixels);
        asphaltTexture.Apply(true, false);
        return asphaltTexture;
    }

    private void CreateMatchTray()
    {
        GameObject trayRoot = new GameObject("Match Tray Road Markings");
        matchTrayRootTransform = trayRoot.transform;

        Material roadPaint = GetParkingLineMaterial();
        Vector3 placeholderScale = new Vector3(0.08f, 0.025f, 0.08f);
        matchTrayTopLine = CreateParkingPaintStripe("Tray Top Road Line", trayRoot.transform, Vector3.zero, placeholderScale, roadPaint);
        matchTrayBottomLine = CreateParkingPaintStripe("Tray Bottom Road Line", trayRoot.transform, Vector3.zero, placeholderScale, roadPaint);
        matchTrayLeftLine = CreateParkingPaintStripe("Tray Left Road Line", trayRoot.transform, Vector3.zero, placeholderScale, roadPaint);
        matchTrayRightLine = CreateParkingPaintStripe("Tray Right Road Line", trayRoot.transform, Vector3.zero, placeholderScale, roadPaint);

        for (int index = 0; index < trayDividerLines.Length; index++)
        {
            Transform divider = CreateParkingPaintStripe($"Tray Road Divider {index + 1}", trayRoot.transform, Vector3.zero, placeholderScale, roadPaint);
            trayDividerLines[index] = divider;
            trayDividerLineRenderers[index] = divider.GetComponent<Renderer>();
        }

        UpdateMatchTrayRoadMarkings();
    }

    private void CreateParkingSlot()
    {
        GameObject parkingRoot = new GameObject("Side Parking Bay Road Markings");
        sideParkingRootTransform = parkingRoot.transform;

        Material roadPaint = GetParkingLineMaterial();
        Vector3 placeholderScale = new Vector3(0.08f, 0.018f, 0.08f);
        sideParkingTopLine = CreateParkingPaintStripe("Side Bay Top Line", parkingRoot.transform, Vector3.zero, placeholderScale, roadPaint);
        sideParkingBottomLine = CreateParkingPaintStripe("Side Bay Bottom Line", parkingRoot.transform, Vector3.zero, placeholderScale, roadPaint);
        sideParkingLeftLine = CreateParkingPaintStripe("Side Bay Left Line", parkingRoot.transform, Vector3.zero, placeholderScale, roadPaint);
        sideParkingRightLine = CreateParkingPaintStripe("Side Bay Right Line", parkingRoot.transform, Vector3.zero, placeholderScale, roadPaint);
        UpdateParkingHighlight();
    }

    private void UpdateTrayHighlights()
    {
        UpdateMatchTrayRoadMarkings();
    }

    private void UpdateMatchTrayRoadMarkings()
    {
        if (matchTrayRootTransform == null || matchTrayTopLine == null) return;

        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        int visibleCapacity = Mathf.Clamp(trayCapacity, 1, MaximumTraySlots);
        float slotSpacing = GetMatchTraySlotSpacing(visibleCapacity);
        Vector2 baySize = GetMatchTrayBaySize(visibleCapacity);
        float bayWidth = Mathf.Max(0.45f, Mathf.Abs(baySize.x));
        float bayDepth = Mathf.Max(0.55f, Mathf.Abs(baySize.y));
        float lineWidth = Mathf.Clamp(currentLayout.sceneParkingLineWidth, 0.025f, Mathf.Min(bayWidth, bayDepth) * 0.22f);
        const float lineHeight = 0.018f;
        const float paintY = 0.068f;

        matchTrayRootTransform.position = new Vector3(currentLayout.sceneMatchTrayPosition.x, 0f, currentLayout.sceneMatchTrayPosition.y);
        float trayWidth = (visibleCapacity - 1) * slotSpacing + bayWidth;
        float sideX = trayWidth * 0.5f - lineWidth * 0.5f;
        float edgeZ = bayDepth * 0.5f - lineWidth * 0.5f;

        SetTrayRoadLine(matchTrayTopLine, new Vector3(0f, paintY, edgeZ), new Vector3(trayWidth, lineHeight, lineWidth));
        SetTrayRoadLine(matchTrayBottomLine, new Vector3(0f, paintY, -edgeZ), new Vector3(trayWidth, lineHeight, lineWidth));
        SetTrayRoadLine(matchTrayLeftLine, new Vector3(-sideX, paintY, 0f), new Vector3(lineWidth, lineHeight, bayDepth));
        SetTrayRoadLine(matchTrayRightLine, new Vector3(sideX, paintY, 0f), new Vector3(lineWidth, lineHeight, bayDepth));

        for (int index = 0; index < trayDividerLines.Length; index++)
        {
            bool visible = index < visibleCapacity - 1;
            if (trayDividerLineRenderers[index] != null)
                trayDividerLineRenderers[index].enabled = visible;
            if (!visible || trayDividerLines[index] == null) continue;

            float center = (visibleCapacity - 1) * 0.5f;
            float dividerX = (index + 0.5f - center) * slotSpacing;
            SetTrayRoadLine(trayDividerLines[index], new Vector3(dividerX, paintY, 0f), new Vector3(lineWidth, lineHeight, bayDepth));
        }
    }

    private static void SetTrayRoadLine(Transform line, Vector3 localPosition, Vector3 localScale)
    {
        if (line == null) return;
        line.localPosition = localPosition;
        line.localScale = localScale;
    }

    private void UpdateParkingHighlight()
    {
        UpdateSideParkingRoadMarkings();
    }

    private void UpdateSideParkingRoadMarkings()
    {
        if (sideParkingRootTransform == null || sideParkingTopLine == null) return;

        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        float bayWidth = Mathf.Max(0.55f, Mathf.Abs(currentLayout.sceneSideParkingSize.x));
        float bayDepth = Mathf.Max(0.65f, Mathf.Abs(currentLayout.sceneSideParkingSize.y));
        float lineWidth = Mathf.Clamp(currentLayout.sceneParkingLineWidth, 0.025f, Mathf.Min(bayWidth, bayDepth) * 0.22f);
        float hatchSpacing = Mathf.Clamp(currentLayout.sceneSideParkingHatchSpacing, 0.12f, 0.65f);
        const float lineHeight = 0.018f;
        const float paintY = 0.068f;

        sideParkingRootTransform.position = new Vector3(currentLayout.sceneSideParkingPosition.x, 0f, currentLayout.sceneSideParkingPosition.y);
        float sideX = bayWidth * 0.5f - lineWidth * 0.5f;
        float edgeZ = bayDepth * 0.5f - lineWidth * 0.5f;
        SetTrayRoadLine(sideParkingTopLine, new Vector3(0f, paintY, edgeZ), new Vector3(bayWidth, lineHeight, lineWidth));
        SetTrayRoadLine(sideParkingBottomLine, new Vector3(0f, paintY, -edgeZ), new Vector3(bayWidth, lineHeight, lineWidth));
        SetTrayRoadLine(sideParkingLeftLine, new Vector3(-sideX, paintY, 0f), new Vector3(lineWidth, lineHeight, bayDepth));
        SetTrayRoadLine(sideParkingRightLine, new Vector3(sideX, paintY, 0f), new Vector3(lineWidth, lineHeight, bayDepth));

        float innerMargin = Mathf.Max(0.08f, lineWidth * 1.8f);
        float halfWidth = Mathf.Max(0.04f, bayWidth * 0.5f - innerMargin);
        float halfDepth = Mathf.Max(0.04f, bayDepth * 0.5f - innerMargin);
        float inverseRootTwo = 1f / Mathf.Sqrt(2f);
        Vector3 hatchDirection = new Vector3(inverseRootTwo, 0f, inverseRootTwo);
        Vector3 hatchNormal = new Vector3(inverseRootTwo, 0f, -inverseRootTwo);
        float normalExtent = (halfWidth + halfDepth) * inverseRootTwo;
        int hatchIndex = 0;

        for (float offset = -normalExtent; offset <= normalExtent + 0.001f; offset += hatchSpacing)
        {
            Vector3 origin = hatchNormal * offset;
            float startT = Mathf.Max(
                (-halfWidth - origin.x) / hatchDirection.x,
                (-halfDepth - origin.z) / hatchDirection.z);
            float endT = Mathf.Min(
                (halfWidth - origin.x) / hatchDirection.x,
                (halfDepth - origin.z) / hatchDirection.z);
            if (endT - startT < 0.08f) continue;

            Transform hatch = GetOrCreateSideParkingHatchLine(hatchIndex);
            Vector3 start = origin + hatchDirection * startT;
            Vector3 end = origin + hatchDirection * endT;
            Vector3 midpoint = (start + end) * 0.5f;
            midpoint.y = paintY;
            hatch.localPosition = midpoint;
            hatch.localScale = new Vector3(lineWidth, lineHeight, Vector3.Distance(start, end));
            hatch.localRotation = Quaternion.Euler(0f, 45f, 0f);
            Renderer hatchRenderer = hatch.GetComponent<Renderer>();
            if (hatchRenderer != null) hatchRenderer.enabled = true;
            hatchIndex++;
        }

        for (int index = hatchIndex; index < sideParkingHatchLines.Count; index++)
        {
            Renderer hatchRenderer = sideParkingHatchLines[index].GetComponent<Renderer>();
            if (hatchRenderer != null) hatchRenderer.enabled = false;
        }
    }

    private Transform GetOrCreateSideParkingHatchLine(int index)
    {
        while (sideParkingHatchLines.Count <= index)
        {
            int lineNumber = sideParkingHatchLines.Count + 1;
            Transform hatch = CreateParkingPaintStripe($"Side Bay Diagonal Hatch {lineNumber}", sideParkingRootTransform,
                Vector3.zero, new Vector3(0.05f, 0.018f, 0.1f), GetParkingLineMaterial());
            sideParkingHatchLines.Add(hatch);
        }

        return sideParkingHatchLines[index];
    }

    private void RepositionTrayCars()
    {
        for (int index = 0; index < trayPieces.Count; index++)
            trayPieces[index].SetTrayPose(GetTraySlotPosition(index));
    }

    private IEnumerator AnimateTrayLayout(float duration, bool skipCarsDrivingFromBoard = false)
    {
        int animatedCars = 0;
        for (int index = 0; index < trayPieces.Count; index++)
        {
            if (skipCarsDrivingFromBoard && boardCarsCurrentlyDriving.Contains(trayPieces[index]))
                continue;

            StartCoroutine(trayPieces[index].DriveToTraySlot(GetTraySlotPosition(index), duration));
            animatedCars++;
        }

        if (animatedCars > 0)
            yield return new WaitForSeconds(duration);
    }

    private void TryParkTrayPiece(CarPuzzlePiece selectedPiece)
    {
        int trayIndex = trayPieces.IndexOf(selectedPiece);
        if (trayIndex < 0) return;

        if (!CanEnterSideParking())
        {
            selectedPiece.Reject();
            RefreshHud();
            return;
        }

        isAnimating = true;
        RegisterParkingEntry();
        if (parkedPiece == null)
        {
            trayPieces.RemoveAt(trayIndex);
            parkedPiece = selectedPiece;
            UpdateTrayHighlights();
            UpdateParkingHighlight();
            StartCoroutine(DriveTrayPieceToParking(selectedPiece));
            return;
        }

        CarPuzzlePiece previouslyParked = parkedPiece;
        trayPieces[trayIndex] = previouslyParked;
        parkedPiece = selectedPiece;
        UpdateTrayHighlights();
        UpdateParkingHighlight();
        StartCoroutine(AnimateParkingSwap(selectedPiece, previouslyParked, trayIndex));
    }

    private void TryReturnParkedPiece()
    {
        if (parkedPiece == null) return;

        isAnimating = true;
        if (trayPieces.Count < trayCapacity)
        {
            CarPuzzlePiece returningPiece = parkedPiece;
            trayPieces.Add(returningPiece);
            parkedPiece = null;
            UpdateTrayHighlights();
            UpdateParkingHighlight();
            StartCoroutine(DriveParkingPieceToTray(returningPiece, trayPieces.Count - 1));
            return;
        }

        int swapIndex = FindTraySwapIndexForParkingMatch();
        if (swapIndex < 0)
        {
            parkedPiece.Reject();
            RegisterWrongMove();
            isAnimating = false;
            return;
        }

        if (!CanEnterSideParking())
        {
            parkedPiece.Reject();
            isAnimating = false;
            RefreshHud();
            return;
        }

        CarPuzzlePiece trayPiece = trayPieces[swapIndex];
        CarPuzzlePiece returningParkedPiece = parkedPiece;
        trayPieces[swapIndex] = returningParkedPiece;
        parkedPiece = trayPiece;
        RegisterParkingEntry();
        UpdateTrayHighlights();
        UpdateParkingHighlight();
        StartCoroutine(AnimateReturnParkingSwap(returningParkedPiece, trayPiece, swapIndex));
    }

    private IEnumerator DriveTrayPieceToParking(CarPuzzlePiece piece)
    {
        Vector3 parkingTarget = GetParkingSlotPosition();
        yield return StartCoroutine(DriveTrayPieceToLowerApproach(piece, parkingTarget.x));

        // The selected car is now fully below the tray row, so the remaining
        // cars can safely slide into their new bays without crossing through it.
        StartCoroutine(AnimateTrayLayout(0.24f));
        yield return StartCoroutine(FinishParkingEntry(piece, parkingTarget));
        isAnimating = false;
    }

    private IEnumerator AnimateParkingSwap(CarPuzzlePiece trayPiece, CarPuzzlePiece previouslyParked, int trayIndex)
    {
        Vector3 parkingTarget = GetParkingSlotPosition();
        yield return StartCoroutine(DriveTrayPieceToLowerApproach(trayPiece, BoardRouteLaneX));
        yield return StartCoroutine(DriveParkingPieceToTraySlot(previouslyParked, trayIndex));
        yield return StartCoroutine(DriveLowerLanePieceToParking(trayPiece, parkingTarget));
        CheckTrayMatches();
    }

    private IEnumerator DriveParkingPieceToTray(CarPuzzlePiece piece, int trayIndex)
    {
        yield return StartCoroutine(DriveParkingPieceToOpenTraySlot(piece, trayIndex));
        CheckTrayMatches();
    }

    private IEnumerator DriveParkingPieceToOpenTraySlot(CarPuzzlePiece piece, int trayIndex)
    {
        Vector3 trayTarget = GetTraySlotPosition(trayIndex);
        float parkingTravelZ = GetParkingTravelZ();
        Vector3 lowerTurn = new Vector3(piece.transform.position.x, trayTarget.y, parkingTravelZ);
        Vector3 belowTarget = new Vector3(trayTarget.x, trayTarget.y, parkingTravelZ);

        // When there is an empty tray bay, stay completely below the occupied
        // row and finish every turn before approaching the occupied row.
        var lowerRoute = new List<Vector3> { lowerTurn, belowTarget };
        yield return StartCoroutine(DriveRoundedRouteToFinalApproach(piece, lowerRoute, OutsideCarDriveSpeed, 0.48f));
        yield return StartCoroutine(DriveRouteSegment(piece, belowTarget, OutsideCarDriveSpeed));
        yield return StartCoroutine(piece.PrepareForTrayEntry(0.12f));

        // The car is now facing the final tray direction and already has its
        // tray scale, so it backs straight into the empty bay without swinging
        // its body through either neighboring car.
        float finalDuration = Mathf.Clamp(Vector3.Distance(piece.transform.position, trayTarget) / CarDriveSpeed, 0.12f, 0.24f);
        yield return StartCoroutine(piece.DriveToTraySlot(trayTarget, finalDuration));
    }

    private IEnumerator DriveParkingPieceToTraySlot(CarPuzzlePiece piece, int trayIndex)
    {
        Vector3 trayTarget = GetTraySlotPosition(trayIndex);
        Vector3 parkingTarget = GetParkingSlotPosition();
        float trayApproachZ = GetTrayApproachZ();
        float routeHeight = trayTarget.y;
        Vector3 parkingSide = new Vector3(-BoardRouteLaneX, routeHeight, parkingTarget.z);
        Vector3 traySide = new Vector3(-BoardRouteLaneX, routeHeight, trayApproachZ);
        Vector3 trayApproach = new Vector3(trayTarget.x, routeHeight, trayApproachZ);

        // Never cut diagonally through occupied tray bays. Leave the parking
        // space along the outside aisle, cross above the tray, then enter the
        // assigned bay from its open end.
        var route = new List<Vector3> { parkingSide, traySide, trayApproach, trayTarget };
        yield return StartCoroutine(DriveRoundedRouteToFinalApproach(piece, route, OutsideCarDriveSpeed, 0.62f));
        float finalDuration = Mathf.Clamp(Vector3.Distance(piece.transform.position, trayTarget) / CarDriveSpeed, 0.1f, 0.2f);
        yield return StartCoroutine(piece.DriveToTraySlot(trayTarget, finalDuration));
    }

    private IEnumerator AnimateReturnParkingSwap(CarPuzzlePiece returningPiece, CarPuzzlePiece outgoingTrayPiece, int trayIndex)
    {
        Vector3 parkingTarget = GetParkingSlotPosition();
        Vector3 staging = new Vector3(-BoardRouteLaneX, parkingTarget.y, parkingTarget.z);
        yield return StartCoroutine(DriveRouteSegment(returningPiece, staging, OutsideCarDriveSpeed));
        yield return StartCoroutine(DriveTrayPieceToLowerApproach(outgoingTrayPiece, BoardRouteLaneX));
        yield return StartCoroutine(DriveLowerLanePieceToParking(outgoingTrayPiece, parkingTarget));
        yield return StartCoroutine(DriveParkingPieceToTraySlot(returningPiece, trayIndex));
        CheckTrayMatches();
    }

    private IEnumerator DriveTrayPieceToLowerApproach(CarPuzzlePiece piece, float holdingX)
    {
        float routeHeight = GetParkingSlotPosition().y;
        float parkingTravelZ = GetParkingTravelZ();
        Vector3 lowerTurn = new Vector3(piece.transform.position.x, routeHeight, parkingTravelZ);
        Vector3 holding = new Vector3(holdingX, routeHeight, parkingTravelZ);
        var route = new List<Vector3> { lowerTurn, holding };
        yield return StartCoroutine(DriveRoundedRouteToFinalApproach(piece, route, OutsideCarDriveSpeed, 0.48f));
        yield return StartCoroutine(DriveRouteSegment(piece, holding, OutsideCarDriveSpeed));
    }

    private IEnumerator DriveLowerLanePieceToParking(CarPuzzlePiece piece, Vector3 parkingTarget)
    {
        Vector3 belowParking = new Vector3(parkingTarget.x, parkingTarget.y, GetParkingTravelZ());
        yield return StartCoroutine(DriveRouteSegment(piece, belowParking, OutsideCarDriveSpeed));
        yield return StartCoroutine(FinishParkingEntry(piece, parkingTarget));
    }

    private IEnumerator FinishParkingEntry(CarPuzzlePiece piece, Vector3 parkingTarget)
    {
        yield return StartCoroutine(piece.PrepareForParkingEntry(0.12f));
        float finalDuration = Mathf.Clamp(Vector3.Distance(piece.transform.position, parkingTarget) / CarDriveSpeed, 0.1f, 0.2f);
        yield return StartCoroutine(piece.DriveToParkingSlot(parkingTarget, finalDuration));
    }

    private int FindTraySwapIndexForParkingMatch()
    {
        if (parkedPiece == null) return -1;

        if (parkedPiece.PieceColor == PieceColor.Blue)
        {
            int target = activeBlueTarget;
            if (target <= 0) return -1;

            for (int index = 0; index < trayPieces.Count; index++)
            {
                if (trayPieces[index].PieceColor == PieceColor.Blue) continue;

                int adjacentBlueCount = 1;
                for (int left = index - 1; left >= 0 && trayPieces[left].PieceColor == PieceColor.Blue; left--) adjacentBlueCount++;
                for (int right = index + 1; right < trayPieces.Count && trayPieces[right].PieceColor == PieceColor.Blue; right++) adjacentBlueCount++;
                if (adjacentBlueCount >= target) return index;
            }

            return -1;
        }

        int matchingCars = 0;
        for (int index = 0; index < trayPieces.Count; index++)
        {
            if (trayPieces[index].PieceColor == parkedPiece.PieceColor)
                matchingCars++;
        }

        if (matchingCars < GetMatchTarget(parkedPiece.PieceColor) - 1) return -1;

        for (int index = 0; index < trayPieces.Count; index++)
        {
            if (trayPieces[index].PieceColor != parkedPiece.PieceColor)
                return index;
        }

        return -1;
    }

    public void UseExtraSlot()
    {
        if (!CanUseExtraSlot) return;

        trayCapacity++;
        extraSlotUsed = true;
        lastMovedPiece = null;
        RepositionTrayCars();
        UpdateTrayHighlights();
        RefreshHud();
    }

    public void RestartCurrentBoard()
    {
        LoadLevel(levelIndex);
    }

    public void UseUndo()
    {
        if (!CanUseUndo) return;

        isAnimating = true;
        CarPuzzlePiece undoPiece = lastMovedPiece;
        lastMovedPiece = null;
        trayPieces.Remove(undoPiece);
        if (parkedPiece == undoPiece) parkedPiece = null;
        if (!boardPieces.Contains(undoPiece)) boardPieces.Add(undoPiece);
        undoPiece.SetVisible(true);
        undoPiece.SetBoardPose(GetBoardPiecePosition(undoPiece));
        UpdateTrayHighlights();
        UpdateParkingHighlight();
        StartCoroutine(FinishUndoLayout());
    }

    private IEnumerator FinishUndoLayout()
    {
        yield return StartCoroutine(AnimateTrayLayout(0.22f));
        isAnimating = false;
        RefreshHud();
    }

    public void TogglePause(bool paused)
    {
        Time.timeScale = paused ? 0f : 1f;
    }

    private void RegisterWrongMove()
    {
        if (hearts <= 0) return;

        hearts--;
        RefreshHud();
        if (hearts == 0 && hud != null)
            hud.ShowDefeat();
    }

    private void RefreshHud()
    {
        if (hud != null)
            hud.Refresh(BoardNumber, redCleared, greenCleared, blueCleared, yellowCleared, activeMatchTarget, activeBlueTarget, activeYellowTarget, hearts, trayCapacity, extraSlotUsed);
    }

    private bool CanEnterSideParking()
    {
        return activeExperimentalRules == null
            || activeExperimentalRules.parkingUseLimit < 0
            || parkingUses < activeExperimentalRules.parkingUseLimit;
    }

    private void RegisterParkingEntry()
    {
        if (activeExperimentalRules != null && activeExperimentalRules.parkingUseLimit > 0)
            parkingUses++;
        RefreshHud();
    }

    private bool IsColorCleared(PieceColor color)
    {
        switch (color)
        {
            case PieceColor.Red: return redCleared >= activeMatchTarget;
            case PieceColor.Green: return greenCleared >= activeMatchTarget;
            case PieceColor.Blue: return activeBlueTarget == 0 || blueCleared >= activeBlueTarget;
            case PieceColor.Yellow: return activeYellowTarget == 0 || yellowCleared >= activeYellowTarget;
            default: return false;
        }
    }

    private bool TryGetNextOrderedColor(out PieceColor color)
    {
        color = PieceColor.Trash;
        if (activeExperimentalRules == null || activeExperimentalRules.requiredColorOrder.Length == 0)
            return false;

        for (int index = 0; index < activeExperimentalRules.requiredColorOrder.Length; index++)
        {
            PieceColor candidate = activeExperimentalRules.requiredColorOrder[index];
            if (IsColorCleared(candidate)) continue;
            color = candidate;
            return true;
        }

        return false;
    }

    private void ApplyExperimentalLocks()
    {
        if (activeExperimentalRules == null) return;

        for (int ruleIndex = 0; ruleIndex < activeExperimentalRules.locks.Length; ruleIndex++)
        {
            ExperimentalLockRule rule = activeExperimentalRules.locks[ruleIndex];
            CarPuzzlePiece lockedPiece = null;
            for (int pieceIndex = 0; pieceIndex < boardPieces.Count; pieceIndex++)
            {
                CarPuzzlePiece candidate = boardPieces[pieceIndex];
                if (candidate.IsTrash || !candidate.OccupiesCell(rule.row, rule.col)) continue;
                lockedPiece = candidate;
                break;
            }

            if (lockedPiece == null)
            {
                Debug.LogWarning($"Temporary Level {BoardNumber} could not find its lock at {rule.row},{rule.col}.");
                continue;
            }

            activeExperimentalLocks.Add(new ActiveExperimentalLock(lockedPiece, rule.unlockAfterColor));
            lockedPiece.SetLocked(!IsColorCleared(rule.unlockAfterColor), rule.unlockAfterColor);
        }
    }

    private void UpdateExperimentalLocks()
    {
        for (int index = 0; index < activeExperimentalLocks.Count; index++)
        {
            ActiveExperimentalLock activeLock = activeExperimentalLocks[index];
            if (activeLock.piece != null)
                activeLock.piece.SetLocked(!IsColorCleared(activeLock.unlockAfterColor), activeLock.unlockAfterColor);
        }
    }

    private string BuildExperimentalRuleStatus()
    {
        if (activeExperimentalRules == null) return string.Empty;

        string status = activeExperimentalRules.planningHint;
        if (TryGetNextOrderedColor(out PieceColor nextColor))
            status = $"NEXT: {ColorName(nextColor)}";

        if (activeExperimentalRules.parkingUseLimit > 0)
        {
            int remaining = Mathf.Max(0, activeExperimentalRules.parkingUseLimit - parkingUses);
            status += $"   PARK: {remaining}/{activeExperimentalRules.parkingUseLimit}";
        }

        int lockedCount = 0;
        for (int index = 0; index < activeExperimentalLocks.Count; index++)
        {
            if (activeExperimentalLocks[index].piece != null && activeExperimentalLocks[index].piece.IsLocked)
                lockedCount++;
        }
        if (lockedCount > 0) status += $"   LOCKED: {lockedCount}";
        return $"SAMPLE • {activeExperimentalRules.title}\n{status}";
    }

    private static string ColorName(PieceColor color)
    {
        return color.ToString().ToUpperInvariant();
    }

    private static ExperimentalRuleSet CreateExperimentalRules(int boardNumber)
    {
        switch (boardNumber)
        {
            case 61:
                return new ExperimentalRuleSet("THE LONG WAY OUT", "TRACE THE EXIT CHAIN");
            case 62:
                return new ExperimentalRuleSet("FOUR IS NOT FIVE", "FIND ALL 5 BEFORE COMMITTING");
            case 63:
                return new ExperimentalRuleSet("VALET EXCHANGE", "PLAN EACH SIDE-BAY MOVE", 5);
            case 64:
                return new ExperimentalRuleSet(
                    "KEY COLOR",
                    "CLEAR GREEN TO UNLOCK RED",
                    locks: new[] { new ExperimentalLockRule(4, 4, PieceColor.Green) });
            case 65:
                return new ExperimentalRuleSet(
                    "TRAFFIC SCHEDULE",
                    "FOLLOW THE COLOR ORDER",
                    requiredColorOrder: new[] { PieceColor.Red, PieceColor.Green, PieceColor.Blue, PieceColor.Yellow });
            case 66:
                return new ExperimentalRuleSet(
                    "RUSH-HOUR FINALE",
                    "COMBINE EVERY RULE",
                    4,
                    new[] { PieceColor.Green, PieceColor.Red, PieceColor.Yellow, PieceColor.Blue },
                    new[]
                    {
                        new ExperimentalLockRule(0, 2, PieceColor.Green),
                        new ExperimentalLockRule(2, 4, PieceColor.Red)
                    });
            default:
                return null;
        }
    }

    private Vector3 GetBoardCellPosition(int row, int col)
    {
        float spacing = GetBoardSpacing();
        float center = (activeBoardSize - 1) * 0.5f;
        return new Vector3((col - center) * spacing, 0.35f, GetSceneLayout().sceneBoardFirstRowZ - row * spacing);
    }

    private float GetBoardSpacing()
    {
        if (activeBoardSize >= 5) return 1.30f;
        return activeBoardSize >= 4 ? 1.55f : 1.92f;
    }

    private Vector3 GetBoardPiecePosition(CarPuzzlePiece piece)
    {
        return GetBoardPiecePosition(piece.Row, piece.Col, piece.Direction, piece.CellLength);
    }

    private Vector3 GetBoardPiecePosition(int leadingRow, int leadingCol, ExitDirection direction, int cellLength)
    {
        Vector3 leadingPosition = GetBoardCellPosition(leadingRow, leadingCol);
        if (cellLength <= 1) return leadingPosition;

        Vector2Int step = DirectionToGridStep(direction);
        int trailingRow = leadingRow - step.y * (cellLength - 1);
        int trailingCol = leadingCol - step.x * (cellLength - 1);
        Vector3 trailingPosition = GetBoardCellPosition(trailingRow, trailingCol);
        return (leadingPosition + trailingPosition) * 0.5f;
    }

    private float GetBoardPieceScale()
    {
        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        float scale = activeBoardSize >= 5
            ? currentLayout.scenePieceScale5x5
            : activeBoardSize >= 4 ? currentLayout.scenePieceScale4x4 : currentLayout.scenePieceScale3x3;
        return Mathf.Clamp(scale, 0.3f, 1.2f);
    }

    private float GetOffBoardPieceScale(int cellLength)
    {
        if (cellLength > 1)
            return Mathf.Clamp(GetSceneLayout().sceneLimousineOffBoardScale, 0.25f, 0.9f);
        return activeBoardSize >= 4 ? 0.76f : 1f;
    }

    private Vector3 GetTraySlotPosition(int index)
    {
        return GetTraySlotPosition(index, trayCapacity);
    }

    private float GetMatchTrayZ()
    {
        return GetSceneLayout().sceneMatchTrayPosition.y;
    }

    private float GetTrayApproachZ()
    {
        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        float halfDepth = Mathf.Max(0.55f, Mathf.Abs(GetMatchTrayBaySize(trayCapacity).y)) * 0.5f;
        return currentLayout.sceneMatchTrayPosition.y - halfDepth - 0.5f;
    }

    private float GetParkingTravelZ()
    {
        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        float halfDepth = Mathf.Max(0.65f, Mathf.Abs(currentLayout.sceneSideParkingSize.y)) * 0.5f;
        return currentLayout.sceneSideParkingPosition.y - halfDepth - 0.45f;
    }

    private Vector3 GetTraySlotPosition(int index, int capacity)
    {
        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        float spacing = GetMatchTraySlotSpacing(capacity);
        float center = (capacity - 1) * 0.5f;
        return new Vector3(
            currentLayout.sceneMatchTrayPosition.x + (index - center) * spacing,
            0.34f,
            currentLayout.sceneMatchTrayPosition.y);
    }

    private float GetMatchTraySlotSpacing(int capacity)
    {
        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        float configuredSpacing = capacity >= MaximumTraySlots
            ? currentLayout.sceneMatchTraySlotSpacing5
            : currentLayout.sceneMatchTraySlotSpacing;
        return Mathf.Max(0.5f, configuredSpacing);
    }

    private Vector2 GetMatchTrayBaySize(int capacity)
    {
        CarPrototypeHudLayout currentLayout = GetSceneLayout();
        return capacity >= MaximumTraySlots
            ? currentLayout.sceneMatchTrayBaySize5
            : currentLayout.sceneMatchTrayBaySize;
    }

    private Vector3 GetParkingSlotPosition()
    {
        Vector2 parkingPosition = GetSceneLayout().sceneSideParkingPosition;
        return new Vector3(parkingPosition.x, 0.34f, parkingPosition.y);
    }

    internal static Vector2Int DirectionToGridStep(ExitDirection direction)
    {
        switch (direction)
        {
            case ExitDirection.Up: return new Vector2Int(0, -1);
            case ExitDirection.Down: return new Vector2Int(0, 1);
            case ExitDirection.Left: return new Vector2Int(-1, 0);
            default: return new Vector2Int(1, 0);
        }
    }

    internal static Vector3 DirectionToWorld(ExitDirection direction)
    {
        switch (direction)
        {
            case ExitDirection.Up: return Vector3.forward;
            case ExitDirection.Down: return Vector3.back;
            case ExitDirection.Left: return Vector3.left;
            default: return Vector3.right;
        }
    }

    internal static GameObject CreateBox(string objectName, Vector3 position, Vector3 scale, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = objectName;
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = CreateMaterial(color);
        return box;
    }

    internal static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material material = new Material(shader);
        material.color = color;
        return material;
    }
}

public sealed class CarPuzzlePiece : MonoBehaviour
{
    private const string ImportedPoliceCarResourcePath = "CarModels/PoliceCar/policecar";
    private const string ImportedPoliceCarTexturePath = "CarModels/PoliceCar/carPolice";
    private const string ImportedRedCarResourcePath = "CarModels/RedCar/redcar";
    private const string ImportedRedCarTexturePath = "CarModels/RedCar/Material-color";
    private const string ImportedRedCarMetallicPath = "CarModels/RedCar/Material-metallic";
    private const string ImportedGreenCarResourcePath = "CarModels/GreenCar/greencar";
    private const string ImportedGreenCarTexturePath = "CarModels/GreenCar/Material-color";
    private const string ImportedGreenCarMetallicPath = "CarModels/GreenCar/Material-metallic";

    private static Material importedRedCarMaterial;
    private static Material importedGreenCarMaterial;
    private static Material importedPoliceCarMaterial;

    private readonly List<Transform> wheels = new List<Transform>();
    private readonly List<GameObject> lockVisuals = new List<GameObject>();
    private Vector3 visualBaseScale = Vector3.one;
    private float boardVisualScale = 1f;
    private float offBoardVisualScale = 1f;
    private bool usesImportedRedCar;
    private bool usesImportedGreenCar;
    private bool usesLimousineVisual;
    private PoliceLightController policeLightController;
    private Collider rootCollider;

    internal int Row { get; private set; }
    internal int Col { get; private set; }
    internal int CellLength { get; private set; } = 1;
    internal bool IsTrash { get; private set; }
    internal CarPrototype3D.PieceColor PieceColor { get; private set; }
    internal CarPrototype3D.ExitDirection Direction { get; private set; }
    internal bool IsTouchable => rootCollider != null && rootCollider.enabled && transform.localScale.sqrMagnitude > 0.0001f;
    internal bool IsLocked { get; private set; }

    internal void SetLocked(bool locked, CarPrototype3D.PieceColor unlockAfterColor)
    {
        if (lockVisuals.Count == 0) BuildLockBadge(unlockAfterColor);
        IsLocked = locked;
        for (int index = 0; index < lockVisuals.Count; index++)
            if (lockVisuals[index] != null) lockVisuals[index].SetActive(locked);
    }

    private void BuildLockBadge(CarPrototype3D.PieceColor unlockAfterColor)
    {
        Color keyColor;
        switch (unlockAfterColor)
        {
            case CarPrototype3D.PieceColor.Red: keyColor = new Color(0.96f, 0.13f, 0.16f); break;
            case CarPrototype3D.PieceColor.Green: keyColor = new Color(0.18f, 0.88f, 0.18f); break;
            case CarPrototype3D.PieceColor.Blue: keyColor = new Color(0.12f, 0.58f, 1f); break;
            default: keyColor = new Color(1f, 0.78f, 0.12f); break;
        }

        lockVisuals.Add(CreateLocalBox("Color Lock Body", new Vector3(0f, 1.16f, -0.08f), new Vector3(0.58f, 0.12f, 0.52f), keyColor));
        lockVisuals.Add(CreateLocalBox("Color Lock Left Shackle", new Vector3(-0.22f, 1.19f, 0.22f), new Vector3(0.10f, 0.13f, 0.35f), Color.white));
        lockVisuals.Add(CreateLocalBox("Color Lock Right Shackle", new Vector3(0.22f, 1.19f, 0.22f), new Vector3(0.10f, 0.13f, 0.35f), Color.white));
        lockVisuals.Add(CreateLocalBox("Color Lock Top Shackle", new Vector3(0f, 1.19f, 0.38f), new Vector3(0.52f, 0.13f, 0.10f), Color.white));
    }

    internal void Configure(
        int row,
        int col,
        CarPrototype3D.PieceColor pieceColor,
        CarPrototype3D.ExitDirection exitDirection,
        Vector3 position,
        float boardScale,
        float offBoardScale,
        int cellLength)
    {
        Row = row;
        Col = col;
        CellLength = Mathf.Max(1, cellLength);
        PieceColor = pieceColor;
        Direction = exitDirection;
        IsTrash = pieceColor == CarPrototype3D.PieceColor.Trash;
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(CarPrototype3D.DirectionToWorld(exitDirection), Vector3.up);
        boardVisualScale = boardScale;
        offBoardVisualScale = offBoardScale;
        visualBaseScale = Vector3.one * boardVisualScale;
        transform.localScale = visualBaseScale;

        if (IsTrash)
        {
            if (!BuildImportedPoliceCar()) BuildDeliveryCrateCart();
        }
        else
        {
            if (CellLength > 1) BuildLimousine(pieceColor);
            else BuildCar(pieceColor);
            BuildAnimatedEyes();
        }

        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        collider.size = IsTrash
            ? new Vector3(1.55f, 1.45f, 2.1f)
            : new Vector3(1.55f, 1.2f, CellLength > 1 ? 4.55f : 2.1f);
        collider.center = IsTrash ? new Vector3(0f, 0.5f, 0f) : new Vector3(0f, 0.42f, 0f);
        rootCollider = collider;
    }

    internal bool OccupiesCell(int row, int col)
    {
        Vector2Int step = CarPrototype3D.DirectionToGridStep(Direction);
        for (int offset = 0; offset < CellLength; offset++)
        {
            if (Row - step.y * offset == row && Col - step.x * offset == col)
                return true;
        }

        return false;
    }

    internal IEnumerator DriveTo(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        Quaternion targetRotation = Quaternion.LookRotation((target - start).normalized, Vector3.up);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            transform.position = Vector3.Lerp(start, target, eased);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, eased);
            SetWheelSpin(progress * 520f);
            yield return null;
        }

        transform.position = target;
    }

    internal IEnumerator DriveCurve(Vector3 control, Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float remaining = 1f - progress;
            transform.position = remaining * remaining * start
                + 2f * remaining * progress * control
                + progress * progress * target;

            Vector3 tangent = 2f * remaining * (control - start) + 2f * progress * (target - control);
            if (tangent.sqrMagnitude > 0.0001f)
            {
                Quaternion steeringRotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                float steeringBlend = 1f - Mathf.Exp(-18f * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, steeringRotation, steeringBlend);
            }

            SetWheelSpin(progress * 520f);
            yield return null;
        }

        transform.position = target;
        Vector3 finalTangent = target - control;
        if (finalTangent.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(finalTangent.normalized, Vector3.up);
    }

    internal IEnumerator DriveToTraySlot(Vector3 target, float duration)
    {
        Vector3 trayScale = Vector3.one * (offBoardVisualScale * 0.68f);
        yield return DriveToPose(target, Quaternion.Euler(0f, 180f, 0f), trayScale, duration);
    }

    internal IEnumerator DriveToParkingSlot(Vector3 target, float duration)
    {
        Vector3 parkingScale = Vector3.one * (offBoardVisualScale * 0.74f);
        yield return DriveToPose(target, Quaternion.Euler(0f, 180f, 0f), parkingScale, duration);
    }

    internal IEnumerator PrepareForTrayEntry(float duration)
    {
        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.Euler(0f, 180f, 0f);
        Vector3 startScale = transform.localScale;
        Vector3 trayScale = Vector3.one * (offBoardVisualScale * 0.68f);
        visualBaseScale = trayScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
            transform.localScale = Vector3.Lerp(startScale, trayScale, progress);
            yield return null;
        }

        transform.rotation = targetRotation;
        transform.localScale = trayScale;
    }

    internal IEnumerator PrepareForParkingEntry(float duration)
    {
        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.Euler(0f, 180f, 0f);
        Vector3 startScale = transform.localScale;
        Vector3 parkingScale = Vector3.one * (offBoardVisualScale * 0.74f);
        visualBaseScale = parkingScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
            transform.localScale = Vector3.Lerp(startScale, parkingScale, progress);
            yield return null;
        }

        transform.rotation = targetRotation;
        transform.localScale = parkingScale;
    }

    private IEnumerator DriveToPose(Vector3 target, Quaternion targetRotation, Vector3 targetScale, float duration)
    {
        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        Vector3 startScale = transform.localScale;
        visualBaseScale = targetScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            transform.position = Vector3.Lerp(startPosition, target, eased);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, eased);
            transform.localScale = Vector3.Lerp(startScale, targetScale, eased);
            SetWheelSpin(progress * 520f);
            yield return null;
        }

        transform.position = target;
        transform.rotation = targetRotation;
        transform.localScale = targetScale;
    }

    internal void SetTrayPose(Vector3 position, bool animateArrival = false)
    {
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        visualBaseScale = Vector3.one * (offBoardVisualScale * 0.68f);
        transform.localScale = animateArrival ? Vector3.zero : visualBaseScale;
    }

    internal void SetBoardPose(Vector3 position)
    {
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(CarPrototype3D.DirectionToWorld(Direction), Vector3.up);
        visualBaseScale = Vector3.one * boardVisualScale;
        transform.localScale = visualBaseScale;
    }

    internal void UpdateScaleSettings(float boardScale, float offBoardScale)
    {
        boardVisualScale = boardScale;
        offBoardVisualScale = offBoardScale;
    }

    internal void SetParkingPose(Vector3 position)
    {
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        visualBaseScale = Vector3.one * (offBoardVisualScale * 0.74f);
        transform.localScale = visualBaseScale;
    }

    internal IEnumerator ArriveInTray()
    {
        float elapsed = 0f;
        while (elapsed < 0.2f)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / 0.2f);
            float pop = progress < 0.72f
                ? Mathf.Lerp(0f, 1.12f, progress / 0.72f)
                : Mathf.Lerp(1.12f, 1f, (progress - 0.72f) / 0.28f);
            transform.localScale = visualBaseScale * pop;
            yield return null;
        }
        transform.localScale = visualBaseScale;
    }

    internal IEnumerator Despawn(float duration)
    {
        float elapsed = 0f;
        Vector3 start = transform.localScale;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, Vector3.zero, elapsed / duration);
            yield return null;
        }
        SetVisible(false);
    }

    internal IEnumerator CelebrateAndDespawn()
    {
        Vector3 start = transform.localScale;
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / 0.3f);
            float scale = progress < 0.25f
                ? Mathf.Lerp(1f, 1.18f, progress / 0.25f)
                : Mathf.Lerp(1.18f, 0f, (progress - 0.25f) / 0.75f);
            transform.localScale = start * scale;
            yield return null;
        }
        SetVisible(false);
    }

    internal void SetVisible(bool visible)
    {
        if (!visible && policeLightController != null) policeLightController.StopFlashing();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = visible;
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            collider.enabled = visible;
    }

    internal void BeginExitPoliceLights()
    {
        if (policeLightController != null) policeLightController.BeginExitFlash();
    }

    internal void Reject()
    {
        if (policeLightController != null) policeLightController.FlashBriefly(0.62f);
        StartCoroutine(Shake());
    }

    private IEnumerator Shake()
    {
        Vector3 start = transform.position;
        float elapsed = 0f;
        while (elapsed < 0.16f)
        {
            elapsed += Time.deltaTime;
            float offset = Mathf.Sin(elapsed * 82f) * 0.09f * (1f - elapsed / 0.16f);
            transform.position = start + transform.right * offset;
            yield return null;
        }
        transform.position = start;
    }

    private void BuildCar(CarPrototype3D.PieceColor pieceColor)
    {
        if (pieceColor == CarPrototype3D.PieceColor.Red && BuildImportedRedCar())
            return;
        if (pieceColor == CarPrototype3D.PieceColor.Green && BuildImportedGreenCar())
            return;

        Color bodyColor;
        switch (pieceColor)
        {
            case CarPrototype3D.PieceColor.Red:
                bodyColor = new Color(0.92f, 0.18f, 0.16f);
                break;
            case CarPrototype3D.PieceColor.Blue:
                bodyColor = new Color(0.1f, 0.68f, 0.88f);
                break;
            case CarPrototype3D.PieceColor.Yellow:
                bodyColor = new Color(0.96f, 0.74f, 0.12f);
                break;
            default:
                bodyColor = new Color(0.18f, 0.74f, 0.32f);
                break;
        }
        CreateLocalBox("Body", new Vector3(0f, 0.22f, 0f), new Vector3(1.55f, 0.45f, 2.1f), bodyColor);
        CreateLocalBox("Cabin", new Vector3(0f, 0.56f, 0.08f), new Vector3(1.18f, 0.35f, 0.98f), Color.Lerp(bodyColor, Color.white, 0.42f));
        CreateLocalBox("Windshield", new Vector3(0f, 0.74f, 0.47f), new Vector3(1.02f, 0.18f, 0.24f), new Color(0.12f, 0.28f, 0.4f));
        CreateLocalBox("Front Bumper", new Vector3(0f, 0.19f, 1.08f), new Vector3(1.2f, 0.16f, 0.12f), new Color(0.08f, 0.11f, 0.16f));
        CreateLocalBox("Left Headlight", new Vector3(-0.48f, 0.36f, 1.08f), new Vector3(0.26f, 0.17f, 0.1f), new Color(1f, 0.89f, 0.52f));
        CreateLocalBox("Right Headlight", new Vector3(0.48f, 0.36f, 1.08f), new Vector3(0.26f, 0.17f, 0.1f), new Color(1f, 0.89f, 0.52f));

        CreateWheel(new Vector3(-0.78f, 0.06f, -0.62f));
        CreateWheel(new Vector3(0.78f, 0.06f, -0.62f));
        CreateWheel(new Vector3(-0.78f, 0.06f, 0.62f));
        CreateWheel(new Vector3(0.78f, 0.06f, 0.62f));
    }

    private void BuildLimousine(CarPrototype3D.PieceColor pieceColor)
    {
        usesLimousineVisual = true;
        Color bodyColor = GetProceduralBodyColor(pieceColor);
        Color glass = new Color(0.08f, 0.20f, 0.31f);
        Color trim = new Color(0.96f, 0.72f, 0.20f);
        Color dark = new Color(0.045f, 0.06f, 0.085f);

        CreateLocalBox("Limousine Body", new Vector3(0f, 0.22f, 0f), new Vector3(1.52f, 0.45f, 4.42f), bodyColor);
        CreateLocalBox("Limousine Cabin", new Vector3(0f, 0.58f, -0.12f), new Vector3(1.16f, 0.38f, 2.62f), Color.Lerp(bodyColor, Color.white, 0.26f));
        CreateLocalBox("Limousine Roof", new Vector3(0f, 0.80f, -0.18f), new Vector3(1.06f, 0.12f, 2.20f), Color.Lerp(bodyColor, Color.white, 0.12f));
        CreateLocalBox("Limousine Windshield", new Vector3(0f, 0.78f, 1.23f), new Vector3(1.04f, 0.20f, 0.25f), glass);
        CreateLocalBox("Limousine Rear Window", new Vector3(0f, 0.76f, -1.46f), new Vector3(1.02f, 0.18f, 0.22f), glass);
        CreateLocalBox("Limousine Left Window Strip", new Vector3(-0.585f, 0.66f, -0.12f), new Vector3(0.055f, 0.25f, 2.28f), glass);
        CreateLocalBox("Limousine Right Window Strip", new Vector3(0.585f, 0.66f, -0.12f), new Vector3(0.055f, 0.25f, 2.28f), glass);
        CreateLocalBox("Limousine Left Gold Trim", new Vector3(-0.755f, 0.38f, 0f), new Vector3(0.045f, 0.07f, 3.72f), trim);
        CreateLocalBox("Limousine Right Gold Trim", new Vector3(0.755f, 0.38f, 0f), new Vector3(0.045f, 0.07f, 3.72f), trim);
        CreateLocalBox("Limousine Front Bumper", new Vector3(0f, 0.18f, 2.25f), new Vector3(1.24f, 0.16f, 0.12f), dark);
        CreateLocalBox("Limousine Rear Bumper", new Vector3(0f, 0.18f, -2.25f), new Vector3(1.24f, 0.16f, 0.12f), dark);
        CreateLocalBox("Limousine Left Headlight", new Vector3(-0.48f, 0.36f, 2.24f), new Vector3(0.27f, 0.17f, 0.10f), new Color(1f, 0.91f, 0.55f));
        CreateLocalBox("Limousine Right Headlight", new Vector3(0.48f, 0.36f, 2.24f), new Vector3(0.27f, 0.17f, 0.10f), new Color(1f, 0.91f, 0.55f));

        float[] axlePositions = { -1.62f, 0f, 1.62f };
        for (int index = 0; index < axlePositions.Length; index++)
        {
            CreateWheel(new Vector3(-0.78f, 0.06f, axlePositions[index]));
            CreateWheel(new Vector3(0.78f, 0.06f, axlePositions[index]));
        }
    }

    private static Color GetProceduralBodyColor(CarPrototype3D.PieceColor pieceColor)
    {
        switch (pieceColor)
        {
            case CarPrototype3D.PieceColor.Red: return new Color(0.92f, 0.18f, 0.16f);
            case CarPrototype3D.PieceColor.Blue: return new Color(0.10f, 0.68f, 0.88f);
            case CarPrototype3D.PieceColor.Yellow: return new Color(0.96f, 0.74f, 0.12f);
            default: return new Color(0.18f, 0.74f, 0.32f);
        }
    }

    private bool BuildImportedRedCar()
    {
        GameObject redCarPrefab = Resources.Load<GameObject>(ImportedRedCarResourcePath);
        if (redCarPrefab == null) return false;

        GameObject model = Instantiate(redCarPrefab, transform, false);
        model.name = "Imported Red Car Model";
        model.transform.localPosition = Vector3.zero;
        // REDCARFINAL was authored with its hood facing local -X, while every
        // puzzle direction and movement routine treats local +Z as the front.
        // A -90-degree visual correction maps the model's left-facing hood to
        // gameplay-forward without changing any puzzle coordinates.
        model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        model.transform.localScale = Vector3.one;

        Camera[] importedCameras = model.GetComponentsInChildren<Camera>(true);
        for (int index = 0; index < importedCameras.Length; index++)
            Destroy(importedCameras[index].gameObject);

        Light[] importedLights = model.GetComponentsInChildren<Light>(true);
        for (int index = 0; index < importedLights.Length; index++)
            Destroy(importedLights[index].gameObject);

        Collider[] importedColliders = model.GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < importedColliders.Length; index++)
            Destroy(importedColliders[index]);

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Destroy(model);
            return false;
        }

        // REDCARFINAL uses one UV-mapped material. Its Blender source points to
        // the matching carmatchcar44_4 texture set, which is imported alongside
        // the FBX. Apply that exact texture instead of the incompatible texture
        // from the previous red-car mesh.
        Material redMaterial = GetImportedRedCarMaterial();
        if (redMaterial != null)
        {
            for (int index = 0; index < renderers.Length; index++)
            {
                Material[] materials = renderers[index].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    materials[materialIndex] = redMaterial;
                renderers[index].sharedMaterials = materials;
            }
        }

        usesImportedRedCar = true;

        if (!TryGetLocalRenderBounds(renderers, out Bounds bounds))
            return true;

        const float targetWidth = 1.55f;
        const float targetLength = 2.10f;
        float widthScale = bounds.size.x > 0.001f ? targetWidth / bounds.size.x : 1f;
        float lengthScale = bounds.size.z > 0.001f ? targetLength / bounds.size.z : 1f;
        const float importedRedCarSizeBoost = 1.10f;
        float uniformScale = Mathf.Min(widthScale, lengthScale) * importedRedCarSizeBoost;
        model.transform.localScale = Vector3.one * uniformScale;

        if (TryGetLocalRenderBounds(renderers, out bounds))
        {
            const float desiredWheelBottom = -0.22f;
            model.transform.localPosition += new Vector3(
                -bounds.center.x,
                desiredWheelBottom - bounds.min.y,
                -bounds.center.z);
        }

        return true;
    }

    private bool BuildImportedGreenCar()
    {
        GameObject greenCarPrefab = Resources.Load<GameObject>(ImportedGreenCarResourcePath);
        if (greenCarPrefab == null) return false;

        GameObject model = Instantiate(greenCarPrefab, transform, false);
        model.name = "Imported Green Car Model";
        model.transform.localPosition = Vector3.zero;
        // Unlike REDCARFINAL, this FBX already exports its length and hood on
        // the gameplay-local Z axis, so it must not receive the red car's
        // additional 90-degree visual correction.
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        Camera[] importedCameras = model.GetComponentsInChildren<Camera>(true);
        for (int index = 0; index < importedCameras.Length; index++)
            Destroy(importedCameras[index].gameObject);

        Light[] importedLights = model.GetComponentsInChildren<Light>(true);
        for (int index = 0; index < importedLights.Length; index++)
            Destroy(importedLights[index].gameObject);

        Collider[] importedColliders = model.GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < importedColliders.Length; index++)
            Destroy(importedColliders[index]);

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Destroy(model);
            return false;
        }

        Material greenMaterial = GetImportedGreenCarMaterial();
        if (greenMaterial != null)
        {
            for (int index = 0; index < renderers.Length; index++)
            {
                Material[] materials = renderers[index].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    materials[materialIndex] = greenMaterial;
                renderers[index].sharedMaterials = materials;
            }
        }

        usesImportedGreenCar = true;

        if (!TryGetLocalRenderBounds(renderers, out Bounds bounds))
            return true;

        const float targetWidth = 1.55f;
        const float targetLength = 2.10f;
        float widthScale = bounds.size.x > 0.001f ? targetWidth / bounds.size.x : 1f;
        float lengthScale = bounds.size.z > 0.001f ? targetLength / bounds.size.z : 1f;
        const float importedGreenCarSizeBoost = 1.10f;
        float uniformScale = Mathf.Min(widthScale, lengthScale) * importedGreenCarSizeBoost;
        model.transform.localScale = Vector3.one * uniformScale;

        if (TryGetLocalRenderBounds(renderers, out bounds))
        {
            const float desiredWheelBottom = -0.22f;
            model.transform.localPosition += new Vector3(
                -bounds.center.x,
                desiredWheelBottom - bounds.min.y,
                -bounds.center.z);
        }

        return true;
    }

    private bool BuildImportedPoliceCar()
    {
        GameObject policeCarPrefab = Resources.Load<GameObject>(ImportedPoliceCarResourcePath);
        if (policeCarPrefab == null) return false;

        GameObject model = Instantiate(policeCarPrefab, transform, false);
        model.name = "Imported Police Car Model";
        model.transform.localPosition = Vector3.zero;
        // This FBX already exports with its hood on local +Z, matching the
        // direction used by the puzzle movement code.
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        Camera[] importedCameras = model.GetComponentsInChildren<Camera>(true);
        for (int index = 0; index < importedCameras.Length; index++)
            Destroy(importedCameras[index].gameObject);

        Light[] importedLights = model.GetComponentsInChildren<Light>(true);
        for (int index = 0; index < importedLights.Length; index++)
            Destroy(importedLights[index].gameObject);

        Collider[] importedColliders = model.GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < importedColliders.Length; index++)
            Destroy(importedColliders[index]);

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Destroy(model);
            return false;
        }

        Material policeMaterial = GetImportedPoliceCarMaterial();
        if (policeMaterial != null)
        {
            for (int index = 0; index < renderers.Length; index++)
            {
                Material[] materials = renderers[index].sharedMaterials;
                if (materials.Length == 0)
                {
                    renderers[index].sharedMaterial = policeMaterial;
                    continue;
                }

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    materials[materialIndex] = policeMaterial;
                renderers[index].sharedMaterials = materials;
            }
        }

        if (!TryGetLocalRenderBounds(renderers, out Bounds bounds))
            return true;

        const float targetWidth = 1.55f;
        const float targetLength = 2.10f;
        float widthScale = bounds.size.x > 0.001f ? targetWidth / bounds.size.x : 1f;
        float lengthScale = bounds.size.z > 0.001f ? targetLength / bounds.size.z : 1f;
        float uniformScale = Mathf.Min(widthScale, lengthScale);
        model.transform.localScale = Vector3.one * uniformScale;

        if (TryGetLocalRenderBounds(renderers, out bounds))
        {
            const float desiredWheelBottom = -0.22f;
            model.transform.localPosition += new Vector3(
                -bounds.center.x,
                desiredWheelBottom - bounds.min.y,
                -bounds.center.z);
        }

        policeLightController = gameObject.AddComponent<PoliceLightController>();
        policeLightController.Initialize(
            new Vector3(-0.22f, 1.22f, -0.14f),
            new Vector3(0.22f, 1.22f, -0.14f));

        return true;
    }

    private void BuildAnimatedEyes()
    {
        CarEyeController eyeController = gameObject.AddComponent<CarEyeController>();
        Vector3 windshieldPosition = usesLimousineVisual
            ? new Vector3(0f, 0.93f, 1.21f)
            : usesImportedRedCar || usesImportedGreenCar
                ? new Vector3(0f, 1.15f, 0.27f)
                : new Vector3(0f, 0.88f, 0.43f);
        eyeController.Initialize(windshieldPosition);
    }

    private static Material GetImportedRedCarMaterial()
    {
        if (importedRedCarMaterial != null) return importedRedCarMaterial;

        Texture2D colorTexture = Resources.Load<Texture2D>(ImportedRedCarTexturePath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        importedRedCarMaterial = new Material(shader)
        {
            name = "REDCARFINAL Runtime Material",
            hideFlags = HideFlags.HideAndDontSave,
            color = Color.white
        };

        if (colorTexture != null)
        {
            if (importedRedCarMaterial.HasProperty("_BaseMap")) importedRedCarMaterial.SetTexture("_BaseMap", colorTexture);
            if (importedRedCarMaterial.HasProperty("_MainTex")) importedRedCarMaterial.SetTexture("_MainTex", colorTexture);
        }

        Texture2D metallicTexture = Resources.Load<Texture2D>(ImportedRedCarMetallicPath);
        if (metallicTexture != null && importedRedCarMaterial.HasProperty("_MetallicGlossMap"))
        {
            importedRedCarMaterial.SetTexture("_MetallicGlossMap", metallicTexture);
            importedRedCarMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        if (importedRedCarMaterial.HasProperty("_Metallic")) importedRedCarMaterial.SetFloat("_Metallic", 0.18f);
        if (importedRedCarMaterial.HasProperty("_Smoothness")) importedRedCarMaterial.SetFloat("_Smoothness", 0.42f);
        return importedRedCarMaterial;
    }

    private static Material GetImportedGreenCarMaterial()
    {
        if (importedGreenCarMaterial != null) return importedGreenCarMaterial;

        Texture2D colorTexture = Resources.Load<Texture2D>(ImportedGreenCarTexturePath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        importedGreenCarMaterial = new Material(shader)
        {
            name = "Green Car Runtime Material",
            hideFlags = HideFlags.HideAndDontSave,
            color = Color.white
        };

        if (colorTexture != null)
        {
            if (importedGreenCarMaterial.HasProperty("_BaseMap")) importedGreenCarMaterial.SetTexture("_BaseMap", colorTexture);
            if (importedGreenCarMaterial.HasProperty("_MainTex")) importedGreenCarMaterial.SetTexture("_MainTex", colorTexture);
        }

        Texture2D metallicTexture = Resources.Load<Texture2D>(ImportedGreenCarMetallicPath);
        if (metallicTexture != null && importedGreenCarMaterial.HasProperty("_MetallicGlossMap"))
        {
            importedGreenCarMaterial.SetTexture("_MetallicGlossMap", metallicTexture);
            importedGreenCarMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        if (importedGreenCarMaterial.HasProperty("_Metallic")) importedGreenCarMaterial.SetFloat("_Metallic", 0.18f);
        if (importedGreenCarMaterial.HasProperty("_Smoothness")) importedGreenCarMaterial.SetFloat("_Smoothness", 0.42f);
        return importedGreenCarMaterial;
    }

    private static Material GetImportedPoliceCarMaterial()
    {
        if (importedPoliceCarMaterial != null) return importedPoliceCarMaterial;

        Texture2D colorTexture = Resources.Load<Texture2D>(ImportedPoliceCarTexturePath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        importedPoliceCarMaterial = new Material(shader)
        {
            name = "Police Car Original Textured Material",
            hideFlags = HideFlags.HideAndDontSave,
            color = Color.white
        };

        if (colorTexture != null)
        {
            if (importedPoliceCarMaterial.HasProperty("_BaseMap"))
                importedPoliceCarMaterial.SetTexture("_BaseMap", colorTexture);
            if (importedPoliceCarMaterial.HasProperty("_MainTex"))
                importedPoliceCarMaterial.SetTexture("_MainTex", colorTexture);
        }

        if (importedPoliceCarMaterial.HasProperty("_Metallic"))
            importedPoliceCarMaterial.SetFloat("_Metallic", 0.04f);
        if (importedPoliceCarMaterial.HasProperty("_Smoothness"))
            importedPoliceCarMaterial.SetFloat("_Smoothness", 0.28f);
        return importedPoliceCarMaterial;
    }

    private bool TryGetLocalRenderBounds(Renderer[] renderers, out Bounds localBounds)
    {
        localBounds = default;
        bool hasBounds = false;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Bounds worldBounds = renderers[rendererIndex].bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                if (!hasBounds)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return hasBounds;
    }

    private void BuildDeliveryCrateCart()
    {
        Color metal = new Color(0.18f, 0.25f, 0.31f);
        Color darkMetal = new Color(0.055f, 0.07f, 0.09f);
        Color wood = new Color(0.74f, 0.40f, 0.17f);
        Color lightWood = new Color(0.94f, 0.63f, 0.29f);
        Color darkWood = new Color(0.46f, 0.24f, 0.09f);

        CreateLocalBox("Cart Platform", new Vector3(0f, 0.18f, 0f), new Vector3(1.34f, 0.16f, 1.28f), metal);
        CreateLocalBox("Delivery Crate", new Vector3(0f, 0.72f, 0.02f), new Vector3(1.08f, 0.92f, 1.02f), wood);
        CreateLocalBox("Crate Top", new Vector3(0f, 1.22f, 0.02f), new Vector3(1.16f, 0.10f, 1.10f), lightWood);

        CreateLocalBox("Crate Front Left Brace", new Vector3(-0.43f, 0.72f, 0.55f), new Vector3(0.11f, 0.88f, 0.07f), darkWood);
        CreateLocalBox("Crate Front Right Brace", new Vector3(0.43f, 0.72f, 0.55f), new Vector3(0.11f, 0.88f, 0.07f), darkWood);
        CreateLocalBox("Crate Front Cross Brace", new Vector3(0f, 0.72f, 0.56f), new Vector3(0.96f, 0.11f, 0.07f), darkWood);
        CreateLocalBox("Shipping Label", new Vector3(0f, 0.90f, 0.60f), new Vector3(0.42f, 0.27f, 0.035f), new Color(0.96f, 0.91f, 0.72f));

        CreateLocalBox("Cart Handle Left", new Vector3(-0.48f, 0.82f, -0.66f), new Vector3(0.08f, 1.05f, 0.08f), metal);
        CreateLocalBox("Cart Handle Right", new Vector3(0.48f, 0.82f, -0.66f), new Vector3(0.08f, 1.05f, 0.08f), metal);
        CreateLocalBox("Cart Handle Grip", new Vector3(0f, 1.34f, -0.66f), new Vector3(1.04f, 0.09f, 0.09f), metal);

        GameObject arrowShaft = CreateLocalBox("Direction Arrow Shaft", new Vector3(0f, 1.285f, 0.08f), new Vector3(0.12f, 0.035f, 0.48f), darkMetal);
        GameObject arrowLeft = CreateLocalBox("Direction Arrow Left", new Vector3(-0.085f, 1.285f, 0.29f), new Vector3(0.10f, 0.035f, 0.30f), darkMetal);
        GameObject arrowRight = CreateLocalBox("Direction Arrow Right", new Vector3(0.085f, 1.285f, 0.29f), new Vector3(0.10f, 0.035f, 0.30f), darkMetal);
        arrowShaft.transform.localRotation = Quaternion.identity;
        arrowLeft.transform.localRotation = Quaternion.Euler(0f, -42f, 0f);
        arrowRight.transform.localRotation = Quaternion.Euler(0f, 42f, 0f);

        CreateCartWheel(new Vector3(-0.61f, 0.08f, -0.42f));
        CreateCartWheel(new Vector3(0.61f, 0.08f, -0.42f));
        CreateCartWheel(new Vector3(-0.61f, 0.08f, 0.42f));
        CreateCartWheel(new Vector3(0.61f, 0.08f, 0.42f));
    }

    private GameObject CreateLocalBox(string objectName, Vector3 localPosition, Vector3 scale, Color color)
    {
        GameObject box = CarPrototype3D.CreateBox(objectName, Vector3.zero, scale, color);
        box.transform.SetParent(transform, false);
        box.transform.localPosition = localPosition;
        Destroy(box.GetComponent<Collider>());
        return box;
    }

    private void CreateCartWheel(Vector3 localPosition)
    {
        GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheel.name = "Cart Wheel";
        wheel.transform.SetParent(transform, false);
        wheel.transform.localPosition = localPosition;
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        wheel.transform.localScale = new Vector3(0.17f, 0.10f, 0.17f);
        wheel.GetComponent<Renderer>().sharedMaterial = CarPrototype3D.CreateMaterial(new Color(0.04f, 0.05f, 0.065f));
        Destroy(wheel.GetComponent<Collider>());
    }

    private void CreateWheel(Vector3 localPosition)
    {
        GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheel.name = "Wheel";
        wheel.transform.SetParent(transform, false);
        wheel.transform.localPosition = localPosition;
        wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        wheel.transform.localScale = new Vector3(0.32f, 0.14f, 0.32f);
        wheel.GetComponent<Renderer>().sharedMaterial = CarPrototype3D.CreateMaterial(new Color(0.035f, 0.045f, 0.06f));
        Destroy(wheel.GetComponent<Collider>());
        wheels.Add(wheel.transform);
    }

    private void SetWheelSpin(float degrees)
    {
        for (int index = 0; index < wheels.Count; index++)
            wheels[index].localRotation = Quaternion.Euler(90f, 0f, degrees);
    }
}

public sealed class PoliceLightController : MonoBehaviour
{
    private const float PulseInterval = 0.115f;
    private static readonly Vector3 EmitterScale = new Vector3(0.34f, 0.025f, 0.28f);
    private static readonly Color RedIdle = new Color(0.70f, 0.035f, 0.025f);
    private static readonly Color BlueIdle = new Color(0.025f, 0.25f, 0.76f);
    private static readonly Color RedActive = new Color(1f, 0.10f, 0.06f);
    private static readonly Color BlueActive = new Color(0.05f, 0.48f, 1f);

    private Material redMaterial;
    private Material blueMaterial;
    private Coroutine flashRoutine;
    private WaitForSeconds pulseDelay;
    private float briefFlashEndTime;
    private bool continuousFlash;
    private bool redPhase = true;
    private bool initialized;

    public void Initialize(Vector3 redLocalPosition, Vector3 blueLocalPosition)
    {
        if (initialized) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return;

        redMaterial = CreateEmitterMaterial(shader, "Police Red Flash", RedIdle);
        blueMaterial = CreateEmitterMaterial(shader, "Police Blue Flash", BlueIdle);
        CreateEmitter("Police Red Light Emitter", redLocalPosition, redMaterial);
        CreateEmitter("Police Blue Light Emitter", blueLocalPosition, blueMaterial);
        pulseDelay = new WaitForSeconds(PulseInterval);
        initialized = true;
        SetIdle();
    }

    public void BeginExitFlash()
    {
        if (!initialized) return;
        continuousFlash = true;
        EnsureFlashing();
    }

    public void FlashBriefly(float duration)
    {
        if (!initialized) return;
        briefFlashEndTime = Mathf.Max(briefFlashEndTime, Time.time + Mathf.Max(PulseInterval, duration));
        EnsureFlashing();
    }

    public void StopFlashing()
    {
        continuousFlash = false;
        briefFlashEndTime = 0f;
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        SetIdle();
    }

    private void EnsureFlashing()
    {
        if (flashRoutine == null) flashRoutine = StartCoroutine(Flash());
    }

    private IEnumerator Flash()
    {
        while (continuousFlash || Time.time < briefFlashEndTime)
        {
            SetPhase(redPhase);
            redPhase = !redPhase;
            yield return pulseDelay;
        }

        flashRoutine = null;
        SetIdle();
    }

    private static Material CreateEmitterMaterial(Shader shader, string materialName, Color idleColor)
    {
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave,
            color = idleColor
        };
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.04f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.58f);
        if (material.HasProperty("_EmissionColor")) material.EnableKeyword("_EMISSION");
        return material;
    }

    private GameObject CreateEmitter(string objectName, Vector3 localPosition, Material material)
    {
        GameObject emitter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        emitter.name = objectName;
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = localPosition;
        emitter.transform.localScale = EmitterScale;

        MeshRenderer renderer = emitter.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        Collider emitterCollider = emitter.GetComponent<Collider>();
        if (Application.isPlaying) Destroy(emitterCollider);
        else DestroyImmediate(emitterCollider);
        return emitter;
    }

    private void SetPhase(bool showRed)
    {
        SetMaterialState(redMaterial, showRed ? RedActive : RedIdle, showRed ? RedActive * 5.5f : Color.black);
        SetMaterialState(blueMaterial, showRed ? BlueIdle : BlueActive, showRed ? Color.black : BlueActive * 5.5f);
    }

    private void SetIdle()
    {
        SetMaterialState(redMaterial, RedIdle, Color.black);
        SetMaterialState(blueMaterial, BlueIdle, Color.black);
        redPhase = true;
    }

    private static void SetMaterialState(Material material, Color baseColor, Color emissionColor)
    {
        if (material == null) return;
        material.color = baseColor;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emissionColor);
    }

    private void OnDisable()
    {
        StopFlashing();
    }

    private void OnDestroy()
    {
        DestroyMaterial(redMaterial);
        DestroyMaterial(blueMaterial);
    }

    private static void DestroyMaterial(Material material)
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material);
        else DestroyImmediate(material);
    }
}
