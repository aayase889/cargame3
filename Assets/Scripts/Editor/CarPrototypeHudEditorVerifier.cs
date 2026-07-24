using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight editor-side integration check for the runtime-built 3D HUD.
/// It verifies the layout asset schema and proves that edited values reach the
/// matching RectTransforms without changing the saved asset.
/// </summary>
public static class CarPrototypeHudEditorVerifier
{
    private const string LayoutPath = "Assets/Resources/CarPrototypeHudLayout.asset";

    private static readonly string[] RequiredProperties =
    {
        "sceneCameraPosition", "sceneCameraLookAt", "sceneCameraOrthographicSize",
        "sceneBoardFirstRowZ", "scenePieceScale3x3", "scenePieceScale4x4", "scenePieceScale5x5", "sceneLimousineOffBoardScale",
        "sceneAsphaltGroundPosition", "sceneAsphaltGroundSize", "sceneRoadCenterZ", "sceneRoadDepth",
        "sceneBackgroundAsphaltColor", "scenePlayfieldAsphaltColor", "sceneRoadBorderColor",
        "sceneMatchTrayPosition", "sceneMatchTraySlotSpacing", "sceneMatchTrayBaySize", "sceneMatchTraySlotSpacing5", "sceneMatchTrayBaySize5",
        "sceneSideParkingPosition", "sceneSideParkingSize", "sceneParkingLineWidth", "sceneSideParkingHatchSpacing",
        "levelPillPosition", "levelPillSize", "levelTextPosition", "levelTextSize", "levelTextFontSize",
        "mainMenuPlayPosition", "mainMenuPlaySize", "mainMenuPlayText", "mainMenuPlayFontSize",
        "mainMenuShopTabPosition", "mainMenuHomeTabPosition", "mainMenuLockedTabPosition",
        "mainMenuTabSize", "mainMenuTallTabSize", "mainMenuSelectedTabOffset",
        "mainMenuSelectedLabelOffset", "mainMenuSelectedLabelSize", "mainMenuSelectedLabelFontSize",
        "redDotPosition", "greenDotPosition", "blueDotPosition", "yellowDotPosition", "dotSize",
        "redTextPosition", "greenTextPosition", "blueTextPosition", "yellowTextPosition",
        "progressTextSize", "progressFontSize", "heartsPosition", "heartsSize", "heartsFontSize",
        "extraSlotPosition", "undoPosition", "boosterSize", "pausePosition", "pauseSize",
        "showLevelPreviewButtons", "previousLevelPosition", "nextLevelPosition", "levelPreviewButtonSize",
        "settingsPanelPosition", "settingsPanelSize", "settingsTitlePosition", "hapticsPosition",
        "soundsPosition", "musicPosition", "resumePosition", "quitPosition", "morePosition",
        "settingsClosePosition", "morePanelPosition", "morePanelSize", "moreTitlePosition",
        "termsPosition", "privacyPosition", "moreBackPosition", "moreClosePosition",
        "leavePanelSize", "leaveTitlePosition", "leaveDescriptionPosition", "leaveCancelPosition",
        "leaveConfirmPosition", "defeatPanelSize", "defeatTitlePosition", "defeatDescriptionPosition",
        "restartPosition", "fontOverride"
    };

    [MenuItem("Color Sort/Verify 3D HUD Editor")]
    public static void RunFromMenu()
    {
        try
        {
            RunVerification();
            EditorUtility.DisplayDialog("3D HUD Editor", "Verification passed. Layout fields, live application, and preview visibility are connected correctly.", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("3D HUD Editor", "Verification failed. See the Console for the exact cause.", "OK");
        }
    }

    public static void RunBatch()
    {
        RunVerification();
    }

    private static void RunVerification()
    {
        CarPrototypeHudLayout savedLayout = AssetDatabase.LoadAssetAtPath<CarPrototypeHudLayout>(LayoutPath);
        Require(savedLayout != null, $"Missing layout asset at {LayoutPath}.");

        ColorSortLevelDatabase levelDatabase = AssetDatabase.LoadAssetAtPath<ColorSortLevelDatabase>("Assets/Resources/ColorSortLevelDatabase.asset");
        Require(levelDatabase != null && levelDatabase.levels != null && levelDatabase.levels.Count >= 60, "The fixed level database is unavailable.");
        VerifyLimousineProgression(levelDatabase);

        GameObject importedRedCar = Resources.Load<GameObject>("CarModels/RedCar/redcar");
        Texture2D importedRedCarColor = Resources.Load<Texture2D>("CarModels/RedCar/Material-color");
        Texture2D importedRedCarMetallic = Resources.Load<Texture2D>("CarModels/RedCar/Material-metallic");
        Require(importedRedCar != null, "The imported red-car FBX is missing from Resources.");
        Require(importedRedCar.GetComponentsInChildren<Renderer>(true).Length > 0, "The imported red-car FBX has no renderable mesh.");
        Require(importedRedCarColor != null, "The REDCARFINAL color texture is missing from Resources.");
        Require(importedRedCarMetallic != null, "The REDCARFINAL metallic texture is missing from Resources.");

        GameObject importedGreenCar = Resources.Load<GameObject>("CarModels/GreenCar/greencar");
        Texture2D importedGreenCarColor = Resources.Load<Texture2D>("CarModels/GreenCar/Material-color");
        Texture2D importedGreenCarMetallic = Resources.Load<Texture2D>("CarModels/GreenCar/Material-metallic");
        Require(importedGreenCar != null, "The imported green-car FBX is missing from Resources.");
        Require(importedGreenCar.GetComponentsInChildren<Renderer>(true).Length > 0, "The imported green-car FBX has no renderable mesh.");
        Require(importedGreenCarColor != null, "The green-car color texture is missing from Resources.");
        Require(importedGreenCarMetallic != null, "The green-car metallic texture is missing from Resources.");

        SerializedObject serializedLayout = new SerializedObject(savedLayout);
        for (int index = 0; index < RequiredProperties.Length; index++)
            Require(serializedLayout.FindProperty(RequiredProperties[index]) != null, $"Missing serialized layout property '{RequiredProperties[index]}'.");

        GameObject testRoot = null;
        CarPrototypeHudLayout testLayout = null;
        float previousTimeScale = Time.timeScale;
        try
        {
            testRoot = new GameObject("3D HUD Editor Verification");
            CarPrototype3D game = testRoot.AddComponent<CarPrototype3D>();
            CarPrototypeHud hud = testRoot.AddComponent<CarPrototypeHud>();
            hud.Initialize(game);

            testLayout = UnityEngine.Object.Instantiate(savedLayout);
            testLayout.levelPillPosition = new Vector2(17f, 819f);
            testLayout.levelPillSize = new Vector2(413f, 127f);
            testLayout.mainMenuPlayPosition = new Vector2(91f, -123f);
            testLayout.mainMenuPlaySize = new Vector2(533f, 147f);
            testLayout.sceneCameraPosition = new Vector3(0f, 15f, -9f);
            testLayout.sceneBoardFirstRowZ = 2.1f;
            testLayout.scenePieceScale3x3 = 0.79f;
            testLayout.sceneBackgroundAsphaltColor = new Color(0.17f, 0.22f, 0.31f);
            testLayout.showLevelPreviewButtons = false;

            Require(hud.ApplyLayoutFromEditor(testLayout), "The runtime HUD rejected the edited layout.");

            RectTransform levelPill = FindNamedComponent<RectTransform>(testRoot.transform, "Level Pill");
            Require(levelPill != null, "The compact Level Pill was not built.");
            Require(Approximately(levelPill.anchoredPosition, testLayout.levelPillPosition), "The edited Level Pill position did not reach the runtime RectTransform.");
            Require(Approximately(levelPill.sizeDelta, testLayout.levelPillSize), "The edited Level Pill size did not reach the runtime RectTransform.");
            Require(FindNamedTransform(testRoot.transform, "Retry Button") == null, "The removed gameplay Retry Button was unexpectedly built.");

            RectTransform playButton = FindNamedComponent<RectTransform>(testRoot.transform, "MainMenuPlayButton");
            Require(playButton != null, "The runtime main-menu Play button was not built.");
            Require(Approximately(playButton.anchoredPosition, testLayout.mainMenuPlayPosition), "The edited Play position did not reach the main menu.");
            Require(Approximately(playButton.sizeDelta, testLayout.mainMenuPlaySize), "The edited Play size did not reach the main menu.");

            Transform previousButton = FindNamedTransform(testRoot.transform, "Previous Level");
            Transform nextButton = FindNamedTransform(testRoot.transform, "Next Level");
            Require(previousButton != null && nextButton != null, "The level preview buttons were not built.");
            Require(!previousButton.gameObject.activeSelf && !nextButton.gameObject.activeSelf, "The level preview visibility toggle was not applied.");

            testLayout.showLevelPreviewButtons = true;
            Require(hud.ApplyLayoutFromEditor(testLayout), "The runtime HUD rejected the second edited layout.");
            Require(previousButton.gameObject.activeSelf && nextButton.gameObject.activeSelf, "The level preview buttons did not become visible again.");

            hud.EditorShowSettingsPreview();
            Require(IsNamedObjectActive(testRoot.transform, "Settings Overlay"), "The Settings preview control did not open its overlay.");
            hud.EditorShowMorePreview();
            Require(IsNamedObjectActive(testRoot.transform, "Settings More Overlay"), "The More preview control did not open its overlay.");
            hud.EditorShowLeavePreview();
            Require(IsNamedObjectActive(testRoot.transform, "Leave Confirmation Overlay"), "The Leave preview control did not open its overlay.");
            hud.EditorShowDefeatPreview();
            Require(IsNamedObjectActive(testRoot.transform, "Defeat Overlay"), "The Loss preview control did not open its overlay.");
            hud.EditorShowMainMenuPreview();
            Require(IsNamedObjectActive(testRoot.transform, "StartMenuPanel"), "The Main Menu preview control did not open the menu.");
            Require(IsNamedObjectActive(testRoot.transform, "MainMenuHomePage"), "The main menu did not select the Home page.");
            Require(!IsNamedObjectActive(testRoot.transform, "Defeat Overlay"), "Opening the main menu did not close the Loss overlay.");

            Button shopTab = FindNamedComponent<Button>(testRoot.transform, "MainMenuShopTab");
            Button homeTab = FindNamedComponent<Button>(testRoot.transform, "MainMenuHomeTab");
            Button lockedTab = FindNamedComponent<Button>(testRoot.transform, "MainMenuLockedTab");
            Button menuPlay = FindNamedComponent<Button>(testRoot.transform, "MainMenuPlayButton");
            Require(shopTab != null && homeTab != null && lockedTab != null && menuPlay != null, "One or more main-menu buttons were not built.");
            shopTab.onClick.Invoke();
            Require(IsNamedObjectActive(testRoot.transform, "MainMenuShopPage"), "The Shop tab did not select its page.");
            lockedTab.onClick.Invoke();
            Require(IsNamedObjectActive(testRoot.transform, "MainMenuLockedPage"), "The Locked tab did not select its page.");
            homeTab.onClick.Invoke();
            Require(IsNamedObjectActive(testRoot.transform, "MainMenuHomePage"), "The Home tab did not return to its page.");
            menuPlay.onClick.Invoke();
            Require(!IsNamedObjectActive(testRoot.transform, "StartMenuPanel"), "The Play button did not close the main menu.");

            hud.EditorShowLeavePreview();
            Button confirmLeave = FindNamedComponent<Button>(testRoot.transform, "Confirm Leave");
            Require(confirmLeave != null, "The leave-confirmation button was not built.");
            confirmLeave.onClick.Invoke();
            Require(IsNamedObjectActive(testRoot.transform, "StartMenuPanel"), "Confirming Quit did not return to the main menu.");
            hud.EditorShowGameplayPreview();
            Require(!IsNamedObjectActive(testRoot.transform, "StartMenuPanel"), "The Gameplay preview control did not close the main menu.");

            Debug.Log("[3D HUD Editor Verification] PASS: 3D scene fields, direct live apply, main menu, recursive visibility, and overlay previews are functional.");
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            if (testLayout != null) UnityEngine.Object.DestroyImmediate(testLayout);
            if (testRoot != null) UnityEngine.Object.DestroyImmediate(testRoot);
        }
    }

    private static void VerifyLimousineProgression(ColorSortLevelDatabase database)
    {
        MethodInfo buildPieces = typeof(CarPrototype3D).GetMethod("BuildPrototypePieceSpecs", BindingFlags.NonPublic | BindingFlags.Static);
        Require(buildPieces != null, "The post-level-30 limousine builder is missing.");

        for (int levelIndex = 0; levelIndex < 60; levelIndex++)
        {
            UnityGameManager.LevelConfig level = database.levels[levelIndex];
            Array specs = buildPieces.Invoke(null, new object[] { level }) as Array;
            Require(specs != null, $"Level {level.id} did not produce prototype pieces.");

            bool[,] occupied = new bool[level.boardSize, level.boardSize];
            int limousineCount = 0;
            int occupiedCellCount = 0;
            int redCount = 0;
            int greenCount = 0;

            foreach (object spec in specs)
            {
                Type specType = spec.GetType();
                int leadingRow = (int)specType.GetField("row").GetValue(spec);
                int leadingCol = (int)specType.GetField("col").GetValue(spec);
                int color = Convert.ToInt32(specType.GetField("color").GetValue(spec));
                int direction = Convert.ToInt32(specType.GetField("direction").GetValue(spec));
                int cellLength = (int)specType.GetField("cellLength").GetValue(spec);
                if (cellLength > 1) limousineCount++;
                if (color == 0) redCount++;
                if (color == 1) greenCount++;

                int rowStep = direction == 0 ? -1 : direction == 1 ? 1 : 0;
                int colStep = direction == 2 ? -1 : direction == 3 ? 1 : 0;
                for (int offset = 0; offset < cellLength; offset++)
                {
                    int row = leadingRow - rowStep * offset;
                    int col = leadingCol - colStep * offset;
                    Require(row >= 0 && row < level.boardSize && col >= 0 && col < level.boardSize,
                        $"Level {level.id} contains a limousine footprint outside the board.");
                    Require(!occupied[row, col], $"Level {level.id} contains overlapping prototype pieces at {row},{col}.");
                    occupied[row, col] = true;
                    occupiedCellCount++;
                }
            }

            Require(occupiedCellCount == level.blocks.Count, $"Level {level.id} no longer covers every original board cell.");
            Require(redCount == level.matchTarget && greenCount == level.matchTarget,
                $"Level {level.id} changed a required red or green color target.");
            if (level.id >= 51)
            {
                Require(level.boardSize == 5, $"Level {level.id} should use the new 5x5 board.");
                Require(level.matchTarget == 5, $"Level {level.id} should use a five-car matching tray.");
            }
            if (level.id <= 30)
                Require(limousineCount == 0, $"Level {level.id} introduced a limousine before level 31.");
            else if (level.id <= 40)
                Require(limousineCount == 1, $"Level {level.id} should introduce exactly one limousine.");
            else
                Require(limousineCount == 2, $"Level {level.id} should contain two limousines.");
        }

        Debug.Log("[3D Level Verification] PASS: levels 1-30 have no limousines, levels 31-40 have one, levels 41-60 have two, and levels 51-60 are valid 5x5 boards.");
    }

    private static bool IsNamedObjectActive(Transform root, string name)
    {
        Transform target = FindNamedTransform(root, name);
        return target != null && target.gameObject.activeSelf;
    }

    private static T FindNamedComponent<T>(Transform root, string name) where T : Component
    {
        Transform target = FindNamedTransform(root, name);
        return target != null ? target.GetComponent<T>() : null;
    }

    private static Transform FindNamedTransform(Transform root, string name)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index].gameObject.name == name)
                return transforms[index];
        }

        return null;
    }

    private static bool Approximately(Vector2 a, Vector2 b)
    {
        return Mathf.Abs(a.x - b.x) < 0.01f && Mathf.Abs(a.y - b.y) < 0.01f;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
