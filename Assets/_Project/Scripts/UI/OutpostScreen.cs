using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 04 - Outpost. Renders module levels and drives
/// GameSession.TryUpgradeRefinery().
/// </summary>
public class OutpostScreen
{
    private readonly GameSession _session;

    public OutpostScreen(GameSession session)
    {
        _session = session;
    }

    public void Bind(VisualElement root)
    {
        Button upgrade = root.Q<Button>(ElementNames.UpgradeRefineryButton);
        if (upgrade != null)
        {
            upgrade.clicked += () => _session.TryUpgradeRefinery();
        }
        else
        {
            Debug.LogError("OutpostScreen: missing element " + ElementNames.UpgradeRefineryButton);
        }
    }

    public void Refresh(VisualElement root)
    {
        SetLevel(root, ElementNames.HarvesterLevelValue, "HARVESTER LV.", _session.HarvesterLevel);
        SetLevel(root, ElementNames.RefineryLevelValue, "REFINERY LV.", _session.RefineryLevel);
        SetLevel(root, ElementNames.OutpostLevelValue, "OUTPOST LV.", _session.OutpostLevel);
        SetText(root, ElementNames.OutpostAlloyValue, _session.Alloy);

        Button upgrade = root.Q<Button>(ElementNames.UpgradeRefineryButton);
        if (upgrade != null)
        {
            // Disabled while Alloy < 3 (TryUpgradeRefinery also fails safely).
            upgrade.SetEnabled(_session.Alloy >= GameSession.RefineryUpgradeAlloyCost);
        }
    }

    private static void SetLevel(VisualElement root, string name, string prefix, int level)
    {
        Label label = root.Q<Label>(name);
        if (label != null)
        {
            label.text = prefix + level;
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
