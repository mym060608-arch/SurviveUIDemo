using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 01 - Overview (Phase 02 main visual).
///
/// Reads GameSession and renders ship status exactly once per state change:
/// - survival-critical rows (ENERGY / FOOD) show live bars and numeric values,
/// - resource inventory rows show plain counts,
/// - the destination hero label is authored NEREID-04 (single destination),
/// - the center planet is a deterministic procedural texture, generated once.
///
/// Requests navigation only through the callback supplied by AppController.
/// No per-frame work: bars/text are updated on Refresh (state change only).
/// </summary>
public class OverviewScreen
{
    private readonly VisualElement _root;
    private readonly GameSession _session;

    private Label _energy;
    private Label _food;
    private Label _minerals;
    private Label _fuel;
    private Label _alloy;
    private Label _destination;

    private VisualElement _energyFill;
    private VisualElement _foodFill;
    private VisualElement _planetDisc;
    private Button _expeditionCta;
    private bool _planetReady;
    private RenderTexture _planetPreviewTexture;
    private bool _planetPreviewApplied;

    /// <summary>The rotating 3D preview RenderTexture when this overview is
    /// driven by the shared preview rig (null = static procedural disc).</summary>
    public RenderTexture PlanetPreviewTexture => _planetPreviewTexture;

    /// <summary>True while the planet area displays the 3D preview texture.</summary>
    public bool IsPlanetPreviewActive => _planetPreviewApplied;

    /// <summary>True while the fallback static disc is used instead.</summary>
    public bool IsPlanetFallbackActive => _planetReady;

    public OverviewScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    /// <summary>
    /// Binds the view.
    ///
    /// <paramref name="showExpeditionCta"/> is the HOST CONTEXT switch
    /// (Phase 07B.2.3): the legacy Hub keeps the 准备远征 CTA because it is the
    /// only way into Preparation there, while the ship's physical Overview
    /// terminal hides it - inside the ship the expedition is launched from the
    /// Airlock equipment locker + deployment station, so the CTA would be both
    /// dead and semantically duplicated.
    ///
    /// <paramref name="planetPreview"/> is the shared rotating 3D planet
    /// RenderTexture (Phase 07B.2.4). Every host reads the SAME texture; when it
    /// is absent the view falls back to the original static procedural disc, so
    /// the overview can never render without a planet.
    /// </summary>
    public void Bind(System.Action navigateToPreparation,
        bool showExpeditionCta = true,
        RenderTexture planetPreview = null)
    {
        _planetPreviewTexture = planetPreview;
        _energy = BindLabel(ElementNames.EnergyValue);
        _food = BindLabel(ElementNames.FoodValue);
        _minerals = BindLabel(ElementNames.MineralsValue);
        _fuel = BindLabel(ElementNames.FuelValue);
        _alloy = BindLabel(ElementNames.AlloyValue);
        _destination = BindLabel(ElementNames.DestinationValue);

        _energyFill = BindElement(ElementNames.EnergyBarFill);
        _foodFill = BindElement(ElementNames.FoodBarFill);
        _planetDisc = BindElement(ElementNames.PlanetDisc);
        if (_planetDisc != null)
        {
            // The planet is a pure visual preview: it must never intercept a
            // pointer that belongs to an overview button.
            _planetDisc.pickingMode = PickingMode.Ignore;
        }

        _expeditionCta = _root.Q<Button>(ElementNames.OpenPreparationButton);
        if (_expeditionCta == null)
        {
            LogMissing(ElementNames.OpenPreparationButton);
        }
        else
        {
            _expeditionCta.style.display = showExpeditionCta
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _expeditionCta.SetEnabled(showExpeditionCta);
            if (showExpeditionCta && navigateToPreparation != null)
            {
                _expeditionCta.clicked += navigateToPreparation;
            }
        }
    }

    /// <summary>The 准备远征 CTA (hidden on the ship's physical terminal).</summary>
    public Button ExpeditionCta => _expeditionCta;

    /// <summary>True when the expedition CTA is actually drawn.</summary>
    public bool IsExpeditionCtaVisible => _expeditionCta != null
                                          && _expeditionCta.resolvedStyle.display
                                          != DisplayStyle.None;

    public void Refresh()
    {
        SetText(_energy, _session.Energy);
        SetText(_food, _session.Food);
        SetText(_minerals, _session.Minerals);
        SetText(_fuel, _session.Fuel);
        SetText(_alloy, _session.Alloy);

        SetBarWidth(_energyFill, _session.Energy);
        SetBarWidth(_foodFill, _session.Food);

        if (_destination != null)
        {
            // Phase 01/02 use a single authored destination regardless of index.
            _destination.text = "NEREID-04";
        }

        EnsurePlanetTexture();
    }

    /// <summary>
    /// Assigns the planet visual ONCE.
    ///
    /// With the shared preview rig available this is the live rotating 3D
    /// RenderTexture (Phase 07B.2.4); the procedural disc is only generated as a
    /// fallback, so hosts that show the preview never pay for the 2D texture.
    /// </summary>
    private void EnsurePlanetTexture()
    {
        if (_planetDisc == null)
        {
            return;
        }

        if (_planetPreviewTexture != null)
        {
            if (!_planetPreviewApplied)
            {
                _planetDisc.style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(_planetPreviewTexture));
                _planetPreviewApplied = true;
            }

            return;
        }

        if (_planetReady)
        {
            return;
        }

        Texture2D texture = PlanetTextureFactory.Create();
        if (texture == null)
        {
            LogMissing("planet texture generation failed");
            return;
        }

        _planetDisc.style.backgroundImage = new StyleBackground(texture);
        // Planet disc and texture are both square, so every background scale
        // mode (stretch/contain/cover default) resolves to the exact fill.
        _planetReady = true;
    }

    private static void SetBarWidth(VisualElement fill, int value)
    {
        if (fill == null)
        {
            return;
        }

        // Survival bars display the default resource semantics as 0-100 %.
        fill.style.width = Length.Percent(Mathf.Clamp(value, 0, 100));
    }

    private Label BindLabel(string name)
    {
        Label label = _root.Q<Label>(name);
        if (label == null)
        {
            LogMissing(name);
        }

        return label;
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

    private static void SetText(Label label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
        }
    }

    private static void LogMissing(string name)
    {
        Debug.LogError("OverviewScreen: missing element " + name);
    }
}
