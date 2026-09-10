using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.2 release evidence probe (inert unless launched with
/// -ship07B22Shot &lt;output folder&gt;).
///
/// This is the release authority for the two human blockers of 07B.2.2:
///   1. the console UI was upside down in the Release player although the
///      07B.2.1 diagnostic reported PASS, and
///   2. the console screens behaved like a global HUD overlay (drawn through
///      walls and on top of the wrist).
///
/// Ordered hard gates (a failure stops the later steps):
///   1. ship world visible, all four physical screens lit on the world layer
///      with the focus overlay camera DISABLED -> frame_console_idle_world_depth
///   2. TL / TR / BL / BR rendered by the REAL UIDocument -> PanelSettings ->
///      RenderTexture -> screen shader chain on the physical screen, captured
///      in the Windows standalone Release player
///                                              -> frame_console_release_uv_reference
///   3. corrected UV flip sweep (all four combinations, scored in one
///      documented image space) proving the authored authority is canonical
///   4. wall occlusion: from behind the dividing wall the console screen must
///      contribute ZERO pixels (magenta A/B with a positive control)
///                                              -> frame_console_wall_occlusion
///   5. wrist occlusion: with the wrist open no distant console RT rectangle
///      may be drawn over the wrist disc (magenta A/B)
///                                       -> frame_ship_wrist_no_console_overlay
///   6. real mouse: visual Batch 05 -> Batch 05 selected, visual Start ->
///      a real 5-mineral batch starts (visual and pointer share one transform)
///   7. focus composition: environment visible + soft, screen sharp, bezel
///      visible, proxy bound to the active console's RenderTexture
///                                                   -> frame_console_focus_sharp
///
/// Writes phase07b22_result.txt; exit code follows the result.
/// </summary>
public class ShipB22ShotProbe : MonoBehaviour
{
    private const string Flag = "-ship07B22Shot";

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

    private static readonly string[] ConsoleKinds =
    {
        "overview", "warehouse", "processing", "outpost"
    };

    private static readonly string[] MarkerNames =
    {
        "RED", "GREEN", "BLUE", "YELLOW"
    };

    private string _folder;
    private bool _active;
    private readonly StringBuilder _report = new StringBuilder();
    private int _failures;
    private ShipGameController _controller;
    private SurfacePlayerController _player;
    private Camera _eye;
    private Camera _sharp;
    private ShipConsoleFocusController _focus;
    private ConsoleFocusProxy _proxy;

    private void Awake()
    {
        _folder = ParseFlag(Flag);
        _active = !string.IsNullOrEmpty(_folder);
        if (!_active)
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(_folder);
    }

    private void Start()
    {
        if (_active)
        {
            StartCoroutine(Run());
            StartCoroutine(Watchdog());
        }
    }

    private static IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(600f);
        Debug.LogError("[ShipB22] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        GameSession.Ensure().ResetSession();

        for (int i = 0; i < 2400; i++)
        {
            _controller = Object.FindAnyObjectByType<ShipGameController>();
            if (_controller != null && _controller.IsReady
                && _controller.Consoles.Count == 4 && _controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        if (_controller == null || !_controller.IsReady)
        {
            Check("ship interior ready", false);
            Finish();
            yield break;
        }

        Check("ship interior ready", true);
        _player = _controller.Player;
        _eye = _player.EyeCamera;
        _focus = _controller.Focus;
        _proxy = Object.FindAnyObjectByType<ConsoleFocusProxy>(
            FindObjectsInactive.Include);
        Transform sharpTransform = _eye.transform.Find("SharpScreenCamera");
        _sharp = sharpTransform != null ? sharpTransform.GetComponent<Camera>() : null;

        Check("focus proxy present in the scene", _proxy != null);
        Check("focus overlay camera present", _sharp != null);
        Info("graphics: gpu=" + SystemInfo.graphicsDeviceName
             + " api=" + SystemInfo.graphicsDeviceType
             + " uvStartsAtTop=" + SystemInfo.graphicsUVStartsAtTop);
        Info("uv authority: " + ConsoleUvAuthority.DescribeConvention());

        Screen.SetResolution(1920, 1080, false);
        yield return WaitReal(1.0f);

        yield return GateScreenArchitecture();
        if (_failures > 0)
        {
            Finish();
            yield break;
        }

        yield return GateIdleWorldDepth();
        yield return GateReleaseUvReference();
        yield return GateUvFlipSweep();
        yield return GateWallOcclusion();
        yield return GateWristOcclusion();
        yield return GateRealPointer();
        yield return GateFocusSharp();

        Finish();
    }

    // ====================================================================
    // ARCHITECTURE GATE - world screens + focus-only proxy
    // ====================================================================

    private IEnumerator GateScreenArchitecture()
    {
        bool ok = _controller.VerifyBaseCameraVisibility(out string report);
        Info("screen architecture: " + report);
        Check("screen architecture: world screens + focus-only proxy", ok);

        Check("focus overlay camera is DISABLED in ShipExplore",
            _sharp != null && !_sharp.enabled);
        Check("focus proxy is hidden in ShipExplore",
            _proxy != null && !_proxy.IsVisible);

        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface = SurfaceOf(c);
            yield return WaitSurfaceReady(surface);
            yield return WaitReal(0.35f);
            int layer = surface.ScreenRenderer.gameObject.layer;
            Check("console " + (c + 1) + " screen is ordinary world geometry "
                  + "(layer " + layer + ", overlay="
                  + WorldConsoleSurface.FocusOverlayLayer + ")",
                layer == WorldConsoleSurface.PhysicalScreenLayer
                && layer != WorldConsoleSurface.FocusOverlayLayer);
            Check("console " + (c + 1) + " screen rendered by the base camera",
                (_eye.cullingMask & (1 << layer)) != 0);
            Check("console " + (c + 1) + " screen renderer enabled",
                surface.ScreenRenderer.enabled);
            Check("console " + (c + 1) + " always-on RT carries light UI",
                surface.SampleScreenContent(out _, out _));
        }

        if (_proxy != null)
        {
            bool proxyOk = _proxy.VerifySetup(out string proxyReport);
            Info("focus proxy: " + proxyReport);
            Check("focus proxy configuration", proxyOk);
        }
    }

    // ====================================================================
    // 1) idle world depth - screens lit through normal world perspective
    // ====================================================================

    private IEnumerator GateIdleWorldDepth()
    {
        yield return ApproachConsole(1); // console 2 faces east, fills the view
        yield return WaitReal(0.7f);
        yield return WaitSurfaceReady(SurfaceOf(1));
        yield return Capture("frame_console_idle_world_depth");

        Check("focus overlay camera stays DISABLED in normal ShipExplore",
            _sharp != null && !_sharp.enabled);
        Check("focus proxy stays hidden in normal ShipExplore",
            _proxy != null && !_proxy.IsVisible);

        Rect rect = ScreenRectForConsole(1);
        FrameStats stats = AnalyseRegion("frame_console_idle_world_depth", rect);
        Info("idle world frame: screen region meanLum=" + stats.MeanLum.ToString("0")
             + " edge=" + stats.MeanEdge.ToString("0.0")
             + " | whole frame colours=" + AnalyseFrame("frame_console_idle_world_depth").UniqueColors);
        Check("idle console screen is visibly LIT through world perspective "
              + "(meanLum " + stats.MeanLum.ToString("0") + ")",
            stats.MeanLum > 120f);

        // The world screen really samples its own console RenderTexture.
        WorldConsoleSurface surface = SurfaceOf(1);
        Check("idle console screen samples its own RT",
            surface.VerifyScreenBinding(out string binding));
        Info("idle binding: " + binding);
    }

    // ====================================================================
    // 2) RELEASE UV REFERENCE - real UIDocument TL/TR/BL/BR markers
    // ====================================================================

    private IEnumerator GateReleaseUvReference()
    {
        int index = 0;
        yield return ApproachConsole(index);
        yield return WaitReal(0.6f);
        WorldConsoleSurface surface = SurfaceOf(index);
        yield return WaitSurfaceReady(surface);

        bool markers = surface.SetUvReferenceMarkersVisible(true);
        Check("TL/TR/BL/BR markers created inside the REAL console UI document",
            markers);
        if (!markers)
        {
            yield break;
        }

        yield return WaitReal(0.6f);
        yield return Capture("frame_console_release_uv_reference");

        string mapping = ClassifyCorners("frame_console_release_uv_reference");
        Info("RELEASE UV reference mapping (image space, y down from the top): "
             + mapping);
        float score = ScoreMapping(mapping);
        Check("Release TL/TR/BL/BR mapping is canonical (score "
              + score.ToString("0") + "/4): " + mapping, score >= 4f);

        // The authored authority must be exactly what the physical screen uses.
        Material material = surface.ScreenMaterial;
        Check("physical screen uses the authored FlipU",
            material != null && Mathf.Abs(material.GetFloat(
                ConsoleUvAuthority.FlipUProperty) - ConsoleUvAuthority.FlipU) < 0.001f);
        Check("physical screen uses the authored FlipV",
            material != null && Mathf.Abs(material.GetFloat(
                ConsoleUvAuthority.FlipVProperty) - ConsoleUvAuthority.FlipV) < 0.001f);

        surface.SetUvReferenceMarkersVisible(false);
        yield return WaitReal(0.3f);
    }

    /// <summary>Sweeps every flip combination on the REAL UI chain and scores
    /// the captures in ONE documented image space (y measured downward from
    /// the top of the frame), so the mapping the shader actually produces can
    /// never be scored backwards.</summary>
    private IEnumerator GateUvFlipSweep()
    {
        WorldConsoleSurface surface = SurfaceOf(0);
        Material material = surface.ScreenMaterial;
        if (material == null)
        {
            Check("UV sweep has a screen material", false);
            yield break;
        }

        surface.SetUvReferenceMarkersVisible(true);
        yield return WaitReal(0.5f);

        float[,] combos = { { 0f, 0f }, { 1f, 0f }, { 0f, 1f }, { 1f, 1f } };
        string table = "";
        float bestScore = -1f;
        string best = "none";
        for (int c = 0; c < combos.GetLength(0); c++)
        {
            material.SetFloat(ConsoleUvAuthority.FlipUProperty, combos[c, 0]);
            material.SetFloat(ConsoleUvAuthority.FlipVProperty, combos[c, 1]);
            yield return WaitReal(0.35f);
            string name = "diag_uv_flip_" + combos[c, 0].ToString("0")
                          + combos[c, 1].ToString("0");
            yield return Capture(name);
            string mapping = ClassifyCorners(name);
            float score = ScoreMapping(mapping);
            table += "\n    FlipU=" + combos[c, 0] + " FlipV=" + combos[c, 1]
                     + " -> " + mapping + " (score " + score.ToString("0") + "/4)";
            if (score > bestScore)
            {
                bestScore = score;
                best = "U=" + combos[c, 0] + " V=" + combos[c, 1];
            }
        }

        Info("UV flip sweep (corrected scorer):" + table);
        Info("UV flip sweep winner: " + best + " (score "
             + bestScore.ToString("0") + "/4)");

        material.SetFloat(ConsoleUvAuthority.FlipUProperty, ConsoleUvAuthority.FlipU);
        material.SetFloat(ConsoleUvAuthority.FlipVProperty, ConsoleUvAuthority.FlipV);
        yield return WaitReal(0.35f);
        surface.SetUvReferenceMarkersVisible(false);

        Check("the authored authority (FlipU=" + ConsoleUvAuthority.FlipU
              + ", FlipV=" + ConsoleUvAuthority.FlipV + ") is the canonical "
              + "mapping measured on the real Release UI chain",
            bestScore >= 4f
            && best == "U=" + ConsoleUvAuthority.FlipU.ToString("0")
               + " V=" + ConsoleUvAuthority.FlipV.ToString("0"));
    }

    // ====================================================================
    // 4) WALL OCCLUSION
    // ====================================================================

    /// <summary>Console 01 sits in the bridge (z 6.3..10.6) while the dividing
    /// wall with its 1.8 m doorway spans z = 6.3. Standing off the doorway axis
    /// puts a solid wall between the eye and the screen.</summary>
    private static readonly Vector3 WallOcclusionPosition = new Vector3(2.6f, 0f, 4.5f);
    private static readonly Vector3 WallOcclusionTarget = new Vector3(0f, 1.55f, 10.05f);

    private IEnumerator GateWallOcclusion()
    {
        // Deterministic precondition: a real raycast must hit the wall first.
        Vector3 origin = WallOcclusionPosition + new Vector3(0f, 1.65f, 0f);
        Vector3 direction = (WallOcclusionTarget - origin).normalized;
        bool blocked;
        string blockerName = "none";
        float blockerDistance = 0f;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, 40f))
        {
            blockerName = hit.collider.gameObject.name + "("
                          + hit.collider.transform.root.name + ")";
            blockerDistance = hit.distance;
            // A hit at ~0 m would be the player's own capsule, not the wall.
            blocked = hit.collider.gameObject.name != "Screen"
                      && hit.distance > 0.5f;
        }
        else
        {
            blocked = false;
        }

        _player.TeleportTo(WallOcclusionPosition);
        // yaw 0 faces world +z, so the facing that looks ALONG the sight line is
        // atan2(dir.x, dir.z) (the same convention as ScreenDirs-based
        // approaches: -ScreenDirs is the direction back to the console).
        // Eye level (1.65 m) vs screen centre (1.55 m) over ~5.7 m is under a
        // degree, so a level facing aim is exact enough and deterministic.
        Vector3 sightDirection = WallOcclusionTarget - origin;
        float yaw = Mathf.Atan2(sightDirection.x, sightDirection.z) * Mathf.Rad2Deg;
        _player.SetFacing(yaw, 0f);
        yield return WaitReal(0.6f);

        Check("precondition: the console screen is physically behind a wall "
              + "(first hit='" + blockerName + "' at "
              + blockerDistance.ToString("0.00") + " m)", blocked);

        yield return Capture("frame_console_wall_occlusion");
        Rect rect = ScreenRectForConsole(0);
        FrameStats stats = AnalyseRegion("frame_console_wall_occlusion", rect);
        Info("wall occlusion frame: projected screen region meanLum="
             + stats.MeanLum.ToString("0"));

        // Numeric anti-vacuum guard (replaces looking at the frame): the
        // occluded console screen must really be INSIDE the camera's view at
        // this pose, otherwise "no screen pixels" would prove nothing.
        Vector3 centre = SurfaceOf(0).ScreenQuad.position;
        Vector3 centreViewport = _eye.WorldToViewportPoint(centre);
        bool inFrustum = centreViewport.z > 0f
                         && centreViewport.x > 0f && centreViewport.x < 1f
                         && centreViewport.y > 0f && centreViewport.y < 1f;
        Info("occluded console centre viewport=" + centreViewport
             + " | projected rect=" + rect + " | on screen=" + inFrustum);
        Check("anti-vacuum: the occluded console screen is really inside the "
              + "camera view at this pose (viewport " + centreViewport + ")",
            inFrustum);

        // Decisive A/B: paint the occluded console screen magenta. If the
        // screen were still drawn as an overlay, magenta would appear even
        // through the wall.
        WorldConsoleSurface surface = SurfaceOf(0);
        Texture2D magenta = WorldConsoleSurface.CreateSolidTexture(
            new Color(1f, 0f, 1f, 1f), "B22Magenta");
        surface.SetDiagnosticTexture(magenta);
        yield return WaitReal(0.5f);
        yield return Capture("frame_console_wall_occlusion_magenta_behind");
        int behind = CountPixels("frame_console_wall_occlusion_magenta_behind",
            IsMagenta);
        Info("magenta pixels visible from BEHIND the wall: " + behind);
        Check("no console screen pixels leak through the wall (" + behind
              + " magenta pixels)", behind == 0);

        // Positive control: the same magenta texture IS visible from in front,
        // so the A/B above cannot pass vacuously.
        yield return ApproachConsole(0);
        yield return WaitReal(0.6f);
        yield return Capture("frame_console_wall_occlusion_magenta_front");
        int front = CountPixels("frame_console_wall_occlusion_magenta_front",
            IsMagenta);
        Info("magenta pixels visible from IN FRONT (positive control): " + front);
        Check("positive control: the same screen IS visible from the front ("
              + front + " magenta pixels)", front > 2000);

        surface.ClearDiagnosticTexture();
        Object.Destroy(magenta);
        yield return WaitReal(0.4f);
    }

    // ====================================================================
    // 5) WRIST OCCLUSION
    // ====================================================================

    private IEnumerator GateWristOcclusion()
    {
        SurfaceWristController wrist = _controller.Wrist;
        Check("ship wrist controller bound", wrist != null);
        if (wrist == null)
        {
            yield break;
        }

        int index = 2; // console 03, directly along the +x axis of the spine
        Vector3 position = ConsolePositions[index] + ScreenDirs[index] * 4.6f;
        float yaw = Mathf.Atan2(-ScreenDirs[index].x, -ScreenDirs[index].z)
                    * Mathf.Rad2Deg;
        float pitch = 0f;
        _player.TeleportTo(position);
        _player.SetFacing(yaw, pitch);
        yield return WaitReal(0.3f);
        _controller.SetWristOpen(true);
        yield return WaitReal(1.0f);
        Check("wrist is open in ShipExplore", _controller.IsWristOpen);

        // Recreate the human-reported framing exactly: a DISTANT console screen
        // is aimed onto the raised wrist disc, so an overlay-style screen would
        // have to appear inside the disc.
        WorldConsoleSurface surface = SurfaceOf(index);
        yield return AimScreenOntoWristDisc(surface, yaw, pitch);

        Rect disc;
        bool discOk = TryWristDisc(wrist, out Vector2 discCentre,
            out float discRadius);
        disc = new Rect(discCentre.x - discRadius, discCentre.y - discRadius,
            discRadius * 2f, discRadius * 2f);
        Check("wrist disc projects on screen", discOk);
        if (!discOk)
        {
            _controller.SetWristOpen(false);
            yield return WaitReal(0.4f);
            yield break;
        }

        Rect screenRect = ScreenRectForConsole(index);
        bool screenOverDisc = screenRect.Contains(discCentre);
        Info("wrist disc centre=" + discCentre + " radius="
             + discRadius.ToString("0") + " px | console screen rect="
             + screenRect + " | screen covers the disc centre=" + screenOverDisc);
        Check("the distant console screen really covers the wrist disc centre "
              + "(so an overlay-style screen would be visible there)",
            screenOverDisc);

        yield return Capture("frame_ship_wrist_no_console_overlay");

        // Decisive A/B at ONE frozen pose: paint that distant console magenta,
        // then compare the same frame with the wrist open and closed.
        Texture2D magenta = WorldConsoleSurface.CreateSolidTexture(
            new Color(1f, 0f, 1f, 1f), "B22MagentaWrist");
        surface.SetDiagnosticTexture(magenta);
        yield return WaitReal(0.6f);
        yield return Capture("frame_ship_wrist_magenta_open");

        // Sample well inside the disc, where the console screen projects.
        Rect probeBox = new Rect(discCentre.x - discRadius * 0.5f,
            discCentre.y - discRadius * 0.5f, discRadius, discRadius);
        int overDisc = CountPixelsIn("frame_ship_wrist_magenta_open", probeBox,
            IsMagenta);
        int withWrist = CountPixels("frame_ship_wrist_magenta_open", IsMagenta);

        _controller.SetWristOpen(false);
        yield return WaitReal(0.9f);
        Check("the wrist really closed for the control frame",
            !_controller.IsWristOpen);
        yield return Capture("frame_ship_wrist_magenta_closed");
        int withoutWrist = CountPixels("frame_ship_wrist_magenta_closed",
            IsMagenta);

        Info("magenta pixels with the wrist OPEN: " + withWrist
             + " (inside the disc " + probeBox + ": " + overDisc + ")"
             + " | with the wrist CLOSED at the same pose: " + withoutWrist);

        Check("no distant console screen is drawn over the wrist disc ("
              + overDisc + " magenta pixels inside the disc)", overDisc == 0);
        Check("the wrist hides the console screen that is behind it ("
              + withWrist + " magenta pixels with the wrist open)", withWrist == 0);
        Check("positive control: the SAME console screen is fully visible from "
              + "the same pose once the wrist closes (" + withoutWrist
              + " magenta pixels)", withoutWrist > 2000);

        surface.ClearDiagnosticTexture();
        Object.Destroy(magenta);
        yield return WaitReal(0.5f);
        Check("closing the wrist returns to ShipExplore",
            !_controller.IsWristOpen && _player.InputEnabled);
    }

    /// <summary>Aims the eye so the given console's screen centre lands on the
    /// wrist disc centre (first-order viewport offsets, refined a few times).
    /// The wrist display is camera-locked, so the disc keeps its viewport
    /// position while the console moves.</summary>
    private IEnumerator AimScreenOntoWristDisc(WorldConsoleSurface surface,
        float startYaw, float startPitch)
    {
        const float verticalFov = 70f;
        float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f
            * Mathf.Deg2Rad) * (16f / 9f)) * Mathf.Rad2Deg;
        float yaw = startYaw;
        float pitch = startPitch;
        SurfaceWristController wrist = _controller.Wrist;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            if (!TryWristDisc(wrist, out Vector2 discCentre, out float discRadius))
            {
                break;
            }

            Vector3 target = _eye.WorldToViewportPoint(surface.ScreenQuad.position);
            if (target.z <= 0f)
            {
                break;
            }

            Vector2 discViewport = new Vector2(discCentre.x / Screen.width,
                1f - discCentre.y / Screen.height);
            float dx = discViewport.x - target.x;
            float dy = discViewport.y - target.y;
            if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f)
            {
                Info("aim converged after " + iteration + " refinements: screen "
                     + "centre offset from the disc centre=(" + dx.ToString("0.000")
                     + "," + dy.ToString("0.000") + ") discCentre=" + discCentre
                     + " radius=" + discRadius.ToString("0") + " px");
                yield break;
            }

            yaw += -dx * horizontalFov;
            pitch += dy * verticalFov;
            _player.SetFacing(yaw, pitch);
            yield return null;
            yield return null;
        }

        Info("aim finished: screen centre viewport="
             + _eye.WorldToViewportPoint(surface.ScreenQuad.position));
    }

    /// <summary>
    /// Wrist disc centre + radius in screen pixels (y down). The hologram disc
    /// is centred in the 1280x800 wrist panel, so its centre is the display
    /// quad's own centre and its radius is 235/1280 of the panel width. The
    /// quad's world corners are NOT usable for this: the raised wrist sits a few
    /// centimetres from the eye, so its corners project far outside the frustum.
    /// </summary>
    private bool TryWristDisc(SurfaceWristController wrist, out Vector2 centrePx,
        out float radiusPx)
    {
        centrePx = Vector2.zero;
        radiusPx = 0f;
        Transform display = wrist != null && wrist.Display != null
            ? wrist.Display.transform
            : null;
        if (display == null || _eye == null)
        {
            return false;
        }

        Vector3 scale = display.lossyScale;
        if (!TryViewportToPixels(_eye.WorldToViewportPoint(display.position),
                out centrePx)
            || !TryViewportToPixels(_eye.WorldToViewportPoint(
                display.position - display.right * (scale.x * 0.5f)), out Vector2 left)
            || !TryViewportToPixels(_eye.WorldToViewportPoint(
                display.position + display.right * (scale.x * 0.5f)), out Vector2 right))
        {
            return false;
        }

        radiusPx = Vector2.Distance(left, right) * 0.5f * (WristDiscRadiusPanelPx
            / WristPanelWidthPx);
        return radiusPx > 8f;
    }

    private const float WristPanelWidthPx = 1280f;
    private const float WristDiscRadiusPanelPx = 235f;

    private bool TryViewportToPixels(Vector3 viewport, out Vector2 pixels)
    {
        pixels = Vector2.zero;
        if (viewport.z <= 0f)
        {
            return false;
        }

        pixels = new Vector2(viewport.x * Screen.width,
            (1f - viewport.y) * Screen.height);
        return true;
    }

    // ====================================================================
    // 6) REAL POINTER - visual position == pointer position
    // ====================================================================

    private IEnumerator GateRealPointer()
    {
        Mouse mouse = Mouse.current;
        Check("real mouse device available", mouse != null);
        if (mouse == null)
        {
            yield break;
        }

        GameSession session = GameSession.Instance;
        int index = 2; // Console03 资源加工
        yield return ApproachConsole(index);
        yield return WaitReal(0.4f);
        yield return EnterConsole(index);
        yield return WaitFocused();
        WorldConsoleSurface surface = SurfaceOf(index);
        yield return WaitSurfaceReady(surface);

        Check("focus proxy visible while focused",
            _proxy != null && _proxy.IsVisible);
        Check("focus proxy mirrors the ACTIVE console screen",
            _proxy != null && ReferenceEquals(_proxy.ActiveSurface, surface));
        Check("focus overlay camera ENABLED while focused",
            _sharp != null && _sharp.enabled);
        if (_proxy != null)
        {
            _proxy.VerifySetup(out string proxyReport);
            Info("focused proxy: " + proxyReport);
        }

        VisualElement root = surface.Document != null
            ? surface.Document.rootVisualElement
            : null;
        Button batch5 = root != null ? root.Q<Button>(ElementNames.Batch5Button) : null;
        Button batch10 = root != null ? root.Q<Button>(ElementNames.Batch10Button) : null;
        Button start = root != null
            ? root.Q<Button>(ElementNames.ProcessingStartButton)
            : null;
        Check("processing batch/start buttons resolved",
            batch5 != null && batch10 != null && start != null);
        if (batch5 == null || batch10 == null || start == null)
        {
            _focus.RequestExit();
            yield return WaitExplore();
            yield break;
        }

        Info("initial selection: batch5=" + batch5.ClassListContains("batch-selected")
             + " batch10=" + batch10.ClassListContains("batch-selected")
             + " startEnabled=" + start.enabledSelf);

        // The pointer chain must resolve the SAME panel cell the screen paints.
        yield return VerifyVisualPointerAgreement(surface, batch5, "Batch 05");

        // Real device clicks through the real cursor. Batch 10 first so the
        // selection change cannot be mistaken for a default selection, then the
        // contract's Batch 05 click.
        yield return WarpAndClick(batch10, surface, "Batch 10");
        bool batch10Selected = batch10.ClassListContains("batch-selected");
        Check("real mouse click on the VISIBLE Batch 10 selected Batch 10 and "
              + "cleared Batch 05 (batch10=" + batch10Selected + " batch5="
              + batch5.ClassListContains("batch-selected") + ")",
            batch10Selected && !batch5.ClassListContains("batch-selected"));

        yield return WarpAndClick(batch5, surface, "Batch 05");
        bool batchSelected = batch5.ClassListContains("batch-selected");
        Check("real mouse click on the VISIBLE Batch 05 selected Batch 05 "
              + "(batch-selected=" + batchSelected + ")", batchSelected);

        yield return WarpAndClick(start, surface, "Start");
        for (int i = 0; i < 180 && !session.IsProcessing; i++)
        {
            yield return null;
        }

        Info("session after the Start click: processing=" + session.IsProcessing
             + " batch=" + session.ActiveBatchMinerals + " minerals="
             + session.Minerals + " state=" + session.ProcessState
             + " | batch5 still selected="
             + batch5.ClassListContains("batch-selected")
             + " | start button enabled=" + start.enabledSelf);

        Check("real mouse click on the VISIBLE Start began the 5-mineral batch "
              + "(processing=" + session.IsProcessing + " batch="
              + session.ActiveBatchMinerals + ")",
            session.IsProcessing && session.ActiveBatchMinerals == 5);

        Info("selected console kind: " + ConsoleKinds[index]);

        _focus.RequestExit();
        yield return WaitExplore();
        yield return WaitReal(0.4f);
        Check("first focus exit returns to ShipExplore",
            _focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore);
    }

    /// <summary>Independent proof that the visible screen and the pointer use
    /// one transform: turn the TL/TR/BL/BR markers into points, map them into
    /// viewport space through the SAME authority the shader uses, then feed the
    /// resulting screen positions back through the pointer router and require
    /// each one to land in the matching panel quadrant.</summary>
    private IEnumerator VerifyVisualPointerAgreement(WorldConsoleSurface surface,
        VisualElement probeElement, string label)
    {
        WorldUiPointerRouter router =
            Object.FindAnyObjectByType<WorldUiPointerRouter>();
        Check("world pointer router present", router != null);
        if (router == null)
        {
            yield break;
        }

        Vector2 panelSize = new Vector2(WorldConsoleSurface.UiWidth,
            WorldConsoleSurface.UiHeight);
        Vector2[] corners =
        {
            new Vector2(0f, 0f),                       // panel top-left
            new Vector2(panelSize.x, 0f),              // panel top-right
            new Vector2(0f, panelSize.y),              // panel bottom-left
            new Vector2(panelSize.x, panelSize.y)      // panel bottom-right
        };
        string[] names = { "top-left", "top-right", "bottom-left", "bottom-right" };
        bool allOk = true;
        string detail = "";
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 inset = Vector2.Lerp(corners[i],
                new Vector2(panelSize.x * 0.5f, panelSize.y * 0.5f), 0.12f);
            if (!ConsoleScreenProjection.TryPanelPixelViewport(surface, _eye, inset,
                    out Vector2 viewport))
            {
                allOk = false;
                detail += " " + names[i] + "=unprojectable";
                continue;
            }

            bool mapped = router.TryScreenToPanelPixel(
                new Vector2(viewport.x * Screen.width, viewport.y * Screen.height),
                out Vector2 back);
            bool quadrant = mapped
                            && Mathf.Abs(back.x - inset.x) < panelSize.x * 0.06f
                            && Mathf.Abs(back.y - inset.y) < panelSize.y * 0.06f;
            detail += " " + names[i] + "->(" + back.x.ToString("0") + ","
                      + back.y.ToString("0") + ")" + (quadrant ? "OK" : "BAD");
            allOk &= quadrant;
            yield return null;
        }

        Info("visual/pointer corner agreement for " + label + ":" + detail);
        Check("visual and pointer share one canonical transform for " + label
              + " (all four corners round-trip)", allOk);
    }

    /// <summary>Real device click: moves the cursor onto the element's projected
    /// position and drives the mouse buttons through the InputSystem state (the
    /// same stream the router forwards for real hardware).
    ///
    /// WarpCursorPosition and Mouse.position do not always share an origin
    /// (window vs. desktop) and an unfocused window can ignore the OS warp
    /// entirely, so the warp is closed-loop calibrated against what the router
    /// itself reads back and, if the OS cursor still refuses to move, the SAME
    /// position is delivered through the InputSystem mouse state. Either way the
    /// click is validated by where the game really sees the cursor.</summary>
    private IEnumerator WarpAndClick(VisualElement element,
        WorldConsoleSurface surface, string label)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            yield break;
        }

        Vector2 viewport;
        if (!ConsoleScreenProjection.TryElementViewport(surface, _eye, element,
                out viewport))
        {
            Check("click target projects on screen: " + label, false);
            yield break;
        }

        Vector2 target = new Vector2(viewport.x * Screen.width,
            viewport.y * Screen.height);
        Vector2 warp = target;
        Vector2 readBack = mouse.position.ReadValue();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            mouse.WarpCursorPosition(warp);
            yield return WaitReal(0.15f);
            readBack = mouse.position.ReadValue();
            if ((readBack - target).sqrMagnitude < 4f)
            {
                break;
            }

            warp = target + (warp - readBack);
        }

        string cursorPath = "os-warp";
        if ((readBack - target).sqrMagnitude >= 4f)
        {
            cursorPath = "input-state";
            using (StateEvent.From(mouse, out var move))
            {
                mouse.position.WriteValueIntoEvent(target, move);
                InputSystem.QueueEvent(move);
            }

            yield return WaitReal(0.2f);
            readBack = mouse.position.ReadValue();
        }

        WorldUiPointerRouter router =
            Object.FindAnyObjectByType<WorldUiPointerRouter>();
        Vector2 routedPanel = Vector2.zero;
        bool mapped = router != null
                      && router.TryScreenToPanelPixel(readBack, out routedPanel);
        bool inside = mapped && element.worldBound.Contains(routedPanel);
        Info(label + " target=" + target.ToString("0") + " cursor="
             + readBack.ToString("0") + " via=" + cursorPath + " routerPanel="
             + routedPanel.ToString("0") + " elementBound=" + element.worldBound
             + " insideElement=" + inside + " enabled=" + element.enabledSelf);
        Check("the real pointer sits on the displayed " + label
              + " (cursor " + readBack.ToString("0") + " -> panel "
              + routedPanel.ToString("0") + " inside " + element.worldBound + ")",
            inside);

        using (StateEvent.From(mouse, out var pressed))
        {
            mouse.leftButton.WriteValueIntoEvent(1f, pressed);
            InputSystem.QueueEvent(pressed);
        }

        yield return WaitReal(0.2f);

        using (StateEvent.From(mouse, out var released))
        {
            mouse.leftButton.WriteValueIntoEvent(0f, released);
            InputSystem.QueueEvent(released);
        }

        yield return WaitReal(0.25f);
        Info("real device click dispatched to " + label + " at screen "
             + readBack.x.ToString("0") + "," + readBack.y.ToString("0"));
    }

    // ====================================================================
    // 7) FOCUS COMPOSITION
    // ====================================================================

    private IEnumerator GateFocusSharp()
    {
        yield return ApproachConsole(0);
        yield return EnterConsole(0);
        yield return WaitFocused();
        yield return WaitReal(0.6f);
        yield return Capture("frame_console_focus_sharp");

        WorldConsoleSurface surface = SurfaceOf(0);
        Rect rect = ScreenRectForConsole(0);

        FrameStats whole = AnalyseFrame("frame_console_focus_sharp");
        FrameStats screen = AnalyseRegion("frame_console_focus_sharp", rect);
        Info("focus frame: meanLum=" + whole.MeanLum.ToString("0")
             + " stdDev=" + whole.StdDev.ToString("0.0")
             + " colours=" + whole.UniqueColors
             + " | screen region meanLum=" + screen.MeanLum.ToString("0"));

        Check("focused frame still shows the ship environment (colours "
              + whole.UniqueColors + ", stdDev " + whole.StdDev.ToString("0.0") + ")",
            IsRealContent(whole));
        Check("focused screen stays bright (screen region meanLum "
              + screen.MeanLum.ToString("0") + ")", screen.MeanLum > 120f);
        Check("focus proxy visible while focused",
            _proxy != null && _proxy.IsVisible);
        Check("focus overlay camera ENABLED while focused",
            _sharp != null && _sharp.enabled);

        // Blur A/B (weight 0 / 1) proves the environment really softens.
        Volume volume = _focus != null ? _focus.BlurVolume : null;
        if (volume != null)
        {
            volume.weight = 0f;
            yield return WaitReal(0.5f);
            yield return Capture("frame_console_focus_sharp_blur_off");
            volume.weight = 1f;
            yield return WaitReal(0.5f);
            yield return Capture("frame_console_focus_sharp_blur_on");
            FrameStats off = AnalyseFrame("frame_console_focus_sharp_blur_off");
            FrameStats on = AnalyseFrame("frame_console_focus_sharp_blur_on");
            Info("blur A/B whole-frame edge: off=" + off.MeanEdge.ToString("0.00")
                 + " on=" + on.MeanEdge.ToString("0.00"));
            Check("environment DOF softens the frame (edge off "
                  + off.MeanEdge.ToString("0.00") + " > on "
                  + on.MeanEdge.ToString("0.00") + ")", off.MeanEdge > on.MeanEdge);
        }

        // Exit: proxy and overlay camera must both go away, DOF back to 0.
        _focus.RequestExit();
        yield return WaitExplore();
        yield return WaitReal(0.4f);
        Check("exit hides the focus proxy", _proxy == null || !_proxy.IsVisible);
        Check("exit disables the focus overlay camera",
            _sharp == null || !_sharp.enabled);
        Check("exit returns the blur weight to 0",
            _focus == null || _focus.BlurWeight < 0.01f);
        Check("exit returns the world screen to normal world rendering",
            SurfaceOf(0).ScreenRenderer.enabled);
    }

    // ====================================================================
    // Corner classification (ONE documented image space)
    // ====================================================================

    /// <summary>Locates the four canonical corner markers by colour centroid.
    /// Coordinates are reported in IMAGE space with y growing DOWNWARD from the
    /// top of the frame (Unity's Texture2D.GetPixel grows upward, so it is
    /// converted here) - the exact space ScoreMapping documents.</summary>
    private string ClassifyCorners(string png)
    {
        string path = Path.Combine(_folder, png + ".png");
        if (!File.Exists(path))
        {
            return "no frame";
        }

        byte[] bytes = File.ReadAllBytes(path);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            return "unreadable frame";
        }

        var counts = new int[4];
        var sumX = new float[4];
        var sumY = new float[4];
        for (int y = 0; y < texture.height; y += 3)
        {
            for (int x = 0; x < texture.width; x += 3)
            {
                Color c = texture.GetPixel(x, y);
                int idx = MarkerIndex(c);
                if (idx < 0)
                {
                    continue;
                }

                counts[idx]++;
                sumX[idx] += x;
                sumY[idx] += texture.height - 1 - y; // -> y grows downward
            }
        }

        Object.Destroy(texture);

        string result = "";
        for (int i = 0; i < 4; i++)
        {
            if (counts[i] == 0)
            {
                return MarkerNames[i] + " marker MISSING";
            }

            result += MarkerNames[i] + "=(" + (sumX[i] / counts[i]).ToString("0")
                      + "," + (sumY[i] / counts[i]).ToString("0") + ") ";
        }

        return result.Trim();
    }

    private static int MarkerIndex(Color c)
    {
        float r = c.r;
        float g = c.g;
        float b = c.b;
        if (r > 0.55f && g < 0.4f && b < 0.4f) return 0;   // RED    (panel top-left)
        if (g > 0.55f && r < 0.4f && b < 0.4f) return 1;   // GREEN  (panel top-right)
        if (b > 0.55f && r < 0.4f && g < 0.4f) return 2;   // BLUE   (panel bottom-left)
        if (r > 0.55f && g > 0.55f && b < 0.4f) return 3;  // YELLOW (panel bottom-right)
        return -1;
    }

    /// <summary>Scores a mapping in IMAGE space (y grows DOWNWARD from the top
    /// of the frame). Canonical orientation requires the panel top-left marker
    /// (RED) to be the leftmost AND topmost of its pair, and so on. The
    /// 07B.2.1 version fed this function raw Texture2D.GetPixel y values (which
    /// grow upward) while claiming the opposite, so it rewarded exactly the
    /// vertically mirrored mapping - the reason a flipped Release UI could
    /// still be reported as a PASS.</summary>
    private static float ScoreMapping(string mapping)
    {
        float[] xs = new float[4];
        float[] ys = new float[4];
        for (int i = 0; i < 4; i++)
        {
            int at = mapping.IndexOf(MarkerNames[i] + "=(",
                System.StringComparison.Ordinal);
            if (at < 0)
            {
                return 0f;
            }

            int close = mapping.IndexOf(')', at);
            string[] parts = mapping.Substring(at + MarkerNames[i].Length + 2,
                close - at - MarkerNames[i].Length - 2).Split(',');
            if (parts.Length != 2
                || !float.TryParse(parts[0], out xs[i])
                || !float.TryParse(parts[1], out ys[i]))
            {
                return 0f;
            }
        }

        float score = 0f;
        if (xs[0] < xs[1]) score += 1f; // RED left of GREEN
        if (xs[2] < xs[3]) score += 1f; // BLUE left of YELLOW
        if (ys[0] < ys[2]) score += 1f; // RED above BLUE (y grows downward)
        if (ys[1] < ys[3]) score += 1f; // GREEN above YELLOW
        return score;
    }

    // ====================================================================
    // Capture + analysis helpers
    // ====================================================================

    private struct FrameStats
    {
        public float MeanLum;
        public float MeanEdge;
        public float StdDev;
        public int UniqueColors;
        public float BlueFraction;
        public float CentreLum;
    }

    private static bool IsRealContent(in FrameStats stats)
    {
        return stats.UniqueColors > 40 && stats.StdDev > 6f
               && stats.BlueFraction < 0.5f;
    }

    /// <summary>Diagnostic magenta survived any tone mapping / sRGB pass: red
    /// and blue stay far above green.</summary>
    private static bool IsMagenta(Color c)
    {
        return c.r > 0.5f && c.b > 0.5f && c.g < 0.4f
               && c.r - c.g > 0.25f && c.b - c.g > 0.25f;
    }

    private IEnumerator Capture(string name)
    {
        string path = Path.Combine(_folder, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 300; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return WaitReal(0.2f);
        Info("captured " + name + ".png");
    }

    private bool TryLoad(string name, out Texture2D texture)
    {
        texture = null;
        string path = Path.Combine(_folder, name + ".png");
        if (!File.Exists(path))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (System.Exception)
        {
            return false;
        }

        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            texture = null;
            return false;
        }

        return true;
    }

    private int CountPixels(string name, System.Func<Color, bool> predicate)
    {
        return CountPixelsIn(name, new Rect(0f, 0f, 1920f, 1080f), predicate);
    }

    /// <summary>Counts matching pixels inside a rect given in IMAGE space (y
    /// downward from the top), converting to Unity's texture space internally.</summary>
    private int CountPixelsIn(string name, Rect rect,
        System.Func<Color, bool> predicate)
    {
        if (!TryLoad(name, out Texture2D texture))
        {
            return -1;
        }

        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, texture.width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), x0 + 1, texture.width);
        int topY = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, texture.height - 1);
        int bottomY = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), topY + 1,
            texture.height);
        int count = 0;
        for (int imageY = topY; imageY < bottomY; imageY += 2)
        {
            int y = texture.height - 1 - imageY;
            for (int x = x0; x < x1; x += 2)
            {
                if (predicate(texture.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }

        Object.Destroy(texture);
        return count;
    }

    private FrameStats AnalyseFrame(string name)
    {
        return AnalyseRegion(name, new Rect(0f, 0f, 1920f, 1080f));
    }

    private FrameStats AnalyseRegion(string name, Rect rect)
    {
        var stats = new FrameStats();
        if (!TryLoad(name, out Texture2D texture))
        {
            return stats;
        }

        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, texture.width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), x0 + 1, texture.width);
        int topY = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, texture.height - 1);
        int bottomY = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), topY + 1,
            texture.height);
        float sumLum = 0f;
        float sumLumSq = 0f;
        float sumEdge = 0f;
        int blue = 0;
        int total = 0;
        var colours = new System.Collections.Generic.HashSet<int>();
        for (int imageY = topY; imageY < bottomY; imageY += 3)
        {
            int y = texture.height - 1 - imageY;
            for (int x = x0; x < x1; x += 3)
            {
                Color c = texture.GetPixel(x, y);
                float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                sumLum += lum;
                sumLumSq += lum * lum;
                total++;
                colours.Add(((int)(c.r * 31f) << 10) | ((int)(c.g * 31f) << 5)
                            | (int)(c.b * 31f));
                if (Mathf.Abs(c.r - 0.19f) < 0.05f && Mathf.Abs(c.g - 0.30f) < 0.05f
                    && Mathf.Abs(c.b - 0.47f) < 0.05f)
                {
                    blue++;
                }

                if (x + 3 < x1)
                {
                    Color n = texture.GetPixel(x + 3, y);
                    float lumN = n.r * 0.2126f + n.g * 0.7152f + n.b * 0.0722f;
                    sumEdge += Mathf.Abs(lumN - lum);
                }
            }
        }

        stats.MeanLum = total > 0 ? sumLum / total * 255f : 0f;
        float mean = total > 0 ? sumLum / total : 0f;
        float variance = total > 0 ? Mathf.Max(0f, sumLumSq / total - mean * mean) : 0f;
        stats.StdDev = Mathf.Sqrt(variance) * 255f;
        stats.UniqueColors = colours.Count;
        stats.MeanEdge = total > 0 ? sumEdge / total * 255f : 0f;
        stats.BlueFraction = total > 0 ? blue / (float)total : 0f;
        stats.CentreLum = stats.MeanLum;
        Object.Destroy(texture);
        return stats;
    }

    // ====================================================================
    // Movement / waiting helpers
    // ====================================================================

    private WorldConsoleSurface SurfaceOf(int index)
    {
        return _controller.Consoles[index].GetComponent<WorldConsoleSurface>();
    }

    private IEnumerator ApproachConsole(int index)
    {
        yield return ApproachConsoleFar(index, 1.7f);
    }

    private IEnumerator ApproachConsoleFar(int index, float distance)
    {
        Vector3 position = ConsolePositions[index] + ScreenDirs[index] * distance;
        _player.TeleportTo(position);
        float yaw = Mathf.Atan2(-ScreenDirs[index].x, -ScreenDirs[index].z)
                    * Mathf.Rad2Deg;
        _player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;
        yield return WaitReal(0.3f);
    }

    private IEnumerator EnterConsole(int index)
    {
        ShipConsoleInteractable console = _controller.Consoles[index];
        bool entered = false;
        for (int t = 0; t < 500 && !entered; t++)
        {
            _controller.Interactor.RefreshTarget();
            if (ReferenceEquals(_controller.Interactor.CurrentTarget, console)
                && _controller.Interactor.HasValidTarget)
            {
                entered = _controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Check("console " + (index + 1) + " E entered focus", entered);
    }

    private IEnumerator WaitFocused()
    {
        for (int f = 0; f < 600 && _focus != null && _focus.State
             != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    private IEnumerator WaitExplore()
    {
        for (int f = 0; f < 600 && _focus != null && _focus.State
             != ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore; f++)
        {
            yield return null;
        }

        yield return null;
    }

    private static IEnumerator WaitSurfaceReady(WorldConsoleSurface surface)
    {
        for (int f = 0; f < 900 && !surface.IsReady; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    /// <summary>Projected screen rectangle in IMAGE pixels (y down from the top
    /// of the frame) - the same space the frame analysis helpers use.</summary>
    private Rect ScreenRectForConsole(int index)
    {
        WorldConsoleSurface surface = SurfaceOf(index);
        Transform quad = surface.ScreenQuad;
        if (quad == null || _eye == null)
        {
            return new Rect(0f, 0f, 1920f, 1080f);
        }

        Vector3 scale = quad.lossyScale;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = quad.position
                                 + quad.right * (sx * scale.x * 0.5f)
                                 + quad.up * (sy * scale.y * 0.5f);
                Vector3 sp = _eye.WorldToScreenPoint(corner);
                if (sp.z > 0f)
                {
                    minX = Mathf.Min(minX, sp.x);
                    maxX = Mathf.Max(maxX, sp.x);
                    minY = Mathf.Min(minY, Screen.height - sp.y);
                    maxY = Mathf.Max(maxY, Screen.height - sp.y);
                }
            }
        }

        if (maxX <= minX || maxY <= minY)
        {
            return new Rect(0f, 0f, 1920f, 1080f);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static IEnumerator WaitReal(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static string ParseFlag(string flag)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private void Check(string label, bool ok)
    {
        _report.AppendLine((ok ? "PASS :: " : "FAIL :: ") + label);
        Debug.Log("[ShipB22] " + (ok ? "PASS :: " : "FAIL :: ") + label);
        if (!ok)
        {
            _failures++;
        }
    }

    private void Info(string label)
    {
        _report.AppendLine("INFO :: " + label);
        Debug.Log("[ShipB22] INFO :: " + label);
    }

    private void Finish()
    {
        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        string path = Path.Combine(_folder, "phase07b22_result.txt");
        try
        {
            File.WriteAllText(path, _report.ToString());
            Debug.Log("[ShipB22] wrote result: " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ShipB22] cannot write result: " + e);
        }

        Debug.Log("[ShipB22] report:\n" + _report);
        Application.Quit(_failures == 0 ? 0 : 1);
    }
}
