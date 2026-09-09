using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Binds Console02's dedicated 仓库总览 (Warehouse / CARGO // STORAGE) document
/// to the authoritative GameSession (Phase 07B.2).
///
/// The permanent terminal role of Console02 changed from 出发准备 to a
/// read-only warehouse overview, so this console no longer hosts the legacy
/// Preparation/MainApp tree. It summarizes the authoritative ship-held
/// resources (矿物 / 燃料 / 合金) plus the current expedition equipment state
/// (必需 / 已选择 / 未选择) and cargo mass - every value rendered from
/// GameSession only, refreshed on OnStateChanged. It is NOT a loadout editor.
/// </summary>
public class ShipWarehouseScreenHost : MonoBehaviour
{
    // Authoritative UXML element names (WarehouseConsole.uxml).
    public const string MineralsValueName = "WarehouseMineralsValue";
    public const string FuelValueName = "WarehouseFuelValue";
    public const string AlloyValueName = "WarehouseAlloyValue";
    public const string HarvesterStateName = "WarehouseHarvesterState";
    public const string BatteryStateName = "WarehouseBatteryState";
    public const string DroneStateName = "WarehouseDroneState";
    public const string CargoValueName = "WarehouseCargoValue";
    public const string NoteLabelName = "WarehouseNoteLabel";

    [SerializeField]
    public StyleSheet warehouseStyleSheet;

    private UIDocument _document;
    private VisualElement _root;
    private GameSession _session;
    private bool _bound;

    private Label _mineralsValue;
    private Label _fuelValue;
    private Label _alloyValue;
    private Label _harvesterState;
    private Label _batteryState;
    private Label _droneState;
    private Label _cargoValue;

    public bool IsBound => _bound;

    public VisualElement Root => _root;

    private void OnEnable()
    {
        _session = GameSession.Ensure();
        EnsureBound();
    }

    private void Update()
    {
        // Late-binding safety: the UIDocument attach order is not guaranteed
        // across regenerated scenes; retry until the tree is queryable.
        if (!_bound && _document == null)
        {
            _document = GetComponent<UIDocument>();
        }

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
        if (warehouseStyleSheet != null && !_root.styleSheets.Contains(warehouseStyleSheet))
        {
            _root.styleSheets.Add(warehouseStyleSheet);
        }

        ApplyUiCjkFont();

        _mineralsValue = BindLabel(MineralsValueName);
        _fuelValue = BindLabel(FuelValueName);
        _alloyValue = BindLabel(AlloyValueName);
        _harvesterState = BindLabel(HarvesterStateName);
        _batteryState = BindLabel(BatteryStateName);
        _droneState = BindLabel(DroneStateName);
        _cargoValue = BindLabel(CargoValueName);

        _session.OnStateChanged += HandleSessionChanged;
        RefreshAll();
        _bound = true;
    }

    private Label BindLabel(string name)
    {
        Label label = _root != null ? _root.Q<Label>(name) : null;
        if (label == null)
        {
            Debug.LogError("[ShipWarehouseScreenHost] missing label " + name);
        }

        return label;
    }

    private void HandleSessionChanged()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_session == null || _root == null)
        {
            return;
        }

        SetText(_mineralsValue, _session.Minerals.ToString());
        SetText(_fuelValue, _session.Fuel.ToString());
        SetText(_alloyValue, _session.Alloy.ToString());
        SetText(_cargoValue, _session.CargoWeight + " / "
                + GameSession.CargoCapacityKg + " KG");

        SetState(_harvesterState, "必需", true);
        SetState(_batteryState, _session.BatterySelected ? "已选择" : "未选择",
            _session.BatterySelected);
        SetState(_droneState, _session.DroneSelected ? "已选择" : "未选择",
            _session.DroneSelected);
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
            label.AddToClassList("gear-state-on");
        }
        else
        {
            label.RemoveFromClassList("gear-state-on");
        }
    }

    private static void SetText(Label label, string text)
    {
        if (label != null)
        {
            label.text = text;
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
