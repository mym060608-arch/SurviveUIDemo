using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 09B PASS 1 - the shared controller interaction foundation.
///
/// The rows here drive REAL queued gamepad state (the same route a physical Xbox
/// pad takes) through the shared pieces every page will build on:
///
///   * the platform-neutral semantic actions (Confirm / Back / ContextAction /
///     Wrist / Pause / PreviousTab / NextTab / MapCursor / ZoomIn / ZoomOut),
///   * the device-dependent prompt glyphs (Xbox / PlayStation-compatible pad /
///     mouse+keyboard) and the dynamic prompt bar that never offers a dead key,
///   * the shared focus navigator (default focus, disabled/hidden skipping, wrap,
///     Confirm activation, LB/RB tab groups, Back consumed by layer).
///
/// The fixture page is built inside the REAL surface HUD document, so the focus
/// ring and the prompt rows run against a live UI Toolkit panel with the project's
/// own styles instead of a panel invented for the test.
/// </summary>
public class SurvivePhase09BControllerTests
{
    private Gamepad _pad;
    private Keyboard _keyboard;
    private VisualElement _fixture;
    private Button[] _rows;
    private UiFocusNavigator _focus;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        InputDeviceTracker.ResetToDefaults();
        UiInput.ResetLayersForAutomation();
        UiPointerState.ResetForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }

        _pad = InputSystem.AddDevice<Gamepad>();
        _keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.EnableDevice(_keyboard);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        if (_fixture != null)
        {
            _fixture.RemoveFromHierarchy();
            _fixture = null;
        }

        _focus = null;
        _rows = null;

        if (_keyboard != null && _keyboard.added)
        {
            InputSystem.RemoveDevice(_keyboard);
        }

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _keyboard = null;
        _pad = null;
        UiInput.ResetLayersForAutomation();
        UiPointerState.ResetForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // 1. Semantics: the logical layer names actions, never controls
    // ====================================================================

    [UnityTest]
    public IEnumerator Semantics_ArePlatformNeutral_AndPinnedAsData()
    {
        Assert.Contains("Confirm=Gamepad.buttonSouth,Keyboard.eKey|enterKey|spaceKey",
            UiInput.BindingTable, "Confirm stays the south face control");
        Assert.Contains("Back=Gamepad.buttonEast,Keyboard.escapeKey", UiInput.BindingTable,
            "Back stays the east face control / Escape");
        Assert.Contains("Pause=Gamepad.startButton,Keyboard.escapeKey", UiInput.BindingTable,
            "Pause stays Menu/Start");
        Assert.Contains("Wrist=Gamepad.buttonNorth,Keyboard.mKey|iKey", UiInput.BindingTable,
            "Phase 09A: Y / buttonNorth is still the wrist");
        Assert.Contains("Map=Gamepad.buttonWest,Keyboard.mKey|iKey", UiInput.BindingTable,
            "Phase 09A: X / buttonWest is still the surface map");

        // ---- Phase 09B rows ----
        Assert.Contains("ContextAction=Gamepad.buttonWest,Keyboard.mKey|iKey",
            UiInput.BindingTable, "the page context action is the same west control");
        Assert.Contains("PreviousTab=Gamepad.leftShoulder,Keyboard.qKey",
            UiInput.BindingTable, "LB is PreviousTab");
        Assert.Contains("NextTab=Gamepad.rightShoulder,Keyboard.tabKey",
            UiInput.BindingTable, "RB is NextTab");
        Assert.Contains("MapCursor=Gamepad.rightStick,Mouse.position",
            UiInput.BindingTable, "the map cursor is the right stick (and the mouse)");
        Assert.Contains("ZoomIn=Gamepad.rightTrigger,Keyboard.pageUpKey|Mouse.wheelUp",
            UiInput.BindingTable, "RT is ZoomIn");
        Assert.Contains("ZoomOut=Gamepad.leftTrigger,Keyboard.pageDownKey|Mouse.wheelDown",
            UiInput.BindingTable, "LT is ZoomOut");

        foreach (string row in UiInput.BindingTable)
        {
            Assert.IsFalse(row.Contains("AButton") || row.Contains("BButton")
                           || row.Contains("XButton") || row.Contains("YButton")
                           || row.Contains("PlayStation") || row.Contains("StartButton"),
                "no logical row may name a physical control (" + row + ")");
        }

        // ---- the dead zone / axis semantics the pages rely on ----
        Assert.AreEqual(Vector2.zero, UiInput.MapCursorVector(),
            "a centred right stick is not cursor movement");
        Assert.AreEqual(0f, UiInput.ZoomAxis(), "a released trigger pair is not zoom");
        Assert.AreEqual(0, UiInput.TabDirection(), "released shoulders are not a tab press");

        // ---- the surface page reads the west control through the SAME path ----
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.West));
        bool contextActionSeen = false;
        bool pageNameAgrees = false;
        for (int i = 0; i < 4 && !contextActionSeen; i++)
        {
            yield return null;
            if (UiInput.ContextActionPressed())
            {
                contextActionSeen = true;
                pageNameAgrees = UiInput.MapPressed();
            }
        }

        Assert.IsTrue(contextActionSeen,
            "the west control is the page ContextAction semantic");
        Assert.IsTrue(pageNameAgrees,
            "Map is the surface page's ContextAction name: one read path, two names");

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    // ====================================================================
    // 2. Prompt glyphs follow the device, not the logic
    // ====================================================================

    [Test]
    public void PromptGlyphs_SwitchBetweenDevices_WithoutChangingActions()
    {
        // ---- mouse + keyboard ----
        InputDeviceTracker.NotifyKeyboardMouse();
        Assert.AreEqual("E", InputDeviceTracker.PromptGlyph(UiAction.Confirm));
        Assert.AreEqual("Esc", InputDeviceTracker.PromptGlyph(UiAction.Back));
        Assert.AreEqual("Q", InputDeviceTracker.PromptGlyph(UiAction.PreviousTab));
        Assert.AreEqual("Tab", InputDeviceTracker.PromptGlyph(UiAction.NextTab));
        Assert.AreEqual("鼠标", InputDeviceTracker.PromptGlyph(UiAction.MapCursor));
        Assert.AreEqual("滚轮", InputDeviceTracker.PromptGlyph(UiAction.ZoomIn));

        // ---- the virtual pad (generic family: the same A/B/X/Y shell wording) ----
        InputDeviceTracker.NotifyGamepad();
        Assert.AreEqual(InputDeviceTracker.GamepadFamily.Generic,
            InputDeviceTracker.FamilyOf(_pad),
            "a synthetic pad is the generic family");
        Assert.AreEqual("A", InputDeviceTracker.PromptGlyph(UiAction.Confirm));
        Assert.AreEqual("B", InputDeviceTracker.PromptGlyph(UiAction.Back));
        Assert.AreEqual("X", InputDeviceTracker.PromptGlyph(UiAction.ContextAction));
        Assert.AreEqual("Y", InputDeviceTracker.PromptGlyph(UiAction.Wrist));
        Assert.AreEqual("LB", InputDeviceTracker.PromptGlyph(UiAction.PreviousTab));
        Assert.AreEqual("RB", InputDeviceTracker.PromptGlyph(UiAction.NextTab));
        Assert.AreEqual("RS", InputDeviceTracker.PromptGlyph(UiAction.MapCursor));
        Assert.AreEqual("LT", InputDeviceTracker.PromptGlyph(UiAction.ZoomOut));
        Assert.AreEqual("RT", InputDeviceTracker.PromptGlyph(UiAction.ZoomIn));

        // ---- family classification is name driven (pure, no device needed) ----
        Assert.AreEqual(InputDeviceTracker.GamepadFamily.PlayStation,
            InputDeviceTracker.FamilyOf("DualSense Wireless Controller"));
        Assert.AreEqual(InputDeviceTracker.GamepadFamily.PlayStation,
            InputDeviceTracker.FamilyOf("DualShock 4"));
        Assert.AreEqual(InputDeviceTracker.GamepadFamily.Xbox,
            InputDeviceTracker.FamilyOf("XInputControllerWindows"));
        Assert.AreEqual(InputDeviceTracker.GamepadFamily.Xbox,
            InputDeviceTracker.FamilyOf("Xbox Wireless Controller"));

        // ---- a PlayStation-compatible pad: ONLY the glyphs change ----
        InputSystem.RemoveDevice(_pad);
        _pad = (Gamepad)InputSystem.AddDevice("Gamepad", "DualSense Wireless Controller");
        Assert.AreSame(_pad, Gamepad.current, "the PlayStation-compatible pad is current");
        Assert.AreEqual(InputDeviceTracker.GamepadFamily.PlayStation,
            InputDeviceTracker.CurrentFamily);

        Assert.AreEqual("✕", InputDeviceTracker.PromptGlyph(UiAction.Confirm));
        Assert.AreEqual("○", InputDeviceTracker.PromptGlyph(UiAction.Back));
        Assert.AreEqual("□", InputDeviceTracker.PromptGlyph(UiAction.ContextAction));
        Assert.AreEqual("△", InputDeviceTracker.PromptGlyph(UiAction.Wrist));
        Assert.AreEqual("L1", InputDeviceTracker.PromptGlyph(UiAction.PreviousTab));
        Assert.AreEqual("R1", InputDeviceTracker.PromptGlyph(UiAction.NextTab));
        Assert.AreEqual("L2", InputDeviceTracker.PromptGlyph(UiAction.ZoomOut));
        Assert.AreEqual("R2", InputDeviceTracker.PromptGlyph(UiAction.ZoomIn));
    }

    // ====================================================================
    // 3. The dynamic prompt bar
    // ====================================================================

    [UnityTest]
    public IEnumerator PromptBar_ShowsOnlyWhatThePageCanOffer_Green()
    {
        SurfaceHudController hud = null;
        yield return EnterSurface(value => hud = value.Hud);

        Assert.IsNotNull(hud, "the surface HUD exists");
        Assert.IsNotNull(hud.PromptBar, "the page owns a shared prompt bar");
        Assert.IsFalse(hud.PromptBar.IsShown,
            "no prompt is offered while the map is closed (the world layer names its own key)");
        Assert.AreSame(hud.PromptBar, ControllerPromptBar.Attach(hud.UiRoot, null),
            "attaching twice returns the page's own bar (never a second prompt row)");

        // A fixture bar on its own host, so the assertions are independent of the
        // page's per-frame driving of its own bar.
        _fixture = new VisualElement { name = "Phase09BPromptFixture" };
        _fixture.style.position = Position.Absolute;
        hud.UiRoot.Add(_fixture);
        ControllerPromptBar bar = ControllerPromptBar.Attach(_fixture);
        Assert.IsNotNull(bar, "a bar can be attached to any page host");

        // ---- the map row set on a pad ----
        InputDeviceTracker.NotifyGamepad();
        bar.SetRows(ControllerPromptBar.SurfaceMapRows());
        bar.SetVisible(true);
        bar.Tick();
        yield return null;

        Assert.AreEqual("[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回", bar.PromptText,
            "the map prompt names the pad's own controls");
        Assert.IsTrue(bar.IsShown, "the bar is drawn while the page asks for it");
        Assert.AreEqual(4, bar.VisibleRowCount, "every map row has a function");
        Assert.IsFalse(bar.PromptText.Contains("LB") || bar.PromptText.Contains("RB"),
            "the surface map has no tabs, so LB/RB are never offered");

        // ---- the same rows for mouse + keyboard: glyphs change, actions do not ----
        InputDeviceTracker.NotifyKeyboardMouse();
        bar.Tick();
        yield return null;
        Assert.AreEqual("[鼠标] 光标   [E] 选择   [滚轮/滚轮] 缩放   [Esc] 返回", bar.PromptText,
            "the same rows are re-glyphed for mouse+keyboard");

        // ---- a PlayStation-compatible pad ----
        InputSystem.RemoveDevice(_pad);
        _pad = (Gamepad)InputSystem.AddDevice("Gamepad", "DualSense Wireless Controller");
        InputDeviceTracker.NotifyGamepad();
        bar.Tick();
        yield return null;
        Assert.AreEqual("[RS] 光标   [✕] 选择   [L2/R2] 缩放   [○] 返回", bar.PromptText,
            "the same rows are re-glyphed for a PlayStation-compatible pad");

        // ---- a row with no function here is dropped, never shown dead ----
        bar.SetRows(ControllerPromptRow.Key("确认", UiAction.Confirm),
            ControllerPromptRow.Key("无功能", new UiAction[0]));
        yield return null;
        Assert.AreEqual(1, bar.VisibleRowCount, "a row with no action is not offered");
        Assert.AreEqual("[✕] 确认", bar.PromptText);

        // ---- an empty bar leaves no background pill behind ----
        bar.SetRows(new ControllerPromptRow[0]);
        yield return null;
        Assert.IsFalse(bar.IsShown, "an empty prompt bar is not drawn");

        bar.SetVisible(false);
        Assert.IsFalse(bar.IsShown, "hidden stays hidden");
    }

    // ====================================================================
    // 4. Shared focus: default, skipping, wrap, no dead ends
    // ====================================================================

    [UnityTest]
    public IEnumerator FocusNavigator_Default_SkipsDisabledAndHidden_AndWraps_Green()
    {
        yield return BuildFocusFixture();

        Assert.AreEqual(4, _focus.Count, "four rows are registered");
        Assert.IsTrue(_focus.FocusDefault(), "opening the page gives a default focus");
        Assert.AreSame(_rows[0], _focus.Focused, "the default focus is the first row");
        Assert.IsTrue(_rows[0].ClassListContains(UiFocusNavigator.DefaultFocusClass),
            "the focus ring is the navigator's own class");

        // A disabled row can never be a focus target.
        _rows[1].SetEnabled(false);
        yield return null;
        _focus.Move(new Vector2(0f, -1f));
        Assert.AreSame(_rows[2], _focus.Focused, "the disabled row is skipped");

        // Neither can a hidden one: hiding the FOCUSED row heals the focus.
        _rows[2].style.display = DisplayStyle.None;
        yield return null;
        _focus.Move(new Vector2(0f, -1f));
        Assert.AreSame(_rows[0], _focus.Focused,
            "a row that became hidden cannot keep the focus");

        // Down again skips both the disabled and the hidden row.
        _focus.Move(new Vector2(0f, -1f));
        Assert.AreSame(_rows[3], _focus.Focused, "disabled AND hidden rows are skipped");

        // No dead ends: the focus wraps instead of stopping at the edge.
        _focus.Move(new Vector2(0f, -1f));
        Assert.AreSame(_rows[0], _focus.Focused, "down at the end wraps to the top");
        _focus.Move(new Vector2(0f, 1f));
        Assert.AreSame(_rows[3], _focus.Focused, "up at the top wraps to the bottom");
    }

    [UnityTest]
    public IEnumerator Gamepad_StickAndDpad_MoveTheFocus_Green()
    {
        yield return BuildFocusFixture();

        Assert.IsTrue(_focus.FocusDefault());

        // A real D-Pad press moves the focus exactly one row.
        yield return PressButton(GamepadButton.DpadDown);
        Assert.AreSame(_rows[1], _focus.Focused, "D-Pad down moves to the next row");

        // A real left-stick push moves it exactly one row as well.
        yield return PushStick(new Vector2(0f, -1f));
        Assert.AreSame(_rows[2], _focus.Focused, "the left stick moves the focus");

        yield return PushStick(new Vector2(0f, 1f));
        Assert.AreSame(_rows[1], _focus.Focused, "the left stick moves back up");

        yield return PressButton(GamepadButton.DpadUp);
        Assert.AreSame(_rows[0], _focus.Focused, "D-Pad up moves back to the first row");
    }

    [UnityTest]
    public IEnumerator Gamepad_Confirm_ActivatesExactlyOneAction_Green()
    {
        yield return BuildFocusFixture();

        int activated = 0;
        _focus.Register(_rows[0], () => activated++);
        _focus.Register(_rows[1], () => activated += 10);
        Assert.IsTrue(_focus.FocusDefault());

        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(1, activated, "A activates the focused row exactly once");

        _focus.Move(new Vector2(0f, -1f));
        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(11, activated, "A activates the newly focused row");
    }

    [UnityTest]
    public IEnumerator FocusNavigator_Confirm_FallsBackToARealClick_Green()
    {
        yield return BuildFocusFixture();

        int clicked = -1;
        _rows[1].clicked += () => clicked = 1;
        Assert.IsTrue(_focus.FocusDefault());
        _focus.Move(new Vector2(0f, -1f));
        Assert.AreSame(_rows[1], _focus.Focused);

        // No action was registered for this row: the navigator dispatches a real
        // UI Toolkit click at its centre, so a page keeps its normal handlers.
        Assert.IsTrue(_focus.Confirm(), "Confirm reached the row");
        yield return null;
        yield return null;
        Assert.AreEqual(1, clicked, "the row's own click handler ran");
    }

    [UnityTest]
    public IEnumerator Gamepad_Shoulders_SwitchTabGroups_Green()
    {
        yield return BuildFocusFixture();

        _focus.AddTab("left", new VisualElement[] { _rows[0], _rows[3] });
        _focus.AddTab("right", new VisualElement[] { _rows[1], _rows[2] });
        Assert.IsTrue(_focus.HasTabs, "two groups are registered");
        Assert.AreEqual("left", _focus.ActiveTab, "the first group starts active");
        Assert.IsTrue(_focus.FocusDefault());
        Assert.AreSame(_rows[0], _focus.Focused);

        yield return PressButton(GamepadButton.RightShoulder);
        Assert.AreEqual("right", _focus.ActiveTab, "RB switches to the next group");
        Assert.AreSame(_rows[1], _focus.Focused, "RB enters the group at its first row");

        yield return PressButton(GamepadButton.LeftShoulder);
        Assert.AreEqual("left", _focus.ActiveTab, "LB switches back");
        Assert.AreSame(_rows[3], _focus.Focused, "LB enters the group at its last row");
    }

    [UnityTest]
    public IEnumerator FocusNavigator_ConsumesBackOnlyForItsOwnLayer_Green()
    {
        yield return BuildFocusFixture();

        _focus.OwnerLayer = UiInput.UiLayer.Modal;
        UiInput.SetLayerActive(UiInput.UiLayer.Modal, true);
        bool consumed = false;
        bool consumedFlag = false;
        bool pauseStole = true;
        try
        {
            UiInput.InjectEscapeForAutomation();
            for (int i = 0; i < 4 && !consumed; i++)
            {
                yield return null;
                consumed = _focus.ConsumeBack();
                if (consumed)
                {
                    // Read the same frame the press was handled.
                    consumedFlag = UiInput.BackConsumedThisFrame;
                    pauseStole = UiInput.PausePressed();
                }
            }
        }
        finally
        {
            UiInput.SetLayerActive(UiInput.UiLayer.Modal, false);
        }

        Assert.IsTrue(consumed, "the page owns Back while it is the top local layer");
        Assert.IsTrue(consumedFlag,
            "the navigator consumed the press: it cannot leak into the global Pause");
        Assert.IsFalse(pauseStole,
            "a Back that a page handled is never also the pause");

        // A higher layer owns Back: this page must not react to it at all.
        UiInput.SetLayerActive(UiInput.UiLayer.PauseMenu, true);
        bool stolen = false;
        bool stolenFlag = false;
        try
        {
            UiInput.InjectEscapeForAutomation();
            for (int i = 0; i < 4 && !stolen; i++)
            {
                yield return null;
                stolen = _focus.ConsumeBack();
                stolenFlag |= UiInput.BackConsumedThisFrame;
            }
        }
        finally
        {
            UiInput.SetLayerActive(UiInput.UiLayer.PauseMenu, false);
        }

        Assert.IsFalse(stolen, "Back belongs to the pause above this page");
        Assert.IsFalse(stolenFlag,
            "the page never consumed a Back press that belongs to a higher layer");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    /// <summary>
    /// Builds a real four-row page inside the surface HUD document and attaches the
    /// shared navigator. The world interactor is switched off, because it shares the
    /// Confirm control and this suite isolates the UI layer.
    /// </summary>
    private IEnumerator BuildFocusFixture()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        Assert.IsNotNull(surface, "the surface is ready");
        if (surface.Interactor != null)
        {
            surface.Interactor.enabled = false;
        }

        _fixture = new VisualElement { name = "Phase09BFocusFixture" };
        _fixture.style.position = Position.Absolute;
        _fixture.style.left = 240f;
        _fixture.style.top = 200f;
        _fixture.style.flexDirection = FlexDirection.Column;
        surface.Hud.UiRoot.Add(_fixture);

        _rows = new Button[4];
        for (int i = 0; i < _rows.Length; i++)
        {
            var button = new Button { text = "行 " + i, name = "FocusRow" + i };
            button.style.width = 180f;
            button.style.height = 34f;
            button.style.marginTop = 6f;
            _rows[i] = button;
            _fixture.Add(button);
        }

        _focus = new UiFocusNavigator();
        _focus.SetItems(_rows);
        _focus.Enabled = true;

        yield return null;
        yield return null;
    }

    private IEnumerator PressButton(GamepadButton button)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return TickFrames(2);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return TickFrames(2);
    }

    private IEnumerator PushStick(Vector2 direction)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = direction * 0.9f });
        yield return TickFrames(4);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return TickFrames(2);
    }

    /// <summary>
    /// Frames as a real page would run them: the navigator's Tick runs in Update,
    /// once per frame, exactly like the page's own Update loop.
    /// </summary>
    private IEnumerator TickFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            _focus?.Tick();
            yield return null;
        }
    }

    private static IEnumerator EnterSurface(System.Action<SurfaceGameController> onSurface)
    {
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return null;

        SurfaceGameController surface = null;
        yield return WaitUntil(() =>
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            return surface != null && surface.IsReady && surface.Player != null
                   && surface.Hud != null;
        }, 25f);
        Assert.IsNotNull(surface, "the planet surface is ready");
        yield return WaitUntil(() => surface.Player.InputEnabled, 5f);
        yield return null;
        onSurface?.Invoke(surface);
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
