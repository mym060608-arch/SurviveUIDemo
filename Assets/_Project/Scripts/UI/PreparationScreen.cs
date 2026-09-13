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
///
/// Presentation pass 01 (Deep Space Industrial Terminal) keeps every value
/// authority where it was: the payload text and bar are written from the
/// session in the SAME refresh that reports the change, because the frozen
/// Phase 03 suite and the standalone smoke read both one frame later. What the
/// pass adds is presentation only - a state lamp, a carried/focus marker, a
/// short brightness acknowledgement on a payload change, and one very slow
/// telemetry pulse for the still-unknown surface reading.
/// </summary>
public class PreparationScreen
{
    /// <summary>How long the payload acknowledgement stays lit.</summary>
    private const float CargoPulseSeconds = 0.36f;

    /// <summary>Telemetry pulse rate (rad/s). ~0.24 Hz: a slow "still scanning"
    /// breath, never a blink the player has to read through.</summary>
    private const float TelemetryPulseRate = 1.5f;

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
    private VisualElement _readinessState;
    private VisualElement _surfaceState;
    private Label _surfaceRisk;
    private VisualElement _missionDetails;
    private Button _missionDetailsButton;
    private Label _detailPower;
    private Label _detailSurvey;

    private int _lastCargo = -1;
    private float _cargoPulseUntil;
    private int _lastContextFrame = -1;
    private bool _detailsOpen;

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

        _confirmButton = BindButton(ElementNames.ConfirmLoadoutButton, OnPrimaryAction);
        _missionDetails = BindElement("PrepMissionDetails");
        _missionDetailsButton = BindButton("PrepMissionDetailsButton", ToggleMissionDetails);
        _detailPower = BindLabel("PrepDetailPower");
        _detailSurvey = BindLabel("PrepDetailSurvey");
        _readinessState = _root.Q<VisualElement>("ReadinessState");
        _surfaceState = _root.Q<VisualElement>("EnvSurfaceState");
        _surfaceRisk = _root.Q<Label>(null, "env-risk-unknown");

        BindBackdrop();
        AddSketch("EnvTempIcon", PreparationSketchIcon.Kind.Temperature);
        AddSketch("EnvGravityIcon", PreparationSketchIcon.Kind.Gravity);
        AddSketch("EnvRadiationIcon", PreparationSketchIcon.Kind.Radiation);
        AddSketch("EnvAtmosphereIcon", PreparationSketchIcon.Kind.Atmosphere);
        AddSketch("EnvSurfaceIcon", PreparationSketchIcon.Kind.Surface);
        AddSketch("HarvesterSketch", PreparationSketchIcon.Kind.Harvester);
        AddSketch("BatterySketch", PreparationSketchIcon.Kind.Battery);
        AddSketch("DroneSketch", PreparationSketchIcon.Kind.Drone);
        AddSketch("MissionResourceIcon", PreparationSketchIcon.Kind.Resource);

        _root.schedule.Execute(TickPresentation).Every(1);

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

    /// <summary>Payload read-out. The text and the bar are written straight from
    /// the session (the frozen Phase 03 contract reads both one frame after the
    /// change); the acknowledgement is a colour pulse, so no measured value is
    /// ever animated.</summary>
    private void RefreshCargo()
    {
        int cargo = _session.CargoWeight;

        if (_lastCargo >= 0 && cargo != _lastCargo)
        {
            _cargoPulseUntil = Time.realtimeSinceStartup + CargoPulseSeconds;
            SetCargoPulse(true);
        }

        _lastCargo = cargo;

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
        // "已选择 / 可选" is the loadout vocabulary the ship-side pages already
        // use (ShipDeployConfirmPage / ShipWarehouseScreenHost), and the frozen
        // Phase 03 contract for this screen.
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

        // One lamp for the whole package: lit while the loadout can launch.
        if (_readinessState != null)
        {
            bool lit = ready || confirmed;
            _readinessState.EnableInClassList("ready", lit);
            _readinessState.EnableInClassList("blocked", !lit);
        }

        if (_readinessSub != null)
        {
            _readinessSub.text = confirmed
                ? "配置已锁定，任务就绪"
                : (ready ? "配置有效，等待确认" : "缺少必需模块");
        }

        SetReadinessValue(_powerValue, _session.BatterySelected);
        SetReadinessValue(_surveyValue, _session.DroneSelected);
        if (_detailPower != null)
            _detailPower.text = "能源模块      " + (_session.BatterySelected ? "就绪" : "可选");
        if (_detailSurvey != null)
            _detailSurvey.text = "勘测系统      " + (_session.DroneSelected ? "就绪" : "可选");
    }

    /// <summary>One check row: the value, its weight, and the row's own lamp.
    /// The lamp lives on the row because USS has no sibling selector.</summary>
    private static void SetReadinessValue(Label label, bool active)
    {
        if (label == null)
        {
            return;
        }

        label.text = active ? "就绪" : "可选";
        label.EnableInClassList("readiness-active", active);
        if (label.parent != null)
        {
            label.parent.EnableInClassList("row-active", active);
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
            // Phase 06: a confirmed loadout turns the CTA into the real
            // expedition launch; always available while confirmed.
            _confirmButton.text = "开始远征 »";
            _confirmButton.SetEnabled(_session.CanStartExpedition);
            return;
        }

        _confirmButton.text = "确认配置";
        _confirmButton.SetEnabled(_session.IsLoadoutValid);
    }

    /// <summary>Adds the page's own backdrop once (a static engineering grid).</summary>
    private void BindBackdrop()
    {
        if (_root.Q<PreparationBackdrop>(PreparationBackdrop.ElementName) != null)
        {
            return;
        }

        _root.Insert(0, new PreparationBackdrop());
    }

    private void AddSketch(string hostName, PreparationSketchIcon.Kind kind)
    {
        VisualElement host = BindElement(hostName);
        if (host != null && host.childCount == 0)
        {
            host.Add(new PreparationSketchIcon(kind));
        }
    }

    private void ToggleMissionDetails()
    {
        _detailsOpen = !_detailsOpen;
        if (_missionDetails != null)
        {
            _missionDetails.EnableInClassList("details-open", _detailsOpen);
        }
        if (_missionDetailsButton != null)
        {
            _missionDetailsButton.text = _detailsOpen ? "收起详情 ‹" : "查看详情 ›";
        }
    }

    /// <summary>Per-frame presentation only: the payload acknowledgement, the
    /// surface telemetry breath, and the page's own context action. No value,
    /// no layout and no focus state is recomputed here.</summary>
    private void TickPresentation()
    {
        if (_root.panel == null) return;

        if (_cargoPulseUntil > 0f && Time.realtimeSinceStartup >= _cargoPulseUntil)
        {
            _cargoPulseUntil = 0f;
            SetCargoPulse(false);
        }

        if (_surfaceState != null)
        {
            // SURFACE is still unknown: a very slow breath on the hollow lamp
            // and its label, so the row reads as "scanning", not as "broken".
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * TelemetryPulseRate);
            _surfaceState.style.opacity = Mathf.Lerp(0.4f, 1f, wave);
            if (_surfaceRisk != null)
            {
                _surfaceRisk.style.opacity = Mathf.Lerp(0.4f, 0.85f, wave);
            }
        }

        // The preparation page owns X only while it is actually visible.
        for (VisualElement element = _root; element != null; element = element.parent)
        {
            if (element.resolvedStyle.display == DisplayStyle.None) return;
        }
        if (_lastContextFrame != Time.frameCount && UiInput.ContextActionPressed())
        {
            _lastContextFrame = Time.frameCount;
            ToggleMissionDetails();
        }
    }

    private void SetCargoPulse(bool on)
    {
        if (_cargoValue != null)
        {
            _cargoValue.EnableInClassList("cargo-pulse", on);
        }

        if (_cargoFill != null)
        {
            _cargoFill.EnableInClassList("cargo-pulse", on);
        }
    }

    /// <summary>Primary action: confirm the package, or (once confirmed)
    /// launch the expedition into the NEREID-04 surface scene.</summary>
    private void OnPrimaryAction()
    {
        if (_session.LoadoutConfirmed)
        {
            if (_session.CanStartExpedition)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.SurfaceSceneName);
            }

            return;
        }

        _session.TryConfirmLoadout();
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
