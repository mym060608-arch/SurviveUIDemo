using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Console02's 仓库 (Warehouse) page - the ship's read-only inventory view
/// (Phase 07B.2; rebuilt as a tabbed item grid in Phase 09B PASS 9).
///
/// Structure (the approved reference):
///
///   仓库                                  02
///   [资源] [装备]                    LB ◀ ▶ RB
///   ┌ 4 x 3 item grid ┐ │ 详情 / big icon / name xN / description
///   └─────────────────┘ │
///                       [B] 返回
///
/// What it is NOT: an editor. The page only views, categorizes, browses and
/// shows details. It has no use / drop / dismantle / sell / transfer / equip
/// action, so it never prints a Confirm key and A does nothing here (the game
/// has no such gameplay yet - inventing one is explicitly out of scope).
///
/// Controller contract:
///   LB / RB        PreviousTab / NextTab  -> 资源 / 装备 (exactly two pages)
///   LS / D-Pad     NavigationVector       -> move the cell focus in the grid
///   B / Escape     the console focus layer's Back -> leave the terminal
///
/// Focus: the page reuses the shared <see cref="UiFocusNavigator"/> as the ONE
/// focus authority, so the mouse and the pad can never disagree. Only real item
/// cells are registered; an empty slot is not focusable, not clickable and can
/// never become a dead end. LB/RB tab switching is the navigator's own tab
/// group switch, and each tab remembers the cell the player left on.
///
/// Every value and every description comes from <see cref="WarehouseCatalog"/>
/// (real GameSession data), refreshed on OnStateChanged - the page never stores
/// gameplay state.
/// </summary>
public class ShipWarehouseScreenHost : MonoBehaviour
{
    // ---- Authoritative element names (WarehouseConsole.uxml) ----
    public const string TitleName = "WarehouseTitleLabel";
    public const string ResourcesTabName = "WarehouseResourcesTab";
    public const string EquipmentTabName = "WarehouseEquipmentTab";
    public const string TabPrevHintName = "WarehouseTabPrevHint";
    public const string TabNextHintName = "WarehouseTabNextHint";
    public const string GridName = "WarehouseGrid";
    public const string DetailIconHostName = "WarehouseDetailIconHost";
    public const string DetailNameName = "WarehouseDetailName";
    public const string DetailCountName = "WarehouseDetailCount";
    public const string DetailStateName = "WarehouseDetailState";
    public const string DetailTextName = "WarehouseDetailText";
    public const string NoteLabelName = "WarehouseNoteLabel";
    public const string PromptHostName = "WarehousePromptHost";

    // Legacy element names (kept so older probes / tests keep finding the real
    // read-outs; they now live on the item cells).
    public const string MineralsValueName = "WarehouseMineralsValue";
    public const string FuelValueName = "WarehouseFuelValue";
    public const string AlloyValueName = "WarehouseAlloyValue";
    public const string HarvesterStateName = "WarehouseHarvesterState";
    public const string BatteryStateName = "WarehouseBatteryState";
    public const string DroneStateName = "WarehouseDroneState";
    public const string CargoValueName = "WarehouseCargoValue";

    /// <summary>Grid geometry: the reference shows a 4 x 3 visible area.</summary>
    public const int GridColumns = 4;
    public const int GridRows = 3;
    public const int GridSlots = GridColumns * GridRows;

    /// <summary>Style class the shared focus navigator paints.</summary>
    public const string FocusClass = "wh-cell-focused";

    /// <summary>Class of one real item cell (and of an empty slot).</summary>
    public const string CellClass = "wh-cell";

    /// <summary>Class of an inert empty slot.</summary>
    public const string EmptyCellClass = "wh-cell-empty";

    [SerializeField]
    public StyleSheet warehouseStyleSheet;

    private UIDocument _document;
    private VisualElement _root;
    private GameSession _session;
    private bool _bound;

    private Label _title;
    private Label _tabPrevHint;
    private Label _tabNextHint;
    private Button _resourcesTab;
    private Button _equipmentTab;
    private VisualElement _grid;
    private VisualElement _detailIconHost;
    private Label _detailName;
    private Label _detailCount;
    private Label _detailState;
    private Label _detailText;

    private UiFocusNavigator _focus;
    private ControllerPromptBar _promptBar;
    private string _detailIconItemId;

    private string _activeTab = WarehouseCatalog.ResourcesTab;
    private readonly List<WarehouseItem> _items = new List<WarehouseItem>();
    private readonly List<VisualElement> _cells = new List<VisualElement>();
    private readonly Dictionary<string, VisualElement> _tabMemory =
        new Dictionary<string, VisualElement>();

    private VisualElement _pendingFocusCell;
    private ShipConsoleFocusController _focusController;
    private int _nextFocusSearchFrame;
    private bool _inputWasEnabled;

    /// <summary>True once this console's document is bound.</summary>
    public bool IsBound => _bound;

    /// <summary>The warehouse document root (probes / tests).</summary>
    public VisualElement Root => _root;

    /// <summary>Active tab id ("resources" / "equipment").</summary>
    public string ActiveTab => _activeTab;

    /// <summary>The focused item (null while nothing is focused).</summary>
    public WarehouseItem FocusedItem
    {
        get
        {
            int index = FocusedIndex;
            return index >= 0 && index < _items.Count ? _items[index] : null;
        }
    }

    /// <summary>Index of the focused cell inside the active page (-1 = none).</summary>
    public int FocusedIndex => _cells.IndexOf(_focus != null ? _focus.Focused : null);

    /// <summary>Number of real (focusable) cells on the active page.</summary>
    public int ItemCount => _items.Count;

    /// <summary>Number of inert empty slots on the active page.</summary>
    public int EmptySlotCount { get; private set; }

    /// <summary>The shared focus navigator (probes / tests).</summary>
    public UiFocusNavigator Focus => _focus;

    /// <summary>The bottom key line (probes / tests).</summary>
    public ControllerPromptBar PromptBar => _promptBar;

    /// <summary>Detail panel name currently shown.</summary>
    public string DetailNameText => _detailName != null ? _detailName.text : string.Empty;

    /// <summary>Detail panel amount / state currently shown.</summary>
    public string DetailCountText => _detailCount != null ? _detailCount.text : string.Empty;

    /// <summary>Detail panel description currently shown.</summary>
    public string DetailDescriptionText => _detailText != null ? _detailText.text : string.Empty;

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

        if (_bound)
        {
            Tick(IsConsoleFocused());
        }
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    /// <summary>
    /// The page only reacts to the pad while the player is really focused on
    /// THIS terminal (same contract as the other console pages): an unfocused
    /// screen must never move its focus or answer a press.
    /// </summary>
    private bool IsConsoleFocused()
    {
        if (_focusController == null && Time.frameCount >= _nextFocusSearchFrame)
        {
            _focusController = Object.FindAnyObjectByType<ShipConsoleFocusController>();
            _nextFocusSearchFrame = Time.frameCount + 30;
        }

        WorldConsoleSurface active = _focusController != null
            ? _focusController.ActiveSurface
            : null;

        return _focusController != null && _focusController.FocusActive
               && active != null && ReferenceEquals(active.WarehouseHost, this);
    }

    /// <summary>
    /// Per-frame page tick. <paramref name="interactive"/> is false while the
    /// player is not focused on this terminal, so nothing here can fight the
    /// ship's movement or another console.
    /// </summary>
    public void Tick(bool interactive)
    {
        if (!_bound)
        {
            return;
        }

        if (!interactive)
        {
            if (_inputWasEnabled)
            {
                _inputWasEnabled = false;
                _focus.Enabled = false;
                _promptBar?.SetVisible(false);
            }

            return;
        }

        _inputWasEnabled = true;
        _focus.Enabled = true;
        InputDeviceTracker.Poll();
        RefreshDeviceHints();

        if (_promptBar != null)
        {
            _promptBar.SetVisible(true);
            _promptBar.Tick();
        }

        // The shared navigator owns the movement pacing (direction repeat), the
        // LB / RB tab switch and Confirm. Confirm is a no-op on this page (see
        // the no-op action registered per cell), so A really does nothing here.
        _focus.Tick();

        if (_focus.ActiveTab != _activeTab)
        {
            // The navigator paged to the other category: rebuild the grid for it.
            _activeTab = _focus.ActiveTab;
            RebuildPage();
            RefreshPrompt();
        }

        // A page that just opened (or was rebuilt) has no focus until its cells
        // are laid out: apply the remembered cell, else the first real one.
        if (_focus.Focused == null)
        {
            if (_pendingFocusCell == null || !_focus.SetFocus(_pendingFocusCell))
            {
                if (_focus.FocusDefault())
                {
                    _pendingFocusCell = null;
                }
            }
            else
            {
                _pendingFocusCell = null;
            }
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

        _title = BindLabel(TitleName);
        _tabPrevHint = BindLabel(TabPrevHintName);
        _tabNextHint = BindLabel(TabNextHintName);
        _detailName = BindLabel(DetailNameName);
        _detailCount = BindLabel(DetailCountName);
        _detailState = BindLabel(DetailStateName);
        _detailText = BindLabel(DetailTextName);
        _detailIconHost = BindElement(DetailIconHostName);
        _grid = BindElement(GridName);

        _resourcesTab = BindTab(ResourcesTabName, WarehouseCatalog.ResourcesTab);
        _equipmentTab = BindTab(EquipmentTabName, WarehouseCatalog.EquipmentTab);

        // ONE focus authority for pad and mouse (shared Phase 09B navigator).
        _focus = new UiFocusNavigator(FocusClass);
        _focus.FocusedChanged += HandleFocusedChanged;

        _promptBar = ControllerPromptBar.Attach(
            _root.Q<VisualElement>(PromptHostName), UiFontProvider.CreateCjkFont());
        RefreshPrompt();

        _session.OnStateChanged += HandleSessionChanged;
        RebuildPage();
        RefreshDeviceHints();
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

    private VisualElement BindElement(string name)
    {
        VisualElement element = _root != null ? _root.Q<VisualElement>(name) : null;
        if (element == null)
        {
            Debug.LogError("[ShipWarehouseScreenHost] missing element " + name);
        }

        return element;
    }

    private Button BindTab(string name, string tabId)
    {
        Button button = _root != null ? _root.Q<Button>(name) : null;
        if (button == null)
        {
            Debug.LogError("[ShipWarehouseScreenHost] missing tab " + name);
            return null;
        }

        // A mouse click on a tab goes through the SAME tab switch as LB / RB.
        button.clicked += () => SwitchTab(tabId);
        return button;
    }

    // ---- page build ----

    /// <summary>
    /// Rebuilds the grid for the active tab: real items become focusable cells,
    /// the remaining slots stay inert (a faint crosshair, no focus, no click).
    /// </summary>
    private void RebuildPage()
    {
        if (_grid == null)
        {
            return;
        }

        _items.Clear();
        _items.AddRange(WarehouseCatalog.ForTab(_activeTab, _session));

        _grid.Clear();
        _cells.Clear();
        _focus.Clear();

        var cells = new List<VisualElement>();
        int slot = 0;

        foreach (WarehouseItem item in _items)
        {
            if (slot >= GridSlots)
            {
                break;
            }

            VisualElement cell = BuildCell(item, slot);
            _grid.Add(cell);
            cells.Add(cell);
            _cells.Add(cell);
            slot++;
        }

        EmptySlotCount = GridSlots - slot;
        for (; slot < GridSlots; slot++)
        {
            _grid.Add(BuildEmptySlot(slot));
        }

        _focus.SetItems(cells);
        // The ACTIVE category is registered first: the navigator activates the first
        // group it sees, so it can never end up pointing at the hidden page.
        _focus.AddTab(_activeTab, cells);
        _focus.AddTab(OtherTab(), null);
        SetTabActive(_activeTab);

        // §18: returning to a tab restores the cell the player left on; if that
        // item is gone, the first real cell of the page takes the focus.
        VisualElement remembered;
        if (!_tabMemory.TryGetValue(_activeTab, out remembered)
            || remembered == null || !cells.Contains(remembered))
        {
            remembered = cells.Count > 0 ? cells[0] : null;
        }

        // Applied again by Tick() once the new cells have a layout.
        _pendingFocusCell = remembered;
        _focus.SetFocus(remembered);

        RefreshDetail();
    }

    private VisualElement BuildCell(WarehouseItem item, int slot)
    {
        // A Button so the mouse and the pad share ONE selection path (clicking a
        // cell focuses it, exactly like the focus ring moving onto it).
        var cell = new Button
        {
            name = "WarehouseCell_" + item.Id,
            text = string.Empty
        };
        cell.AddToClassList(CellClass);
        if (slot % GridColumns == GridColumns - 1)
        {
            cell.AddToClassList("wh-cell-rowend");
        }

        var iconHost = new VisualElement { pickingMode = PickingMode.Ignore };
        iconHost.AddToClassList("wh-cell-icon");
        iconHost.Add(CreateMark(item.Icon));
        cell.Add(iconHost);

        string value = !string.IsNullOrEmpty(item.ValueText)
            ? item.ValueText
            : item.AmountText;
        if (!string.IsNullOrEmpty(value))
        {
            var valueLabel = new Label(value) { pickingMode = PickingMode.Ignore };
            valueLabel.AddToClassList("wh-cell-value");
            if (!string.IsNullOrEmpty(item.ValueElementName))
            {
                valueLabel.name = item.ValueElementName;
            }

            cell.Add(valueLabel);
        }

        if (!string.IsNullOrEmpty(item.State))
        {
            var stateLabel = new Label(item.State) { pickingMode = PickingMode.Ignore };
            stateLabel.AddToClassList("wh-cell-sub");
            if (item.StateOn)
            {
                stateLabel.AddToClassList("wh-cell-sub-on");
            }

            if (!string.IsNullOrEmpty(item.ValueElementName))
            {
                stateLabel.name = item.ValueElementName;
            }

            cell.Add(stateLabel);
        }

        cell.Add(Corner("wh-corner-tl"));
        cell.Add(Corner("wh-corner-br"));

        cell.clicked += () => SelectCell(cell);
        cell.RegisterCallback<PointerEnterEvent>(_ => HandleCellHover(cell));

        // This page has NO item action: registering an explicit no-op keeps the
        // shared navigator from synthesizing a click, so Confirm really does nothing.
        _focus.Register(cell, NoItemAction);
        return cell;
    }

    private static void NoItemAction()
    {
    }

    private string OtherTab()
    {
        return _activeTab == WarehouseCatalog.ResourcesTab
            ? WarehouseCatalog.EquipmentTab
            : WarehouseCatalog.ResourcesTab;
    }

    private static VisualElement Corner(string className)
    {
        var corner = new VisualElement { pickingMode = PickingMode.Ignore };
        corner.AddToClassList("wh-cell-corner");
        corner.AddToClassList(className);
        return corner;
    }

    private VisualElement BuildEmptySlot(int slot)
    {
        var empty = new VisualElement { pickingMode = PickingMode.Ignore };
        empty.AddToClassList(CellClass);
        empty.AddToClassList(EmptyCellClass);
        if (slot % GridColumns == GridColumns - 1)
        {
            empty.AddToClassList("wh-cell-rowend");
        }

        var mark = new Label("+") { pickingMode = PickingMode.Ignore };
        mark.AddToClassList("wh-empty-mark");
        empty.Add(mark);
        return empty;
    }

    // ---- selection / detail ----

    /// <summary>
    /// Selects a cell through the ONE focus authority. The pad's Move() and the
    /// mouse click both end up here, so the two devices can never hold different
    /// selections.
    /// </summary>
    private void SelectCell(VisualElement cell)
    {
        _focus.SetFocus(cell);
        RefreshDetail();
    }

    /// <summary>A real mouse moving over the grid takes the selection with it.</summary>
    private void HandleCellHover(VisualElement cell)
    {
        // Hover only steers while the player is on mouse+keyboard: a resting
        // cursor must never steal the focus the pad just moved.
        if (InputDeviceTracker.GamepadActive)
        {
            return;
        }

        _focus.SetFocus(cell);
    }

    private void HandleFocusedChanged(VisualElement element)
    {
        if (!string.IsNullOrEmpty(_activeTab) && element != null)
        {
            _tabMemory[_activeTab] = element;
        }

        RefreshDetail();
    }

    private void RefreshDetail()
    {
        WarehouseItem item = FocusedItem;
        if (item == null)
        {
            SetText(_detailName, "—");
            SetText(_detailCount, string.Empty);
            SetText(_detailState, string.Empty);
            SetText(_detailText, string.Empty);
            return;
        }

        SetText(_detailName, item.Name);
        SetText(_detailCount, !string.IsNullOrEmpty(item.ValueText)
            ? item.ValueText
            : item.AmountText);
        SetText(_detailState, item.State);
        SetText(_detailText, item.Description);
        SetIcon(item);
    }

    private void SetIcon(WarehouseItem item)
    {
        if (_detailIconHost == null || item == null)
        {
            return;
        }

        if (_detailIconItemId == item.Id && _detailIconHost.childCount > 0)
        {
            return;
        }

        _detailIconItemId = item.Id;
        _detailIconHost.Clear();

        // Inset box so the enlarged mark keeps the reference framing.
        var box = new VisualElement { pickingMode = PickingMode.Ignore };
        box.AddToClassList("wh-detail-icon");
        box.Add(CreateMark(item.Icon));
        _detailIconHost.Add(box);
    }

    /// <summary>
    /// The mark of one item, drawn with the SAME code-drawn vocabulary the
    /// rest of the project already uses: the three materials come from the
    /// Processing page0027s sketch set and the equipment from the Preparation
    /// page0027s, so one item never shows two different icons across screens.
    /// (The payload crate has no counterpart elsewhere, so it keeps its own mark.)
    /// </summary>
    private static VisualElement CreateMark(WarehouseItemKind kind)
    {
        switch (kind)
        {
            case WarehouseItemKind.Mineral:
                return new ProcessingSketchIcon(ProcessingSketchIcon.Kind.Ore, true);
            case WarehouseItemKind.Fuel:
                return new ProcessingSketchIcon(ProcessingSketchIcon.Kind.Fuel, true);
            case WarehouseItemKind.Alloy:
                return new ProcessingSketchIcon(ProcessingSketchIcon.Kind.Alloy, true);
            case WarehouseItemKind.Harvester:
                return new PreparationSketchIcon(PreparationSketchIcon.Kind.Harvester);
            case WarehouseItemKind.Battery:
                return new PreparationSketchIcon(PreparationSketchIcon.Kind.Battery);
            case WarehouseItemKind.Drone:
                return new PreparationSketchIcon(PreparationSketchIcon.Kind.Drone);
            default:
                return new WarehouseItemIcon();
        }
    }

    // ---- tabs / presentation ----

    /// <summary>Switches the visible page (LB / RB, a tab click or a test).</summary>
    public void SwitchTab(string tabId)
    {
        if (!_bound || string.IsNullOrEmpty(tabId) || tabId == _activeTab)
        {
            return;
        }

        _activeTab = tabId;
        RebuildPage();
        RefreshPrompt();
    }

    private void SetTabActive(string tabId)
    {
        bool resources = tabId == WarehouseCatalog.ResourcesTab;
        SetClass(_resourcesTab, "wh-tab-active", resources);
        SetClass(_equipmentTab, "wh-tab-active", !resources);
    }

    /// <summary>Fills the paging glyphs for the device in hand.</summary>
    private void RefreshDeviceHints()
    {
        SetText(_tabPrevHint, InputDeviceTracker.PromptGlyph(UiAction.PreviousTab));
        SetText(_tabNextHint, InputDeviceTracker.PromptGlyph(UiAction.NextTab));
    }

    /// <summary>
    /// The page's key line: the paging hint already lives in the tab row, so the
    /// bottom row only names Back. There is no item action, so no Confirm row.
    /// </summary>
    private void RefreshPrompt()
    {
        if (_promptBar == null)
        {
            return;
        }

        _promptBar.SetRows(new[]
        {
            ControllerPromptRow.Key("返回", UiAction.Back)
        });
    }

    private void HandleSessionChanged()
    {
        RefreshAll();
    }

    /// <summary>
    /// Re-reads the authoritative session: the amounts on the cells and the
    /// detail of the focused item are rebuilt from the catalog.
    /// </summary>
    public void RefreshAll()
    {
        if (!_bound)
        {
            return;
        }

        // Remember the focused ITEM (not the element): a refresh must never drop
        // the focus or move it off screen (§18).
        WarehouseItem before = FocusedItem;
        string beforeId = before != null ? before.Id : null;

        _items.Clear();
        _items.AddRange(WarehouseCatalog.ForTab(_activeTab, _session));

        VisualElement target = null;
        for (int i = 0; i < _cells.Count && i < _items.Count; i++)
        {
            UpdateCell(_cells[i], _items[i]);
            if (beforeId != null && _items[i].Id == beforeId)
            {
                target = _cells[i];
            }
        }

        if (target != null)
        {
            _focus.SetFocus(target);
        }

        RefreshDetail();
    }

    private static void UpdateCell(VisualElement cell, WarehouseItem item)
    {
        string value = !string.IsNullOrEmpty(item.ValueText) ? item.ValueText : item.AmountText;

        var valueLabel = cell.Q<Label>(className: "wh-cell-value");
        if (valueLabel != null)
        {
            valueLabel.text = value;
            valueLabel.style.display = string.IsNullOrEmpty(value)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        var stateLabel = cell.Q<Label>(className: "wh-cell-sub");
        if (stateLabel != null)
        {
            stateLabel.text = item.State;
            stateLabel.style.display = string.IsNullOrEmpty(item.State)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            SetClass(stateLabel, "wh-cell-sub-on", item.StateOn);
        }
    }

    private static void SetClass(VisualElement element, string className, bool on)
    {
        if (element == null)
        {
            return;
        }

        if (on)
        {
            element.AddToClassList(className);
        }
        else
        {
            element.RemoveFromClassList(className);
        }
    }

    private static void SetText(Label label, string text)
    {
        if (label != null && label.text != text)
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
