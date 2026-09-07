using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 04 - Outpost / Module Topology + Upgrade Economy (Phase 05).
///
/// Presents the three authoritative modules (Core/OutpostLevel, Harvester,
/// Refinery) as a selectable topology. All upgrade decisions live in
/// GameSession.TryUpgrade; this screen only renders state, forwards clicks and
/// shows the exact blocking reason (Alloy / Core prerequisite / max level).
///
/// Selection is purely presentational (default: Refinery, mirroring the
/// Processing -> Outpost economy loop) and never stored in GameSession.
/// </summary>
public class OutpostScreen
{
    private readonly VisualElement _root;
    private readonly GameSession _session;

    private GameSession.OutpostModule _selected = GameSession.OutpostModule.Refinery;

    // Status + topology.
    private Label _outpostLevel;
    private Label _harvesterLevel;
    private Label _refineryLevel;
    private Label _alloy;
    private Label _cap;

    private Label _coreNodeLevel;
    private Label _harvesterNodeLevel;
    private Label _refineryNodeLevel;
    private Label _coreNodeStatus;
    private Label _harvesterNodeStatus;
    private Label _refineryNodeStatus;

    private Button _coreNode;
    private Button _harvesterNode;
    private Button _refineryNode;

    // Detail.
    private Label _detailName;
    private Label _detailLevels;
    private Label _effectTitle;
    private Label _currentEffect;
    private Label _nextEffect;
    private Label _upgradeCost;
    private Label _requirement;
    private Button _upgradeButton;

    public OutpostScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    public void Bind()
    {
        _outpostLevel = BindLabel(ElementNames.OutpostLevelValue);
        _harvesterLevel = BindLabel(ElementNames.HarvesterLevelValue);
        _refineryLevel = BindLabel(ElementNames.RefineryLevelValue);
        _alloy = BindLabel(ElementNames.OutpostAlloyValue);
        _cap = BindLabel(ElementNames.OutpostCapValue);

        _coreNodeLevel = BindLabel(ElementNames.CoreNodeLevel);
        _harvesterNodeLevel = BindLabel(ElementNames.HarvesterNodeLevel);
        _refineryNodeLevel = BindLabel(ElementNames.RefineryNodeLevel);
        _coreNodeStatus = BindLabel(ElementNames.CoreNodeStatusValue);
        _harvesterNodeStatus = BindLabel(ElementNames.HarvesterNodeStatusValue);
        _refineryNodeStatus = BindLabel(ElementNames.RefineryNodeStatusValue);

        _coreNode = BindNode(ElementNames.CoreModuleNode, GameSession.OutpostModule.Core);
        _harvesterNode = BindNode(ElementNames.HarvesterModuleNode, GameSession.OutpostModule.Harvester);
        _refineryNode = BindNode(ElementNames.RefineryModuleNode, GameSession.OutpostModule.Refinery);

        _detailName = BindLabel(ElementNames.ModuleDetailName);
        _detailLevels = BindLabel(ElementNames.ModuleDetailLevels);
        _effectTitle = BindLabel(ElementNames.ModuleDetailEffectTitle);
        _currentEffect = BindLabel(ElementNames.ModuleCurrentEffectValue);
        _nextEffect = BindLabel(ElementNames.ModuleNextEffectValue);
        _upgradeCost = BindLabel(ElementNames.ModuleUpgradeCostValue);
        _requirement = BindLabel(ElementNames.ModuleUpgradeRequirement);
        _upgradeButton = BindLabelButton(ElementNames.OutpostUpgradeButton,
            () =>
            {
                _session.TryUpgrade(_selected);
                Refresh();
            });
    }

    private Button BindNode(string name, GameSession.OutpostModule module)
    {
        Button node = _root.Q<Button>(name);
        if (node == null)
        {
            LogMissing(name);
            return null;
        }

        node.clicked += () => SelectModule(module);
        return node;
    }

    /// <summary>Purely presentational selection (defaults to Refinery to
    /// mirror the Processing-&gt;Alloy-&gt;upgrade loop). Exposed for tests and
    /// node clicks; never stored in GameSession.</summary>
    public void SelectModule(GameSession.OutpostModule module)
    {
        _selected = module;
        Refresh();
    }

    private Button BindLabelButton(string name, System.Action onClick)
    {
        Button button = _root.Q<Button>(name);
        if (button == null)
        {
            LogMissing(name);
            return null;
        }

        button.clicked += onClick;
        return button;
    }

    public void Refresh()
    {
        int core = _session.OutpostLevel;
        int harvester = _session.HarvesterLevel;
        int refinery = _session.RefineryLevel;
        int cap = _session.SubsystemLevelCap;

        SetLevel(_outpostLevel, core);
        SetLevel(_harvesterLevel, harvester);
        SetLevel(_refineryLevel, refinery);
        SetText(_alloy, _session.Alloy);
        SetLevel(_cap, cap);

        SetLevel(_coreNodeLevel, core);
        SetLevel(_harvesterNodeLevel, harvester);
        SetLevel(_refineryNodeLevel, refinery);

        SetText(_coreNodeStatus, "模块上限 LV." + cap);
        SetText(_harvesterNodeStatus, "采集效率 " + _session.HarvesterEfficiencyPercent + "%");
        SetText(_refineryNodeStatus, "加工周期 " + Seconds(_session.RefineryProcessSeconds));

        SetNodeSelected(_coreNode, _selected == GameSession.OutpostModule.Core);
        SetNodeSelected(_harvesterNode, _selected == GameSession.OutpostModule.Harvester);
        SetNodeSelected(_refineryNode, _selected == GameSession.OutpostModule.Refinery);

        // A subsystem whose next tier is blocked by the Core is visibly muted
        // (text + emphasis; never colour-only).
        SetLocked(_harvesterNode, IsCoreLocked(GameSession.OutpostModule.Harvester, harvester));
        SetLocked(_refineryNode, IsCoreLocked(GameSession.OutpostModule.Refinery, refinery));

        RefreshDetail();
    }

    private bool IsCoreLocked(GameSession.OutpostModule module, int level)
    {
        return module != GameSession.OutpostModule.Core
               && level < GameSession.MaxModuleLevel
               && level + 1 > _session.SubsystemLevelCap;
    }

    private void RefreshDetail()
    {
        if (_detailName == null)
        {
            return;
        }

        GameSession.UpgradeFailureReason reason = _session.CanUpgrade(_selected);
        int level = _session.ModuleLevel(_selected);

        _detailName.text = ModuleName(_selected);
        if (level >= GameSession.MaxModuleLevel)
        {
            _detailLevels.text = "LV." + level + " · 已达上限";
        }
        else
        {
            _detailLevels.text = "LV." + level + " → LV." + (level + 1);
        }

        switch (_selected)
        {
            case GameSession.OutpostModule.Core:
                FillCoreDetail(level);
                break;
            case GameSession.OutpostModule.Harvester:
                FillHarvesterDetail(level);
                break;
            default:
                FillRefineryDetail(level);
                break;
        }

        _upgradeCost.text = level >= GameSession.MaxModuleLevel
            ? "—"
            : _session.GetUpgradeCost(_selected) + " 合金";

        _requirement.text = RequirementCopy(_selected, level, reason);
        bool canUpgrade = reason == GameSession.UpgradeFailureReason.None;
        _upgradeButton.text = CtaCopy(reason);
        _upgradeButton.SetEnabled(canUpgrade);
    }

    private void FillCoreDetail(int level)
    {
        _effectTitle.text = "模块等级上限";
        _currentEffect.text = "LV." + _session.SubsystemLevelCap;
        _nextEffect.text = level >= GameSession.MaxModuleLevel
            ? "—"
            : "LV." + Mathf.Min(GameSession.MaxModuleLevel, _session.SubsystemLevelCap + 1);
    }

    private void FillHarvesterDetail(int level)
    {
        _effectTitle.text = "采集效率";
        _currentEffect.text = _session.HarvesterEfficiencyPercent + "%";
        _nextEffect.text = level >= GameSession.MaxModuleLevel
            ? "—"
            : PercentForLevel(level + 1) + "%";
    }

    private void FillRefineryDetail(int level)
    {
        _effectTitle.text = "加工周期";
        _currentEffect.text = Seconds(_session.RefineryProcessSeconds);
        _nextEffect.text = level >= GameSession.MaxModuleLevel
            ? "—"
            : Seconds(DurationForLevel(level + 1));
    }

    private string RequirementCopy(GameSession.OutpostModule module, int level,
        GameSession.UpgradeFailureReason reason)
    {
        switch (reason)
        {
            case GameSession.UpgradeFailureReason.CoreLevelRequired:
                return "需要核心模块 LV." + level;
            case GameSession.UpgradeFailureReason.InsufficientAlloy:
                return "合金不足";
            case GameSession.UpgradeFailureReason.MaxLevel:
                return "已达最高等级";
            default:
                return "—";
        }
    }

    private string CtaCopy(GameSession.UpgradeFailureReason reason)
    {
        switch (reason)
        {
            case GameSession.UpgradeFailureReason.CoreLevelRequired:
                return "需要核心升级";
            case GameSession.UpgradeFailureReason.InsufficientAlloy:
                return "合金不足";
            case GameSession.UpgradeFailureReason.MaxLevel:
                return "已达最高等级";
            default:
                return "升级模块";
        }
    }

    private static string ModuleName(GameSession.OutpostModule module)
    {
        switch (module)
        {
            case GameSession.OutpostModule.Core:
                return "核心模块";
            case GameSession.OutpostModule.Harvester:
                return "采集模块";
            default:
                return "精炼模块";
        }
    }

    private static int PercentForLevel(int level)
    {
        return 100 + (level - 1) * 25;
    }

    private static float DurationForLevel(int level)
    {
        return Mathf.Max(0.5f, 2.0f - (level - 1) * 0.4f);
    }

    private static string Seconds(float value)
    {
        return value.ToString("0.0") + "s";
    }

    private static void SetNodeSelected(Button node, bool selected)
    {
        if (node == null)
        {
            return;
        }

        if (selected)
        {
            node.AddToClassList("node-selected");
        }
        else
        {
            node.RemoveFromClassList("node-selected");
        }
    }

    private static void SetLocked(Button node, bool locked)
    {
        if (node == null)
        {
            return;
        }

        if (locked)
        {
            node.AddToClassList("node-locked");
        }
        else
        {
            node.RemoveFromClassList("node-locked");
        }
    }

    private static void SetLevel(Label label, int level)
    {
        if (label != null)
        {
            label.text = "LV." + level;
        }
    }

    private static void SetText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }

    private static void SetText(Label label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
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

    private static void LogMissing(string name)
    {
        Debug.LogError("OutpostScreen: missing element " + name);
    }
}
