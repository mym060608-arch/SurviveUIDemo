using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Scoped verification for the Airlock Landing Confirmation / 最终确认 page only
/// (Phase 09B PASS 8). Nothing else is loaded, asserted or captured here.
///
/// It covers the three things this pass is allowed to touch:
///
///   * STRUCTURE / COPY - the page opens through the real airlock route, names
///     the shared AURELIA-01 destination, lists the LIVE equipment and cargo as
///     plain text (no pictograms), and carries the X / A / B glyphs INSIDE the
///     three action cards - with no second prompt row anywhere;
///   * HOLD CONFIRM - a tap never lands, a released half hold rolls back to 0%
///     and stays on the page, and only a completed hold runs the existing
///     deployment routing;
///   * BACK / CONTEXT - B and Escape leave one step (never a pause, never a
///     quit) and X opens the SAME shared loadout view the 出发准备 card opens.
///
/// Evidence: 1920x1080 frames of the resting page, a 50% hold, the completed
/// hold and an edited-equipment state.
/// </summary>
public class SurvivePhase09BLandingTests
{
    private const string Output =
        @"D:\dsh_gpt_unity_blender\export\evidence\phase09b\pass8_landing_confirmation";

    private const int ShotWidth = 1920;
    private const int ShotHeight = 1080;
    private const int MaxWaitFrames = 600;

    /// <summary>The authored Airlock deployment pose (same pose Phase 07B.2 uses).</summary>
    private static readonly Vector3 DeployPose = new Vector3(0f, 0f, -12.3f);
    private const float DeployYaw = 180f;

    private Gamepad _pad;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        InputDeviceTracker.ResetToDefaults();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }

        _pad = InputSystem.AddDevice<Gamepad>();
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _pad = null;
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // 1. Structure, copy and the fused key line
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_Structure_CopyAndFusedKeys_AndCapture()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        yield return OpenLanding(ship, overlay);

        ShipDeployConfirmPage page = overlay.DeployPage;
        VisualElement root = page.Root;
        GameSession session = GameSession.Instance;

        // ---- destination: the ONE shared planet name ----
        Assert.AreEqual(PlanetIdentity.DisplayName, LabelText(root, ShipDeployConfirmPage.DestinationName),
            "the page names the shared planet");
        Assert.AreEqual("目的地", ClassText(root, "landing-kicker", 0),
            "the destination block is labelled 目的地");
        Assert.AreEqual("微光之地 · 资源探测区", ClassText(root, "landing-sub"),
            "the destination carries the approved sub-line");
        foreach (Label label in root.Query<Label>().ToList())
        {
            Assert.IsFalse(PlanetIdentity.NamesAnotherPlanet(label.text),
                "no node of this page names a second planet: " + label.text);
        }

        // ---- final confirmation block ----
        Assert.AreEqual("最终确认", ClassText(root, "landing-final-title"), "final confirm title");
        Assert.AreEqual("长按确认降临", ClassText(root, "landing-final-sub"), "final confirm hint");
        Assert.AreEqual("当前装备", ClassText(root, "landing-kicker", 1), "equipment block label");

        // ---- live equipment + cargo, text only ----
        Assert.AreEqual("采集器 MK-I", GearName(root, 0), "harvester row");
        Assert.AreEqual("备用电池组", GearName(root, 1), "battery row");
        Assert.AreEqual("勘测无人机", GearName(root, 2), "drone row");
        Assert.AreEqual("载荷", GearName(root, 3), "cargo row");
        Assert.AreEqual("必需", LabelText(root, ShipDeployConfirmPage.HarvesterStateName), "harvester state");
        Assert.AreEqual(session.BatterySelected ? "已装备" : "未选择",
            LabelText(root, ShipDeployConfirmPage.BatteryStateName), "battery state");
        Assert.AreEqual(session.DroneSelected ? "已装备" : "未选择",
            LabelText(root, ShipDeployConfirmPage.DroneStateName), "drone state");
        Assert.AreEqual(session.CargoWeight + " / " + GameSession.CargoCapacityKg + " KG",
            LabelText(root, ShipDeployConfirmPage.CargoValueName), "cargo is the real weight");

        // No pictograms on this page: every equipment row is exactly two labels
        // (name + state) and nothing inside the block draws an image.
        VisualElement gear = root.Q<VisualElement>(className: "modal-gear");
        Assert.IsNotNull(gear, "equipment block");
        var gearRows = gear.Query<VisualElement>(className: "modal-gear-row").ToList();
        Assert.AreEqual(4, gearRows.Count, "four equipment rows");
        foreach (VisualElement row in gearRows)
        {
            Assert.AreEqual(2, row.childCount, "a row is a name and a state, nothing else");
            foreach (VisualElement child in row.Children())
            {
                Assert.IsTrue(child is Label,
                    "the equipment rows stay text-only (unexpected "
                    + child.GetType().Name + ")");
            }
        }

        foreach (VisualElement element in gear.Query<VisualElement>().ToList())
        {
            Assert.IsNull(element.style.backgroundImage.value.texture,
                "no pictogram texture inside the equipment block ("
                + element.name + ")");
        }

        // ---- the three cards carry their own glyphs ----
        InputDeviceTracker.NotifyGamepad();
        yield return WaitFrames(3);
        Assert.AreEqual("[X] 出发准备", page.AdjustHintText, "X card hint");
        Assert.AreEqual("[A] 确认降临", page.ConfirmHintText, "A card hint");
        Assert.AreEqual("[B] 返回", page.BackHintText, "B card hint");
        Assert.AreEqual("[B] 返回", overlay.BackHintText, "the overlay reports the card hint");

        InputDeviceTracker.NotifyKeyboardMouse();
        yield return WaitFrames(3);
        Assert.AreEqual("[M] 出发准备", page.AdjustHintText, "keyboard context glyph");
        Assert.AreEqual("[E] 确认降临", page.ConfirmHintText, "keyboard confirm glyph");
        Assert.AreEqual("[Esc] 返回", page.BackHintText, "keyboard back glyph");
        InputDeviceTracker.NotifyGamepad();
        yield return WaitFrames(3);

        // ---- exactly one key line: no second prompt row ----
        Assert.IsNull(root.Q<Label>("DeployBackHint"), "no separate Back hint row");
        Assert.AreEqual(0, root.Query<VisualElement>(className: "prompt-bar").ToList().Count,
            "no shared prompt bar on this page");
        Assert.AreEqual(3, root.Query<Button>(className: "landing-card").ToList().Count,
            "exactly three action cards");
        Assert.AreEqual(1, root.Query<VisualElement>(className: "landing-hold-track").ToList().Count,
            "exactly one hold bar, inside the confirm card");

        // ---- the two cyan dot fields are really drawn ----
        foreach (string hostName in new[] { ElementNames.LandingDotsLeft, ElementNames.LandingDotsRight })
        {
            VisualElement host = root.Q<VisualElement>(hostName);
            Assert.IsNotNull(host, hostName + " mounted");
            LandingDotMatrix matrix = host.Q<LandingDotMatrix>(null);
            Assert.IsNotNull(matrix, hostName + " draws the code-built dot matrix");
        }

        // ---- default focus is the primary CTA, and no hold started by itself ----
        Assert.AreSame(root.Q<Button>(ShipDeployConfirmPage.ConfirmButtonName), page.FocusedCard,
            "focus lands on 确认降临");
        Assert.AreEqual(0f, page.HoldProgress01, 0.0001f, "opening never starts a hold");
        Assert.AreEqual(0, page.ConfirmRequestCount, "opening never requests a landing");

        // ---- real pointer ownership stays with the modal ----
        Assert.AreEqual(ShipGameController.ShipPointerOwner.ScreenModal, ship.PointerOwner,
            "the landing confirmation owns the pointer");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(), "still in the ship");

        Directory.CreateDirectory(Output);
        yield return Capture(page, Path.Combine(Output, "landing_default.png"));
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 2. A tap never deploys
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_ShortPress_NeverDeploys()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        yield return OpenLanding(ship, overlay);
        ShipDeployConfirmPage page = overlay.DeployPage;

        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(0f, page.HoldProgress01, 0.02f,
            "a tap leaves no progress behind");
        Assert.AreEqual(0, page.ConfirmRequestCount, "a tap never requests a landing");
        Assert.IsTrue(overlay.IsOpen && page.IsVisible, "the page stays open");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "a tap never loads the surface");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "a tap never pauses");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 3. A released half hold rolls back and stays on the page
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_HoldHalf_ThenRelease_Resets_AndCapture()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        yield return OpenLanding(ship, overlay);
        ShipDeployConfirmPage page = overlay.DeployPage;

        // Hold for about half of the authored duration.
        yield return HoldButtonStart(GamepadButton.South,
            ShipDeployConfirmPage.HoldSeconds * 0.5f);
        Assert.IsTrue(page.IsHolding, "the confirm card is holding");
        Assert.Greater(page.HoldProgress01, 0.25f, "progress really started");
        Assert.Less(page.HoldProgress01, 0.85f, "and it is a HALF hold");

        VisualElement fill = page.Root.Q<VisualElement>(ShipDeployConfirmPage.HoldFillName);
        Assert.IsNotNull(fill, "hold bar inside the card");
        float midWidth = fill.resolvedStyle.width;
        Assert.Greater(midWidth, 1f, "the inner bar is visibly filled");

        Directory.CreateDirectory(Output);
        yield return CaptureFrozen(page, Path.Combine(Output, "landing_hold_50.png"));

        yield return HoldButtonEnd();
        // The rollback is a smooth ramp (by design), so let it finish.
        yield return WaitSeconds(0.6f);
        Assert.AreEqual(0f, page.HoldProgress01, 0.02f, "releasing early resets the progress");
        Assert.AreEqual(0, page.ConfirmRequestCount, "a cancelled hold never requests a landing");
        Assert.IsTrue(overlay.IsOpen && page.IsVisible, "the page stays up after a cancel");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "a cancelled hold never loads the surface");
        Assert.IsFalse(page.IsHoldComplete, "no completed state survives the cancel");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "a cancelled hold never pauses");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 4. Only a completed hold deploys
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_HoldComplete_OnlyThenDeploys_AndCapture()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        yield return OpenLanding(ship, overlay);
        ShipDeployConfirmPage page = overlay.DeployPage;
        GameSession session = GameSession.Instance;
        int mineralsBefore = session.Minerals;

        yield return HoldButtonUntil(() => page.IsHoldComplete,
            ShipDeployConfirmPage.HoldSeconds + 2f);

        Assert.IsTrue(page.IsHoldComplete, "the completed feedback beat is showing");
        Assert.AreEqual(1f, page.HoldProgress01, 0.001f, "the bar reached 100%");
        Assert.AreEqual(0, page.ConfirmRequestCount,
            "the routing is only requested after the feedback beat");

        // Freeze the game for the evidence frame: the confirmation beat is
        // measured in game time, so the 100% state really holds still.
        Directory.CreateDirectory(Output);
        yield return CaptureFrozen(page, Path.Combine(Output, "landing_hold_complete.png"));

        yield return HoldButtonEnd();
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName, 1800);
        Assert.AreEqual(GameSession.SurfaceSceneName, ActiveSceneName(),
            "the completed hold ran the EXISTING deployment routing");
        Assert.AreEqual(1, page.ConfirmRequestCount, "exactly one landing request");
        Assert.GreaterOrEqual(mineralsBefore, session.Minerals, "the session persisted");
        Assert.AreEqual(1, Object.FindObjectsByType<GameSession>().Length,
            "exactly one authoritative GameSession");
        Assert.AreEqual(0, AppQuit.RequestCount, "a completed hold never quits");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 5. X opens the SAME shared loadout view; B steps back
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_ContextAction_OpensLoadout_AndBackSteps()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        yield return OpenLanding(ship, overlay);
        ShipDeployConfirmPage page = overlay.DeployPage;

        ShipLoadoutPage fromLocker = overlay.LoadoutPage;
        Assert.IsNotNull(fromLocker, "the shared loadout page exists");

        // X / □ / M = the ContextAction: the same 出发准备 path as the card.
        yield return PressButton(GamepadButton.West);
        yield return WaitUntil(() => overlay.Mode == ShipOverlayUi.OverlayMode.Loadout);
        Assert.AreEqual(ShipOverlayUi.OverlayMode.Loadout, overlay.Mode,
            "X opened the shared loadout view");
        Assert.IsTrue(ReferenceEquals(overlay.LoadoutPage, fromLocker),
            "it is the SAME loadout page (no second equipment flow)");
        Assert.IsFalse(page.IsVisible, "the landing page is hidden while editing");
        Assert.AreEqual(0f, page.HoldProgress01, 0.0001f, "no hold state survives the detour");

        // Back from the loadout returns to the landing confirmation (not away).
        yield return PressButton(GamepadButton.East);
        yield return WaitUntil(() => overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm);
        Assert.AreEqual(ShipOverlayUi.OverlayMode.DeploymentConfirm, overlay.Mode,
            "Back steps back to 最终确认");
        Assert.IsTrue(page.IsVisible, "the landing page is up again");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly ONE Back action so far");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Back never pauses");

        // And once more: the landing page closes back to free first person.
        yield return PressButton(GamepadButton.East);
        yield return WaitUntil(() => !overlay.IsOpen);
        Assert.IsFalse(overlay.IsOpen, "B closes 最终确认");
        Assert.AreEqual(2, overlay.BackActionCount, "one press, one step");
        Assert.IsTrue(ship.Player.InputEnabled, "free first person is restored");
        Assert.AreEqual(0, AppQuit.RequestCount, "Back never quits");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 6. Escape is this page's Back (never a pause)
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_Escape_IsBack_NotPause()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        yield return OpenLanding(ship, overlay);
        ShipDeployConfirmPage page = overlay.DeployPage;

        InputDeviceTracker.NotifyKeyboardMouse();
        yield return WaitFrames(3);
        Assert.AreEqual(UiInput.UiLayer.Modal, UiInput.ActiveBackLayer,
            "the landing page is the layer that owns Back");
        Assert.AreEqual("[Esc] 返回", page.BackHintText,
            "the B card really shows the keyboard Back glyph");

        UiInput.InjectEscapeForAutomation();
        yield return WaitFrames(3);
        Assert.IsFalse(overlay.IsOpen, "Escape leaves the landing confirmation");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Escape on this page never opens the pause");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Escape on this page never freezes time");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly ONE Back action");
        Assert.AreEqual(0, AppQuit.RequestCount, "Escape on this page never quits");
        Assert.AreEqual(0, page.ConfirmRequestCount, "Escape never triggers a landing");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 7. The live equipment state (and its evidence frame)
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_EquipmentState_FollowsTheSession_AndCapture()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        GameSession session = GameSession.Instance;

        Assert.IsTrue(session.TrySetBatterySelected(true), "battery selected");
        Assert.IsTrue(session.TrySetDroneSelected(true), "drone selected");

        yield return OpenLanding(ship, overlay);
        ShipDeployConfirmPage page = overlay.DeployPage;
        VisualElement root = page.Root;

        Assert.AreEqual("已装备", LabelText(root, ShipDeployConfirmPage.BatteryStateName),
            "battery reads 已装备");
        Assert.AreEqual("已装备", LabelText(root, ShipDeployConfirmPage.DroneStateName),
            "drone reads 已装备");
        Assert.AreEqual(session.CargoWeight + " / " + GameSession.CargoCapacityKg + " KG",
            LabelText(root, ShipDeployConfirmPage.CargoValueName), "live cargo weight");
        Assert.AreEqual("90 / 100 KG", LabelText(root, ShipDeployConfirmPage.CargoValueName),
            "45 + 25 + 20 KG is the real loadout weight");

        Directory.CreateDirectory(Output);
        yield return Capture(page, Path.Combine(Output, "landing_equipment_state.png"));
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 8. The mouse path: pressing and holding the card is the same hold
    // ====================================================================

    [UnityTest]
    public IEnumerator Landing_MouseHold_OnTheCard_CompletesTheSameWay()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        yield return OpenLanding(ship, overlay);
        ShipDeployConfirmPage page = overlay.DeployPage;
        Button confirm = page.Root.Q<Button>(ShipDeployConfirmPage.ConfirmButtonName);
        Assert.IsNotNull(confirm, "confirm card");

        // Hold about half with the pointer, release: back to zero, no landing.
        Assert.IsTrue(UiToolkitPointerSimulator.PointerDown(confirm), "pointer down");
        yield return WaitSeconds(ShipDeployConfirmPage.HoldSeconds * 0.5f);
        Assert.Greater(page.HoldProgress01, 0.2f, "the pointer hold fills the bar");
        Assert.IsTrue(UiToolkitPointerSimulator.PointerUp(confirm), "pointer up");
        yield return WaitSeconds(0.7f);
        Assert.AreEqual(0f, page.HoldProgress01, 0.02f, "the pointer release rolls back");
        Assert.AreEqual(0, page.ConfirmRequestCount, "a pointer tap never lands");

        // Now hold all the way: the same routing runs.
        Assert.IsTrue(UiToolkitPointerSimulator.PointerDown(confirm), "pointer down again");
        yield return HoldUntil(() => page.IsHoldComplete,
            ShipDeployConfirmPage.HoldSeconds + 2f);
        Assert.IsTrue(page.IsHoldComplete, "the pointer hold reached 100%");
        Assert.IsTrue(UiToolkitPointerSimulator.PointerUp(confirm), "pointer up");
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName, 1800);
        Assert.AreEqual(GameSession.SurfaceSceneName, ActiveSceneName(),
            "a completed pointer hold runs the existing routing");
        Assert.AreEqual(1, page.ConfirmRequestCount, "exactly one landing request");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // helpers
    // ====================================================================

    /// <summary>Polls a condition in real time (used while a button is held).</summary>
    private static IEnumerator HoldUntil(System.Func<bool> condition, float maxSeconds)
    {
        float deadline = Time.realtimeSinceStartup + maxSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private IEnumerator EnterShip(System.Action<ShipGameController> onShip,
        System.Action<ShipOverlayUi> onOverlay)
    {
        GameSession.Ensure().ResetSession();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitUntil(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady && ship.Player != null;
        });

        ShipOverlayUi overlay = null;
        yield return WaitUntil(() =>
        {
            overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
            return overlay != null && overlay.DeployPage != null
                   && overlay.DeployPage.IsBound;
        });

        Assert.IsNotNull(ship, "ship controller ready");
        Assert.IsNotNull(overlay, "overlay coordinator ready");
        yield return WaitFrames(4);
        onShip?.Invoke(ship);
        onOverlay?.Invoke(overlay);
    }

    /// <summary>Walks to the airlock deployment station and opens the page with
    /// the real interaction route (the same path a player takes).</summary>
    private IEnumerator OpenLanding(ShipGameController ship, ShipOverlayUi overlay)
    {
        ship.Player.TeleportTo(DeployPose);
        ship.Player.SetFacing(DeployYaw, 0f);
        yield return null;
        Physics.SyncTransforms();

        ShipAirlockInteractable airlock = ship.Airlock;
        Assert.IsNotNull(airlock, "the airlock deployment station exists");
        yield return WaitUntil(() =>
        {
            ship.Interactor.RefreshTarget();
            return ReferenceEquals(ship.Interactor.CurrentTarget, airlock)
                   && ship.Interactor.HasValidTarget;
        });

        Assert.IsTrue(ReferenceEquals(ship.Interactor.CurrentTarget, airlock),
            "the gaze resolved the deployment station");
        yield return PressButton(GamepadButton.South);

        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && overlay.DeployPage.IsVisible);
        Assert.IsTrue(overlay.IsOpen, "最终确认 opened");
        yield return WaitBound(overlay.DeployPage.Root);
    }

    private static string ActiveSceneName()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "<none>";
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label " + name);
        return label.text;
    }

    private static string SubText(VisualElement root)
    {
        return ClassText(root, "landing-sub");
    }

    /// <summary>Text of the nth label carrying a class (the page's section
    /// labels are styled by class, not by element name).</summary>
    private static string ClassText(VisualElement root, string className, int index = 0)
    {
        var labels = root.Query<Label>(className: className).ToList();
        Assert.Greater(labels.Count, index, "label with class " + className + " exists");
        return labels[index].text;
    }

    private static string GearName(VisualElement root, int index)
    {
        var names = root.Query<Label>(className: "modal-gear-name").ToList();
        Assert.Greater(names.Count, index, "equipment row " + index + " exists");
        return names[index].text;
    }

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
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

    private static IEnumerator WaitBound(VisualElement root)
    {
        yield return WaitUntil(() => root != null && root.panel != null && root.childCount > 0);
        yield return WaitFrames(3);
    }

    /// <summary>One real pad tap: press, two frames, release.</summary>
    private IEnumerator PressButton(GamepadButton button)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return WaitFrames(3);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return WaitFrames(3);
    }

    /// <summary>Presses and KEEPS the button down for a real interval.</summary>
    private IEnumerator HoldButtonStart(GamepadButton button, float seconds)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        yield return null;
    }

    /// <summary>
    /// Keeps the button pressed until the condition becomes true (or the
    /// timeout expires): the landing hold must be released the instant the page
    /// reaches its completed state, not a fixed interval later.
    /// </summary>
    private IEnumerator HoldButtonUntil(System.Func<bool> condition, float maxSeconds)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.South));
        yield return null;

        float deadline = Time.realtimeSinceStartup + maxSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
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

    /// <summary>Releases a held button.</summary>
    private IEnumerator HoldButtonEnd()
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return WaitFrames(3);
    }

    /// <summary>
    /// 1920x1080 frame of the landing document, captured with the game time
    /// frozen so a hold stays exactly where the evidence should show it.
    /// </summary>
    private static IEnumerator CaptureFrozen(ShipDeployConfirmPage page, string path)
    {
        float scale = Time.timeScale;
        Time.timeScale = 0f;
        yield return Capture(page, path);
        Time.timeScale = scale;
    }

    /// <summary>1920x1080 runtime frame of the live panel.</summary>
    private static IEnumerator Capture(ShipDeployConfirmPage page, string path)
    {
        PanelSettings panel = page.Document != null ? page.Document.panelSettings : null;
        Assert.IsNotNull(panel, "landing panel settings");

        var target = new RenderTexture(ShotWidth, ShotHeight, 0, RenderTextureFormat.ARGB32);
        target.Create();
        RenderTexture oldTarget = panel.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        panel.targetTexture = target;
        yield return WaitFrames(10);

        RenderTexture.active = target;
        var texture = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
        texture.Apply();

        RenderTexture.active = oldActive;
        panel.targetTexture = oldTarget;
        target.Release();
        Object.Destroy(target);

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.Destroy(texture);

        Assert.Greater(new FileInfo(path).Length, 20000, "visible landing capture: " + path);
    }
}
