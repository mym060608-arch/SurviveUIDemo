using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase 09A_3 — block / voxel first-person arms finalization.
///
/// Runtime behaviour of the replacement presentation: a code-built block-arm set
/// driven purely by transform interpolation, with the existing gameplay systems
/// still owning every decision. Everything here is driven through the same paths
/// a player uses (virtual Gamepad/Keyboard events, the authoritative player
/// controller, the ship coordinator and the existing wrist terminal).
///
/// Covered contract:
///   one block-arms instance under the player rig, no legacy FBX arms at runtime,
///   level gaze keeps the arms out of the frustum and a look-down brings them in,
///   Walk presentation follows the authoritative movement,
///   Wrist raise -> hold -> lower through the real Wrist gamepad binding,
///   the existing terminal still works with the arms presentation removed,
///   pause freezes / resumes, console focus and device panels hide and restore,
///   return-to-main-menu cleans up and continue creates exactly one instance,
///   the arms never join physics or the interaction ray.
/// </summary>
public class SurvivePhase09A3BlockArmsTests
{
    private Gamepad _pad;
    private Keyboard _keyboard;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        MainMenuSettings.ResetToDefaults();
        InputDeviceTracker.ResetToDefaults();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        if (_keyboard != null && _keyboard.added)
        {
            InputSystem.RemoveDevice(_keyboard);
        }

        _pad = null;
        _keyboard = null;
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // PASS 1 — runtime replacement
    // ====================================================================

    [UnityTest]
    public IEnumerator BlockArms_ReplaceTheLegacyPresentation_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Assert.AreEqual(1, CountBlockArms(), "exactly one block arms instance exists");
        Assert.AreEqual(0, Object.FindObjectsByType<FirstPersonArmsController>(
                FindObjectsInactive.Include).Length,
            "the legacy FBX arms are no longer created at runtime");

        FirstPersonBlockArmsController arms = ship.Arms;
        Assert.IsNotNull(arms, "the ship controller exposes the block arms");
        Assert.AreEqual(_player.transform, arms.transform.parent,
            "the arms hang in body space on the player rig");
        Assert.AreEqual(Mathf.Abs(_player.eyeHeight - arms.transform.localPosition.y) < 0.001f,
            true, "the arms root sits at eye height");
        Assert.IsNotNull(arms.Watch, "the left wrist terminal exists");
        Assert.Greater(arms.RendererCount, 6, "the block arms are assembled from primitives");

        int layer = LayerMask.NameToLayer(FirstPersonBlockArmsController.ArmsLayerName);
        Assert.Greater(layer, 0, "the FirstPersonArms layer exists");
        Assert.AreEqual(layer, arms.gameObject.layer, "the arms root is on that layer");
        Assert.AreEqual(0, arms.GetComponentsInChildren<Collider>(true).Length,
            "block arms carry no collider");
        Assert.AreEqual(1, Object.FindObjectsByType<WristPresentationBridge>(
                FindObjectsInactive.Include).Length,
            "exactly one wrist presentation bridge");

        foreach (Renderer renderer in arms.GetComponentsInChildren<Renderer>(true))
        {
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,
                renderer.shadowCastingMode, renderer.name + " casts no shadow");
        }
    }

    // ====================================================================
    // PASS 2 — forward hidden / look down visible
    // ====================================================================

    [UnityTest]
    public IEnumerator Forward_HidesArms_And_LookDown_ShowsBoth_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        FirstPersonBlockArmsController arms = ship.Arms;

        // level gaze: no arm geometry inside the camera frustum
        _player.SetFacing(180f, 0f);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(0, ArmsInsideFrustum(arms),
            "a level gaze keeps every block-arm renderer out of the frustum");

        // look down: both arms enter the frame, left and right
        _player.SetFacing(180f, 45f);
        yield return new WaitForSecondsRealtime(0.4f);
        int visible = ArmsInsideFrustum(arms);
        Assert.GreaterOrEqual(visible, 4,
            "looking down brings the arms into the frustum (visible renderers "
            + visible + ")");

        bool leftInside = RendererInsideFrustum(arms.LeftArmRoot.GetComponentsInChildren<Renderer>());
        bool rightInside = RendererInsideFrustum(arms.RightArmRoot.GetComponentsInChildren<Renderer>());
        Assert.IsTrue(leftInside && rightInside,
            "both the left and the right arm are visible when looking down");
    }

    /// <summary>Walk presentation comes from the authoritative movement state.</summary>
    [UnityTest]
    public IEnumerator Walk_Presentation_FollowsAuthoritativeMovement_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        FirstPersonBlockArmsController arms = ship.Arms;

        _player.SetFacing(180f, 35f);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.AreEqual("Rest", arms.CurrentStateName, "standing still presents Rest");

        Vector3 before = _player.transform.position;
        InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = new Vector2(0f, 0.9f) });
        yield return WaitUntilReal(() => arms.CurrentStateName == "Walk", 2f);
        Assert.AreEqual("Walk", arms.CurrentStateName, "moving presents Walk");
        Assert.IsTrue(arms.IsMoving, "the movement state is read from the rig");

        // Hold the stick long enough for the rig to actually travel: the state
        // flips as soon as the authoritative speed crosses the threshold, which
        // can be within a couple of frames on a fast machine.
        float holdDeadline = Time.realtimeSinceStartup + 0.8f;
        while (Time.realtimeSinceStartup < holdDeadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        Assert.Greater((_player.transform.position - before).magnitude, 0.3f,
            "the player really moved");

        yield return WaitUntilReal(() => arms.CurrentStateName == "Rest", 2f);
        Assert.AreEqual("Rest", arms.CurrentStateName, "stopping returns to Rest");
    }

    // ====================================================================
    // PASS 3 — wrist raise / hold / lower
    // ====================================================================

    [UnityTest]
    public IEnumerator Wrist_RaiseHoldLower_WithTheWatchInTheLowerLeft_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        FirstPersonBlockArmsController arms = ship.Arms;

        _player.SetFacing(180f, 0f);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.IsFalse(ship.IsWristOpen, "the wrist starts closed");
        Assert.IsFalse(ship.IsWristTerminalRevealed, "no terminal is presented");

        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(ship.IsWristOpen, "the Wrist binding opens the authority");
        Assert.IsFalse(ship.IsWristTerminalRevealed,
            "the existing terminal is not shown on the key press");
        Assert.AreEqual(FirstPersonBlockArmsController.WristPhase.Raise,
            arms.CurrentWristPhase, "the block arm starts raising");
        Assert.IsFalse(ship.Player.InputEnabled, "movement stays frozen");

        // the raise is a smooth blend, never a hard cut
        yield return WaitUntilReal(() => arms.WristPresentationBlend > 0.3f, 2f);
        float mid = arms.WristPresentationBlend;
        Assert.Greater(mid, 0.05f, "the raise starts from the hanging pose");
        Assert.Less(mid, 0.99f, "the raise is interpolated over time, not snapped");
        Assert.AreEqual("WristRaise", arms.CurrentStateName, "the raise phase is reported");

        yield return WaitUntilReal(() => ship.IsWristTerminalRevealed, 3f);
        Assert.IsTrue(ship.IsWristTerminalRevealed, "the terminal is revealed");
        float progress = ship.WristPresentation.LastRaiseProgress;
        Assert.Greater(progress, 0.7f,
            "the reveal waited for the raise (progress " + progress + ")");
        Assert.IsTrue(ship.Wrist.Display.Visible, "the existing wrist display is shown");
        Assert.IsTrue(arms.IsSoftened,
            "the physical arms are softened while the hologram is up");
        Assert.IsTrue(arms.IsWristScreenActive, "the watch screen is active");

        yield return WaitUntilReal(() => arms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Hold, 2f);
        Assert.AreEqual("WristHold", arms.CurrentStateName, "the hold phase is reached");

        // The watch is held in the lower-left of the view direction. That is the
        // aspect-independent contract: the same pose is measured on screen at the
        // product resolution by the standalone evidence run (1920x1080, see
        // export/phase09a3_shots/phase09a3_result.txt).
        Vector3 watchLocal = ship.Player.EyeCamera.transform.InverseTransformPoint(
            arms.Watch.position);
        Assert.Less(watchLocal.x, 0f, "the watch is held left of the view axis");
        Assert.Less(watchLocal.y, 0f, "the watch is held below the view axis");
        Assert.Greater(watchLocal.z, 0.05f, "the watch is held in front of the eye");

        Vector3 watch = arms.WatchViewport;
        if (Mathf.Abs(ship.Player.EyeCamera.aspect - 16f / 9f) < 0.01f)
        {
            Assert.IsTrue(arms.WatchOnScreen,
                "the watch is inside the viewport " + watch.ToString("F3"));
            Assert.Less(watch.x, 0.45f, "the watch sits in the left part of the screen");
            Assert.Less(watch.y, 0.5f, "the watch sits in the lower part of the screen");
        }

        // UI above / watch below: the watch is left of and below the hologram pivot
        Assert.IsNotNull(ship.Wrist.ProjectionPivot, "the existing wrist pivot exists");
        Vector3 pivotViewport = ship.Player.EyeCamera.WorldToViewportPoint(
            ship.Wrist.ProjectionPivot.position);
        Assert.Less(watch.x, pivotViewport.x, "the watch stays left of the UI pivot");
        Assert.Less(watch.y, pivotViewport.y + 0.02f,
            "the watch stays below the UI pivot, never over the hologram");

        // close
        yield return PressButton(GamepadButton.North);
        Assert.IsFalse(ship.IsWristOpen, "the Wrist binding closes the authority");
        Assert.IsFalse(ship.IsWristTerminalRevealed, "the terminal releases immediately");
        Assert.AreEqual(FirstPersonBlockArmsController.WristPhase.Lower,
            arms.CurrentWristPhase, "the arm starts lowering");

        yield return WaitUntilReal(() => arms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Rest, 4f);
        Assert.AreEqual("Rest", arms.CurrentStateName, "the arm returns to Rest");
        Assert.IsFalse(arms.IsSoftened, "the softening ends with the hologram");
        Assert.IsTrue(ship.Player.InputEnabled, "exploration input is restored");
    }

    /// <summary>
    /// The block arms are presentation only: with the bridge removed the existing
    /// wrist system behaves exactly as it did before this phase.
    /// </summary>
    [UnityTest]
    public IEnumerator Wrist_ExistingSystemStillAuthoritative_WithoutBlockArms_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Object.Destroy(ship.WristPresentation);
        for (int i = 0; i < 3; i++)
        {
            yield return null;
        }

        Assert.IsTrue(ship.WristPresentation == null, "the presentation bridge is gone");

        ship.SetWristOpen(true);
        yield return null;

        Assert.IsTrue(ship.IsWristOpen, "the authority still opens the wrist");
        Assert.IsTrue(ship.IsWristTerminalRevealed,
            "without the arms the terminal opens immediately (Phase 08 behaviour)");
        Assert.IsTrue(ship.Wrist.Display.Visible, "the existing terminal is presented");
        Assert.IsFalse(ship.Player.InputEnabled, "the existing input lock is unchanged");

        ship.SetWristOpen(false);
        yield return null;
        Assert.IsFalse(ship.IsWristTerminalRevealed, "closing hides the terminal again");
        Assert.IsTrue(ship.Player.InputEnabled, "and releases the player");
    }

    // ====================================================================
    // PASS 4 — compatibility
    // ====================================================================

    [UnityTest]
    public IEnumerator Pause_FreezesAndRestores_TheBlockArms_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        FirstPersonBlockArmsController arms = ship.Arms;

        _player.SetFacing(180f, 0f);
        ship.SetWristOpen(true);
        yield return WaitUntilReal(() => arms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Hold, 2f);
        yield return new WaitForSecondsRealtime(0.2f);

        string stateBefore = arms.CurrentStateName;
        float blendBefore = arms.WristPresentationBlend;

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "the pause menu is open");
        yield return new WaitForSecondsRealtime(0.4f);

        Assert.IsTrue(arms.IsFrozen, "the block arms report themselves frozen");
        Assert.AreEqual(stateBefore, arms.CurrentStateName, "the state is frozen");
        Assert.AreEqual(blendBefore, arms.WristPresentationBlend, 0.001f,
            "the raise blend is frozen");
        Assert.IsTrue(ship.IsWristOpen && ship.IsWristTerminalRevealed,
            "the terminal stays presented while paused");

        yield return PressButton(GamepadButton.Start);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "the pause closes again");
        yield return WaitUntilReal(() => !arms.IsFrozen, 1f);
        Assert.AreEqual(stateBefore, arms.CurrentStateName,
            "continue resumes the same presentation state");

        ship.SetWristOpen(false);
        yield return WaitUntilReal(() => arms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Rest, 3f);
    }

    [UnityTest]
    public IEnumerator ConsoleFocus_HidesTheBlockArms_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        Assert.IsTrue(ship.Arms.IsVisible, "arms visible in free first person");

        ShipConsoleInteractable console = FindConsole(ship, 1);
        Assert.IsNotNull(console, "console 1 exists");
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);
        Assert.IsTrue(ship.Interactor.TryInteract(), "console focus starts");

        yield return WaitUntilReal(() => ship.IsConsoleFocusActive, 4f);
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.IsFalse(ship.Arms.IsVisible,
            "the block arms are hidden so they cannot cover the console screen");

        ship.Focus.RequestExit();
        yield return WaitUntilReal(() => !ship.IsConsoleFocusActive, 4f);
        yield return WaitUntilReal(() => ship.Arms.IsVisible, 2f);
        Assert.IsTrue(ship.Arms.IsVisible, "the arms come back after leaving the view");
    }

    [UnityTest]
    public IEnumerator DevicePanel_HidesTheBlockArms_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Assert.IsTrue(ship.Overlay.OpenLoadout(ShipOverlayUi.LoadoutReturnTarget.ShipExplore),
            "the shipboard device panel opens");
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.IsFalse(ship.Arms.IsVisible, "the block arms never cover the panel");

        ship.Overlay.CloseToExplore();
        yield return new WaitForSecondsRealtime(0.2f);
        yield return WaitUntilReal(() => ship.Arms.IsVisible, 2f);
        Assert.IsTrue(ship.Arms.IsVisible, "the arms are restored after Back");
    }

    [UnityTest]
    public IEnumerator ReturnToMainMenu_CleansUp_AndContinueCreatesOne_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        Assert.AreEqual(1, CountBlockArms(), "exactly one instance in the interior");

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open");
        ship.PauseMenu.SetFocus(1);
        Assert.IsTrue(ship.PauseMenu.ConfirmFocused(), "返回主菜单 confirmed");

        yield return WaitUntilReal(() => SceneManager.GetActiveScene().name
            == SceneRouting.MainMenuSceneName, 15f);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(0, CountBlockArms(), "no block arms survive the return to the menu");
        Assert.AreEqual(0, Object.FindObjectsByType<WristPresentationBridge>(
                FindObjectsInactive.Include).Length,
            "no wrist bridge survives either");

        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        Assert.IsNotNull(menu, "the title screen is ready");
        Assert.IsTrue(menu.IsContinueAvailable, "继续游戏 is offered");
        menu.RequestContinue();

        yield return WaitUntilReal(() => SceneManager.GetActiveScene().name
            == GameSession.ShipInteriorSceneName, 20f);
        yield return WaitUntilReal(() => CountBlockArms() == 1, 20f);
        yield return new WaitForSecondsRealtime(0.4f);

        Assert.AreEqual(1, CountBlockArms(), "continue creates exactly one instance");
        Assert.AreEqual(1, Object.FindObjectsByType<WristPresentationBridge>(
                FindObjectsInactive.Include).Length, "and exactly one wrist bridge");
    }

    [UnityTest]
    public IEnumerator BlockArms_NeverBlockTheInteractionRay_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Assert.AreEqual(0, ship.Arms.GetComponentsInChildren<Collider>(true).Length,
            "no collider exists on the block arms");

        ShipConsoleInteractable console = FindConsole(ship, 1);
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);
        Assert.AreSame(console, ship.Interactor.CurrentTarget,
            "the gaze ray still reaches the console");
        Assert.IsTrue(ship.Interactor.HasValidTarget, "the target stays interactable");
    }

    // ====================================================================
    // helpers
    // ====================================================================

    private SurfacePlayerController _player;

    private static int CountBlockArms()
    {
        return Object.FindObjectsByType<FirstPersonBlockArmsController>(
            FindObjectsInactive.Include).Length;
    }

    private static int ArmsInsideFrustum(FirstPersonBlockArmsController arms)
    {
        Camera eye = arms.transform.parent != null
            ? arms.transform.parent.GetComponentInChildren<Camera>(true)
            : null;
        if (eye == null)
        {
            return 0;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(eye);
        int count = 0;
        foreach (Renderer renderer in arms.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.enabled && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
            {
                count++;
            }
        }

        return count;
    }

    private static bool RendererInsideFrustum(Renderer[] renderers)
    {
        Camera eye = Camera.main;
        if (eye == null || renderers == null)
        {
            return false;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(eye);
        foreach (Renderer renderer in renderers)
        {
            if (renderer.enabled && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator EnterShip(System.Action<ShipGameController> capture)
    {
        _pad = InputSystem.AddDevice<Gamepad>();
        _keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.EnableDevice(_keyboard);
        Assert.AreSame(_pad, Gamepad.current, "virtual gamepad is current");

        GameSession session = GameSession.Ensure();
        session.ResetSession();
        session.BeginRun();

        SceneRouting.RequestEntryEyelid(SceneRouting.EntryEyelid.None);
        yield return LoadSceneStable(GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);

        Assert.IsNotNull(ship, "the ship interior is ready");
        capture(ship);
        _player = ship.Player;

        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.IsNotNull(ship.Arms, "the block arms were created");
        Assert.IsNotNull(ship.WristPresentation, "the wrist bridge was created");
    }

    private static IEnumerator LoadSceneStable(string sceneName)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            SceneManager.LoadScene(sceneName);
            yield return WaitUntilReal(() => SceneManager.GetActiveScene().name == sceneName, 15f);
            yield return new WaitForSecondsRealtime(0.25f);
            if (SceneManager.GetActiveScene().name == sceneName)
            {
                yield break;
            }
        }
    }

    private static ShipConsoleInteractable FindConsole(ShipGameController ship, int id)
    {
        foreach (ShipConsoleInteractable console in ship.Consoles)
        {
            if (console.ConsoleId == id)
            {
                return console;
            }
        }

        return null;
    }

    private static void FaceConsole(ShipGameController ship, ShipConsoleInteractable console)
    {
        Vector3 forward = console.transform.forward;
        ship.Player.TeleportTo(console.transform.position + forward * 1.9f);
        ship.Player.SetFacing(console.transform.eulerAngles.y + 180f, 0f);
    }

    private static IEnumerator WaitForGaze(ShipGameController ship,
        ShipConsoleInteractable console)
    {
        float deadline = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < deadline)
        {
            ship.Interactor.RefreshTarget();
            if (ship.Interactor.CurrentTarget == (IFirstPersonInteractable)console)
            {
                yield break;
            }

            yield return null;
        }
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

    private static IEnumerator WaitUntilReal(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
