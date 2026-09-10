using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Main Menu presentation controller (Phase 08).
///
/// Ownership: exactly ONE UIDocument (MainMenu.uxml) is the whole menu tree.
/// This controller binds the four real actions, owns the two modals, drives the
/// menu's own motion and the fade into the existing Overview, and hosts the
/// SHARED rotating 3D planet preview (OverviewPlanetPreview) as the hero visual.
///
/// It is deliberately NOT a session or navigation authority:
/// - it never creates / resets / mutates GameSession (opening the menu is not
///   "new game"),
/// - it navigates only through SceneRouting (the project's single routing
///   helper) and always into the pre-existing demo flow.
///
/// Motion contract: with motion disabled the layout is identical and every
/// action still works - only the ambient movement stops.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    [SerializeField]
    private StyleSheet themeStyleSheet;

    [SerializeField]
    private StyleSheet mainMenuStyleSheet;

    // Hero stage reference width used by the scan sweep (logical reference px).
    private const float HeroStageWidth = 600f;
    private const float SweepMargin = 44f;
    private const float SweepSeconds = 12f;

    private UIDocument _document;
    private VisualElement _root;
    private OverviewPlanetPreview _planetPreview;

    private VisualElement _settingsModal;
    private VisualElement _settingsDim;
    private VisualElement _settingsPanel;
    private VisualElement _creditsModal;
    private VisualElement _creditsDim;
    private VisualElement _creditsPanel;
    private VisualElement _hintLayer;
    private VisualElement _scanSweep;
    private VisualElement _statusDot;
    private VisualElement _planetDisc;
    private VisualElement _transitionOverlay;

    private Button _enterMission;
    private Button _settings;
    private Button _credits;
    private Button _exit;
    private Button _settingsClose;
    private Button _creditsClose;
    private Button _motionOn;
    private Button _motionOff;
    private Button _hintsShow;
    private Button _hintsHide;

    private bool _settingsOpen;
    private bool _creditsOpen;
    private float _settingsAlpha;
    private float _creditsAlpha;
    private bool _settingsInteractive;
    private bool _creditsInteractive;

    private bool _transitioning;
    private bool _sceneLoadRequested;
    private float _transitionAlpha;

    /// <summary>True once Start() finished binding and the menu is usable.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Root of the bound UI tree (valid after IsReady).</summary>
    public VisualElement UiRoot => _root;

    /// <summary>The menu's UIDocument.</summary>
    public UIDocument Document => _document;

    /// <summary>The one primary CTA.</summary>
    public Button EnterMissionButton => _enterMission;

    /// <summary>The shared rotating planet RenderTexture shown as the hero.</summary>
    public RenderTexture HeroPlanetTexture { get; private set; }

    /// <summary>True while the hero shows the shared 3D preview RenderTexture.</summary>
    public bool IsHeroPlanetActive { get; private set; }

    /// <summary>Logical settings-modal state.</summary>
    public bool IsSettingsOpen => _settingsOpen;

    /// <summary>Logical credits-modal state.</summary>
    public bool IsCreditsOpen => _creditsOpen;

    /// <summary>
    /// Number of modals currently shown. Phase 08 §53 forbids stacking, so this
    /// must never exceed 1.
    /// </summary>
    public int OpenModalCount
    {
        get
        {
            int count = 0;
            if (_settingsModal != null
                && _settingsModal.resolvedStyle.display == DisplayStyle.Flex)
            {
                count++;
            }

            if (_creditsModal != null
                && _creditsModal.resolvedStyle.display == DisplayStyle.Flex)
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>True while the menu is fading into the existing Overview.</summary>
    public bool IsTransitioning => _transitioning;

    /// <summary>Assigns the style sheets; called once by scene authoring tooling.</summary>
    public void Configure(StyleSheet theme, StyleSheet menu)
    {
        themeStyleSheet = theme;
        mainMenuStyleSheet = menu;
    }

    private void Awake()
    {
        _document = GetComponent<UIDocument>();

        // The Main Menu is a 2D UI screen: keep the OS cursor usable (returning
        // from the wrist/surface modes leaves it locked and hidden).
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private void Start()
    {
        _root = _document != null ? _document.rootVisualElement : null;
        if (_root == null)
        {
            Debug.LogError("MainMenuController: UIDocument root is null.");
            return;
        }

        if (themeStyleSheet != null)
        {
            _root.styleSheets.Add(themeStyleSheet);
        }

        if (mainMenuStyleSheet != null)
        {
            _root.styleSheets.Add(mainMenuStyleSheet);
        }

        UiFontProvider.ApplyTo(_root);

        // A player build runs Resources.UnloadUnusedAssets on every scene load.
        // The panel settings asset of a scene that is being replaced must not be
        // released while UI Toolkit still holds its panel state, so the menu pins
        // it for the process lifetime. (Editor only reads, so nothing is dirtied.)
        if (!Application.isEditor && _document != null && _document.panelSettings != null)
        {
            _document.panelSettings.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }

        BindElements();
        BindActions();
        BindModalElements();
        BindHero();
        ApplySettings();
        IsReady = true;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[Key.Escape].wasPressedThisFrame)
        {
            HandleEscapeKey();
        }

        UpdateAmbientMotion();
        UpdateModalAnimation();
        UpdateTransition();
    }

    /// <summary>
    /// ESC behavior (§53): close an open modal, and never exit the application.
    /// Extracted from the key check so the behavior itself is verifiable in
    /// automated runs that have no keyboard device.
    /// </summary>
    public void HandleEscapeKey()
    {
        CloseOpenModal();
    }

    private void OnDestroy()
    {
        // The shared rig outlives this scene in the Editor; in a player build it
        // is retired with the scene, because the legacy Hub cannot host it.
        _planetPreview?.Release(this);
        _planetPreview = null;
    }

    // ---- Binding ----

    private void BindElements()
    {
        _enterMission = BindButton(MainMenuNames.EnterMissionButton);
        _settings = BindButton(MainMenuNames.SettingsButton);
        _credits = BindButton(MainMenuNames.CreditsButton);
        _exit = BindButton(MainMenuNames.ExitButton);

        _hintLayer = BindElement(MainMenuNames.HintLayer);
        _scanSweep = BindElement(MainMenuNames.HeroScanSweep);
        _statusDot = BindElement(MainMenuNames.StatusDot);
        _planetDisc = BindElement(MainMenuNames.HeroPlanetDisc);
        _transitionOverlay = BindElement(MainMenuNames.TransitionOverlay);

        if (_planetDisc != null)
        {
            // The hero is a pure visual: it must never steal a pointer that
            // belongs to the CTA or the secondary navigation.
            _planetDisc.pickingMode = PickingMode.Ignore;
        }

        if (_transitionOverlay != null)
        {
            _transitionOverlay.style.display = DisplayStyle.None;
            _transitionOverlay.style.opacity = 0f;
        }
    }

    private void BindActions()
    {
        if (_enterMission != null)
        {
            _enterMission.clicked += RequestEnterMission;
        }

        if (_settings != null)
        {
            _settings.clicked += ShowSettings;
        }

        if (_credits != null)
        {
            _credits.clicked += ShowCredits;
        }

        if (_exit != null)
        {
            _exit.clicked += RequestQuit;
        }
    }

    private void BindModalElements()
    {
        _settingsModal = BindElement(MainMenuNames.SettingsModal);
        _settingsDim = BindElement(MainMenuNames.SettingsDim);
        _settingsPanel = BindElement(MainMenuNames.SettingsPanel);
        _creditsModal = BindElement(MainMenuNames.CreditsModal);
        _creditsDim = BindElement(MainMenuNames.CreditsDim);
        _creditsPanel = BindElement(MainMenuNames.CreditsPanel);

        _settingsClose = BindButton(MainMenuNames.SettingsCloseButton);
        _creditsClose = BindButton(MainMenuNames.CreditsCloseButton);

        if (_settingsClose != null)
        {
            _settingsClose.clicked += HideSettings;
        }

        if (_creditsClose != null)
        {
            _creditsClose.clicked += HideCredits;
        }

        _motionOn = BindButton(MainMenuNames.MotionOnButton);
        _motionOff = BindButton(MainMenuNames.MotionOffButton);
        _hintsShow = BindButton(MainMenuNames.HintsShowButton);
        _hintsHide = BindButton(MainMenuNames.HintsHideButton);

        if (_motionOn != null)
        {
            _motionOn.clicked += () => SetMotion(true);
        }

        if (_motionOff != null)
        {
            _motionOff.clicked += () => SetMotion(false);
        }

        if (_hintsShow != null)
        {
            _hintsShow.clicked += () => SetHints(true);
        }

        if (_hintsHide != null)
        {
            _hintsHide.clicked += () => SetHints(false);
        }

        SetModalVisible(_settingsModal, false);
        SetModalVisible(_creditsModal, false);
        ApplyPickingMode(_settingsModal, false);
        ApplyPickingMode(_creditsModal, false);
        _settingsOpen = false;
        _creditsOpen = false;
        _settingsAlpha = 0f;
        _creditsAlpha = 0f;
        _settingsInteractive = false;
        _creditsInteractive = false;
    }

    /// <summary>
    /// Hosts the ONE shared rotating 3D planet preview (Phase 07B.2.4) as the
    /// Main Menu hero visual. The menu never builds a second rig, camera or
    /// RenderTexture - it displays the same RenderTexture the Overview uses.
    /// </summary>
    private void BindHero()
    {
        _planetPreview = OverviewPlanetPreview.Acquire(this);
        if (_planetPreview == null)
        {
            Debug.LogError("MainMenuController: shared planet preview unavailable.");
            return;
        }

        HeroPlanetTexture = _planetPreview.Texture;
        if (_planetDisc != null && HeroPlanetTexture != null)
        {
            _planetDisc.style.backgroundImage =
                new StyleBackground(Background.FromRenderTexture(HeroPlanetTexture));
            IsHeroPlanetActive = true;
        }

        _planetPreview.SetViewed(this, true);
    }

    // ---- Primary action ----

    /// <summary>
    /// The 进入任务 action: fade, then enter the EXISTING demo flow
    /// (Main Menu -> Overview) through the project routing helper.
    ///
    /// It never resets or recreates the session: entering the demo is
    /// navigation, not a new game.
    /// </summary>
    public void RequestEnterMission()
    {
        if (_transitioning || _sceneLoadRequested || IsAnyModalOpen)
        {
            // While a modal is open the background is blocked (§20).
            return;
        }

        _transitioning = true;
        _transitionAlpha = 0f;

        if (_transitionOverlay != null)
        {
            _transitionOverlay.style.display = DisplayStyle.Flex;
            _transitionOverlay.style.opacity = 0f;
            _transitionOverlay.pickingMode = PickingMode.Position;
        }
    }

    // ---- Modals ----

    /// <summary>Opens 系统设置, closing any other modal first (no stacking).</summary>
    public void ShowSettings()
    {
        _creditsOpen = false;
        _settingsOpen = true;
        SetModalVisible(_settingsModal, true);
    }

    /// <summary>Closes 系统设置.</summary>
    public void HideSettings()
    {
        _settingsOpen = false;
    }

    /// <summary>Opens 制作信息, closing any other modal first (no stacking).</summary>
    public void ShowCredits()
    {
        _settingsOpen = false;
        _creditsOpen = true;
        SetModalVisible(_creditsModal, true);
    }

    /// <summary>Closes 制作信息.</summary>
    public void HideCredits()
    {
        _creditsOpen = false;
    }

    /// <summary>Closes whatever modal is open (ESC behavior, §53).</summary>
    public void CloseOpenModal()
    {
        if (_settingsOpen)
        {
            HideSettings();
        }
        else if (_creditsOpen)
        {
            HideCredits();
        }
    }

    /// <summary>True while either modal is logically open.</summary>
    public bool IsAnyModalOpen => _settingsOpen || _creditsOpen;

    // ---- Exit ----

    /// <summary>
    /// The 退出 action.
    ///
    /// In a player this ends the application. Inside the Unity Editor it must
    /// never terminate the editor process, so the guarded branch simply leaves
    /// Play Mode; both paths are error-free.
    /// </summary>
    public static void RequestQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- Per-frame presentation ----

    private void HandleEscape()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[Key.Escape].wasPressedThisFrame)
        {
            return;
        }

        // ESC only closes an open modal - it never exits the application (§53).
        CloseOpenModal();
    }

    private void UpdateAmbientMotion()
    {
        bool motion = MainMenuSettings.MotionEnabled;

        if (_scanSweep != null)
        {
            // Transform, never a layout property: writing `left` every frame
            // invalidates layout forever, which leaves the panel's teardown
            // spinning in a player build.
            float t = motion
                ? Mathf.Repeat(Time.unscaledTime / SweepSeconds, 1f)
                : 0.5f;
            _scanSweep.style.translate = new Translate(
                t * (HeroStageWidth - SweepMargin * 2f), 0f);
            _scanSweep.style.opacity = motion ? 0.55f : 0.18f;
        }

        if (_statusDot != null)
        {
            _statusDot.style.opacity = motion
                ? 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.6f))
                : 1f;
        }
    }

    private void UpdateModalAnimation()
    {
        // Steady state: no style writes at all. Writing layout-affecting style
        // every frame (even the same value) keeps the panel invalidated forever,
        // which leaves UI Toolkit's teardown spinning in a player build.
        float settingsTarget = _settingsOpen ? 1f : 0f;
        float creditsTarget = _creditsOpen ? 1f : 0f;
        if (_settingsAlpha == settingsTarget && _creditsAlpha == creditsTarget)
        {
            return;
        }

        float step = MotionEnabledStep(MainMenuSettings.ModalSeconds);
        _settingsAlpha = Mathf.MoveTowards(_settingsAlpha, settingsTarget, step);
        _creditsAlpha = Mathf.MoveTowards(_creditsAlpha, creditsTarget, step);

        ApplyModalAlpha(_settingsModal, _settingsDim, _settingsPanel, _settingsAlpha, _settingsOpen);
        ApplyModalAlpha(_creditsModal, _creditsDim, _creditsPanel, _creditsAlpha, _creditsOpen);

        // Only an OPEN modal may block the menu behind it.
        SetModalInteractive(_settingsModal, ref _settingsInteractive, _settingsOpen);
        SetModalInteractive(_creditsModal, ref _creditsInteractive, _creditsOpen);
    }

    private void UpdateTransition()
    {
        if (!_transitioning || _sceneLoadRequested)
        {
            return;
        }

        _transitionAlpha = Mathf.MoveTowards(_transitionAlpha, 1f,
            MotionEnabledStep(MainMenuSettings.TransitionSeconds));

        if (_transitionOverlay != null)
        {
            _transitionOverlay.style.opacity = _transitionAlpha;
        }

        if (_transitionAlpha < 1f)
        {
            return;
        }

        _sceneLoadRequested = true;
        SceneRouting.EnterHub();
    }

    /// <summary>Per-frame alpha step; motion disabled resolves instantly.</summary>
    private static float MotionEnabledStep(float seconds)
    {
        if (!MainMenuSettings.MotionEnabled || seconds <= 0f)
        {
            return 1f;
        }

        return Time.unscaledDeltaTime / seconds;
    }

    // ---- Settings application ----

    /// <summary>动态效果: applies the preference and refreshes the switch UI.</summary>
    public void SetMotion(bool enabled)
    {
        MainMenuSettings.SetMotion(enabled);
        ApplySettings();
    }

    /// <summary>界面提示: applies the preference and refreshes the switch UI.</summary>
    public void SetHints(bool visible)
    {
        MainMenuSettings.SetHints(visible);
        ApplySettings();
    }

    /// <summary>
    /// Reflects <see cref="MainMenuSettings"/> into the tree. Called on bind and
    /// after every settings change; always keeps the layout intact.
    /// </summary>
    public void ApplySettings()
    {
        SetToggleState(_motionOn, MainMenuSettings.MotionEnabled);
        SetToggleState(_motionOff, !MainMenuSettings.MotionEnabled);
        SetToggleState(_hintsShow, MainMenuSettings.HintsVisible);
        SetToggleState(_hintsHide, !MainMenuSettings.HintsVisible);

        if (_hintLayer != null)
        {
            // The annotation layer is absolutely positioned, so hiding it can
            // never move the title, the CTA or the hero.
            _hintLayer.style.display = MainMenuSettings.HintsVisible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    private static void SetToggleState(Button button, bool active)
    {
        if (button == null)
        {
            return;
        }

        if (active)
        {
            button.AddToClassList("segment-active");
        }
        else
        {
            button.RemoveFromClassList("segment-active");
        }
    }

    private static void SetModalVisible(VisualElement modal, bool visible)
    {
        if (modal != null)
        {
            modal.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static void ApplyModalAlpha(VisualElement modal, VisualElement dim,
        VisualElement panel, float alpha, bool open)
    {
        if (modal == null)
        {
            return;
        }

        if (dim != null)
        {
            dim.style.opacity = alpha;
        }

        if (panel != null)
        {
            panel.style.opacity = alpha;
            // Slide with a transform: a layout property (marginTop) written every
            // frame keeps the panel invalidated and makes the panel teardown spin
            // in a player build.
            panel.style.translate = new Translate(0f, Mathf.Lerp(16f, 0f, alpha));
        }

        // Keep it drawn while it fades out, then remove it entirely. A closed
        // modal must never keep intercepting pointers while it fades, and the
        // display property is only written when it actually changes.
        bool visible = open || alpha > 0.001f;
        DisplayStyle wanted = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (modal.resolvedStyle.display != wanted)
        {
            modal.style.display = wanted;
        }
    }

    /// <summary>
    /// Enables/disables pointer interception for a whole modal subtree. Called
    /// only on state changes: a fading-out modal must not swallow a click that
    /// belongs to the menu behind it.
    /// </summary>
    private static void SetModalInteractive(VisualElement modal, ref bool applied, bool interactive)
    {
        if (modal == null || applied == interactive)
        {
            return;
        }

        applied = interactive;
        ApplyPickingMode(modal, interactive);
    }

    private static void ApplyPickingMode(VisualElement element, bool interactive)
    {
        element.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
        foreach (VisualElement child in element.Children())
        {
            ApplyPickingMode(child, interactive);
        }
    }

    private Button BindButton(string name)
    {
        Button button = _root.Q<Button>(name);
        if (button == null)
        {
            LogMissing(name);
        }

        return button;
    }

    private VisualElement BindElement(string name)
    {
        VisualElement element = _root.Q<VisualElement>(name);
        if (element == null)
        {
            LogMissing(name);
        }

        return element;
    }

    private static void LogMissing(string name)
    {
        Debug.LogError("MainMenuController: missing UI element " + name);
    }
}
