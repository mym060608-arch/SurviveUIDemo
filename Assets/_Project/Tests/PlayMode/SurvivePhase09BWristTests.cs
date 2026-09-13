using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 09B PASS 3 - the wrist terminal driven by a controller.
///
/// The terminal is ONE presentation with two honest interaction modes, chosen by
/// the control the player picks up:
///
///   MapCursor   right stick / mouse drives the shared cursor, A is the map's
///               left button, LT/RT zoom, B returns - the accepted PASS 2 route,
///               and the mode the surface terminal opens in;
///   ModuleFocus D-Pad / left stick walks the focus ring over the elements that
///               really ACT (equipment toggles, the navigation row while a target
///               is set, the central map), A confirms, B returns.
///
/// These rows queue REAL gamepad state, so the whole route a physical pad takes is
/// covered, and they pin the presentation half of the contract too: the prompt
/// re-glyphs per mode, the standby radar turns into a live bearing, the harvest
/// plate only flows while data really moves, and pure information rows can never
/// take focus.
/// </summary>
public class SurvivePhase09BWristTests
{
    private Gamepad _pad;
    private Keyboard _keyboard;
    private Mouse _mouse;
    private InputSettings.BackgroundBehavior _backgroundBehavior;
    private InputSettings.EditorInputBehaviorInPlayMode _editorInputBehavior;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        InputDeviceTracker.ResetToDefaults();
        UiInput.ResetLayersForAutomation();
        UiPointerState.ResetForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;

        // A batch-mode run never owns the OS foreground: the editor otherwise drops
        // queued pointer/keyboard state (the gamepad is unaffected). Restored in
        // TearDown - this is the harness, never the product.
        _backgroundBehavior = InputSystem.settings.backgroundBehavior;
        _editorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

        // The wrist reads and writes REAL session state (equipment selection,
        // navigation memory, health / satiety), so the run starts from the
        // authoritative session in its launch state - the same ResetSession the
        // world-facing suites use, never a second, test-only state store.
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        session.TryConfirmLoadout();

        _pad = InputSystem.AddDevice<Gamepad>();
        _keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.EnableDevice(_keyboard);
        _mouse = InputSystem.AddDevice<Mouse>();
        InputSystem.EnableDevice(_mouse);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        if (_mouse != null && _mouse.added)
        {
            InputSystem.RemoveDevice(_mouse);
        }

        if (_keyboard != null && _keyboard.added)
        {
            InputSystem.RemoveDevice(_keyboard);
        }

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _mouse = null;
        _keyboard = null;
        _pad = null;
        InputSystem.settings.backgroundBehavior = _backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBehavior;
        UiInput.ResetLayersForAutomation();
        UiPointerState.ResetForAutomation();
        InputDeviceTracker.ResetToDefaults();
        AppQuit.ResetBookkeepingForAutomation();
    }

    // ====================================================================
    // 1. Y opens the terminal in the accepted PASS 2 cursor mode
    // ====================================================================

    [UnityTest]
    public IEnumerator WristOpen_DefaultModeIsCursor_WithNoFocusBox_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);

        SurfaceWristController wrist = surface.Wrist;
        Assert.AreEqual(WristPadMode.MapCursor, wrist.PadMode,
            "the surface terminal opens in the accepted PASS 2 cursor mode");
        Assert.IsNull(wrist.FocusNavigator.Focused,
            "no focus box is shown in cursor mode (contract: 地图 Cursor Mode 下不要显示普通 Focus 框)");
        Assert.AreEqual(0, CountFocusClassed(wrist),
            "not a single wrist element carries the focus class in cursor mode");

        ControllerPromptBar bar = surface.Hud.PromptBar;
        Assert.AreEqual("[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回", bar.PromptText,
            "the cursor-mode prompt is the frozen PASS 2 row set");
    }

    // ====================================================================
    // 2. D-Pad / left stick walk the focus ring - over operable elements only
    // ====================================================================

    [UnityTest]
    public IEnumerator WristFocus_DPadEntersModuleFocus_AndOnlyReachesOperableElements_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;

        // The page gives a sensible default focus the moment focus mode starts.
        wrist.SetPadMode(WristPadMode.ModuleFocus);
        yield return null;
        yield return null;
        Assert.AreEqual(SurfaceWristController.MapFocusAnchorName, wrist.FocusedRowName,
            "module focus opens on the central map (the default focus)");

        // A real D-Pad press moves the ring onto a control that really acts.
        yield return PushDPad(GamepadButton.DpadLeft);
        Assert.AreEqual(WristPadMode.ModuleFocus, wrist.PadMode,
            "the D-Pad owns the terminal");
        Assert.IsTrue(IsOperableName(wrist.FocusedRowName, surface),
            "the D-Pad moved the focus onto a real control (" + wrist.FocusedRowName + ")");
        Assert.IsTrue(wrist.Radial.FocusStrengthOf(wrist.ModuleOfFocusedElement()) > 0f,
            "the focused module plate is lifted");

        // No direction can ever park the focus on a read-out or lose it.
        GamepadButton[] directions =
        {
            GamepadButton.DpadRight, GamepadButton.DpadUp, GamepadButton.DpadDown,
            GamepadButton.DpadLeft, GamepadButton.DpadDown, GamepadButton.DpadRight
        };

        foreach (GamepadButton direction in directions)
        {
            yield return PushDPad(direction);
            Assert.IsNotNull(wrist.FocusNavigator.Focused,
                "the focus never reaches a dead end (direction " + direction + ")");
            Assert.IsTrue(IsOperableName(wrist.FocusedRowName, surface),
                "the focus stays on operable elements (direction " + direction
                + " -> " + wrist.FocusedRowName + ")");
        }

        // Every read-out row is provably outside the focus list.
        List<VisualElement> items = wrist.FocusNavigator.ActiveGroup;
        string[] information =
        {
            "EquipHeading", "EquipHarvester", "EquipBattery", "EquipDrone",
            "StateHeading", "ActionState", "SignalState", "RecoveredState",
            "NavHeading", "NavDistance",
            "HarvestHeading", "HarvestNode", "HarvestState", "HarvestProgress"
        };

        VisualElement root = wrist.RootElement;
        foreach (string name in information)
        {
            Label label = root.Q<Label>(name);
            Assert.IsNotNull(label, "information row exists: " + name);
            Assert.IsFalse(items.Contains(label),
                "a pure information row can never take focus: " + name);
        }
    }

    // ====================================================================
    // 3. The equipment block is READ-ONLY and lists only equipped modules
    // ====================================================================

    [UnityTest]
    public IEnumerator WristEquipment_IsReadOnly_AndListsOnlyEquippedModules_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;
        GameSession session = GameSession.Instance;
        VisualElement root = wrist.RootElement;

        // ---- a bare loadout lists exactly ONE line: the required harvester ----
        Assert.IsFalse(session.BatterySelected && session.DroneSelected,
            "the run starts with no optional module equipped");
        Assert.AreEqual(1, wrist.ListedEquipmentNames.Length,
            "only the mission-required harvester is listed");
        Assert.AreEqual("EquipHarvester", wrist.ListedEquipmentNames[0], "and it is the harvester");
        Assert.AreEqual("采集器 MK-I", root.Q<Label>("EquipHarvester").text, "harvester named");
        Assert.AreEqual(DisplayStyle.None, root.Q<Label>("EquipBattery").resolvedStyle.display,
            "an unequipped module is not drawn at all");
        Assert.AreEqual(string.Empty, root.Q<Label>("EquipBattery").text,
            "and carries no text (no fake inventory / no muted line)");
        Assert.AreEqual(DisplayStyle.None, root.Q<Label>("EquipDrone").resolvedStyle.display,
            "same for the survey drone");

        // ---- NOTHING in the block can take focus, and A cannot change it ----
        wrist.SetPadMode(WristPadMode.ModuleFocus);
        yield return null;
        List<VisualElement> items = wrist.FocusNavigator.ActiveGroup;
        Assert.IsFalse(items.Contains(root.Q<Label>("EquipHarvester"))
                       || items.Contains(root.Q<Label>("EquipBattery"))
                       || items.Contains(root.Q<Label>("EquipDrone")),
            "no equipment row is a focus stop");
        Assert.IsFalse(wrist.FocusNavigator.SetFocus(root.Q<Label>("EquipHarvester")),
            "the equipment block refuses focus outright");

        bool batteryBefore = session.BatterySelected;
        bool droneBefore = session.DroneSelected;
        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(batteryBefore, session.BatterySelected,
            "A on the terminal never changes the loadout (battery)");
        Assert.AreEqual(droneBefore, session.DroneSelected,
            "A on the terminal never changes the loadout (drone)");

        // ---- the LIST follows the real loadout: only what is carried appears ----
        // The loadout authority is the ship's page; the wrist only reflects it.
        Assert.IsTrue(session.TrySetBatterySelected(true), "battery equipped");
        Assert.IsTrue(session.TrySetDroneSelected(true), "drone equipped");
        yield return null;
        yield return null;

        Assert.AreEqual(3, wrist.ListedEquipmentNames.Length,
            "every equipped module is listed (" + string.Join(",", wrist.ListedEquipmentNames) + ")");
        Assert.AreEqual("备用电池", root.Q<Label>("EquipBattery").text, "battery now listed");
        Assert.AreEqual("勘测无人机", root.Q<Label>("EquipDrone").text, "drone now listed");
        Assert.AreNotEqual(DisplayStyle.None,
            root.Q<Label>("EquipBattery").resolvedStyle.display, "battery row drawn");
        Assert.AreNotEqual(DisplayStyle.None,
            root.Q<Label>("EquipDrone").resolvedStyle.display, "drone row drawn");
        Assert.IsTrue(wrist.VerifyWristLayoutGeometry(out string layout),
            "the three-row equipment block still satisfies the frozen slot geometry :: "
            + layout);

        // ---- unequipping packs the list up: no hole where a module used to be ----
        Assert.IsTrue(session.TrySetBatterySelected(false), "battery unequipped");
        yield return null;
        yield return null;

        Assert.AreEqual(2, wrist.ListedEquipmentNames.Length, "two modules left");
        Assert.AreEqual("EquipHarvester", wrist.ListedEquipmentNames[0], "harvester first");
        Assert.AreEqual("EquipDrone", wrist.ListedEquipmentNames[1], "drone second");
        Assert.AreEqual(DisplayStyle.None, root.Q<Label>("EquipBattery").resolvedStyle.display,
            "the unequipped battery disappeared from the terminal");
        Assert.AreEqual(SurfaceWristController.EquipmentSlotY(0),
            root.Q<Label>("EquipHarvester").style.top.value.value
            + SurfaceWristController.RowHeight * 0.5f, 0.75f,
            "the harvester stays in the first slot");
        Assert.AreEqual(SurfaceWristController.EquipmentSlotY(1),
            root.Q<Label>("EquipDrone").style.top.value.value
            + SurfaceWristController.RowHeight * 0.5f, 0.75f,
            "and the drone moved up into the SECOND slot instead of leaving a hole");
    }

    // ====================================================================
    // 4. The navigation row is operable exactly while a target is set
    // ====================================================================

    [UnityTest]
    public IEnumerator WristFocus_NavigationRowIsOperableOnlyWhileATargetIsSet_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;
        VisualElement root = wrist.RootElement;

        // 未设置: standby read-out, no focus stop, "?" radar, static line.
        Assert.IsFalse(surface.IsNavigationActive, "no target is set yet");
        Assert.IsFalse(wrist.NavigationFocusable,
            "the navigation module is not operable while it is in standby");
        Assert.IsFalse(wrist.FocusNavigator.ActiveGroup.Contains(root.Q<Label>("NavTarget")),
            "the standby navigation row is not a focus stop");
        Assert.AreEqual("?", root.Q<Label>("NavRadarGlyph").text,
            "the standby radar shows its ? symbol");
        Assert.IsFalse(wrist.Radial.RadarActive, "the radar is in standby, not locked");

        // 已设置: the row becomes a real control and the radar locks on.
        Assert.IsTrue(surface.TrySelectNavigationTarget(1), "a target can be selected");
        yield return null;
        yield return null;

        Assert.IsTrue(wrist.NavigationFocusable,
            "with a target set the navigation module becomes operable");
        Assert.IsTrue(wrist.FocusNavigator.ActiveGroup.Contains(root.Q<Label>("NavTarget")),
            "the live navigation row is a focus stop");
        Assert.AreEqual("资源节点 02", root.Q<Label>("NavTarget").text,
            "the row shows the navigation target (set state)");
        Assert.IsTrue(wrist.Radial.RadarActive, "the radar switched to a live bearing");
        Assert.AreEqual(string.Empty, root.Q<Label>("NavRadarGlyph").text,
            "the ? symbol is gone once navigation is live");

        wrist.SetPadMode(WristPadMode.ModuleFocus);
        yield return null;
        Assert.IsTrue(wrist.FocusNavigator.SetFocus(root.Q<Label>("NavTarget")),
            "the live navigation row takes focus");
        yield return PressButton(GamepadButton.South);
        yield return null;

        Assert.AreEqual(-1, surface.NavigationTargetNodeId,
            "A on the navigation row cleared the target through the owning controller");
        Assert.IsFalse(wrist.NavigationFocusable,
            "the module fell back to standby after the target was cleared");
        Assert.AreEqual("未设置", root.Q<Label>("NavTarget").text, "the row reads 未设置 again");
        Assert.AreEqual("?", root.Q<Label>("NavRadarGlyph").text,
            "the standby radar symbol is back");
    }

    // ====================================================================
    // 5. The right stick hands the terminal back to the cursor
    // ====================================================================

    [UnityTest]
    public IEnumerator WristFocus_RightStickReturnsToCursorMode_WithoutAFocusBox_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;

        wrist.SetPadMode(WristPadMode.ModuleFocus);
        yield return null;
        Assert.IsTrue(wrist.FocusNavigator.SetFocus(
                wrist.RootElement.Q<VisualElement>(SurfaceWristController.MapFocusAnchorName)),
            "the central map takes focus in module mode (the only operable element here)");

        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(1f, 0f) });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;

        Assert.AreEqual(WristPadMode.MapCursor, wrist.PadMode,
            "the right stick hands the terminal back to the cursor");
        Assert.IsNull(wrist.FocusNavigator.Focused, "the focus box is released");
        Assert.AreEqual(0, CountFocusClassed(wrist),
            "no element keeps the focus look after leaving module focus");

        // ... and the D-Pad takes it back, with the same two-way handover rule.
        yield return PushDPad(GamepadButton.DpadDown);
        Assert.AreEqual(WristPadMode.ModuleFocus, wrist.PadMode,
            "the D-Pad takes the terminal back");
        Assert.IsNotNull(wrist.FocusNavigator.Focused, "and the ring is drawn again");
    }

    // ====================================================================
    // 6. The prompt follows the mode (and never names a dead key)
    // ====================================================================

    [UnityTest]
    public IEnumerator WristPrompt_ReGlyphsForTheActiveMode_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;
        ControllerPromptBar bar = surface.Hud.PromptBar;

        InputDeviceTracker.NotifyGamepad();
        yield return null;
        yield return null;
        Assert.AreEqual("[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回", bar.PromptText,
            "cursor mode advertises the map keys");

        yield return PushDPad(GamepadButton.DpadRight);
        Assert.AreEqual(WristPadMode.ModuleFocus, wrist.PadMode, "the D-Pad switched the mode");
        Assert.AreEqual("[D-Pad] 选择   [A] 确认   [RS] 光标   [LT/RT] 缩放   [B] 返回",
            bar.PromptText,
            "focus mode advertises the focus keys and drops the map's 选择 wording");
        Assert.IsFalse(bar.PromptText.Contains("LB") || bar.PromptText.Contains("RB"),
            "the wrist has no tabs, so no tab key is offered");
        Assert.IsFalse(bar.PromptText.Contains("LS"),
            "the wrist has no pan, so no pan key is offered");

        // Keyboard glyphs follow the last device, rows unchanged.
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Digit1));
        yield return null;
        yield return null;
        Assert.AreEqual(InputDeviceTracker.Device.KeyboardMouse, InputDeviceTracker.LastDevice,
            "a real key press switches the last device");
        Assert.IsTrue(bar.PromptText.Contains("[方向键] 选择") && bar.PromptText.Contains("[E] 确认"),
            "focus mode re-glyphs for mouse + keyboard (" + bar.PromptText + ")");

        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
        InputDeviceTracker.NotifyGamepad();
        yield return null;
        yield return null;
        Assert.IsTrue(bar.PromptText.Contains("[D-Pad] 选择"),
            "and back to the pad without changing the rows");
    }

    // ====================================================================
    // 7. Harvest: the line only flows while data really moves
    // ====================================================================

    [UnityTest]
    public IEnumerator WristHarvest_DataLineFlowsOnlyWhileTransferring_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        // The idle read-out is checked while the terminal is presented but nothing
        // is transferring yet.
        yield return OpenWristOn(surface);
        SurfaceWristController wrist = surface.Wrist;
        VisualElement root = wrist.RootElement;

        Assert.IsFalse(wrist.Radial.HarvestFlowing,
            "待机: the harvest line is static while nothing is transferring");
        Assert.AreEqual("待机", root.Q<Label>("HarvestState").text, "the block reads 待机");

        // Close the terminal, start a REAL harvest on node 0 (the exploration
        // authority refuses to deploy while the wrist owns the input), then reopen.
        surface.SetWristOpen(false);
        yield return null;
        surface.Player.TeleportTo(surface.Nodes[0].transform.position
                                  + new Vector3(0f, 0f, 1.2f));
        yield return null;
        Assert.IsTrue(surface.TryDeployNearest(), "node 0 starts harvesting");
        yield return OpenWristOn(surface);
        wrist = surface.Wrist;
        root = wrist.RootElement;
        yield return null;

        Assert.IsTrue(wrist.Radial.HarvestFlowing,
            "采集 / 传输: the harvest plate flows while data moves");
        Assert.AreEqual("采集中", root.Q<Label>("HarvestState").text, "and the block says so");

        SurfaceResourceNode node0 = surface.Nodes[0];
        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        Assert.AreEqual("无线传输", root.Q<Label>("HarvestState").text, "the transfer stage runs");
        Assert.IsTrue(wrist.Radial.HarvestFlowing,
            "the line keeps flowing through the transfer stage");

        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node0.State, "the node settled");
        Assert.IsFalse(wrist.Radial.HarvestFlowing,
            "the flow stops when the transfer is over");
    }

    // ====================================================================
    // 8. Presentation contract: industrial plates, embedded chips, smooth focus
    // ====================================================================

    [UnityTest]
    public IEnumerator WristPresentation_PlatesChipsAndFocusTransition_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;

        Assert.IsTrue(wrist.VerifyWristFocusModel(out string report),
            "the PASS 3 focus / plate model verifies:\n" + report);

        // The status read-outs are embedded chips, not floating headlines.
        Rect health = SurfaceWristController.ResolvedRectOf(
            wrist.RootElement.Q<Label>("HealthLabel"));
        Rect satiety = SurfaceWristController.ResolvedRectOf(
            wrist.RootElement.Q<Label>("SatietyLabel"));
        Assert.LessOrEqual(health.height, 24f, "the health read-out is a small chip");
        Assert.LessOrEqual(satiety.height, 24f, "the satiety read-out is a small chip");
        Assert.Less(health.y, 160f, "the health chip sits at the upper arc");
        Assert.Greater(satiety.y, 600f, "the satiety chip sits at the lower arc");

        // Focus lifts the plate over ~120 ms instead of popping.
        wrist.SetPadMode(WristPadMode.ModuleFocus);
        yield return null;
        Assert.IsTrue(wrist.FocusNavigator.SetFocus(
                wrist.RootElement.Q<VisualElement>(SurfaceWristController.MapFocusAnchorName)),
            "the central map takes focus");
        // The lift is a real transition, so the FIRST animated frame must show a
        // partial strength instead of a completed one. (The test coroutine and the
        // surface controller are both Update-phase, so the sample is taken on the
        // first frame that actually observed the new focus.)
        float early = 0f;
        for (int i = 0; i < 4 && early <= 0f; i++)
        {
            yield return null;
            early = wrist.Radial.FocusStrengthOf(WristModuleId.Map);
        }

        Assert.Greater(early, 0f, "the plate starts lifting as soon as the row is focused");
        Assert.Less(early, 1f, "but it does not pop to full strength in one frame");

        // The transition completes inside the authored 100-150 ms window, measured
        // in real time (a batch-mode frame is far shorter than a presented one).
        yield return WaitRealtime(0.4f);
        float settled = wrist.Radial.FocusStrengthOf(WristModuleId.Map);
        Assert.GreaterOrEqual(settled, 0.999f,
            "the lift completes within the authored ~120 ms window (was "
            + settled.ToString("0.000") + ")");
        Assert.LessOrEqual(wrist.Radial.FocusStrengthOf(WristModuleId.State), 0.001f,
            "only ONE module is lifted at a time");
        Assert.LessOrEqual(wrist.Radial.FocusStrengthOf(WristModuleId.Equipment), 0.001f,
            "and the read-only equipment plate is never lifted");
    }

    // ====================================================================
    // 9. B closes the terminal from BOTH modes, and exploration comes back
    // ====================================================================

    [UnityTest]
    public IEnumerator WristBack_ClosesFromBothModes_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;

        // ---- from module focus ----
        yield return PushDPad(GamepadButton.DpadDown);
        Assert.AreEqual(WristPadMode.ModuleFocus, wrist.PadMode, "focus mode is active");
        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(surface.IsWristOpen, "B closes the terminal from focus mode");
        Assert.IsTrue(surface.Player.InputEnabled, "exploration input is restored");

        // ---- from map cursor ----
        yield return OpenWristAgain(surface);
        Assert.AreEqual(WristPadMode.MapCursor, surface.Wrist.PadMode,
            "a reopened terminal starts in cursor mode again");
        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(surface.IsWristOpen, "B closes the terminal from cursor mode");
        Assert.IsTrue(surface.Player.InputEnabled, "exploration input is restored again");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "Back belongs to free exploration again");
    }

    // ====================================================================
    // 10. The accepted PASS 2 map route is untouched
    // ====================================================================

    [UnityTest]
    public IEnumerator WristMap_Pass2CursorRouteStillHolds_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenWrist(value => surface = value);
        SurfaceWristController wrist = surface.Wrist;

        Vector2 start = UiPointerState.Position;
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(0f, 1f) });
        yield return null;
        yield return null;
        Assert.IsTrue(UiPointerState.GamepadOwns, "the right stick owns the shared cursor");
        yield return WaitRealtime(0.25f);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        Assert.Greater(Vector2.Distance(UiPointerState.Position, start), 4f,
            "the right stick still moves the existing map cursor");

        // Confirm is still the mouse's left button on a node.
        yield return PlacePointerOnNode(surface, 0);
        Assert.AreEqual(0, wrist.HitTestFromPointer(), "the pointer resolves node 0");
        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(0, surface.NavigationTargetNodeId,
            "A still selects the node under the cursor in cursor mode");

        // ... and the triggers still zoom.
        float zoomBefore = wrist.MapZoom;
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightTrigger = 1f });
        yield return WaitRealtime(0.4f);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        Assert.Greater(wrist.MapZoom, zoomBefore + 0.05f, "RT still zooms the map in");
    }

    // ====================================================================
    // 11. The ship terminal is an ordinary UI: focus keys only, no map keys
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipWrist_OpensInModuleFocus_AndNeverOffersMapKeys_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        Assert.IsFalse(ship.IsWristOpen, "the ship wrist starts closed");

        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(ship.IsWristOpen, "Y opened the ship wrist");
        yield return WaitUntil(() => ship.IsWristTerminalRevealed, 5f);
        yield return WaitUntil(() => ship.Wrist != null && ship.Wrist.Display != null
                                     && ship.Wrist.Display.Visible, 4f);
        yield return WaitForDisplayToSettleShip(ship, 3f);
        yield return null;

        SurfaceWristController wrist = ship.Wrist;
        Assert.AreEqual(WristPadMode.ModuleFocus, wrist.PadMode,
            "the ship terminal is an ordinary UI and opens in module focus");
        Assert.IsNull(wrist.FocusNavigator.Focused,
            "with nothing operable there is no focus ring at all");

        // The ship centre is a deck schematic and the equipment block is read-only,
        // so this terminal genuinely has NOTHING operable: no focus stop at all.
        List<VisualElement> items = wrist.FocusNavigator.ActiveGroup;
        Assert.AreEqual(0, items.Count,
            "the ship terminal is a read-only board (deck schematic + equipment list)");
        Assert.IsFalse(items.Contains(wrist.RootElement.Q<Label>("NavTarget")),
            "the ship has no surface navigation, so its row is not operable");
        Assert.IsFalse(items.Contains(wrist.RootElement.Q<Label>("EquipBattery")),
            "and the equipment list is never operable");

        yield return WaitUntil(() => ship.Hud != null && ship.Hud.PromptBar != null
                                     && ship.Hud.PromptBar.IsShown, 3f);
        ControllerPromptBar bar = ship.Hud.PromptBar;
        Assert.IsNotNull(bar, "the ship terminal owns the shared prompt bar");
        InputDeviceTracker.NotifyGamepad();
        yield return null;
        yield return null;
        Assert.AreEqual("[B] 返回", bar.PromptText,
            "a page with nothing operable advertises only Back (no dead keys)");
        Assert.IsFalse(bar.PromptText.Contains("光标") || bar.PromptText.Contains("缩放"),
            "no map key is offered where the map is a static deck schematic");

        // A cannot change a read-only loadout; B still closes.
        GameSession session = GameSession.Instance;
        bool batteryBefore = session.BatterySelected;
        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(batteryBefore, session.BatterySelected,
            "A never changes the loadout on the ship terminal either");

        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(ship.IsWristOpen, "B closed the ship terminal");
        Assert.IsFalse(bar.IsShown, "and its prompt was withdrawn with it");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static IEnumerator EnterShip(System.Action<ShipGameController> onShip)
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitUntil(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady && ship.Player != null;
        }, 25f);
        Assert.IsNotNull(ship, "the ship interior is ready");
        yield return WaitUntil(() => !ship.IsEntryInputLocked, 8f);
        onShip(ship);
    }

    private static IEnumerator WaitForDisplayToSettleShip(ShipGameController ship, float seconds)
    {
        Transform display = ship.Wrist != null && ship.Wrist.Display != null
            ? ship.Wrist.Display.transform
            : null;
        if (display == null)
        {
            yield break;
        }

        Vector3 lastPosition = display.position;
        Quaternion lastRotation = display.rotation;
        int stable = 0;
        float deadline = Time.realtimeSinceStartup + seconds;
        while (stable < 3 && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            bool still = Vector3.Distance(display.position, lastPosition) < 0.0005f
                         && Quaternion.Angle(display.rotation, lastRotation) < 0.05f;
            stable = still ? stable + 1 : 0;
            lastPosition = display.position;
            lastRotation = display.rotation;
        }
    }

    private static bool IsOperableName(string name, SurfaceGameController surface)
    {
        switch (name)
        {
            case SurfaceWristController.MapFocusAnchorName:
                return true;
            case "NavTarget":
                return surface != null && surface.IsNavigationActive;
            default:
                // Includes every equipment row: the block is read-only, so no
                // direction may ever park the focus on it.
                return false;
        }
    }

    /// <summary>Elements that currently carry the wrist focus class.</summary>
    private static int CountFocusClassed(SurfaceWristController wrist)
    {
        int count = 0;
        foreach (VisualElement element in wrist.RootElement.Query<VisualElement>().ToList())
        {
            if (element.ClassListContains(SurfaceWristController.FocusClass))
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerator WaitRealtime(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private IEnumerator Hold(float seconds)
    {
        yield return WaitRealtime(seconds);
    }

    private IEnumerator PushDPad(GamepadButton direction)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(direction));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    private IEnumerator PressButton(GamepadButton button)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    /// <summary>Opens the wrist through the REAL gamepad route and waits for the
    /// hologram to stop moving (the raise animation travels with the display).</summary>
    private IEnumerator OpenWrist(System.Action<SurfaceGameController> onSurface)
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);
        yield return OpenWristOn(surface);
        onSurface(surface);
    }

    /// <summary>Opens the terminal on an ALREADY running surface and waits for the
    /// raise animation and the hologram to settle (the tests that must prepare world
    /// state first cannot reload the scene a second time).</summary>
    private IEnumerator OpenWristOn(SurfaceGameController surface)
    {
        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(surface.IsWristOpen, "Y (buttonNorth) opens the wrist terminal");

        SurfaceWristController wrist = surface.Wrist;
        yield return WaitUntil(() => wrist != null && wrist.Display != null
                                     && wrist.Display.Visible, 4f);
        Assert.IsTrue(wrist.Display.Visible, "the terminal is presented");
        yield return WaitUntil(() => surface.Arms != null && surface.Arms.FocusBlend >= 0.999f, 5f);
        yield return WaitForDisplayToSettle(surface, 3f);
        yield return WaitUntil(() => surface.Hud != null && surface.Hud.PromptBar != null
                                     && surface.Hud.PromptBar.IsShown, 3f);
    }

    private static IEnumerator OpenWristAgain(SurfaceGameController surface)
    {
        yield return null;
        InputSystem.QueueStateEvent(Gamepad.current, new GamepadState().WithButton(GamepadButton.North));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(Gamepad.current, new GamepadState());
        yield return null;
        yield return null;
        yield return WaitUntil(() => surface.IsWristOpen, 3f);
        yield return WaitUntil(() => surface.Wrist != null && surface.Wrist.Display != null
                                     && surface.Wrist.Display.Visible, 4f);
        yield return WaitUntil(() => surface.Arms != null && surface.Arms.FocusBlend >= 0.999f, 5f);
        yield return WaitForDisplayToSettle(surface, 3f);
    }

    private static IEnumerator WaitForDisplayToSettle(SurfaceGameController surface, float seconds)
    {
        Transform display = surface.Wrist != null && surface.Wrist.Display != null
            ? surface.Wrist.Display.transform
            : null;
        if (display == null)
        {
            yield break;
        }

        Vector3 lastPosition = display.position;
        Quaternion lastRotation = display.rotation;
        int stable = 0;
        float deadline = Time.realtimeSinceStartup + seconds;
        while (stable < 3 && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            bool still = Vector3.Distance(display.position, lastPosition) < 0.0005f
                         && Quaternion.Angle(display.rotation, lastRotation) < 0.05f;
            stable = still ? stable + 1 : 0;
            lastPosition = display.position;
            lastRotation = display.rotation;
        }
    }

    private static IEnumerator PlacePointerOnNode(SurfaceGameController surface, int nodeId,
        int attempts = 6)
    {
        for (int i = 0; i < attempts; i++)
        {
            SurfaceResourceNode node = surface.GetNodeById(nodeId);
            Vector2 pixel = WristMapCoordinates.WorldToWristMap(node.transform.position,
                surface.Wrist.MapZoom);
            if (surface.Wrist.TryProjectWristPixelToViewport(pixel, out Vector3 viewport))
            {
                yield return PlacePointer(new Vector2(viewport.x * Screen.width,
                    viewport.y * Screen.height));
            }

            if (surface.Wrist.HitTestFromPointer() == nodeId)
            {
                yield break;
            }
        }
    }

    private static IEnumerator PlacePointer(Vector2 screenPosition)
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            mouse.WarpCursorPosition(screenPosition);
            InputDeviceTracker.NotifyPointerWarp(screenPosition);
            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                position = screenPosition,
                delta = Vector2.zero
            });
        }

        UiPointerState.PlaceForAutomation(screenPosition, UiPointerState.Source.Mouse);
        yield return null;
        yield return null;
    }

    private static IEnumerator EnterSurface(System.Action<SurfaceGameController> onSurface)
    {
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return null;

        SurfaceGameController surface = null;
        yield return WaitUntil(() =>
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            return surface != null && surface.IsReady && surface.Player != null;
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
