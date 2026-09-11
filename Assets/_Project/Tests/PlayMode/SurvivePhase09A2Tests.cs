using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase 09A_2 first-person arms and wrist integration.
///
/// Real runtime verification, not asset existence: every state is driven through
/// the same paths a player uses - a virtual Gamepad/Keyboard delivering real
/// Input System events, the authoritative player controller, the ship
/// coordinator and the existing wrist terminal - and the assertions read the
/// live presentation state (Animator state, wrist phase, reveal flag, renderer
/// visibility, instance counts).
///
/// Covered contract:
///   one arms instance mounted under the authoritative eye camera,
///   Idle -> Walk -> Idle from the authoritative movement state,
///   Root Motion stays off and never moves the player,
///   the Wrist gamepad binding opens the authority immediately while the
///   existing terminal is revealed only once the raise animation arrives,
///   closing releases the terminal first and then lowers the arm,
///   pause freezes the presentation and continue restores it,
///   the existing wrist system keeps working with the arms presentation gone,
///   console focus and device panels hide the arms and restore them,
///   return-to-main-menu destroys the arms and continue creates exactly one,
///   the arms never join world physics or the interaction ray.
/// </summary>
public class SurvivePhase09A2Tests
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
    // Arms creation / asset contract
    // ====================================================================

    [UnityTest]
    public IEnumerator Arms_MountExactlyOnce_UnderTheEyeCamera_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        FirstPersonArmsController[] arms = Object.FindObjectsByType<FirstPersonArmsController>(
            FindObjectsInactive.Include);
        Assert.AreEqual(1, arms.Length, "entering the interior creates exactly one arms instance");

        FirstPersonArmsController instance = arms[0];
        Assert.AreEqual(FirstPersonArmsController.RootObjectName, instance.gameObject.name,
            "the runtime instance uses the contract root name");
        Assert.IsTrue(instance.transform.IsChildOf(ship.Player.EyeCamera.transform),
            "the arms follow the authoritative eye camera");

        int armsLayer = LayerMask.NameToLayer(FirstPersonArmsController.ArmsLayerName);
        Assert.Greater(armsLayer, 0, "the FirstPersonArms layer exists");
        Assert.AreEqual(armsLayer, instance.gameObject.layer, "the arms root is on that layer");
        Assert.AreNotEqual(0, ship.Player.EyeCamera.cullingMask & (1 << armsLayer),
            "the eye camera renders the arms layer");
        Assert.AreEqual(0,
            instance.GetComponentsInChildren<Collider>(true).Length,
            "the arms carry no collider (contract §57)");

        Animator animator = instance.Animator;
        Assert.IsNotNull(animator, "the arms carry an Animator");
        Assert.IsNotNull(animator.avatar, "the Animator has the generic avatar");
        Assert.IsTrue(animator.avatar.isValid, "the avatar is valid");
        Assert.IsFalse(animator.avatar.isHuman, "the avatar stays a generic rig");
        Assert.IsFalse(animator.applyRootMotion, "Root Motion is OFF");
        Assert.IsNotNull(animator.runtimeAnimatorController, "the animator controller is assigned");

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,
                renderer.shadowCastingMode,
                renderer.name + " casts no shadow (no new shadow-atlas cost)");
        }

        Assert.IsNotNull(ship.WristPresentation, "the wrist presentation bridge exists");
        Assert.AreEqual(1,
            Object.FindObjectsByType<WristPresentationBridge>(FindObjectsInactive.Include).Length,
            "exactly one wrist presentation bridge");
    }

    /// <summary>The six Codex clips are reachable under their exact names.</summary>
    [UnityTest]
    public IEnumerator Animator_ExposesTheSixCodexStates_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Animator animator = ship.Arms.Animator;
        Assert.IsNotNull(animator, "animator available");

        foreach (string state in new[]
                 {
                     FirstPersonArmsController.StateIdle,
                     FirstPersonArmsController.StateWalk,
                     FirstPersonArmsController.StateWristRaise,
                     FirstPersonArmsController.StateWristHold,
                     FirstPersonArmsController.StateWristLower,
                     FirstPersonArmsController.StateInteractRight
                 })
        {
            Assert.IsTrue(animator.HasState(0, Animator.StringToHash(state)),
                "the controller has the " + state + " state");
        }

        Assert.AreEqual(FirstPersonArmsController.StateIdle, ship.Arms.CurrentStateName,
            "the controller presents Idle by default");
    }

    // ====================================================================
    // Locomotion presentation
    // ====================================================================

    [UnityTest]
    public IEnumerator Locomotion_IdleWalkIdle_FromAuthoritativeMovement_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        FirstPersonArmsController arms = ship.Arms;
        Assert.IsNotNull(arms, "arms available");
        yield return SettlePresentation(arms, FirstPersonArmsController.StateIdle);

        Assert.AreEqual("Idle", arms.CurrentStateName, "standing still presents Idle");
        Assert.IsFalse(arms.IsMoving, "no movement is reported while standing");

        Vector3 positionBefore = ship.Player.transform.position;

        // A REAL left-stick push through the authoritative player controller.
        yield return PushStick(new Vector2(0f, 1f), 0.9f);

        Assert.AreEqual("Walk", arms.CurrentStateName, "walking presents Walk");
        Assert.IsTrue(arms.IsMoving, "the movement state is read from the rig, not from a key");
        Assert.Greater((ship.Player.transform.position - positionBefore).magnitude, 0.3f,
            "the player really moved");

        yield return ReleaseStick();
        yield return WaitUntilReal(() => arms.CurrentStateName == "Idle", 2f);

        Assert.AreEqual("Idle", arms.CurrentStateName, "stopping returns to Idle");

        Animator animator = arms.Animator;
        Assert.IsFalse(animator.applyRootMotion, "Root Motion stays OFF during locomotion");
        Vector3 armsLocal = arms.transform.localPosition;
        Assert.Less(armsLocal.magnitude, 0.001f,
            "the arms stay mounted on the camera instead of being pushed by the clip");
    }

    // ====================================================================
    // Wrist presentation chain
    // ====================================================================

    [UnityTest]
    public IEnumerator Wrist_GamepadBinding_RaisesThenRevealsTheTerminal_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        CollectionAssert.Contains(UiInput.BindingTable,
            "Wrist=Gamepad.buttonNorth,Keyboard.mKey|iKey",
            "the wrist binding is the semantic Wrist action on a free face button");

        Assert.IsFalse(ship.IsWristOpen, "the wrist starts closed");
        Assert.IsFalse(ship.IsWristTerminalRevealed, "no terminal is presented");
        Assert.IsTrue(ship.Arms.IsVisible, "the arms are presented in free first person");

        // Real Xbox Y / PlayStation triangle press.
        yield return PressButton(GamepadButton.North);

        Assert.IsTrue(ship.IsWristOpen, "the Wrist binding opens the authority state");
        Assert.IsFalse(ship.IsWristTerminalRevealed,
            "the existing terminal is NOT shown on the key press");
        Assert.AreEqual(FirstPersonArmsController.WristPhase.Raise, ship.Arms.CurrentWristPhase,
            "the arm starts raising");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.Wrist, ship.PointerOwner,
            "the wrist owns the pointer while it opens");
        Assert.IsFalse(ship.Player.InputEnabled, "movement stays frozen by the existing system");

        yield return WaitUntilReal(() => ship.IsWristTerminalRevealed, 3f);
        Assert.IsTrue(ship.IsWristTerminalRevealed, "the terminal is revealed");
        float progress = ship.WristPresentation.LastRaiseProgress;
        Assert.Greater(progress, 0.7f,
            "the reveal waited for the raise animation (progress was " + progress + ")");
        Assert.IsTrue(ship.Wrist.Display.Visible, "the existing wrist display is shown");
        Assert.IsTrue(ship.Arms.IsWristScreenActive, "the wrist screens glow while presented");

        yield return WaitUntilReal(() => ship.Arms.CurrentStateName == "WristHold", 3f);
        Assert.AreEqual("WristHold", ship.Arms.CurrentStateName, "the hold loop takes over");

        // Close: the existing terminal releases first, the arm follows.
        yield return PressButton(GamepadButton.North);

        Assert.IsFalse(ship.IsWristOpen, "the Wrist binding closes the authority state");
        Assert.IsFalse(ship.IsWristTerminalRevealed, "the terminal releases immediately");
        Assert.IsFalse(ship.Wrist.Display.Visible, "the existing display is hidden first");
        Assert.AreEqual(FirstPersonArmsController.WristPhase.Lower, ship.Arms.CurrentWristPhase,
            "the arm only starts lowering afterwards");

        yield return WaitUntilReal(() => ship.Arms.CurrentWristPhase
            == FirstPersonArmsController.WristPhase.Rest, 3f);
        Assert.AreEqual(FirstPersonArmsController.WristPhase.Rest, ship.Arms.CurrentWristPhase,
            "the presentation returns to rest");
        Assert.AreEqual("Idle", ship.Arms.CurrentStateName, "the arm is back on Idle");
        Assert.IsTrue(ship.Player.InputEnabled, "exploration input is restored");
    }

    /// <summary>
    /// Contract §58: the arms are presentation only. With the whole arms
    /// presentation removed the existing wrist system must behave exactly as it
    /// did before this phase - the terminal opens immediately.
    /// </summary>
    [UnityTest]
    public IEnumerator Wrist_ExistingSystemStillAuthoritative_WithoutArms_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        WristPresentationBridge bridge = ship.WristPresentation;
        Assert.IsNotNull(bridge, "bridge present before the check");
        Object.Destroy(bridge);
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
        Assert.IsTrue(ship.Wrist.Display.Visible, "the existing terminal really is presented");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.Wrist, ship.PointerOwner,
            "the existing pointer ownership is unchanged");
        Assert.IsFalse(ship.Player.InputEnabled, "the existing input lock is unchanged");

        ship.SetWristOpen(false);
        yield return null;
        Assert.IsFalse(ship.IsWristTerminalRevealed, "closing hides the terminal again");
        Assert.IsTrue(ship.Player.InputEnabled, "and releases the player");
    }

    [UnityTest]
    public IEnumerator Pause_FreezesAndRestores_TheWristPresentation_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        ship.SetWristOpen(true);
        yield return WaitUntilReal(() => ship.Arms.CurrentStateName == "WristHold", 3f);
        yield return new WaitForSecondsRealtime(0.3f);

        string stateBefore = ship.Arms.CurrentStateName;
        float normalBefore = ship.Arms.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        Assert.AreEqual("WristHold", stateBefore, "the wrist is presented before pausing");

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "the pause menu is open");
        yield return new WaitForSecondsRealtime(0.4f);

        Assert.AreEqual(stateBefore, ship.Arms.CurrentStateName,
            "pause freezes the wrist presentation state");
        float normalDuring = ship.Arms.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        Assert.Less(Mathf.Abs(normalDuring - normalBefore), 0.05f,
            "pause freezes the presentation clock");
        Assert.IsTrue(ship.Arms.IsFrozen, "the presentation reports itself frozen");
        Assert.IsTrue(ship.IsWristOpen && ship.IsWristTerminalRevealed,
            "the terminal stays presented while paused");

        yield return PressButton(GamepadButton.Start);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "the pause closes again");
        yield return WaitUntilReal(() => !ship.Arms.IsFrozen, 1f);
        Assert.AreEqual(stateBefore, ship.Arms.CurrentStateName,
            "continue resumes the same presentation state");

        ship.SetWristOpen(false);
        yield return WaitUntilReal(() => ship.Arms.CurrentWristPhase
            == FirstPersonArmsController.WristPhase.Rest, 3f);
    }

    // ====================================================================
    // Phase 08 compatibility
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleFocus_HidesTheArms_AndRestoresThem_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        Assert.IsTrue(ship.Arms.IsVisible, "arms visible in free first person");

        ShipConsoleInteractable console = FindConsole(ship, 1);
        Assert.IsNotNull(console, "console 1 exists");
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);
        Assert.IsTrue(ship.Interactor.TryInteract(), "the console focus starts");

        yield return WaitUntilReal(() => ship.IsConsoleFocusActive, 4f);
        Assert.IsTrue(ship.IsConsoleFocusActive, "console focus is active");
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.IsFalse(ship.Arms.IsVisible,
            "the arms are hidden so they can never cover the console screen");

        ship.Focus.RequestExit();
        yield return WaitUntilReal(() => !ship.IsConsoleFocusActive, 4f);
        yield return new WaitUntil(() => ship.Arms.IsVisible);
        Assert.IsTrue(ship.Arms.IsVisible, "the arms come back after leaving the view");
    }

    [UnityTest]
    public IEnumerator DevicePanel_HidesTheArms_AndRestoresThem_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Assert.IsTrue(ship.Overlay.OpenLoadout(ShipOverlayUi.LoadoutReturnTarget.ShipExplore),
            "the shipboard device panel opens");
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.IsTrue(ship.IsOverlayOpen, "the panel is open");
        Assert.IsFalse(ship.Arms.IsVisible, "the arms never cover the device panel");

        ship.Overlay.CloseToExplore();
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.IsFalse(ship.IsOverlayOpen, "the panel is closed");
        yield return new WaitUntil(() => ship.Arms.IsVisible);
        Assert.IsTrue(ship.Arms.IsVisible, "the arms are restored after Back");
    }

    [UnityTest]
    public IEnumerator ReturnToMainMenu_CleansTheArms_AndContinueCreatesOne_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        Assert.AreEqual(1, CountArms(), "exactly one arms instance in the interior");

        ship.SetWristOpen(true);
        yield return WaitUntilReal(() => ship.IsWristTerminalRevealed, 3f);

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open");

        ship.PauseMenu.SetFocus(1);
        Assert.IsTrue(ship.PauseMenu.ConfirmFocused(), "返回主菜单 confirmed");
        yield return WaitUntilReal(() => SceneManager.GetActiveScene().name
            == SceneRouting.MainMenuSceneName, 15f);
        yield return new WaitForSecondsRealtime(0.4f);

        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "back at the title screen");
        Assert.AreEqual(0, CountArms(), "no arms instance survives the return to the menu");

        // 继续游戏 goes back into the same interior state.
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        Assert.IsNotNull(menu, "the title screen is ready");
        Assert.IsTrue(menu.IsContinueAvailable, "继续游戏 is offered");
        menu.RequestContinue();

        yield return WaitUntilReal(() => SceneManager.GetActiveScene().name
            == GameSession.ShipInteriorSceneName, 20f);
        ShipGameController second = null;
        yield return WaitUntilReal(() =>
        {
            second = Object.FindAnyObjectByType<ShipGameController>();
            return second != null && second.IsReady;
        }, 20f);

        Assert.IsNotNull(second, "the interior is back");
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(1, CountArms(), "continue creates exactly one arms instance");
        Assert.AreEqual(1,
            Object.FindObjectsByType<WristPresentationBridge>(FindObjectsInactive.Include).Length,
            "and exactly one wrist presentation bridge");
        Assert.IsFalse(second.IsWristOpen, "the wrist starts closed again");
        Assert.IsFalse(second.IsWristTerminalRevealed, "no terminal lingers from the previous run");
    }

    /// <summary>
    /// The arms must never join world physics or the gaze ray: the interactor
    /// still resolves the console in front of the player while both arms fill
    /// the lower frame.
    /// </summary>
    [UnityTest]
    public IEnumerator Arms_NeverBlockTheWorldInteractionRay_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Assert.IsTrue(ship.Arms.IsVisible, "the arms are in front of the camera");
        Assert.AreEqual(0, ship.Arms.GetComponentsInChildren<Collider>(true).Length,
            "no collider exists on the arms");

        ShipConsoleInteractable console = FindConsole(ship, 1);
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);

        Assert.AreSame(console, ship.Interactor.CurrentTarget,
            "the gaze ray still reaches the console through the arms");
        Assert.IsTrue(ship.Interactor.HasValidTarget, "the target stays interactable");
    }

    // ====================================================================
    // helpers
    // ====================================================================

    private static int CountArms()
    {
        return Object.FindObjectsByType<FirstPersonArmsController>(
            FindObjectsInactive.Include).Length;
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

        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.IsNotNull(ship.Arms, "the first-person arms were created");
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

    private static IEnumerator SettlePresentation(FirstPersonArmsController arms,
        string expectedState)
    {
        yield return WaitUntilReal(() => arms.CurrentStateName == expectedState, 2f);
        yield return new WaitForSecondsRealtime(0.2f);
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

    /// <summary>Holds the left stick for a while, then releases it.</summary>
    private IEnumerator PushStick(Vector2 direction, float seconds)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = direction * 0.9f });
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private IEnumerator ReleaseStick()
    {
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
