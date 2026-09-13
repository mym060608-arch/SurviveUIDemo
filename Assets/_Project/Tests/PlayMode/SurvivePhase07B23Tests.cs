using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.3 acceptance - the two blockers that stopped the whole Surface
/// flow, plus the ship-overview / wrist polish:
///
///  - ScreenModalPointer_Runtime: the shipboard SCREEN-SPACE modals (deployment
///    confirmation, shared loadout) must be the UI Toolkit input target. Before
///    this round UI Toolkit routed every screen-space pointer event in the ship
///    to a console RenderTexture panel, so no modal button could ever be
///    clicked, while the console-less legacy Hub worked.
///  - EquipmentLockerRealClick_Runtime / DeploymentConfirmRealClick_Runtime:
///    the real click chains through the native UI Toolkit path and the shared
///    GameSession loadout, including the actual Surface routing.
///  - ShipOverview_NoExpeditionCTA_Runtime: the ship's physical Overview
///    terminal hides the legacy 准备远征 CTA (ship context only).
///  - WristCrosshairVisibility_Runtime: the first-person crosshair is hidden
///    whenever the wrist owns the pointer (ship and surface).
///  - ShipSchematicDoorVisual_Runtime: the four doors render as dashes that
///    replace the wall, with the wall/door/icon hierarchy intact.
/// </summary>
public class SurvivePhase07B23Tests
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

    /// <summary>Waits in real time: the landing hold is measured in seconds.</summary>
    private static IEnumerator WaitSeconds(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition,
        int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private static string ActiveSceneName()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "<none>";
    }

    private static IEnumerator LoadScene(string name)
    {
        SceneManager.LoadScene(name);
        yield return WaitUntil(() => ActiveSceneName() == name);
        yield return null;
        yield return null;
        yield return null;
    }

    private static IEnumerator LoadShipScene()
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        yield return LoadScene(GameSession.ShipInteriorSceneName);
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

    private static IEnumerator WaitBound(VisualElement root)
    {
        yield return WaitUntil(() => root != null && root.panel != null
                                     && root.childCount > 0);
        yield return null;
        yield return null;
    }

    /// <summary>
    /// Deterministic interaction: stand at the documentented pose for the target
    /// (the same poses the Phase 07B.2 acceptance uses), assert the gaze really
    /// resolved THAT target, then interact.
    /// </summary>
    private static IEnumerator InteractAt(ShipGameController controller,
        object target, Vector3 position, float yawDegrees, string label)
    {
        controller.Player.TeleportTo(position);
        controller.Player.SetFacing(yawDegrees, 0f);
        yield return null;
        yield return null;
        Physics.SyncTransforms();

        bool resolved = false;
        for (int f = 0; f < 300 && !resolved; f++)
        {
            controller.Interactor.RefreshTarget();
            resolved = ReferenceEquals(controller.Interactor.CurrentTarget, target)
                       && controller.Interactor.HasValidTarget;
            if (!resolved)
            {
                yield return null;
            }
        }

        Assert.IsTrue(resolved, "gaze resolved the " + label + " from its authored "
                                + "pose (target=" + (target != null
                                    ? target.GetType().Name : "<null>") + ")");
        Assert.IsTrue(controller.Interactor.TryInteract(),
            "E on the " + label + " was accepted");
        yield return null;
        yield return null;
    }

    /// <summary>The authored Airlock poses (mirror the 07B.2 acceptance).</summary>
    private static IEnumerator InteractWithLocker(ShipGameController controller)
    {
        yield return InteractAt(controller,
            Object.FindAnyObjectByType<EquipmentLockerInteractable>(),
            new Vector3(-1.55f, 0f, -12.6f), 270f, "equipment locker");
    }

    private static IEnumerator InteractWithDeploymentStation(
        ShipGameController controller)
    {
        yield return InteractAt(controller, controller.Airlock,
            new Vector3(0f, 0f, -12.3f), 180f, "deployment station");
    }

    /// <summary>Panel-space centre -> screen-space (origin bottom-left).</summary>
    private static Vector2 PanelToScreen(VisualElement root, Vector2 panelPos)
    {
        float scale = root.worldBound.width > 1f
            ? root.worldBound.width / Mathf.Max(1f, Screen.width)
            : 1f;
        return new Vector2(panelPos.x / scale, Screen.height - panelPos.y / scale);
    }

    /// <summary>
    /// Asks UI Toolkit's OWN pointer target resolver which runtime panel would
    /// receive a click at the given screen position. This is the exact routing
    /// the player's mouse uses, so it is the regression guard for the 07B.2.3
    /// "console panels steal the screen-space pointer" bug.
    /// </summary>
    private static string InputPanelNameFor(Vector2 screenPosition)
    {
        System.Type utility = typeof(VisualElement).Assembly.GetType(
            "UnityEngine.UIElements.UIElementsRuntimeUtility");
        object eventSystem = utility?.GetField("s_DefaultEventSystem",
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic)?.GetValue(null);
        if (eventSystem == null)
        {
            return "<no-event-system>";
        }

        System.Reflection.MethodInfo find = eventSystem.GetType().GetMethod(
            "FindTargetAtPosition",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        if (find == null)
        {
            return "<no-resolver>";
        }

        object[] arguments =
        {
            screenPosition, Vector2.zero, 0, null, null, null, Vector3.zero, null, null
        };
        find.Invoke(eventSystem, arguments);
        object panel = arguments[5];
        return panel == null
            ? "<null>"
            : panel.GetType().GetProperty("name",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(panel)?.ToString() ?? "<unnamed>";
    }

    // ====================================================================
    // 1) ScreenModalPointer_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator ScreenModalPointer_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(overlay, "overlay coordinator present");

        // The console panels are offscreen RenderTexture panels; they must sort
        // below the screen-space panels so they can never own screen input.
        for (int i = 0; i < 4; i++)
        {
            WorldConsoleSurface surface = controller.Consoles[i]
                .GetComponent<WorldConsoleSurface>();
            yield return WaitUntil(() => surface.IsReady);
            Assert.AreEqual(WorldConsoleSurface.OffscreenPanelSortingOrder,
                surface.CurrentPanelSettings.sortingOrder,
                "console " + (i + 1) + " offscreen panel sorts below screen UI");
            Assert.IsTrue(surface.CurrentPanelSettings.sortingOrder < 0,
                "console " + (i + 1) + " panel never competes for screen input");
        }

        // ---- Equipment locker -> shared loadout ----
        yield return InteractWithLocker(controller);
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
            && overlay.LoadoutPage != null && overlay.LoadoutPage.IsBound);
        Assert.IsTrue(overlay.IsOpen && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout, "locker opened the shared loadout");
        yield return WaitBound(overlay.LoadoutPage.Root);

        Assert.AreEqual(ShipGameController.ShipPointerOwner.ScreenModal,
            controller.PointerOwner, "screen-space modal owns the pointer");
        Assert.IsFalse(controller.PointerRouter == null
                       || controller.PointerRouter.enabled,
            "the world console pointer router is OFF for a screen-space modal");
        Assert.AreEqual(CursorLockMode.None, UnityEngine.Cursor.lockState,
            "cursor free for the modal");
        Assert.IsTrue(UnityEngine.Cursor.visible, "cursor visible for the modal");

        yield return AssertModalIsPointerTarget(overlay.LoadoutPage.Root,
            ElementNames.BatteryRowButton, "loadout battery row");

        // ---- Deployment confirmation ----
        // The loadout has no top-right 返回 button any more: Back (Esc / B) is
        // the documented exit and the overlay's own handler is the same one.
        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return WaitUntil(() => !overlay.IsOpen && controller.Player.InputEnabled);
        Assert.AreEqual(ShipGameController.ShipPointerOwner.None,
            controller.PointerOwner, "ShipExplore owns nothing but the camera");
        Assert.IsFalse(controller.PointerRouter != null
                       && controller.PointerRouter.enabled,
            "the world console pointer router is OFF in ShipExplore");

        yield return InteractWithDeploymentStation(controller);
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && overlay.DeployPage != null && overlay.DeployPage.IsBound);
        yield return WaitBound(overlay.DeployPage.Root);

        Assert.AreEqual(ShipGameController.ShipPointerOwner.ScreenModal,
            controller.PointerOwner, "deployment confirmation owns the pointer");
        Assert.IsFalse(controller.PointerRouter != null
                       && controller.PointerRouter.enabled,
            "the world console pointer router is OFF for the confirmation");

        yield return AssertModalIsPointerTarget(overlay.DeployPage.Root,
            ShipDeployConfirmPage.ConfirmButtonName, "确认降临");

        overlay.CloseToExplore();
        yield return WaitUntil(() => !overlay.IsOpen && controller.Player.InputEnabled);
        GameSession.Ensure().ResetSession();
    }

    /// <summary>Asserts the modal really is the screen-space input owner: the
    /// button is pickable AND UI Toolkit's resolver routes the click to the
    /// panel that hosts it (never to a console RenderTexture panel).</summary>
    private static IEnumerator AssertModalIsPointerTarget(VisualElement root,
        string buttonName, string label)
    {
        yield return WaitBound(root);
        Button button = root.Q<Button>(buttonName);
        Assert.IsNotNull(button, label + " button present");
        Assert.IsTrue(button.resolvedStyle.display != DisplayStyle.None,
            label + " is displayed");
        Assert.Greater(button.worldBound.width, 1f, label + " has real bounds");
        Assert.AreEqual(PickingMode.Position, button.pickingMode,
            label + " is pickable");

        // Nothing may cover the button inside its own panel: the deepest picked
        // element must be the button itself or one of its children (UI Toolkit
        // bubbles the pointer event up to the Button's Clickable).
        VisualElement picked = root.panel.Pick(button.worldBound.center);
        Assert.IsNotNull(picked, label + " is picked at its centre");
        Assert.IsTrue(IsSelfOrDescendant(button, picked),
            label + " is the element under the pointer (got "
            + (picked != null ? picked.name + "(" + picked.GetType().Name + ")" : "<null>")
            + ")");

        // The routing decision itself.
        string panelName = InputPanelNameFor(PanelToScreen(root,
            button.worldBound.center));
        Debug.Log("[07B23] " + label + " routed input panel=" + panelName);
        Assert.IsFalse(panelName.StartsWith("ConsolePanel_"),
            label + " must never route to a console RenderTexture panel (got "
            + panelName + ")");
        Assert.AreNotEqual("<null>", panelName,
            label + " must resolve to a live screen-space panel");
    }

    /// <summary>True when <paramref name="picked"/> is the button itself or a
    /// descendant of it (a pointer event on a child still reaches the Button).</summary>
    private static bool IsSelfOrDescendant(VisualElement button, VisualElement picked)
    {
        for (VisualElement e = picked; e != null; e = e.parent)
        {
            if (e == button)
            {
                return true;
            }
        }

        return false;
    }

    // ====================================================================
    // 2) EquipmentLockerRealClick_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator EquipmentLockerRealClick_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        GameSession session = GameSession.Instance;

        yield return InteractWithLocker(controller);
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
            && overlay.LoadoutPage.IsBound);
        yield return WaitBound(overlay.LoadoutPage.Root);

        Assert.IsFalse(controller.Player.InputEnabled,
            "movement/look OFF in LoadoutFocus");
        int cargoBefore = session.CargoWeight;

        Button battery = overlay.LoadoutPage.Root
            .Q<Button>(ElementNames.BatteryRowButton);
        Assert.IsFalse(session.BatterySelected, "battery starts off");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(battery), "battery clicked");
        yield return null;
        yield return null;
        Assert.IsTrue(session.BatterySelected,
            "GameSession battery changed by the real UI click");
        Assert.AreNotEqual(cargoBefore, session.CargoWeight,
            "cargo weight follows the loadout change");

        Button drone = overlay.LoadoutPage.Root
            .Q<Button>(ElementNames.DroneRowButton);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(drone), "drone clicked");
        yield return null;
        yield return null;
        Assert.IsTrue(session.DroneSelected,
            "GameSession drone changed by the real UI click");

        // Ready state is derived from the same authority.
        Assert.IsTrue(session.BatterySelected && session.DroneSelected,
            "loadout ready state reflects both optional items");

        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return WaitUntil(() => !overlay.IsOpen && controller.Player.InputEnabled);
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "locker-sourced loadout returns to ShipExplore");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.None,
            controller.PointerOwner, "pointer returns to ShipExplore");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 3) DeploymentConfirmRealClick_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator DeploymentConfirmRealClick_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        GameSession session = GameSession.Instance;

        yield return InteractWithDeploymentStation(controller);
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && overlay.DeployPage.IsBound);
        yield return WaitBound(overlay.DeployPage.Root);

        // 调整装备 -> shared loadout -> back to the confirmation.
        Button adjust = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.AdjustGearButtonName);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(adjust), "调整装备 clicked");
        yield return WaitUntil(() => overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
                                    && overlay.LoadoutPage.IsVisible);
        yield return WaitBound(overlay.LoadoutPage.Root);
        Assert.AreEqual(ShipOverlayUi.LoadoutReturnTarget.DeploymentConfirm,
            overlay.LoadoutReturn, "loadout knows it returns to the confirmation");

        Button battery = overlay.LoadoutPage.Root
            .Q<Button>(ElementNames.BatteryRowButton);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(battery), "battery clicked");
        yield return null;
        yield return null;
        Assert.IsTrue(session.BatterySelected, "loadout edited from the deploy route");

        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return WaitUntil(() => overlay.Mode
            == ShipOverlayUi.OverlayMode.DeploymentConfirm);
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "returning from the loadout stays in the ship");

        // 取消 -> ShipExplore.
        Button cancel = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.CancelButtonName);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(cancel), "取消 clicked");
        yield return WaitUntil(() => !overlay.IsOpen && controller.Player.InputEnabled);
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "cancel stays in the ship");

        // Re-open and 确认降临 -> the real Surface routing.
        yield return InteractWithDeploymentStation(controller);
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && overlay.DeployPage.IsBound);
        yield return WaitBound(overlay.DeployPage.Root);

        int mineralsBefore = session.Minerals;
        Button confirm = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.ConfirmButtonName);

        // Phase 09B PASS 8: a click is NOT enough any more - 确认降临 is a HELD
        // action, so the gesture is a real press, a real hold past the authored
        // duration, and a release.
        Assert.IsTrue(UiToolkitPointerSimulator.PointerDown(confirm), "确认降临 pressed");
        yield return WaitSeconds(ShipDeployConfirmPage.HoldSeconds + 0.25f);
        // The completed hold may already have routed to the surface (which
        // unloads this document), so the release itself is best-effort.
        UiToolkitPointerSimulator.PointerUp(confirm);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);
        yield return WaitForSurface(_ => { });

        Assert.AreEqual(mineralsBefore, session.Minerals,
            "GameSession persisted across the deployment");
        Assert.IsTrue(session.BatterySelected,
            "equipment edited in the confirmation persisted to the surface");
        Assert.AreEqual(1, Object.FindObjectsByType<GameSession>().Length,
            "exactly one authoritative GameSession");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 4) ShipOverview_NoExpeditionCTA_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipOverview_NoExpeditionCTA_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);

        WorldConsoleSurface overview = null;
        for (int i = 0; i < 4; i++)
        {
            WorldConsoleSurface candidate = controller.Consoles[i]
                .GetComponent<WorldConsoleSurface>();
            if (candidate.ScreenHost != null
                && candidate.ScreenHost.MappedScreen == AppScreen.Overview)
            {
                overview = candidate;
                break;
            }
        }

        Assert.IsNotNull(overview, "console 01 hosts the Overview screen");
        yield return WaitUntil(() => overview.IsReady && overview.ScreenHost.IsBound);
        yield return null;
        yield return null;

        VisualElement root = overview.Document.rootVisualElement;
        Button cta = root.Q<Button>(ElementNames.OpenPreparationButton);
        Assert.IsNotNull(cta, "Overview CTA element still exists in the tree");
        Assert.AreEqual(DisplayStyle.None, cta.resolvedStyle.display,
            "the ship Overview terminal hides the 准备远征 CTA");
        Assert.IsFalse(cta.enabledSelf,
            "the hidden ship CTA is not interactive");

        // The ship terminal still carries the planet / mission information.
        Assert.IsNotNull(root.Q<Label>(ElementNames.DestinationValue),
            "planet destination row present");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.PlanetDisc),
            "planet disc present");
        Assert.IsNotNull(root.Q<VisualElement>(ElementNames.ObjectivePanel),
            "current objective panel present");

        // And the legacy Hub keeps the CTA (regression contract).
        yield return LoadScene("01_Hub");
        yield return WaitUntil(() => Object.FindAnyObjectByType<AppController>() != null);
        yield return null;
        yield return null;
        AppController hub = Object.FindAnyObjectByType<AppController>();
        Button hubCta = hub.UiRoot?.Q<Button>(ElementNames.OpenPreparationButton);
        Assert.IsNotNull(hubCta, "legacy Hub CTA present");
        Assert.AreNotEqual(DisplayStyle.None, hubCta.resolvedStyle.display,
            "the legacy Hub still offers 准备远征");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 5) WristCrosshairVisibility_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator WristCrosshairVisibility_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController ship = null;
        yield return WaitForShip(value => ship = value);
        yield return WaitUntil(() => ship.Hud != null && ship.Hud.IsCrosshairVisible);
        yield return null;
        yield return null;

        Assert.IsTrue(ship.Hud.IsCrosshairVisible,
            "crosshair visible in ShipExplore");

        ship.SetWristOpen(true);
        yield return WaitUntil(() => ship.IsWristOpen);
        yield return null;
        yield return null;
        Assert.IsFalse(ship.Hud.IsCrosshairVisible,
            "crosshair HIDDEN while the ship wrist is open");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.Wrist,
            ship.PointerOwner, "the wrist owns the pointer");

        ship.SetWristOpen(false);
        yield return WaitUntil(() => !ship.IsWristOpen);
        yield return null;
        yield return null;
        Assert.IsTrue(ship.Hud.IsCrosshairVisible,
            "crosshair restored after the ship wrist closes");

        // ---- Surface: same rule, one input-mode presentation ----
        GameSession.Ensure().ResetSession();
        yield return LoadScene(GameSession.SurfaceSceneName);
        SurfaceGameController surface = null;
        yield return WaitForSurface(value => surface = value);
        SurfaceHudController hud =
            Object.FindAnyObjectByType<SurfaceHudController>();
        Assert.IsNotNull(hud, "surface HUD present");
        yield return WaitUntil(() => hud.IsCrosshairVisible);
        yield return null;
        yield return null;

        Assert.IsTrue(hud.IsCrosshairVisible,
            "crosshair visible in SurfaceFirstPerson");

        surface.SetWristOpen(true);
        yield return WaitUntil(() => surface.IsWristOpen);
        yield return null;
        yield return null;
        Assert.IsFalse(hud.IsCrosshairVisible,
            "crosshair HIDDEN while the surface wrist is open");

        surface.SetWristOpen(false);
        yield return WaitUntil(() => !surface.IsWristOpen);
        yield return null;
        yield return null;
        Assert.IsTrue(hud.IsCrosshairVisible,
            "crosshair restored after the surface wrist closes");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 6) ShipSchematicDoorVisual_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipSchematicDoorVisual_Runtime()
    {
        GameSession.Ensure().ResetSession();

        // Topology is untouched: exactly four doors in the authored order.
        Assert.AreEqual(4, ShipSchematicLayout.Doors.Length,
            "exactly four ship doors");
        Assert.AreEqual(ShipDoorOrientation.Horizontal,
            ShipSchematicLayout.DoorById(1).Orientation, "bridge bottom horizontal");
        Assert.AreEqual(ShipDoorOrientation.Vertical,
            ShipSchematicLayout.DoorById(2).Orientation, "warehouse east vertical");
        Assert.AreEqual(ShipDoorOrientation.Vertical,
            ShipSchematicLayout.DoorById(3).Orientation, "processing west vertical");
        Assert.AreEqual(ShipDoorOrientation.Horizontal,
            ShipSchematicLayout.DoorById(4).Orientation, "airlock top horizontal");

        foreach (ShipDoorSegment door in ShipSchematicLayout.Doors)
        {
            Vector2 a = ShipSchematicLayout.DeckToPixel(door.A);
            Vector2 b = ShipSchematicLayout.DeckToPixel(door.B);
            float lengthPx = Vector2.Distance(a, b);
            Assert.Greater(lengthPx, 8f, "door " + door.Id + " has a real span");
            int dashes = ShipSchematicVisual.DashCountFor(lengthPx);
            Assert.GreaterOrEqual(dashes, 2,
                "door " + door.Id + " renders at least two dashes (" + dashes
                + " for " + lengthPx.ToString("0.0") + " px) so it can never "
                + "read as a solid wall run");
        }

        // The dash pattern is the authored one (dash-gap-dash-gap).
        Assert.GreaterOrEqual(ShipSchematicVisual.DoorDashPx, 5f, "dash length");
        Assert.LessOrEqual(ShipSchematicVisual.DoorDashPx, 10f, "dash length");
        Assert.GreaterOrEqual(ShipSchematicVisual.DoorGapPx, 3f, "gap length");
        Assert.LessOrEqual(ShipSchematicVisual.DoorGapPx, 7f, "gap length");

        // A door REPLACES the wall: no wall run may overlap a door span.
        foreach (ShipWallSegment wall in ShipSchematicLayout.Walls)
        {
            foreach (ShipDoorSegment door in ShipSchematicLayout.Doors)
            {
                Assert.IsFalse(Overlaps(wall, door),
                    "wall " + wall.Id + " overlaps door " + door.Id
                    + " (the wall must break at the opening)");
            }
        }

        // Visual hierarchy: hull < wall < door < icon/airlock/player.
        Assert.Less(ShipSchematicVisual.HullAlpha,
            ShipSchematicVisual.WallAlpha, "walls read stronger than the hull");
        Assert.GreaterOrEqual(ShipSchematicVisual.DoorAlpha,
            ShipSchematicVisual.WallAlpha, "doors are at least as strong as walls");
        Assert.Greater(ShipSchematicVisual.IconAlpha,
            ShipSchematicVisual.WallAlpha, "terminal icons sit above the walls");
        Assert.GreaterOrEqual(ShipSchematicVisual.PlayerAlpha,
            ShipSchematicVisual.IconAlpha, "player marker is top layer");

        // Geometry helpers used by the hierarchy assertions.
        Assert.Greater(ShipSchematicVisual.WallThicknessPx, 2.5f,
            "walls are thick enough to read on the hologram");
        Assert.Greater(ShipSchematicVisual.DoorThicknessPx, 2.5f,
            "dashes are thick enough to read on the hologram");
        yield return null;
    }

    /// <summary>True when a wall run and a door span are collinear AND their
    /// intervals overlap by more than a hair (touching endpoints are fine:
    /// that is exactly how a wall breaks at a door).</summary>
    private static bool Overlaps(ShipWallSegment wall, ShipDoorSegment door)
    {
        const float tolerance = 0.01f;
        bool horizontal = door.Orientation == ShipDoorOrientation.Horizontal;
        if (horizontal)
        {
            if (Mathf.Abs(wall.A.y - door.A.y) > tolerance
                || Mathf.Abs(wall.B.y - door.B.y) > tolerance)
            {
                return false;
            }

            float a0 = Mathf.Min(wall.A.x, wall.B.x);
            float a1 = Mathf.Max(wall.A.x, wall.B.x);
            float d0 = Mathf.Min(door.A.x, door.B.x);
            float d1 = Mathf.Max(door.A.x, door.B.x);
            return Mathf.Min(a1, d1) - Mathf.Max(a0, d0) > tolerance;
        }

        if (Mathf.Abs(wall.A.x - door.A.x) > tolerance
            || Mathf.Abs(wall.B.x - door.B.x) > tolerance)
        {
            return false;
        }

        float w0 = Mathf.Min(wall.A.y, wall.B.y);
        float w1 = Mathf.Max(wall.A.y, wall.B.y);
        float e0 = Mathf.Min(door.A.y, door.B.y);
        float e1 = Mathf.Max(door.A.y, door.B.y);
        return Mathf.Min(w1, e1) - Mathf.Max(w0, e0) > tolerance;
    }
}
