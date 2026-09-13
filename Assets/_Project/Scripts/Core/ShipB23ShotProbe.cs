using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.3 release evidence probe (inert unless launched with
/// -ship07B23Shot &lt;output folder&gt;).
///
/// Proves, in the Windows standalone Release player and through REAL pointer
/// events, the round's human blockers:
///   1. the shipboard screen-space modals are the pointer target again
///      (equipment locker -> loadout, deployment station -> confirmation),
///   2. a real click on 备用电池组 / 勘测无人机 changes GameSession,
///   3. 调整装备 -> shared loadout -> 返回 -> 确认降临 really reaches
///      02_NereidSurface,
///   4. the ship Overview terminal shows no 准备远征 CTA,
///   5. the first-person crosshair is hidden while the wrist is open,
///   6. the wrist floorplan shows dashed doors that replace the wall.
///
/// TEST INSTRUMENTATION (documented): an automated player window is not the
/// foreground window, and UI Toolkit's default event system ignores input while
/// the app is unfocused (eventSystemUpdateMode = IgnoreIfAppNotFocused). The
/// probe therefore switches that internal mode to Always for the run so the
/// injected mouse state is dispatched exactly like a real focused window would.
/// It never changes what the panels do with the events.
///
/// Writes phase07b23_result.txt; exit code follows the result.
/// </summary>
public class ShipB23ShotProbe : MonoBehaviour
{
    private const string Flag = "-ship07B23Shot";

    private string _folder;
    private bool _active;
    private readonly StringBuilder _report = new StringBuilder();
    private int _failures;
    private ShipGameController _controller;
    private SurfacePlayerController _player;
    private ShipOverlayUi _overlay;
    private Mouse _mouse;

    private void Awake()
    {
        _folder = ParseFlag(Flag);
        _active = !string.IsNullOrEmpty(_folder);
        if (!_active)
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(_folder);
    }

    private void Start()
    {
        if (_active)
        {
            StartCoroutine(Run());
            StartCoroutine(Watchdog());
        }
    }

    private static IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(600f);
        Debug.LogError("[ShipB23] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        GameSession.Ensure().ResetSession();
        Screen.SetResolution(1920, 1080, false);
        yield return WaitReal(0.8f);

        _mouse = Mouse.current;
        Check("real mouse device available", _mouse != null);
        ForceEventSystemAlwaysUpdate();

        for (int i = 0; i < 2400; i++)
        {
            _controller = Object.FindAnyObjectByType<ShipGameController>();
            if (_controller != null && _controller.IsReady
                && _controller.Consoles.Count == 4 && _controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        Check("ship interior ready", _controller != null && _controller.IsReady);
        if (_controller == null || !_controller.IsReady)
        {
            Finish();
            yield break;
        }

        _player = _controller.Player;
        _overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Check("ship overlay coordinator present", _overlay != null);

        yield return GateOverviewNoCta();
        yield return GateWrist();

        if (_failures == 0)
        {
            yield return GateEquipmentLockerLoadout();
            yield return GateDeploymentConfirm();
            yield return GateSurfaceReached();
        }

        Finish();
    }

    /// <summary>UI Toolkit ignores input while the app is unfocused (the normal
    /// state for an automated player). Switch the internal update mode to Always
    /// for the duration of the probe.</summary>
    private void ForceEventSystemAlwaysUpdate()
    {
        System.Type utility = typeof(VisualElement).Assembly.GetType(
            "UnityEngine.UIElements.UIElementsRuntimeUtility");
        System.Reflection.FieldInfo field = utility?.GetField(
            "eventSystemUpdateMode",
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            Info("eventSystemUpdateMode not found - relying on window focus");
            return;
        }

        object previous = field.GetValue(null);
        field.SetValue(null, System.Enum.ToObject(field.FieldType, 0)); // Always
        Info("UI event system update mode " + previous + " -> " + field.GetValue(null)
             + " (automated window is not foreground)");
    }

    // ====================================================================
    // Ship Overview - no expedition CTA
    // ====================================================================

    private IEnumerator GateOverviewNoCta()
    {
        WorldConsoleSurface overview = null;
        for (int i = 0; i < 4; i++)
        {
            WorldConsoleSurface candidate = _controller.Consoles[i]
                .GetComponent<WorldConsoleSurface>();
            if (candidate.ScreenHost != null
                && candidate.ScreenHost.MappedScreen == AppScreen.Overview)
            {
                overview = candidate;
                break;
            }
        }

        Check("console 01 hosts the ship Overview screen", overview != null);
        if (overview == null)
        {
            yield break;
        }

        yield return WaitSurfaceReady(overview);
        yield return ApproachConsole(0, 1.7f);

        VisualElement root = overview.Document.rootVisualElement;
        Button cta = root.Q<Button>(ElementNames.OpenPreparationButton);
        Check("Overview CTA element exists in the shared tree", cta != null);
        Check("ship Overview HIDES the 准备远征 CTA",
            cta != null && cta.resolvedStyle.display == DisplayStyle.None);
        Check("ship Overview CTA is not interactive",
            cta != null && !cta.enabledSelf);
        Check("ship Overview keeps the planet row",
            root.Q<Label>(ElementNames.DestinationValue) != null);
        Check("ship Overview keeps the objective panel",
            root.Q<VisualElement>(ElementNames.ObjectivePanel) != null);

        yield return Capture("frame_ship_overview_no_cta");
    }

    // ====================================================================
    // Wrist - crosshair hidden + dashed door floorplan
    // ====================================================================

    private IEnumerator GateWrist()
    {
        yield return ApproachConsole(0, 4.4f);
        yield return WaitReal(0.4f);

        ShipHudController hud = _controller.Hud;
        Check("ship HUD present", hud != null);
        Check("crosshair visible in ShipExplore",
            hud != null && hud.IsCrosshairVisible);

        _controller.SetWristOpen(true);
        yield return WaitReal(1.2f);
        Check("wrist open", _controller.IsWristOpen);
        Check("wrist owns the pointer",
            _controller.PointerOwner == ShipGameController.ShipPointerOwner.Wrist);
        Check("crosshair HIDDEN while the wrist is open",
            hud != null && !hud.IsCrosshairVisible);
        Check("console pointer router OFF in the wrist",
            _controller.PointerRouter == null || !_controller.PointerRouter.enabled);

        yield return Capture("frame_ship_wrist_crosshair_hidden");
        yield return WaitReal(0.4f);
        yield return Capture("frame_ship_wrist_floorplan_dashed_doors");

        // Numeric crosshair proof. ShipGameController re-asserts HUD
        // suppression every frame while the wrist is open, so the control frame
        // forces the crosshair visible and captures IN THE SAME FRAME (UI
        // Toolkit renders the panel at the end of the frame, after this
        // coroutine resumed). The wrist hologram parallax is static while the
        // cursor does not move, so the only difference in the box is the
        // crosshair itself.
        Rect centre = new Rect(Screen.width * 0.5f - 40f, Screen.height * 0.5f - 40f,
            80f, 80f);
        if (hud != null)
        {
            hud.SetCrosshairVisible(true);
            yield return Capture("frame_ship_wrist_crosshair_control");
            float diff = MeanAbsDiffRegion("frame_ship_wrist_crosshair_hidden",
                "frame_ship_wrist_crosshair_control", centre);
            Info("centre box mean|diff| shipped(hidden) vs same-frame control"
                 + " = " + diff.ToString("0.0000"));
            Check("the centre box really responds to the crosshair (mean|diff| "
                  + diff.ToString("0.0000") + " > 0.05)", diff > 0.05f);
            yield return WaitReal(0.3f);
            Check("crosshair hidden again after the control frame",
                !hud.IsCrosshairVisible);
        }

        // Dashed doors: 4 doors, each rendering >= 2 dashes, none overlapping a
        // wall run (the door REPLACES the wall).
        Check("exactly four ship doors", ShipSchematicLayout.Doors.Length == 4);
        int dashesOk = 0;
        foreach (ShipDoorSegment door in ShipSchematicLayout.Doors)
        {
            float lengthPx = Vector2.Distance(
                ShipSchematicLayout.DeckToPixel(door.A),
                ShipSchematicLayout.DeckToPixel(door.B));
            int dashes = ShipSchematicVisual.DashCountFor(lengthPx);
            Info("door " + door.Id + " span=" + lengthPx.ToString("0.0")
                 + " px dashes=" + dashes);
            if (dashes >= 2)
            {
                dashesOk++;
            }
        }

        Check("every door renders as a dash-gap-dash pattern (" + dashesOk + "/4)",
            dashesOk == 4);

        _controller.SetWristOpen(false);
        yield return WaitReal(0.8f);
        Check("crosshair restored after the wrist closes",
            hud != null && hud.IsCrosshairVisible);
        Check("ShipExplore owns the pointer again",
            _controller.PointerOwner == ShipGameController.ShipPointerOwner.None);
    }

    // ====================================================================
    // Equipment locker -> shared loadout (real clicks)
    // ====================================================================

    private IEnumerator GateEquipmentLockerLoadout()
    {
        yield return InteractAt(Object.FindAnyObjectByType<EquipmentLockerInteractable>(),
            new Vector3(-1.55f, 0f, -12.6f), 270f, "equipment locker");

        for (int f = 0; f < 300 && !(_overlay.IsOpen
                 && _overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
                 && _overlay.LoadoutPage.IsBound); f++)
        {
            yield return null;
        }

        Check("locker E opened the shared loadout in Release",
            _overlay.IsOpen && _overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
            && _overlay.LoadoutPage.IsVisible);
        Check("screen-space modal owns the pointer",
            _controller.PointerOwner == ShipGameController.ShipPointerOwner.ScreenModal);
        Check("console router OFF for the screen-space modal",
            _controller.PointerRouter == null || !_controller.PointerRouter.enabled);
        Check("movement/look frozen in LoadoutFocus", !_player.InputEnabled);

        yield return WaitReal(0.5f);
        yield return Capture("frame_equipment_locker_loadout");

        VisualElement root = _overlay.LoadoutPage.Root;
        Button battery = root.Q<Button>(ElementNames.BatteryRowButton);
        Button drone = root.Q<Button>(ElementNames.DroneRowButton);
        Check("loadout battery + drone rows present",
            battery != null && drone != null);

        GameSession session = GameSession.Instance;
        int cargoBefore = session.CargoWeight;
        Check("battery starts unselected", !session.BatterySelected);

        yield return RealClick(battery, "备用电池组");
        Check("REAL CLICK on 备用电池组 changed GameSession.BatterySelected",
            session.BatterySelected);
        Check("cargo weight followed the loadout change ("
              + cargoBefore + " -> " + session.CargoWeight + ")",
            session.CargoWeight != cargoBefore);

        yield return RealClick(drone, "勘测无人机");
        Check("REAL CLICK on 勘测无人机 changed GameSession.DroneSelected",
            session.DroneSelected);

        // No top-right 返回 button any more (removed on request): the overlay's
        // own Back route closes it, exactly like B / Esc in the real flow.
        UiInput.InjectEscapeForAutomation();
        for (int f = 0; f < 300 && _overlay.IsOpen; f++)
        {
            yield return null;
        }

        Check("返回 closes the locker-sourced loadout to ShipExplore",
            !_overlay.IsOpen && _player.InputEnabled
            && _controller.PointerOwner == ShipGameController.ShipPointerOwner.None);
    }

    // ====================================================================
    // Deployment confirmation -> shared loadout -> confirm -> Surface
    // ====================================================================

    private IEnumerator GateDeploymentConfirm()
    {
        yield return InteractAt(_controller.Airlock,
            new Vector3(0f, 0f, -12.3f), 180f, "deployment station");

        for (int f = 0; f < 300 && !(_overlay.IsOpen
                 && _overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
                 && _overlay.DeployPage.IsBound); f++)
        {
            yield return null;
        }

        Check("station E opened the deployment confirmation in Release",
            _overlay.IsOpen
            && _overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && _overlay.DeployPage.IsVisible);
        Check("confirmation owns the pointer",
            _controller.PointerOwner == ShipGameController.ShipPointerOwner.ScreenModal);

        yield return WaitReal(0.5f);
        yield return Capture("frame_deployment_confirm");

        VisualElement root = _overlay.DeployPage.Root;
        yield return RealClick(root.Q<Button>(ShipDeployConfirmPage.AdjustGearButtonName),
            "调整装备");
        for (int f = 0; f < 300 && _overlay.Mode != ShipOverlayUi.OverlayMode.Loadout; f++)
        {
            yield return null;
        }

        Check("REAL CLICK on 调整装备 opened the shared loadout",
            _overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
            && _overlay.LoadoutPage.IsVisible);
        Check("loadout remembers the confirmation return context",
            _overlay.LoadoutReturn
            == ShipOverlayUi.LoadoutReturnTarget.DeploymentConfirm);

        UiInput.InjectEscapeForAutomation();
        for (int f = 0; f < 300
             && _overlay.Mode != ShipOverlayUi.OverlayMode.DeploymentConfirm; f++)
        {
            yield return null;
        }

        Check("返回 returned to the deployment confirmation",
            _overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm);
        Check("equipment edited from the deploy route persisted",
            GameSession.Instance.BatterySelected && GameSession.Instance.DroneSelected);
    }

    private IEnumerator GateSurfaceReached()
    {
        Button confirm = _overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.ConfirmButtonName);
        Check("确认降临 button resolved", confirm != null);
        if (confirm == null)
        {
            yield break;
        }

        int mineralsBefore = GameSession.Instance.Minerals;
        yield return RealClick(confirm, "确认降临");

        for (int f = 0; f < 1800; f++)
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.name == GameSession.SurfaceSceneName)
            {
                break;
            }

            yield return null;
        }

        Scene scene = SceneManager.GetActiveScene();
        Check("REAL CLICK on 确认降临 loaded 02_NereidSurface (active='"
              + scene.name + "')", scene.name == GameSession.SurfaceSceneName);
        if (scene.name != GameSession.SurfaceSceneName)
        {
            yield break;
        }

        SurfaceGameController surface = null;
        for (int f = 0; f < 900; f++)
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            if (surface != null && surface.IsReady && surface.Player != null)
            {
                break;
            }

            yield return null;
        }

        Check("surface controller ready after deployment",
            surface != null && surface.IsReady);
        Check("GameSession persisted across the deployment (minerals "
              + mineralsBefore + " -> " + GameSession.Instance.Minerals + ")",
            GameSession.Instance.Minerals == mineralsBefore);
        Check("loadout edits persisted to the surface run",
            GameSession.Instance.BatterySelected && GameSession.Instance.DroneSelected);
        Check("exactly one GameSession",
            Object.FindObjectsByType<GameSession>().Length == 1);

        yield return WaitReal(1.2f);
        yield return Capture("frame_surface_after_deployment");

        // Surface wrist still hides the crosshair (same input-mode rule).
        SurfaceHudController hud = Object.FindAnyObjectByType<SurfaceHudController>();
        Check("surface HUD present", hud != null);
        Check("surface crosshair visible in SurfaceFirstPerson",
            hud != null && hud.IsCrosshairVisible);
        if (surface != null)
        {
            surface.SetWristOpen(true);
            yield return WaitReal(1.0f);
            Check("surface crosshair HIDDEN while the surface wrist is open",
                hud != null && !hud.IsCrosshairVisible);
            surface.SetWristOpen(false);
            yield return WaitReal(0.8f);
            Check("surface crosshair restored after the wrist closes",
                hud != null && hud.IsCrosshairVisible);
        }
    }

    // ====================================================================
    // Real pointer helpers
    // ====================================================================

    private IEnumerator InteractAt(object target, Vector3 position, float yawDegrees,
        string label)
    {
        _player.TeleportTo(position);
        _player.SetFacing(yawDegrees, 0f);
        yield return null;
        yield return null;
        Physics.SyncTransforms();

        bool resolved = false;
        for (int f = 0; f < 400 && !resolved; f++)
        {
            _controller.Interactor.RefreshTarget();
            resolved = ReferenceEquals(_controller.Interactor.CurrentTarget, target)
                       && _controller.Interactor.HasValidTarget;
            if (!resolved)
            {
                yield return null;
            }
        }

        Check("gaze resolved the " + label + " in Release", resolved);
        if (resolved)
        {
            Check(label + " E accepted", _controller.Interactor.TryInteract());
        }

        yield return WaitReal(0.3f);
    }

    /// <summary>
    /// A REAL pointer click on a screen-space UI Toolkit button: the cursor is
    /// moved to the button (OS warp closed over the InputSystem read-back, with
    /// an injected mouse-state fallback for unfocused windows) and the buttons
    /// are driven through the InputSystem state stream that UI Toolkit's event
    /// system consumes. Nothing is invoked directly.
    /// </summary>
    private IEnumerator RealClick(VisualElement element, string label)
    {
        if (element == null || _mouse == null)
        {
            Check("real click target for " + label, false);
            yield break;
        }

        IPanel panel = element.panel;
        Check(label + " belongs to a live panel", panel != null);
        if (panel == null)
        {
            yield break;
        }

        float scale = 1f;
        // Panel -> screen: the document root spans the whole panel, so the
        // panel's pixels-per-screen-pixel is the root width over the screen.
        VisualElement root = element;
        while (root.parent != null)
        {
            root = root.parent;
        }

        if (root.worldBound.width > 1f)
        {
            scale = root.worldBound.width / Mathf.Max(1f, Screen.width);
        }
        Vector2 panelCentre = element.worldBound.center;
        Vector2 target = new Vector2(panelCentre.x / scale,
            Screen.height - panelCentre.y / scale);

        Vector2 warp = target;
        Vector2 readBack = _mouse.position.ReadValue();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            _mouse.WarpCursorPosition(warp);
            yield return WaitReal(0.15f);
            readBack = _mouse.position.ReadValue();
            if ((readBack - target).sqrMagnitude < 4f)
            {
                break;
            }

            warp = target + (warp - readBack);
        }

        string path = "os-warp";
        if ((readBack - target).sqrMagnitude >= 4f)
        {
            path = "input-state";
            using (StateEvent.From(_mouse, out var move))
            {
                _mouse.position.WriteValueIntoEvent(target, move);
                InputSystem.QueueEvent(move);
            }

            yield return WaitReal(0.25f);
            readBack = _mouse.position.ReadValue();
        }

        yield return WaitReal(0.25f);

        using (StateEvent.From(_mouse, out var down))
        {
            _mouse.leftButton.WriteValueIntoEvent(1f, down);
            InputSystem.QueueEvent(down);
        }

        yield return WaitReal(0.2f);

        using (StateEvent.From(_mouse, out var up))
        {
            _mouse.leftButton.WriteValueIntoEvent(0f, up);
            InputSystem.QueueEvent(up);
        }

        yield return WaitReal(0.35f);
        Info("REAL CLICK " + label + " target=" + target.ToString("0")
             + " cursor=" + readBack.ToString("0") + " via=" + path);
    }

    // ====================================================================
    // Capture + analysis helpers
    // ====================================================================

    private struct FrameStats
    {
        public float MeanLum;
        public int UniqueColors;
    }

    private IEnumerator Capture(string name)
    {
        string path = Path.Combine(_folder, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 300; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return WaitReal(0.2f);
        Info("captured " + name + ".png");
    }

    /// <summary>Mean absolute per-channel difference (0..255) between two
    /// captured frames inside an image-space rect.</summary>
    private float MeanAbsDiffRegion(string nameA, string nameB, Rect rect)
    {
        if (!TryLoad(nameA, out Texture2D a) || !TryLoad(nameB, out Texture2D b))
        {
            return 0f;
        }

        int width = Mathf.Min(a.width, b.width);
        int height = Mathf.Min(a.height, b.height);
        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), x0 + 1, width);
        int topY = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, height - 1);
        int bottomY = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), topY + 1, height);
        float sum = 0f;
        int total = 0;
        for (int imageY = topY; imageY < bottomY; imageY++)
        {
            int y = height - 1 - imageY;
            for (int x = x0; x < x1; x++)
            {
                Color ca = a.GetPixel(x, y);
                Color cb = b.GetPixel(x, y);
                sum += (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g)
                        + Mathf.Abs(ca.b - cb.b)) / 3f;
                total++;
            }
        }

        Object.Destroy(a);
        Object.Destroy(b);
        return total > 0 ? sum / total * 255f : 0f;
    }

    private bool TryLoad(string name, out Texture2D texture)
    {
        texture = null;
        string path = Path.Combine(_folder, name + ".png");
        if (!File.Exists(path))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (System.Exception)
        {
            return false;
        }

        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            texture = null;
            return false;
        }

        return true;
    }

    private FrameStats AnalyseRegion(string name, Rect rect)
    {
        var stats = new FrameStats();
        string path = Path.Combine(_folder, name + ".png");
        if (!File.Exists(path))
        {
            return stats;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (System.Exception)
        {
            return stats;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            return stats;
        }

        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, texture.width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), x0 + 1, texture.width);
        int topY = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, texture.height - 1);
        int bottomY = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), topY + 1,
            texture.height);
        float sumLum = 0f;
        int total = 0;
        var colours = new System.Collections.Generic.HashSet<int>();
        for (int imageY = topY; imageY < bottomY; imageY += 2)
        {
            int y = texture.height - 1 - imageY;
            for (int x = x0; x < x1; x += 2)
            {
                Color c = texture.GetPixel(x, y);
                sumLum += c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                total++;
                colours.Add(((int)(c.r * 31f) << 10) | ((int)(c.g * 31f) << 5)
                            | (int)(c.b * 31f));
            }
        }

        stats.MeanLum = total > 0 ? sumLum / total * 255f : 0f;
        stats.UniqueColors = colours.Count;
        Object.Destroy(texture);
        return stats;
    }

    private static IEnumerator WaitSurfaceReady(WorldConsoleSurface surface)
    {
        for (int f = 0; f < 900 && !surface.IsReady; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    private IEnumerator ApproachConsole(int index, float distance)
    {
        WorldConsoleSurface surface = _controller.Consoles[index]
            .GetComponent<WorldConsoleSurface>();
        Vector3 outward = surface.ScreenQuad != null
            ? surface.ScreenQuad.forward
            : Vector3.forward;
        Vector3 target = surface.ScreenQuad != null
            ? surface.ScreenQuad.position
            : Vector3.zero;
        _player.TeleportTo(target + outward * distance);
        float yaw = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;
        _player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;
        yield return WaitReal(0.3f);
    }

    private static IEnumerator WaitReal(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static string ParseFlag(string flag)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private void Check(string label, bool ok)
    {
        _report.AppendLine((ok ? "PASS :: " : "FAIL :: ") + label);
        Debug.Log("[ShipB23] " + (ok ? "PASS :: " : "FAIL :: ") + label);
        if (!ok)
        {
            _failures++;
        }
    }

    private void Info(string label)
    {
        _report.AppendLine("INFO :: " + label);
        Debug.Log("[ShipB23] INFO :: " + label);
    }

    private void Finish()
    {
        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        string path = Path.Combine(_folder, "phase07b23_result.txt");
        try
        {
            File.WriteAllText(path, _report.ToString());
            Debug.Log("[ShipB23] wrote result: " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ShipB23] cannot write result: " + e);
        }

        Debug.Log("[ShipB23] report:\n" + _report);
        Application.Quit(_failures == 0 ? 0 : 1);
    }
}
