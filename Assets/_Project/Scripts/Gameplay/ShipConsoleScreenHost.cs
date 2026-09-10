using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Binds one physical console's UIDocument to the EXISTING production UI
/// (Phase 07B). Each console hosts its own instance of the same MainApp tree;
/// this host shows exactly the console's mapped screen, hides the Hub chrome
/// (bottom nav + debug panel + the other three screens) and drives the
/// authoritative *Screen controllers exactly like the legacy AppController
/// does - same classes, same GameSession authority, same CSS sheets. No
/// redesigned ship-specific screen copy exists.
/// </summary>
public class ShipConsoleScreenHost : MonoBehaviour
{
    private const int ConsoleOverview = 1;
    private const int ConsolePreparation = 2;
    private const int ConsoleProcessing = 3;
    private const int ConsoleOutpost = 4;

    [SerializeField]
    private int _consoleId = ConsoleOverview;

    [SerializeField]
    private StyleSheet _themeStyleSheet;

    [SerializeField]
    private StyleSheet _overviewStyleSheet;

    [SerializeField]
    private StyleSheet _preparationStyleSheet;

    [SerializeField]
    private StyleSheet _processingStyleSheet;

    [SerializeField]
    private StyleSheet _outpostStyleSheet;

    private UIDocument _document;
    private VisualElement _root;
    private GameSession _session;

    private OverviewScreen _overview;
    private PreparationScreen _preparation;
    private ProcessingScreen _processing;
    private OutpostScreen _outpost;

    private AppScreen _screen;
    private bool _bound;
    private OverviewPlanetPreview _planetPreview;

    /// <summary>True once this console's UI subtree bound successfully.</summary>
    public bool IsBound => _bound;

    /// <summary>The shared rotating planet preview when this terminal is the
    /// Overview terminal (null on every other console).</summary>
    public OverviewPlanetPreview PlanetPreview => _planetPreview;

    /// <summary>Screen this terminal must display (1..4 mapping).</summary>
    public AppScreen MappedScreen => _screen;

    /// <summary>Number of MainApp screen containers currently visible inside
    /// this console document (must always be exactly one).</summary>
    public int CountVisibleScreens()
    {
        if (_root == null)
        {
            return 0;
        }

        int count = 0;
        string[] names =
        {
            ElementNames.OverviewScreen,
            ElementNames.PreparationScreen,
            ElementNames.ProcessingScreen,
            ElementNames.OutpostScreen
        };

        foreach (string name in names)
        {
            VisualElement container = _root.Q<VisualElement>(name);
            if (container != null && container.style.display == DisplayStyle.Flex)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Builder configuration (scene authoring only).</summary>
    public void Configure(int consoleId, StyleSheet theme, StyleSheet overview,
        StyleSheet preparation, StyleSheet processing, StyleSheet outpost)
    {
        _consoleId = consoleId;
        _themeStyleSheet = theme;
        _overviewStyleSheet = overview;
        _preparationStyleSheet = preparation;
        _processingStyleSheet = processing;
        _outpostStyleSheet = outpost;
    }

    private void OnEnable()
    {
        _session = GameSession.Ensure();
        EnsureBound();
    }

    private void Update()
    {
        // Live progress on the Processing console even while unfocused:
        // authoritative timing stays in GameSession.Update; only the rendered
        // fill/percent needs per-frame refresh while a batch is running.
        if (_processing != null)
        {
            _processing.TickProgressIfVisible(true);
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

    private void EnsureBound()
    {
        if (_bound)
        {
            return;
        }

        _document = GetComponent<UIDocument>();
        if (_document == null || _document.rootVisualElement == null)
        {
            return;
        }

        _root = _document.rootVisualElement;
        AddStyleSheets();
        ApplyUiCjkFont();
        RevealOnlyMappedScreen();
        BindControllers();
        _session.OnStateChanged += HandleSessionChanged;
        RefreshAll();
        _bound = true;
    }

    private void AddStyleSheets()
    {
        // Same five sheets the legacy Hub binds (theme + each screen).
        if (_themeStyleSheet != null && !_root.styleSheets.Contains(_themeStyleSheet))
        {
            _root.styleSheets.Add(_themeStyleSheet);
        }

        AddOnce(_overviewStyleSheet);
        AddOnce(_preparationStyleSheet);
        AddOnce(_processingStyleSheet);
        AddOnce(_outpostStyleSheet);
    }

    private void AddOnce(StyleSheet sheet)
    {
        if (sheet != null && !_root.styleSheets.Contains(sheet))
        {
            _root.styleSheets.Add(sheet);
        }
    }

    private void RevealOnlyMappedScreen()
    {
        _screen = ScreenForConsole(_consoleId);
        VisualElement overview = _root.Q<VisualElement>(ElementNames.OverviewScreen);
        VisualElement preparation = _root.Q<VisualElement>(ElementNames.PreparationScreen);
        VisualElement processing = _root.Q<VisualElement>(ElementNames.ProcessingScreen);
        VisualElement outpost = _root.Q<VisualElement>(ElementNames.OutpostScreen);

        SetDisplay(overview, _screen == AppScreen.Overview);
        SetDisplay(preparation, _screen == AppScreen.Preparation);
        SetDisplay(processing, _screen == AppScreen.Processing);
        SetDisplay(outpost, _screen == AppScreen.Outpost);

        // Hub chrome never appears on a physical terminal.
        SetDisplay(_root.Q<VisualElement>(ElementNames.BottomNav), false);
        SetDisplay(_root.Q<VisualElement>(ElementNames.DebugPanel), false);
    }

    private void BindControllers()
    {
        // Always construct all four controllers (same pattern as AppController
        // BindScreens) so every refresh path stays exercised and session
        // mutations keep every subtree in sync.
        _overview = new OverviewScreen(Container(ElementNames.OverviewScreen), _session);
        _preparation = new PreparationScreen(Container(ElementNames.PreparationScreen), _session);
        _processing = new ProcessingScreen(Container(ElementNames.ProcessingScreen), _session);
        _outpost = new OutpostScreen(Container(ElementNames.OutpostScreen), _session);

        // Phase 07B.2.4: this terminal displays the SAME shared rotating 3D
        // planet preview as the Hub - one rig, one RenderTexture. Because the
        // ship's Overview screen is an always-on physical display, its viewer
        // stays registered for the whole ship session, so the bridge planet
        // keeps turning even while the player looks elsewhere or is in a
        // console focus / wrist state.
        if (_screen == AppScreen.Overview)
        {
            _planetPreview = OverviewPlanetPreview.Acquire(this);
            if (_planetPreview != null)
            {
                _planetPreview.SetViewed(this, true);
                Debug.Log("[ShipConsoleScreenHost] console " + _consoleId
                          + " shares the rotating planet preview"
                          + " rt=" + (_planetPreview.Texture != null
                              ? _planetPreview.Texture.name : "<null>"));
            }
        }

        // Ship 07B.2.3: the physical Overview terminal is ship-context only -
        // it shows planet / mission / status information and must NOT offer the
        // legacy 准备远征 CTA (inside the ship the expedition starts at the
        // Airlock equipment locker + deployment station). The legacy Hub keeps
        // the same view WITH the CTA, driven by the host context flag.
        _overview.Bind(null, false,
            _planetPreview != null ? _planetPreview.Texture : null);
        _preparation.Bind();
        _processing.Bind();
        _outpost.Bind();
    }

    private VisualElement Container(string name)
    {
        VisualElement container = _root.Q<VisualElement>(name);
        if (container == null)
        {
            Debug.LogError("[ShipConsoleScreenHost] console " + _consoleId
                           + " missing screen container " + name);
        }

        return container;
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

    private static AppScreen ScreenForConsole(int consoleId)
    {
        switch (consoleId)
        {
            case ConsolePreparation:
                return AppScreen.Preparation;
            case ConsoleProcessing:
                return AppScreen.Processing;
            case ConsoleOutpost:
                return AppScreen.Outpost;
            default:
                return AppScreen.Overview;
        }
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element == null)
        {
            return;
        }

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static Font _cjkFont;

    private void ApplyUiCjkFont()
    {
        if (_cjkFont == null)
        {
            string[] candidates =
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Noto Sans CJK SC",
                "Source Han Sans SC"
            };

            _cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 16);
        }

        if (_cjkFont == null || _root == null)
        {
            return;
        }

        foreach (TextElement text in _root.Query<TextElement>().ToList())
        {
            text.style.unityFont = _cjkFont;
        }
    }
}
