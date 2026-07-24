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

    private static Sprite runtimeWhiteSprite;

    private CarPrototype3D game;
    private CarPrototypeHudLayout layout;
    private TMP_FontAsset font;
    private TextMeshProUGUI boardText;
    private TextMeshProUGUI redText;
    private TextMeshProUGUI greenText;
    private TextMeshProUGUI blueText;
    private TextMeshProUGUI yellowText;
    private TextMeshProUGUI heartsText;
    private TextMeshProUGUI experimentalRuleText;
    private Image blueDot;
    private Image yellowDot;
    private Button extraSlotButton;
    private Button undoButton;
    private Transform hudRoot;
    private GameObject settingsRoot;
    private GameObject moreRoot;
    private GameObject leaveRoot;
    private GameObject defeatRoot;
    private GameObject mainMenuRoot;
    private GameObject mainMenuHomePage;
    private GameObject mainMenuShopPage;
    private GameObject mainMenuLockedPage;
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

        boardText.text = $"LEVEL {boardNumber}";
        boardText.fontSize = layout.levelTextFontSize;
        redText.text = $"{redCleared}/{goal}";
        greenText.text = $"{greenCleared}/{goal}";
        bool hasBlueGoal = blueGoal > 0;
        bool hasYellowGoal = yellowGoal > 0;
        if (blueText != null)
        {
            blueText.gameObject.SetActive(hasBlueGoal);
            blueText.text = $"{blueCleared}/{blueGoal}";
        }
        if (blueDot != null) blueDot.gameObject.SetActive(hasBlueGoal);
        if (yellowText != null)
        {
            yellowText.gameObject.SetActive(hasYellowGoal);
            yellowText.text = $"{yellowCleared}/{yellowGoal}";
        }
        if (yellowDot != null) yellowDot.gameObject.SetActive(hasYellowGoal);
        heartsText.text = new string('\u2665', Mathf.Clamp(hearts, 0, 3)) + new string('\u2661', Mathf.Clamp(3 - hearts, 0, 3));
        if (experimentalRuleText != null)
        {
            string ruleStatus = game.ExperimentalRuleStatus;
            experimentalRuleText.gameObject.SetActive(!string.IsNullOrEmpty(ruleStatus));
            experimentalRuleText.text = ruleStatus;
        }
        extraSlotButton.interactable = game.CanUseExtraSlot;
        undoButton.interactable = game.CanUseUndo;
    }

    public void ShowDefeat()
    {
        if (defeatRoot == null) return;
        if (mainMenuRoot != null) mainMenuRoot.SetActive(false);
        defeatRoot.SetActive(true);
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

        if (layout == null || hudRoot == null || settingsRoot == null || moreRoot == null || leaveRoot == null || defeatRoot == null || mainMenuRoot == null || game == null)
            return false;

        if (!game.Apply3DSettingsFromEditor(layout))
            return false;

        font = layout.fontOverride != null ? layout.fontOverride : TMP_Settings.defaultFontAsset;

        ApplyRect(hudRoot, "Level Pill", layout.levelPillPosition, layout.levelPillSize);
        ApplyRect(hudRoot, "Level Text", layout.levelTextPosition, layout.levelTextSize);
        ApplyTextStyle(hudRoot, "Level Text", layout.levelTextFontSize);
        ApplyRect(hudRoot, "Red Dot", layout.redDotPosition, layout.dotSize);
        ApplyRect(hudRoot, "Green Dot", layout.greenDotPosition, layout.dotSize);
        ApplyRect(hudRoot, "Blue Dot", layout.blueDotPosition, layout.dotSize);
        ApplyRect(hudRoot, "Yellow Dot", layout.yellowDotPosition, layout.dotSize);
        ApplyRect(hudRoot, "Red Progress", layout.redTextPosition, layout.progressTextSize);
        ApplyRect(hudRoot, "Green Progress", layout.greenTextPosition, layout.progressTextSize);
        ApplyRect(hudRoot, "Blue Progress", layout.blueTextPosition, layout.progressTextSize);
        ApplyRect(hudRoot, "Yellow Progress", layout.yellowTextPosition, layout.progressTextSize);
        ApplyTextStyle(hudRoot, "Red Progress", layout.progressFontSize);
        ApplyTextStyle(hudRoot, "Green Progress", layout.progressFontSize);
        ApplyTextStyle(hudRoot, "Blue Progress", layout.progressFontSize);
        ApplyTextStyle(hudRoot, "Yellow Progress", layout.progressFontSize);
        ApplyRect(hudRoot, "Hearts", layout.heartsPosition, layout.heartsSize);
        ApplyTextStyle(hudRoot, "Hearts", layout.heartsFontSize);

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
        ApplyRect(settingsRoot.transform, "Title", layout.settingsTitlePosition, new Vector2(600f, 84f));
        ApplyRect(settingsRoot.transform, "Haptics Icon", layout.hapticsPosition + new Vector2(-230f, 0f), new Vector2(72f, 72f));
        ApplyRect(settingsRoot.transform, "Haptics Text", layout.hapticsPosition + new Vector2(-35f, 0f), new Vector2(270f, 70f));
        ApplyRect(settingsRoot.transform, "Haptics Toggle", layout.hapticsPosition + new Vector2(225f, 0f), new Vector2(154f, 68f));
        ApplyRect(settingsRoot.transform, "Sounds Icon", layout.soundsPosition + new Vector2(-230f, 0f), new Vector2(72f, 72f));
        ApplyRect(settingsRoot.transform, "Sounds Text", layout.soundsPosition + new Vector2(-35f, 0f), new Vector2(270f, 70f));
        ApplyRect(settingsRoot.transform, "Sounds Toggle", layout.soundsPosition + new Vector2(225f, 0f), new Vector2(154f, 68f));
        ApplyRect(settingsRoot.transform, "Music Icon", layout.musicPosition + new Vector2(-230f, 0f), new Vector2(72f, 72f));
        ApplyRect(settingsRoot.transform, "Music Text", layout.musicPosition + new Vector2(-35f, 0f), new Vector2(270f, 70f));
        ApplyButtonAndLabel(settingsRoot.transform, "Resume", "Resume Text", layout.resumePosition, new Vector2(390f, 104f));
        ApplyButtonAndLabel(settingsRoot.transform, "Quit", "Quit Text", layout.quitPosition, new Vector2(390f, 104f));
        ApplyButtonAndLabel(settingsRoot.transform, "More", "More Text", layout.morePosition, new Vector2(260f, 70f));
        ApplyRect(settingsRoot.transform, "Close", layout.settingsClosePosition, new Vector2(90f, 90f));

        ApplyRect(moreRoot.transform, "More Tray", layout.morePanelPosition, layout.morePanelSize);
        ApplyRect(moreRoot.transform, "More Title", layout.moreTitlePosition, new Vector2(560f, 84f));
        ApplyButtonAndLabel(moreRoot.transform, "Terms", "Terms Text", layout.termsPosition, new Vector2(420f, 90f));
        ApplyButtonAndLabel(moreRoot.transform, "Privacy", "Privacy Text", layout.privacyPosition, new Vector2(420f, 90f));
        ApplyButtonAndLabel(moreRoot.transform, "Back", "Back Text", layout.moreBackPosition, new Vector2(320f, 80f));
        ApplyRect(moreRoot.transform, "Close", layout.moreClosePosition, new Vector2(90f, 90f));

        ApplyRect(leaveRoot.transform, "Leave Tray", Vector2.zero, layout.leavePanelSize);
        ApplyRect(leaveRoot.transform, "Leave Title", layout.leaveTitlePosition, new Vector2(620f, 86f));
        ApplyRect(leaveRoot.transform, "Leave Description", layout.leaveDescriptionPosition, new Vector2(630f, 64f));
        ApplyButtonAndLabel(leaveRoot.transform, "Cancel Leave", "Cancel Leave Text", layout.leaveCancelPosition, new Vector2(280f, 90f));
        ApplyButtonAndLabel(leaveRoot.transform, "Confirm Leave", "Confirm Leave Text", layout.leaveConfirmPosition, new Vector2(280f, 90f));

        ApplyRect(defeatRoot.transform, "Defeat Panel", Vector2.zero, layout.defeatPanelSize);
        ApplyRect(defeatRoot.transform, "Defeat Title", layout.defeatTitlePosition, new Vector2(560f, 80f));
        ApplyRect(defeatRoot.transform, "Defeat Description", layout.defeatDescriptionPosition, new Vector2(560f, 60f));
        ApplyButtonAndLabel(defeatRoot.transform, "Restart", "Restart Text", layout.restartPosition, new Vector2(380f, 104f));

        foreach (TextMeshProUGUI text in GetComponentsInChildren<TextMeshProUGUI>(true))
            text.font = font;

        Refresh(game.BoardNumber, game.RedCleared, game.GreenCleared, game.BlueCleared, game.YellowCleared, game.MatchGoal, game.BlueGoal, game.YellowGoal, game.Hearts, game.TrayCapacity, game.ExtraSlotUsed);
        Canvas.ForceUpdateCanvases();
        return true;
    }

    public void EditorShowGameplayPreview()
    {
        if (!IsHudReady()) return;
        mainMenuRoot.SetActive(false);
        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        defeatRoot.SetActive(false);
        game.TogglePause(false);
    }

    public void EditorShowMainMenuPreview()
    {
        ShowMainMenu();
    }

    public void EditorShowSettingsPreview()
    {
        if (!IsHudReady()) return;
        defeatRoot.SetActive(false);
        OpenSettings();
    }

    public void EditorShowMorePreview()
    {
        if (!IsHudReady()) return;
        defeatRoot.SetActive(false);
        settingsRoot.SetActive(false);
        leaveRoot.SetActive(false);
        moreRoot.SetActive(true);
        game.TogglePause(true);
    }

    public void EditorShowLeavePreview()
    {
        if (!IsHudReady()) return;
        defeatRoot.SetActive(false);
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
        defeatRoot.SetActive(true);
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
        return game != null && settingsRoot != null && moreRoot != null && leaveRoot != null && defeatRoot != null && mainMenuRoot != null;
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
        BuildDefeat(canvasObject.transform);
        BuildMainMenuCanvas();
    }

    private void BuildTopHud(Transform parent)
    {
        Image levelPill = CreateImage(parent, "Level Pill", LoadSprite("point_box"), layout.levelPillPosition, layout.levelPillSize);
        levelPill.preserveAspect = true;

        boardText = CreateText(parent, "Level Text", "LEVEL 1", layout.levelTextPosition, layout.levelTextSize, layout.levelTextFontSize, TextAlignmentOptions.Center);
        boardText.fontStyle = FontStyles.Bold;
        boardText.outlineColor = new Color32(5, 28, 72, 255);
        boardText.outlineWidth = 0.18f;

        CreateDot(parent, "Red Dot", new Color(0.95f, 0.14f, 0.18f), layout.redDotPosition);
        CreateDot(parent, "Green Dot", new Color(0.2f, 0.86f, 0.1f), layout.greenDotPosition);
        blueDot = CreateDot(parent, "Blue Dot", new Color(0.1f, 0.68f, 0.9f), layout.blueDotPosition);
        yellowDot = CreateDot(parent, "Yellow Dot", new Color(0.98f, 0.76f, 0.12f), layout.yellowDotPosition);
        redText = CreateText(parent, "Red Progress", "0/3", layout.redTextPosition, layout.progressTextSize, layout.progressFontSize, TextAlignmentOptions.Left);
        greenText = CreateText(parent, "Green Progress", "0/3", layout.greenTextPosition, layout.progressTextSize, layout.progressFontSize, TextAlignmentOptions.Left);
        blueText = CreateText(parent, "Blue Progress", "0/2", layout.blueTextPosition, layout.progressTextSize, layout.progressFontSize, TextAlignmentOptions.Left);
        yellowText = CreateText(parent, "Yellow Progress", "0/2", layout.yellowTextPosition, layout.progressTextSize, layout.progressFontSize, TextAlignmentOptions.Left);
        heartsText = CreateText(parent, "Hearts", "\u2665\u2665\u2665", layout.heartsPosition, layout.heartsSize, layout.heartsFontSize, TextAlignmentOptions.Center);
        heartsText.color = new Color(1f, 0.16f, 0.25f);

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
        CreateButton(parent, "Pause Button", LoadSprite("pause_button"), layout.pausePosition, layout.pauseSize, OpenSettings);

        Button previousLevelButton = CreateButton(parent, "Previous Level", LoadSprite("settings_resume_button"), layout.previousLevelPosition, layout.levelPreviewButtonSize, game.LoadPreviousLevel);
        TextMeshProUGUI previousLevelText = CreateText(parent, "Previous Level Text", "BACK", layout.previousLevelPosition, layout.levelPreviewButtonSize - new Vector2(16f, 8f), 25f, TextAlignmentOptions.Center);
        previousLevelButton.gameObject.SetActive(layout.showLevelPreviewButtons);
        previousLevelText.gameObject.SetActive(layout.showLevelPreviewButtons);

        Button nextLevelButton = CreateButton(parent, "Next Level", LoadSprite("settings_resume_button"), layout.nextLevelPosition, layout.levelPreviewButtonSize, game.LoadNextLevel);
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
        TextMeshProUGUI playText = CreateText(playButton.transform, "MainMenuPlayButtonText", layout.mainMenuPlayText, Vector2.zero, layout.mainMenuPlaySize, layout.mainMenuPlayFontSize, TextAlignmentOptions.Center);
        playText.fontWeight = FontWeight.Heavy;

        mainMenuShopPage = CreateFullScreenRoot(mainMenuRoot.transform, "MainMenuShopPage");
        mainMenuLockedPage = CreateFullScreenRoot(mainMenuRoot.transform, "MainMenuLockedPage");

        GameObject tabsRoot = CreateFullScreenRoot(mainMenuRoot.transform, "MainMenuTabs");
        mainMenuShopTab = CreateMainMenuTab(tabsRoot.transform, "MainMenuShopTab", "Shop", menuShopNormalSprite, layout.mainMenuShopTabPosition, () => SelectMainMenuTab(MainMenuTab.Shop));
        mainMenuHomeTab = CreateMainMenuTab(tabsRoot.transform, "MainMenuHomeTab", "Home", menuHomeNormalSprite, layout.mainMenuHomeTabPosition, () => SelectMainMenuTab(MainMenuTab.Home));
        mainMenuLockedTab = CreateMainMenuTab(tabsRoot.transform, "MainMenuLockedTab", "Locked", menuLockedNormalSprite, layout.mainMenuLockedTabPosition, () => SelectMainMenuTab(MainMenuTab.Locked));

        SelectMainMenuTab(MainMenuTab.Home);
        mainMenuRoot.SetActive(false);
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

    private Image CreateMainMenuTab(Transform parent, string name, string label, Sprite sprite, Vector2 position, UnityEngine.Events.UnityAction click)
    {
        Button button = CreateButton(parent, name, sprite, position, layout.mainMenuTabSize, click);
        button.transition = Selectable.Transition.None;
        Image image = button.GetComponent<Image>();
        image.preserveAspect = true;

        TextMeshProUGUI text = CreateText(button.transform, name + "Label", label, layout.mainMenuSelectedLabelOffset, layout.mainMenuSelectedLabelSize, layout.mainMenuSelectedLabelFontSize, TextAlignmentOptions.Center);
        text.fontWeight = FontWeight.Heavy;
        text.gameObject.SetActive(false);
        return image;
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

        settingsRoot.SetActive(false);
        moreRoot.SetActive(false);
        leaveRoot.SetActive(false);
        defeatRoot.SetActive(false);
        SelectMainMenuTab(MainMenuTab.Home);
        mainMenuRoot.SetActive(true);
        mainMenuRoot.transform.SetAsLastSibling();
        game.TogglePause(true);
    }

    private void StartFromMainMenu()
    {
        if (mainMenuRoot == null) return;
        mainMenuRoot.SetActive(false);
        game.TogglePause(false);
    }

    private void BuildSettings(Transform parent)
    {
        settingsRoot = new GameObject("Settings Overlay");
        settingsRoot.transform.SetParent(parent, false);
        CreateImage(settingsRoot.transform, "Dim", null, Vector2.zero, new Vector2(1080f, 1920f)).color = new Color(0f, 0.03f, 0.1f, 0.75f);
        Image panel = CreateImage(settingsRoot.transform, "Settings Tray", LoadSprite("settings_tray"), layout.settingsPanelPosition, layout.settingsPanelSize);
        panel.preserveAspect = true;
        CreateText(settingsRoot.transform, "Title", "SETTINGS", layout.settingsTitlePosition, new Vector2(600f, 84f), 62f, TextAlignmentOptions.Center);
        CreateSettingRow(settingsRoot.transform, "Haptics", "settings_vibration_icon", layout.hapticsPosition, () => hapticsOn = !hapticsOn, () => hapticsOn);
        CreateSettingRow(settingsRoot.transform, "Sounds", "settings_sound_icon", layout.soundsPosition, () => soundOn = !soundOn, () => soundOn);
        CreateSettingRow(settingsRoot.transform, "Music", "settings_music_icon", layout.musicPosition, () => musicOn = !musicOn, () => musicOn);
        CreateButton(settingsRoot.transform, "Resume", LoadSprite("settings_resume_button"), layout.resumePosition, new Vector2(390f, 104f), CloseSettings);
        CreateText(settingsRoot.transform, "Resume Text", "RESUME", layout.resumePosition, new Vector2(350f, 80f), 40f, TextAlignmentOptions.Center);
        CreateButton(settingsRoot.transform, "Quit", LoadSprite("settings_quit_button"), layout.quitPosition, new Vector2(390f, 104f), OpenLeaveConfirmation);
        CreateText(settingsRoot.transform, "Quit Text", "QUIT", layout.quitPosition, new Vector2(350f, 80f), 40f, TextAlignmentOptions.Center);
        CreateButton(settingsRoot.transform, "More", LoadSprite("settings_resume_button"), layout.morePosition, new Vector2(260f, 70f), OpenMorePage);
        CreateText(settingsRoot.transform, "More Text", "MORE", layout.morePosition, new Vector2(230f, 56f), 30f, TextAlignmentOptions.Center);
        CreateButton(settingsRoot.transform, "Close", LoadSprite("settings_close_button"), layout.settingsClosePosition, new Vector2(90f, 90f), CloseSettings);
        settingsRoot.SetActive(false);
    }

    private void BuildMorePage(Transform parent)
    {
        moreRoot = new GameObject("Settings More Overlay");
        moreRoot.transform.SetParent(parent, false);
        CreateImage(moreRoot.transform, "Dim", null, Vector2.zero, new Vector2(1080f, 1920f)).color = new Color(0f, 0.03f, 0.1f, 0.75f);
        Image panel = CreateImage(moreRoot.transform, "More Tray", LoadSprite("settings_tray"), layout.morePanelPosition, layout.morePanelSize);
        panel.preserveAspect = true;
        CreateText(moreRoot.transform, "More Title", "MORE", layout.moreTitlePosition, new Vector2(560f, 84f), 58f, TextAlignmentOptions.Center);
        CreateButton(moreRoot.transform, "Terms", LoadSprite("settings_resume_button"), layout.termsPosition, new Vector2(420f, 90f), null);
        CreateText(moreRoot.transform, "Terms Text", "TERMS", layout.termsPosition, new Vector2(380f, 68f), 36f, TextAlignmentOptions.Center);
        CreateButton(moreRoot.transform, "Privacy", LoadSprite("settings_resume_button"), layout.privacyPosition, new Vector2(420f, 90f), null);
        CreateText(moreRoot.transform, "Privacy Text", "PRIVACY", layout.privacyPosition, new Vector2(380f, 68f), 36f, TextAlignmentOptions.Center);
        CreateButton(moreRoot.transform, "Back", LoadSprite("settings_resume_button"), layout.moreBackPosition, new Vector2(320f, 80f), CloseMorePage);
        CreateText(moreRoot.transform, "Back Text", "BACK", layout.moreBackPosition, new Vector2(280f, 60f), 32f, TextAlignmentOptions.Center);
        CreateButton(moreRoot.transform, "Close", LoadSprite("settings_close_button"), layout.moreClosePosition, new Vector2(90f, 90f), CloseSettings);
        moreRoot.SetActive(false);
    }

    private void BuildLeaveConfirmation(Transform parent)
    {
        leaveRoot = new GameObject("Leave Confirmation Overlay");
        leaveRoot.transform.SetParent(parent, false);
        CreateImage(leaveRoot.transform, "Dim", null, Vector2.zero, new Vector2(1080f, 1920f)).color = new Color(0f, 0.03f, 0.1f, 0.8f);
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
        defeatRoot = new GameObject("Defeat Overlay");
        defeatRoot.transform.SetParent(parent, false);
        CreateImage(defeatRoot.transform, "Dim", null, Vector2.zero, new Vector2(1080f, 1920f)).color = new Color(0f, 0.03f, 0.1f, 0.76f);
        Image panel = CreateImage(defeatRoot.transform, "Defeat Panel", LoadSprite("settings_tray"), Vector2.zero, layout.defeatPanelSize);
        panel.preserveAspect = true;
        CreateText(defeatRoot.transform, "Defeat Title", "NO HEARTS LEFT", layout.defeatTitlePosition, new Vector2(560f, 80f), 50f, TextAlignmentOptions.Center);
        CreateText(defeatRoot.transform, "Defeat Description", "Try this board again.", layout.defeatDescriptionPosition, new Vector2(560f, 60f), 32f, TextAlignmentOptions.Center);
        CreateButton(defeatRoot.transform, "Restart", LoadSprite("settings_resume_button"), layout.restartPosition, new Vector2(380f, 104f), RestartAfterDefeat);
        CreateText(defeatRoot.transform, "Restart Text", "RESTART", layout.restartPosition, new Vector2(330f, 70f), 38f, TextAlignmentOptions.Center);
        defeatRoot.SetActive(false);
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

    private void RestartAfterDefeat()
    {
        defeatRoot.SetActive(false);
        game.RestartCurrentBoard();
        game.TogglePause(false);
    }

    private Image CreateDot(Transform parent, string name, Color color, Vector2 position)
    {
        Image dot = CreateImage(parent, name, null, position, layout.dotSize);
        dot.color = color;
        return dot;
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
