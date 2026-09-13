using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 02 - Preparation / Expedition Preparation (Phase 03; controller focus
/// added in the Phase 09B PASS 5 follow-up).
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
/// Controller contract (Phase 09B): the page owns a real focus ring - the D-pad
/// / left stick (arrow keys) walk the equipment slots, the mission-details
/// button and the confirm action, A activates what the ring is on, X toggles the
/// mission details, B leaves the page (raised as <see cref="BackRequested"/> so
/// each host decides: the hub steps back a screen, the ship keeps B for its own
/// overlay). Nothing here needs a mouse first.
///
/// Presentation pass 01 keeps every value authority where it was: the payload
/// text and bar are written from the session in the SAME refresh that reports
/// the change, because the frozen Phase 03 suite and the standalone smoke read
/// both one frame later.
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

    private Label _hintDetails;
    private Label _hintNavigate;
    private Label _hintBack;
    private Label _hintConfirm;

    private UiFocusNavigator _focus;
    private bool _controllerWasEnabled;
    private int _inputReadyFrame;

    private int _lastCargo = -1;
    private float _cargoPulseUntil;
    private int _lastContextFrame = -1;
    private bool _detailsOpen;

    /// <summary>Raised when the player presses Back on this page. The host
    /// decides the meaning (hub: leave the screen; ship overlay: its own Back
    /// layer already owns B, so it never enables Back here).</summary>
    public event Action BackRequested;

    /// <summary>True while this page consumes Back (set by the host).</summary>
    public bool OwnsBack { get; set; }

    /// <summary>The shared focus ring (probes / tests).</summary>
    public UiFocusNavigator Focus => _focus;

    /// <summary>The device-aware Back hint the page really shows
    /// ("[B] 返回" on a controller, "[Esc] 返回" on the keyboard).</summary>
    public string BackHintText => _hintBack != null ? _hintBack.text : string.Empty;

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

        _hintDetails = BindLabel(ElementNames.PrepHintDetails);
        _hintNavigate = BindLabel(ElementNames.PrepHintNavigate);
        _hintBack = BindLabel(ElementNames.PrepHintBack);
        _hintConfirm = BindLabel(ElementNames.PrepHintConfirm);

        BindBackdrop();
        AddSketch("EnvTempIcon", PreparationSketchIcon.Kind.Temperature);
        AddSketch("EnvGravityIcon", PreparationSketchIcon.Kind.Gravity);
        AddSketch("EnvRadiationIcon", PreparationSketchIcon.Kind.Radiation);
        AddSketch("EnvAtmosphereIcon", PreparationSketchIcon.Kind.Atmosphere);
        AddSketch("EnvSurfaceIcon", PreparationSketchIcon.Kind.Surface);
        AddSketch("HarvesterSketch", PreparationSketchIcon.Kind.Harvester);
        AddSketch("BatterySketch", PreparationSketchIcon.Kind.Battery);
        AddSketch("DroneSketch", PreparationSketchIcon.Kind.Drone);

        BindController();

        _root.schedule.Execute(TickPresentation).Every(1);

        _readinessSub = _root.Q<Label>(null, "readiness-sub");
        if (_readinessSub == null)
        {
            LogMissing("readiness-sub");
        }
    }

    /// <summary>
    /// The page's operable set, in reading order: the three equipment slots, the
    /// mission-details button and the confirm action. The harvester slot is
    /// required, so it stays focusable (the player can read it) but its Confirm
    /// is a no-op - exactly like its mouse click.
    /// </summary>
    private void BindController()
    {
        _focus = new UiFocusNavigator();
        _focus.SetItems(new VisualElement[]
        {
            _harvesterRow, _batteryRow, _droneRow, _missionDetailsButton, _confirmButton
        });
        _focus.Register(_harvesterRow);
        _focus.Register(_batteryRow,
            () => _session.TrySetBatterySelected(!_session.BatterySelected));
        _focus.Register(_droneRow,
            () => _session.TrySetDroneSelected(!_session.DroneSelected));
        _focus.Register(_missionDetailsButton, ToggleMissionDetails);
        _focus.Register(_confirmButton, OnPrimaryAction);

        RefreshHints();
    }

    /// <summary>
    /// Per-frame controller input. The host calls it only while the page is on
    /// screen; <paramref name="controllerEnabled"/> is false for an overlay that
    /// is closed, so a hidden page can never answer a press.
    /// </summary>
    public void Tick(bool visible, bool controllerEnabled)
    {
        if (_focus != null)
        {
            // The navigator is inert until its page opens it (shared contract).
            _focus.Enabled = visible && controllerEnabled;
        }

        if (!visible || !controllerEnabled)
        {
            _controllerWasEnabled = false;
            return;
        }

        if (!_controllerWasEnabled)
        {
            // Input just became ours: the press that opened this page can reach
            // the same frame, so the first two frames only observe.
            _controllerWasEnabled = true;
            _inputReadyFrame = Time.frameCount + 2;
        }

        if (Time.frameCount < _inputReadyFrame)
        {
            return;
        }

        InputDeviceTracker.Poll();
        RefreshHints();

        if (_focus != null && _focus.Focused == null)
        {
            _focus.FocusDefault();
        }

        _focus?.Tick();

        if (OwnsBack && UiInput.BackPressed())
        {
            UiInput.ConsumeBackForFrame();
            BackRequested?.Invoke();
        }
    }

    /// <summary>The page's key line names the device in hand and never prints a
    /// control twice: the D-pad reads as "&lt; &gt;" arrows, the rest through the
    /// shared prompt formatter.</summary>
    private void RefreshHints()
    {
        SetHint(_hintDetails, UiAction.ContextAction, "详情");
        SetHint(_hintNavigate, UiAction.Navigate, "选择");
        SetHint(_hintBack, UiAction.Back, "返回");
        SetHint(_hintConfirm, UiAction.Confirm, "装备 / 卸下");
    }

    private static void SetHint(Label label, UiAction action, string verb)
    {
        if (label == null)
        {
            return;
        }

        string text = InputDeviceTracker.FormatPrompt(action, verb);
        if (label.text != text)
        {
            label.text = text;
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
