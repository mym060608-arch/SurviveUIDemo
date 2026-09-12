using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Escape priority contract: Escape is BACK-OR-PAUSE-FALLBACK.
///
///   Pause menu open      + Esc -> resume, nothing else
///   Device panel open    + Esc -> close that panel, no pause
///   Wrist terminal open  + Esc -> close the terminal, no pause
///   Console view focused + Esc -> leave the view, no pause
///   Free first person    + Esc -> open the pause
///
/// Gamepad is unchanged: Menu/Start is the GLOBAL pause in every layer (and the
/// layer underneath stays frozen as the state 继续游戏 returns to), while
/// buttonEast is the LOCAL Back of whichever layer owns Back.
///
/// Everything is driven through the real path: a virtual gamepad delivering real
/// Input System state events, the real interactor, the real overlay / wrist /
/// console-focus controllers. Keyboard Escape uses the project's documented
/// automation seam (UiInput.InjectEscapeForAutomation) because a batch-mode
/// player cannot deliver keyboard state to a synthetic Keyboard device - the
/// physical mapping itself is pinned by UiInput.BindingTable.
///
/// The suites also pin that the resolution is STATE based: UiInput.ActiveBackLayer
/// must report the highest-priority open layer, never "whoever updated first".
/// </summary>
public class SurviveEscPriorityTests
{
    private const int MaxWaitFrames = 400;

    private static readonly Vector3 LockerPose = new Vector3(-1.55f, 0f, -12.6f);
    private const float LockerYaw = 270f;
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
        UiInput.ResetLayersForAutomation();

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
        UiInput.ResetLayersForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // Free first person: Escape is the global pause
    // ====================================================================

    [UnityTest]
    public IEnumerator FreeFirstPerson_EscapePauses_AndEscapeResumes_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "nothing local can go back in free first person");
        Assert.IsTrue(ship.Player.InputEnabled, "exploration input is live");

        // ---- Escape in free first person -> pause ----
        yield return PressEscape();

        Assert.IsTrue(ship.PauseMenu.IsOpen, "Escape pauses in free first person");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "and freezes the game");
        Assert.AreEqual(UiInput.UiLayer.PauseMenu, UiInput.ActiveBackLayer,
            "the pause is now the layer that owns Back");
        Assert.IsFalse(ship.Player.InputEnabled, "movement and look are frozen");

        // ---- Escape in the pause -> continue ----
        yield return PressEscape();

        Assert.IsFalse(ship.PauseMenu.IsOpen, "Escape inside the pause resumes");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "time runs again");
        Assert.IsTrue(ship.Player.InputEnabled, "control is handed back");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "and nothing local owns Back again");
        Assert.AreEqual(0, AppQuit.RequestCount, "Escape never quits");
    }

    // ====================================================================
    // Device panels: Escape is that panel's Back key
    // ====================================================================

    [UnityTest]
    public IEnumerator EquipmentPanel_EscapeClosesPanelOnly_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return OpenPanel(ship, overlay, PanelKind.Loadout);
        Assert.AreEqual(UiInput.UiLayer.Modal, UiInput.ActiveBackLayer,
            "the open equipment panel owns Back");
        Assert.AreEqual(0, overlay.BackActionCount, "opening is not a Back action");

        yield return PressEscape();

        Assert.IsFalse(overlay.IsOpen, "Escape closes 装备整理");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly ONE Back action");
        Assert.IsFalse(ship.PauseMenu.IsOpen,
            "Escape in the panel layer must NOT also open the pause");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "no accidental freeze");
        Assert.IsTrue(ship.Player.InputEnabled, "movement and look are restored");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "the panel released the Back ownership");
        Assert.AreEqual(0, AppQuit.RequestCount, "Escape never quits");
    }

    [UnityTest]
    public IEnumerator DeploymentPanel_EscapeClosesPanelOnly_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return OpenPanel(ship, overlay, PanelKind.DeploymentConfirm);
        Assert.AreEqual(UiInput.UiLayer.Modal, UiInput.ActiveBackLayer,
            "the open deployment panel owns Back");

        yield return PressEscape();

        Assert.IsFalse(overlay.IsOpen, "Escape closes 出征前准备");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly ONE Back action");
        Assert.IsFalse(ship.PauseMenu.IsOpen,
            "the deployment panel's Escape never also pauses");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "no freeze");
        Assert.IsTrue(ship.Player.InputEnabled, "control restored");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "Escape never leaves the ship");
    }

    // ====================================================================
    // Wrist terminal: Escape closes it and nothing else
    // ====================================================================

    [UnityTest]
    public IEnumerator Wrist_EscapeClosesTerminalOnly_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        // The Wrist action opens the authority (real Xbox Y through UiInput).
        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(ship.IsWristOpen, "Y opens the wrist authority");
        yield return WaitUntilReal(() => ship.IsWristTerminalRevealed, 4f);
        Assert.IsTrue(ship.IsWristTerminalRevealed, "the terminal is presented");
        Assert.AreEqual(UiInput.UiLayer.Wrist, UiInput.ActiveBackLayer,
            "the open wrist owns Back");
        Assert.IsFalse(ship.Player.InputEnabled, "the wrist captures input");

        yield return PressEscape();

        Assert.IsFalse(ship.IsWristOpen, "Escape closes the wrist terminal");
        Assert.IsFalse(ship.IsWristTerminalRevealed, "the terminal is released");
        Assert.IsFalse(ship.PauseMenu.IsOpen,
            "Escape on the wrist must NOT also open the pause");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "no freeze");
        Assert.IsTrue(ship.Player.InputEnabled, "free first person is restored");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "the wrist released the Back ownership");
    }

    // ====================================================================
    // Console view: the reported bug - Escape used to open the pause
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleFocus_EscapeLeavesViewWithoutPause_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        // Stand where the console view will start from, so the pose it must
        // return to is the one measured here.
        yield return StandAtConsole(ship);
        Vector3 freePose = ship.Player.EyeCamera.transform.position;
        Quaternion freeRotation = ship.Player.EyeCamera.transform.rotation;

        yield return EnterConsoleFocus(ship);
        Assert.AreEqual(UiInput.UiLayer.ConsoleFocus, UiInput.ActiveBackLayer,
            "the focused console owns Back");
        Assert.IsFalse(ship.Player.InputEnabled, "the view freezes movement and look");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "no pause while focused");

        // ---- The fix: Escape leaves the console view, it does NOT pause ----
        yield return PressEscape();

        bool pausedDuringExit = false;
        yield return WaitUntil(() =>
        {
            pausedDuringExit |= ship.PauseMenu.IsOpen;
            return ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore;
        }, 600);

        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore,
            ship.Focus.State, "Escape leaves the console view");
        Assert.IsFalse(pausedDuringExit, "the pause never opened while leaving");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "and it is not open afterwards");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "no freeze");
        Assert.IsTrue(ship.Player.InputEnabled,
            "movement, look and world interaction are restored");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "free first person owns Back again");

        // The camera really came back to the free first-person pose.
        Vector3 camDelta = ship.Player.EyeCamera.transform.position - freePose;
        Assert.Less(new Vector2(camDelta.x, camDelta.z).magnitude, 0.35f,
            "the camera returned to the exploration pose (delta " + camDelta + ")");
        Assert.Less(Quaternion.Angle(freeRotation,
                ship.Player.EyeCamera.transform.rotation), 5f,
            "and the eye is back on its free first-person orientation");

        // ---- and the SAME Escape now pauses, because nothing local is left ----
        yield return PressEscape();
        Assert.IsTrue(ship.PauseMenu.IsOpen,
            "once free first person is back, Escape is the pause key again");
        yield return PressEscape();
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Escape resumes again");
    }

    // ====================================================================
    // Menu/Start keeps pausing above the local layer, which stays underneath
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleFocus_MenuStart_PausesAboveIt_AndContinueReturns_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        yield return EnterConsoleFocus(ship);

        // ---- Menu/Start is the global pause, in every layer ----
        yield return PressButton(GamepadButton.Start);

        Assert.IsTrue(ship.PauseMenu.IsOpen, "Menu/Start pauses above the console view");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "and freezes");
        Assert.IsTrue(ship.IsConsoleFocusActive,
            "the console view stays as the frozen underlying state");
        Assert.AreEqual(UiInput.UiLayer.PauseMenu, UiInput.ActiveBackLayer,
            "the pause outranks the console view while it is open");

        // ---- Continue returns to exactly that console view ----
        yield return PressEscape();

        Assert.IsFalse(ship.PauseMenu.IsOpen, "Escape resumes the game");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "time runs again");
        Assert.IsTrue(ship.IsConsoleFocusActive,
            "继续游戏 returns to the console view, not to free first person");
        Assert.IsFalse(ship.Player.InputEnabled, "the console view still owns input");
        Assert.AreEqual(UiInput.UiLayer.ConsoleFocus, UiInput.ActiveBackLayer,
            "and the console view owns Back again once the pause is gone");

        // ---- so the next Escape is the console view's Back, not a pause ----
        yield return PressEscape();
        yield return WaitUntil(() => ship.Focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore, 600);

        Assert.IsFalse(ship.PauseMenu.IsOpen, "that Escape left the view instead");
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore,
            ship.Focus.State, "the console view closed through its own Back");
        Assert.IsTrue(ship.Player.InputEnabled, "free first person is back");
    }

    // ====================================================================
    // The stacked case: pause above a device panel (one press, one action)
    // ====================================================================

    [UnityTest]
    public IEnumerator PauseAbovePanel_EscapeResumesOnly_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        yield return OpenPanel(ship, overlay, PanelKind.Loadout);

        // Menu/Start pauses ABOVE the open panel; the panel stays open.
        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "Menu/Start pauses above the panel");
        Assert.IsTrue(overlay.IsOpen, "the panel stays open underneath");
        Assert.AreEqual(UiInput.UiLayer.PauseMenu, UiInput.ActiveBackLayer,
            "the pause outranks the modal panel");

        // Escape #1 belongs to the pause and must not touch the panel.
        yield return PressEscape();
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Escape resumes the pause");
        Assert.IsTrue(overlay.IsOpen, "the panel underneath is untouched");
        Assert.AreEqual(0, overlay.BackActionCount,
            "the panel saw no Back action at all");

        // Escape #2 now belongs to the panel.
        Assert.AreEqual(UiInput.UiLayer.Modal, UiInput.ActiveBackLayer,
            "the panel owns Back again after the pause closed");
        yield return PressEscape();
        Assert.IsFalse(overlay.IsOpen, "the second Escape closes the panel");
        Assert.AreEqual(1, overlay.BackActionCount, "exactly one Back action");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "no pause was stacked");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "everything is closed and Back falls back to the pause");
    }

    // ====================================================================
    // The same layer priority for the gamepad's LOCAL Back (buttonEast)
    // ====================================================================

    [UnityTest]
    public IEnumerator GamepadBack_FollowsTheSameLayerPriority_Green()
    {
        ShipGameController ship = null;
        ShipOverlayUi overlay = null;
        yield return EnterShip(value => ship = value, value => overlay = value);

        // Free first person: B does nothing at all (no local layer, no pause).
        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "B never pauses");
        Assert.IsFalse(overlay.IsOpen, "B opens nothing");
        Assert.IsTrue(ship.Player.InputEnabled, "B never freezes the player");

        // Console view: B leaves the view (its local Back).
        yield return EnterConsoleFocus(ship);
        yield return PressButton(GamepadButton.East);
        yield return WaitUntil(() => ship.Focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore, 600);
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore,
            ship.Focus.State, "B leaves the console view");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "B never pauses inside the view");

        // Menu/Start stays the global pause.
        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "Menu/Start is still the global pause");
        yield return PressButton(GamepadButton.Start);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Menu/Start resumes it again");
    }

    // ====================================================================
    // Helpers (real input path + real controllers)
    // ====================================================================

    private enum PanelKind
    {
        Loadout,
        DeploymentConfirm
    }

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

    /// <summary>Walks the player to the first bridge console's approach pose.</summary>
    private static IEnumerator StandAtConsole(ShipGameController ship)
    {
        ShipConsoleInteractable console = ship.Consoles[0];
        Vector3 dir = new Vector3(0f, 0f, -1f);
        ship.Player.TeleportTo(console.transform.position + dir * 1.7f);
        ship.Player.SetFacing(Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return null;
        Physics.SyncTransforms();
    }

    /// <summary>Walks to the first bridge console and focuses it through the real interactor.</summary>
    private static IEnumerator EnterConsoleFocus(ShipGameController ship)
    {
        yield return StandAtConsole(ship);

        ShipConsoleInteractable console = ship.Consoles[0];
        bool entered = false;
        for (int frame = 0; frame < MaxWaitFrames && !entered; frame++)
        {
            ship.Interactor.RefreshTarget();
            if (ReferenceEquals(ship.Interactor.CurrentTarget, console)
                && ship.Interactor.HasValidTarget)
            {
                entered = ship.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Assert.IsTrue(entered, "the real interactor entered the console view");
        yield return WaitUntil(() => ship.Focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus, 600);
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus,
            ship.Focus.State, "the console view reached its focused state");
    }

    /// <summary>
    /// Presses Escape through the project's automation seam (the physical mapping
    /// is pinned by UiInput.BindingTable) and lets the layer resolution run.
    /// </summary>
    private static IEnumerator PressEscape()
    {
        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return null;
        yield return null;
    }

    private static IEnumerator EnterShip(System.Action<ShipGameController> onShip)
    {
        return EnterShip(onShip, null);
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
                   && ship.Overlay != null && ship.Focus != null;
        }, 25f);

        Assert.IsNotNull(ship, "the ship interior is ready");

        yield return WaitUntilReal(() => !ship.IsEntryInputLocked, 8f);
        Assert.IsFalse(ship.IsEntryInputLocked, "first-person control is available");

        ShipOverlayUi overlay = ship.Overlay;
        yield return WaitUntil(() => overlay.DeployPage != null && overlay.LoadoutPage != null
                                     && overlay.DeployPage.IsBound
                                     && overlay.LoadoutPage.IsBound);
        Assert.IsTrue(ship.PauseMenu != null && ship.PauseMenu.IsReady,
            "the pause menu is ready");

        onShip(ship);
        onOverlay?.Invoke(overlay);
    }

    private static string ActiveSceneName()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "<none>";
    }

    /// <summary>Queues a real press/release of one face or menu button.</summary>
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
