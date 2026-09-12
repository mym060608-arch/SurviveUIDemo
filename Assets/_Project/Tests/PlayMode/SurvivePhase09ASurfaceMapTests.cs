using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase 09A_4 - the planet surface's map/controller contract.
///
/// Root cause this suite pins: SurfaceGameController polled the raw M / I keys and
/// never consumed a logical action, so NO gamepad face control reached the surface
/// at all - Xbox X (buttonWest) could not open the map, and neither could Xbox Y.
/// The fix routes the surface through UiInput's semantic actions (Wrist =
/// buttonNorth, Map = buttonWest), so these rows drive the REAL queued devices
/// through the authoritative route instead of calling the controller's toggle.
///
/// The leak rules are pinned too: the map never opens behind the pause layer, a
/// modal layer or the ship's own frame, and X never becomes a second interface on
/// top of the already-open map.
/// </summary>
public class SurvivePhase09ASurfaceMapTests
{
    private Gamepad _pad;
    private Keyboard _keyboard;

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
        _keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.EnableDevice(_keyboard);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        if (_keyboard != null && _keyboard.added)
        {
            InputSystem.RemoveDevice(_keyboard);
        }

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _keyboard = null;
        _pad = null;
        UiInput.ResetLayersForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // The reported regression: Xbox X opens the surface map
    // ====================================================================

    [UnityTest]
    public IEnumerator SurfaceFree_ButtonWest_OpensTheMap_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        // The binding is data, so it can be pinned without pressing anything.
        Assert.Contains("Map=Gamepad.buttonWest,Keyboard.mKey|iKey", UiInput.BindingTable,
            "the surface map is the platform-neutral Map action (Xbox X / Square)");
        Assert.Contains("Wrist=Gamepad.buttonNorth,Keyboard.mKey|iKey", UiInput.BindingTable,
            "Y / buttonNorth still owns the wrist: X and Y are never swapped");
        foreach (string row in UiInput.BindingTable)
        {
            Assert.IsFalse(row.Contains("XButton") || row.Contains("YButton")
                           || row.Contains("PlayStation") || row.Contains("StartButton"),
                "no logical row names a physical control (" + row + ")");
        }

        Assert.IsFalse(surface.IsWristOpen, "the surface map starts closed");
        Assert.IsNotNull(surface.Wrist, "the surface map UI exists");
        Assert.IsFalse(MapVisible(surface), "no map presentation before the press");
        Assert.AreEqual(1, CountArms(), "the surface arms are untouched by map input");

        yield return PressButton(GamepadButton.West);

        Assert.IsTrue(surface.IsWristOpen,
            "Xbox X (buttonWest) opens the surface map");
        yield return WaitUntil(() => MapVisible(surface), 3f);
        Assert.IsTrue(MapVisible(surface), "the surface map is presented");
        Assert.IsFalse(surface.Player.InputEnabled,
            "the open map owns exploration input");
        Assert.AreEqual(1, CountArms(),
            "opening the map never duplicates the arms presentation");
    }

    [UnityTest]
    public IEnumerator Surface_ButtonNorth_StillOpensTheWrist_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.North);

        Assert.IsTrue(surface.IsWristOpen,
            "Xbox Y (buttonNorth) still opens the surface wrist and its map");
        yield return WaitUntil(() => MapVisible(surface), 3f);
        Assert.IsTrue(MapVisible(surface), "the map is presented through the wrist");
    }

    // ====================================================================
    // Close contract: Back (B) and Escape, never the pause
    // ====================================================================

    [UnityTest]
    public IEnumerator SurfaceMap_ButtonEast_ClosesTheMap_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.West);
        Assert.IsTrue(surface.IsWristOpen, "X opened the map");

        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(surface.IsWristOpen,
            "Xbox B (buttonEast) closes the surface map");
        yield return WaitUntil(() => !MapVisible(surface), 3f);
        Assert.IsFalse(MapVisible(surface), "the map presentation is released");
        Assert.IsTrue(surface.Player.InputEnabled,
            "exploration control is restored after Back");
    }

    [UnityTest]
    public IEnumerator SurfaceMap_Escape_ClosesTheMap_WithoutPause_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.West);
        Assert.IsTrue(surface.IsWristOpen, "X opened the map");

        // The harness cannot deliver a synthetic keyboard EDGE (measured: a queued
        // KeyboardState sets the control's value but never wasPressedThisFrame), so
        // this row drives the physical Escape key through the same automation seam
        // the accepted Phase 09A Esc rows use. Everything below the seam - the
        // Back priority board, the wrist layer's Back handler and the pause
        // fallback - runs for real, and the physical binding itself is pinned by
        // UiInput.BindingTable.
        Assert.Contains("Back=Gamepad.buttonEast,Keyboard.escapeKey", UiInput.BindingTable,
            "Escape is the keyboard Back binding");
        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return null;

        Assert.IsFalse(surface.IsWristOpen, "Escape closes the surface map");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "Escape was the wrist layer's Back, so no pause layer was opened");
        Assert.IsTrue(surface.Player.InputEnabled,
            "exploration control is restored");
    }

    // ====================================================================
    // Leak rules: nothing opens behind an owning layer
    // ====================================================================

    [UnityTest]
    public IEnumerator SurfaceMap_ButtonWest_TogglesItClosed_WithoutASecondMap_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(surface.IsWristOpen, "the wrist/map is open");

        yield return PressButton(GamepadButton.West);

        // One map authority: X toggles the open map closed instead of stacking a
        // second interface behind the first.
        Assert.IsFalse(surface.IsWristOpen,
            "X toggles the open map closed rather than opening a second one");
        Assert.AreEqual(1, CountWristUis(), "exactly one surface map UI exists");
        Assert.AreEqual(1, CountArms(), "the arms presentation stays unique");
        Assert.IsFalse(MapVisible(surface),
            "the map presentation is never left visible while the authority says closed");
    }

    [UnityTest]
    public IEnumerator SurfacePauseLayer_ButtonWest_DoesNotOpenTheMap_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        // The pause owns the frame (the ship's pause publishes this layer).
        UiInput.SetLayerActive(UiInput.UiLayer.PauseMenu, true);
        try
        {
            yield return PressButton(GamepadButton.West);
        }
        finally
        {
            UiInput.SetLayerActive(UiInput.UiLayer.PauseMenu, false);
        }

        Assert.IsFalse(surface.IsWristOpen,
            "X never opens the map behind the pause layer");
        Assert.IsFalse(MapVisible(surface), "and no map presentation leaked out");
    }

    [UnityTest]
    public IEnumerator SurfaceModalLayer_ButtonWest_DoesNotOpenTheMap_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        UiInput.SetLayerActive(UiInput.UiLayer.Modal, true);
        try
        {
            yield return PressButton(GamepadButton.West);
        }
        finally
        {
            UiInput.SetLayerActive(UiInput.UiLayer.Modal, false);
        }

        Assert.IsFalse(surface.IsWristOpen,
            "X never opens the map behind a modal layer");
        Assert.IsFalse(MapVisible(surface), "and no map presentation leaked out");
    }

    [UnityTest]
    public IEnumerator ShipInterior_ButtonWest_NeverOpensTheSurfaceMap_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        Assert.IsFalse(ship.IsWristOpen, "the ship wrist starts closed");

        yield return PressButton(GamepadButton.West);

        Assert.IsFalse(ship.IsWristOpen, "X never opens the ship wrist");
        Assert.IsFalse(ship.IsWristTerminalRevealed,
            "X never reveals the ship hologram either");
        Assert.AreEqual(0,
            Object.FindObjectsByType<SurfaceGameController>(FindObjectsInactive.Include).Length,
            "X never spawns the surface slice in the ship interior");

        // ... and the ship's own wrist control is untouched: Y still works.
        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(ship.IsWristOpen, "Y still opens the ship wrist");
    }

    [UnityTest]
    public IEnumerator Surface_ConfirmBackAndPause_NeverOpenTheMap_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.South);
        Assert.IsFalse(surface.IsWristOpen, "A (confirm/interact) never opens the map");

        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(surface.IsWristOpen, "B (Back) never opens the map");

        yield return PressButton(GamepadButton.Start);
        Assert.IsFalse(surface.IsWristOpen, "Menu/Start never opens the map");

        Assert.AreEqual(1, CountArms(), "the arms presentation stays unique");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static bool MapVisible(SurfaceGameController surface)
    {
        return surface != null && surface.Wrist != null
               && surface.Wrist.Display != null && surface.Wrist.Display.Visible;
    }

    private static int CountWristUis()
    {
        return Object.FindObjectsByType<SurfaceWristController>(
            FindObjectsInactive.Include).Length;
    }

    private static int CountArms()
    {
        return Object.FindObjectsByType<FirstPersonBlockArmsController>(
            FindObjectsInactive.Include).Length;
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
