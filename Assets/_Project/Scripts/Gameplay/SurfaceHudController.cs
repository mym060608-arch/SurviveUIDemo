using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Lightweight Chinese-first first-person HUD for the NEREID-04 surface.
///
/// Normal exploration shows: mission context, real resource readouts, the
/// gaze interaction prompt, a small center crosshair, the navigation readout,
/// temporary harvest/transfer feedback and the return action. When the wrist
/// terminal opens (M/I/Esc) the whole normal layer hides and SurfaceWrist-
/// Controller owns the screen instead; the world keeps running underneath.
/// </summary>
public class SurfaceHudController : MonoBehaviour
{
    public enum HudCrosshairState
    {
        None,
        ValidTarget,
        BlockedTarget
    }

    [SerializeField]
    public StyleSheet hudStyleSheet;

    [SerializeField]
    public StyleSheet wristStyleSheet;

    // Panel settings wired at runtime: UIDocument.panelSettings does not
    // serialize reliably from the deterministic scene builder, so the builder
    // stores the asset on this controller instead.
    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _hudLayer;
    private SurfaceGameController _controller;
    private GameSession _session;

    private Label _minerals;
    private Label _efficiency;
    private Label _nodes;
    private Label _prompt;
    private Label _status;
    private Label _statusPercent;
    private Label _result;
    private Button _returnButton;
    private VisualElement _statusCard;

    private VisualElement _crosshairDot;
    private VisualElement _crosshairRing;
    private VisualElement _navBlock;
    private Label _navName;
    private Label _navDistance;

    private VisualElement _overlayCursor;
    private bool _cursorEnabled;
    private bool _cursorHover;

    private bool _bound;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        if (_document != null)
        {
            if (_document.panelSettings == null && panelSettings != null)
            {
                _document.panelSettings = panelSettings;
            }

            if (_document.rootVisualElement != null)
            {
                BindElements(_document.rootVisualElement);
            }
        }

        _session = GameSession.Instance;
        if (_session != null)
        {
            _session.OnStateChanged += HandleSessionChanged;
        }
    }

    private void OnDisable()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    /// <summary>Called once the surface controller is ready (Start order).</summary>
    public void Bind(SurfaceGameController controller)
    {
        _controller = controller;
        if (_document != null && _document.rootVisualElement != null)
        {
            BindElements(_document.rootVisualElement);
        }
    }

    private void BindElements(VisualElement root)
    {
        _root = root;
        if (!_bound && hudStyleSheet != null)
        {
            root.styleSheets.Add(hudStyleSheet);
            if (wristStyleSheet != null)
            {
                root.styleSheets.Add(wristStyleSheet);
            }

            _bound = true;
        }

        ApplyUiCjkFont();

        _hudLayer = root.Q<VisualElement>("HudLayer");
        _minerals = root.Q<Label>("HudMineralsValue");
        _efficiency = root.Q<Label>("HudEfficiencyValue");
        _nodes = root.Q<Label>("HudNodesValue");
        _prompt = root.Q<Label>("HudPrompt");
        _status = root.Q<Label>("HudStatusText");
        _statusPercent = root.Q<Label>("HudStatusPercent");
        _result = root.Q<Label>("HudResult");
        _statusCard = root.Q<VisualElement>("HudStatusCard");
        _returnButton = root.Q<Button>("HudReturnButton");
        _crosshairDot = root.Q<VisualElement>("HudCrosshairDot");
        _crosshairRing = root.Q<VisualElement>("HudCrosshairRing");
        _navBlock = root.Q<VisualElement>("HudNavBlock");
        _navName = root.Q<Label>("HudNavName");
        _navDistance = root.Q<Label>("HudNavDistance");
        _overlayCursor = root.Q<VisualElement>("WristCursorOverlay");
        if (_overlayCursor != null)
        {
            _overlayCursor.pickingMode = PickingMode.Ignore;
            _overlayCursor.style.display = DisplayStyle.None;
        }

        if (_returnButton != null && _controller != null)
        {
            _returnButton.clicked += _controller.ReturnToHub;
        }
    }

    private static Font _cjkFont;

    /// <summary>Phase 06 Chinese-first HUD: OS runtime CJK font, same policy
    /// as AppController (never copied into the project).</summary>
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

    private void HandleSessionChanged()
    {
        RefreshCore();
    }

    /// <summary>Refreshes real GameSession-derived numbers (event driven).</summary>
    public void RefreshCore()
    {
        if (_root == null || _session == null)
        {
            return;
        }

        SetText(_minerals, _session.Minerals.ToString());
        SetText(_efficiency, _session.HarvesterEfficiencyPercent + "%");
        SetText(_nodes, _session.HarvestedNodeCount + " / " + GameSession.SurfaceNodeCount);

        bool canReturn = _session.HarvestedNodeCount > 0;
        if (_returnButton != null)
        {
            _returnButton.style.display = canReturn ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Wrist terminal mode: the whole normal exploration layer hides
    /// so the world + wrist stay uncluttered.</summary>
    public void SetWristMode(bool wristOpen)
    {
        if (_hudLayer != null)
        {
            _hudLayer.style.display = wristOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    // ---- Wrist-mode screen-space dot cursor overlay (round 6) ----

    /// <summary>Enables/disables the overlay dot cursor. It lives on the
    /// screen-space HUD document (never in the wrist RenderTexture) and only
    /// mirrors the real mouse position; picking is ignored so it never blocks
    /// the wrist display hit-test.</summary>
    public void SetWristCursorEnabled(bool enabled)
    {
        _cursorEnabled = enabled;
        if (!enabled && _overlayCursor != null)
        {
            _overlayCursor.style.display = DisplayStyle.None;
        }

        _cursorHover = false;
    }

    /// <summary>Driven every frame while WristMode is open: reads the real
    /// Mouse.current position (bottom-left origin) and maps it to the panel's
    /// top-left origin with the Y flip.</summary>
    public void TickWristCursor(bool hoverNode)
    {
        if (!_cursorEnabled || _overlayCursor == null)
        {
            return;
        }

        if (_root == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            _overlayCursor.style.display = DisplayStyle.None;
            return;
        }

        _overlayCursor.style.display = DisplayStyle.Flex;
        Vector2 screen = mouse.position.ReadValue();
        Rect rootBounds = _root.worldBound;
        if (rootBounds.width < 10f)
        {
            return;
        }

        float localX = screen.x - rootBounds.xMin - 6f;
        float localY = rootBounds.yMax - screen.y - 6f;
        _overlayCursor.style.left = localX;
        _overlayCursor.style.top = localY;

        if (hoverNode != _cursorHover)
        {
            _cursorHover = hoverNode;
            if (hoverNode)
            {
                _overlayCursor.AddToClassList("over-node");
            }
            else
            {
                _overlayCursor.RemoveFromClassList("over-node");
            }
        }
    }

    /// <summary>Whether the dot cursor is currently shown (tests).</summary>
    public bool IsWristCursorVisible =>
        _cursorEnabled && _overlayCursor != null
        && _overlayCursor.style.display == DisplayStyle.Flex;

    /// <summary>Whether the dot cursor mode is armed while WristMode is open
    /// (tests; visibility also needs a real mouse device).</summary>
    public bool IsWristCursorEnabled => _cursorEnabled;

    public void SetPrompt(string text)
    {
        if (_prompt != null)
        {
            _prompt.text = text;
        }
    }

    public void SetNodeStatus(string statusText, float progress01)
    {
        bool active = statusText != "—";
        if (_statusCard != null)
        {
            _statusCard.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_status != null)
        {
            _status.text = statusText;
        }

        if (_statusPercent != null)
        {
            _statusPercent.text = active
                ? Mathf.RoundToInt(progress01 * 100f) + "%"
                : "";
        }
    }

    /// <summary>Crosshair feedback for the gaze interaction state.</summary>
    public void SetCrosshairState(HudCrosshairState state)
    {
        if (_crosshairDot == null || _crosshairRing == null)
        {
            return;
        }

        _crosshairDot.RemoveFromClassList("dot-valid");
        _crosshairDot.RemoveFromClassList("dot-blocked");
        _crosshairRing.RemoveFromClassList("ring-visible");
        _crosshairRing.RemoveFromClassList("ring-valid");
        _crosshairRing.RemoveFromClassList("ring-blocked");

        switch (state)
        {
            case HudCrosshairState.ValidTarget:
                _crosshairDot.AddToClassList("dot-valid");
                _crosshairRing.AddToClassList("ring-visible");
                _crosshairRing.AddToClassList("ring-valid");
                break;
            case HudCrosshairState.BlockedTarget:
                _crosshairDot.AddToClassList("dot-blocked");
                _crosshairRing.AddToClassList("ring-visible");
                _crosshairRing.AddToClassList("ring-blocked");
                break;
        }
    }

    /// <summary>Restrained exploration navigation readout.</summary>
    public void SetNavigation(string nodeName, string distanceText, bool visible)
    {
        if (_navBlock == null)
        {
            return;
        }

        _navBlock.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (visible)
        {
            SetText(_navName, "◆ " + nodeName);
            SetText(_navDistance, distanceText);
        }
    }

    /// <summary>Transient transfer result shown next to the prompt area.
    /// Hidden again when cleared (no empty pill over the world).</summary>
    public void ShowTransferResult(string text)
    {
        if (_result == null)
        {
            return;
        }

        _result.text = text;
        _result.style.display = string.IsNullOrEmpty(text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    private static void SetText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }
}
