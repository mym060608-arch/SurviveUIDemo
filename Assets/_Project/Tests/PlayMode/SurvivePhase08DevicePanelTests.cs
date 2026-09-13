using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 08 device-panel hotfix - the two Airlock device panels.
///
///   装备整理   = the equipment locker           -> ShipOverlayUi.OpenLoadoutFromLocker
///   出征前准备 = the airlock deployment station  -> ShipOverlayUi.OpenDeploymentConfirm
///
/// Both were reachable but could NOT be left with the controller: Back (Xbox B /
/// buttonEast) did nothing because the overlay only read the Escape key. This
/// suite drives the REAL path (a virtual gamepad added to the Input System, real
/// queued state events, the real interactor, the real overlay pages) and pins:
///
///   open -> B -> closed, with the exact pre-panel Ship Interior state restored
///   exactly ONE Back action per press (never a double close, never a quit)
///   the panel really shows its Back binding ([B] 返回 on a controller)
///   Menu/Start pauses ABOVE an open panel, 继续游戏 returns to that same panel
///   the loadout opened from 出征前准备 steps back to the confirmation first
///
/// There is exactly ONE panel state (ShipOverlayUi + the two pages): no second
/// panel manager, no second input system (asserted at the end of the suite).
/// </summary>
public class SurvivePhase08DevicePanelTests
{
    private const int MaxWaitFrames = 400;

    private Gamepad _pad;

    /// <summary>The authored Airlock poses (same poses Phase 07B.2 uses).</summary>
    private static readonly Vector3 LockerPose = new Vector3(-1.55f, 0f, -12.6f);
    private const float LockerYaw = 270f;
    private static readonly Vector3 DeployPose = new Vector3(0f, 0f, -12.3f);
    private const float DeployYaw = 180f;

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
    // 14.1 / 14.2 - 装备整理 (equipment locker): open with A, close with B
    // ====================================================================

    [UnityTest]
    public IEnumerator LoadoutPanel_GamepadA_Opens_GamepadB_Closes_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        GameSession session = GameSession.Instance;

        yield return OpenPanel(ship, overlay, PanelKind.Loadout);

        Assert.AreEqual(ShipOverlayUi.OverlayMode.Loadout, overlay.Mode,
            "装备整理 opens the shared loadout page");
        Assert.IsTrue(ship.IsOverlayOpen, "the panel is the open modal");
        Assert.IsFalse(ship.Player.InputEnabled, "movement and look are frozen");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.ScreenModal,
            ship.PointerOwner, "the panel owns the pointer");
        Assert.AreEqual("[B] 返回", overlay.BackHintText,
            "the panel really shows the controller Back binding");
        Assert.AreEqual(0, overlay.BackActionCount, "opening is not a Back action");

        // ---- Real Xbox B ----
        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(overlay.IsOpen, "B closes 装备整理");
        Assert.AreEqual(ShipOverlayUi.OverlayMode.None, overlay.Mode, "mode is None again");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly ONE Back action per press");
        Assert.IsTrue(ship.Player.InputEnabled, "movement and look are restored");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.None, ship.PointerOwner,
            "the panel releases the pointer");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "B never opens the pause");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f,
            "closing a panel never freezes time");
        Assert.AreEqual(0, AppQuit.RequestCount, "B never quits");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "B never changes the scene");
        Assert.IsTrue(GameSession.Instance == session && session != null,
            "the session is neither reset nor replaced");

        // ---- the world interaction really works again ----
        object locker = Object.FindAnyObjectByType<EquipmentLockerInteractable>();
        yield return FaceTarget(ship, locker);
        Assert.IsTrue(ship.Interactor.HasValidTarget,
            "the gaze prompt is restored after the panel closes");
        StringAssert.Contains("调整远征装备", ship.Interactor.ResolvePrompt(),
            "the locker prompt is back");

        // Opening it again proves the world interaction path was fully restored.
        yield return OpenPanel(ship, overlay, PanelKind.Loadout);
        Assert.AreEqual(1, overlay.BackActionCount,
            "reopening is not a Back action");
        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(overlay.IsOpen, "the panel closes again");
        Assert.AreEqual(2, overlay.BackActionCount,
            "the second press is the second action (one per press)");
        Assert.AreEqual(0, AppQuit.RequestCount, "reopening and closing never quits");
    }

    // ====================================================================
    // 14.1 / 14.2 - 出征前准备 (deployment station): open with A, close with B
    // ====================================================================

    [UnityTest]
    public IEnumerator DeployPanel_GamepadA_Opens_GamepadB_Closes_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);
        GameSession session = GameSession.Instance;

        yield return OpenPanel(ship, overlay, PanelKind.DeploymentConfirm);

        Assert.AreEqual(ShipOverlayUi.OverlayMode.DeploymentConfirm, overlay.Mode,
            "出征前准备 opens the deployment confirmation");
        Assert.IsFalse(ship.Player.InputEnabled, "movement and look are frozen");
        Assert.AreEqual("[B] 返回", overlay.BackHintText,
            "the panel really shows the controller Back binding");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "opening the panel does NOT route to the surface");

        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(overlay.IsOpen, "B closes 出征前准备");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly ONE Back action per press");
        Assert.IsTrue(ship.Player.InputEnabled, "movement and look are restored");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.None, ship.PointerOwner,
            "the panel releases the pointer");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "B never opens the pause");
        Assert.AreEqual(0, AppQuit.RequestCount, "B never quits");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "B never changes the scene");
        Assert.IsTrue(GameSession.Instance == session && session != null,
            "the session is neither reset nor replaced");

        yield return FaceTarget(ship, ship.Airlock);
        StringAssert.Contains(PlanetIdentity.SurfacePrompt, ship.Interactor.ResolvePrompt(),
            "the deployment prompt is back after the panel closes");
    }

    // ====================================================================
    // 14.4 - Menu/Start pauses ABOVE an open panel; the panel is preserved
    // ====================================================================

    [UnityTest]
    public IEnumerator DeployPanel_MenuStart_PausesAbove_KeepsPanel_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return OpenPanel(ship, overlay, PanelKind.DeploymentConfirm);

        yield return PressButton(GamepadButton.Start);

        Assert.IsTrue(ship.PauseMenu.IsOpen, "Menu/Start opens the pause over the panel");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "the pause freezes the game");
        Assert.IsTrue(overlay.IsOpen, "the panel stays as the underlying state");
        Assert.AreEqual(ShipOverlayUi.OverlayMode.DeploymentConfirm, overlay.Mode,
            "Menu/Start does NOT close the panel");
        Assert.AreEqual(0, overlay.BackActionCount,
            "Menu/Start is a pause press, never a Back action");
        Assert.AreEqual("Resume", ship.PauseMenu.FocusedItemId, "focus lands on 继续游戏");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.PauseMenu, ship.PointerOwner,
            "the pause owns the pointer while it is open");

        // Back while paused belongs to the PAUSE (top of the chain): it resumes
        // and must not also close the panel underneath.
        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(ship.PauseMenu.IsOpen, "B resumes the game");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "time is restored");
        Assert.IsTrue(overlay.IsOpen, "the panel is still open after the pause");
        Assert.AreEqual(ShipOverlayUi.OverlayMode.DeploymentConfirm, overlay.Mode,
            "the panel kept its mode");
        Assert.AreEqual(0, overlay.BackActionCount,
            "one press performed exactly one action (the pause)");
        Assert.IsFalse(ship.Player.InputEnabled,
            "the panel still owns the input after the pause closes");
        Assert.AreEqual(0, AppQuit.RequestCount, "no quit anywhere in this loop");

        // 继续游戏 through the pause menu instead of B: same result.
        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open again");
        yield return PressButton(GamepadButton.South);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "继续游戏 closes the pause");
        Assert.IsTrue(overlay.IsOpen, "the panel survived 继续游戏");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "time restored by 继续游戏");

        // And now B closes the panel for real.
        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(overlay.IsOpen, "B closes the panel after the pause loop");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly one Back action happened");
        Assert.IsTrue(ship.Player.InputEnabled, "first-person control is back");
        Assert.AreEqual(0, AppQuit.RequestCount, "the whole loop never quits");
    }

    // ====================================================================
    // 14.9 - 装备整理: pause -> 继续游戏 -> the panel is still open -> B closes
    // ====================================================================

    [UnityTest]
    public IEnumerator LoadoutPanel_MenuStart_Continue_ThenBack_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return OpenPanel(ship, overlay, PanelKind.Loadout);

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "Menu/Start opens the pause over 装备整理");
        Assert.IsTrue(overlay.IsOpen, "the loadout page is the underlying state");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "frozen by the pause");

        yield return PressButton(GamepadButton.South);

        Assert.IsFalse(ship.PauseMenu.IsOpen, "A confirms 继续游戏");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "time restored");
        Assert.IsTrue(overlay.IsOpen
                      && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout,
            "继续游戏 returns to the same 装备整理 panel");
        Assert.IsFalse(ship.Player.InputEnabled, "the panel still owns the input");

        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(overlay.IsOpen, "B closes the panel after the pause loop");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly one Back action");
        Assert.IsTrue(ship.Player.InputEnabled, "first-person control is back");
        Assert.AreEqual(0, AppQuit.RequestCount, "no quit in the pause loop");
    }

    // ====================================================================
    // 14.3 - Back walks the chain one step at a time
    //    出征前准备 --调整装备--> 装备整理 --B--> 出征前准备 --B--> ShipExplore
    // ====================================================================

    [UnityTest]
    public IEnumerator AdjustGearRoute_BackStepsOnce_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return OpenPanel(ship, overlay, PanelKind.DeploymentConfirm);
        yield return WaitBound(overlay.DeployPage.Root);

        Button adjust = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.AdjustGearButtonName);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(adjust), "调整装备 clicked");
        yield return WaitUntil(() => overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
                                    && overlay.LoadoutPage.IsVisible
                                    && overlay.LoadoutPage.IsBound);
        Assert.AreEqual(ShipOverlayUi.LoadoutReturnTarget.DeploymentConfirm,
            overlay.LoadoutReturn, "the loadout knows where it came from");

        // First B: one step back to the deployment confirmation.
        yield return PressButton(GamepadButton.East);

        Assert.IsTrue(overlay.IsOpen, "the panel is still open");
        Assert.AreEqual(ShipOverlayUi.OverlayMode.DeploymentConfirm, overlay.Mode,
            "Back stepped back to 出征前准备");
        Assert.AreEqual(1, overlay.BackActionCount, "one press = one step");
        Assert.IsFalse(ship.Player.InputEnabled, "the modal still owns the input");
        Assert.AreEqual(0, AppQuit.RequestCount, "never quits");

        // Second B: out of the panel entirely.
        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(overlay.IsOpen, "the second Back closes the panel");
        Assert.AreEqual(2, overlay.BackActionCount, "two presses = two steps");
        Assert.IsTrue(ship.Player.InputEnabled, "control restored");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "still inside the ship");
        Assert.AreEqual(0, AppQuit.RequestCount, "never quits");
    }

    // ====================================================================
    // 14.3 - Escape keeps its two semantics apart (panel layer vs gameplay)
    // ====================================================================

    [UnityTest]
    public IEnumerator Escape_ClosesPanelOnly_AndStillPausesInGameplay_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return OpenPanel(ship, overlay, PanelKind.Loadout);

        // Escape inside the panel layer (injected: the batch-mode harness cannot
        // deliver keyboard state to a synthetic Keyboard device).
        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return null;

        Assert.IsFalse(overlay.IsOpen, "Escape closes the open panel (Back layer)");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly one Back action");
        Assert.IsFalse(ship.PauseMenu.IsOpen,
            "Escape in the panel layer must NOT also open the pause");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "no accidental freeze");
        Assert.IsTrue(ship.Player.InputEnabled, "control restored");
        Assert.AreEqual(0, AppQuit.RequestCount, "Escape never quits");

        // The same Escape in gameplay is still the global pause key.
        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return null;

        Assert.IsTrue(ship.PauseMenu.IsOpen, "Escape in gameplay pauses");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "and freezes");
        Assert.IsFalse(overlay.IsOpen, "no panel is involved");
        Assert.AreEqual(0, AppQuit.RequestCount, "still no quit");
    }

    // ====================================================================
    // 14.3 - Back in free first person does nothing at all
    // ====================================================================

    [UnityTest]
    public IEnumerator BackInFreeFirstPerson_DoesNothing_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return PressButton(GamepadButton.East);
        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(overlay.IsOpen, "B opens nothing");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "B does not pause");
        Assert.AreEqual(0, AppQuit.RequestCount, "B never quits");
        Assert.IsTrue(ship.Player.InputEnabled, "B never freezes the player");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "B never leaves the ship");
    }

    // ====================================================================
    // 14.6 / 14.7 - ONE authoritative panel state, no duplicate manager
    // ====================================================================

    [UnityTest]
    public IEnumerator SinglePanelAuthority_NoDuplicateManager_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        Assert.AreEqual(1, Object.FindObjectsByType<ShipOverlayUi>().Length,
            "exactly one overlay authority in the ship scene");
        Assert.AreEqual(1, Object.FindObjectsByType<ShipDeployConfirmPage>().Length,
            "exactly one deployment confirmation page");
        Assert.AreEqual(1, Object.FindObjectsByType<ShipLoadoutPage>().Length,
            "exactly one shared loadout page");
        Assert.AreEqual(1, Object.FindObjectsByType<EquipmentLockerInteractable>().Length,
            "exactly one equipment locker interactable");
        Assert.AreEqual(1, Object.FindObjectsByType<ShipAirlockInteractable>().Length,
            "exactly one deployment station interactable");
        Assert.IsTrue(ReferenceEquals(ship.Overlay, overlay),
            "ShipGameController exposes the ONE overlay authority");

        // No duplicate panel / modal manager may exist anywhere.
        string[] forbidden = { "EquipmentPanel2", "PreparationPanel2", "SecondModalManager" };
        foreach (string name in forbidden)
        {
            bool found = false;
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType(name) != null
                    || assembly.GetType("Assets._Project.Scripts.Gameplay." + name) != null)
                {
                    found = true;
                    break;
                }
            }

            Assert.IsFalse(found, "no duplicate panel type exists: " + name);
        }

        // The locker and the deployment station share the SAME loadout page
        // instance (one authority, one GameSession loadout).
        yield return OpenPanel(ship, overlay, PanelKind.Loadout);
        ShipLoadoutPage fromLocker = overlay.LoadoutPage;
        Assert.IsNotNull(fromLocker, "the loadout page is bound");
        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(overlay.IsOpen, "closed again");

        yield return OpenPanel(ship, overlay, PanelKind.DeploymentConfirm);
        yield return WaitBound(overlay.DeployPage.Root);
        Button adjust = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.AdjustGearButtonName);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(adjust), "调整装备 clicked");
        yield return WaitUntil(() => overlay.Mode == ShipOverlayUi.OverlayMode.Loadout);

        Assert.IsTrue(ReferenceEquals(overlay.LoadoutPage, fromLocker),
            "both entry points edit the SAME loadout page (no second panel)");
        Assert.AreEqual(1, Object.FindObjectsByType<ShipLoadoutPage>().Length,
            "still exactly one loadout page after both entry points");
    }

    // ====================================================================
    // helpers
    // ====================================================================

    private enum PanelKind
    {
        Loadout,
        DeploymentConfirm
    }

    /// <summary>
    /// Stands at the panel's authored pose, lets the real gaze resolve its
    /// interactable and presses A on the virtual gamepad - the exact path a
    /// controller player takes.
    /// </summary>
    private IEnumerator OpenPanel(ShipGameController ship, ShipOverlayUi overlay,
        PanelKind kind)
    {
        Vector3 pose = kind == PanelKind.Loadout ? LockerPose : DeployPose;
        float yaw = kind == PanelKind.Loadout ? LockerYaw : DeployYaw;
        object target = kind == PanelKind.Loadout
            ? (object)Object.FindAnyObjectByType<EquipmentLockerInteractable>()
            : ship.Airlock;

        Assert.IsNotNull(target, kind + " interactable present");

        ship.Player.TeleportTo(pose);
        ship.Player.SetFacing(yaw, 0f);
        yield return null;
        Physics.SyncTransforms();

        yield return WaitUntil(() =>
        {
            ship.Interactor.RefreshTarget();
            return ReferenceEquals(ship.Interactor.CurrentTarget, target)
                   && ship.Interactor.HasValidTarget;
        });

        Assert.IsTrue(ReferenceEquals(ship.Interactor.CurrentTarget, target),
            "the gaze resolved " + kind + " from its authored pose");

        yield return PressButton(GamepadButton.South);

        ShipOverlayUi.OverlayMode expected = kind == PanelKind.Loadout
            ? ShipOverlayUi.OverlayMode.Loadout
            : ShipOverlayUi.OverlayMode.DeploymentConfirm;
        yield return WaitUntilReal(() => overlay.IsOpen && overlay.Mode == expected, 5f);

        Assert.IsTrue(overlay.IsOpen && overlay.Mode == expected,
            "A on " + kind + " opened its panel (mode=" + overlay.Mode + ")");
        yield return WaitBound(kind == PanelKind.Loadout
            ? overlay.LoadoutPage.Root
            : overlay.DeployPage.Root);
        yield return null;
    }

    /// <summary>Points the player back at a target and waits for the gaze.</summary>
    private static IEnumerator FaceTarget(ShipGameController ship, object target)
    {
        Vector3 pose = ReferenceEquals(target, ship.Airlock) ? DeployPose : LockerPose;
        float yaw = ReferenceEquals(target, ship.Airlock) ? DeployYaw : LockerYaw;

        ship.Player.TeleportTo(pose);
        ship.Player.SetFacing(yaw, 0f);
        yield return null;
        Physics.SyncTransforms();

        int frames = 0;
        while (!(ReferenceEquals(ship.Interactor.CurrentTarget, target)
                 && ship.Interactor.HasValidTarget) && frames < MaxWaitFrames)
        {
            frames++;
            ship.Interactor.RefreshTarget();
            yield return null;
        }
    }

    private static IEnumerator EnterShip(System.Action<ShipGameController> onShip,
        System.Action<ShipOverlayUi> onOverlay)
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntilReal(() => ActiveSceneName() == GameSession.ShipInteriorSceneName, 15f);

        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady && ship.Player != null
                   && ship.Overlay != null;
        }, 25f);

        Assert.IsNotNull(ship, "the ship interior is ready");
        Assert.IsNotNull(ship.Overlay, "the overlay authority is ready");

        // The entry eyelids (title-screen hand-over) lock input for a moment;
        // wait for the real release so this suite never races a presentation.
        yield return WaitUntilReal(() => !ship.IsEntryInputLocked, 8f);
        Assert.IsFalse(ship.IsEntryInputLocked, "first-person control is available");

        ShipOverlayUi overlay = ship.Overlay;
        yield return WaitUntil(() => overlay.DeployPage != null && overlay.LoadoutPage != null
                                     && overlay.DeployPage.IsBound
                                     && overlay.LoadoutPage.IsBound);
        Assert.IsTrue(overlay.DeployPage.IsBound, "the deployment panel is bound");
        Assert.IsTrue(overlay.LoadoutPage.IsBound, "the loadout panel is bound");
        Assert.IsTrue(ship.PauseMenu != null && ship.PauseMenu.IsReady,
            "the pause menu is ready");

        onShip(ship);
        onOverlay(overlay);
    }

    private static string ActiveSceneName()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "<none>";
    }

    /// <summary>Queues a real press/release of one face, d-pad or menu button.</summary>
    private IEnumerator PressButton(GamepadButton button)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
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

    private static IEnumerator WaitUntilReal(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitBound(VisualElement root)
    {
        yield return WaitUntil(() => root != null && root.panel != null
                                     && root.childCount > 0);
        yield return null;
        yield return null;
    }
}
