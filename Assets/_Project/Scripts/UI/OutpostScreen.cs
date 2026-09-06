using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 04 - Outpost. Renders module levels and drives
/// GameSession.TryUpgradeRefinery().
/// </summary>
public class OutpostScreen
{
    private readonly VisualElement _root;
    private readonly GameSession _session;

    private Label _harvesterLevel;
    private Label _refineryLevel;
    private Label _outpostLevel;
    private Label _alloy;
    private Button _upgradeButton;

    public OutpostScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    public void Bind()
    {
        _harvesterLevel = BindLabel(ElementNames.HarvesterLevelValue);
        _refineryLevel = BindLabel(ElementNames.RefineryLevelValue);
        _outpostLevel = BindLabel(ElementNames.OutpostLevelValue);
        _alloy = BindLabel(ElementNames.OutpostAlloyValue);

        _upgradeButton = _root.Q<Button>(ElementNames.UpgradeRefineryButton);
        if (_upgradeButton != null)
        {
            _upgradeButton.clicked += () => _session.TryUpgradeRefinery();
        }
        else
        {
            LogMissing(ElementNames.UpgradeRefineryButton);
        }
    }

    public void Refresh()
    {
        SetLevel(_harvesterLevel, "采集模块 LV.", _session.HarvesterLevel);
        SetLevel(_refineryLevel, "精炼模块 LV.", _session.RefineryLevel);
        SetLevel(_outpostLevel, "据点 LV.", _session.OutpostLevel);
        SetText(_alloy, _session.Alloy);

        if (_upgradeButton != null)
        {
            // Disabled while Alloy < 3 (TryUpgradeRefinery also fails safely).
            _upgradeButton.SetEnabled(_session.Alloy >= GameSession.RefineryUpgradeAlloyCost);
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

    private static void SetLevel(Label label, string prefix, int level)
    {
        if (label != null)
        {
            label.text = prefix + level;
        }
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
        Debug.LogError("OutpostScreen: missing element " + name);
    }
}
