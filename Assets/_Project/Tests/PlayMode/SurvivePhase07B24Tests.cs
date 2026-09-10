using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.4 acceptance - the Overview main visual is a real rotating 3D
/// planet preview shared by every Overview host.
///
///  - OverviewPlanetPreview_Runtime: the rig is real (sphere mesh + mesh
///    renderer, dedicated camera, valid square RenderTexture), it spins over
///    time, it is shared (never duplicated) by the Hub and the ship's Overview
///    terminal, it never intercepts pointer input, and it creates no extra
///    GameSession.
///  - OverviewPlanetPreview_FocusContinuity_Runtime: entering and leaving the
///    bridge ConsoleFocus leaves the rotation continuous (no stop, no reset, no
///    jump back to the start pose).
/// </summary>
public class SurvivePhase07B24Tests
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

    private static IEnumerator WaitSeconds(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    /// <summary>Forward rotation delta in degrees (wraps cleanly at 360).</summary>
    private static float ForwardDelta(float before, float after)
    {
        return Mathf.Repeat(after - before, 360f);
    }

    // ====================================================================
    // 1) OverviewPlanetPreview_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator OverviewPlanetPreview_Runtime()
    {
        GameSession.Ensure().ResetSession();

        // ---- legacy Hub Overview host ----
        yield return LoadScene("01_Hub");
        AppController hub = null;
        yield return WaitUntil(() =>
        {
            hub = Object.FindAnyObjectByType<AppController>();
            return hub != null && hub.IsReady;
        });
        yield return null;
        yield return null;

        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        Assert.IsNotNull(preview, "shared planet preview rig exists");
        Assert.IsTrue(preview.VerifySetup(out string report),
            "preview rig structure: " + report);
        Debug.Log("[07B24] hub preview: " + report);

        // Real 3D mesh, not a sprite / scrolling UV / flat disc.
        Assert.IsTrue(preview.HasRealPlanetMesh(out string meshReport),
            "real sphere mesh: " + meshReport);
        Assert.IsNotNull(preview.PlanetSphere, "sphere transform");
        Assert.IsNotNull(preview.PlanetSphere.GetComponent<MeshRenderer>(),
            "sphere carries a MeshRenderer");
        Assert.IsNotNull(preview.PlanetPivot, "planet pivot");
        Assert.IsNotNull(preview.KeyLight, "dedicated preview key light");
        Assert.AreEqual(LightType.Directional, preview.KeyLight.type,
            "preview key light is directional");

        // Dedicated preview camera, never stacked on a gameplay camera.
        Camera camera = preview.PreviewCamera;
        Assert.IsNotNull(camera, "preview camera");
        Assert.AreNotEqual(Camera.main, camera, "preview camera is not the main camera");
        Assert.AreEqual(preview.Texture, camera.targetTexture,
            "preview camera renders into the preview RenderTexture");
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        Assert.IsNotNull(cameraData, "preview camera carries URP data");
        Assert.IsFalse(cameraData.renderPostProcessing,
            "preview camera must not post-process (focus DOF is a global volume)");
        Assert.AreEqual(0, cameraData.volumeLayerMask.value,
            "preview camera ignores volumes");

        // Square, valid RenderTexture.
        RenderTexture rt = preview.Texture;
        Assert.IsNotNull(rt, "preview RenderTexture");
        Assert.IsTrue(rt.IsCreated(), "preview RenderTexture is created");
        Assert.AreEqual(rt.width, rt.height, "preview RenderTexture is square");
        Assert.AreEqual(OverviewPlanetPreview.TextureSize, rt.width,
            "preview RenderTexture size");

        // The Hub overview actually displays it, and only one screen does.
        VisualElement disc = hub.UiRoot.Q<VisualElement>(ElementNames.PlanetDisc);
        Assert.IsNotNull(disc, "planet disc element");
        Assert.AreEqual(PickingMode.Ignore, disc.pickingMode,
            "the planet preview never intercepts pointer input");
        yield return WaitSeconds(0.3f);
        Assert.IsTrue(preview.IsRendering,
            "preview renders while an Overview host is visible");

        // ---- rotation over time ----
        float angle0 = preview.SpinAngleDegrees;
        yield return WaitSeconds(0.8f);
        float angle1 = preview.SpinAngleDegrees;
        float delta = ForwardDelta(angle0, angle1);
        Debug.Log("[07B24] rotation " + angle0.ToString("0.00") + " -> "
                  + angle1.ToString("0.00") + " (delta " + delta.ToString("0.00") + ")");
        Assert.Greater(delta, 0.5f, "the planet really rotates over time");
        Assert.Less(delta, OverviewPlanetPreview.SpinDegreesPerSecond * 0.8f + 4f,
            "the planet rotates slowly (not a menu turntable)");
        Assert.Less(Quaternion.Angle(preview.PlanetPivot.localRotation,
                Quaternion.Euler(0f, 0f, -OverviewPlanetPreview.AxialTiltDegrees)), 0.5f,
            "the spin axis is tilted by the authored axial tilt");

        int gameSessionCount = Object.FindObjectsByType<GameSession>().Length;
        Assert.AreEqual(1, gameSessionCount, "no extra GameSession");

        // ---- the ship's Overview terminal shares the SAME rig ----
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        yield return LoadScene(GameSession.ShipInteriorSceneName);
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        yield return null;
        yield return null;

        Assert.AreEqual(1, Object.FindObjectsByType<OverviewPlanetPreview>().Length,
            "exactly one preview rig exists (no duplicates)");
        Assert.AreSame(preview, OverviewPlanetPreview.Shared,
            "the ship reuses the same shared rig instance");

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
        Assert.AreSame(preview, overview.ScreenHost.PlanetPreview,
            "console 01 uses the shared preview rig");
        yield return WaitUntil(() => overview.IsReady);
        yield return WaitSeconds(0.4f);
        Assert.IsTrue(preview.IsRendering,
            "the always-on bridge terminal keeps the preview rendering");

        VisualElement consoleDisc = overview.Document.rootVisualElement
            .Q<VisualElement>(ElementNames.PlanetDisc);
        Assert.IsNotNull(consoleDisc, "console planet disc element");
        Assert.AreEqual(PickingMode.Ignore, consoleDisc.pickingMode,
            "console planet preview never intercepts console pointer input");

        // No console pointer regression: the router stays off outside focus and
        // the pointer owner stays ShipExplore.
        Assert.AreEqual(ShipGameController.ShipPointerOwner.None,
            controller.PointerOwner, "pointer owner unchanged by the preview");
        Assert.IsFalse(controller.PointerRouter != null
                       && controller.PointerRouter.enabled,
            "console pointer router stays off in ShipExplore");

        float shipAngle = preview.SpinAngleDegrees;
        yield return WaitSeconds(0.8f);
        Assert.Greater(ForwardDelta(shipAngle, preview.SpinAngleDegrees), 0.5f,
            "the bridge planet keeps turning in the ship interior");

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 2) OverviewPlanetPreview_FocusContinuity_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator OverviewPlanetPreview_FocusContinuity_Runtime()
    {
        GameSession.Ensure().ResetSession();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        yield return LoadScene(GameSession.ShipInteriorSceneName);
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        Assert.IsNotNull(preview, "shared preview rig exists in the ship");
        yield return WaitSeconds(0.4f);

        // Approach the bridge Overview terminal (console 01).
        WorldConsoleSurface overview = controller.Consoles[0]
            .GetComponent<WorldConsoleSurface>();
        yield return WaitUntil(() => overview.IsReady);
        SurfacePlayerController player = controller.Player;
        player.TeleportTo(ConsolePositions[0] + ScreenDirs[0] * 1.7f);
        player.SetFacing(Mathf.Atan2(-ScreenDirs[0].x, -ScreenDirs[0].z)
                         * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return null;
        Physics.SyncTransforms();

        float beforeFocus = preview.SpinAngleDegrees;
        yield return WaitSeconds(0.5f);
        float approachDelta = ForwardDelta(beforeFocus, preview.SpinAngleDegrees);
        Assert.Greater(approachDelta, 0.5f, "planet turns before focus");

        // Enter console focus (this enables the focus proxy + DOF volume).
        bool entered = false;
        ShipConsoleInteractable console = controller.Consoles[0];
        for (int f = 0; f < 300 && !entered; f++)
        {
            controller.Interactor.RefreshTarget();
            if (ReferenceEquals(controller.Interactor.CurrentTarget, console)
                && controller.Interactor.HasValidTarget)
            {
                entered = controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Assert.IsTrue(entered, "E entered the bridge console focus");
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus);
        Assert.AreEqual(ShipGameController.ShipPointerOwner.ConsoleScreen,
            controller.PointerOwner, "console owns the pointer while focused");

        float atFocusEntry = preview.SpinAngleDegrees;
        yield return WaitSeconds(1.0f);
        float duringFocus = preview.SpinAngleDegrees;
        float focusDelta = ForwardDelta(atFocusEntry, duringFocus);
        Debug.Log("[07B24] focus continuity " + atFocusEntry.ToString("0.00")
                  + " -> " + duringFocus.ToString("0.00") + " (delta "
                  + focusDelta.ToString("0.00") + ")");
        Assert.Greater(focusDelta, 0.5f,
            "the planet keeps rotating during ConsoleFocus");
        Assert.Less(focusDelta, 180f,
            "the planet never jumps back to its start pose on focus");

        // Leave focus: rotation must stay continuous.
        focus.RequestExit();
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore);
        float atExit = preview.SpinAngleDegrees;
        yield return WaitSeconds(1.0f);
        float afterExitDelta = ForwardDelta(atExit, preview.SpinAngleDegrees);
        Assert.Greater(afterExitDelta, 0.5f,
            "the planet keeps rotating after ConsoleFocus");
        Assert.Less(afterExitDelta, 180f,
            "no reset or jump after leaving console focus");
        Assert.IsTrue(overview.ScreenRenderer.enabled,
            "the bridge screen still renders after focus");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.None,
            controller.PointerOwner, "ShipExplore owns the pointer again");

        GameSession.Ensure().ResetSession();
    }
}
