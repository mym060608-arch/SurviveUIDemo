using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 04 - Outpost / Module Topology + Upgrade Economy (Phase 05; controller
/// pass in Phase 09B PASS 6).
///
/// Presents the three authoritative modules (Core/OutpostLevel, Harvester,
/// Refinery) as a selectable topology. Every upgrade decision still lives in
/// GameSession.CanUpgrade / TryUpgrade; this screen only renders state, forwards
/// input and shows the exact blocking reason (Alloy / Core prerequisite / max
/// level). The economy itself (Core level gate, Alloy costs, harvester
/// efficiency, refinery process time, LV.3 cap) is untouched by this pass.
///
/// Controller contract (the user's sketch):
///   RS / right stick  move the EXISTING round cursor (mouse emulation)
///   X  / buttonWest   select the module under the cursor
///   A  / buttonSouth  upgrade the selected module
///   B  / buttonEast   leave the page
///
/// The cursor is the shared <see cref="UiPointerState"/> authority - the same
/// pointer the surface map uses, warped onto the real OS cursor. There is no
/// second, page-only cursor, and a real mouse keeps working at any time: the
/// pointer just hands over to whoever moved last.
///
/// Selection is purely presentational (default: Refinery, mirroring the
/// Processing -> Outpost economy loop) and never stored in GameSession.
/// </summary>
public class OutpostScreen
{
    /// <summary>Class the module card under the shared cursor carries.</summary>
    public const string CursorOverClass = "node-cursor";

    /// <summary>USS class that makes the system cursor transparent for the whole
    /// document while this page owns the pointer (see OutpostScreen.uss).</summary>
    public const string CursorHiddenClass = "cursor-blank";

    private const string NodeClass = "outpost-node";

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
    private VisualElement _secondEffectRow;
    private Label _secondEffectKey;
    private Label _secondEffectCurrent;
    private Label _secondEffectNext;

    // Controller.
    private ControllerPromptBar _promptBar;
    private UiCursorOverlay _overlay;
    private bool _cursorOwned;
    private readonly List<VisualElement> _cursorStyled = new List<VisualElement>();
    private VisualElement _cursorHiddenClassHost;
    private readonly List<VisualElement> _pickingSuspended = new List<VisualElement>();
    private readonly List<PickingMode> _pickingPrevious = new List<PickingMode>();
    private GameSession.OutpostModule? _cursorModule;
    private VisualElement _cursorNode;
    private bool _controllerWasEnabled;
    private int _inputReadyFrame;

    public OutpostScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    /// <summary>Raised when the player presses Back on this page. The host
    /// decides the meaning (hub: leave the screen; console: its own focus layer
    /// already owns B, so it never enables Back here).</summary>
    public event Action BackRequested;

    /// <summary>True while this page consumes Back (set by the host).</summary>
    public bool OwnsBack { get; set; }

    /// <summary>
    /// True only for a host that owns the plain 2D cursor (the hub app). A
    /// physical console must leave the OS cursor to its own focus layer: forcing
    /// the hub cursor back on exit is exactly what put a system arrow on screen
    /// when the player walked away from a terminal into free movement.
    /// </summary>
    public bool RestoreSystemCursorOnExit { get; set; }

    /// <summary>The shared controller prompt bar (probes / tests).</summary>
    public ControllerPromptBar PromptBar => _promptBar;

    /// <summary>Module under the shared cursor, or null (probes / tests).</summary>
    public GameSession.OutpostModule? CursorModule => _cursorModule;

    /// <summary>Currently presented module (probes / tests).</summary>
    public GameSession.OutpostModule SelectedModule => _selected;

    /// <summary>The cursor the page draws inside the game UI (probes / tests).</summary>
    public UiCursorOverlay CursorOverlay => _overlay;

    /// <summary>True while the page draws its own cursor and hides the OS one.</summary>
    public bool CursorOwned => _cursorOwned;

    /// <summary>
    /// Suspends UI Toolkit picking for the WHOLE document while this page owns
    /// the pointer.
    ///
    /// This is what finally kills the flickering system arrow: UI Toolkit
    /// re-applies the hovered element's cursor on every hover change, and while
    /// any element can be hovered there is always a frame where the stock arrow
    /// wins. With nothing pickable there is no hover and no cursor application
    /// at all; the page resolves the pad and the mouse through its own hit test
    /// (and the hub tabs through a direct click).
    /// </summary>
    private void SuspendPicking()
    {
        if (_pickingSuspended.Count > 0 || _root.panel == null)
        {
            return;
        }

        VisualElement panelRoot = _root.panel.visualTree;
        _pickingSuspended.Add(panelRoot);

        foreach (VisualElement element in panelRoot.Query<VisualElement>().ToList())
        {
            if (!_pickingSuspended.Contains(element))
            {
                _pickingSuspended.Add(element);
            }
        }

        // Capture every previous value BEFORE touching any of them: the panel
        // root is part of the walk above, and a second capture would record the
        // Ignore we just wrote and never restore it.
        _pickingPrevious.Clear();
        foreach (VisualElement element in _pickingSuspended)
        {
            _pickingPrevious.Add(element.pickingMode);
        }

        for (int i = 0; i < _pickingSuspended.Count; i++)
        {
            _pickingSuspended[i].pickingMode = PickingMode.Ignore;
        }
    }

    private void RestorePicking()
    {
        for (int i = 0; i < _pickingSuspended.Count && i < _pickingPrevious.Count; i++)
        {
            if (_pickingSuspended[i] != null)
            {
                _pickingSuspended[i].pickingMode = _pickingPrevious[i];
            }
        }

        _pickingSuspended.Clear();
        _pickingPrevious.Clear();
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
        _secondEffectRow = BindElement("ModuleSecondEffectRow");
        _secondEffectKey = BindLabel("ModuleSecondEffectKey");
        _secondEffectCurrent = BindLabel("ModuleSecondEffectCurrent");
        _secondEffectNext = BindLabel("ModuleSecondEffectNext");
        _upgradeCost = BindLabel(ElementNames.ModuleUpgradeCostValue);
        _requirement = BindLabel(ElementNames.ModuleUpgradeRequirement);
        _upgradeButton = BindLabelButton(ElementNames.OutpostUpgradeButton,
            () => UpgradeSelected());

        BindIcons();
        BindController();
        Refresh();
    }

    /// <summary>Mounts the three module marks once (a second Bind must never
    /// stack a second drawing).</summary>
    private void BindIcons()
    {
        AddIcon("CoreNodeIcon", OutpostModuleIcon.Kind.Core);
        AddIcon("HarvesterNodeIcon", OutpostModuleIcon.Kind.Harvester);
        AddIcon("RefineryNodeIcon", OutpostModuleIcon.Kind.Refinery);
    }

    private void AddIcon(string hostName, OutpostModuleIcon.Kind kind)
    {
        VisualElement host = _root.Q<VisualElement>(hostName);
        if (host == null)
        {
            LogMissing(hostName);
            return;
        }

        if (host.Q<OutpostModuleIcon>(null) != null)
        {
            return;
        }

        host.Add(new OutpostModuleIcon(kind));
    }

    private void BindController()
    {
        VisualElement promptHost = _root.Q<VisualElement>(ElementNames.OutpostPromptHost);
        _promptBar = ControllerPromptBar.Attach(promptHost, UiFontProvider.CreateCjkFont());
        if (_promptBar != null)
        {
            _promptBar.SetRows(DefaultPromptRows());
            _promptBar.SetVisible(false);
        }
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

    /// <summary>Upgrade the presented module through the authoritative session
    /// (the same path the upgrade button uses). A blocked upgrade changes
    /// nothing - the session returns the reason and state stays untouched.</summary>
    public bool UpgradeSelected()
    {
        if (_session.CanUpgrade(_selected) != GameSession.UpgradeFailureReason.None)
        {
            return false;
        }

        _session.TryUpgrade(_selected);
        Refresh();
        return true;
    }

    // ---- controller ----

    /// <summary>
    /// Per-frame controller input: the right stick drives the shared pointer,
    /// X selects the module under it, A upgrades, B leaves. The host calls this
    /// only while the page is on screen; a hidden page releases the cursor
    /// region and stops listening.
    /// </summary>
    public void Tick(bool visible, bool controllerEnabled)
    {
        if (!visible || !controllerEnabled)
        {
            _controllerWasEnabled = false;
            ClearCursorOver();
            UiPointerState.ClearBounds();
            ReleaseCursor();
            _promptBar?.SetVisible(false);
            return;
        }

        AcquireCursor();

        InputDeviceTracker.Poll();

        if (_promptBar != null)
        {
            _promptBar.SetVisible(true);
            _promptBar.Tick();
        }

        if (!_controllerWasEnabled)
        {
            // The press that opened this page can reach the same frame: the
            // first two frames only observe.
            _controllerWasEnabled = true;
            _inputReadyFrame = Time.frameCount + 2;
        }

        if (Time.frameCount < _inputReadyFrame)
        {
            RefreshPrompt();
            return;
        }

        // The page declares where the pad may push the cursor (screen pixels,
        // bottom-left origin) - the same clamp the surface map uses.
        UiPointerState.SetBounds(ScreenBoundsOf(_root));
        UiPointerState.Tick(Time.unscaledDeltaTime);
        UpdateCursorOver();

        UpdateCursorOverlay();

        if (UiInput.ContextActionPressed())
        {
            SelectUnderCursor();
        }

        if (UiInput.ConfirmPressed())
        {
            UpgradeSelected();
        }

        HandleMouseClick();

        if (OwnsBack && UiInput.BackPressed())
        {
            UiInput.ConsumeBackForFrame();
            BackRequested?.Invoke();
        }

        RefreshPrompt();
    }

    /// <summary>X: the module under the cursor becomes the presented one. The
    /// mouse path below funnels into exactly the same call, so a pad press and a
    /// mouse click can never diverge.</summary>
    private void SelectUnderCursor()
    {
        GameSession.OutpostModule? module = ModuleOf(_cursorNode);
        if (module.HasValue)
        {
            SelectModule(module.Value);
        }
    }

    /// <summary>
    /// Real mouse support without UI Toolkit picking: hover comes from the
    /// shared pointer (already tracked) and a left click is hit-tested against
    /// the upgrade action and the module cards.
    /// </summary>
    private void HandleMouseClick()
    {
        // Called after the opening-press grace, so the click produced by the
        // press that opened this terminal can never select or upgrade by itself.
        if (Time.frameCount < _inputReadyFrame)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Vector2 point = PanelPointOf(UiPointerState.Position);
        if (_upgradeButton != null && _upgradeButton.worldBound.Contains(point))
        {
            UpgradeSelected();
            return;
        }

        if (NodeAtCursor() != null)
        {
            SelectUnderCursor();
            return;
        }

        // Hub chrome is suspended from picking as well, so a mouse click on the
        // bottom tabs is routed straight to the tab under the pointer.
        foreach (string name in new[]
                 {
                     ElementNames.NavOverview, ElementNames.NavPreparation,
                     ElementNames.NavProcessing, ElementNames.NavOutpost
                 })
        {
            Button tab = _root.panel.visualTree.Q<Button>(name);
            if (tab != null && tab.worldBound.Contains(point))
            {
                UiToolkitPointerSimulator.ClickDirect(tab);
                return;
            }
        }
    }

    /// <summary>
    /// Resolves which module card the shared cursor is over. Hit-testing goes
    /// through the panel's own picker, so the pad and the mouse always agree on
    /// what is under the dot.
    /// </summary>
    private void UpdateCursorOver()
    {
        VisualElement node = NodeAtCursor();
        if (ReferenceEquals(node, _cursorNode))
        {
            return;
        }

        ClearCursorOver();
        _cursorNode = node;

        if (_cursorNode != null)
        {
            _cursorNode.AddToClassList(CursorOverClass);
            _cursorModule = ModuleOf(_cursorNode);
        }

    }

    /// <summary>
    /// The card under the shared pointer, resolved geometrically: the page is
    /// deliberately unpickable (that is what keeps UI Toolkit's cursor manager
    /// out of the picture), so the page does its own hit test.
    /// </summary>
    private VisualElement NodeAtCursor()
    {
        Vector2 point = PanelPointOf(UiPointerState.Position);
        if (_coreNode != null && _coreNode.worldBound.Contains(point))
        {
            return _coreNode;
        }

        if (_harvesterNode != null && _harvesterNode.worldBound.Contains(point))
        {
            return _harvesterNode;
        }

        return _refineryNode != null && _refineryNode.worldBound.Contains(point)
            ? _refineryNode
            : null;
    }

    private void ClearCursorOver()
    {
        if (_cursorNode != null)
        {
            _cursorNode.RemoveFromClassList(CursorOverClass);
        }

        _cursorNode = null;
        _cursorModule = null;
    }

    private GameSession.OutpostModule? ModuleOf(VisualElement node)
    {
        if (ReferenceEquals(node, _coreNode))
        {
            return GameSession.OutpostModule.Core;
        }

        if (ReferenceEquals(node, _harvesterNode))
        {
            return GameSession.OutpostModule.Harvester;
        }

        return ReferenceEquals(node, _refineryNode)
            ? GameSession.OutpostModule.Refinery
            : (GameSession.OutpostModule?)null;
    }

    /// <summary>
    /// Takes the project's EXISTING round cursor for this page: the same dot /
    /// The page does NOT fight for the OS cursor any more: UI Toolkit re-applies
    /// the hovered element's own cursor style on every hover change and Windows
    /// refuses small hardware-cursor textures, which left the stock arrow on
    /// screen. Instead the page HIDES the system cursor and draws the project's
    /// dot / dot+ring itself (<see cref=UiCursorOverlay/>), so what the player
    /// sees is always ours - every frame, over every element.
    /// </summary>
    private void AcquireCursor()
    {
        if (_cursorOwned)
        {
            return;
        }

        if (_overlay == null)
        {
            VisualElement host = _root.panel != null ? _root.panel.visualTree : null;
            if (host == null)
            {
                return;
            }

            _overlay = new UiCursorOverlay();
            host.Add(_overlay);
        }

        // The system pointer is made INVISIBLE rather than hidden: a windowed
        // Windows player re-shows a hardware cursor whenever UI Toolkit applies
        // one on a hover change, which is exactly how the stock arrow kept
        // reappearing next to our drawn dot.
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.SetCursor(UiCursorOverlay.InvisibleTexture, Vector2.zero,
            CursorMode.Auto);
        ApplyInvisibleCursorStyles();
        SuspendPicking();
        _cursorHiddenClassHost = _root.panel != null ? _root.panel.visualTree : null;
        _cursorHiddenClassHost?.AddToClassList(CursorHiddenClass);
        _cursorOwned = true;
    }

    private void ReleaseCursor()
    {
        if (!_cursorOwned)
        {
            return;
        }

        _cursorOwned = false;
        _overlay?.Hide();
        RestorePicking();
        _cursorHiddenClassHost?.RemoveFromClassList(CursorHiddenClass);
        _cursorHiddenClassHost = null;
        ClearInvisibleCursorStyles();

        // Only the 2D hub restores the plain cursor here. A physical console hands
        // the cursor back to the console-focus layer (which hides and locks it for
        // free movement), so the page must not re-show it.
        if (RestoreSystemCursorOnExit && UiInput.ActiveBackLayer != UiInput.UiLayer.ConsoleFocus)
        {
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }
        else if (!RestoreSystemCursorOnExit)
        {
            // Hand the OS cursor back untouched: the console layer re-applies its
            // own mode on every state change.
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }

    /// <summary>Keeps the drawn cursor on the shared pointer position.</summary>
    private void UpdateCursorOverlay()
    {
        if (_overlay == null)
        {
            return;
        }

        if (!_cursorOwned)
        {
            _overlay.Hide();
            return;
        }

        // Re-apply the transparent system cursor every frame: the ship's
        // console-focus layer (and UI Toolkit itself) re-applies its own cursor
        // on state changes, and two pointers on screen is exactly the bug this
        // replaced.
        UnityEngine.Cursor.SetCursor(UiCursorOverlay.InvisibleTexture, Vector2.zero,
            CursorMode.Auto);

        _overlay.PlaceAt(PanelPointOf(UiPointerState.Position));
        _overlay.SetHover(_cursorNode != null);
    }

    /// <summary>
    /// Marks every element of the document with the transparent cursor, so the
    /// cursor UI Toolkit applies on each hover change is invisible too.
    /// </summary>
    private void ApplyInvisibleCursorStyles()
    {
        VisualElement host = _root.panel != null ? _root.panel.visualTree : null;
        if (host == null)
        {
            return;
        }

        var invisible = new StyleCursor(UiCursorOverlay.InvisibleUiCursor);
        _cursorStyled.Clear();

        foreach (VisualElement element in host.Query<VisualElement>().ToList())
        {
            element.style.cursor = invisible;
            _cursorStyled.Add(element);
        }

        host.style.cursor = invisible;
        if (!_cursorStyled.Contains(host))
        {
            _cursorStyled.Add(host);
        }
    }

    private void ClearInvisibleCursorStyles()
    {
        foreach (VisualElement element in _cursorStyled)
        {
            element.style.cursor = StyleKeyword.Null;
        }

        _cursorStyled.Clear();
    }

    /// <summary>Screen-pixel rect (bottom-left origin) of a page element.</summary>
    private static Rect ScreenBoundsOf(VisualElement element)
    {
        if (element == null || element.panel == null)
        {
            return Rect.MinMaxRect(0f, 0f, Screen.width, Screen.height);
        }

        Rect panelRoot = element.panel.visualTree.worldBound;
        float scaleX = Screen.width / Mathf.Max(1f, panelRoot.width);
        float scaleY = Screen.height / Mathf.Max(1f, panelRoot.height);

        Rect page = element.worldBound;
        float minX = page.xMin * scaleX;
        float maxX = page.xMax * scaleX;
        float minY = Screen.height - page.yMax * scaleY;
        float maxY = Screen.height - page.yMin * scaleY;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    /// <summary>Screen pixels -&gt; panel pixels (UI Toolkit grows downwards).</summary>
    private Vector2 PanelPointOf(Vector2 screenPoint)
    {
        Rect panelRoot = _root.panel.visualTree.worldBound;
        float scaleX = panelRoot.width / Mathf.Max(1f, Screen.width);
        float scaleY = panelRoot.height / Mathf.Max(1f, Screen.height);
        return new Vector2(screenPoint.x * scaleX,
            (Screen.height - screenPoint.y) * scaleY);
    }

    /// <summary>Prompt rows follow the operable set: a blocked upgrade is not
    /// advertised (a page must never print a key that does nothing).</summary>
    private void RefreshPrompt()
    {
        if (_promptBar == null)
        {
            return;
        }

        bool canUpgrade = _session.CanUpgrade(_selected)
                          == GameSession.UpgradeFailureReason.None;
        _promptBar.SetRows(canUpgrade
            ? new[]
            {
                ControllerPromptRow.Key("光标", UiAction.MapCursor),
                ControllerPromptRow.Key("选择", UiAction.ContextAction),
                ControllerPromptRow.Key("升级", UiAction.Confirm),
                ControllerPromptRow.Key("返回", UiAction.Back)
            }
            : new[]
            {
                ControllerPromptRow.Key("光标", UiAction.MapCursor),
                ControllerPromptRow.Key("选择", UiAction.ContextAction),
                ControllerPromptRow.Key("返回", UiAction.Back)
            });
    }

    private static ControllerPromptRow[] DefaultPromptRows()
    {
        return new[]
        {
            ControllerPromptRow.Key("光标", UiAction.MapCursor),
            ControllerPromptRow.Key("选择", UiAction.ContextAction),
            ControllerPromptRow.Key("升级", UiAction.Confirm),
            ControllerPromptRow.Key("返回", UiAction.Back)
        };
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

        SetText(_coreNodeStatus, "当前上限 LV." + cap);
        SetText(_harvesterNodeStatus, "采集效率 " + _session.HarvesterEfficiencyPercent + "%");
        SetText(_refineryNodeStatus, "加工时间 " + Seconds(_session.RefineryProcessSeconds));

        SetNodeSelected(_coreNode, _selected == GameSession.OutpostModule.Core);
        SetNodeSelected(_harvesterNode, _selected == GameSession.OutpostModule.Harvester);
        SetNodeSelected(_refineryNode, _selected == GameSession.OutpostModule.Refinery);

        // A subsystem whose next tier is blocked by the Core is visibly muted
        // (text + emphasis; never colour-only).
        SetLocked(_harvesterNode, IsCoreLocked(GameSession.OutpostModule.Harvester, harvester));
        SetLocked(_refineryNode, IsCoreLocked(GameSession.OutpostModule.Refinery, refinery));

        RefreshDetail();
        RefreshPrompt();
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
            _detailLevels.text = "LV." + level + " ▶ LV." + (level + 1);
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

        // The action reads "升级" whatever the state (the request): WHY it is
        // unavailable is carried by the 条件 row and the disabled state, never
        // by rewriting the button into a second label.
        if (_upgradeButton != null)
        {
            _upgradeButton.text = "升级";
            _upgradeButton.SetEnabled(reason == GameSession.UpgradeFailureReason.None);
        }
    }

    /// <summary>Core: the subsystem cap it unlocks (and nothing else - the row
    /// is hidden rather than filled with a made-up second number).</summary>
    private void FillCoreDetail(int level)
    {
        _effectTitle.text = "模块上限";
        _currentEffect.text = "LV." + _session.SubsystemLevelCap;
        _nextEffect.text = level >= GameSession.MaxModuleLevel
            ? "—"
            : "LV." + Mathf.Min(GameSession.MaxModuleLevel, _session.SubsystemLevelCap + 1);
        HideSecondEffect();
    }

    /// <summary>Harvester: extraction efficiency and the surface harvest time it
    /// derives (both read from the authoritative session formulas).</summary>
    private void FillHarvesterDetail(int level)
    {
        _effectTitle.text = "采集效率";
        _currentEffect.text = _session.HarvesterEfficiencyPercent + "%";
        _nextEffect.text = level >= GameSession.MaxModuleLevel
            ? "—"
            : PercentForLevel(level + 1) + "%";

        ShowSecondEffect("采集耗时",
            Seconds(_session.SurfaceHarvestSeconds),
            level >= GameSession.MaxModuleLevel
                ? "—"
                : Seconds(HarvestSecondsForLevel(level + 1)));
    }

    /// <summary>Refinery: process time and the throughput it derives.</summary>
    private void FillRefineryDetail(int level)
    {
        _effectTitle.text = "加工时间";
        _currentEffect.text = Seconds(_session.RefineryProcessSeconds);
        _nextEffect.text = level >= GameSession.MaxModuleLevel
            ? "—"
            : Seconds(DurationForLevel(level + 1));

        ShowSecondEffect("加工效率",
            ThroughputPercent(_session.RefineryProcessSeconds) + "%",
            level >= GameSession.MaxModuleLevel
                ? "—"
                : ThroughputPercent(DurationForLevel(level + 1)) + "%");
    }

    /// <summary>Second effect row (efficiency / harvest time). Hidden when the
    /// selected module has only one authoritative effect.</summary>
    private void ShowSecondEffect(string key, string current, string next)
    {
        if (_secondEffectRow == null)
        {
            return;
        }

        _secondEffectRow.style.display = DisplayStyle.Flex;
        SetText(_secondEffectKey, key);
        SetText(_secondEffectCurrent, current);
        SetText(_secondEffectNext, next);
    }

    private void HideSecondEffect()
    {
        if (_secondEffectRow != null)
        {
            _secondEffectRow.style.display = DisplayStyle.None;
        }
    }

    /// <summary>Throughput relative to the LV.1 process time (2.0 s = 100%).</summary>
    private static int ThroughputPercent(float seconds)
    {
        return Mathf.RoundToInt(2.0f / Mathf.Max(0.01f, seconds) * 100f);
    }

    /// <summary>Surface harvest time at a module level (same formula the surface
    /// scene uses).</summary>
    private static float HarvestSecondsForLevel(int level)
    {
        float multiplier = PercentForLevel(level) / 100f;
        return GameSession.SurfaceBaseHarvestSeconds / Mathf.Max(0.01f, multiplier);
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

    private VisualElement BindElement(string name)
    {
        VisualElement element = _root.Q<VisualElement>(name);
        if (element == null)
        {
            LogMissing(name);
        }

        return element;
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
