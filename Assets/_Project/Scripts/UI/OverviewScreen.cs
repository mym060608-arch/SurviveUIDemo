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
    private bool _planetReady;

    public OverviewScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    public void Bind(System.Action navigateToPreparation)
    {
        _energy = BindLabel(ElementNames.EnergyValue);
        _food = BindLabel(ElementNames.FoodValue);
        _minerals = BindLabel(ElementNames.MineralsValue);
        _fuel = BindLabel(ElementNames.FuelValue);
        _alloy = BindLabel(ElementNames.AlloyValue);
        _destination = BindLabel(ElementNames.DestinationValue);

        _energyFill = BindElement(ElementNames.EnergyBarFill);
        _foodFill = BindElement(ElementNames.FoodBarFill);
        _planetDisc = BindElement(ElementNames.PlanetDisc);

        Button prepare = _root.Q<Button>(ElementNames.OpenPreparationButton);
        if (prepare != null)
        {
            prepare.clicked += navigateToPreparation;
        }
        else
        {
            LogMissing(ElementNames.OpenPreparationButton);
        }
    }

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

    /// <summary>Assigns the deterministic procedural planet texture once.</summary>
    private void EnsurePlanetTexture()
    {
        if (_planetReady || _planetDisc == null)
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
