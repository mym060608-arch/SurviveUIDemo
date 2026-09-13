using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Minimal Chinese-first ship interior HUD (Phase 07 Checkpoint A).
///
/// While walking the ship in first person it shows: a small compartment
/// tag (中文主标签 + restrained English metadata), the gaze interaction
/// prompt (bottom centre, dark chip) and a centre crosshair that reflects
/// valid/blocked interaction targets. There is intentionally no wrist or
/// resource card yet - the four production screens stay authoritative and
/// arrive on physical consoles in checkpoint 07B.
/// </summary>
public class ShipHudController : MonoBehaviour
{
    public enum ShipHudCrosshairState
    {
        None,
        ValidTarget,
        BlockedTarget
    }

    [SerializeField]
    public StyleSheet hudStyleSheet;

    // Panel settings assigned at runtime because UIDocument.panelSettings does
    // not serialize reliably from the deterministic scene builder (same
    // convention as the surface HUD).
    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private VisualElement _root;
    private bool _bound;
    private bool _suppressed;
    private string _focusPrompt;

    private Label _zoneName;
    private Label _zoneMeta;
    private VisualElement _zoneChip;
    private Label _prompt;
    private VisualElement _crosshairDot;
    private VisualElement _crosshairRing;
    private ControllerPromptBar _promptBar;
    private ControllerPromptRow[] _promptRows;

    private static Font _cjkFont;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        if (_document != null && _document.panelSettings == null && panelSettings != null)
        {
            _document.panelSettings = panelSettings;
        }

        EnsureBound();
    }

    /// <summary>Called by ShipGameController once the scene is ready.</summary>
    public void Bind(ShipGameController controller)
    {
        EnsureBound();
    }

    private void EnsureBound()
    {
        if (_bound || _document == null || _document.rootVisualElement == null)
        {
            return;
        }

        _root = _document.rootVisualElement;
        if (hudStyleSheet != null)
        {
            _root.styleSheets.Add(hudStyleSheet);
        }

        ApplyUiCjkFont();

        _zoneChip = _root.Q<VisualElement>("ShipZoneChip");
        _zoneName = _root.Q<Label>("ShipZoneName");
        _zoneMeta = _root.Q<Label>("ShipZoneMeta");
        _prompt = _root.Q<Label>("ShipPrompt");
        _crosshairDot = _root.Q<VisualElement>("ShipCrosshairDot");
        _crosshairRing = _root.Q<VisualElement>("ShipCrosshairRing");

        // Phase 09B PASS 3: the SHARED controller prompt bar lives on the document
        // ROOT (outside the exploration chrome that SetUiSuppressed hides), so the
        // ship wrist terminal can show its own key rows exactly like the surface
        // map does. It stays hidden until a page publishes rows.
        _promptBar = ControllerPromptBar.Attach(_root, _cjkFont);
        _bound = true;
    }

    /// <summary>The shared prompt bar of this document (tests / probes).</summary>
    public ControllerPromptBar PromptBar => _promptBar;

    /// <summary>
    /// Shows the prompt rows for the current page state, or hides the bar. The
    /// rows are only rebuilt when the page really changed them (reference
    /// compare), and the glyphs follow the device the player is holding.
    /// </summary>
    public void SetControllerPrompt(bool visible, ControllerPromptRow[] rows)
    {
        EnsureBound();
        if (_promptBar == null)
        {
            return;
        }

        if (!visible)
        {
            _promptBar.SetVisible(false);
            return;
        }

        if (rows != null && !ReferenceEquals(_promptRows, rows))
        {
            _promptRows = rows;
            _promptBar.SetRows(rows);
        }

        _promptBar.Tick();
        _promptBar.SetVisible(true);
    }

    /// <summary>
    /// Hides the exploration HUD chrome (prompt / CROSSHAIR / zone) while the
    /// wrist terminal, a focused console or a shipboard overlay is open; a later
    /// false restores the normal dynamic refresh.
    ///
    /// Phase 07B.2.3: the crosshair is a first-person mouse-look presentation
    /// element and MUST disappear whenever the pointer belongs to another owner
    /// (wrist, console screen, screen-space modal) - previously only its state
    /// classes were cleared, so the neutral dot stayed drawn over the wrist.
    /// </summary>
    public void SetUiSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        EnsureBound();
        SetCrosshairVisible(!suppressed);
        if (suppressed)
        {
            if (_prompt != null && _focusPrompt == null)
            {
                _prompt.style.display = DisplayStyle.None;
            }

            if (_crosshairDot != null)
            {
                _crosshairDot.RemoveFromClassList("ship-dot-valid");
                _crosshairDot.RemoveFromClassList("ship-dot-blocked");
            }

            if (_crosshairRing != null)
            {
                _crosshairRing.RemoveFromClassList("ship-ring-visible");
                _crosshairRing.RemoveFromClassList("ship-ring-valid");
                _crosshairRing.RemoveFromClassList("ship-ring-blocked");
            }
        }
    }

    /// <summary>Explicit crosshair visibility (dot + ring). A hidden crosshair
    /// is never picked by the UI Toolkit pointer either.</summary>
    public void SetCrosshairVisible(bool visible)
    {
        DisplayStyle style = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (_crosshairDot != null)
        {
            _crosshairDot.style.display = style;
        }

        if (_crosshairRing != null)
        {
            _crosshairRing.style.display = style;
        }
    }

    /// <summary>True while the centre crosshair dot is actually drawn (used by
    /// WristCrosshairVisibility_Runtime).</summary>
    public bool IsCrosshairVisible => _crosshairDot != null
                                      && _crosshairDot.resolvedStyle.display
                                      != DisplayStyle.None;

    /// <summary>Chinese-first compartment tag; empty Chinese name hides it.</summary>
    public void SetZoneTag(string zhName, string meta)
    {
        EnsureBound();
        if (_zoneChip == null)
        {
            return;
        }

        bool visible = !_suppressed && !string.IsNullOrEmpty(zhName);
        _zoneChip.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (visible)
        {
            SetText(_zoneName, zhName);
            SetText(_zoneMeta, meta);
        }
    }

    /// <summary>Restrained interaction prompt (bottom centre). Empty hides it.</summary>
    public void SetPrompt(string text)
    {
        EnsureBound();
        if (_prompt == null || _focusPrompt != null)
        {
            // A console-view hint owns the prompt line while it is up.
            return;
        }

        if (_suppressed)
        {
            _prompt.style.display = DisplayStyle.None;
            return;
        }

        _prompt.text = text;
        _prompt.style.display = string.IsNullOrEmpty(text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    /// <summary>
    /// Shows the console-view hint ("[B] 返回") even while the rest of the HUD
    /// chrome is suppressed: the player must always be told how to leave the
    /// focused terminal. Passing null clears it.
    /// </summary>
    public void SetFocusPrompt(string text)
    {
        EnsureBound();
        _focusPrompt = string.IsNullOrEmpty(text) ? null : text;

        if (_prompt == null)
        {
            return;
        }

        if (_focusPrompt == null)
        {
            _prompt.style.display = DisplayStyle.None;
            return;
        }

        _prompt.text = _focusPrompt;
        _prompt.style.display = DisplayStyle.Flex;
    }

    /// <summary>Clears the console-view hint (gaze prompts take over again).</summary>
    public void ClearFocusPrompt()
    {
        if (_focusPrompt == null)
        {
            return;
        }

        _focusPrompt = null;
        EnsureBound();
        if (_prompt != null)
        {
            _prompt.style.display = DisplayStyle.None;
        }
    }

    /// <summary>The console-view hint currently shown (verification).</summary>
    public string FocusPromptText => _focusPrompt ?? string.Empty;

    /// <summary>Crosshair feedback for the gaze interaction state.</summary>
    public void SetCrosshairState(ShipHudCrosshairState state)
    {
        EnsureBound();
        if (_crosshairDot == null || _crosshairRing == null)
        {
            return;
        }

        _crosshairDot.RemoveFromClassList("ship-dot-valid");
        _crosshairDot.RemoveFromClassList("ship-dot-blocked");
        _crosshairRing.RemoveFromClassList("ship-ring-visible");
        _crosshairRing.RemoveFromClassList("ship-ring-valid");
        _crosshairRing.RemoveFromClassList("ship-ring-blocked");

        if (_suppressed)
        {
            return;
        }

        switch (state)
        {
            case ShipHudCrosshairState.ValidTarget:
                _crosshairDot.AddToClassList("ship-dot-valid");
                _crosshairRing.AddToClassList("ship-ring-visible");
                _crosshairRing.AddToClassList("ship-ring-valid");
                break;
            case ShipHudCrosshairState.BlockedTarget:
                _crosshairDot.AddToClassList("ship-dot-blocked");
                _crosshairRing.AddToClassList("ship-ring-visible");
                _crosshairRing.AddToClassList("ship-ring-blocked");
                break;
        }
    }

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

    private static void SetText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }
}
