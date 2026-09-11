using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared loadout page (Phase 07B.2) hosted by the ShipOverlayUi.
///
/// This is the SAME loadout UI reached from two shipboard entry points:
///   A. physical equipment locker in the Airlock  -> LoadoutFocus
///   B. deployment confirmation 调整装备          -> LoadoutFocus
///
/// Implementation: the EXISTING legacy Preparation/MainApp tree is hosted in
/// this screen-space document (identical tree/controller/GameSession
/// authority as Phase 03-06 - never a second loadout implementation), with
/// the Hub chrome hidden. The page is dimmed to read as a shipboard overlay.
/// Equipment rows edit GameSession; the legacy 开始远征 CTA is hidden on the
/// ship (scene routing happens only through the deployment confirmation's
/// 确认降临), and a 返回 button closes back to the correct return context.
///
/// The GameObject/UIDocument stays active; visibility is driven by the root
/// display style so the panel tree binds once and never re-instantiates.
/// </summary>
public class ShipLoadoutPage : MonoBehaviour
{
    public const string CloseButtonName = "LoadoutCloseButton";
    public const string CloseHintName = "LoadoutCloseHint";

    /// <summary>The panel's own copy of the Back verb (the device glyph is added
    /// by InputDeviceTracker, so it reads "[B] 返回" on a controller and
    /// "[Esc] 返回" on the keyboard).</summary>
    public const string BackHintVerb = "返回";

    [SerializeField]
    public StyleSheet themeStyleSheet;

    [SerializeField]
    public StyleSheet preparationStyleSheet;

    [SerializeField]
    public StyleSheet modalStyleSheet;

    // Panel settings assigned at runtime because UIDocument.panelSettings does
    // not serialize reliably from the deterministic scene builder (same
    // convention as the ship HUD / console hosts).
    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private VisualElement _root;
    private GameSession _session;
    private PreparationScreen _preparation;
    private Label _backHint;
    private bool _bound;
    private bool _visible;
    private int _shownFrame = -1000;

    /// <summary>User closed the loadout view (Back action / 返回 button).</summary>
    public event Action CloseRequested;

    public bool IsBound => _bound;

    public VisualElement Root => _root;

    public UIDocument Document => _document;

    public bool IsVisible => _visible;

    /// <summary>The device-aware Back hint the panel really shows
    /// ("[B] 返回" on a controller, "[Esc] 返回" on the keyboard).</summary>
    public string BackHintText => _backHint != null ? _backHint.text : string.Empty;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        if (_document != null && _document.panelSettings == null && panelSettings != null)
        {
            _document.panelSettings = panelSettings;
        }
    }

    private void Update()
    {
        if (!_bound && _document != null && _document.rootVisualElement != null
            && _document.rootVisualElement.childCount > 0)
        {
            EnsureBound();
        }

        // The Back hint names the device the player is actually holding, so it
        // is refreshed while the panel is on screen (gamepad <-> keyboard).
        if (_bound && _visible)
        {
            InputDeviceTracker.Poll();
            RefreshBackHint();
        }
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    private void EnsureBound()
    {
        if (_bound || _document == null || _document.rootVisualElement == null
            || _document.rootVisualElement.childCount == 0)
        {
            return;
        }

        _root = _document.rootVisualElement;
        AddOnce(themeStyleSheet);
        AddOnce(preparationStyleSheet);
        AddOnce(modalStyleSheet);

        // Dim the legacy page slightly so the shipboard view reads as an
        // overlay over the world, not an opaque full-screen swap.
        _root.style.backgroundColor = new Color(0.902f, 0.914f, 0.922f, 0.96f);

        // Reveal only the legacy Preparation screen (shared loadout view).
        SetElementDisplay(ElementNames.OverviewScreen, false);
        SetElementDisplay(ElementNames.ProcessingScreen, false);
        SetElementDisplay(ElementNames.OutpostScreen, false);
        SetElementDisplay(ElementNames.BottomNav, false);
        SetElementDisplay(ElementNames.DebugPanel, false);
        SetElementDisplay(ElementNames.PreparationScreen, true);

        // The shipboard loadout view never launches the expedition itself:
        // routing happens exclusively through the deployment confirmation.
        // The legacy Hub keeps the full confirm/launch ceremony.
        VisualElement confirm = _root.Q<VisualElement>(ElementNames.ConfirmLoadoutButton);
        if (confirm != null)
        {
            confirm.style.display = DisplayStyle.None;
        }

        ApplyUiCjkFont();

        // The gamepad confirm press that OPENED this panel is also turned into a
        // synthetic pointer click by Unity's runtime UI (the same echo that once
        // activated 退出 on the title screen). On the frame the panel appears
        // that click IS the opening press, not a second action: it must not
        // toggle an equipment row or hit 返回 behind the player's back.
        _root.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (IsOpeningPressEcho())
            {
                Debug.Log("[OVERLAY] ignored the click produced by the press "
                          + "that opened 装备整理");
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);

        _session = GameSession.Ensure();
        _preparation = new PreparationScreen(
            Container(ElementNames.PreparationScreen), _session);
        _preparation.Bind();
        _preparation.Refresh();
        _session.OnStateChanged += HandleSessionChanged;

        BuildCloseBar();
        RefreshBackHint();
        _bound = true;
        _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>
    /// True while a click can only be the echo of the press that opened the
    /// panel (the opening frame, plus one frame of delivery slack).
    /// </summary>
    private bool IsOpeningPressEcho()
    {
        return Time.frameCount <= _shownFrame + 1
               && UiInput.GamepadConfirmPressedThisFrame();
    }

    /// <summary>Rewrites the panel's Back hint for the current device.</summary>
    private void RefreshBackHint()
    {
        if (_backHint == null)
        {
            return;
        }

        string text = InputDeviceTracker.FormatPrompt(UiAction.Back, BackHintVerb);
        if (_backHint.text != text)
        {
            _backHint.text = text;
        }
    }

    private void BuildCloseBar()
    {
        // Top-right close control (appended to the document instance only;
        // MainApp.uxml itself is never modified).
        var bar = new VisualElement();
        bar.name = "LoadoutCloseBar";
        bar.pickingMode = PickingMode.Ignore;
        bar.AddToClassList("loadout-close-bar");
        bar.style.position = Position.Absolute;
        bar.style.top = 20f;
        bar.style.right = 26f;
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;

        // The hint names the Back control of the device in hand - the same
        // "[B] 返回" / "[Esc] 返回" wording the rest of the ship uses.
        _backHint = new Label { name = CloseHintName };
        _backHint.AddToClassList("loadout-close-hint");
        bar.Add(_backHint);

        var closeButton = new Button(() => CloseRequested?.Invoke())
        {
            name = CloseButtonName,
            text = "返回"
        };
        closeButton.AddToClassList("loadout-close-btn");
        bar.Add(closeButton);

        _root.Add(bar);
    }

    private void HandleSessionChanged()
    {
        _preparation?.Refresh();
    }

    private void AddOnce(StyleSheet sheet)
    {
        if (sheet != null && _root != null && !_root.styleSheets.Contains(sheet))
        {
            _root.styleSheets.Add(sheet);
        }
    }

    private VisualElement Container(string name)
    {
        VisualElement container = _root != null ? _root.Q<VisualElement>(name) : null;
        if (container == null)
        {
            Debug.LogError("[ShipLoadoutPage] missing screen container " + name);
        }

        return container;
    }

    private void SetElementDisplay(string elementName, bool visible)
    {
        if (_root == null)
        {
            return;
        }

        SetDisplay(_root.Q<VisualElement>(elementName), visible);
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element == null)
        {
            return;
        }

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>Show / hide the page (display style; tree stays bound).</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (visible)
        {
            _shownFrame = Time.frameCount;
            RefreshBackHint();
        }
        else
        {
            ReleaseFocus();
        }

        if (_root != null)
        {
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>
    /// Drops any UI Toolkit focus this page still holds, so a closed panel can
    /// never keep swallowing input (Phase 08 device-panel hotfix: no hidden UI
    /// may own the keyboard focus after Back closed it).
    /// </summary>
    private void ReleaseFocus()
    {
        if (_root == null || _root.focusController == null)
        {
            return;
        }

        Focusable focused = _root.focusController.focusedElement;
        if (focused == null)
        {
            return;
        }

        var element = focused as VisualElement;
        if (element != null && _root.Contains(element))
        {
            focused.Blur();
        }
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
