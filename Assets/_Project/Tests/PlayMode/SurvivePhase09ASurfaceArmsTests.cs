using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// The block-arm presentation is environment INDEPENDENT: the ship interior and
/// the planet surface run the same controller, the same rest / walk behaviour and
/// the same wrist raise / hold / lower. What differs is only the context handed in
/// through IArmsPresentationContext.
///
/// This suite pins the surface side of that contract (the ship side is covered by
/// SurvivePhase09A3BlockArmsTests) plus the "exactly one presentation" rule
/// across scene transitions, which is what a duplicated DontDestroyOnLoad copy
/// would break.
/// </summary>
public class SurvivePhase09ASurfaceArmsTests
{
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

    [UnityTest]
    public IEnumerator Surface_HasOneBlockArmsPresentation_AndDrivesIt_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        Assert.AreEqual(1, CountArms(), "exactly one block arms instance on the surface");
        Assert.IsNotNull(surface.Arms, "the surface controller exposes the arms");
        Assert.AreEqual(surface.Player.transform, surface.Arms.transform.parent,
            "the surface arms hang from the surface player rig");
        Assert.IsNotNull(surface.WristPresentation,
            "the surface drives the same wrist presentation bridge");
        Assert.AreEqual(0, Object.FindObjectsByType<FirstPersonArmsController>(
                FindObjectsInactive.Include).Length,
            "no legacy FBX arms are created on the surface either");

        surface = Object.FindAnyObjectByType<SurfaceGameController>();
        Assert.IsNotNull(surface, "the surface controller is still live");

        // Forward: level gaze keeps them out of the frustum.
        surface.Player.SetFacing(180f, 0f);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(0, RenderersInsideFrustum(
                surface.Arms.GetComponentsInChildren<Renderer>(true)),
            "a level gaze keeps the surface arms out of the frustum");

        // Look down: both arms come in.
        surface.Player.SetFacing(180f, 45f);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.Greater(RenderersInsideFrustum(
                surface.Arms.GetComponentsInChildren<Renderer>(true)), 3,
            "looking down brings the surface arms into the frustum");
    }

    [UnityTest]
    public IEnumerator Surface_WristRaiseHoldLower_UsesTheSamePresentation_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        Assert.IsFalse(surface.IsWristOpen, "the surface wrist starts closed");
        Assert.AreEqual(0f, surface.Arms.FocusBlend, 0.001f, "the arm starts crisp");

        // The surface keeps its own legacy wrist bindings (M / I); the semantic
        // toggle is the same authority entry point the ship uses.
        surface.ToggleWrist();
        yield return null;
        Assert.IsTrue(surface.IsWristOpen, "the wrist authority opens on the surface");
        Debug.Log("[SurfaceArms] after toggle: phase=" + surface.Arms.CurrentWristPhase
                  + " frozen=" + surface.Arms.IsFrozen
                  + " blend=" + surface.Arms.WristPresentationBlend.ToString("0.000")
                  + " bridgeAwaiting=" + surface.WristPresentation.AwaitingUiReveal
                  + " ready=" + surface.IsReady);
        Assert.AreEqual(FirstPersonBlockArmsController.WristPhase.Raise,
            surface.Arms.CurrentWristPhase, "the raise phase starts on the surface");

        yield return WaitUntil(() => surface.Arms.WristPresentationBlend > 0.3f, 2f);
        Assert.Greater(surface.Arms.WristPresentationBlend, 0.05f,
            "the surface arm raises over time");
        Assert.AreEqual("WristRaise", surface.Arms.CurrentStateName,
            "the raise phase is reported on the surface too");

        yield return WaitUntil(() => surface.Wrist.Display != null
                                     && surface.Wrist.Display.Visible, 4f);
        Assert.IsTrue(surface.Wrist.Display.Visible,
            "the planet's own terminal is shown while the wrist is open");
        yield return WaitUntil(() => surface.WristPresentation.LastRevealProgress > 0f, 3f);
        Assert.Greater(surface.WristPresentation.LastRevealProgress, 0.70f,
            "the arm raise was driven to the reveal point ("
            + surface.WristPresentation.LastRevealProgress.ToString("0.00") + ")");

        yield return WaitUntil(() => surface.Arms.FocusBlend >= 0.999f, 3f);
        Assert.IsTrue(surface.Arms.IsSoftened, "the surface arm frosts while the hologram is up");
        Assert.Greater(surface.Arms.PhysicalArmAlpha, 0.4f,
            "the frosted surface arm stays visible");

        surface.ToggleWrist();
        yield return null;
        Assert.IsFalse(surface.IsWristOpen, "the wrist authority closes on the surface");
        yield return WaitUntil(() => surface.Arms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Rest, 4f);
        Assert.AreEqual("Rest", surface.Arms.CurrentStateName,
            "the surface arm returns to rest");
        Assert.IsFalse(surface.Arms.IsSoftened, "the surface arm is crisp again");
        Assert.IsTrue(surface.Player.InputEnabled, "exploration input is restored");
    }

    [UnityTest]
    public IEnumerator SceneTransitions_NeverDuplicateTheArms_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        Assert.AreEqual(1, CountArms(), "one presentation in the ship interior");

        SceneRouting.EnterSurface();
        SurfaceGameController surface = null;
        yield return WaitUntil(() =>
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            return surface != null && surface.IsReady && surface.Arms != null;
        }, 20f);
        Assert.IsNotNull(surface, "the surface becomes ready");
        Assert.AreEqual(1, CountArms(), "still exactly one presentation after the entry routing");
        Assert.AreEqual(surface.Player.transform, surface.Arms.transform.parent,
            "the live presentation belongs to the surface rig, not a stale ship copy");

        // Back to the ship through the surface's own return path (the R semantic).
        surface.ReturnToShip();
        yield return null;
        ShipGameController shipAgain = null;
        yield return WaitUntil(() =>
        {
            shipAgain = Object.FindAnyObjectByType<ShipGameController>();
            return shipAgain != null && shipAgain.IsReady && shipAgain.Arms != null;
        }, 20f);
        Assert.IsNotNull(shipAgain, "the ship interior becomes ready again");
        Assert.AreEqual(1, CountArms(), "still exactly one presentation after going back");
        Assert.AreEqual(shipAgain.Player.transform, shipAgain.Arms.transform.parent,
            "and it belongs to the ship rig again");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static int CountArms()
    {
        return Object.FindObjectsByType<FirstPersonBlockArmsController>(
            FindObjectsInactive.Include).Length;
    }

    private static int RenderersInsideFrustum(Renderer[] renderers)
    {
        Camera eye = Camera.main;
        if (eye == null || renderers == null)
        {
            return 0;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(eye);
        int count = 0;
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.enabled
                && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
            {
                count++;
            }
        }

        return count;
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

    private static IEnumerator EnterSurface(System.Action<SurfaceGameController> onSurface)
    {
        // Load the planet scene explicitly: the tests run in one process, and a
        // previous test's surface state must never leak into this one.
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return null;

        SurfaceGameController surface = null;
        yield return WaitUntil(() =>
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            return surface != null && surface.IsReady && surface.Player != null
                   && surface.Arms != null;
        }, 25f);
        Assert.IsNotNull(surface, "the planet surface is ready with its arms");
        yield return WaitUntil(() => surface.Player.InputEnabled, 5f);
        onSurface(surface);
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
