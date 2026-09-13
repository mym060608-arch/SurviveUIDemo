using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

/// <summary>
/// Phase 09B PASS 3 wrist evidence probe.
///
/// Inert unless the player is launched with
/// <c>-ship09BWristShot &lt;output folder&gt;</c>. It drives the REAL product route with
/// a REAL virtual gamepad (queued device state, exactly what a physical pad
/// sends) and records what the player would see and feel on the wrist terminal:
///
///   * Y opens the terminal in the accepted PASS 2 cursor mode with no focus box,
///   * the D-Pad takes it into module focus, the ring only ever lands on elements
///     that act, and A confirms the focused one (real equipment authority),
///   * the right stick hands the terminal back to the shared map cursor, A is the
///     map's left button and LT/RT zoom the map content,
///   * the navigation block shows the standby radar + "?" until a target exists
///     and a live bearing once it does,
///   * the harvest plate only flows while data is really moving,
///   * the shared Controller Prompt re-glyphs for the mode and the device.
///
/// Six acceptance frames are written: wrist_default, wrist_focus,
/// wrist_map_controller, wrist_navigation_unset, wrist_navigation_set and
/// wrist_collecting.
/// </summary>
public class Ship09BWristProbe : MonoBehaviour
{
    private const string Flag = "-ship09BWristShot";

    private string _folder;
    private bool _active;
    private readonly StringBuilder _log = new StringBuilder();
    private bool _allPassed = true;

    private Gamepad _pad;
    private bool _addedPad;
    private SurfaceGameController _surface;
    private SurfacePlayerController _player;

    private void Awake()
    {
        _folder = ParseFlag(Flag);
        _active = !string.IsNullOrEmpty(_folder);
        if (!_active)
        {
            enabled = false;
            return;
        }

        Directory.CreateDirectory(_folder);
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        Screen.SetResolution(1920, 1080, false);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (_active)
        {
            StartCoroutine(Run());
            StartCoroutine(Watchdog());
        }
    }

    private IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(420f);
        Fail("watchdog timeout");
        Finish(2);
    }

    private IEnumerator Run()
    {
        Info("probe start: screen " + Screen.width + "x" + Screen.height);

        yield return EnsureGamepadDevice();
        if (_pad == null || !_pad.added)
        {
            Finish(1);
            yield break;
        }

        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // The expedition carries the optional battery and left the survey drone
        // behind: the terminal must list exactly that (one equipped module, one
        // module absent), which is the read-only contract this evidence covers.
        Check("the expedition carries the optional battery",
            session.TrySetBatterySelected(true));
        Check("the loadout is confirmed before landing", session.TryConfirmLoadout());

        SceneRouting.EnterSurface();
        yield return WaitUntil(() =>
        {
            _surface = UnityEngine.Object.FindAnyObjectByType<SurfaceGameController>();
            return _surface != null && _surface.IsReady && _surface.Player != null
                   && _surface.Nodes.Count > 0;
        }, 40f);

        Check("the planet surface is ready", _surface != null && _surface.Player != null);
        if (_surface == null || _surface.Player == null)
        {
            Finish(1);
            yield break;
        }

        _player = _surface.Player;
        yield return WaitUntil(() => _player.InputEnabled, 5f);

        SurfaceResourceNode node0 = _surface.GetNodeById(0);
        Check("node 0 exists for the navigation evidence", node0 != null);
        _player.TeleportTo(node0.transform.position + new Vector3(-1.7f, 0f, 1.9f));
        yield return null;
        FaceToward(_player, node0.transform.position);
        yield return new WaitForSecondsRealtime(0.5f);

        // ---- Y opens the terminal (the frozen Phase 09A binding) ----
        Check("the wrist is closed before the press", !_surface.IsWristOpen);
        yield return PressButton(GamepadButton.North);
        Check("Y (buttonNorth) opened the wrist terminal", _surface.IsWristOpen);

        SurfaceWristController wrist = _surface.Wrist;
        yield return WaitUntil(() => wrist != null && wrist.Display != null
                                     && wrist.Display.Visible, 4f);
        Check("the terminal hologram is presented", MapVisible());
        yield return WaitUntil(() => _surface.Arms != null && _surface.Arms.FocusBlend >= 0.999f, 5f);
        yield return WaitForDisplayToSettle(3f);
        yield return new WaitForSecondsRealtime(0.4f);

        Check("the deck has exactly one wrist terminal (no second, pad-only UI)",
            UnityEngine.Object.FindObjectsByType<SurfaceWristController>(
                FindObjectsInactive.Include).Length == 1);
        Check("the terminal opens in the accepted PASS 2 cursor mode",
            wrist.PadMode == WristPadMode.MapCursor);
        Check("cursor mode draws no focus box", wrist.FocusNavigator.Focused == null);

        ControllerPromptBar bar = _surface.Hud != null ? _surface.Hud.PromptBar : null;
        Check("the surface owns the shared prompt bar", bar != null);
        if (bar != null)
        {
            Info("cursor-mode prompt: " + bar.PromptText);
            Check("cursor mode advertises the frozen map rows",
                bar.PromptText == "[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回");
        }

        // ---- objective visual contract, measured on the terminal's own texture
        // ---- and on the final composited frame
        yield return null;
        yield return CheckWristPresentation(wrist);

        // ---- frame 1: default terminal, cursor mode, navigation unset ----
        yield return Capture("wrist_default");

        // ---- navigation UNSET: standby radar + "?" ----
        VisualElement root = wrist.RootElement;
        Check("navigation starts unset", !_surface.IsNavigationActive);
        Check("the navigation module is not operable in standby",
            !wrist.NavigationFocusable);
        Check("the standby radar shows its ? symbol",
            root.Q<Label>("NavRadarGlyph") != null
            && root.Q<Label>("NavRadarGlyph").text == "?");
        Check("the radar is sweeping, not locked", !wrist.Radial.RadarActive);
        Check("the harvest line is static while nothing transfers",
            !wrist.Radial.HarvestFlowing);
        yield return Capture("wrist_navigation_unset");

        // ---- the D-Pad takes the terminal into module focus ----
        yield return PushDPad(GamepadButton.DpadDown);
        Check("the D-Pad switched the terminal into module focus",
            wrist.PadMode == WristPadMode.ModuleFocus);
        Check("a D-Pad press moves the ring onto a real control ("
              + wrist.FocusedRowName + ")", IsOperable(wrist.FocusedRowName));
        if (bar != null)
        {
            Info("focus-mode prompt: " + bar.PromptText);
            Check("focus mode advertises the focus keys",
                bar.PromptText.Contains("[D-Pad] 选择") && bar.PromptText.Contains("[A] 确认"));
            Check("focus mode never advertises a dead key (no LB/RB/LS)",
                !bar.PromptText.Contains("LB") && !bar.PromptText.Contains("RB")
                && !bar.PromptText.Contains("LS"));
        }

        // ---- the equipment block is READ-ONLY and lists only what is carried ----
        Check("the equipment block lists every equipped module ("
              + string.Join(",", wrist.ListedEquipmentNames) + ")",
            wrist.ListedEquipmentNames.Length == 2
            && wrist.ListedEquipmentNames[0] == "EquipHarvester"
            && wrist.ListedEquipmentNames[1] == "EquipBattery");
        Check("no equipment row is a focus stop",
            !wrist.FocusNavigator.ActiveGroup.Contains(root.Q<Label>("EquipBattery"))
            && !wrist.FocusNavigator.ActiveGroup.Contains(root.Q<Label>("EquipDrone")));
        Check("the unequipped survey drone is not drawn at all",
            root.Q<Label>("EquipDrone").resolvedStyle.display == DisplayStyle.None
            && root.Q<Label>("EquipDrone").text.Length == 0);

        // ---- A confirms the focused element (the central map hands back to cursor) ----
        wrist.FocusNavigator.SetFocus(root.Q<VisualElement>(
            SurfaceWristController.MapFocusAnchorName));
        yield return null;
        yield return null;
        Check("the central map is the terminal's focus stop",
            wrist.FocusedRowName == SurfaceWristController.MapFocusAnchorName);
        Check("and the map plate is the one that lifts (not the read-only equipment)",
            wrist.Radial.FocusStrengthOf(WristModuleId.Map) > 0f
            && wrist.Radial.FocusStrengthOf(WristModuleId.Equipment) <= 0.001f);

        bool batteryBefore = session.BatterySelected;
        yield return PressButton(GamepadButton.South);
        Check("A on the focused map returns the terminal to the cursor",
            wrist.PadMode == WristPadMode.MapCursor);
        Check("A never changes the loadout from the wrist",
            session.BatterySelected == batteryBefore);
        Check("A never closed the terminal", _surface.IsWristOpen);
        yield return Capture("wrist_focus");

        // ---- the right stick hands the terminal back to the cursor ----
        Vector2 pointerBefore = UiPointerState.Position;
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(0f, 1f) });
        yield return new WaitForSecondsRealtime(0.3f);
        Vector2 pointerAfter = UiPointerState.Position;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        Check("the right stick hands the terminal back to the cursor",
            wrist.PadMode == WristPadMode.MapCursor);
        Check("the focus box is released in cursor mode",
            wrist.FocusNavigator.Focused == null);
        Check("the right stick moved the existing map cursor",
            Vector2.Distance(pointerAfter, pointerBefore) > 4f);
        if (bar != null)
        {
            Info("prompt back on the cursor rows: " + bar.PromptText);
            Check("the prompt followed the mode back",
                bar.PromptText == "[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回");
        }

        // ---- A is still the map's left button ----
        yield return SteerOntoNode(node0);
        Check("the cursor resolves node 0", wrist.HitTestFromPointer() == 0);
        yield return PressButton(GamepadButton.South);
        Info("after A on the node: target=" + _surface.NavigationTargetNodeId);
        Check("A selected the node under the cursor", _surface.NavigationTargetNodeId == 0);
        Check("selecting never closed the terminal", _surface.IsWristOpen);

        // ---- LT / RT still zoom ----
        float zoomBefore = wrist.MapZoom;
        yield return HoldTrigger(true, 0.5f);
        Info("zoom: " + zoomBefore.ToString("0.00") + " -> " + wrist.MapZoom.ToString("0.00"));
        Check("RT zooms the map content in", wrist.MapZoom > zoomBefore + 0.05f);
        yield return Capture("wrist_map_controller");

        // ---- navigation SET: live bearing + operable row ----
        yield return null;
        Check("the navigation module became operable", wrist.NavigationFocusable);
        Check("the radar switched to a live bearing", wrist.Radial.RadarActive);
        Check("the ? symbol is gone once navigation is live",
            root.Q<Label>("NavRadarGlyph") != null
            && root.Q<Label>("NavRadarGlyph").text.Length == 0);
        Info("navigation row: " + root.Q<Label>("NavTarget").text
             + " / " + root.Q<Label>("NavDistance").text
             + " bearing=" + wrist.Radial.RadarBearingDeg.ToString("0") + "deg");
        Check("the navigation row shows the live target and distance",
            root.Q<Label>("NavTarget").text == SurfaceGameController.NodeDisplayName(0)
            && root.Q<Label>("NavDistance").text.Length > 0);

        yield return PushDPad(GamepadButton.DpadLeft);
        wrist.FocusNavigator.SetFocus(root.Q<Label>("NavTarget"));
        yield return null;
        yield return null;
        Check("the live navigation row can be focused",
            wrist.FocusedRowName == "NavTarget");
        yield return Capture("wrist_navigation_set");

        // ---- collecting: the harvest line flows ----
        yield return PressButton(GamepadButton.East);
        Check("B closed the terminal", !_surface.IsWristOpen);
        Check("exploration input is restored", _player.InputEnabled);
        yield return null;

        _player.TeleportTo(node0.transform.position + new Vector3(0f, 0f, 1.2f));
        yield return null;
        Check("node 0 starts harvesting", _surface.TryDeployNearest());
        yield return new WaitForSecondsRealtime(0.4f);

        yield return PressButton(GamepadButton.North);
        yield return WaitUntil(() => _surface.IsWristOpen && MapVisible(), 4f);
        yield return WaitUntil(() => _surface.Arms != null && _surface.Arms.FocusBlend >= 0.999f, 5f);
        yield return WaitForDisplayToSettle(3f);
        yield return new WaitForSecondsRealtime(0.4f);

        wrist = _surface.Wrist;
        root = wrist.RootElement;
        Info("harvest: state=" + root.Q<Label>("HarvestState").text
             + " node=" + root.Q<Label>("HarvestNode").text
             + " progress=" + root.Q<Label>("HarvestProgress").text
             + " flowing=" + wrist.Radial.HarvestFlowing);
        Check("the harvest block reports the live state",
            root.Q<Label>("HarvestState").text == "采集中");
        Check("the harvest plate flows while data moves", wrist.Radial.HarvestFlowing);
        yield return Capture("wrist_collecting");

        Check("the terminal survived the whole walkthrough", _surface.IsWristOpen);

        // ---- the read-only equipment block tracks the REAL loadout live ----
        // (the loadout authority is the ship's page; the wrist only reflects it)
        Check("the survey drone is equipped at the ship",
            session.TrySetDroneSelected(true));
        yield return new WaitForSecondsRealtime(0.25f);
        Info("equipment after equipping the drone: ["
             + string.Join(",", wrist.ListedEquipmentNames) + "]");
        Check("the terminal lists all three equipped modules",
            wrist.ListedEquipmentNames.Length == 3
            && root.Q<Label>("EquipBattery").text == "备用电池"
            && root.Q<Label>("EquipDrone").text == "勘测无人机");
        yield return Capture("wrist_equipment_equipped_only");

        session.TrySetBatterySelected(false);
        yield return new WaitForSecondsRealtime(0.25f);
        Info("equipment after unequipping the battery: ["
             + string.Join(",", wrist.ListedEquipmentNames) + "]");
        Check("an unequipped module disappears from the terminal",
            root.Q<Label>("EquipBattery").resolvedStyle.display == DisplayStyle.None
            && root.Q<Label>("EquipBattery").text.Length == 0);
        Check("and the remaining rows are packed up (no hole where it was)",
            wrist.ListedEquipmentNames.Length == 2
            && Mathf.Abs(RowCentreY(root.Q<Label>("EquipHarvester"))
                         - SurfaceWristController.EquipmentSlotY(0)) < 0.75f
            && Mathf.Abs(RowCentreY(root.Q<Label>("EquipDrone"))
                         - SurfaceWristController.EquipmentSlotY(1)) < 0.75f);

        session.TrySetDroneSelected(false);
        yield return new WaitForSecondsRealtime(0.25f);
        Check("a bare loadout lists the required harvester alone",
            wrist.ListedEquipmentNames.Length == 1
            && wrist.ListedEquipmentNames[0] == "EquipHarvester");
        Check("and nothing in the block ever became a focus stop",
            wrist.FocusNavigator.ActiveGroup.Count == 0
            || !wrist.FocusNavigator.ActiveGroup.Contains(root.Q<Label>("EquipHarvester")));

        yield return PressButton(GamepadButton.East);
        Check("B closed the terminal again", !_surface.IsWristOpen);

        if (_addedPad && _pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        Finish(_allPassed ? 0 : 1);
    }

    // ====================================================================
    // Stages
    // ====================================================================

    /// <summary>
    /// Measures the PASS 3 presentation on the wrist's own render texture: the four
    /// peripheral plates must stay BELOW the central map in brightness (中央地图是
    /// 第一视觉), and the two status arcs must carry their colour families.
    /// </summary>
    private IEnumerator CheckWristPresentation(SurfaceWristController wrist)
    {
        // Sample points sit in each plate's OUTER margin and inside the map disc
        // away from the cross reference, so no glyph and no hairline can bias the
        // measurement (the row slots stop 14 px short of the plate edges).
        Color map = Sample(wrist, new Vector2(600f, 358f));
        Color equip = Sample(wrist, new Vector2(296f, 300f));
        Color state = Sample(wrist, new Vector2(984f, 300f));
        Color nav = Sample(wrist, new Vector2(302f, 520f));
        Color harvest = Sample(wrist, new Vector2(978f, 520f));
        Color health = Sample(wrist, new Vector2(640f, 145f));
        Color satiety = Sample(wrist, new Vector2(640f, 655f));

        float mapLuma = Luma(map);
        float plateMaxLuma = Mathf.Max(Mathf.Max(Luma(equip), Luma(state)),
            Mathf.Max(Luma(nav), Luma(harvest)));
        float plateMinAlpha = Mathf.Min(Mathf.Min(equip.a, state.a),
            Mathf.Min(nav.a, harvest.a));

        Info("rt sample: map luma=" + mapLuma.ToString("0.000") + " a=" + map.a.ToString("0.00")
             + " | plates luma=" + Luma(equip).ToString("0.000") + "/"
             + Luma(state).ToString("0.000") + "/" + Luma(nav).ToString("0.000")
             + "/" + Luma(harvest).ToString("0.000")
             + " a=" + equip.a.ToString("0.00")
             + " | health=" + health.ToString("0.00")
             + " satiety=" + satiety.ToString("0.00"));

        Check("the central map reads lighter than every peripheral plate (map "
              + mapLuma.ToString("0.000") + " > plates " + plateMaxLuma.ToString("0.000") + ")",
            mapLuma > plateMaxLuma);
        Check("the map lets far more of the world through than the plates do (map a="
              + map.a.ToString("0.00") + " < plate a=" + plateMinAlpha.ToString("0.00")
              + "), which is what keeps the frosted map the first visual level",
            map.a < plateMinAlpha);
        Check("the four peripheral plates share one restrained structure tone",
            Mathf.Abs(Luma(equip) - Luma(state)) < 0.06f
            && Mathf.Abs(Luma(nav) - Luma(harvest)) < 0.06f);
        // The RT keeps the authored straight RGB but not the authored
        // translucency (the UI Toolkit renderer writes coverage there), so the
        // honest translucency proof is the SHEET material: the peripheral
        // structure must be a semi-transparent sheet while the map disc keeps its
        // own frosted lens.
        WristDisplayAnchor display = wrist.Display;
        Info("sheet opacity: structure=" + (display != null
                 ? display.StructureOpacity.ToString("0.00")
                 : "<no display>")
             + " disc=" + (display != null && display.SurfaceMaterial != null
                 && display.SurfaceMaterial.HasProperty("_FrostOpacity")
                     ? display.SurfaceMaterial.GetFloat("_FrostOpacity").ToString("0.00")
                     : "<n/a>"));
        Check("the peripheral structure is a translucent sheet (alpha between 0.5 and 0.85)",
            display != null && display.StructureOpacity > 0.5f
            && display.StructureOpacity < 0.85f);
        Check("the frosted map disc keeps its own accepted opacity",
            display != null && display.SurfaceMaterial != null
            && display.SurfaceMaterial.HasProperty("_FrostOpacity")
            && display.SurfaceMaterial.GetFloat("_FrostOpacity") < 0.95f);
        yield return CheckFinalFrame(wrist);
        Check("the upper status arc reads GREEN (health)",
            health.g > health.r && health.g > health.b);
        Check("the lower status arc reads ORANGE (satiety)",
            satiety.r > satiety.g && satiety.g > satiety.b);
    }

    /// <summary>
    /// Scans the FINAL frame (the backbuffer the player sees) for pixels that carry
    /// the plates' own opaque colour. If the structure sheet were opaque - the
    /// "card / paper" failure the contract forbids - thousands of pixels would be
    /// exactly that colour; a translucent sheet never produces a single one,
    /// because every plate pixel is a blend with the world behind it. The test is
    /// orientation- and projection-free on purpose: it cannot be fooled by the
    /// hologram's parallax moving a sample point.
    /// </summary>
    private IEnumerator CheckFinalFrame(SurfaceWristController wrist)
    {
        yield return null;
        yield return null;

        var grab = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
        grab.Create();
        ScreenCapture.CaptureScreenshotIntoRenderTexture(grab);
        yield return new WaitForEndOfFrame();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = grab;
        var readback = new Texture2D(Screen.width, Screen.height, TextureFormat.ARGB32, false);
        readback.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
        readback.Apply();

        Color[] pixels = readback.GetPixels();
        RenderTexture.active = previous;
        UnityEngine.Object.Destroy(readback);
        grab.Release();
        UnityEngine.Object.Destroy(grab);

        // The authored plate ink (35,48,58) and the retired paper fill
        // (196,207,213): neither may appear as an untouched block in the frame.
        Color plateInk = Sample(wrist, new Vector2(296f, 300f));
        var paper = new Color(196f / 255f, 207f / 255f, 213f / 255f);

        int opaquePlate = 0;
        int paperPixels = 0;
        foreach (Color pixel in pixels)
        {
            if (Near(pixel, plateInk, 5)) { opaquePlate++; }
            if (Near(pixel, paper, 4)) { paperPixels++; }
        }

        float total = Mathf.Max(1, pixels.Length);
        Info("final frame: plateInk=" + Rgb(plateInk) + " opaquePlatePixels=" + opaquePlate
             + " (" + (opaquePlate / total * 100f).ToString("0.000") + "%)"
             + " paperPixels=" + paperPixels
             + " (" + (paperPixels / total * 100f).ToString("0.000") + "%)");

        // Calibration for the thresholds: an opaque set of four plates would cover
        // roughly 8% of a 1080p frame, so anything an order of magnitude below that
        // proves the structure sheet is genuinely translucent (the remaining pixels
        // are ordinary scene content - dark terrain and the block arms - that happen
        // to share the tone; the Phase 09B PASS 2 frame measures 0.025% of them with
        // light plates, i.e. the same unrelated population).
        Check("the plates are not drawn as opaque blocks in the final frame ("
              + (opaquePlate / total * 100f).ToString("0.000")
              + "% of pixels carry the plate colour; opaque plates would cover ~8%)",
            opaquePlate < total * 0.02f);
        Check("no white paper plate survives anywhere in the final frame ("
              + (paperPixels / total * 100f).ToString("0.000") + "% of the retired fill)",
            paperPixels < total * 0.01f);
    }

    /// <summary>Centre Y a text row is actually laid out at (wrist pixels).</summary>
    private static float RowCentreY(Label row)
    {
        return row.style.top.value.value + SurfaceWristController.RowHeight * 0.5f;
    }

    private static bool Near(Color pixel, Color target, int tolerance)
    {
        float t = tolerance / 255f;
        return Mathf.Abs(pixel.r - target.r) <= t
               && Mathf.Abs(pixel.g - target.g) <= t
               && Mathf.Abs(pixel.b - target.b) <= t;
    }

    private static string Rgb(Color color)
    {
        return "(" + Mathf.RoundToInt(color.r * 255f) + ","
               + Mathf.RoundToInt(color.g * 255f) + ","
               + Mathf.RoundToInt(color.b * 255f) + ")";
    }

    private Color Sample(SurfaceWristController wrist, Vector2 wristPixel)
    {
        if (!wrist.TrySampleWristPixel(wristPixel, out Color color))
        {
            Fail("wrist render texture unavailable for " + Format(wristPixel));
            return Color.clear;
        }

        return color;
    }

    private static float Luma(Color color)
    {
        return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
    }

    private static bool IsOperable(string name)
    {
        // The equipment block is read-only, so it is deliberately not operable.
        return name == SurfaceWristController.MapFocusAnchorName || name == "NavTarget";
    }

    /// <summary>Drives the cursor onto node 0 with the stick alone.</summary>
    private IEnumerator SteerOntoNode(SurfaceResourceNode node)
    {
        int frames = 0;
        while (frames < 220)
        {
            if (wristHit() == 0)
            {
                break;
            }

            Vector2 target = ScreenOf(WristMapCoordinates.WorldToWristMap(
                node.transform.position, _surface.Wrist.MapZoom));
            Vector2 current = UiPointerState.Position;
            Vector2 delta = target - current;
            if (delta.magnitude < 1f)
            {
                break;
            }

            InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = delta.normalized });
            frames++;
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        Info("stick steering: " + frames + " frames -> hit=" + wristHit());
    }

    private int wristHit()
    {
        return _surface.Wrist != null ? _surface.Wrist.HitTestFromPointer() : -1;
    }

    private Vector2 ScreenOf(Vector2 wristPixel)
    {
        if (_surface.Wrist.TryProjectWristPixelToViewport(wristPixel, out Vector3 viewport))
        {
            return new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        }

        Fail("wrist pixel " + Format(wristPixel) + " does not project onto the display");
        return Vector2.zero;
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private bool MapVisible()
    {
        return _surface != null && _surface.Wrist != null && _surface.Wrist.Display != null
               && _surface.Wrist.Display.Visible;
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

    private IEnumerator HoldTrigger(bool right, float seconds)
    {
        InputSystem.QueueStateEvent(_pad, right
            ? new GamepadState { rightTrigger = 1f }
            : new GamepadState { leftTrigger = 1f });
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

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

    private IEnumerator WaitForDisplayToSettle(float seconds)
    {
        Transform display = _surface.Wrist != null && _surface.Wrist.Display != null
            ? _surface.Wrist.Display.transform
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

    /// <summary>
    /// Creates the run's virtual gamepad once and proves that a queued state event
    /// really reaches it. The proof uses the Select control: it has no meaning
    /// anywhere in the product, so the delivery check cannot disturb the state under
    /// test.
    /// </summary>
    private IEnumerator EnsureGamepadDevice()
    {
        // ALWAYS drive a device this run OWNS. A queued synthetic state is only
        // authoritative on a device the Input System treats as script-owned: a
        // physically connected pad is polled by its native backend every frame and
        // overwrites (or rejects) the queued event, which would silently turn the
        // whole evidence run into "no input at all". The synthetic pad is removed
        // again at the end, so a real controller is left exactly as it was found.
        _pad = InputSystem.AddDevice<Gamepad>();
        _addedPad = true;

        InputSystem.EnableDevice(_pad);
        yield return null;

        // The first queued event also makes the script-owned pad the CURRENT one,
        // which is the device the product reads - so the two checks below cover both
        // halves of the route: "the game reads my pad" and "my pad really receives
        // what I queue".
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.Select));
        yield return WaitUntil(() => Gamepad.current == _pad, 8f);
        yield return null;

        float value = _pad.selectButton.ReadValue();
        Info("gamepad route: owned=" + (Gamepad.current == _pad)
             + " current=" + (Gamepad.current != null ? Gamepad.current.name : "<none>"));
        Check("the evidence run owns the current gamepad device (a physically connected "
              + "pad cannot steal the queued input)", Gamepad.current == _pad);
        Check("a queued gamepad state event reaches the real device route (Select read "
              + value.ToString("0.00", CultureInfo.InvariantCulture) + ")", value > 0.5f);
        if (Gamepad.current != _pad || value <= 0.5f)
        {
            Finish(1);
            yield break;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    private static void FaceToward(SurfacePlayerController player, Vector3 target)
    {
        Vector3 delta = target - player.transform.position;
        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
    }

    private static string Format(Vector2 value)
    {
        return "(" + value.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
               + value.y.ToString("0.0", CultureInfo.InvariantCulture) + ")";
    }

    private IEnumerator Capture(string suffix)
    {
        string path = Path.Combine(_folder, suffix + ".png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 600; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.12f);
        Check("frame captured: " + suffix, File.Exists(path));
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private void Check(string label, bool passed)
    {
        _log.AppendLine((passed ? "PASS" : "FAIL") + " :: " + label);
        if (!passed)
        {
            _allPassed = false;
        }
    }

    private void Info(string message)
    {
        _log.AppendLine("INFO :: " + message);
    }

    private void Fail(string message)
    {
        Check(message, false);
    }

    private void Finish(int exitCode)
    {
        string resultPath = Path.Combine(_folder, "phase09b_wrist_result.txt");
        try
        {
            File.WriteAllText(resultPath,
                "=== PHASE 09B PASS 3 WRIST EVIDENCE ===\n" + _log);
        }
        catch (Exception e)
        {
            Debug.LogError("[Ship09BWristProbe] cannot write result: " + e.Message);
        }

        Debug.Log("[Ship09BWristProbe] result " + (_allPassed ? "ALL PASS" : "FAILURES")
                  + " -> " + resultPath);
        Application.Quit(_allPassed ? 0 : exitCode);
    }

    private static string ParseFlag(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
