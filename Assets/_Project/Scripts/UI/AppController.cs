using System;
using UnityEngine;
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
/// Elements are resolved against the LIVE UIDocument root on every operation
/// so state stays consistent even if the panel re-attaches its tree.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AppController : MonoBehaviour
{
    [SerializeField]
    private StyleSheet themeStyleSheet;

    private UIDocument _document;
    private GameSession _session;

    private OverviewScreen _overview;
    private PreparationScreen _preparation;
    private ProcessingScreen _processing;
    private OutpostScreen _outpost;

    /// <summary>The UIDocument hosting the primary UI application.</summary>
    public UIDocument Document => _document;

    /// <summary>Live root of the UI tree (may re-query across panel re-attach).</summary>
    public VisualElement UiRoot => _document != null ? _document.rootVisualElement : null;

    /// <summary>The authoritative session this controller renders (read-only for tests/tools).</summary>
    public GameSession Session => _session;

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
            if (IsVisible(ElementNames.OverviewScreen))
            {
                count++;
            }

            if (IsVisible(ElementNames.PreparationScreen))
            {
                count++;
            }

            if (IsVisible(ElementNames.ProcessingScreen))
            {
                count++;
            }

            if (IsVisible(ElementNames.OutpostScreen))
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>Assigns the placeholder theme; called once by scene authoring tooling.</summary>
    public void Configure(StyleSheet theme)
    {
        themeStyleSheet = theme;
    }

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        // Hub may be opened directly (tests/manual); a missing session is
        // created here with defaults. After Boot it simply returns Boot's
        // persistent session, never a duplicate.
        _session = GameSession.Ensure();
    }

    private void Start()
    {
        VisualElement root = UiRoot;
        if (root == null)
        {
            Debug.LogError("AppController: UIDocument root is null.");
            return;
        }

        if (themeStyleSheet != null)
        {
            root.styleSheets.Add(themeStyleSheet);
        }

        BindNavigation(root);
        BindDebugPanel(root);
        BindScreens(root);

        _session.OnStateChanged += HandleSessionChanged;
        RefreshAll();

        // Exactly one visible screen after startup: Overview.
        ShowScreen(AppScreen.Overview);
        IsReady = true;
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    private void BindNavigation(VisualElement root)
    {
        RegisterNav(root.Q<Button>(ElementNames.NavOverview), AppScreen.Overview);
        RegisterNav(root.Q<Button>(ElementNames.NavPreparation), AppScreen.Preparation);
        RegisterNav(root.Q<Button>(ElementNames.NavProcessing), AppScreen.Processing);
        RegisterNav(root.Q<Button>(ElementNames.NavOutpost), AppScreen.Outpost);
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

    private void BindDebugPanel(VisualElement root)
    {
        Button toggle = root.Q<Button>(ElementNames.DebugToggleButton);
        if (toggle != null)
        {
            toggle.clicked += ToggleDebugPanel;
        }
        else
        {
            LogMissing(ElementNames.DebugToggleButton);
        }

        Button addMinerals = root.Q<Button>(ElementNames.DebugAddMineralsButton);
        if (addMinerals != null)
        {
            addMinerals.clicked += () => _session.AddMinerals(10);
        }
        else
        {
            LogMissing(ElementNames.DebugAddMineralsButton);
        }

        Button addAlloy = root.Q<Button>(ElementNames.DebugAddAlloyButton);
        if (addAlloy != null)
        {
            addAlloy.clicked += () => _session.AddAlloy(10);
        }
        else
        {
            LogMissing(ElementNames.DebugAddAlloyButton);
        }

        Button reset = root.Q<Button>(ElementNames.DebugResetSessionButton);
        if (reset != null)
        {
            reset.clicked += () => _session.ResetSession();
        }
        else
        {
            LogMissing(ElementNames.DebugResetSessionButton);
        }
    }

    private void BindScreens(VisualElement root)
    {
        _overview = new OverviewScreen(_session);
        _preparation = new PreparationScreen(_session);
        _processing = new ProcessingScreen(_session);
        _outpost = new OutpostScreen(_session);

        _overview.Bind(root, () => ShowScreen(AppScreen.Preparation));
        _preparation.Bind(root);
        _processing.Bind(root);
        _outpost.Bind(root);
    }

    /// <summary>Central navigation authority: shows exactly one major screen.</summary>
    public void ShowScreen(AppScreen screen)
    {
        VisualElement root = UiRoot;
        if (root == null)
        {
            return;
        }

        SetScreenVisibility(root, ElementNames.OverviewScreen, screen == AppScreen.Overview);
        SetScreenVisibility(root, ElementNames.PreparationScreen, screen == AppScreen.Preparation);
        SetScreenVisibility(root, ElementNames.ProcessingScreen, screen == AppScreen.Processing);
        SetScreenVisibility(root, ElementNames.OutpostScreen, screen == AppScreen.Outpost);

        SetTabActive(root, ElementNames.NavOverview, screen == AppScreen.Overview);
        SetTabActive(root, ElementNames.NavPreparation, screen == AppScreen.Preparation);
        SetTabActive(root, ElementNames.NavProcessing, screen == AppScreen.Processing);
        SetTabActive(root, ElementNames.NavOutpost, screen == AppScreen.Outpost);

        CurrentScreen = screen;
    }

    private static void SetScreenVisibility(VisualElement root, string containerName, bool visible)
    {
        VisualElement container = root.Q<VisualElement>(containerName);
        if (container == null)
        {
            return;
        }

        container.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void SetTabActive(VisualElement root, string tabName, bool active)
    {
        Button tab = root.Q<Button>(tabName);
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

    private bool IsVisible(string containerName)
    {
        VisualElement root = UiRoot;
        VisualElement container = root != null ? root.Q<VisualElement>(containerName) : null;
        return container != null && container.style.display == DisplayStyle.Flex;
    }

    private void ToggleDebugPanel()
    {
        VisualElement debugContent = UiRoot?.Q<VisualElement>(ElementNames.DebugContent);
        if (debugContent == null)
        {
            return;
        }

        debugContent.style.display =
            debugContent.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void HandleSessionChanged()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        VisualElement root = UiRoot;
        if (root == null)
        {
            return;
        }

        _overview?.Refresh(root);
        _preparation?.Refresh(root);
        _processing?.Refresh(root);
        _outpost?.Refresh(root);
    }

    private static void LogMissing(string element)
    {
        Debug.LogError("AppController: required UI element missing: " + element);
    }
}
