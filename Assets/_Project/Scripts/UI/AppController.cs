using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Single controller for the whole Hub UI application.
///
/// Ownership:
/// - one UIDocument (MainApp.uxml) is the only UI tree,
/// - AppController owns navigation (ShowScreen) and global UI wiring,
/// - individual screens refresh their own subtree and never own global
///   navigation state.
///
/// State flow:
/// GameSession.OnStateChanged -> AppController.RefreshAll() -> every screen
/// reads the session and updates its labels/buttons.
///
/// Binding happens once in Start() against the UIDocument root, which is the
/// normal UI Toolkit lifetime for a scene-owned UIDocument.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AppController : MonoBehaviour
{
    [SerializeField]
    private StyleSheet themeStyleSheet;

    [SerializeField]
    private StyleSheet overviewStyleSheet;

    [SerializeField]
    private StyleSheet preparationStyleSheet;

    [SerializeField]
    private StyleSheet processingStyleSheet;

    [SerializeField]
    private StyleSheet outpostStyleSheet;

    private UIDocument _document;
    private VisualElement _root;
    private GameSession _session;

    private VisualElement _overviewContainer;
    private VisualElement _preparationContainer;
    private VisualElement _processingContainer;
    private VisualElement _outpostContainer;
    private VisualElement _debugPanel;
    private VisualElement _debugContent;

    private OverviewScreen _overview;
    private PreparationScreen _preparation;
    private ProcessingScreen _processing;
    private OutpostScreen _outpost;
    private OverviewPlanetPreview _planetPreview;

    private readonly Button[] _navButtons = new Button[4];
    private readonly VisualElement[] _screenContainers = new VisualElement[4];

    /// <summary>The UIDocument hosting the primary UI application.</summary>
    public UIDocument Document => _document;

    /// <summary>Root of the UI tree bound in Start() (valid after IsReady).</summary>
    public VisualElement UiRoot => _root;

    /// <summary>The authoritative session this controller renders (read-only for tests/tools).</summary>
    public GameSession Session => _session;

    /// <summary>Outpost presentation controller (read-only access for tests;
    /// state stays in GameSession).</summary>
    public OutpostScreen OutpostView => _outpost;

    /// <summary>Overview presentation controller (read-only access for tests).</summary>
    public OverviewScreen OverviewView => _overview;

    /// <summary>The shared rotating 3D planet preview driving the Overview
    /// central visual (Phase 07B.2.4).</summary>
    public OverviewPlanetPreview PlanetPreview => _planetPreview;

    public AppScreen CurrentScreen { get; private set; } = AppScreen.Overview;

    /// <summary>True once Start() finished binding and showing the first screen.
    /// Tests and the standalone probe wait for this before acting.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Number of screen containers currently displayed (must stay 1).</summary>
    public int VisibleScreenCount
    {
        get
        {
            int count = 0;
            foreach (VisualElement container in _screenContainers)
            {
                if (container != null && container.style.display == DisplayStyle.Flex)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Assigns the style sheets; called once by scene authoring tooling.</summary>
    public void Configure(StyleSheet theme, StyleSheet overview, StyleSheet preparation,
        StyleSheet processing, StyleSheet outpost)
    {
        themeStyleSheet = theme;
        overviewStyleSheet = overview;
        preparationStyleSheet = preparation;
        processingStyleSheet = processing;
        outpostStyleSheet = outpost;
    }

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        // Hub may be opened directly (tests/manual); a missing session is
        // created here with defaults. After Boot it simply returns Boot's
        // persistent session, never a duplicate.
        _session = GameSession.Ensure();
        ApplyHubUiMode();
    }

    private void OnEnable()
    {
        ApplyHubUiMode();
    }

    /// <summary>
    /// Hub is a 2D UI screen: the OS cursor must always be unlocked + visible
    /// (HubUiMode). This restores the cursor when returning from the surface
    /// (WristMode / SurfaceFirstPersonMode leave it locked/hidden), and never
    /// depends on object destruction to reset cursor state.
    /// </summary>
    public void ApplyHubUiMode()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private void Start()
    {
        _root = _document.rootVisualElement;
        if (_root == null)
        {
            Debug.LogError("AppController: UIDocument root is null.");
            return;
        }

        if (themeStyleSheet != null)
        {
            _root.styleSheets.Add(themeStyleSheet);
        }

        if (overviewStyleSheet != null)
        {
            _root.styleSheets.Add(overviewStyleSheet);
        }

        if (preparationStyleSheet != null)
        {
            _root.styleSheets.Add(preparationStyleSheet);
        }

        if (processingStyleSheet != null)
        {
            _root.styleSheets.Add(processingStyleSheet);
        }

        if (outpostStyleSheet != null)
        {
            _root.styleSheets.Add(outpostStyleSheet);
        }

        ApplyUiCjkFont();

        CacheContainers();
        BindNavigation();
        BindDebugPanel();
        BindScreens();

        _session.OnStateChanged += HandleSessionChanged;
        RefreshAll();

        // Exactly one visible screen after startup: Overview.
        ShowScreen(AppScreen.Overview);
        IsReady = true;
    }

    private static Font _uiCjkFont;

    /// <summary>
    /// Phase 04 Chinese-first UI: applies a legal runtime system CJK font to
    /// every text element. The font is referenced from the OS (never copied
    /// into the project), so Editor, PlayMode, Standalone and the clean panel
    /// screenshots all render identical glyphs on this machine.
    /// See Docs/UI_LANGUAGE_STYLE.md for the temporary font policy.
    /// </summary>
    private void ApplyUiCjkFont()
    {
        if (_uiCjkFont == null)
        {
            string[] candidates =
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Noto Sans CJK SC",
                "Source Han Sans SC"
            };

            _uiCjkFont = Font.CreateDynamicFontFromOSFont(candidates, 16);
        }

        if (_uiCjkFont == null || _root == null)
        {
            return;
        }

        foreach (TextElement text in _root.Query<TextElement>().ToList())
        {
            text.style.unityFont = _uiCjkFont;
        }
    }

    private void Update()
    {
        // Developer shortcut: F12 reveals/hides the debug panel. Debug UI is
        // never visible in normal play or in competition screenshots.
        if (Keyboard.current != null && Keyboard.current[Key.F12].wasPressedThisFrame)
        {
            ToggleDebugVisibility();
        }

        // Only live progress rendering while the Processing screen is visible;
        // the authoritative timer itself advances in GameSession.Update.
        if (_processing != null)
        {
            _processing.TickProgressIfVisible(CurrentScreen == AppScreen.Processing);
        }
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }

        _planetPreview?.Release(this);
        _planetPreview = null;
    }

    private void CacheContainers()
    {
        _overviewContainer = _root.Q<VisualElement>(ElementNames.OverviewScreen);
        _preparationContainer = _root.Q<VisualElement>(ElementNames.PreparationScreen);
        _processingContainer = _root.Q<VisualElement>(ElementNames.ProcessingScreen);
        _outpostContainer = _root.Q<VisualElement>(ElementNames.OutpostScreen);

        _screenContainers[0] = _overviewContainer;
        _screenContainers[1] = _preparationContainer;
        _screenContainers[2] = _processingContainer;
        _screenContainers[3] = _outpostContainer;
    }

    private void BindNavigation()
    {
        _navButtons[0] = _root.Q<Button>(ElementNames.NavOverview);
        _navButtons[1] = _root.Q<Button>(ElementNames.NavPreparation);
        _navButtons[2] = _root.Q<Button>(ElementNames.NavProcessing);
        _navButtons[3] = _root.Q<Button>(ElementNames.NavOutpost);

        RegisterNav(_navButtons[0], AppScreen.Overview);
        RegisterNav(_navButtons[1], AppScreen.Preparation);
        RegisterNav(_navButtons[2], AppScreen.Processing);
        RegisterNav(_navButtons[3], AppScreen.Outpost);
    }

    private void RegisterNav(Button button, AppScreen screen)
    {
        if (button == null)
        {
            LogMissing(screen.ToString() + " nav button");
            return;
        }

        button.clicked += () => ShowScreen(screen);
    }

    private void BindDebugPanel()
    {
        _debugPanel = _root.Q<VisualElement>(ElementNames.DebugPanel);
        _debugContent = _root.Q<VisualElement>(ElementNames.DebugContent);

        Button toggle = _root.Q<Button>(ElementNames.DebugToggleButton);
        if (toggle != null)
        {
            toggle.clicked += ToggleDebugPanel;
        }
        else
        {
            LogMissing(ElementNames.DebugToggleButton);
        }

        Button addMinerals = _root.Q<Button>(ElementNames.DebugAddMineralsButton);
        if (addMinerals != null)
        {
            addMinerals.clicked += () => _session.AddMinerals(10);
        }
        else
        {
            LogMissing(ElementNames.DebugAddMineralsButton);
        }

        Button addAlloy = _root.Q<Button>(ElementNames.DebugAddAlloyButton);
        if (addAlloy != null)
        {
            addAlloy.clicked += () => _session.AddAlloy(10);
        }
        else
        {
            LogMissing(ElementNames.DebugAddAlloyButton);
        }

        Button reset = _root.Q<Button>(ElementNames.DebugResetSessionButton);
        if (reset != null)
        {
            reset.clicked += () => _session.ResetSession();
        }
        else
        {
            LogMissing(ElementNames.DebugResetSessionButton);
        }
    }

    private void BindScreens()
    {
        // Phase 07B.2.4: the Hub shares the ONE rotating 3D planet preview with
        // the ship's Overview terminal (same rig, same RenderTexture).
        _planetPreview = OverviewPlanetPreview.Acquire(this);

        _overview = new OverviewScreen(_overviewContainer, _session);
        _preparation = new PreparationScreen(_preparationContainer, _session);
        _processing = new ProcessingScreen(_processingContainer, _session);
        _outpost = new OutpostScreen(_outpostContainer, _session);

        _overview.Bind(() => ShowScreen(AppScreen.Preparation), true,
            _planetPreview != null ? _planetPreview.Texture : null);
        _preparation.Bind();
        _processing.Bind();
        _outpost.Bind();
    }

    /// <summary>Central navigation authority: shows exactly one major screen.</summary>
    public void ShowScreen(AppScreen screen)
    {
        bool overview = screen == AppScreen.Overview;
        bool preparation = screen == AppScreen.Preparation;
        bool processing = screen == AppScreen.Processing;
        bool outpost = screen == AppScreen.Outpost;

        SetScreenVisibility(_overviewContainer, overview);
        SetScreenVisibility(_preparationContainer, preparation);
        SetScreenVisibility(_processingContainer, processing);
        SetScreenVisibility(_outpostContainer, outpost);

        SetTabActive(_navButtons[0], overview);
        SetTabActive(_navButtons[1], preparation);
        SetTabActive(_navButtons[2], processing);
        SetTabActive(_navButtons[3], outpost);

        CurrentScreen = screen;

        // The shared planet preview only renders while an Overview host is
        // actually visible (the ship terminal keeps its own viewer registered).
        _planetPreview?.SetViewed(this, overview);
    }

    private static void SetScreenVisibility(VisualElement container, bool visible)
    {
        if (container == null)
        {
            return;
        }

        container.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void SetTabActive(Button tab, bool active)
    {
        if (tab == null)
        {
            return;
        }

        if (active)
        {
            tab.AddToClassList("tab-active");
        }
        else
        {
            tab.RemoveFromClassList("tab-active");
        }
    }

    private void ToggleDebugVisibility()
    {
        if (_debugPanel == null)
        {
            return;
        }

        _debugPanel.style.display =
            _debugPanel.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void ToggleDebugPanel()
    {
        if (_debugContent == null)
        {
            return;
        }

        _debugContent.style.display =
            _debugContent.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void HandleSessionChanged()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        _overview?.Refresh();
        _preparation?.Refresh();
        _processing?.Refresh();
        _outpost?.Refresh();
    }

    private static void LogMissing(string element)
    {
        Debug.LogError("AppController: required UI element missing: " + element);
    }
}
