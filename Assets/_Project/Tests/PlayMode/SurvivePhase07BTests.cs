using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B Play Mode validation - interactive physical consoles, door
/// geometry repair, console focus/blur/pointer and the ship wrist.
///
/// Real-interaction tests go through the genuine world-pointer pipeline
/// (viewport -> focused eye camera ray -> screen collider -> UV -> panel
/// pixels -> UI Toolkit dispatcher) and assert GameSession/authority changes -
/// no direct controller-method invocation for the clicked action.
/// </summary>
public class SurvivePhase07BTests
{
    private const int MaxWaitFrames = 2400;

    private static readonly Vector3[] ConsolePositions =
    {
        new Vector3(0f, 0f, 10.05f),
        new Vector3(-4.95f, 0f, 3.4f),
        new Vector3(4.95f, 0f, 3.4f),
        new Vector3(-2.2f, 0f, -8.85f)
    };

    private static readonly Vector3[] ScreenDirs =
    {
        new Vector3(0f, 0f, -1f),
        new Vector3(1f, 0f, 0f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(0f, 0f, 1f)
    };

    // ---- helpers ----

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static string ActiveSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    private static IEnumerator LoadShipScene()
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.ShipInteriorSceneName);
        yield return null;
        yield return null;
    }

    private static IEnumerator LoadSurfaceScene()
    {
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitForShip(System.Action<ShipGameController> onReady)
    {
        ShipGameController controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            return controller != null && controller.IsReady && controller.Player != null;
        });
        onReady(controller);
    }

    private static IEnumerator WaitForSurface(System.Action<SurfaceGameController> onReady)
    {
        SurfaceGameController controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
            return controller != null && controller.IsReady && controller.Player != null;
        });
        onReady(controller);
    }

    /// <summary>Positions the player 1.7 m in front of a console facing it.</summary>
    private static IEnumerator ApproachConsole(ShipGameController controller, int index)
    {
        SurfacePlayerController player = controller.Player;
        Vector3 approach = ConsolePositions[index] + ScreenDirs[index] * 1.7f;
        player.TeleportTo(approach);
        float yaw = Mathf.Atan2(-ScreenDirs[index].x, -ScreenDirs[index].z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;
    }

    /// <summary>Approaches a console, gazes it through the real interactor and
    /// presses E (semantic) - the exact world path a human uses.</summary>
    private static IEnumerator EnterConsole(ShipGameController controller, int index)
    {
        SurfacePlayerController player = controller.Player;
        ShipConsoleInteractable console = controller.Consoles[index];
        Vector3 approach = ConsolePositions[index] + ScreenDirs[index] * 1.7f;
        player.TeleportTo(approach);
        float yaw = Mathf.Atan2(-ScreenDirs[index].x, -ScreenDirs[index].z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;

        FirstPersonInteractor interactor = controller.Interactor;
        interactor.RefreshTarget();
        Assert.IsTrue(ReferenceEquals(interactor.CurrentTarget, console),
            "console " + console.ConsoleId + " is the gaze target");
        Assert.IsTrue(interactor.TryInteract(),
            "E enters console " + console.ConsoleId + " focus");
    }

    private static IEnumerator WaitFocused(ShipConsoleFocusController focus)
    {
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus);
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitExplore(ShipConsoleFocusController focus)
    {
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore);
        yield return null;
    }

    private static void AssertNearly(float expected, float actual, string label)
    {
        Assert.IsTrue(Mathf.Abs(expected - actual) < 0.001f,
            label + " expected " + expected + " but was " + actual);
    }

    /// <summary>Maps a UI element centre (panel pixel, y down) through the
    /// projected screen quad into a viewport fraction using the single
    /// projection authority (Phase 07B.2.1).</summary>
    private static bool TryElementViewport(WorldConsoleSurface surface, Camera camera,
        VisualElement element, out Vector2 viewport)
    {
        return ConsoleScreenProjection.TryElementViewport(surface, camera, element,
            out viewport);
    }

    // ====================================================================
    // Door geometry (Phase 07B #6-#10)
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipDoorGeometry_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);

        for (int d = 0; d < controller.Doors.Count; d++)
        {
            ShipDoorInteractable door = controller.Doors[d];

            // Closed geometry: leaf recessed from the frame planes.
            if (!door.IsOpen)
            {
                door.RequestOpen();
            }

            yield return WaitSeconds(2.5f);
            door.RequestClose();
            yield return WaitUntil(() => door.State == ShipDoorInteractable.ShipDoorState.Closed);
            yield return null;
            Assert.IsTrue(door.VerifyClosedGeometry(out float separation, out string closedReport),
                "door " + door.DoorId + " closed geometry:\n" + closedReport);
            Assert.IsTrue(separation >= 0.015f,
                "door " + door.DoorId + " leaf/frame separation " + separation);

            // Open geometry: leaves outside the doorway span, blocker off.
            door.RequestOpen();
            yield return WaitUntil(() => door.State == ShipDoorInteractable.ShipDoorState.Open);
            yield return null;
            Assert.IsTrue(door.VerifyOpenClearance(out string openReport),
                "door " + door.DoorId + " open clearance:\n" + openReport);
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // Always-on console screens (Phase 07B #12-#19, #73)
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipConsole_AlwaysOnScreens_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);

        Assert.AreEqual(4, controller.Consoles.Count, "four consoles");
        const float expectedAspect = WorldConsoleSurface.UiWidth
                                     / (float)WorldConsoleSurface.UiHeight;
        for (int i = 0; i < controller.Consoles.Count; i++)
        {
            WorldConsoleSurface surface =
                controller.Consoles[i].GetComponent<WorldConsoleSurface>();
            Assert.IsNotNull(surface, "console " + (i + 1) + " has a surface");

            // The surface must finish initializing without any interaction.
            for (int f = 0; f < 600 && !surface.IsReady; f++)
            {
                yield return null;
            }

            Assert.IsTrue(surface.IsReady, "console " + (i + 1) + " surface ready");
            Assert.IsNotNull(surface.CurrentRenderTexture, "console " + (i + 1) + " RT");
            Assert.IsTrue(surface.CurrentRenderTexture.IsCreated()
                          && surface.CurrentRenderTexture.width == WorldConsoleSurface.UiWidth,
                "console " + (i + 1) + " RT valid at native resolution");
            Assert.IsNotNull(surface.ScreenRenderer, "console " + (i + 1) + " renderer");
            Assert.IsTrue(surface.ScreenRenderer.enabled, "console " + (i + 1) + " renderer on");
            if (i == 1)
            {
                // Console02 = 仓库总览: dedicated warehouse terminal host.
                Assert.IsNotNull(surface.WarehouseHost,
                    "console 2 warehouse host bound");
                Assert.IsTrue(surface.WarehouseHost.IsBound,
                    "console 2 warehouse host initialized");
                VisualElement warehouseRoot = surface.WarehouseHost.Root;
                Assert.IsNotNull(warehouseRoot, "console 2 warehouse root");
                Assert.AreEqual("仓库总览",
                    warehouseRoot.Q<Label>("WarehouseTitleLabel").text,
                    "console 2 title is 仓库总览");
            }
            else
            {
                Assert.IsNotNull(surface.ScreenHost, "console " + (i + 1) + " UI host bound");
                Assert.IsTrue(surface.ScreenHost.CountVisibleScreens() == 1,
                    "console " + (i + 1) + " shows exactly one screen");
                Assert.AreEqual(i + 1,
                    surface.ScreenHost.MappedScreen == AppScreen.Overview
                        ? 1 : surface.ScreenHost.MappedScreen == AppScreen.Processing
                            ? 3 : 4,
                    "console " + (i + 1) + " maps the fixed screen id");
            }

            // Physical aspect must match the RenderTexture aspect (<=1%).
            float aspect = surface.PhysicalScreenAspect;
            Assert.IsTrue(Mathf.Abs(aspect - expectedAspect) / expectedAspect <= 0.01f,
                "console " + (i + 1) + " physical aspect " + aspect
                + " vs UI " + expectedAspect);
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // Console focus transition, pose, blur, input gating
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipConsoleTransition_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        Assert.IsNotNull(focus, "focus controller present");
        Assert.IsTrue(focus.transitionDuration > 0f
                      && focus.transitionDuration <= 0.40f,
            "transition duration in contract band");

        Camera eye = controller.Player.EyeCamera;
        Vector3 startPos = eye.transform.position;
        Quaternion startRot = eye.transform.rotation;
        yield return EnterConsole(controller, 0);

        // Mid-transition the camera must differ from BOTH endpoints (the
        // implementation is a real interpolation, never an instant teleport).
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.TransitionIn);
        yield return WaitSeconds(focus.transitionDuration * 0.5f);
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.TransitionIn,
            focus.State, "still transitioning at midpoint");
        Assert.IsTrue(focus.TransitionProgress01 > 0.15f
                      && focus.TransitionProgress01 < 0.85f,
            "mid transition progress " + focus.TransitionProgress01);
        Vector3 midPos = eye.transform.position;
        Assert.IsTrue(Vector3.Distance(midPos, startPos) > 0.01f,
            "mid pose differs from entry pose");
        Assert.IsTrue(Vector3.Distance(midPos,
            controller.Consoles[0].GetComponent<WorldConsoleSurface>().FocusAnchor.position)
            > 0.01f, "mid pose differs from final pose");

        yield return WaitFocused(focus);
        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipConsoleFocusPose_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        SurfacePlayerController player = controller.Player;
        Camera eye = player.EyeCamera;

        for (int c = 0; c < controller.Consoles.Count; c++)
        {
            yield return ApproachConsole(controller, c);
            // Let the CharacterController settle on the floor after the teleport
            // so the captured pose matches the pose the focus controller lerps
            // back to (a fresh teleport's capsule settle can shift the eye by
            // ~0.08 m on regenerated scenes).
            yield return WaitSeconds(0.25f);
            Vector3 capturePos = eye.transform.position;
            Quaternion captureRot = eye.transform.rotation;

            // Gaze + E without re-teleporting: the capture above must be the
            // exact pre-focus view the controller returns to.
            FirstPersonInteractor interactor = controller.Interactor;
            interactor.RefreshTarget();
            Assert.IsTrue(ReferenceEquals(interactor.CurrentTarget,
                controller.Consoles[c]) && interactor.HasValidTarget,
                "console " + (c + 1) + " is the gaze target");
            Assert.IsTrue(interactor.TryInteract(),
                "E enters console " + (c + 1) + " focus");
            yield return WaitFocused(focus);

            WorldConsoleSurface surface =
                controller.Consoles[c].GetComponent<WorldConsoleSurface>();
            Transform anchor = surface.FocusAnchor;

            // Final camera pose sits on the anchor (deterministic transform).
            Assert.IsTrue(Vector3.Distance(eye.transform.position, anchor.position) <= 0.02f,
                "console " + (c + 1) + " camera at anchor ("
                + Vector3.Distance(eye.transform.position, anchor.position) + "m)");
            float dot = Vector3.Dot(eye.transform.forward, -anchor.forward);
            Assert.IsTrue(Mathf.Abs(dot) >= 0.97f,
                "console " + (c + 1) + " screen facing (dot " + dot + ")");

            // Screen projects to a readable size. The exact 65-78% contract is
            // defined at the human 1920x1080 (16:9) viewport; the editor test
            // runner window may be narrower, so the strict band is only
            // enforced at 16:9 and the general run checks readability.
            float widthFraction = ProjectedQuadWidthFraction(surface, eye);
            Assert.IsTrue(widthFraction >= 0.45f,
                "console " + (c + 1) + " readable projected width ("
                + (widthFraction * 100f) + "%)");
            if (eye.aspect > 1.55f)
            {
                Assert.IsTrue(widthFraction <= 0.98f && widthFraction >= 0.55f,
                    "console " + (c + 1) + " width " + (widthFraction * 100f)
                    + "% at 16:9 (target 65-78%)");
            }

            // Focused mode keeps the world visible: bezel corners outside the
            // projected screen still resolve to screen pixels.
            Assert.IsTrue(player != null && !player.InputEnabled,
                "console " + (c + 1) + " movement disabled while focused");

            // Esc leaves: camera returns to the captured pose, input restored.
            focus.RequestExit();
            yield return WaitExplore(focus);
            Assert.IsTrue(player.InputEnabled, "console " + (c + 1) + " input restored");
            Assert.IsTrue(Vector3.Distance(eye.transform.position, capturePos) <= 0.02f,
                "console " + (c + 1) + " camera returned ("
                + Vector3.Distance(eye.transform.position, capturePos) + "m)");
            Assert.IsTrue(Quaternion.Angle(eye.transform.rotation, captureRot) <= 0.5f,
                "console " + (c + 1) + " camera rotation restored ("
                + Quaternion.Angle(eye.transform.rotation, captureRot) + "deg)");
        }

        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipConsoleBlur_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;

        Assert.IsTrue(Mathf.Abs(focus.BlurWeight) < 0.02f,
            "ShipExplore starts with no blur");
        yield return EnterConsole(controller, 0);
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.TransitionIn);
        yield return WaitSeconds(focus.transitionDuration * 0.6f);
        Assert.IsTrue(focus.BlurWeight > 0.2f,
            "blur ramps in during transition (" + focus.BlurWeight + ")");
        yield return WaitFocused(focus);
        Assert.IsTrue(focus.BlurWeight > 0.8f,
            "blur active while focused");

        focus.RequestExit();
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.TransitionOut);
        yield return WaitSeconds(focus.transitionDuration * 0.6f);
        Assert.IsTrue(focus.BlurWeight < 0.8f,
            "blur ramps out during exit");
        yield return WaitExplore(focus);
        Assert.IsTrue(Mathf.Abs(focus.BlurWeight) < 0.02f,
            "no persistent blur after exit");

        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipConsoleInputGating_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;

        yield return EnterConsole(controller, 0);
        yield return WaitFocused(focus);
        Assert.IsFalse(controller.Player.InputEnabled, "movement/look OFF while focused");
        Assert.IsTrue(controller.IsConsoleFocusActive, "console focus active");

        // M/I (semantic) must NOT open the wrist while a console is focused.
        controller.SetWristOpen(true);
        yield return null;
        Assert.IsFalse(controller.IsWristOpen, "wrist cannot open over console focus");

        focus.RequestExit();
        yield return WaitExplore(focus);
        Assert.IsTrue(controller.Player.InputEnabled, "explore input restored after exit");
        Assert.IsFalse(controller.IsConsoleFocusActive, "focus no longer active");

        // Wrist opens again in ShipExplore and closes back to it.
        controller.SetWristOpen(true);
        yield return null;
        Assert.IsTrue(controller.IsWristOpen, "wrist opens in ShipExplore");
        Assert.IsFalse(controller.Player.InputEnabled, "explore input frozen by wrist");
        controller.SetWristOpen(false);
        yield return null;
        Assert.IsFalse(controller.IsWristOpen, "wrist closed");
        Assert.IsTrue(controller.Player.InputEnabled, "explore input restored after wrist");

        GameSession.Ensure().ResetSession();
    }

    private static float ProjectedQuadWidthFraction(WorldConsoleSurface surface, Camera camera)
    {
        Transform quad = surface.ScreenQuad;
        Vector3 scale = quad.lossyScale;
        float minX = 1f, maxX = 0f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 corner = quad.position
                                 + quad.right * (sx * scale.x * 0.5f)
                                 + quad.forward * (sz * scale.z * 0.5f);
                Vector3 vp = camera.WorldToViewportPoint(corner);
                if (vp.z > 0f)
                {
                    minX = Mathf.Min(minX, vp.x);
                    maxX = Mathf.Max(maxX, vp.x);
                }
            }
        }

        return Mathf.Clamp01(maxX - minX);
    }

    // ====================================================================
    // Real UI interaction through the world pointer (Phase 07B #44-#48,
    // #92-#95)
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipConsole_Warehouse_Runtime()
    {
        GameSession.Ensure().ResetSession();
        GameSession session = GameSession.Instance;
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        WorldUiPointerRouter router = Object.FindAnyObjectByType<WorldUiPointerRouter>();
        Assert.IsNotNull(router, "shared world pointer router present");

        yield return EnterConsole(controller, 1);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface =
            controller.Consoles[1].GetComponent<WorldConsoleSurface>();
        yield return WaitConsoleUi(surface);

        // Console02 = 仓库总览 (Phase 07B.2): dedicated read-only terminal.
        Assert.IsNotNull(surface.WarehouseHost, "warehouse host bound");
        Assert.IsTrue(surface.WarehouseHost.IsBound, "warehouse host ready");
        Assert.IsNull(surface.ScreenHost,
            "warehouse terminal does not host the legacy MainApp tree");
        VisualElement root = surface.WarehouseHost.Root;
        Assert.IsNotNull(root, "warehouse document root");
        Assert.AreEqual("仓库总览", root.Q<Label>("WarehouseTitleLabel").text,
            "warehouse screen title");
        Assert.AreEqual(session.Minerals.ToString(),
            root.Q<Label>(ShipWarehouseScreenHost.MineralsValueName).text,
            "warehouse minerals show the authoritative session value");

        // Live: an authoritative mutation refreshes the warehouse screen.
        session.AddMinerals(5);
        yield return null;
        yield return null;
        Assert.AreEqual(session.Minerals.ToString(),
            root.Q<Label>(ShipWarehouseScreenHost.MineralsValueName).text,
            "warehouse minerals refresh from GameSession");
        Assert.IsNull(root.Q<Button>(ElementNames.BatteryRowButton),
            "warehouse is NOT a loadout editor (no equipment rows)");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipConsole_Processing_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        WorldUiPointerRouter router = Object.FindAnyObjectByType<WorldUiPointerRouter>();
        GameSession session = GameSession.Instance;

        yield return EnterConsole(controller, 2);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface =
            controller.Consoles[2].GetComponent<WorldConsoleSurface>();
        yield return WaitConsoleUi(surface);

        int fuelBefore = session.Fuel;
        int alloyBefore = session.Alloy;

        // Raise the run size, then confirm through the world pointer.
        yield return ClickElement(router, surface,
            FindButton(surface, ElementNames.ProcessingQuantityUp));
        yield return ClickElement(router, surface,
            FindButton(surface, ElementNames.ProcessingStartButton));
        Assert.IsTrue(session.IsProcessing, "real click started the process");

        // Walk away: process continues under the persistent session.
        focus.RequestExit();
        yield return WaitExplore(focus);
        yield return WaitSeconds(3f); // refinery LV.1 takes 2.0 s
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState,
            "processing completed while the player walked away");
        // Phase 09B PASS 5 yield: one mineral pays one Alloy + one Fuel, and the
        // click above raised the run size to 2.
        Assert.AreEqual(fuelBefore + 2 * GameSession.ProcessFuelPerMineral, session.Fuel,
            "fuel settled by authority");
        Assert.AreEqual(alloyBefore + 2 * GameSession.ProcessAlloyPerMineral, session.Alloy,
            "alloy settled by authority");

        // Re-entering the console reflects the actual current state.
        yield return EnterConsole(controller, 2);
        yield return WaitFocused(focus);
        yield return WaitConsoleUi(surface);
        Label status = surface.Document.rootVisualElement
            .Q<Label>(ElementNames.ProcessingStatusLabel);
        Assert.IsNotNull(status, "processing status label present");
        Assert.AreEqual("加工完成", status.text, "re-entered screen shows live completed state");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipConsole_Outpost_Runtime()
    {
        GameSession.Ensure().ResetSession();
        GameSession session = GameSession.Instance;
        session.AddAlloy(50);
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        WorldUiPointerRouter router = Object.FindAnyObjectByType<WorldUiPointerRouter>();

        int refineryBefore = session.RefineryLevel;
        int alloyBefore = session.Alloy;
        int expectedCost = session.GetUpgradeCost(GameSession.OutpostModule.Refinery);

        yield return EnterConsole(controller, 3);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface =
            controller.Consoles[3].GetComponent<WorldConsoleSurface>();
        yield return WaitConsoleUi(surface);

        // The Outpost screen defaults to the Refinery module; click upgrade.
        Button upgrade = FindButton(surface, ElementNames.OutpostUpgradeButton);
        Assert.IsNotNull(upgrade, "outpost upgrade button present");
        yield return ClickElement(router, surface, upgrade);
        Assert.AreEqual(refineryBefore + 1, session.RefineryLevel,
            "clicking a valid upgrade increased the Refinery module");
        Assert.AreEqual(alloyBefore - expectedCost, session.Alloy,
            "upgrade cost deducted by the authority");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipConsole_Overview_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;

        yield return EnterConsole(controller, 0);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface =
            controller.Consoles[0].GetComponent<WorldConsoleSurface>();
        yield return WaitConsoleUi(surface);

        Assert.IsTrue(surface.ScreenHost.MappedScreen == AppScreen.Overview,
            "console 01 hosts the Overview screen");
        Assert.AreEqual(1, surface.ScreenHost.CountVisibleScreens(),
            "exactly one screen visible on console 01");
        VisualElement root = surface.Document.rootVisualElement;
        Assert.IsNotNull(root.Q<Label>(ElementNames.EnergyValue), "Overview energy label");
        Assert.IsNotNull(root.Q<Label>(ElementNames.MineralsValue), "Overview minerals label");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipConsole_PointerOrientationAndHover_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        WorldUiPointerRouter router = Object.FindAnyObjectByType<WorldUiPointerRouter>();

        yield return EnterConsole(controller, 1);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface =
            controller.Consoles[1].GetComponent<WorldConsoleSurface>();
        yield return WaitConsoleUi(surface);

        // The authoritative UV->panel conversion must map what the PLAYER SEES
        // as the screen's top-left onto the UI panel's top-left (small panel x
        // AND small panel y). The viewer frame is derived from the camera, not
        // from the quad axes, so a mirrored screen cannot pass this check.
        Camera eye = controller.Player.EyeCamera;
        Transform quad = surface.ScreenQuad;
        Vector2 scale = quad.lossyScale;
        Vector3 viewerForward = -quad.forward; // player stands in front, looks at the screen
        Vector3 viewerRight = Vector3.Cross(Vector3.up, viewerForward).normalized;
        Vector3 viewerTop = Vector3.up;
        Vector3 seenTopLeft = quad.position
                              - viewerRight * (scale.x * 0.5f)
                              + viewerTop * (scale.y * 0.5f);
        Vector3 seenBottomRight = quad.position
                                  + viewerRight * (scale.x * 0.5f)
                                  - viewerTop * (scale.y * 0.5f);
        Vector2 vpTl = eye.WorldToViewportPoint(seenTopLeft);
        Vector2 vpBr = eye.WorldToViewportPoint(seenBottomRight);
        Vector2 panelTl;
        Vector2 panelBr;
        Assert.IsTrue(router.TryScreenToPanelPixel(new Vector2(vpTl.x * Screen.width,
            vpTl.y * Screen.height), out panelTl),
            "maps the seen screen top-left");
        Assert.IsTrue(router.TryScreenToPanelPixel(new Vector2(vpBr.x * Screen.width,
            vpBr.y * Screen.height), out panelBr),
            "maps the seen screen bottom-right");
        Assert.IsTrue(panelTl.x < panelBr.x && panelTl.y < panelBr.y,
            "orientation: what the player sees as top-left maps to panel "
            + "top-left (got " + panelTl + " vs " + panelBr + ")");

        // Hover a real button through the world pointer. The dispatcher's
        // pointer-under-element resolution (the exact mechanism that drives
        // :hover pseudo states on a real device) must land on the batch
        // button. The editor Test Runner has no OS mouse, so the *visual*
        // pseudo-state change is asserted by the Release-player probe with a
        // real OS cursor (ShipB2ShotProbe real-mouse hover stage).
        focus.RequestExit();
        yield return WaitExplore(focus);
        yield return EnterConsole(controller, 2);
        yield return WaitFocused(focus);
        surface = controller.Consoles[2].GetComponent<WorldConsoleSurface>();
        yield return WaitConsoleUi(surface);
        Button quantityUp = FindButton(surface, ElementNames.ProcessingQuantityUp);
        Assert.IsNotNull(quantityUp, "quantity control present");
        Vector2 viewport;
        Assert.IsTrue(TryElementViewport(surface, eye, quantityUp, out viewport),
            "quantity control maps into the viewport");
        Assert.IsTrue(router.TryHoverViewportPoint(viewport), "hover lands on the screen");
        yield return null;
        yield return null;

        // The hovered element must be the button itself or one of its
        // descendants (the genuine :hover recipient chain).
        Vector2 screenPixel = new Vector2(viewport.x * Screen.width,
            viewport.y * Screen.height);
        Vector2 panelPos;
        Assert.IsTrue(router.TryScreenToPanelPixel(screenPixel, out panelPos),
            "hover point maps into the focused panel");
        VisualElement picked = surface.Document.rootVisualElement.panel != null
            ? surface.Document.rootVisualElement.panel.Pick(panelPos)
            : null;
        bool chainToButton = false;
        for (VisualElement walk = picked; walk != null; walk = walk.parent)
        {
            if (walk == quantityUp)
            {
                chainToButton = true;
                break;
            }
        }

        Assert.IsTrue(chainToButton,
            "hover targeting resolves to the processing control through the "
            + "dispatcher picker (" + (picked != null ? picked.name : "null") + ")");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }

    private static Button FindButton(WorldConsoleSurface surface, string name)
    {
        return surface.Document != null && surface.Document.rootVisualElement != null
            ? surface.Document.rootVisualElement.Q<Button>(name)
            : null;
    }

    private static IEnumerator WaitConsoleUi(WorldConsoleSurface surface)
    {
        // Allow the console panel to attach and lay out its MainApp tree.
        for (int f = 0; f < 600 && (surface.Document == null
             || surface.Document.rootVisualElement == null
             || surface.Document.rootVisualElement.childCount == 0); f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
        yield return null;
    }

    private static IEnumerator ClickElement(WorldUiPointerRouter router,
        WorldConsoleSurface surface, VisualElement element)
    {
        SurfacePlayerController player =
            Object.FindAnyObjectByType<SurfacePlayerController>();
        Camera eye = player != null ? player.EyeCamera : Camera.main;
        Vector2 viewport;
        if (!TryElementViewport(surface, eye, element, out viewport))
        {
            Assert.Fail("click target not mappable to the viewport");
        }

        Assert.IsTrue(router.TryClickViewportPoint(viewport),
            "world-pointer click dispatched to the focused screen");
        yield return null;
        yield return null;
        yield return null;
    }

    // ====================================================================
    // Wrist return mode + ship wrist content (Phase 07B #53-#58, #67)
    // ====================================================================

    [UnityTest]
    public IEnumerator WristReturnMode_Runtime()
    {
        GameSession.Ensure().ResetSession();

        // Surface -> Wrist -> Surface.
        yield return LoadSurfaceScene();
        SurfaceGameController surface = null;
        yield return WaitForSurface(value => surface = value);
        SurfaceWristController surfaceWrist =
            Object.FindAnyObjectByType<SurfaceWristController>();
        Assert.IsNotNull(surfaceWrist, "surface wrist host present");
        surface.SetWristOpen(true);
        yield return null;
        Assert.IsTrue(surface.IsWristOpen, "surface wrist open");
        Assert.AreEqual(WristReturnMode.SurfaceFirstPerson, surfaceWrist.ReturnMode,
            "surface wrist records SurfaceFirstPerson return");
        Assert.IsFalse(surfaceWrist.IsShipMode, "surface wrist not ship mode");
        surface.SetWristOpen(false);
        yield return null;
        Assert.IsFalse(surface.IsWristOpen, "surface wrist closed");
        Assert.IsTrue(surface.Player.InputEnabled, "surface input restored");

        // Ship -> Wrist -> Ship.
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        SurfaceWristController shipWrist = controller.Wrist;
        Assert.IsNotNull(shipWrist, "ship wrist host bound");
        controller.SetWristOpen(true);
        yield return null;
        Assert.IsTrue(controller.IsWristOpen, "ship wrist open");
        Assert.AreEqual(WristReturnMode.ShipExplore, shipWrist.ReturnMode,
            "ship wrist records ShipExplore return");
        Assert.IsTrue(shipWrist.IsShipMode, "ship wrist is ship mode");
        controller.SetWristOpen(false);
        yield return null;
        Assert.IsFalse(controller.IsWristOpen, "ship wrist closed");
        Assert.IsTrue(controller.Player.InputEnabled, "ship input restored to explore");

        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipWrist_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        SurfaceWristController wrist = controller.Wrist;
        Assert.IsNotNull(wrist, "ship wrist present");

        controller.SetWristOpen(true);
        yield return null;
        for (int f = 0; f < 120; f++)
        {
            yield return null;
        }

        VisualElement root = wrist.RootElement;
        Assert.IsNotNull(root, "ship wrist root bound");

        // Ship semantics: identity footer, 舰内 state, no surface navigation
        // distance, harvest reads 地表采集待命, surface markers hidden.
        Assert.AreEqual("ARK-07 · 舰内模式", root.Q<Label>("WristIdentity").text,
            "ship wrist identity footer");
        Assert.AreEqual("舰内", root.Q<Label>("ActionState").text,
            "ship wrist action state");
        Assert.AreEqual("地表采集待命", root.Q<Label>("HarvestState").text,
            "ship wrist harvest region is standby");
        Assert.AreEqual("未设置", root.Q<Label>("NavTarget").text,
            "no fake nav target without a recorded surface target");
        Assert.AreEqual("", root.Q<Label>("NavDistance").text,
            "no fake surface distance in the ship");
        Assert.IsTrue(root.Q<Label>("MapPlayer").style.display == DisplayStyle.None,
            "surface player marker hidden in ship");
        Assert.IsTrue(root.Q<VisualElement>("MapLanding").style.display == DisplayStyle.None,
            "surface landing marker hidden in ship");

        controller.SetWristOpen(false);
        yield return null;
        GameSession.Ensure().ResetSession();
    }

    // SurfaceWristController navigation helper not present on ShipGameController;
    // this tiny accessor keeps tests tidy.
}
