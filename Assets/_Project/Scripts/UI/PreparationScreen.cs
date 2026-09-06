using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 02 - Preparation / Expedition Preparation (Phase 03).
///
/// All loadout state lives in GameSession (single authority); this class only
/// renders it and forwards user intent. Cargo, readiness and confirmation UI
/// are refreshed on every GameSession.OnStateChanged, so cross-screen edits
/// (including Processing / Outpost mutations and Reset) stay consistent.
///
/// Interactions:
/// - HARVESTER MK-I is mission-required (always carried, not toggleable);
/// - BATTERY PACK / SURVEY DRONE rows toggle optional equipment;
/// - CONFIRM LOADOUT locks the package (GameSession.TryConfirmLoadout);
/// - any equipment change after confirmation clears it (state-side rule).
/// </summary>
public class PreparationScreen
{
    private readonly VisualElement _root;
    private readonly GameSession _session;

    private Label _cargoValue;
    private VisualElement _cargoFill;
    private Label _stateLabel;
    private Label _readinessSub;
    private Label _powerValue;
    private Label _surveyValue;
    private Label _readinessCargo;
    private Button _confirmButton;

    private Button _harvesterRow;
    private Button _batteryRow;
    private Button _droneRow;

    public PreparationScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    public void Bind()
    {
        _cargoValue = BindLabel(ElementNames.CargoValue);
        _cargoFill = BindElement(ElementNames.CargoBarFill);
        _stateLabel = BindLabel(ElementNames.LoadoutStateLabel);
        _powerValue = BindLabel(ElementNames.ReadinessPowerValue);
        _surveyValue = BindLabel(ElementNames.ReadinessSurveyValue);
        _readinessCargo = BindLabel(ElementNames.ReadinessCargoValue);

        _harvesterRow = BindButton(ElementNames.HarvesterRowButton, null);
        _batteryRow = BindButton(ElementNames.BatteryRowButton,
            () => _session.TrySetBatterySelected(!_session.BatterySelected));
        _droneRow = BindButton(ElementNames.DroneRowButton,
            () => _session.TrySetDroneSelected(!_session.DroneSelected));

        _confirmButton = BindButton(ElementNames.ConfirmLoadoutButton, () => _session.TryConfirmLoadout());

        _readinessSub = _root.Q<Label>(null, "readiness-sub");
        if (_readinessSub == null)
        {
            LogMissing("readiness-sub");
        }
    }

    private Button BindButton(string name, System.Action onClick)
    {
        Button button = _root.Q<Button>(name);
        if (button == null)
        {
            LogMissing(name);
            return null;
        }

        if (onClick != null)
        {
            button.clicked += onClick;
        }

        return button;
    }

    public void Refresh()
    {
        RefreshCargo();
        RefreshEquipmentRows();
        RefreshReadiness();
        RefreshConfirm();
    }

    private void RefreshCargo()
    {
        int cargo = _session.CargoWeight;
        if (_cargoValue != null)
        {
            _cargoValue.text = cargo + " / " + GameSession.CargoCapacityKg;
        }

        if (_cargoFill != null)
        {
            float percent = cargo * 100f / GameSession.CargoCapacityKg;
            _cargoFill.style.width = Length.Percent(Mathf.Clamp(percent, 0f, 100f));
        }

        if (_readinessCargo != null)
        {
            _readinessCargo.text = cargo + " / " + GameSession.CargoCapacityKg;
        }
    }

    private void RefreshEquipmentRows()
    {
        SetRowSelected(_batteryRow, _session.BatterySelected,
            _session.BatterySelected ? "已选择" : "可选");
        SetRowSelected(_droneRow, _session.DroneSelected,
            _session.DroneSelected ? "已选择" : "可选");

        // Harvester is required and always carried.
        if (_harvesterRow != null)
        {
            _harvesterRow.AddToClassList("row-required");
            _harvesterRow.AddToClassList("row-selected");
        }
    }

    private static void SetRowSelected(Button row, bool selected, string chipText)
    {
        if (row == null)
        {
            return;
        }

        if (selected)
        {
            row.AddToClassList("row-selected");
        }
        else
        {
            row.RemoveFromClassList("row-selected");
        }

        Label chip = row.Q<Label>(null, "equipment-chip");
        if (chip != null)
        {
            chip.text = chipText;
        }
    }

    private void RefreshReadiness()
    {
        bool confirmed = _session.LoadoutConfirmed;
        bool ready = _session.IsLoadoutValid;

        if (_stateLabel != null)
        {
            _stateLabel.text = confirmed ? "已确认" : (ready ? "就绪" : "未就绪");
        }

        if (_readinessSub != null)
        {
            _readinessSub.text = confirmed
                ? "配置已锁定 // 任务就绪"
                : (ready ? "配置有效 // 等待确认" : "缺少必需模块");
        }

        SetReadinessValue(_powerValue, _session.BatterySelected);
        SetReadinessValue(_surveyValue, _session.DroneSelected);
    }

    private static void SetReadinessValue(Label label, bool active)
    {
        if (label == null)
        {
            return;
        }

        label.text = active ? "就绪" : "可选";
        if (active)
        {
            label.AddToClassList("readiness-active");
        }
        else
        {
            label.RemoveFromClassList("readiness-active");
        }
    }

    private void RefreshConfirm()
    {
        if (_confirmButton == null)
        {
            return;
        }

        if (_session.LoadoutConfirmed)
        {
            _confirmButton.text = "配置已确认";
            _confirmButton.SetEnabled(false);
            return;
        }

        _confirmButton.text = "确认配置";
        _confirmButton.SetEnabled(_session.IsLoadoutValid);
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

    private static void LogMissing(string name)
    {
        Debug.LogError("PreparationScreen: missing element " + name);
    }
}
