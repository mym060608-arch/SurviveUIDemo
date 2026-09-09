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

    private Label _zoneName;
    private Label _zoneMeta;
    private VisualElement _zoneChip;
    private Label _prompt;
    private VisualElement _crosshairDot;
    private VisualElement _crosshairRing;

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
        _bound = true;
    }

    /// <summary>Chinese-first compartment tag; empty Chinese name hides it.</summary>
    public void SetZoneTag(string zhName, string meta)
    {
        EnsureBound();
        if (_zoneChip == null)
        {
            return;
        }

        bool visible = !string.IsNullOrEmpty(zhName);
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
        if (_prompt == null)
        {
            return;
        }

        _prompt.text = text;
        _prompt.style.display = string.IsNullOrEmpty(text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

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
