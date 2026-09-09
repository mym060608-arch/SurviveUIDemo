using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Deployment Confirmation page (Phase 07B.2) hosted by the ShipOverlayUi.
///
/// Shown by the Airlock deployment station BEFORE any ShipInterior -> Surface
/// routing: NEREID-04 destination, the CURRENT expedition equipment summary
/// (live GameSession read-only) and three actions:
///   调整装备  -> open the SAME shared loadout view used by the equipment locker
///   确认降临  -> perform the real ShipInterior -> Surface routing
///   取消      -> return to ShipExplore
/// No immediate scene load happens when the station is interacted with.
///
/// The GameObject/UIDocument stays active; visibility is driven by the root
/// display style so the panel tree binds once and never re-instantiates.
/// </summary>
public class ShipDeployConfirmPage : MonoBehaviour
{
    public const string DestinationName = "DeployDestination";
    public const string HarvesterStateName = "DeployHarvesterState";
    public const string BatteryStateName = "DeployBatteryState";
    public const string DroneStateName = "DeployDroneState";
    public const string CargoValueName = "DeployCargoValue";
    public const string AdjustGearButtonName = "DeployAdjustGearButton";
    public const string ConfirmButtonName = "DeployConfirmButton";
    public const string CancelButtonName = "DeployCancelButton";

    [SerializeField]
    public StyleSheet modalStyleSheet;

    // Panel settings assigned at runtime because UIDocument.panelSettings does
    // not serialize reliably from the deterministic scene builder (same
    // convention as the ship HUD / console hosts).
    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private VisualElement _root;
    private GameSession _session;
    private bool _bound;
    private bool _visible;

    private Label _harvesterState;
    private Label _batteryState;
    private Label _droneState;
    private Label _cargoValue;

    /// <summary>User asked to edit the shared loadout.</summary>
    public event Action AdjustGearRequested;

    /// <summary>User confirmed landing: perform the real routing.</summary>
    public event Action ConfirmLandingRequested;

    /// <summary>User cancelled: back to ShipExplore.</summary>
    public event Action CancelRequested;

    public bool IsBound => _bound;

    public VisualElement Root => _root;

    public UIDocument Document => _document;

    public bool IsVisible => _visible;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        if (_document != null && _document.panelSettings == null && panelSettings != null)
        {
            _document.panelSettings = panelSettings;
        }
    }

    private void Update()
    {
        if (!_bound && _document != null && _document.rootVisualElement != null
            && _document.rootVisualElement.childCount > 0)
        {
            EnsureBound();
        }
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    private void EnsureBound()
    {
        if (_bound || _document == null || _document.rootVisualElement == null
            || _document.rootVisualElement.childCount == 0)
        {
            return;
        }

        _root = _document.rootVisualElement;
        if (modalStyleSheet != null && !_root.styleSheets.Contains(modalStyleSheet))
        {
            _root.styleSheets.Add(modalStyleSheet);
        }

        ApplyUiCjkFont();

        _harvesterState = BindLabel(HarvesterStateName);
        _batteryState = BindLabel(BatteryStateName);
        _droneState = BindLabel(DroneStateName);
        _cargoValue = BindLabel(CargoValueName);

        BindButton(AdjustGearButtonName, () => AdjustGearRequested?.Invoke());
        BindButton(ConfirmButtonName, () => ConfirmLandingRequested?.Invoke());
        BindButton(CancelButtonName, () => CancelRequested?.Invoke());

        _session = GameSession.Ensure();
        _session.OnStateChanged += HandleSessionChanged;
        RefreshAll();
        _bound = true;
        _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void HandleSessionChanged()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_session == null)
        {
            _session = GameSession.Ensure();
        }

        SetState(_harvesterState, "必需", true);
        SetState(_batteryState, _session.BatterySelected ? "已选择" : "未选择",
            _session.BatterySelected);
        SetState(_droneState, _session.DroneSelected ? "已选择" : "未选择",
            _session.DroneSelected);
        if (_cargoValue != null)
        {
            _cargoValue.text = _session.CargoWeight + " / "
                               + GameSession.CargoCapacityKg + " KG";
        }
    }

    private static void SetState(Label label, string text, bool on)
    {
        if (label == null)
        {
            return;
        }

        label.text = text;
        if (on)
        {
            label.AddToClassList("modal-state-required");
        }
        else
        {
            label.RemoveFromClassList("modal-state-required");
        }
    }

    private Label BindLabel(string name)
    {
        Label label = _root != null ? _root.Q<Label>(name) : null;
        if (label == null)
        {
            Debug.LogError("[ShipDeployConfirmPage] missing label " + name);
        }

        return label;
    }

    private void BindButton(string name, Action onClick)
    {
        Button button = _root != null ? _root.Q<Button>(name) : null;
        if (button == null)
        {
            Debug.LogError("[ShipDeployConfirmPage] missing button " + name);
            return;
        }

        button.clicked += onClick;
    }

    /// <summary>Show / hide the page (display style; tree stays bound).</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_root != null)
        {
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static Font _cjkFont;

    private void ApplyUiCjkFont()
    {
        if (_cjkFont == null)
        {
            string[] candidates =
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Noto Sans CJK SC",
                "Source Han Sans SC"
            };

            _cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 16);
        }

        if (_cjkFont == null || _root == null)
        {
            return;
        }

        foreach (TextElement text in _root.Query<TextElement>().ToList())
        {
            text.style.unityFont = _cjkFont;
        }
    }
}
