using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 01 - Overview. Reads GameSession and renders ship status.
/// Requests navigation only through the callback supplied by AppController.
/// </summary>
public class OverviewScreen
{
    private readonly GameSession _session;

    public OverviewScreen(GameSession session)
    {
        _session = session;
    }

    public void Bind(VisualElement root, System.Action navigateToPreparation)
    {
        Button prepare = root.Q<Button>(ElementNames.OpenPreparationButton);
        if (prepare != null)
        {
            prepare.clicked += navigateToPreparation;
        }
        else
        {
            Debug.LogError("OverviewScreen: missing element " + ElementNames.OpenPreparationButton);
        }
    }

    public void Refresh(VisualElement root)
    {
        SetText(root, ElementNames.EnergyValue, _session.Energy);
        SetText(root, ElementNames.FoodValue, _session.Food);
        SetText(root, ElementNames.MineralsValue, _session.Minerals);
        SetText(root, ElementNames.FuelValue, _session.Fuel);
        SetText(root, ElementNames.AlloyValue, _session.Alloy);

        Label destination = root.Q<Label>(ElementNames.DestinationValue);
        if (destination != null)
        {
            // Phase 01 uses a single authored destination regardless of index.
            destination.text = "NEREID-04";
        }
    }

    private static void SetText(VisualElement root, string name, int value)
    {
        Label label = root.Q<Label>(name);
        if (label != null)
        {
            label.text = value.ToString();
        }
    }
}
