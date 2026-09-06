using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 03 - Processing. Renders raw minerals and drives
/// GameSession.TryProcessMinerals().
/// </summary>
public class ProcessingScreen
{
    private readonly VisualElement _root;
    private readonly GameSession _session;

    private Label _minerals;
    private Label _fuel;
    private Label _alloy;
    private Button _processButton;

    public ProcessingScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    public void Bind()
    {
        _minerals = BindLabel(ElementNames.ProcessingMineralsValue);
        _fuel = BindLabel(ElementNames.ProcessingFuelValue);
        _alloy = BindLabel(ElementNames.ProcessingAlloyValue);

        _processButton = _root.Q<Button>(ElementNames.ProcessButton);
        if (_processButton != null)
        {
            _processButton.clicked += () => _session.TryProcessMinerals();
        }
        else
        {
            LogMissing(ElementNames.ProcessButton);
        }
    }

    public void Refresh()
    {
        SetText(_minerals, _session.Minerals);
        SetText(_fuel, _session.Fuel);
        SetText(_alloy, _session.Alloy);

        if (_processButton != null)
        {
            // Disabled while Minerals < 5 (TryProcessMinerals also fails safely).
            _processButton.SetEnabled(_session.Minerals >= GameSession.ProcessMineralsCost);
        }
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

    private static void SetText(Label label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
        }
    }

    private static void LogMissing(string name)
    {
        Debug.LogError("ProcessingScreen: missing element " + name);
    }
}
