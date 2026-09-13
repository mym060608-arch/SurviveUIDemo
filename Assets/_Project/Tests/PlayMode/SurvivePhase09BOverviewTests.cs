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
/// Scoped verification for the Overview / 星球总览 page only (Phase 09B PASS 7).
/// Nothing outside this page is asserted or captured here.
///
/// It covers the three things this pass is allowed to touch:
///
///   * STRUCTURE / COPY - one Chinese page title, no decorative system English,
///     the planet name identical in every node of the page (hero + objective),
///     the live left column, the exploration read-out and a key line that lists
///     ONLY keys this page really has;
///   * BEHAVIOUR - the user's sketch on the real input route: the RIGHT stick
///     rotates the shared planet preview (horizontal spin, clamped latitude,
///     dead zone, smooth stop, no flip), it never touches the camera, the other
///     screens or the session data, and the ship's physical terminal only obeys
///     the stick while the player is focused on it;
///   * EVIDENCE - 1920x1080 runtime frames plus planet / mission close-ups.
/// </summary>
public class SurvivePhase09BOverviewTests
{
    private const string Output =
        @"D:\dsh_gpt_unity_blender\export\evidence\phase09b\pass7_overview";

    private const int ShotWidth = 1920;
    private const int ShotHeight = 1080;

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

    private AppController _app;
    private Gamepad _pad;

    [TearDown]
    public void RemoveVirtualPad()
    {
        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _pad = null;
        UiInput.ResetLayersForAutomation();
        InputDeviceTracker.ResetToDefaults();

        // The shared rig survives a scene reload by design: never let one test's
        // rotation leak into the next one's starting state.
        OverviewPlanetPreview.Shared?.ResetPlayerControl();
    }

    // ====================================================================
    // 1. Structure, page copy and the controller key line
    // ====================================================================

    [UnityTest]
    public IEnumerator Overview_Structure_PlanetNameAndKeyLine_AndCapture()
    {
        yield return LoadHubOverview();
        AppController app = _app;
        VisualElement page = ScreenRoot(app);
        OverviewScreen view = app.OverviewView;

        // ---- the page's own identity: Chinese title, no system strip ----
        Assert.AreEqual("星球总览", LabelText(page, ElementNames.OverviewTitle),
            "the page keeps one Chinese title");
        Assert.AreEqual(0, page.Query<Label>(className: "topbar-brand").ToList().Count,
            "no brand wordmark on the page");
        Assert.AreEqual(0, page.Query<Label>(className: "topbar-cell").ToList().Count,
            "no SHIP / CYCLE / SYSTEM ONLINE cells on the page");
        foreach (Label label in page.Query<Label>().ToList())
        {
            StringAssert.DoesNotContain("SURVIVE // DRIFT", label.text, "decorative brand removed");
            StringAssert.DoesNotContain("ARK-07", label.text, "ship code removed from this page");
            StringAssert.DoesNotContain("CYCLE", label.text, "cycle counter removed");
            StringAssert.DoesNotContain("SYSTEM ONLINE", label.text, "status english removed");
            StringAssert.DoesNotContain("ORBITAL SURVEY", label.text, "english annotation removed");
            StringAssert.DoesNotContain("TARGET LOCK", label.text, "english annotation removed");
        }

        // ---- one planet name for the whole page ----
        Assert.AreEqual(PlanetIdentity.DisplayName, LabelText(page, ElementNames.DestinationValue),
            "hero shows the shared planet name");
        Assert.AreEqual(PlanetIdentity.SurveyObjective,
            LabelText(page, ElementNames.ObjectiveSurveyText),
            "the mission objective names the same planet");
        foreach (Label label in page.Query<Label>().ToList())
        {
            Assert.IsFalse(PlanetIdentity.NamesAnotherPlanet(label.text),
                "no node of this page names a second planet: " + label.text);
        }

        // ---- left column: the live ship status / inventory ----
        GameSession session = GameSession.Instance;
        Assert.AreEqual("舰船状态", HeaderText(page, ElementNames.ShipStatusPanel), "status header");
        Assert.AreEqual(session.Energy.ToString(), LabelText(page, ElementNames.EnergyValue), "energy");
        Assert.AreEqual(session.Food.ToString(), LabelText(page, ElementNames.FoodValue), "food");
        Assert.AreEqual(session.Minerals.ToString(), LabelText(page, ElementNames.MineralsValue),
            "minerals");
        Assert.AreEqual(session.Fuel.ToString(), LabelText(page, ElementNames.FuelValue), "fuel");
        Assert.AreEqual(session.Alloy.ToString(), LabelText(page, ElementNames.AlloyValue), "alloy");
        AssertBarRatio(page, ElementNames.EnergyBarFill, session.Energy, "energy bar");

        // ---- exploration read-out: value + simple bar, right of the planet ----
        Assert.AreEqual(OverviewScreen.ExplorationPercent + "%",
            LabelText(page, ElementNames.ExplorationValue), "exploration value");
        AssertBarRatio(page, ElementNames.ExplorationBarFill, OverviewScreen.ExplorationPercent,
            "exploration bar");
        Assert.IsTrue(IsRightOf(page.Q<VisualElement>(ElementNames.ExplorationValue),
                page.Q<VisualElement>(ElementNames.PlanetDisc)),
            "exploration sits right of the planet");
        Assert.AreEqual("探索度",
            page.Q<VisualElement>(ElementNames.ExplorationValue)
                .parent.Q<Label>(className: "rail-key").text,
            "exploration label");

        // ---- right column: current objective + mission progress ----
        Assert.AreEqual("当前目标", HeaderText(page, ElementNames.ObjectivePanel),
            "objective header reads 当前目标");
        Assert.AreEqual(3, page.Query<VisualElement>(className: "objective-row").ToList().Count,
            "the authored objective list is intact");
        StringAssert.Contains("进行中",
            page.Query<Label>(className: "objective-state").ToList()[0].text,
            "the first objective is in progress");
        Assert.IsTrue(page.Q<VisualElement>(ElementNames.MissionStatusBlock) != null,
            "mission status block");
        Assert.AreEqual(PlanetIdentity.DisplayName,
            LabelText(page, ElementNames.DestinationValue), "mission and hero agree");

        // ---- the key line lists exactly what this page can do ----
        Assert.IsNotNull(view.PromptBar, "shared prompt bar mounted");
        yield return WaitFrames(4);
        Assert.IsTrue(view.IsPlanetFallbackActive || view.IsPlanetPreviewActive,
            "the planet visual is up");
        Assert.IsTrue(view.IsPlanetInteractive, "the page is interactive while it is on screen");
        StringAssert.Contains(OverviewScreen.RotatePromptLabel, view.PromptBar.PromptText,
            "the key line offers the planet rotation");
        StringAssert.Contains("返回", view.PromptBar.PromptText,
            "the Hub page prints the Back row too (B is the Hub's page step)");
        Assert.IsTrue(view.OwnsBack, "the Hub page owns the Back press");
        foreach (string dead in new[] { "[A]", "[X]", "[Y]", "[LB]", "[RB]" })
        {
            StringAssert.DoesNotContain(dead, view.PromptBar.PromptText,
                "the key line never advertises a key this page does not have");
        }

        // ---- one planet name across every player-facing surface ----
        AssertPlayerFacingCopyUsesCurrentPlanet();

        Directory.CreateDirectory(Output);
        yield return CapturePage(app, Path.Combine(Output, "overview_default.png"));
    }

    // ====================================================================
    // 1b. Hub Back: B is the Hub's page step, and it really leaves the page
    // ====================================================================

    [UnityTest]
    public IEnumerator Overview_HubBackRow_ReturnsToPreviousScreen()
    {
        yield return LoadHubOverview();
        AppController app = _app;

        // On the very first screen there is no earlier tab: Back stays on
        // Overview (the four tabs are the Hub's navigation; Back never invents a
        // fifth page).
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "the Hub opens on Overview");
        yield return Press(_pad, GamepadButton.East);
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen,
            "Back on the root page never leaves the Hub");
        Assert.AreEqual(1, app.VisibleScreenCount, "still exactly one visible screen");

        // Arrive at Overview from 据点: Back must return there.
        app.ShowScreen(AppScreen.Outpost);
        yield return WaitFrames(3);
        app.ShowScreen(AppScreen.Overview);
        yield return WaitFrames(4);
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "outpost is the previous screen");

        yield return Press(_pad, GamepadButton.East);
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen,
            "B on the Hub Overview returns to the screen the player came from");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one screen stays visible");
    }

    // ====================================================================
    // 2. RS rotates the planet, and a released stick stops it
    // ====================================================================

    [UnityTest]
    public IEnumerator Overview_RightStick_RotatesPlanet_ThenStops_AndCapture()
    {
        yield return LoadHubOverview();
        AppController app = _app;
        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        Assert.IsNotNull(preview, "shared planet rig in the Hub");
        yield return WaitFrames(6);

        // The authored idle spin owns the planet before the player touches it.
        Assert.IsFalse(preview.IsPlayerControlled, "the idle spin starts as the owner");
        yield return CapturePage(app, Path.Combine(Output, "overview_planet_front.png"));
        yield return CaptureRegion(app, Path.Combine(Output, "overview_planet_front_zoom.png"),
            WorldBoundOf(app.UiRoot, ElementNames.PlanetStage));

        float before = preview.SpinAngleDegrees;
        float pitchBefore = preview.PitchDegrees;
        yield return PushStick(_pad, new Vector2(1f, 0f), 0.7f);
        Assert.IsTrue(preview.IsPlayerControlled, "the right stick took the planet over");
        float turned = ForwardDelta(before, preview.SpinAngleDegrees);
        Assert.Greater(turned, 15f,
            "the stick really spins the planet (turned " + turned.ToString("0.0")
            + " deg in 0.7s, idle would be ~3.5)");
        Assert.Less(Mathf.Abs(Mathf.DeltaAngle(pitchBefore, preview.PitchDegrees)), 0.5f,
            "a horizontal push never changes the latitude");

        yield return CapturePage(app, Path.Combine(Output, "overview_planet_rotated.png"));
        yield return CaptureRegion(app, Path.Combine(Output, "overview_planet_rotated_zoom.png"),
            WorldBoundOf(app.UiRoot, ElementNames.PlanetStage));

        // A released stick decays to a real stop: no drift, no snap. The decay
        // is measured in real seconds (a batchmode frame is far shorter than a
        // played one).
        yield return WaitSeconds(0.9f);
        float settled = preview.SpinAngleDegrees;
        Assert.IsFalse(preview.IsCoasting, "the planet coasts to a stop after the release");
        yield return WaitSeconds(0.4f);
        Assert.Less(ForwardDelta(settled, preview.SpinAngleDegrees), 0.5f,
            "a released stick stops the planet (it does not keep turning)");

        // A left push turns the other way.
        float leftStart = preview.SpinAngleDegrees;
        yield return PushStick(_pad, new Vector2(-1f, 0f), 0.7f);
        float turnedLeft = ForwardDelta(preview.SpinAngleDegrees, leftStart);
        Assert.Greater(turnedLeft, 15f, "the opposite direction really spins the other way");
    }

    // ====================================================================
    // 3. Latitude is a limited look band, never a pole flip
    // ====================================================================

    [UnityTest]
    public IEnumerator Overview_LatitudeBand_IsClamped_AndNeverFlips()
    {
        yield return LoadHubOverview();
        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        Assert.IsNotNull(preview, "shared planet rig in the Hub");
        yield return WaitFrames(4);

        // Hold "up" far longer than the band needs: the clamp must hold.
        yield return PushStick(_pad, new Vector2(0f, 1f), 1.6f);
        Assert.AreEqual(OverviewPlanetPreview.MaxPitchDegrees, preview.PitchDegrees, 0.6f,
            "the latitude stops at the authored band limit");
        Assert.LessOrEqual(Mathf.Abs(preview.PitchDegrees), OverviewPlanetPreview.MaxPitchDegrees,
            "the latitude never exceeds the band");

        yield return PushStick(_pad, new Vector2(0f, -1f), 2.6f);
        Assert.AreEqual(-OverviewPlanetPreview.MaxPitchDegrees, preview.PitchDegrees, 0.6f,
            "and it stops at the other end of the band");

        // The axial tilt pivot is untouched: the planet can never flip over.
        Assert.Less(Quaternion.Angle(preview.PlanetPivot.localRotation,
                Quaternion.Euler(0f, 0f, -OverviewPlanetPreview.AxialTiltDegrees)), 0.5f,
            "the spin axis keeps its authored tilt");
        // The sphere's own latitude stays inside the authored band (a negative
        // pitch reads back as 334 in euler angles, hence the signed delta).
        float spherePitch = Mathf.DeltaAngle(0f,
            preview.PlanetSphere.localRotation.eulerAngles.x);
        Assert.LessOrEqual(Mathf.Abs(spherePitch), OverviewPlanetPreview.MaxPitchDegrees + 0.5f,
            "the sphere is never turned past the authored latitude band");
    }

    // ====================================================================
    // 4. The stick rotates the planet only - nothing else moves
    // ====================================================================

    [UnityTest]
    public IEnumerator Overview_Rotation_LeavesSessionAndOtherScreens_Untouched_AndCapture()
    {
        yield return LoadHubOverview();
        AppController app = _app;
        VisualElement page = ScreenRoot(app);
        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        GameSession session = GameSession.Instance;

        int energy = session.Energy;
        int food = session.Food;
        int minerals = session.Minerals;
        int fuel = session.Fuel;
        int alloy = session.Alloy;
        int harvester = session.HarvesterLevel;
        int refinery = session.RefineryLevel;
        string objective = LabelText(page, ElementNames.ObjectiveCopy);
        string destination = LabelText(page, ElementNames.DestinationValue);
        Vector3 cameraPosition = Camera.main != null
            ? Camera.main.transform.position
            : Vector3.zero;
        Quaternion cameraRotation = Camera.main != null
            ? Camera.main.transform.rotation
            : Quaternion.identity;

        yield return PushStick(_pad, new Vector2(0.92f, 0.35f), 0.8f);
        Assert.IsTrue(preview.IsPlayerControlled, "the stick rotated the planet");
        Assert.Greater(Mathf.Abs(preview.PitchDegrees), 1f, "and inspected a latitude");

        // Gameplay data is untouched: this pass is presentation + rotation only.
        Assert.AreEqual(energy, session.Energy, "energy unchanged");
        Assert.AreEqual(food, session.Food, "food unchanged");
        Assert.AreEqual(minerals, session.Minerals, "minerals unchanged");
        Assert.AreEqual(fuel, session.Fuel, "fuel unchanged");
        Assert.AreEqual(alloy, session.Alloy, "alloy unchanged");
        Assert.AreEqual(harvester, session.HarvesterLevel, "harvester level unchanged");
        Assert.AreEqual(refinery, session.RefineryLevel, "refinery level unchanged");

        // The page's own copy, the active screen and the camera are untouched.
        Assert.AreEqual(objective, LabelText(page, ElementNames.ObjectiveCopy),
            "objective copy unchanged by rotation");
        Assert.AreEqual(destination, LabelText(page, ElementNames.DestinationValue),
            "planet name unchanged by rotation");
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "still the Overview screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "still exactly one visible screen");
        Assert.IsTrue(HasClass(app.UiRoot, ElementNames.NavOverview, "tab-active"),
            "the Overview tab stays active");
        if (Camera.main != null)
        {
            Assert.AreEqual(cameraPosition, Camera.main.transform.position,
                "the camera never moves");
            Assert.AreEqual(cameraRotation, Camera.main.transform.rotation,
                "the camera never turns");
        }

        Directory.CreateDirectory(Output);
        yield return CapturePage(app, Path.Combine(Output, "overview_mission_progress.png"));
        yield return CaptureRegion(app, Path.Combine(Output, "overview_mission_zoom.png"),
            WorldBoundOf(app.UiRoot, ElementNames.ObjectivePanel));
    }

    // ====================================================================
    // 5. Dead zone: a resting / drifting stick keeps the authored idle spin
    // ====================================================================

    [UnityTest]
    public IEnumerator Overview_StickDeadzone_KeepsAuthoredIdleSpin()
    {
        yield return LoadHubOverview();
        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        yield return WaitFrames(4);

        // Below the shared dead zone: not the player's input, so the authored
        // idle spin keeps ownership and the planet is not dragged around.
        float before = preview.SpinAngleDegrees;
        yield return PushStick(_pad, new Vector2(0.10f, 0.06f), 0.6f);
        float delta = ForwardDelta(before, preview.SpinAngleDegrees);
        Assert.IsFalse(preview.IsPlayerControlled, "a drifting stick is not player input");
        Assert.Less(delta, 12f, "the planet keeps its slow idle speed (delta "
                                + delta.ToString("0.0") + " deg)");

        // A worn stick that rests just past the cursor dead zone (but below the
        // rotation engagement threshold) must still not hijack the display.
        float restedStart = preview.SpinAngleDegrees;
        yield return PushStick(_pad, new Vector2(0.24f, 0f), 0.6f);
        Assert.IsFalse(preview.IsPlayerControlled,
            "the rotation only engages above the authored engagement threshold");
        Assert.Less(ForwardDelta(restedStart, preview.SpinAngleDegrees), 12f,
            "and the authored idle speed is untouched");

        // The next real push still takes over immediately (no sticky dead zone).
        yield return PushStick(_pad, new Vector2(0f, 1f), 0.35f);
        Assert.IsTrue(preview.IsPlayerControlled, "a real push takes over at once");
    }

    // ====================================================================
    // 6. The ship's physical terminal: pad rotation only while focused
    // ====================================================================

    [UnityTest]
    public IEnumerator Overview_ConsoleTerminal_RotatesOnlyWhileFocused()
    {
        GameSession.Ensure().ResetSession();
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        yield return LoadScene(GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitUntil(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady && ship.Player != null;
        });

        _pad = InputSystem.AddDevice<Gamepad>();
        yield return WaitFrames(4);

        WorldConsoleSurface overview = ship.Consoles[0].GetComponent<WorldConsoleSurface>();
        yield return WaitUntil(() => overview.IsReady);
        ShipConsoleScreenHost host = overview.ScreenHost;
        Assert.IsNotNull(host, "console 01 hosts a screen");
        Assert.AreEqual(AppScreen.Overview, host.MappedScreen, "console 01 is the Overview terminal");

        OverviewScreen page = host.OverviewView;
        Assert.IsNotNull(page, "the terminal exposes its Overview page");
        Assert.IsTrue(page.ShowBackRow,
            "the console view's B leaves the page's context, so the row is honest");
        Assert.IsFalse(page.OwnsBack, "the console focus layer keeps the Back press");

        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        Assert.IsNotNull(preview, "shared planet rig");
        Assert.IsFalse(page.IsPlanetInteractive,
            "an unfocused terminal never drives the planet");

        // Unfocused: the stick must not take the rotation over.
        yield return PushStick(_pad, new Vector2(1f, 0f), 0.5f);
        Assert.IsFalse(preview.IsPlayerControlled,
            "the stick does nothing while the player is not focused on the terminal");

        // Walk up to the bridge terminal and focus it with the real route.
        SurfacePlayerController player = ship.Player;
        player.TeleportTo(ConsolePositions[0] + ScreenDirs[0] * 1.7f);
        player.SetFacing(Mathf.Atan2(-ScreenDirs[0].x, -ScreenDirs[0].z) * Mathf.Rad2Deg, 0f);
        yield return WaitFrames(3);
        Physics.SyncTransforms();

        ShipConsoleInteractable console = ship.Consoles[0];
        bool entered = false;
        for (int f = 0; f < 300 && !entered; f++)
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

        Assert.IsTrue(entered, "E entered the bridge terminal");
        yield return WaitUntil(() => ship.Focus.FocusActive);
        yield return WaitFrames(6);
        Assert.IsTrue(page.IsPlanetInteractive, "the focused terminal drives the planet");
        StringAssert.Contains(OverviewScreen.RotatePromptLabel, page.PromptBar.PromptText,
            "the terminal's key line offers the planet rotation");
        StringAssert.Contains("返回", page.PromptBar.PromptText,
            "and the Back row, because B really leaves this context");

        float before = preview.SpinAngleDegrees;
        yield return PushStick(_pad, new Vector2(1f, 0f), 0.7f);
        Assert.IsTrue(preview.IsPlayerControlled, "the focused terminal rotates the planet");
        Assert.Greater(ForwardDelta(before, preview.SpinAngleDegrees), 15f,
            "and it really turns with the stick");

        // Evidence: the terminal's own 1920x1080 RenderTexture, i.e. exactly what
        // the physical screen shows - with the gamepad key line on it.
        yield return WaitFrames(4);
        Directory.CreateDirectory(Output);
        SaveConsoleScreen(overview.CurrentRenderTexture,
            Path.Combine(Output, "console", "ship_console_overview_pad.png"));

        // Leaving the view hands the planet back to the authored idle spin.
        UiInput.InjectEscapeForAutomation();
        yield return WaitFrames(2);
        yield return WaitUntil(() => !ship.Focus.FocusActive);
        yield return WaitFrames(3);
        Assert.IsFalse(preview.IsPlayerControlled,
            "leaving the terminal returns the planet to the authored idle spin");
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // helpers
    // ====================================================================

    private IEnumerator LoadHubOverview()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        SceneManager.LoadScene(BootController.HubSceneName);

        AppController app = null;
        for (int i = 0; i < 600 && (app == null || !app.IsReady); i++)
        {
            app = Object.FindAnyObjectByType<AppController>();
            yield return null;
        }

        Assert.IsNotNull(app, "hub app loaded");
        Assert.IsTrue(app.IsReady, "hub app bound");

        _pad = InputSystem.AddDevice<Gamepad>();
        for (int i = 0; i < 3; i++)
        {
            yield return null;
        }

        app.ShowScreen(AppScreen.Overview);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "overview is the active screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one screen is drawn");
        _app = app;
    }

    /// <summary>
    /// Phase 09B PASS 7 follow-up: the planet display name is ONE name across
    /// every player-facing surface. The internal IDs (the surface scene, the
    /// routing, the GameSession fields) keep their NEREID names on purpose.
    /// </summary>
    private static void AssertPlayerFacingCopyUsesCurrentPlanet()
    {
        string root = Path.Combine(Application.dataPath, "..");
        string[] sources =
        {
            "Assets/_Project/UI/UXML/MainApp.uxml",
            "Assets/_Project/UI/UXML/ShipDeployConfirm.uxml",
            "Assets/_Project/UI/UXML/SurfaceHUD.uxml",
            "Assets/_Project/UI/UXML/SurfaceWrist.uxml",
            "Assets/_Project/UI/UXML/ShipHUD.uxml",
            "Assets/_Project/UI/UXML/MainMenu.uxml"
        };

        foreach (string source in sources)
        {
            string path = Path.Combine(root, source);
            Assert.IsTrue(File.Exists(path), "player-facing source exists: " + source);
            string copy = File.ReadAllText(path);
            StringAssert.DoesNotContain("NEREID-04", copy,
                "no stale planet name in " + source);
        }

        string mainApp = File.ReadAllText(Path.Combine(root, sources[0]));
        StringAssert.Contains("勘测 " + PlanetIdentity.DisplayName, mainApp,
            "the mission objective names the current planet");
        StringAssert.Contains(PlanetIdentity.ExpeditionContext, mainApp,
            "the preparation context names the current planet");

        StringAssert.Contains("text=\"" + PlanetIdentity.DisplayName + "\"",
            File.ReadAllText(Path.Combine(root, sources[1])),
            "the deployment sheet names the current planet");
        StringAssert.Contains("text=\"" + PlanetIdentity.DisplayName + "\"",
            File.ReadAllText(Path.Combine(root, sources[2])),
            "the surface HUD names the current planet");
        StringAssert.Contains(PlanetIdentity.SurfaceIdentity,
            File.ReadAllText(Path.Combine(root, sources[3])),
            "the wrist identity names the current planet");

        // The airlock prompt reads the shared source instead of a hardcoded name.
        string airlock = File.ReadAllText(
            Path.Combine(root, "Assets/_Project/Scripts/Gameplay/ShipAirlockInteractable.cs"));
        StringAssert.Contains("PlanetIdentity.SurfacePrompt", airlock,
            "the airlock prompt reads the shared planet name");
        StringAssert.DoesNotContain("NEREID-04", airlock,
            "the airlock prompt carries no stale planet name");
    }

    private static VisualElement ScreenRoot(AppController app)    {
        VisualElement root = app.UiRoot.Q<VisualElement>(ElementNames.OverviewScreen);
        Assert.IsNotNull(root, "overview screen root");
        return root;
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }

    private static string HeaderText(VisualElement root, string panelName)
    {
        VisualElement panel = root.Q<VisualElement>(panelName);
        Assert.IsNotNull(panel, "panel " + panelName);
        Label header = panel.Q<Label>(null, "panel-header-text");
        Assert.IsNotNull(header, panelName + " has a header");
        return header.text;
    }

    private static void AssertBarRatio(VisualElement root, string fillName, float expectedPercent,
        string label)
    {
        VisualElement fill = root.Q<VisualElement>(fillName);
        Assert.IsNotNull(fill, "missing bar fill " + fillName);
        Assert.IsNotNull(fill.parent, "bar fill has a track parent");
        float track = fill.parent.resolvedStyle.width;
        float filled = fill.resolvedStyle.width;
        Assert.IsTrue(track > 1f, label + " track measured");
        float ratio = filled / track * 100f;
        Assert.IsTrue(Mathf.Abs(ratio - expectedPercent) <= 5f,
            label + " expected ~" + expectedPercent + "% but was " + ratio.ToString("0") + "%");
    }

    private static bool IsRightOf(VisualElement element, VisualElement reference)
    {
        return element != null && reference != null
               && element.worldBound.xMin >= reference.worldBound.xMax - 8f;
    }

    private static bool HasClass(VisualElement root, string elementName, string className)
    {
        VisualElement element = root.Q<VisualElement>(elementName);
        return element != null && element.ClassListContains(className);
    }

    private static Rect WorldBoundOf(VisualElement root, string elementName)
    {
        VisualElement element = root.Q<VisualElement>(elementName);
        Assert.IsNotNull(element, "missing element " + elementName);
        return element.worldBound;
    }

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
    }

    /// <summary>Waits in REAL time: rotation is integrated per frame, so the
    /// interesting quantities are seconds, not frames.</summary>
    private static IEnumerator WaitSeconds(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = 2400)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private static IEnumerator LoadScene(string name)
    {
        SceneManager.LoadScene(name);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == name);
        yield return WaitFrames(3);
    }

    /// <summary>Forward rotation delta in degrees (wraps cleanly at 360).</summary>
    private static float ForwardDelta(float before, float after)
    {
        return Mathf.Repeat(after - before, 360f);
    }

    /// <summary>One real pad press: queue the button, let the page tick, release.</summary>
    private static IEnumerator Press(Gamepad pad, GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        yield return WaitFrames(3);

        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return WaitFrames(3);
    }

    /// <summary>Holds the RIGHT stick for a real interval, then releases it.</summary>
    private static IEnumerator PushStick(Gamepad pad, Vector2 direction, float seconds)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = direction });
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return WaitFrames(2);
    }

    private static IEnumerator CapturePage(AppController app, string path)
    {
        Texture2D shot = null;
        yield return ReadPanel(app, texture => shot = texture);
        Save(shot, path, new Rect(0, 0, ShotWidth, ShotHeight));
        Object.Destroy(shot);
    }

    private static IEnumerator CaptureRegion(AppController app, string path, Rect panelRect)
    {
        Texture2D shot = null;
        yield return ReadPanel(app, texture => shot = texture);
        Save(shot, path, panelRect);
        Object.Destroy(shot);
    }

    /// <summary>Renders the live panel at 1920x1080 and hands the readback over.</summary>
    private static IEnumerator ReadPanel(AppController app, System.Action<Texture2D> onRead)
    {
        PanelSettings panel = app.Document.panelSettings;
        Assert.IsNotNull(panel, "panel settings");

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

        onRead?.Invoke(texture);
    }

    /// <summary>Writes a region of the readback (panel coordinates, y down).</summary>
    private static void Save(Texture2D source, string path, Rect region)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(region.xMin), 0, source.width - 1);
        int width = Mathf.Clamp(Mathf.RoundToInt(region.width), 1, source.width - x);
        int yTop = Mathf.Clamp(Mathf.FloorToInt(region.yMin), 0, source.height - 1);
        int height = Mathf.Clamp(Mathf.RoundToInt(region.height), 1, source.height - yTop);

        var crop = new Texture2D(width, height, TextureFormat.RGB24, false);
        // Texture rows start at the BOTTOM, the panel's y at the top: flip.
        crop.SetPixels(source.GetPixels(x, source.height - (yTop + height), width, height));
        crop.Apply();

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, crop.EncodeToPNG());
        Object.Destroy(crop);

        Assert.Greater(new FileInfo(path).Length, 20000, "visible capture: " + path);
    }

    /// <summary>Saves a console RenderTexture: exactly what the physical screen
    /// shows, including the page's own controller key line.</summary>
    private static void SaveConsoleScreen(RenderTexture source, string path)
    {
        Assert.IsNotNull(source, "the console exposes its RenderTexture");
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.Destroy(texture);

        Assert.Greater(new FileInfo(path).Length, 100000, "visible console capture: " + path);
    }
}
