using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 03 - Processing. Renders raw minerals and drives
/// GameSession.TryProcessMinerals().
/// </summary>
public class ProcessingScreen
{
    private readonly GameSession _session;

    public ProcessingScreen(GameSession session)
    {
        _session = session;
    }

    public void Bind(VisualElement root)
    {
        Button process = root.Q<Button>(ElementNames.ProcessButton);
        if (process != null)
        {
            process.clicked += () => _session.TryProcessMinerals();
        }
        else
        {
            Debug.LogError("ProcessingScreen: missing element " + ElementNames.ProcessButton);
        }
    }

    public void Refresh(VisualElement root)
    {
        SetText(root, ElementNames.ProcessingMineralsValue, _session.Minerals);
        SetText(root, ElementNames.ProcessingFuelValue, _session.Fuel);
        SetText(root, ElementNames.ProcessingAlloyValue, _session.Alloy);

        Button process = root.Q<Button>(ElementNames.ProcessButton);
        if (process != null)
        {
            // Disabled while Minerals < 5 (TryProcessMinerals also fails safely).
            process.SetEnabled(_session.Minerals >= GameSession.ProcessMineralsCost);
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
