using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 03 - Processing / 资源加工 (Phase 04; controller + presentation
/// upgrade in Phase 09B PASS 5).
///
/// The page still renders the authoritative refinery flow owned by GameSession
/// and owns no economy of its own:
/// - the recipe list is a projection of the single real recipe the session
///   supports (minerals in, alloy + fuel out),
/// - the quantity control selects any run size inside
///   [ProcessBatchMin, min(ProcessBatchMax, minerals)],
/// - expected / settled output, the short run timer, the inventory and the
///   refinery metadata all read the session,
/// - every resource mutation goes through GameSession.
///
/// Controller contract (Phase 09B):
/// - Left stick / D-Pad (arrow keys) walk the page: 加工列表 <-> 加工数量,
/// - left / right adjust the quantity only while the quantity control is
///   focused, and never past a value the stock cannot pay for,
/// - A confirms the run, B leaves the page (raised as <see cref="BackRequested"/>
///   so each host decides what "back" means: the hub returns to the previous
///   screen, a physical console keeps B for its own exit),
/// - the prompt bar only ever lists keys that really work in the current state.
///
/// Rendering is cached-reference only: state changes repaint through
/// <see cref="Refresh"/> (GameSession.OnStateChanged), and the per-frame
/// <see cref="Tick"/> only advances the rotor, the travelling flow ticks, the
/// progress bar and the controller focus.
/// </summary>
public class ProcessingScreen
{
    /// <summary>The one authoritative recipe name (GameSession converts
    /// minerals into alloy + fuel; the list is data-driven for the day the
    /// economy grows a second recipe).</summary>
    public const string RecipeAlloyName = "合金";

    private readonly VisualElement _root;
    private readonly GameSession _session;

    private int _quantity = GameSession.ProcessBatchMin;
    private bool _quantityDirty;

    private Label _minerals;
    private Label _fuel;
    private Label _alloy;

    private Button _quantityDown;
    private Button _quantityUp;
    private Label _quantityValue;
    private Label _quantityHint;
    private VisualElement _quantityRow;

    private Button _recipeRow;
    private Label _recipeName;
    private Label _recipeFlow;

    private Button _startButton;

    private Label _fuelGain;
    private Label _alloyGain;
    private Label _outputCaption;
    private Label _statusLabel;
    private Label _percentLabel;
    private Label _refineryLevel;
    private Label _durationValue;
    private Label _coreNote;
    private VisualElement _progressFill;
    private VisualElement _corePanel;

    private RefineryCoreElement _core;
    private ProcessingFlowLine _lineIn;
    private ProcessingFlowLine _lineOut;
    private ProcessingDotStrip _activityStrip;

    private UiFocusNavigator _focus;
    private ControllerPromptBar _promptBar;
    private bool _quantityFocused;
    private int _lastDirectionX;
    private int _lastDirectionY;
    private float _nextRepeatTime;
    private bool _controllerWasEnabled;
    private int _inputReadyFrame;

    /// <summary>Raised when the player presses Back on this page. The host
    /// decides the meaning (hub: leave the screen; console: the console focus
    /// controller already owns B, so it simply never enables Back here).</summary>
    public event Action BackRequested;

    /// <summary>True while this page consumes Back (set by the host).</summary>
    public bool OwnsBack { get; set; }

    /// <summary>Selected run size in minerals (read-only for probes/tests).</summary>
    public int Quantity => _quantity;

    /// <summary>True while the quantity control owns the focus ring.</summary>
    public bool QuantityFocused => _quantityFocused;

    /// <summary>The shared controller prompt bar (probes/tests).</summary>
    public ControllerPromptBar PromptBar => _promptBar;

    /// <summary>The animated refinery core (probes/tests).</summary>
    public RefineryCoreElement Core => _core;

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

        _recipeRow = BindButton(ElementNames.ProcessingRecipeRow, FocusQuantity);
        _recipeName = BindLabel(ElementNames.ProcessingRecipeName);
        _recipeFlow = BindLabel(ElementNames.ProcessingRecipeFlow);

        _quantityRow = BindElement(ElementNames.ProcessingQuantityRow);
        _quantityValue = BindLabel(ElementNames.ProcessingQuantityValue);
        _quantityHint = BindLabel(ElementNames.ProcessingQuantityHint);
        _quantityDown = BindButton(ElementNames.ProcessingQuantityDown, () => AdjustQuantity(-1));
        _quantityUp = BindButton(ElementNames.ProcessingQuantityUp, () => AdjustQuantity(1));

        _fuelGain = BindLabel(ElementNames.ProcessingFuelGainValue);
        _alloyGain = BindLabel(ElementNames.ProcessingAlloyGainValue);
        _outputCaption = BindLabel(ElementNames.ProcessingOutputCaption);
        _statusLabel = BindLabel(ElementNames.ProcessingStatusLabel);
        _percentLabel = BindLabel(ElementNames.ProcessingProgressPercent);
        _refineryLevel = BindLabel(ElementNames.ProcessingRefineryLevel);
        _durationValue = BindLabel(ElementNames.ProcessingDurationValue);
        _coreNote = BindLabel(ElementNames.ProcessingCoreNote);
        _progressFill = BindElement(ElementNames.ProcessingProgressFill);
        _corePanel = BindElement(ElementNames.ProcessingCorePanel);

        _startButton = BindButton(ElementNames.ProcessingStartButton, ConfirmStart);

        BindVisuals();
        BindController();
        Refresh();
    }

    /// <summary>Mounts the code-drawn visuals (one instance each: a second Bind
    /// must never stack a second rotor or a second set of icons).</summary>
    private void BindVisuals()
    {
        AddSketch("ProcessingRecipeIcon", ProcessingSketchIcon.Kind.Alloy);
        AddSketch("ProcessingCurrentRecipeIcon", ProcessingSketchIcon.Kind.Alloy, true);
        AddSketch("FlowOreIcon", ProcessingSketchIcon.Kind.Ore, true);
        AddSketch("FlowFuelIcon", ProcessingSketchIcon.Kind.Fuel, true);
        AddSketch("FlowAlloyIcon", ProcessingSketchIcon.Kind.Alloy, true);
        AddSketch("InventoryOreIcon", ProcessingSketchIcon.Kind.Ore);
        AddSketch("InventoryFuelIcon", ProcessingSketchIcon.Kind.Fuel);
        AddSketch("InventoryAlloyIcon", ProcessingSketchIcon.Kind.Alloy);

        VisualElement coreHost = _root.Q<VisualElement>("RefineryCoreHost");
        if (coreHost != null && coreHost.Q<RefineryCoreElement>(RefineryCoreElement.ElementName) == null)
        {
            _core = new RefineryCoreElement();
            coreHost.Add(_core);
        }

        VisualElement stripHost = _root.Q<VisualElement>("ProcessingActivityStrip");
        if (stripHost != null && stripHost.childCount == 0)
        {
            _activityStrip = new ProcessingDotStrip();
            stripHost.Add(_activityStrip);
        }

        _lineIn = MountFlowLine("FlowLineIn", false);
        _lineOut = MountFlowLine("FlowLineOut", false);
    }

    private ProcessingFlowLine MountFlowLine(string hostName, bool vertical)
    {
        VisualElement host = _root.Q<VisualElement>(hostName);
        if (host == null)
        {
            return null;
        }

        ProcessingFlowLine existing = host.Q<ProcessingFlowLine>();
        if (existing != null)
        {
            return existing;
        }

        var line = new ProcessingFlowLine(vertical);
        host.Add(line);
        return line;
    }

    private void AddSketch(string hostName, ProcessingSketchIcon.Kind kind, bool hero = false)
    {
        VisualElement host = _root.Q<VisualElement>(hostName);
        if (host == null)
        {
            return;
        }

        if (host.Q<ProcessingSketchIcon>(null) != null)
        {
            return;
        }

        host.Add(new ProcessingSketchIcon(kind, hero));
    }

    private void BindController()
    {
        _focus = new UiFocusNavigator();
        _focus.SetItems(new VisualElement[] { _recipeRow, _quantityRow });
        _focus.Register(_recipeRow);
        _focus.Register(_quantityRow);

        VisualElement promptHost = _root.Q<VisualElement>(ElementNames.ProcessingPromptHost);
        _promptBar = ControllerPromptBar.Attach(promptHost, UiFontProvider.CreateCjkFont());
        if (_promptBar != null)
        {
            _promptBar.SetRows(IdlePromptRows());
            _promptBar.SetVisible(false);
        }
    }

    private Button BindButton(string name, Action onClick)
    {
        Button button = _root.Q<Button>(name);
        if (button == null)
        {
            Debug.LogError("ProcessingScreen: missing button " + name);
            return null;
        }

        button.clicked += onClick;
        return button;
    }

    // ---- controller ----

    /// <summary>
    /// Per-frame controller input plus the visible animation. The host calls it
    /// only while the page is on screen; <paramref name="controllerEnabled"/> is
    /// false for a physical terminal that is not focused, so an unfocused page
    /// can never answer a key press meant for the player's hands.
    /// </summary>
    public void Tick(bool visible, bool controllerEnabled)
    {
        if (!visible)
        {
            if (_promptBar != null)
            {
                _promptBar.SetVisible(false);
            }

            return;
        }

        TickAnimation(Time.unscaledDeltaTime);
        TickProgress();

        if (_promptBar != null)
        {
            _promptBar.SetVisible(controllerEnabled);
            if (controllerEnabled)
            {
                _promptBar.Tick();
            }
        }

        if (!controllerEnabled)
        {
            _controllerWasEnabled = false;
            return;
        }

        if (!_controllerWasEnabled)
        {
            // Input just became ours. On a physical terminal the very same A
            // press that focused the console also reaches this frame, and a run
            // must never start from the key that opened the page: the first two
            // frames only observe.
            _controllerWasEnabled = true;
            _inputReadyFrame = Time.frameCount + 2;
        }

        if (Time.frameCount < _inputReadyFrame)
        {
            return;
        }

        // This page owns its prompt, so it also samples which device the player
        // is holding: the Hub has no first-person controller doing it for us.
        InputDeviceTracker.Poll();

        // Default focus: layout resolves one frame after the page is shown, so
        // the ring is (re)established here instead of guessing in Bind().
        if (_focus != null && !_session.IsProcessing && _focus.Focused == null)
        {
            _focus.FocusDefault();
        }

        TickNavigation();

        if (UiInput.ConfirmPressed())
        {
            ConfirmStart();
        }

        if (OwnsBack && UiInput.BackPressed())
        {
            UiInput.ConsumeBackForFrame();
            BackRequested?.Invoke();
        }
    }

    private void TickNavigation()
    {
        Vector2 navigation = UiInput.NavigationVector();
        int x = Mathf.RoundToInt(navigation.x);
        int y = Mathf.RoundToInt(navigation.y);

        if (x == 0 && y == 0)
        {
            _lastDirectionX = 0;
            _lastDirectionY = 0;
            return;
        }

        float now = Time.unscaledTime;
        if (x != _lastDirectionX || y != _lastDirectionY)
        {
            _lastDirectionX = x;
            _lastDirectionY = y;
            _nextRepeatTime = now + UiFocusNavigator.RepeatDelay;
            ApplyDirection(x, y);
            return;
        }

        if (now >= _nextRepeatTime)
        {
            _nextRepeatTime = now + UiFocusNavigator.RepeatRate;
            ApplyDirection(x, y);
        }
    }

    /// <summary>
    /// Semantic directions: +y is UP (see UiInput.NavigationVector).
    /// 加工列表 sits above 加工数量, so down enters the quantity control, up
    /// leaves it, and left / right adjust the amount. Left / right pressed ON
    /// the list both enter the control and step it: a first press that only
    /// moved the ring read as "the pad does nothing", and the horizontal axis
    /// belongs to the quantity control alone.
    /// </summary>
    private void ApplyDirection(int x, int y)
    {
        if (_session.IsProcessing)
        {
            // A running batch locks the parameters that would corrupt it.
            return;
        }

        if (_quantityFocused)
        {
            if (x != 0)
            {
                AdjustQuantity(x);
                return;
            }

            if (y > 0)
            {
                SetQuantityFocus(false);
            }

            return;
        }

        if (x != 0)
        {
            SetQuantityFocus(true);
            AdjustQuantity(x);
            return;
        }

        if (y < 0)
        {
            SetQuantityFocus(true);
        }
    }

    private void FocusQuantity()
    {
        if (!_session.IsProcessing)
        {
            SetQuantityFocus(true);
        }
    }

    private void SetQuantityFocus(bool focused)
    {
        _quantityFocused = focused;
        _focus?.SetFocus(focused ? _quantityRow : _recipeRow);
        RefreshQuantity();
        RefreshPrompt();
    }

    private void AdjustQuantity(int direction)
    {
        if (_session.IsProcessing)
        {
            return;
        }

        int max = _session.MaxSelectableBatch;
        int next = Mathf.Clamp(_quantity + direction, GameSession.ProcessBatchMin, max);
        if (next == _quantity)
        {
            return;
        }

        _quantity = next;
        // The player moved away from the run the session is still showing, so
        // the page previews the NEW run instead of continuing to report the old
        // one's settled output.
        _quantityDirty = true;
        RefreshRunState();
    }

    private void ConfirmStart()
    {
        // A click that only exists because of the press that opened this console
        // (UI Toolkit turns a pad confirm into a synthetic pointer click) lands on
        // the page before the player ever sees it, and the run must not start by
        // itself: the same opening-press grace the pad path uses.
        if (Time.frameCount < _inputReadyFrame)
        {
            return;
        }

        if (!_session.CanStartBatch(_quantity))
        {
            return;
        }

        // The session is the only authority: a second press while the run is
        // live, or a run the stock cannot pay for, is rejected there as well.
        _session.TryStartProcess(_quantity);
    }

    /// <summary>Prompt rows follow the operable set, never the layout: an
    /// unavailable key is simply not listed.</summary>
    private void RefreshPrompt()
    {
        if (_promptBar == null)
        {
            return;
        }

        if (_session.IsProcessing)
        {
            _promptBar.SetRows(ControllerPromptBar.BackOnlyRows());
            return;
        }

        var rows = new System.Collections.Generic.List<ControllerPromptRow>
        {
            ControllerPromptRow.Key(_quantityFocused ? "数量" : "选择", UiAction.Navigate),
            ControllerPromptRow.Key("返回", UiAction.Back)
        };

        if (_session.CanStartBatch(_quantity))
        {
            rows.Insert(1, ControllerPromptRow.Key("确认加工", UiAction.Confirm));
        }

        _promptBar.SetRows(rows.ToArray());
    }

    private static ControllerPromptRow[] IdlePromptRows()
    {
        return new[]
        {
            ControllerPromptRow.Key("选择", UiAction.Navigate),
            ControllerPromptRow.Key("确认加工", UiAction.Confirm),
            ControllerPromptRow.Key("返回", UiAction.Back)
        };
    }

    // ---- session-driven refresh ----

    public void Refresh()
    {
        SetText(_minerals, _session.Minerals);
        SetText(_fuel, _session.Fuel);
        SetText(_alloy, _session.Alloy);

        // The quantity control mirrors whatever run the session is showing, no
        // matter who started it (this page, a probe or the smoke): the page must
        // never report a settled result the control contradicts.
        if (_session.ProcessState != GameSession.RefineryProcessState.Idle
            && _session.ActiveBatchMinerals > 0)
        {
            _quantity = _session.ActiveBatchMinerals;
            _quantityDirty = false;
        }

        _quantity = Mathf.Clamp(_quantity, GameSession.ProcessBatchMin, _session.MaxSelectableBatch);

        if (_refineryLevel != null)
        {
            _refineryLevel.text = "LV." + _session.RefineryLevel;
        }

        if (_durationValue != null)
        {
            _durationValue.text = _session.RefineryProcessSeconds.ToString("0.0") + "s";
        }

        if (_recipeName != null)
        {
            _recipeName.text = RecipeAlloyName;
        }

        if (_recipeFlow != null)
        {
            _recipeFlow.text = "矿物 → " + RecipeAlloyName;
        }

        RefreshControllerAvailability();
        RefreshRunState();
        RefreshAnimation();
    }

    /// <summary>Everything that depends on the selected quantity and the run
    /// state: the quantity control, the output row, the progress read-out, the
    /// confirm action and the prompt rows.</summary>
    private void RefreshRunState()
    {
        RefreshQuantity();
        RefreshOutput();
        RefreshProgress();
        RefreshStartButton();
        RefreshAnimation();
        RefreshPrompt();
    }

    /// <summary>A running batch is the one state that locks the page's
    /// parameters: the recipe row and the quantity control leave the focus set
    /// entirely rather than silently ignoring input.</summary>
    private void RefreshControllerAvailability()
    {
        bool locked = _session.IsProcessing;
        _recipeRow?.SetEnabled(!locked);
        _quantityRow?.SetEnabled(!locked);

        if (locked)
        {
            _quantityFocused = false;
        }

        _focus?.SetItems(new VisualElement[] { _recipeRow, _quantityRow });
        if (!locked && _focus != null && _focus.Focused == null)
        {
            _focus.SetFocus(_recipeRow);
        }
        else if (locked)
        {
            _focus?.SetFocus(null);
        }
    }

    /// <summary>Lightweight per-frame progress refresh while the screen is
    /// visible and a process is running (cached references only).</summary>
    private void TickProgress()
    {
        if (!_session.IsProcessing)
        {
            return;
        }

        if (_progressFill != null)
        {
            _progressFill.style.width = Length.Percent(_session.ProcessProgress01 * 100f);
        }

        if (_percentLabel != null)
        {
            _percentLabel.text = Mathf.RoundToInt(_session.ProcessProgress01 * 100f) + "%";
        }

        _core?.SetState(RefineryCoreElement.CoreState.Running, _session.ProcessProgress01);
    }

    private void TickAnimation(float deltaTime)
    {
        _core?.Tick(deltaTime);
        _lineIn?.Tick(deltaTime);
        _lineOut?.Tick(deltaTime);
    }

    private void RefreshQuantity()
    {
        if (_quantityValue != null)
        {
            _quantityValue.text = _quantity.ToString("00");
        }

        if (_quantityHint != null)
        {
            _quantityHint.text = _session.Minerals <= 0
                ? "库存不足 · 无法加工"
                : "上限 " + Mathf.Min(GameSession.ProcessBatchMax, _session.Minerals)
                  + " · 库存 " + _session.Minerals;
        }

        bool locked = _session.IsProcessing;
        int max = _session.MaxSelectableBatch;

        // The arrows are the page's own statement of what is still possible.
        _quantityDown?.SetEnabled(!locked && _quantity > GameSession.ProcessBatchMin);
        _quantityUp?.SetEnabled(!locked && _quantity < max);

        if (_quantityRow != null)
        {
            _quantityRow.EnableInClassList("quantity-disabled", locked || _session.Minerals <= 0);
            _quantityRow.EnableInClassList("ui-focus", _quantityFocused && !locked);
        }
    }

    /// <summary>True while the page reports the LAST RUN's settled result: the
    /// session finished a run and the quantity control still points at it (the
    /// player has not started previewing a different run yet).</summary>
    private bool SettledResultVisible => !_session.IsProcessing
                                          && _session.ProcessState == GameSession.RefineryProcessState.Complete
                                          && _session.ActiveBatchMinerals == _quantity
                                          && !_quantityDirty;

    private void RefreshOutput()
    {
        bool showingResult = SettledResultVisible;
        bool noStock = !_session.IsProcessing && !showingResult && _session.Minerals <= 0;

        if (_outputCaption != null)
        {
            _outputCaption.text = showingResult ? "本次产出" : "预计产出";
        }

        if (noStock)
        {
            // Nothing can be produced from an empty hopper: the row reports the
            // missing input instead of a preview the player cannot start.
            SetGainText(_fuelGain, null);
            SetGainText(_alloyGain, null);
            return;
        }

        int fuel = showingResult ? _session.PendingFuel : GameSession.FuelGainFor(_quantity);
        int alloy = showingResult ? _session.PendingAlloy : GameSession.AlloyGainFor(_quantity);

        SetGainText(_fuelGain, fuel);
        SetGainText(_alloyGain, alloy);
    }

    private static void SetGainText(Label label, int? amount)
    {
        if (label != null)
        {
            label.text = amount.HasValue ? "+" + amount.Value : "—";
        }
    }

    private void RefreshProgress()
    {
        bool running = _session.IsProcessing;
        bool settledVisible = SettledResultVisible;

        if (_statusLabel != null)
        {
            bool noStock = !running && !settledVisible && _session.Minerals <= 0;
            _statusLabel.text = running
                ? "加工中"
                : (settledVisible ? "加工完成" : (noStock ? "原料不足" : "准备就绪"));
        }

        if (_percentLabel != null)
        {
            _percentLabel.text = running
                ? Mathf.RoundToInt(_session.ProcessProgress01 * 100f) + "%"
                : (settledVisible ? "100%" : "—");
        }

        if (_progressFill != null)
        {
            float percent = running
                ? _session.ProcessProgress01 * 100f
                : (settledVisible ? 100f : 0f);
            _progressFill.style.width = Length.Percent(percent);
        }

        // Refinery note: which run the core is showing (selection when idle,
        // the live run while processing, the settled result when complete).
        if (_coreNote != null)
        {
            int run = running || _session.ProcessState == GameSession.RefineryProcessState.Complete
                ? _session.ActiveBatchMinerals
                : _quantity;
            string note = "待机 · 矿物 ×" + run;
            if (running)
            {
                note = "加工中 · 矿物 ×" + run;
            }
            else if (settledVisible)
            {
                note = "完成 · 矿物 ×" + run;
            }

            _coreNote.text = note;
        }

        // Distinct stage emphasis: running lights the module + progress; a
        // completed run highlights the settled result row.
        if (_corePanel != null)
        {
            _corePanel.EnableInClassList("stage-running", running);
            _corePanel.EnableInClassList("stage-complete", settledVisible);
        }
    }

    private void RefreshStartButton()
    {
        if (_startButton == null)
        {
            return;
        }

        if (_session.IsProcessing)
        {
            _startButton.text = "加工中";
            _startButton.SetEnabled(false);
            return;
        }

        if (!_session.CanStartBatch(_quantity))
        {
            _startButton.text = "原料不足";
            _startButton.SetEnabled(false);
            return;
        }

        _startButton.text = "确认加工";
        _startButton.SetEnabled(true);
    }

    /// <summary>Pushes the session state into the animated visuals. Idle keeps
    /// everything still (no per-frame cost), a run turns the rotor and sends the
    /// flow ticks travelling, completion keeps the heat for a short flash.</summary>
    private void RefreshAnimation()
    {
        bool running = _session.IsProcessing;
        bool complete = SettledResultVisible;

        RefineryCoreElement.CoreState state = running
            ? RefineryCoreElement.CoreState.Running
            : (complete ? RefineryCoreElement.CoreState.Complete : RefineryCoreElement.CoreState.Idle);

        _core?.SetState(state, _session.ProcessProgress01);
        _lineIn?.SetActive(running);
        _lineOut?.SetActive(running);
        _activityStrip?.SetActive(running);
    }

    private Label BindLabel(string name)
    {
        Label label = _root.Q<Label>(name);
        if (label == null)
        {
            Debug.LogError("ProcessingScreen: missing label " + name);
        }

        return label;
    }

    private VisualElement BindElement(string name)
    {
        VisualElement element = _root.Q<VisualElement>(name);
        if (element == null)
        {
            Debug.LogError("ProcessingScreen: missing element " + name);
        }

        return element;
    }

    private static void SetText(Label label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
        }
    }
}
