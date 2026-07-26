using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Standalone phone HUD for the isolated 3D car prototype. It deliberately does
/// not use the 2D game's launcher, so the two game modes remain independent.
/// </summary>
public sealed class CarPrototypeHud : MonoBehaviour
{
    private enum MainMenuTab { Shop, Home, Locked }

    private sealed class OutcomeObjectiveRow
    {
        public GameObject root;
        public RawImage car;
        public TextMeshProUGUI counter;
        public RawImage check;
    }

    private static Sprite runtimeWhiteSprite;

    private CarPrototype3D game;
    private CarPrototypeHudLayout layout;
    private TMP_FontAsset font;
    private TextMeshProUGUI boardText;
    private TextMeshProUGUI redText;
    private TextMeshProUGUI greenText;
    private TextMeshProUGUI blueText;
    private TextMeshProUGUI yellowText;
    private TextMeshProUGUI experimentalRuleText;
    private RawImage redObjectiveCar;
    private RawImage greenObjectiveCar;
    private RawImage blueObjectiveCar;
    private RawImage yellowObjectiveCar;
    private RawImage redObjectiveCheck;
    private RawImage greenObjectiveCheck;
    private RawImage blueObjectiveCheck;
    private RawImage yellowObjectiveCheck;
    private RectTransform heartsRoot;
    private readonly RawImage[] heartImages = new RawImage[3];
    private Texture2D fullHeartTexture;
    private Texture2D staleHeartTexture;
    private Texture2D brokenHeartTexture;
    private int displayedHearts = -1;
    private Coroutine defeatRevealCoroutine;
    private Button extraSlotButton;
    private Button undoButton;
    private Transform hudRoot;
    private GameObject settingsRoot;
    private GameObject moreRoot;
    private GameObject leaveRoot;
    private GameObject defeatRoot;
    private GameObject victoryRoot;
    private readonly OutcomeObjectiveRow[] victoryObjectiveRows = new OutcomeObjectiveRow[4];
    private readonly RawImage[] defeatHeartImages = new RawImage[CarPrototypeHeartBank.MaximumHearts];
    private TextMeshProUGUI defeatHeartCountdownText;
    private Button lossRetryButton;
    private Coroutine defeatHeartBankCoroutine;
    private bool retryHeartConsumedForCurrentDefeat;
    private GameObject mainMenuRoot;
    private GameObject mainMenuHomePage;
    private GameObject mainMenuShopPage;
    private GameObject mainMenuLockedPage;
    private GameObject mainMenuHeartHudRoot;
    private TextMeshProUGUI mainMenuHeartCountText;
    private TextMeshProUGUI mainMenuHeartTimerText;
    private Coroutine mainMenuHeartHudCoroutine;
    private Image mainMenuShopTab;
    private Image mainMenuHomeTab;
    private Image mainMenuLockedTab;
    private Sprite menuShopNormalSprite;
    private Sprite menuShopTallSprite;
    private Sprite menuHomeNormalSprite;
    private Sprite menuHomeTallSprite;
    private Sprite menuLockedNormalSprite;
    private Sprite menuLockedTallSprite;
    private MainMenuTab selectedMainMenuTab = MainMenuTab.Home;
    private bool hapticsOn = true;
    private bool soundOn = true;
    private bool musicOn = true;

    public void Initialize(CarPrototype3D prototype)
    {
        game = prototype;
        layout = CarPrototypeHudLayout.LoadOrDefault();
        font = layout.fontOverride != null ? layout.fontOverride : TMP_Settings.defaultFontAsset;
        BuildCanvas();
    }

    public void Refresh(int boardNumber, int redCleared, int greenCleared, int blueCleared, int yellowCleared, int goal, int blueGoal, int yellowGoal, int hearts, int trayCapacity, bool extraSlotUsed)
    {
        if (boardText == null) return;

        boardText.text = $"LEVEL <size=112%>{boardNumber}</size>";
        boardText.fontSize = layout.levelTextFontSize;
        bool hasBlueGoal = blueGoal > 0;
        bool hasYellowGoal = yellowGoal > 0;
        RefreshObjective(redObjectiveCar, redText, redObjectiveCheck, true,
            redCleared >= goal, game.RedObjectiveRemaining);
        RefreshObjective(greenObjectiveCar, greenText, greenObjectiveCheck, true,
            greenCleared >= goal, game.GreenObjectiveRemaining);
        RefreshObjective(blueObjectiveCar, blueText, blueObjectiveCheck, hasBlueGoal,
            hasBlueGoal && blueCleared >= blueGoal, game.BlueObjectiveRemaining);
        RefreshObjective(yellowObjectiveCar, yellowText, yellowObjectiveCheck, hasYellowGoal,
            hasYellowGoal && yellowCleared >= yellowGoal, game.YellowObjectiveRemaining);
        UpdateHeartsPosition();
        RefreshHearts(hearts);
        if (experimentalRuleText != null)
        {
            string ruleStatus = game.ExperimentalRuleStatus;
            experimentalRuleText.gameObject.SetActive(!string.IsNullOrEmpty(ruleStatus));
            experimentalRuleText.text = ruleStatus;
        }
        extraSlotButton.interactable = game.CanUseExtraSlot;
        undoButton.interactable = game.CanUseUndo;
    }

    private static void RefreshObjective(
        RawImage car,
        TextMeshProUGUI counter,
        RawImage check,
        bool isVisible,
        bool isComplete,
        int remaining)
    {
        if (car != null) car.gameObject.SetActive(isVisible);
        if (counter != null)
        {
            counter.gameObject.SetActive(isVisible && !isComplete);
            if (isVisible && !isComplete)
                counter.text = Mathf.Max(0, remaining).ToString();
        }
        if (check != null) check.gameObject.SetActive(isVisible && isComplete);
    }

    private void UpdateHeartsPosition()
    {
        if (heartsRoot != null)
            heartsRoot.anchoredPosition = layout.heartsPosition;
    }

    public void ShowDefeat(bool waitForHeartAnimation = true)
    {
        if (defeatRoot == null) return;
        StopMainMenuHeartHudUpdates();
        if (mainMenuRoot != null) mainMenuRoot.SetActive(false);
        HidePauseOverlays();
        if (!retryHeartConsumedForCurrentDefeat)
        {
            CarPrototypeHeartBank.TryConsumeHeart();
            retryHeartConsumedForCurrentDefeat = true;
        }
        RefreshDefeatHeartBank();
        game.TogglePause(true);

        if (defeatRevealCoroutine != null)
            StopCoroutine(defeatRevealCoroutine);
        if (waitForHeartAnimation)
        {
            defeatRevealCoroutine = StartCoroutine(RevealDefeatAfterHeartLoss());
            return;
        }

        ActivateDefeatOutcome();
    }

    public void ShowVictory()
    {
        if (victoryRoot == null) return;
        StopMainMenuHeartHudUpdates();
        if (mainMenuRoot != null) mainMenuRoot.SetActive(false);
        HidePauseOverlays();
        StopDefeatHeartBankUpdates();
        retryHeartConsumedForCurrentDefeat = false;
        if (defeatRoot != null) defeatRoot.SetActive(false);
        RefreshOutcomeObjectives(victoryObjectiveRows, true);
        victoryRoot.SetActive(true);
        game.TogglePause(true);
    }

    /// <summary>
    /// Called by the 3D HUD Layout Editor while Play Mode is running. The HUD is
    /// built at runtime, so its RectTransforms need an explicit refresh after an
    /// asset value changes.
    /// </summary>
    public bool ApplyLayoutFromEditor(CarPrototypeHudLayout editedLayout = null)
    {
        if (editedLayout != null) layout = editedLayout;
        else layout = CarPrototypeHudLayout.LoadOrDefault();

        if (layout == null || hudRoot == null || settingsRoot == null || moreRoot == null || leaveRoot == null
            || defeatRoot == null || victoryRoot == null || mainMenuRoot == null || game == null)
            return false;

        if (!game.Apply3DSettingsFromEditor(layout))
            return false;

        font = layout.fontOverride != null ? layout.fontOverride : TMP_Settings.defaultFontAsset;

        ApplyRect(hudRoot, "Level Pill", layout.levelPillPosition, layout.levelPillSize);
        ApplyRect(hudRoot, "Level Text", layout.levelTextPosition, layout.levelTextSize);
        ApplyTextStyle(hudRoot, "Level Text", layout.levelTextFontSize);
        ApplyObjectiveLayout("Red", layout.redObjectiveCarPosition, layout.redObjectiveStatusPosition);
        ApplyObjectiveLayout("Green", layout.greenObjectiveCarPosition, layout.greenObjectiveStatusPosition);
        ApplyObjectiveLayout("Blue", layout.blueObjectiveCarPosition, layout.blueObjectiveStatusPosition);
        ApplyObjectiveLayout("Yellow", layout.yellowObjectiveCarPosition, layout.yellowObjectiveStatusPosition);
        ApplyHeartLayout();

        ApplyRect(mainMenuRoot.transform, "MainMenuPlayButton", layout.mainMenuPlayPosition, layout.mainMenuPlaySize);
        ApplyRect(mainMenuRoot.transform, "MainMenuPlayButtonText", Vector2.zero, layout.mainMenuPlaySize);
        ApplyTextStyle(mainMenuRoot.transform, "MainMenuPlayButtonText", layout.mainMenuPlayFontSize);
        TextMeshProUGUI playText = FindNamedComponent<TextMeshProUGUI>(mainMenuRoot.transform, "MainMenuPlayButtonText");
        if (playText != null) playText.text = layout.mainMenuPlayText;
        ApplyMainMenuTabState(mainMenuShopTab, selectedMainMenuTab == MainMenuTab.Shop, menuShopNormalSprite, menuShopTallSprite, layout.mainMenuShopTabPosition);
        ApplyMainMenuTabState(mainMenuHomeTab, selectedMainMenuTab == MainMenuTab.Home, menuHomeNormalSprite, menuHomeTallSprite, layout.mainMenuHomeTabPosition);
        ApplyMainMenuTabState(mainMenuLockedTab, selectedMainMenuTab == MainMenuTab.Locked, menuLockedNormalSprite, menuLockedTallSprite, layout.mainMenuLockedTabPosition);
        ApplyRect(mainMenuRoot.transform, "MainMenuShopTabLabel", layout.mainMenuSelectedLabelOffset, layout.mainMenuSelectedLabelSize);
        ApplyRect(mainMenuRoot.transform, "MainMenuHomeTabLabel", layout.mainMenuSelectedLabelOffset, layout.mainMenuSelectedLabelSize);
        ApplyRect(mainMenuRoot.transform, "MainMenuLockedTabLabel", layout.mainMenuSelectedLabelOffset, layout.mainMenuSelectedLabelSize);
        ApplyTextStyle(mainMenuRoot.transform, "MainMenuShopTabLabel", layout.mainMenuSelectedLabelFontSize);
        ApplyTextStyle(mainMenuRoot.transform, "MainMenuHomeTabLabel", layout.mainMenuSelectedLabelFontSize);
        ApplyTextStyle(mainMenuRoot.transform, "MainMenuLockedTabLabel", layout.mainMenuSelectedLabelFontSize);

        ApplyRect(hudRoot, "Extra Slot Booster", layout.extraSlotPosition, layout.boosterSize);
        ApplyRect(hudRoot, "Undo Booster", layout.undoPosition, layout.boosterSize);
        ApplyRect(hudRoot, "Pause Button", layout.pausePosition, layout.pauseSize);
        ApplyButtonAndLabel(hudRoot, "Previous Level", "Previous Level Text", layout.previousLevelPosition, layout.levelPreviewButtonSize);
        ApplyButtonAndLabel(hudRoot, "Next Level", "Next Level Text", layout.nextLevelPosition, layout.levelPreviewButtonSize);
        SetNamedActive(hudRoot, "Previous Level", layout.showLevelPreviewButtons);
        SetNamedActive(hudRoot, "Previous Level Text", layout.showLevelPreviewButtons);
        SetNamedActive(hudRoot, "Next Level", layout.showLevelPreviewButtons);
        SetNamedActive(hudRoot, "Next Level Text", layout.showLevelPreviewButtons);

        ApplyRect(settingsRoot.transform, "Settings Tray", layout.settingsPanelPosition, layout.settingsPanelSize);
        ApplyRect(settingsRoot.transform, "Haptics Icon", layout.hapticsPosition + new Vector2(-230f, 0f), new Vector2(72f, 72f));
        ApplyRect(settingsRoot.transform, "Haptics Text", layout.hapticsPosition + new Vector2(-35f, 0f), new Vector2(270f, 70f));
        ApplyRect(settingsRoot.transform, "Haptics Toggle", layout.hapticsPosition + new Vector2(225f, 0f), new Vector2(154f, 68f));
        ApplyRect(settingsRoot.transform, "Sounds Icon", layout.soundsPosition + new Vector2(-230f, 0f), new Vector2(72f, 72f));
        ApplyRect(settingsRoot.transform, "Sounds Text", layout.soundsPosition + new Vector2(-35f, 0f), new Vector2(270f, 70f));
        ApplyRect(settingsRoot.transform, "Sounds Toggle", layout.soundsPosition + new Vector2(225f, 0f), new Vector2(154f, 68f));
        ApplyRect(settingsRoot.transform, "Music Icon", layout.musicPosition + new Vector2(-230f, 0f), new Vector2(72f, 72f));
        ApplyRect(settingsRoot.transform, "Music Text", layout.musicPosition + new Vector2(-35f, 0f), new Vector2(270f, 70f));
        ApplyRect(settingsRoot.transform, "Music Toggle", layout.musicPosition + new Vector2(225f, 0f), new Vector2(154f, 68f));
        ApplyButtonAndLabel(settingsRoot.transform, "Resume", "Resume Text", layout.resumePosition, new Vector2(390f, 104f));
        ApplyButtonAndLabel(settingsRoot.transform, "Quit", "Quit Text", layout.quitPosition, new Vector2(390f, 104f));
        ApplyButtonAndLabel(settingsRoot.transform, "More", "More Text", layout.morePosition, new Vector2(260f, 70f));
        ApplyRect(settingsRoot.transform, "Close", layout.settingsClosePosition, new Vector2(120f, 120f));

        ApplyRect(moreRoot.transform, "More Tray", layout.morePanelPosition, layout.morePanelSize);
        ApplyRect(moreRoot.transform, "More Title", layout.moreTitlePosition, new Vector2(560f, 84f));
        ApplyButtonAndLabel(moreRoot.transform, "Terms", "Terms Text", layout.termsPosition, new Vector2(420f, 90f));
        ApplyButtonAndLabel(moreRoot.transform, "Privacy", "Privacy Text", layout.privacyPosition, new Vector2(420f, 90f));
        ApplyButtonAndLabel(moreRoot.transform, "Back", "Back Text", layout.moreBackPosition, new Vector2(320f, 80f));
        ApplyRect(moreRoot.transform, "Close", layout.moreClosePosition, new Vector2(120f, 120f));

        ApplyRect(leaveRoot.transform, "Leave Tray", Vector2.zero, layout.leavePanelSize);
        ApplyRect(leaveRoot.transform, "Leave Title", layout.leaveTitlePosition, new Vector2(620f, 86f));
        ApplyRect(leaveRoot.transform, "Leave Description", layout.leaveDescriptionPosition, new Vector2(630f, 64f));
        ApplyButtonAndLabel(leaveRoot.transform, "Cancel Leave", "Cancel Leave Text", layout.leaveCancelPosition, new Vector2(280f, 90f));
        ApplyButtonAndLabel(leaveRoot.transform, "Confirm Leave", "Confirm Leave Text", layout.leaveConfirmPosition, new Vector2(280f, 90f));

        foreach (TextMeshProUGUI text in GetComponentsInChildren<TextMeshProUGUI>(true))
            text.font = font;

        Refresh(game.BoardNumber, game.RedCleared, game.GreenCleared, game.BlueCleared, game.YellowCleared, game.MatchGoal, game.BlueGoal, game.YellowGoal, game.Hearts, game.TrayCapacity, game.ExtraSlotUsed);
        Canvas.ForceUpdateCanvases();
        return true;
    }

    private void ApplyObjectiveLayout(string colorName, Vector2 carPosition, Vector2 statusPosition)
    {
        ApplyRect(hudRoot, colorName + " Objective Car", carPosition, layout.objectiveCarSize);
        ApplyRect(hudRoot, colorName + " Objective Counter", statusPosition, layout.objectiveStatusSize);
        ApplyTextStyle(hudRoot, colorName + " Objective Counter", layout.objectiveStatusFontSize);
        ApplyRect(hudRoot, colorName + " Objective Check", statusPosition, layout.objectiveCheckSize);
    }

    public void EditorShowGameplayPreview()
    {
        if (!IsHudReady()) return;
        StopMainMenuHeartHudUpdates();
        mainMenuRoot.SetActive(false);
        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        HideOutcomeRoots();
        game.TogglePause(false);
    }

    public void EditorShowMainMenuPreview()
    {
        ShowMainMenu();
    }

    public void EditorShowSettingsPreview()
    {
        if (!IsHudReady()) return;
        HideOutcomeRoots();
        OpenSettings();
    }

    public void EditorShowMorePreview()
    {
        if (!IsHudReady()) return;
        HideOutcomeRoots();
        settingsRoot.SetActive(false);
        leaveRoot.SetActive(false);
        moreRoot.SetActive(true);
        game.TogglePause(true);
    }

    public void EditorShowLeavePreview()
    {
        if (!IsHudReady()) return;
        HideOutcomeRoots();
        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(true);
        game.TogglePause(true);
    }

    public void EditorShowDefeatPreview()
    {
        if (!IsHudReady()) return;
        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        HideOutcomeRoots();
        ActivateDefeatOutcome();
        game.TogglePause(true);
    }

    public void EditorShowVictoryPreview()
    {
        if (!IsHudReady()) return;
        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        HideOutcomeRoots();
        RefreshOutcomeObjectives(victoryObjectiveRows, true);
        victoryRoot.SetActive(true);
        game.TogglePause(true);
    }

    public void EditorLoadPreviousBoard()
    {
        if (game != null) game.LoadPreviousLevel();
    }

    public void EditorLoadNextBoard()
    {
        if (game != null) game.LoadNextLevel();
    }

    private bool IsHudReady()
    {
        return game != null && settingsRoot != null && moreRoot != null && leaveRoot != null
            && defeatRoot != null && victoryRoot != null && mainMenuRoot != null;
    }

    private void BuildCanvas()
    {
        if (EventSystem.current == null)
        {
            GameObject eventSystem = new GameObject("Car Prototype EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }

        GameObject canvasObject = new GameObject("Car Prototype HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        hudRoot = canvasObject.transform;
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.55f;

        BuildTopHud(canvasObject.transform);
        BuildBottomControls(canvasObject.transform);
        BuildSettings(canvasObject.transform);
        BuildMorePage(canvasObject.transform);
        BuildLeaveConfirmation(canvasObject.transform);
        BuildMainMenuCanvas();
        BuildOutcomeCanvas();
    }

    private void BuildTopHud(Transform parent)
    {
        // One text-free production sprite is shared by every board; the level
        // number remains live TMP text instead of being baked into 60 images.
        Image levelPill = CreateImage(parent, "Level Pill", LoadSprite("level_badge_road_sign"), layout.levelPillPosition, layout.levelPillSize);
        levelPill.preserveAspect = true;

        boardText = CreateText(parent, "Level Text", "LEVEL <size=112%>1</size>", layout.levelTextPosition, layout.levelTextSize, layout.levelTextFontSize, TextAlignmentOptions.Center);
        boardText.fontStyle = FontStyles.Bold;
        boardText.outlineColor = new Color32(5, 28, 72, 255);
        boardText.outlineWidth = 0.18f;

        redObjectiveCar = CreateRawImage(parent, "Red Objective Car", LoadTexture("objective_car_red"),
            layout.redObjectiveCarPosition, layout.objectiveCarSize);
        greenObjectiveCar = CreateRawImage(parent, "Green Objective Car", LoadTexture("objective_car_green"),
            layout.greenObjectiveCarPosition, layout.objectiveCarSize);
        blueObjectiveCar = CreateRawImage(parent, "Blue Objective Car", LoadTexture("objective_car_blue"),
            layout.blueObjectiveCarPosition, layout.objectiveCarSize);
        yellowObjectiveCar = CreateRawImage(parent, "Yellow Objective Car", LoadTexture("objective_car_yellow"),
            layout.yellowObjectiveCarPosition, layout.objectiveCarSize);

        redText = CreateObjectiveCounter(parent, "Red Objective Counter", layout.redObjectiveStatusPosition);
        greenText = CreateObjectiveCounter(parent, "Green Objective Counter", layout.greenObjectiveStatusPosition);
        blueText = CreateObjectiveCounter(parent, "Blue Objective Counter", layout.blueObjectiveStatusPosition);
        yellowText = CreateObjectiveCounter(parent, "Yellow Objective Counter", layout.yellowObjectiveStatusPosition);

        Texture2D checkTexture = LoadTexture("objective_complete_check");
        redObjectiveCheck = CreateRawImage(parent, "Red Objective Check", checkTexture,
            layout.redObjectiveStatusPosition, layout.objectiveCheckSize);
        greenObjectiveCheck = CreateRawImage(parent, "Green Objective Check", checkTexture,
            layout.greenObjectiveStatusPosition, layout.objectiveCheckSize);
        blueObjectiveCheck = CreateRawImage(parent, "Blue Objective Check", checkTexture,
            layout.blueObjectiveStatusPosition, layout.objectiveCheckSize);
        yellowObjectiveCheck = CreateRawImage(parent, "Yellow Objective Check", checkTexture,
            layout.yellowObjectiveStatusPosition, layout.objectiveCheckSize);

        redObjectiveCheck.gameObject.SetActive(false);
        greenObjectiveCheck.gameObject.SetActive(false);
        blueObjectiveCheck.gameObject.SetActive(false);
        yellowObjectiveCheck.gameObject.SetActive(false);

        fullHeartTexture = LoadTexture("heart_full");
        staleHeartTexture = LoadTexture("heart_stale");
        brokenHeartTexture = LoadTexture("heart_broken_borderless");
        BuildHearts(parent);

        // These samples are temporary, so their compact rule strip is kept out
        // of the permanent layout asset. It appears only on Levels 61-66.
        experimentalRuleText = CreateText(parent, "Experimental Rule Status", string.Empty,
            new Vector2(0f, 650f), new Vector2(650f, 92f), 24f, TextAlignmentOptions.Center);
        experimentalRuleText.fontStyle = FontStyles.Bold;
        experimentalRuleText.outlineColor = new Color32(5, 28, 72, 255);
        experimentalRuleText.outlineWidth = 0.16f;
        experimentalRuleText.textWrappingMode = TextWrappingModes.Normal;
        experimentalRuleText.gameObject.SetActive(false);
    }

    private void BuildBottomControls(Transform parent)
    {
        extraSlotButton = CreateButton(parent, "Extra Slot Booster", LoadSprite("booster_extra_slot"), layout.extraSlotPosition, layout.boosterSize, game.UseExtraSlot);
        undoButton = CreateButton(parent, "Undo Booster", LoadSprite("booster_undo"), layout.undoPosition, layout.boosterSize, game.UseUndo);
        Button pauseButton = CreateButton(parent, "Pause Button", LoadSprite("pause_button"), layout.pausePosition, layout.pauseSize, OpenSettings);
        AddScalePressAnimation(extraSlotButton);
        AddScalePressAnimation(undoButton);
        AddScalePressAnimation(pauseButton);

        Button previousLevelButton = CreateButton(parent, "Previous Level", LoadSprite("settings_resume_button"), layout.previousLevelPosition, layout.levelPreviewButtonSize, game.LoadPreviousLevel);
        AddScalePressAnimation(previousLevelButton);
        TextMeshProUGUI previousLevelText = CreateText(parent, "Previous Level Text", "BACK", layout.previousLevelPosition, layout.levelPreviewButtonSize - new Vector2(16f, 8f), 25f, TextAlignmentOptions.Center);
        previousLevelButton.gameObject.SetActive(layout.showLevelPreviewButtons);
        previousLevelText.gameObject.SetActive(layout.showLevelPreviewButtons);

        Button nextLevelButton = CreateButton(parent, "Next Level", LoadSprite("settings_resume_button"), layout.nextLevelPosition, layout.levelPreviewButtonSize, game.LoadNextLevel);
        AddScalePressAnimation(nextLevelButton);
        TextMeshProUGUI nextLevelText = CreateText(parent, "Next Level Text", "NEXT", layout.nextLevelPosition, layout.levelPreviewButtonSize - new Vector2(16f, 8f), 25f, TextAlignmentOptions.Center);
        nextLevelButton.gameObject.SetActive(layout.showLevelPreviewButtons);
        nextLevelText.gameObject.SetActive(layout.showLevelPreviewButtons);
    }

    private void BuildMainMenu(Transform parent)
    {
        menuShopNormalSprite = LoadSprite("menu_shop_normal");
        menuShopTallSprite = LoadSprite("menu_shop_tall");
        menuHomeNormalSprite = LoadSprite("menu_home_normal");
        menuHomeTallSprite = LoadSprite("menu_home_tall");
        menuLockedNormalSprite = LoadSprite("menu_locked_normal");
        menuLockedTallSprite = LoadSprite("menu_locked_tall");

        mainMenuRoot = CreateFullScreenRoot(parent, "StartMenuPanel");
        Image background = mainMenuRoot.AddComponent<Image>();
        background.sprite = LoadSprite("bg");
        background.color = Color.white;

        mainMenuHomePage = CreateFullScreenRoot(mainMenuRoot.transform, "MainMenuHomePage");
        Button playButton = CreateButton(mainMenuHomePage.transform, "MainMenuPlayButton", LoadSprite("settings_resume_button"), layout.mainMenuPlayPosition, layout.mainMenuPlaySize, StartFromMainMenu);
        playButton.gameObject.AddComponent<SimpleButtonPressAnimation>();
        TextMeshProUGUI playText = CreateText(playButton.transform, "MainMenuPlayButtonText", layout.mainMenuPlayText, Vector2.zero, layout.mainMenuPlaySize, layout.mainMenuPlayFontSize, TextAlignmentOptions.Center);
        playText.fontWeight = FontWeight.Heavy;

        mainMenuShopPage = CreateFullScreenRoot(mainMenuRoot.transform, "MainMenuShopPage");
        mainMenuLockedPage = CreateFullScreenRoot(mainMenuRoot.transform, "MainMenuLockedPage");

        GameObject tabsRoot = CreateFullScreenRoot(mainMenuRoot.transform, "MainMenuTabs");
        mainMenuShopTab = CreateMainMenuTab(tabsRoot.transform, "MainMenuShopTab", "Shop", menuShopNormalSprite, layout.mainMenuShopTabPosition, () => SelectMainMenuTab(MainMenuTab.Shop));
        mainMenuHomeTab = CreateMainMenuTab(tabsRoot.transform, "MainMenuHomeTab", "Home", menuHomeNormalSprite, layout.mainMenuHomeTabPosition, () => SelectMainMenuTab(MainMenuTab.Home));
        mainMenuLockedTab = CreateMainMenuTab(tabsRoot.transform, "MainMenuLockedTab", "Locked", menuLockedNormalSprite, layout.mainMenuLockedTabPosition, () => SelectMainMenuTab(MainMenuTab.Locked));

        BuildMainMenuHeartHud(mainMenuRoot.transform);
        SelectMainMenuTab(MainMenuTab.Home);
        mainMenuRoot.SetActive(false);
    }

    private void BuildMainMenuHeartHud(Transform parent)
    {
        mainMenuHeartHudRoot = new GameObject("Main Menu Heart HUD", typeof(RectTransform));
        mainMenuHeartHudRoot.transform.SetParent(parent, false);
        RectTransform rootRect = mainMenuHeartHudRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = Vector2.one;
        rootRect.pivot = Vector2.one;
        rootRect.anchoredPosition = new Vector2(-46f, -155f);
        rootRect.sizeDelta = new Vector2(480f, 146f);

        CreateRawImage(
            mainMenuHeartHudRoot.transform,
            "Heart HUD Approved Artwork",
            LoadTexture("heart_hud_bar"),
            Vector2.zero,
            rootRect.sizeDelta);

        mainMenuHeartCountText = CreateText(
            mainMenuHeartHudRoot.transform,
            "Heart HUD Count",
            "5",
            new Vector2(-148f, 8f),
            new Vector2(105f, 84f),
            52f,
            TextAlignmentOptions.Center);
        StyleHeartHudText(mainMenuHeartCountText);

        mainMenuHeartTimerText = CreateText(
            mainMenuHeartHudRoot.transform,
            "Heart HUD Timer",
            "MAX",
            new Vector2(16f, 5f),
            new Vector2(205f, 82f),
            48f,
            TextAlignmentOptions.Center);
        StyleHeartHudText(mainMenuHeartTimerText);

        CreateOutcomeButton(
            mainMenuHeartHudRoot.transform,
            "Heart HUD Plus Button",
            LoadTexture("heart_hud_plus"),
            new Vector2(169f, 5f),
            new Vector2(111f, 105f),
            OpenShopFromHeartHud);

        RefreshMainMenuHeartHud();
    }

    private static void StyleHeartHudText(TextMeshProUGUI text)
    {
        if (text == null) return;

        text.fontStyle = FontStyles.Bold;
        text.fontWeight = FontWeight.Heavy;
        text.outlineColor = new Color32(5, 28, 72, 255);
        text.outlineWidth = 0.19f;
    }

    private void BuildMainMenuCanvas()
    {
        GameObject canvasObject = new GameObject("3D Main Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2400f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        BuildMainMenu(canvasObject.transform);
    }

    private void BuildOutcomeCanvas()
    {
        GameObject canvasObject = new GameObject("3D Result Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1125f, 2436f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        BuildDefeat(canvasObject.transform);
        BuildVictory(canvasObject.transform);
    }

    private Image CreateMainMenuTab(Transform parent, string name, string label, Sprite sprite, Vector2 position, UnityEngine.Events.UnityAction click)
    {
        Button button = CreateButton(parent, name, sprite, position, layout.mainMenuTabSize, click);
        button.transition = Selectable.Transition.None;
        MainMenuTabScrubTarget scrubTarget = button.gameObject.AddComponent<MainMenuTabScrubTarget>();
        scrubTarget.Initialize(UpdateMainMenuTabFromPointer);
        Image image = button.GetComponent<Image>();
        image.preserveAspect = true;

        TextMeshProUGUI text = CreateText(button.transform, name + "Label", label, layout.mainMenuSelectedLabelOffset, layout.mainMenuSelectedLabelSize, layout.mainMenuSelectedLabelFontSize, TextAlignmentOptions.Center);
        text.fontWeight = FontWeight.Heavy;
        text.gameObject.SetActive(false);
        return image;
    }

    private void UpdateMainMenuTabFromPointer(PointerEventData eventData)
    {
        if (eventData == null || mainMenuShopTab == null) return;

        RectTransform tabsRect = mainMenuShopTab.rectTransform.parent as RectTransform;
        if (tabsRect == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                tabsRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPosition))
            return;

        TrySelectMainMenuTabAtLocalPosition(localPosition);
    }

    private void TrySelectMainMenuTabAtLocalPosition(Vector2 localPosition)
    {
        float halfNormalHeight = layout.mainMenuTabSize.y * 0.5f;
        float halfSelectedHeight = layout.mainMenuTallTabSize.y * 0.5f;
        float lowestTabCenter = Mathf.Min(
            layout.mainMenuShopTabPosition.y,
            layout.mainMenuHomeTabPosition.y,
            layout.mainMenuLockedTabPosition.y);
        float highestTabCenter = Mathf.Max(
            layout.mainMenuShopTabPosition.y,
            layout.mainMenuHomeTabPosition.y,
            layout.mainMenuLockedTabPosition.y);
        const float verticalTolerance = 60f;
        float minimumY = lowestTabCenter - halfNormalHeight - verticalTolerance;
        float maximumY = highestTabCenter + layout.mainMenuSelectedTabOffset.y + halfSelectedHeight + verticalTolerance;
        if (localPosition.y < minimumY || localPosition.y > maximumY)
            return;

        float shopDistance = Mathf.Abs(localPosition.x - layout.mainMenuShopTabPosition.x);
        float homeDistance = Mathf.Abs(localPosition.x - layout.mainMenuHomeTabPosition.x);
        float lockedDistance = Mathf.Abs(localPosition.x - layout.mainMenuLockedTabPosition.x);

        MainMenuTab nearestTab = shopDistance <= homeDistance && shopDistance <= lockedDistance
            ? MainMenuTab.Shop
            : homeDistance <= lockedDistance
                ? MainMenuTab.Home
                : MainMenuTab.Locked;
        if (nearestTab != selectedMainMenuTab)
            SelectMainMenuTab(nearestTab);
    }

    private void SelectMainMenuTab(MainMenuTab tab)
    {
        selectedMainMenuTab = tab;
        bool shopSelected = tab == MainMenuTab.Shop;
        bool homeSelected = tab == MainMenuTab.Home;
        bool lockedSelected = tab == MainMenuTab.Locked;

        mainMenuShopPage.SetActive(shopSelected);
        mainMenuHomePage.SetActive(homeSelected);
        mainMenuLockedPage.SetActive(lockedSelected);

        ApplyMainMenuTabState(mainMenuShopTab, shopSelected, menuShopNormalSprite, menuShopTallSprite, layout.mainMenuShopTabPosition);
        ApplyMainMenuTabState(mainMenuHomeTab, homeSelected, menuHomeNormalSprite, menuHomeTallSprite, layout.mainMenuHomeTabPosition);
        ApplyMainMenuTabState(mainMenuLockedTab, lockedSelected, menuLockedNormalSprite, menuLockedTallSprite, layout.mainMenuLockedTabPosition);
    }

    private void OpenShopFromHeartHud()
    {
        SelectMainMenuTab(MainMenuTab.Shop);
    }

    private void RefreshMainMenuHeartHud()
    {
        int availableHearts = CarPrototypeHeartBank.AvailableHearts;
        if (mainMenuHeartCountText != null)
            mainMenuHeartCountText.text = availableHearts.ToString();

        if (mainMenuHeartTimerText == null) return;
        if (availableHearts >= CarPrototypeHeartBank.MaximumHearts)
        {
            mainMenuHeartTimerText.text = "MAX";
            return;
        }

        int remainingSeconds = Mathf.Clamp(
            Mathf.CeilToInt((float)CarPrototypeHeartBank.SecondsUntilNextHeart),
            0,
            CarPrototypeHeartBank.RecoveryMinutes * 60);
        int minutes = remainingSeconds / 60;
        int seconds = remainingSeconds % 60;
        mainMenuHeartTimerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void StartMainMenuHeartHudUpdates()
    {
        StopMainMenuHeartHudUpdates();
        if (Application.isPlaying)
            mainMenuHeartHudCoroutine = StartCoroutine(UpdateMainMenuHeartHud());
    }

    private void StopMainMenuHeartHudUpdates()
    {
        if (mainMenuHeartHudCoroutine == null) return;

        StopCoroutine(mainMenuHeartHudCoroutine);
        mainMenuHeartHudCoroutine = null;
    }

    private IEnumerator UpdateMainMenuHeartHud()
    {
        var refreshDelay = new WaitForSecondsRealtime(0.25f);
        while (mainMenuRoot != null && mainMenuRoot.activeSelf)
        {
            RefreshMainMenuHeartHud();
            yield return refreshDelay;
        }

        mainMenuHeartHudCoroutine = null;
    }

    private void ApplyMainMenuTabState(Image tabImage, bool selected, Sprite normalSprite, Sprite tallSprite, Vector2 basePosition)
    {
        if (tabImage == null) return;

        tabImage.sprite = selected ? tallSprite : normalSprite;
        RectTransform rect = tabImage.rectTransform;
        rect.anchoredPosition = basePosition + (selected ? layout.mainMenuSelectedTabOffset : Vector2.zero);
        rect.sizeDelta = selected ? layout.mainMenuTallTabSize : layout.mainMenuTabSize;
        if (selected) tabImage.transform.SetAsLastSibling();

        Transform label = FindNamedTransform(tabImage.transform, tabImage.name + "Label");
        if (label != null) label.gameObject.SetActive(selected);
    }

    public void ShowMainMenu()
    {
        if (!IsHudReady()) return;

        CancelDefeatReveal();
        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        HideOutcomeRoots();
        SelectMainMenuTab(MainMenuTab.Home);
        mainMenuRoot.SetActive(true);
        mainMenuRoot.transform.SetAsLastSibling();
        RefreshMainMenuHeartHud();
        StartMainMenuHeartHudUpdates();
        game.TogglePause(true);
    }

    private void StartFromMainMenu()
    {
        if (mainMenuRoot == null) return;
        StopMainMenuHeartHudUpdates();
        mainMenuRoot.SetActive(false);
        game.TogglePause(false);
    }

    private void BuildSettings(Transform parent)
    {
        settingsRoot = CreateFullScreenRoot(parent, "Settings Overlay");
        CreateFullScreenDim(settingsRoot.transform, "Dim", new Color(0f, 0.03f, 0.1f, 0.75f));
        CreateRawImage(
            settingsRoot.transform,
            "Settings Tray",
            LoadTexture("settings_panel_final"),
            layout.settingsPanelPosition,
            layout.settingsPanelSize);
        CreateSettingRow(settingsRoot.transform, "Haptics", "settings_vibration_icon", layout.hapticsPosition, () => hapticsOn = !hapticsOn, () => hapticsOn);
        CreateSettingRow(settingsRoot.transform, "Sounds", "settings_sound_icon", layout.soundsPosition, () => soundOn = !soundOn, () => soundOn);
        CreateSettingRow(settingsRoot.transform, "Music", "settings_music_icon", layout.musicPosition, () => musicOn = !musicOn, () => musicOn);
        Button resumeButton = CreateButton(settingsRoot.transform, "Resume", LoadSprite("settings_resume_button"), layout.resumePosition, new Vector2(390f, 104f), CloseSettings);
        AddScalePressAnimation(resumeButton);
        CreateText(settingsRoot.transform, "Resume Text", "RESUME", layout.resumePosition, new Vector2(350f, 80f), 40f, TextAlignmentOptions.Center);
        Button quitButton = CreateButton(settingsRoot.transform, "Quit", LoadSprite("settings_quit_button"), layout.quitPosition, new Vector2(390f, 104f), OpenLeaveConfirmation);
        AddScalePressAnimation(quitButton);
        CreateText(settingsRoot.transform, "Quit Text", "QUIT", layout.quitPosition, new Vector2(350f, 80f), 40f, TextAlignmentOptions.Center);
        Button moreButton = CreateButton(settingsRoot.transform, "More", LoadSprite("settings_resume_button"), layout.morePosition, new Vector2(260f, 70f), OpenMorePage);
        AddScalePressAnimation(moreButton);
        CreateText(settingsRoot.transform, "More Text", "MORE", layout.morePosition, new Vector2(230f, 56f), 30f, TextAlignmentOptions.Center);
        CreateSettingsArtworkCloseButton(settingsRoot.transform, layout.settingsClosePosition, CloseSettings);
        settingsRoot.SetActive(false);
    }

    private void BuildMorePage(Transform parent)
    {
        moreRoot = CreateFullScreenRoot(parent, "Settings More Overlay");
        CreateFullScreenDim(moreRoot.transform, "Dim", new Color(0f, 0.03f, 0.1f, 0.75f));
        CreateRawImage(
            moreRoot.transform,
            "More Tray",
            LoadTexture("settings_panel_final"),
            layout.morePanelPosition,
            layout.morePanelSize);
        CreateText(moreRoot.transform, "More Title", "MORE", layout.moreTitlePosition, new Vector2(560f, 84f), 58f, TextAlignmentOptions.Center);
        Button termsButton = CreateButton(moreRoot.transform, "Terms", LoadSprite("settings_resume_button"), layout.termsPosition, new Vector2(420f, 90f), null);
        AddScalePressAnimation(termsButton);
        CreateText(moreRoot.transform, "Terms Text", "TERMS", layout.termsPosition, new Vector2(380f, 68f), 36f, TextAlignmentOptions.Center);
        Button privacyButton = CreateButton(moreRoot.transform, "Privacy", LoadSprite("settings_resume_button"), layout.privacyPosition, new Vector2(420f, 90f), null);
        AddScalePressAnimation(privacyButton);
        CreateText(moreRoot.transform, "Privacy Text", "PRIVACY", layout.privacyPosition, new Vector2(380f, 68f), 36f, TextAlignmentOptions.Center);
        Button backButton = CreateButton(moreRoot.transform, "Back", LoadSprite("settings_resume_button"), layout.moreBackPosition, new Vector2(320f, 80f), CloseMorePage);
        AddScalePressAnimation(backButton);
        CreateText(moreRoot.transform, "Back Text", "BACK", layout.moreBackPosition, new Vector2(280f, 60f), 32f, TextAlignmentOptions.Center);
        CreateSettingsArtworkCloseButton(moreRoot.transform, layout.moreClosePosition, CloseSettings);
        moreRoot.SetActive(false);
    }

    private void BuildLeaveConfirmation(Transform parent)
    {
        leaveRoot = CreateFullScreenRoot(parent, "Leave Confirmation Overlay");
        CreateFullScreenDim(leaveRoot.transform, "Dim", new Color(0f, 0.03f, 0.1f, 0.8f));
        Image panel = CreateImage(leaveRoot.transform, "Leave Tray", LoadSprite("settings_tray"), Vector2.zero, layout.leavePanelSize);
        panel.preserveAspect = true;
        CreateText(leaveRoot.transform, "Leave Title", "LEAVE THIS BOARD?", layout.leaveTitlePosition, new Vector2(620f, 86f), 48f, TextAlignmentOptions.Center);
        CreateText(leaveRoot.transform, "Leave Description", "Your progress on this board will be lost.", layout.leaveDescriptionPosition, new Vector2(630f, 64f), 30f, TextAlignmentOptions.Center);
        CreateButton(leaveRoot.transform, "Cancel Leave", LoadSprite("settings_resume_button"), layout.leaveCancelPosition, new Vector2(280f, 90f), CancelLeaveConfirmation);
        CreateText(leaveRoot.transform, "Cancel Leave Text", "CANCEL", layout.leaveCancelPosition, new Vector2(240f, 66f), 30f, TextAlignmentOptions.Center);
        CreateButton(leaveRoot.transform, "Confirm Leave", LoadSprite("settings_quit_button"), layout.leaveConfirmPosition, new Vector2(280f, 90f), LeaveCurrentBoard);
        CreateText(leaveRoot.transform, "Confirm Leave Text", "LEAVE", layout.leaveConfirmPosition, new Vector2(240f, 66f), 30f, TextAlignmentOptions.Center);
        leaveRoot.SetActive(false);
    }

    private void BuildDefeat(Transform parent)
    {
        Texture2D artwork = LoadTexture("outcome_loss_ui");
        defeatRoot = BuildOutcomeRoot(parent, "Defeat Overlay", artwork);
        BuildDefeatHeartBank(defeatRoot.transform);
        lossRetryButton = CreateOutcomeButton(
            defeatRoot.transform,
            "Loss Retry Button",
            LoadTexture("outcome_loss_retry"),
            new Vector2(-124.5f, -202f),
            new Vector2(248f, 250f),
            RetryCurrentLevelFromOutcome);
        CreateOutcomeButton(
            defeatRoot.transform,
            "Loss Home Button",
            LoadTexture("outcome_loss_home"),
            new Vector2(135.5f, -202f),
            new Vector2(248f, 250f),
            ReturnHomeFromOutcome);
        defeatRoot.SetActive(false);
    }

    private void BuildDefeatHeartBank(Transform parent)
    {
        GameObject hearts = new GameObject("Defeat Heart Bank", typeof(RectTransform));
        hearts.transform.SetParent(parent, false);
        RectTransform heartsRect = hearts.GetComponent<RectTransform>();
        heartsRect.anchorMin = heartsRect.anchorMax = new Vector2(0.5f, 0.5f);
        heartsRect.anchoredPosition = new Vector2(4f, 266f);
        heartsRect.sizeDelta = new Vector2(620f, 104f);

        const float heartSpacing = 104f;
        for (int index = 0; index < defeatHeartImages.Length; index++)
        {
            float x = (index - 2) * heartSpacing;
            defeatHeartImages[index] = CreateRawImage(
                hearts.transform,
                $"Defeat Heart {index + 1}",
                fullHeartTexture,
                new Vector2(x, 0f),
                new Vector2(92f, 92f));
        }

        defeatHeartCountdownText = CreateText(
            parent,
            "Defeat Heart Countdown",
            "10:00",
            new Vector2(4f, 116f),
            new Vector2(650f, 82f),
            42f,
            TextAlignmentOptions.Center);
        defeatHeartCountdownText.fontStyle = FontStyles.Bold;
        defeatHeartCountdownText.outlineColor = new Color32(5, 28, 72, 255);
        defeatHeartCountdownText.outlineWidth = 0.18f;
    }

    private void BuildVictory(Transform parent)
    {
        Texture2D artwork = LoadTexture("outcome_win_ui");
        victoryRoot = BuildOutcomeRoot(parent, "Victory Overlay", artwork);
        BuildOutcomeObjectiveRows(victoryRoot.transform, victoryObjectiveRows);
        CreateOutcomeButton(
            victoryRoot.transform,
            "Win Retry Button",
            LoadTexture("outcome_win_retry"),
            new Vector2(-263f, -207f),
            new Vector2(248f, 250f),
            RetryCurrentLevelFromOutcome);
        CreateOutcomeButton(
            victoryRoot.transform,
            "Win Home Button",
            LoadTexture("outcome_win_home"),
            new Vector2(-1f, -207f),
            new Vector2(248f, 250f),
            ReturnHomeFromOutcome);
        CreateOutcomeButton(
            victoryRoot.transform,
            "Win Next Level Button",
            LoadTexture("outcome_win_next"),
            new Vector2(258f, -207f),
            new Vector2(250f, 250f),
            ContinueToNextLevel);
        victoryRoot.SetActive(false);
    }

    private GameObject BuildOutcomeRoot(Transform parent, string name, Texture2D artwork)
    {
        GameObject root = CreateFullScreenRoot(parent, name);
        Image inputBlocker = root.AddComponent<Image>();
        inputBlocker.sprite = GetRuntimeWhiteSprite();
        inputBlocker.color = new Color(0f, 0.03f, 0.1f, 0.75f);

        Vector2 artworkSize = artwork != null
            ? new Vector2(artwork.width, artwork.height)
            : new Vector2(1125f, 2436f);
        CreateRawImage(root.transform, name + " Approved Artwork", artwork, Vector2.zero, artworkSize);
        return root;
    }

    private void BuildOutcomeObjectiveRows(Transform parent, OutcomeObjectiveRow[] rows)
    {
        string[] colorNames = { "Red", "Green", "Blue", "Yellow" };
        string[] textureNames =
        {
            "objective_car_red",
            "objective_car_green",
            "objective_car_blue",
            "objective_car_yellow"
        };
        Texture2D checkTexture = LoadTexture("objective_complete_check");

        for (int index = 0; index < rows.Length; index++)
        {
            GameObject rowRoot = new GameObject($"Result {colorNames[index]} Objective", typeof(RectTransform));
            rowRoot.transform.SetParent(parent, false);
            RectTransform rowRect = rowRoot.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(4f, 200f);
            rowRect.sizeDelta = new Vector2(620f, 106f);

            RawImage car = CreateRawImage(
                rowRoot.transform,
                $"Result {colorNames[index]} Car",
                LoadTexture(textureNames[index]),
                new Vector2(-82f, 0f),
                new Vector2(118f, 100f));
            TextMeshProUGUI counter = CreateText(
                rowRoot.transform,
                $"Result {colorNames[index]} Counter",
                "0",
                new Vector2(84f, 0f),
                new Vector2(100f, 88f),
                58f,
                TextAlignmentOptions.Center);
            counter.fontStyle = FontStyles.Bold;
            counter.outlineColor = new Color32(5, 28, 72, 255);
            counter.outlineWidth = 0.18f;
            RawImage check = CreateRawImage(
                rowRoot.transform,
                $"Result {colorNames[index]} Check",
                checkTexture,
                new Vector2(84f, 0f),
                new Vector2(76f, 76f));

            rows[index] = new OutcomeObjectiveRow
            {
                root = rowRoot,
                car = car,
                counter = counter,
                check = check
            };
        }
    }

    private Button CreateOutcomeButton(
        Transform parent,
        string name,
        Texture2D texture,
        Vector2 position,
        Vector2 size,
        UnityEngine.Events.UnityAction click)
    {
        RawImage image = CreateRawImage(parent, name, texture, position, size);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(click);
        image.gameObject.AddComponent<SimpleButtonPressAnimation>();
        return button;
    }

    private void CreateSettingRow(Transform parent, string label, string icon, Vector2 position, System.Action toggle, System.Func<bool> getter)
    {
        Image iconImage = CreateImage(parent, label + " Icon", LoadSprite(icon), position + new Vector2(-230f, 0f), new Vector2(72f, 72f));
        iconImage.color = new Color(0.04f, 0.13f, 0.22f, 1f);
        CreateText(parent, label + " Text", label + ":", position + new Vector2(-35f, 0f), new Vector2(270f, 70f), 42f, TextAlignmentOptions.Left);
        Button toggleButton = CreateButton(parent, label + " Toggle", null, position + new Vector2(225f, 0f), new Vector2(154f, 68f), null);
        toggleButton.onClick.AddListener(() =>
        {
            toggle();
            UpdateToggle(toggleButton, getter());
        });
        UpdateToggle(toggleButton, getter());
    }

    private static void UpdateToggle(Button button, bool enabled)
    {
        Image image = button.GetComponent<Image>();
        image.color = enabled ? new Color(0.28f, 0.83f, 0.17f) : new Color(0.42f, 0.46f, 0.52f);
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
        if (text == null)
        {
            GameObject label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(button.transform, false);
            text = label.GetComponent<TextMeshProUGUI>();
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 26f;
            text.font = TMP_Settings.defaultFontAsset;
            text.raycastTarget = false;
        }
        text.text = enabled ? "ON" : "OFF";
    }

    private void OpenSettings()
    {
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        settingsRoot.SetActive(true);
        game.TogglePause(true);
    }

    private void CloseSettings()
    {
        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        game.TogglePause(false);
    }

    private void OpenMorePage()
    {
        settingsRoot.SetActive(false);
        moreRoot.SetActive(true);
    }

    private void CloseMorePage()
    {
        moreRoot.SetActive(false);
        settingsRoot.SetActive(true);
    }

    private void OpenLeaveConfirmation()
    {
        settingsRoot.SetActive(false);
        leaveRoot.SetActive(true);
    }

    private void CancelLeaveConfirmation()
    {
        leaveRoot.SetActive(false);
        settingsRoot.SetActive(true);
    }

    private void LeaveCurrentBoard()
    {
        leaveRoot.SetActive(false);
        game.RestartCurrentBoard();
        ShowMainMenu();
    }

    private void RetryCurrentLevelFromOutcome()
    {
        if (defeatRoot != null && defeatRoot.activeSelf && CarPrototypeHeartBank.AvailableHearts <= 0)
        {
            RefreshDefeatHeartBank();
            return;
        }

        CancelDefeatReveal();
        HideOutcomeRoots();
        game.RestartCurrentBoard();
        game.TogglePause(false);
    }

    private void ReturnHomeFromOutcome()
    {
        CancelDefeatReveal();
        HideOutcomeRoots();
        game.RestartCurrentBoard();
        ShowMainMenu();
    }

    private void ContinueToNextLevel()
    {
        CancelDefeatReveal();
        HideOutcomeRoots();
        game.TogglePause(false);
        game.LoadNextLevel();
    }

    private void CancelDefeatReveal()
    {
        if (defeatRevealCoroutine == null) return;

        StopCoroutine(defeatRevealCoroutine);
        defeatRevealCoroutine = null;
    }

    private void HideOutcomeRoots()
    {
        StopDefeatHeartBankUpdates();
        retryHeartConsumedForCurrentDefeat = false;
        if (defeatRoot != null) defeatRoot.SetActive(false);
        if (victoryRoot != null) victoryRoot.SetActive(false);
    }

    private void ActivateDefeatOutcome()
    {
        if (defeatRoot == null) return;

        RefreshDefeatHeartBank();
        defeatRoot.SetActive(true);
        if (Application.isPlaying)
            StartDefeatHeartBankUpdates();
    }

    private void RefreshDefeatHeartBank()
    {
        int availableHearts = CarPrototypeHeartBank.AvailableHearts;
        for (int index = 0; index < defeatHeartImages.Length; index++)
        {
            if (defeatHeartImages[index] != null)
                defeatHeartImages[index].texture = index < availableHearts ? fullHeartTexture : staleHeartTexture;
        }

        if (lossRetryButton != null)
            lossRetryButton.interactable = availableHearts > 0;

        if (defeatHeartCountdownText == null) return;
        if (availableHearts >= CarPrototypeHeartBank.MaximumHearts)
        {
            defeatHeartCountdownText.text = "MAX";
            return;
        }

        int remainingSeconds = Mathf.Clamp(
            Mathf.CeilToInt((float)CarPrototypeHeartBank.SecondsUntilNextHeart),
            0,
            CarPrototypeHeartBank.RecoveryMinutes * 60);
        int minutes = remainingSeconds / 60;
        int seconds = remainingSeconds % 60;
        defeatHeartCountdownText.text = $"{minutes:00}:{seconds:00}";
    }

    private void StartDefeatHeartBankUpdates()
    {
        StopDefeatHeartBankUpdates();
        defeatHeartBankCoroutine = StartCoroutine(UpdateDefeatHeartBank());
    }

    private void StopDefeatHeartBankUpdates()
    {
        if (defeatHeartBankCoroutine == null) return;

        StopCoroutine(defeatHeartBankCoroutine);
        defeatHeartBankCoroutine = null;
    }

    private IEnumerator UpdateDefeatHeartBank()
    {
        var refreshDelay = new WaitForSecondsRealtime(0.25f);
        while (defeatRoot != null && defeatRoot.activeSelf)
        {
            RefreshDefeatHeartBank();
            yield return refreshDelay;
        }

        defeatHeartBankCoroutine = null;
    }

    private void HidePauseOverlays()
    {
        if (settingsRoot != null) settingsRoot.SetActive(false);
        if (moreRoot != null) moreRoot.SetActive(false);
        if (leaveRoot != null) leaveRoot.SetActive(false);
    }

    private void RefreshOutcomeObjectives(OutcomeObjectiveRow[] rows, bool showCompletionChecks)
    {
        if (rows == null || game == null) return;

        bool[] visible =
        {
            true,
            true,
            game.BlueGoal > 0,
            game.YellowGoal > 0
        };
        int[] remaining =
        {
            game.RedObjectiveRemaining,
            game.GreenObjectiveRemaining,
            game.BlueObjectiveRemaining,
            game.YellowObjectiveRemaining
        };

        int visibleCount = 0;
        for (int index = 0; index < visible.Length; index++)
        {
            if (visible[index]) visibleCount++;
        }

        float spacing = visibleCount <= 2 ? 126f : visibleCount == 3 ? 104f : 88f;
        float topOffset = (visibleCount - 1) * spacing * 0.5f;
        int visibleIndex = 0;
        for (int index = 0; index < rows.Length; index++)
        {
            OutcomeObjectiveRow row = rows[index];
            if (row == null || row.root == null) continue;

            row.root.SetActive(visible[index]);
            if (!visible[index]) continue;

            RectTransform rowRect = row.root.GetComponent<RectTransform>();
            rowRect.anchoredPosition = new Vector2(4f, 200f + topOffset - visibleIndex * spacing);
            row.counter.gameObject.SetActive(!showCompletionChecks);
            row.counter.text = Mathf.Max(0, remaining[index]).ToString();
            row.check.gameObject.SetActive(showCompletionChecks);
            visibleIndex++;
        }
    }

    private void BuildHearts(Transform parent)
    {
        GameObject root = new GameObject("Hearts", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        heartsRoot = root.GetComponent<RectTransform>();
        heartsRoot.anchorMin = heartsRoot.anchorMax = new Vector2(0.5f, 0.5f);

        for (int index = 0; index < heartImages.Length; index++)
        {
            float x = (index - 1) * layout.heartSpacing;
            heartImages[index] = CreateRawImage(
                heartsRoot,
                $"Heart {index + 1}",
                fullHeartTexture,
                new Vector2(x, 0f),
                layout.heartSize);
        }

        ApplyHeartLayout();
    }

    private void ApplyHeartLayout()
    {
        if (heartsRoot == null) return;

        heartsRoot.anchoredPosition = layout.heartsPosition;
        heartsRoot.sizeDelta = new Vector2(
            layout.heartSize.x + layout.heartSpacing * (heartImages.Length - 1),
            layout.heartSize.y);

        for (int index = 0; index < heartImages.Length; index++)
        {
            if (heartImages[index] == null) continue;
            heartImages[index].rectTransform.anchoredPosition = new Vector2(
                (index - 1) * layout.heartSpacing,
                0f);
            heartImages[index].rectTransform.sizeDelta = layout.heartSize;
        }
    }

    private void RefreshHearts(int heartCount)
    {
        int clampedCount = Mathf.Clamp(heartCount, 0, heartImages.Length);
        if (displayedHearts >= 0 && clampedCount == displayedHearts)
            return;

        if (displayedHearts < 0 || clampedCount > displayedHearts)
        {
            StopAllCoroutines();
            defeatRevealCoroutine = null;
            DestroyFallingHearts();
            for (int index = 0; index < heartImages.Length; index++)
            {
                if (heartImages[index] != null)
                    heartImages[index].texture = index < clampedCount ? fullHeartTexture : staleHeartTexture;
            }

            displayedHearts = clampedCount;
            return;
        }

        for (int index = displayedHearts - 1; index >= clampedCount; index--)
        {
            if (heartImages[index] != null)
                heartImages[index].texture = staleHeartTexture;
            PlayHeartLoss(index);
        }

        displayedHearts = clampedCount;
    }

    private void PlayHeartLoss(int heartIndex)
    {
        if (heartsRoot == null || brokenHeartTexture == null) return;

        float x = (heartIndex - 1) * layout.heartSpacing;
        RawImage fallingHeart = CreateRawImage(
            heartsRoot,
            $"Falling Broken Heart {heartIndex + 1}",
            brokenHeartTexture,
            new Vector2(x, 0f),
            layout.heartSize);
        fallingHeart.transform.SetAsLastSibling();
        StartCoroutine(AnimateFallingHeart(fallingHeart));
    }

    private IEnumerator AnimateFallingHeart(RawImage fallingHeart)
    {
        RectTransform rect = fallingHeart.rectTransform;
        Vector2 startPosition = rect.anchoredPosition;
        float duration = Mathf.Max(0.1f, layout.heartLossDuration);
        float elapsed = 0f;

        while (elapsed < duration && fallingHeart != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float fallProgress = progress * progress;
            rect.anchoredPosition = startPosition + Vector2.down * (layout.heartLossFallDistance * fallProgress);

            Color color = Color.white;
            color.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 1f, progress));
            fallingHeart.color = color;
            yield return null;
        }

        if (fallingHeart != null)
            Destroy(fallingHeart.gameObject);
    }

    private IEnumerator RevealDefeatAfterHeartLoss()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, layout.heartLossDuration));
        ActivateDefeatOutcome();
        defeatRevealCoroutine = null;
    }

    private void DestroyFallingHearts()
    {
        if (heartsRoot == null) return;

        for (int index = heartsRoot.childCount - 1; index >= 0; index--)
        {
            Transform child = heartsRoot.GetChild(index);
            if (child.name.StartsWith("Falling Broken Heart"))
                Destroy(child.gameObject);
        }
    }

    private TextMeshProUGUI CreateObjectiveCounter(Transform parent, string name, Vector2 position)
    {
        TextMeshProUGUI counter = CreateText(parent, name, "3", position,
            layout.objectiveStatusSize, layout.objectiveStatusFontSize, TextAlignmentOptions.Center);
        counter.fontStyle = FontStyles.Bold;
        counter.outlineColor = new Color32(5, 28, 72, 255);
        counter.outlineWidth = 0.18f;
        return counter;
    }

    private RawImage CreateRawImage(Transform parent, string name, Texture texture, Vector2 position, Vector2 size)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        item.transform.SetParent(parent, false);
        RawImage image = item.GetComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return image;
    }

    private Image CreateImage(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>();
        image.sprite = sprite != null ? sprite : GetRuntimeWhiteSprite();
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return image;
    }

    private Image CreateFullScreenDim(Transform parent, string name, Color color)
    {
        Image image = CreateImage(parent, name, null, Vector2.zero, Vector2.zero);
        image.color = color;
        image.raycastTarget = true;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return image;
    }

    private static GameObject CreateFullScreenRoot(Transform parent, string name)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return root;
    }

    private Button CreateButton(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction click)
    {
        Image image = CreateImage(parent, name, sprite, position, size);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        if (click != null) button.onClick.AddListener(click);
        return button;
    }

    private static void AddScalePressAnimation(Button button)
    {
        if (button != null && button.GetComponent<SimpleButtonPressAnimation>() == null)
            button.gameObject.AddComponent<SimpleButtonPressAnimation>();
    }

    private Button CreateSettingsArtworkCloseButton(
        Transform parent,
        Vector2 position,
        UnityEngine.Events.UnityAction click)
    {
        const float sourceWidth = 1125f;
        const float sourceHeight = 2436f;
        const float closeSize = 120f;
        // Source-space center of the red X baked into settings_panel_final.png.
        const float sourceCenterX = 927.5f;
        const float sourceCenterYFromTop = 543f;

        RawImage pressTint = CreateRawImage(
            parent,
            "Close Artwork Press Tint",
            LoadTexture("settings_panel_final"),
            position,
            new Vector2(closeSize, closeSize));
        pressTint.uvRect = new Rect(
            (sourceCenterX - closeSize * 0.5f) / sourceWidth,
            (sourceHeight - sourceCenterYFromTop - closeSize * 0.5f) / sourceHeight,
            closeSize / sourceWidth,
            closeSize / sourceHeight);
        // Invisible at rest, then a sampled dark overlay fades in while pressed.
        pressTint.color = Color.clear;

        Button button = CreateButton(parent, "Close", null, position, new Vector2(closeSize, closeSize), click);
        button.GetComponent<Image>().color = Color.clear;
        button.targetGraphic = pressTint;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.clear;
        colors.highlightedColor = Color.clear;
        colors.selectedColor = Color.clear;
        colors.pressedColor = new Color(0.38f, 0.38f, 0.38f, 0.58f);
        colors.disabledColor = Color.clear;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.06f;
        button.colors = colors;
        return button;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, string value, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        TextMeshProUGUI text = item.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.color = Color.white;
        // Labels are visual only. Leaving their raycast target on would block the button beneath.
        text.raycastTarget = false;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return text;
    }

    private static Sprite LoadSprite(string resourceName)
    {
        return Resources.Load<Sprite>(resourceName);
    }

    private static Texture2D LoadTexture(string resourceName)
    {
        return Resources.Load<Texture2D>(resourceName);
    }

    private static Sprite GetRuntimeWhiteSprite()
    {
        if (runtimeWhiteSprite != null) return runtimeWhiteSprite;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;

        runtimeWhiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        runtimeWhiteSprite.hideFlags = HideFlags.HideAndDontSave;
        return runtimeWhiteSprite;
    }

    private static void ApplyButtonAndLabel(Transform root, string buttonName, string labelName, Vector2 position, Vector2 size)
    {
        ApplyRect(root, buttonName, position, size);
        ApplyRect(root, labelName, position, size);
    }

    private static void ApplyTextStyle(Transform root, string name, float fontSize)
    {
        TextMeshProUGUI text = FindNamedComponent<TextMeshProUGUI>(root, name);
        if (text != null) text.fontSize = fontSize;
    }

    private static void ApplyRect(Transform root, string name, Vector2 position, Vector2 size)
    {
        RectTransform rect = FindNamedComponent<RectTransform>(root, name);
        if (rect == null) return;

        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetNamedActive(Transform root, string name, bool isActive)
    {
        Transform child = FindNamedTransform(root, name);
        if (child != null) child.gameObject.SetActive(isActive);
    }

    private static Transform FindNamedTransform(Transform root, string name)
    {
        if (root == null) return null;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index].gameObject.name == name)
                return transforms[index];
        }

        return null;
    }

    private static T FindNamedComponent<T>(Transform root, string name) where T : Component
    {
        if (root == null) return null;

        T[] components = root.GetComponentsInChildren<T>(true);
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index].gameObject.name == name)
                return components[index];
        }

        return null;
    }
}
