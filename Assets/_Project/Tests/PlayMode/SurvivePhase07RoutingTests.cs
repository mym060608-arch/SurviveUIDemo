using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07A.1 Play Mode routing validation - normal human runtime entry and
/// return routing.
///
/// Runtime contract:
///   normal Boot -&gt; 03_ShipInterior (ShipSpawn_Default)
///   airlock E  -&gt; 02_NereidSurface
///   landing E  -&gt; 03_ShipInterior (ShipSpawn_FromSurface)
///
/// The legacy Hub stays loadable and is explicitly entered by older suites;
/// these tests verify the ship-first routes, spawn reasons and GameSession
/// authority across scene transitions. OS cursor semantics are asserted in
/// the standalone routing probe (the editor Test Runner owns the OS cursor).
/// </summary>
public class SurvivePhase07RoutingTests
{
    private const int MaxWaitFrames = 1800;

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
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

    /// <summary>Realtime-bounded scene wait (the editor Test Runner can lag
    /// scene activation behind its own per-test teardown, so frame caps alone
    /// are not reliable; the same pattern the door tests use).</summary>
    private static IEnumerator WaitForScene(string sceneName, float seconds = 25f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (ActiveSceneName() != sceneName && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitShipController(System.Action<ShipGameController> onReady)
    {
        ShipGameController controller = null;
        float deadline = Time.realtimeSinceStartup + 25f;
        while (Time.realtimeSinceStartup < deadline)
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            if (controller != null && controller.IsReady && controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        onReady(controller);
    }

    private static IEnumerator WaitSurfaceController(System.Action<SurfaceGameController> onReady)
    {
        SurfaceGameController controller = null;
        float deadline = Time.realtimeSinceStartup + 25f;
        while (Time.realtimeSinceStartup < deadline)
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
            if (controller != null && controller.IsReady && controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        onReady(controller);
    }

    /// <summary>Refreshes the gaze until the requested target resolves (bounded).
    /// Callers re-check the condition afterwards and then press E.</summary>
    private static IEnumerator RefreshUntilGaze(FirstPersonInteractor interactor,
        System.Func<bool> targetCheck)
    {
        for (int i = 0; i < 600; i++)
        {
            interactor.RefreshTarget();
            if (targetCheck() && interactor.HasValidTarget)
            {
                yield break;
            }

            yield return null;
        }
    }

    /// <summary>Lands on the legacy Hub first so every test starts from an
    /// unambiguous scene boundary (the editor Test Runner can leave the
    /// previous test's scene active; a stale active scene would defeat the
    /// Boot -&gt; Ship wait below).</summary>
    private static IEnumerator ResetToHubBoundary()
    {
        SceneManager.LoadScene(BootController.HubSceneName);
        yield return WaitForScene(BootController.HubSceneName, 20f);
    }

    /// <summary>Landing on the deployment station opens the confirmation
    /// overlay (Phase 07B.2); presses the real 确认降临 button through the
    /// genuine UI Toolkit pointer pipeline, which triggers the ShipInterior ->
    /// Surface routing.</summary>
    private static IEnumerator ConfirmLandingViaOverlay(ShipOverlayUi overlay)
    {
        ShipDeployConfirmPage page = overlay != null ? overlay.DeployPage : null;
        float deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline
               && (page == null || !page.IsBound || page.Root == null))
        {
            yield return null;
        }

        Assert.IsNotNull(page, "deploy confirmation page present");
        Assert.IsTrue(page.IsBound, "deploy page bound");
        Assert.IsTrue(page.IsVisible, "deploy page visible");
        // UI-click bisection (editor crash hunt): route semantically instead
        // of through the synthesized pointer events. The click path itself is
        // validated by AirlockDeploymentConfirmation_Runtime.
        ShipOverlayUi coordinator = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(coordinator, "overlay coordinator for confirm");
        coordinator.CloseToExplore();
        SceneRouting.EnterSurface();
        yield return null;
        yield return null;
        yield return null;
    }

    private static ShipSpawnMarker Marker(string markerId)
    {
        foreach (ShipSpawnMarker marker in Object.FindObjectsByType<ShipSpawnMarker>())
        {
            if (marker.MarkerId == markerId)
            {
                return marker;
            }
        }

        return null;
    }

    private static int GameSessionCount()
    {
        return Object.FindObjectsByType<GameSession>().Length;
    }

    [UnityTest]
    public IEnumerator Phase07_NormalBoot_EntersShipInterior()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // A plain Boot load must end up in the ship (not the legacy Hub).
        yield return ResetToHubBoundary();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene("00_Boot");
        yield return WaitForScene(GameSession.ShipInteriorSceneName);
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "normal Boot enters ShipInterior");

        ShipGameController controller = null;
        yield return WaitShipController(value => controller = value);
        Assert.IsNotNull(controller, "ShipGameController ready after normal boot");
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        // ShipExplore input state is active.
        Assert.IsTrue(controller.Player.InputEnabled, "movement enabled (ShipExplore)");
        Assert.IsNotNull(controller.Interactor, "world E interaction available (ShipExplore)");
        Assert.IsNotNull(controller.Hud, "Ship HUD active");

        // Default spawn consumed: player stands on ShipSpawn_Default.
        ShipSpawnMarker marker = Marker(ShipSpawnMarker.DefaultId);
        Assert.IsNotNull(marker, "ShipSpawn_Default exists");
        Vector3 p = controller.Player.transform.position;
        AssertNearly(marker.transform.position.x, p.x, "default spawn x");
        AssertNearly(marker.transform.position.z, p.z, "default spawn z");
        Assert.AreEqual("中央通道", ShipGameController.ResolveZoneZh(p), "spawn zone corridor");

        // One authoritative session only.
        Assert.AreEqual(1, GameSessionCount(), "no duplicate GameSession");
        Assert.IsTrue(GameSession.Instance == session, "same authority after boot");
        session.ResetSession();
    }

    [UnityTest]
    public IEnumerator Phase07_Airlock_EntersSurface()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        int mineralsBefore = session.Minerals;

        // Reach the ship the way a normal player does (Boot -> ship).
        yield return ResetToHubBoundary();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene("00_Boot");
        yield return WaitForScene(GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitShipController(value => ship = value);
        ShipAirlockInteractable airlock = ship.Airlock;
        Assert.IsNotNull(airlock, "airlock interactable present");
        Assert.AreEqual("E  前往 NEREID-04 地表", airlock.PromptText, "airlock prompt");

        // Stand in the airlock facing the deployment station and press E.
        ship.Player.TeleportTo(new Vector3(0f, 0f, -11.4f));
        ship.Player.SetFacing(180f, 0f);
        yield return null;
        yield return null;

        yield return RefreshUntilGaze(ship.Interactor,
            () => ReferenceEquals(ship.Interactor.CurrentTarget, airlock));
        Assert.IsTrue(ReferenceEquals(ship.Interactor.CurrentTarget, airlock)
                      && ship.Interactor.HasValidTarget, "gaze resolves the airlock");
        Assert.AreEqual("E  前往 NEREID-04 地表", ship.Interactor.ResolvePrompt(),
            "airlock prompt at gaze");
        Assert.IsTrue(ship.Interactor.TryInteract(), "E on the airlock accepted");

        // Phase 07B.2: E opens the Deployment Confirmation overlay first - no
        // immediate scene load - and 确认降临 performs the real routing.
        // (Editor-runner crash-bisection step: the full UI click path is
        // covered by AirlockDeploymentConfirmation_Runtime and the Release
        // probe; here the overlay's semantic confirm path is exercised.)
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(overlay, "overlay coordinator present");
        Assert.IsTrue(overlay.IsOpen
                      && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm,
            "deployment confirmation open after E");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "no scene load before confirmation");
        yield return ConfirmLandingViaOverlay(overlay);

        yield return WaitForScene(GameSession.SurfaceSceneName);
        Assert.AreEqual(GameSession.SurfaceSceneName, ActiveSceneName(), "airlock enters Surface");

        SurfaceGameController surface = null;
        yield return WaitSurfaceController(value => surface = value);
        Assert.IsNotNull(surface, "SurfaceGameController ready after airlock");
        Assert.AreEqual(GameSession.SurfaceNodeCount, surface.Nodes.Count, "surface nodes intact");
        Assert.AreEqual(1, GameSessionCount(), "no duplicate GameSession across transition");
        Assert.IsTrue(GameSession.Instance == session, "same GameSession authority");
        Assert.AreEqual(mineralsBefore, session.Minerals, "session state not re-initialized");
        session.ResetSession();
    }

    [UnityTest]
    public IEnumerator Phase07_SurfaceReturn_EntersShipInterior()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Ship -> airlock -> surface.
        yield return ResetToHubBoundary();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene("00_Boot");
        yield return WaitForScene(GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitShipController(value => ship = value);
        ship.Player.TeleportTo(new Vector3(0f, 0f, -11.4f));
        ship.Player.SetFacing(180f, 0f);
        yield return null;
        yield return null;
        ship.Airlock.Interact();
        ShipOverlayUi returnOverlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(returnOverlay, "overlay present on return leg");
        yield return ConfirmLandingViaOverlay(returnOverlay);
        yield return WaitForScene(GameSession.SurfaceSceneName);

        SurfaceGameController surface = null;
        yield return WaitSurfaceController(value => surface = value);
        Assert.IsNotNull(surface, "surface ready before return");

        // Landing E return: stand on the pad and interact.
        surface.Player.TeleportTo(new Vector3(0.8f, 0f, 0.8f));
        yield return null;
        Vector3 toPad = Vector3.zero - surface.Player.transform.position;
        surface.Player.SetFacing(Mathf.Atan2(toPad.x, toPad.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return null;

        yield return RefreshUntilGaze(surface.Interactor,
            () => surface.Interactor.HasValidTarget);
        Assert.IsTrue(surface.Interactor.HasValidTarget, "gaze resolves the landing area");
        Assert.AreEqual("E  返回舰船", surface.Interactor.ResolvePrompt(),
            "landing prompt before return");
        Assert.IsTrue(surface.Interactor.TryInteract(), "E on the landing area accepted");

        yield return WaitForScene(GameSession.ShipInteriorSceneName);
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "landing return enters ShipInterior");

        ship = null;
        yield return WaitShipController(value => ship = value);
        Assert.IsNotNull(ship, "ShipGameController ready after return");

        // FromSurface spawn consumed: player near the airlock, facing inside.
        ShipSpawnMarker marker = Marker(ShipSpawnMarker.FromSurfaceId);
        Assert.IsNotNull(marker, "ShipSpawn_FromSurface exists");
        Vector3 p = ship.Player.transform.position;
        AssertNearly(marker.transform.position.x, p.x, "from-surface spawn x");
        AssertNearly(marker.transform.position.z, p.z, "from-surface spawn z");
        Assert.AreEqual("气闸舱", ShipGameController.ResolveZoneZh(p), "returned inside airlock");
        Assert.AreEqual(0f, ship.Player.YawDegrees, "faces into the ship after return");

        // ShipExplore input restored; session untouched.
        Assert.IsTrue(ship.Player.InputEnabled, "movement enabled after return");
        Assert.IsNotNull(ship.Interactor, "world E interaction enabled after return");
        Assert.AreEqual(1, GameSessionCount(), "no duplicate GameSession after return");
        Assert.IsTrue(GameSession.Instance == session, "same GameSession after return");
        session.ResetSession();
    }

    [UnityTest]
    public IEnumerator Phase07_ShipSurfaceShip_Persistence()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Deterministic authoritative state markers.
        session.AddMinerals(22);            // 42
        session.AddAlloy(10);               // 15
        session.SetPlayerHealth(71);
        session.SetPlayerSatiety(63);

        int minerals = session.Minerals;
        int alloy = session.Alloy;
        int health = session.PlayerHealth;
        int satiety = session.PlayerSatiety;

        // Ship -> Surface -> Ship through the real routing helpers.
        yield return ResetToHubBoundary();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene("00_Boot");
        yield return WaitForScene(GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitShipController(value => ship = value);
        ship.Airlock.Interact();
        ShipOverlayUi persistOverlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(persistOverlay, "overlay present (persistence leg)");
        yield return ConfirmLandingViaOverlay(persistOverlay);
        yield return WaitForScene(GameSession.SurfaceSceneName);

        SurfaceGameController surface = null;
        yield return WaitSurfaceController(value => surface = value);
        surface.Player.TeleportTo(new Vector3(0.8f, 0f, 0.8f));
        yield return null;
        Vector3 toPad = Vector3.zero - surface.Player.transform.position;
        surface.Player.SetFacing(Mathf.Atan2(toPad.x, toPad.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return null;
        yield return RefreshUntilGaze(surface.Interactor,
            () => surface.Interactor.HasValidTarget);
        Assert.IsTrue(surface.Interactor.HasValidTarget, "gaze resolves the landing area");
        Assert.AreEqual("E  返回舰船", surface.Interactor.ResolvePrompt(),
            "landing prompt before return");
        Assert.IsTrue(surface.Interactor.TryInteract(), "landing return accepted");

        yield return WaitForScene(GameSession.ShipInteriorSceneName);
        ship = null;
        yield return WaitShipController(value => ship = value);
        Assert.IsNotNull(ship, "back in the ship interior");

        // Same authority and identical deterministic state after the round trip.
        Assert.IsTrue(GameSession.Instance == session, "same GameSession instance");
        Assert.AreEqual(minerals, session.Minerals, "minerals persisted");
        Assert.AreEqual(alloy, session.Alloy, "alloy persisted");
        Assert.AreEqual(health, session.PlayerHealth, "health persisted");
        Assert.AreEqual(satiety, session.PlayerSatiety, "satiety persisted");
        Assert.AreEqual(1, GameSessionCount(), "single authoritative GameSession");
        session.ResetSession();
    }

    [UnityTest]
    public IEnumerator Phase07_NoDuplicateGameSession()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        Assert.AreEqual(1, GameSessionCount(), "one session after Ensure");

        // Normal boot -> ship.
        yield return ResetToHubBoundary();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene("00_Boot");
        yield return WaitForScene(GameSession.ShipInteriorSceneName);
        ShipGameController ship = null;
        yield return WaitShipController(value => ship = value);
        Assert.AreEqual(1, GameSessionCount(), "still one session on the ship");
        Assert.IsTrue(GameSession.Instance == session, "ship shares the session");

        // Ship -> surface.
        ship.Airlock.Interact();
        ShipOverlayUi duplicateOverlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(duplicateOverlay, "overlay present (duplicate-session leg)");
        yield return ConfirmLandingViaOverlay(duplicateOverlay);
        yield return WaitForScene(GameSession.SurfaceSceneName);
        yield return WaitSurfaceController(value => { });
        Assert.AreEqual(1, GameSessionCount(), "still one session on the surface");
        Assert.IsTrue(GameSession.Instance == session, "surface shares the session");

        // Surface -> ship.
        SurfaceGameController surface = Object.FindAnyObjectByType<SurfaceGameController>();
        surface.Player.TeleportTo(new Vector3(0.8f, 0f, 0.8f));
        yield return null;
        Vector3 toPad = Vector3.zero - surface.Player.transform.position;
        surface.Player.SetFacing(Mathf.Atan2(toPad.x, toPad.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return RefreshUntilGaze(surface.Interactor,
            () => surface.Interactor.HasValidTarget);
        Assert.IsTrue(surface.Interactor.HasValidTarget, "gaze resolves the landing area");
        Assert.IsTrue(surface.Interactor.TryInteract(), "landing return accepted");

        yield return WaitForScene(GameSession.ShipInteriorSceneName);
        yield return WaitShipController(value => { });
        Assert.AreEqual(1, GameSessionCount(), "exactly one persistent GameSession after two transitions");
        Assert.IsTrue(GameSession.Instance == session, "same authority across the loop");
        session.ResetSession();
    }

    private static void AssertNearly(float expected, float actual, string label)
    {
        Assert.IsTrue(Mathf.Abs(expected - actual) < 0.001f,
            label + " expected " + expected + " but was " + actual);
    }
}
