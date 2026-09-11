using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Authoritative coordinator of the ARK-07 walkable interior (Phase 07B).
///
/// Phase 07B responsibilities on top of the 07A shell:
/// - deterministic spawn application (ShipSpawn_Default / FromSurface),
/// - explicit input modes (ShipExplore / console focus transitions / wrist)
///   through one small state authority - ShipConsoleFocusController owns the
///   console camera/blur/cursor machine, this controller owns exploration,
///   the wrist terminal and HUD suppression,
/// - the M / I wrist toggle with WristReturnMode.ShipExplore return context
///   (the wrist is NEVER allowed to stack on a focused physical console),
/// - the registries of the four physical consoles, the doors, the airlock and
///   the first-person stack reused from Phase 06.
/// </summary>
public class ShipGameController : MonoBehaviour, IWristContext
{
    // Compartment zones match the deterministic ShipInteriorBuilder layout.
    // Chinese primary + restrained English technical metadata.
    private static readonly (string Zh, string Meta, float X0, float Z0, float X1, float Z1)[] Zones =
    {
        ("舰桥", "BRIDGE // OPS", -3.4f, 6.3f, 3.4f, 10.6f),
        ("仓库", "WAREHOUSE // STORAGE", -5.5f, 0.3f, -1.1f, 6.3f),
        ("中央通道", "SPINE", -1.1f, 0.3f, 1.1f, 6.3f),
        ("资源加工", "RESOURCE PROCESSING", 1.1f, 0.3f, 5.5f, 6.3f),
        ("据点", "OUTPOST // COMMS", -5.5f, -9.4f, 5.5f, 0.3f),
        ("气闸舱", "AIRLOCK // EVA", -3.0f, -14.4f, 3.0f, -9.4f)
    };

    private static readonly SurfaceResourceNode[] EmptyNodes = new SurfaceResourceNode[0];

    private SurfacePlayerController _player;
    private FirstPersonInteractor _interactor;
    private ShipHudController _hud;
    private ShipAirlockInteractable _airlock;
    private ShipConsoleFocusController _focus;
    private ShipOverlayUi _overlayUi;
    private SurfaceWristController _wrist;
    private WorldUiPointerRouter _pointerRouter;
    private ShipEntryBlinkFade _blinkFade;
    private ShipPauseMenu _pauseMenu;
    private FirstPersonArmsController _arms;
    private WristPresentationBridge _wristBridge;
    private bool _wristEnabledBeforePause = true;
    private bool _paused;
    private bool _entryInputLocked;
    private readonly List<ShipConsoleInteractable> _consoles = new List<ShipConsoleInteractable>();
    private readonly List<ShipDoorInteractable> _doors = new List<ShipDoorInteractable>();
    private bool _ready;
    private bool _isWristOpen;
    private bool _wristTerminalRevealed;
    private bool _ownershipApplied;
    private ShipPointerOwner _appliedOwner = ShipPointerOwner.None;

    public bool IsReady => _ready;
    public SurfacePlayerController Player => _player;
    public FirstPersonInteractor Interactor => _interactor;
    public ShipHudController Hud => _hud;
    public ShipAirlockInteractable Airlock => _airlock;
    public ShipConsoleFocusController Focus => _focus;
    public SurfaceWristController Wrist => _wrist;
    public IReadOnlyList<ShipConsoleInteractable> Consoles => _consoles;
    public IReadOnlyList<ShipDoorInteractable> Doors => _doors;

    /// <summary>Wrist mode currently open inside the ship.</summary>
    public bool IsWristOpen => _isWristOpen;

    /// <summary>
    /// First-person arms presentation instance (Phase 09A_2). Null when the
    /// runtime asset is unavailable - every wrist behaviour stays functional in
    /// that case, only the arm motion is missing.
    /// </summary>
    public FirstPersonArmsController Arms => _arms;

    /// <summary>Wrist presentation bridge (null whenever the arms are).</summary>
    public WristPresentationBridge WristPresentation => _wristBridge;

    /// <summary>
    /// True once the EXISTING wrist terminal is actually presented on screen.
    /// Opening the wrist sets <see cref="IsWristOpen"/> immediately (authority),
    /// while this flag follows the real presentation: with the arms available it
    /// only turns true when the left wrist has risen into view (contract §29).
    /// </summary>
    public bool IsWristTerminalRevealed => _wristTerminalRevealed;

    /// <summary>First-person entry blink, while one is playing (PASS 4).</summary>
    public ShipEntryBlinkFade EntryBlink => _blinkFade;

    /// <summary>
    /// True when THIS entry played the first-person eyelid animation. It stays
    /// true after the animation finished (the component removes itself), so the
    /// hand-over is verifiable without racing a sub-second presentation.
    /// </summary>
    public bool EntryBlinkArmed { get; private set; }

    /// <summary>True while the entry eyelids still hold movement/look locked.</summary>
    public bool IsEntryInputLocked => _entryInputLocked;

    /// <summary>
    /// Releases the entry lock once the eyelids are gone: movement, look and
    /// interaction are enabled and the HUD chrome comes back.
    /// </summary>
    private void UpdateEntryEyelids()
    {
        if (!_entryInputLocked)
        {
            return;
        }

        // The eyelid component destroys itself when the last beat completes, so
        // a null reference IS the finished signal (Unity's destroyed-object null).
        if (_blinkFade != null)
        {
            return;
        }

        _entryInputLocked = false;
        _hud?.SetUiSuppressed(false);
        ApplyPointerOwnership();
        Debug.Log("[ShipGameController] entry eyelids finished - first-person control released");
    }

    /// <summary>True while a console transition or focus is active (the wrist
    /// must never open on top of a physical terminal).</summary>
    public bool IsConsoleFocusActive => _focus != null && _focus.FocusActive;

    /// <summary>True while a shipboard overlay (deployment confirmation /
    /// loadout) is open - movement, world E and the wrist stay gated.</summary>
    public bool IsOverlayOpen => _overlayUi != null && _overlayUi.IsOpen;

    /// <summary>
    /// The single shipboard overlay authority (deployment confirmation + the
    /// shared loadout page reached from the airlock locker). Exposed so the
    /// pause and the tests can address the SAME panel state - never a second
    /// panel, never a second input path.
    /// </summary>
    public ShipOverlayUi Overlay => _overlayUi;

    /// <summary>True while ANY modal interface is open (console focus, wrist
    /// or overlay) - used by HUD suppression and input gating.</summary>
    public bool IsAnyModalActive => IsConsoleFocusActive || _isWristOpen
                                    || IsOverlayOpen;

    /// <summary>
    /// True while the pause menu owns the input context (Phase 08 pause work).
    /// Movement, look, world interaction, the wrist keys and the world-console
    /// pointer are all frozen; Time.timeScale is 0.
    /// </summary>
    public bool IsPaused => _paused;

    /// <summary>The pause menu (created at runtime, never null once ready).</summary>
    public ShipPauseMenu PauseMenu => _pauseMenu;

    /// <summary>
    /// EXACTLY ONE pointer owner per ship mode (Phase 07B.2.3). Two owners must
    /// never process the mouse at the same time:
    ///   None          ShipExplore         - mouse look, world E, cursor locked
    ///   ConsoleScreen ShipConsoleFocus    - WorldUiPointerRouter drives the
    ///                                       physical console RenderTexture panel
    ///   ScreenModal   DeploymentConfirm / LoadoutFocus - the native UI Toolkit
    ///                                       screen-space pointer path only
    ///   Wrist         ship wrist          - the wrist raycast pointer only
    ///   PauseMenu     pause open          - the pause document only; every other
    ///                                       mode is frozen until it closes
    /// </summary>
    public enum ShipPointerOwner
    {
        None,
        ConsoleScreen,
        ScreenModal,
        Wrist,
        PauseMenu
    }

    /// <summary>Current pointer owner (set by ApplyPointerOwnership).</summary>
    public ShipPointerOwner PointerOwner { get; private set; }

    /// <summary>The shared world-space console pointer router. Enabled ONLY for
    /// ShipPointerOwner.ConsoleScreen: it must never run while a screen-space
    /// modal or the wrist owns the pointer.</summary>
    public WorldUiPointerRouter PointerRouter => _pointerRouter;

    // ---- IWristContext (Phase 07B) ----
    // Ship contexts expose no live surface nodes/navigation: the wrist shows
    // ship semantics (舰内 / 地表目标已记录 memory) instead.

    public WristReturnMode WristSourceMode => WristReturnMode.ShipExplore;

    public IReadOnlyList<SurfaceResourceNode> Nodes => EmptyNodes;

    public SurfaceResourceNode ActiveNode => null;

    public bool IsNavigationActive => false;

    public int NavigationTargetNodeId => -1;

    public bool NavigationReached => false;

    public float NavigationDistance => 0f;

    public SurfaceResourceNode GetNodeById(int nodeId)
    {
        return null;
    }

    public bool TrySelectNavigationTarget(int nodeId)
    {
        return false;
    }

    // Custom-accessor events (interface contract; the wrist subscribes).
    private Action<bool> _wristOpenChanged;
    private Action _navigationChanged;

    public event Action<bool> WristOpenChanged
    {
        add => _wristOpenChanged += value;
        remove => _wristOpenChanged -= value;
    }

    public event Action NavigationChanged
    {
        add => _navigationChanged += value;
        remove => _navigationChanged -= value;
    }

    private void Start()
    {
        _player = UnityEngine.Object.FindAnyObjectByType<SurfacePlayerController>();
        _interactor = UnityEngine.Object.FindAnyObjectByType<FirstPersonInteractor>();
        _hud = UnityEngine.Object.FindAnyObjectByType<ShipHudController>();
        _focus = UnityEngine.Object.FindAnyObjectByType<ShipConsoleFocusController>();
        _wrist = UnityEngine.Object.FindAnyObjectByType<SurfaceWristController>();
        _pointerRouter = UnityEngine.Object.FindAnyObjectByType<WorldUiPointerRouter>();
        _overlayUi = UnityEngine.Object.FindAnyObjectByType<ShipOverlayUi>(
            FindObjectsInactive.Include);

        _consoles.Clear();
        foreach (ShipConsoleInteractable console in UnityEngine.Object.FindObjectsByType<ShipConsoleInteractable>())
        {
            _consoles.Add(console);
        }

        _consoles.Sort((a, b) => a.ConsoleId.CompareTo(b.ConsoleId));

        _doors.Clear();
        foreach (ShipDoorInteractable door in UnityEngine.Object.FindObjectsByType<ShipDoorInteractable>())
        {
            _doors.Add(door);
        }

        _doors.Sort((a, b) => a.DoorId.CompareTo(b.DoorId));

        _airlock = UnityEngine.Object.FindAnyObjectByType<ShipAirlockInteractable>();

        // Phase 07B.2.2 screen composition: enforce the world-screen / focus
        // proxy split at runtime. The scene authoring adds the focus overlay
        // camera to the base camera's URP stack, but that relationship is not
        // guaranteed to survive every scene serialization/load path;
        // re-asserting it here is idempotent. The focus overlay camera stays
        // DISABLED: only ShipConsoleFocusController enables it while a console
        // is focused.
        EnsureScreenComposition();
        VerifyBaseCameraVisibility(out string visibilityReport);
        Debug.Log("[ShipGameController] base camera visibility: " + visibilityReport);

        // Consume the routing reason and place the player on the matching marker.
        // FromMainMenu shares the Default marker: that marker already faces the
        // bridge terminals, so the hand-over from the cover drops the player in
        // front of the 总览 terminal instead of in a special-case pose.
        SceneRouting.ShipEntry entry = SceneRouting.ConsumeShipEntry();
        string markerId = entry == SceneRouting.ShipEntry.FromSurface
            ? ShipSpawnMarker.FromSurfaceId
            : ShipSpawnMarker.DefaultId;
        ApplySpawn(markerId);

        // Phase 08 Revision 2 (PASS 4): the title screen hands the player over
        // as "waking up inside the ship", so the interior opens through the
        // two-panel eyelid animation instead of a hard cut. The requested mode
        // is consumed exactly once and only the title screen sets it; every
        // other entry (-bootShip, airlock, landing) stays an ordinary cut.
        SceneRouting.EntryEyelid eyelid = SceneRouting.ConsumeEntryEyelid();
        _blinkFade = gameObject.GetComponent<ShipEntryBlinkFade>();
        if (_blinkFade == null
            && (eyelid != SceneRouting.EntryEyelid.None
                || ShipEntryBlinkFade.AutomationRequested))
        {
            _blinkFade = gameObject.AddComponent<ShipEntryBlinkFade>();
            _blinkFade.Configure(eyelid != SceneRouting.EntryEyelid.None
                ? eyelid
                : SceneRouting.EntryEyelid.Full);
            EntryBlinkArmed = true;
            // Movement, look and interaction stay locked until the eyes are
            // fully open, so the player cannot turn away before seeing anything.
            _entryInputLocked = true;
            _hud?.SetUiSuppressed(true);
            Debug.Log("[ShipGameController] entry eyelids armed (" + eyelid + ")");
        }

        if (_hud != null)
        {
            _hud.Bind(this);
        }

        // Phase 08 pause work: the pause menu is created here rather than
        // authored into the frozen Phase 07 ship scene (same pattern as the entry
        // eyelids). It reuses the HUD's panel settings and draws above it.
        EnsurePauseMenu();

        // The ship wrist is the SAME wrist document/render-texture/radial/
        // cursor chain Phase 06 validated; only its content mode is ship.
        if (_wrist != null)
        {
            _wrist.Bind(this);
        }

        // Phase 09A_2: the Codex first-person arms are created at runtime under
        // the authoritative eye camera, exactly like the entry eyelids and the
        // pause menu - the validated Phase 07 ship scene is never regenerated,
        // and destroying the scene destroys the arms with it.
        EnsureFirstPersonArms();

        ApplyShipExploreMode();
        _ready = true;
        Debug.Log("[ShipGameController] ShipExplore ready entry=" + entry
                  + " spawn=" + markerId + " scene=" + gameObject.scene.name
                  + " focus=" + (_focus != null) + " wrist=" + (_wrist != null));
    }

    private void EnsureScreenComposition()
    {
        if (_player == null)
        {
            return;
        }

        Camera eye = _player.EyeCamera;
        if (eye == null)
        {
            return;
        }

        int layer = WorldConsoleSurface.FocusOverlayLayer;
        Camera sharp = null;
        Transform sharpTransform = eye.transform.Find("SharpScreenCamera");
        if (sharpTransform != null)
        {
            sharp = sharpTransform.GetComponent<Camera>();
        }

        if (sharp == null)
        {
            Debug.LogError("[ShipGameController] SharpScreenCamera missing - "
                           + "the focused console screen cannot stay sharp");
            return;
        }

        // Environment base camera: post-processing ON, focus-overlay layer OUT.
        var eyeData = eye.GetComponent<UniversalAdditionalCameraData>();
        var sharpData = sharp.GetComponent<UniversalAdditionalCameraData>();
        if (eyeData == null)
        {
            eyeData = eye.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }

        if (sharpData == null)
        {
            sharpData = sharp.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }

        // Environment BASE camera: post-processing ON, renders the whole
        // visible world INCLUDING the four real physical console screens
        // (ordinary world geometry on Default) and excludes ONLY the focus-only
        // proxy layer. The mask is built explicitly so a stale mask can never
        // exclude the environment (Phase 07B.2.1 render-recovery hard gate).
        eye.cullingMask = ~(1 << layer);
        eyeData.renderPostProcessing = true;
        eyeData.volumeLayerMask = ~0;
        eyeData.renderType = CameraRenderType.Base;
        eye.depth = 0f;

        // Focus-only proxy camera: URP OVERLAY camera rendering ONLY the
        // ConsoleFocusOverlay layer, post-processing OFF, stacked on the base
        // camera EXACTLY ONCE, and DISABLED unless a console is focused.
        // It must never be a second Base camera: a second Base camera clears
        // the frame with its own background colour and the whole ship
        // environment (and the wrist) disappears.
        sharp.cullingMask = 1 << layer;
        sharp.depth = eye.depth + 1f;
        sharp.clearFlags = CameraClearFlags.Nothing;
        sharpData.renderType = CameraRenderType.Overlay;
        sharpData.renderPostProcessing = false;
        sharp.enabled = _focus != null && _focus.FocusActive;

        // clearDepth is authored by ShipInteriorBuilder through SerializedObject
        // (URP exposes it read-only at runtime). If it is ever true again the
        // overlay would paint over the world instead of honoring base depth.
        if (sharpData.clearDepth)
        {
            Debug.LogError("[ShipGameController] focus overlay camera clears "
                           + "depth: it would float above walls and the wrist. "
                           + "Rebuild 03_ShipInterior with ShipInteriorBuilder.");
        }

        List<Camera> stack = eyeData.cameraStack;
        if (stack == null)
        {
            Debug.LogError("[ShipGameController] base camera has no URP camera "
                           + "stack - the focus proxy cannot composite");
        }
        else
        {
            stack.Clear();
            stack.Add(sharp);
        }

        Debug.Log("[ShipGameController] screen composition enforced: baseType="
                  + eyeData.renderType + " baseMask=" + eye.cullingMask
                  + " overlayType=" + sharpData.renderType
                  + " overlayMask=" + sharp.cullingMask
                  + " overlayEnabled=" + sharp.enabled
                  + " overlayClearDepth=" + sharpData.clearDepth
                  + " stack=" + (stack != null ? stack.Count : -1));
    }

    /// <summary>
    /// Phase 07B.2.2 screen-architecture verification. The four REAL physical
    /// console screens must be ordinary world geometry rendered by the base
    /// camera with normal depth, the focus-only proxy layer must hold exactly
    /// one object (the ConsoleFocusProxy quad) and must be excluded from the
    /// base camera, and the focus overlay camera must be an overlay that honors
    /// the base depth (clearDepth false) and is enabled only while focused.
    /// </summary>
    public bool VerifyBaseCameraVisibility(out string report)
    {
        var sb = new System.Text.StringBuilder();
        bool ok = true;

        Camera eye = _player != null ? _player.EyeCamera : null;
        if (eye == null)
        {
            report = "base camera missing";
            return false;
        }

        int overlayLayer = WorldConsoleSurface.FocusOverlayLayer;
        int worldLayer = WorldConsoleSurface.PhysicalScreenLayer;
        var eyeData = eye.GetComponent<UniversalAdditionalCameraData>();
        var sharpTransform = eye.transform.Find("SharpScreenCamera");
        Camera sharp = sharpTransform != null
            ? sharpTransform.GetComponent<Camera>()
            : null;
        var sharpData = sharp != null
            ? sharp.GetComponent<UniversalAdditionalCameraData>()
            : null;

        sb.Append("base=").Append(eye.name)
          .Append(" renderType=").Append(eyeData != null ? eyeData.renderType.ToString() : "-")
          .Append(" mask=").Append(eye.cullingMask)
          .Append(" clear=").Append(eye.clearFlags)
          .Append(" | overlay=").Append(sharp != null ? sharp.name : "MISSING")
          .Append(" renderType=").Append(sharpData != null ? sharpData.renderType.ToString() : "-")
          .Append(" mask=").Append(sharp != null ? sharp.cullingMask : 0)
          .Append(" pp=").Append(sharpData != null && sharpData.renderPostProcessing)
          .Append(" enabled=").Append(sharp != null && sharp.enabled)
          .Append(" clearDepth=").Append(sharpData != null && sharpData.clearDepth)
          .Append(" | stack=").Append(eyeData != null && eyeData.cameraStack != null
              ? eyeData.cameraStack.Count.ToString() : "-");

        if ((eye.cullingMask & (1 << overlayLayer)) != 0)
        {
            sb.Append(" ERROR: base camera renders the focus overlay layer");
            ok = false;
        }

        if ((eye.cullingMask & (1 << worldLayer)) == 0)
        {
            sb.Append(" ERROR: base camera does not render world layer "
                      + worldLayer + " (physical screens would vanish)");
            ok = false;
        }

        if (sharp == null || sharpData == null
            || sharpData.renderType != CameraRenderType.Overlay
            || sharp.cullingMask != (1 << overlayLayer)
            || sharpData.renderPostProcessing)
        {
            sb.Append(" ERROR: focus overlay camera misconfigured");
            ok = false;
        }

        if (sharpData != null && sharpData.clearDepth)
        {
            sb.Append(" ERROR: overlay camera clears depth (screen would float "
                      + "above walls and the wrist)");
            ok = false;
        }

        bool focusActive = _focus != null && _focus.FocusActive;
        if (sharpData != null && sharp.enabled != focusActive)
        {
            sb.Append(" ERROR: overlay camera must be enabled only during "
                      + "console focus (focusActive=").Append(focusActive)
              .Append(" enabled=").Append(sharp.enabled).Append(")");
            ok = false;
        }

        if (eyeData == null || eyeData.cameraStack == null
            || eyeData.cameraStack.Count != 1
            || eyeData.cameraStack[0] != sharp)
        {
            sb.Append(" ERROR: overlay camera not stacked exactly once");
            ok = false;
        }

        // Key visible environment objects must be rendered by the base camera.
        var required = new System.Collections.Generic.List<Renderer>();
        AddFirstRenderer(required, "ShipStructure");
        AddFirstRenderer(required, "ShipDoors");
        AddFirstRenderer(required, "ShipConsoles");
        AddFirstRenderer(required, "Airlock_DeployStation");
        AddFirstRenderer(required, "EquipmentLocker");
        AddFirstRenderer(required, "ShipWristUI");
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include))
        {
            string n = renderer.gameObject.name;
            if (n == "Deck_Floor" || n == "Deck_Ceiling" || n == "WristDisplay"
                || n == "Screen")
            {
                required.Add(renderer);
            }
        }

        int visible = 0;
        int excluded = 0;
        foreach (Renderer renderer in required)
        {
            if (renderer == null)
            {
                continue;
            }

            int objLayer = renderer.gameObject.layer;
            bool inBase = (eye.cullingMask & (1 << objLayer)) != 0;
            if (inBase)
            {
                visible++;
            }
            else
            {
                excluded++;
                sb.Append(" ERROR: env object excluded from base: ")
                  .Append(renderer.gameObject.name)
                  .Append("(layer ").Append(objLayer).Append(")");
                ok = false;
            }
        }

        sb.Append(" | envVisible=").Append(visible)
          .Append(" envExcluded=").Append(excluded);

        // Physical screens: exactly four, all on the ordinary world layer and
        // never on the focus overlay layer.
        var screens = new System.Collections.Generic.List<Renderer>();
        var onOverlayLayer = new System.Collections.Generic.List<string>();
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include))
        {
            string n = renderer.gameObject.name;
            int objLayer = renderer.gameObject.layer;
            if (n == "Screen" && renderer.transform.parent != null
                && renderer.transform.parent.name.StartsWith("ShipConsole_"))
            {
                screens.Add(renderer);
                if (objLayer != worldLayer)
                {
                    sb.Append(" ERROR: physical screen not on the world layer: ")
                      .Append(renderer.transform.parent.name)
                      .Append("(layer ").Append(objLayer).Append(")");
                    ok = false;
                }
            }

            if (objLayer == overlayLayer)
            {
                onOverlayLayer.Add(n);
            }
        }

        sb.Append(" | physicalScreens=").Append(screens.Count);
        if (screens.Count != 4)
        {
            sb.Append(" ERROR: expected exactly four physical console screens");
            ok = false;
        }

        sb.Append(" | focusOverlayLayerObjects=").Append(onOverlayLayer.Count)
          .Append("[");
        for (int i = 0; i < onOverlayLayer.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }

            sb.Append(onOverlayLayer[i]);
        }

        sb.Append("]");
        if (onOverlayLayer.Count != 1 || onOverlayLayer[0] != "FocusScreenProxy")
        {
            sb.Append(" ERROR: the focus overlay layer must hold exactly the one "
                      + "FocusScreenProxy quad");
            ok = false;
        }

        report = sb.ToString();
        return ok;
    }

    private static void AddFirstRenderer(System.Collections.Generic.List<Renderer> list,
        string rootName)
    {
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include))
        {
            if (renderer.gameObject.name == rootName
                || renderer.transform.parent != null
                && renderer.transform.parent.name == rootName)
            {
                list.Add(renderer);
                return;
            }
        }
    }

    private void Update()
    {
        if (!_ready)
        {
            return;
        }

        PollWristKeys();
        UpdateEntryEyelids();

        // Arms presentation follows the authoritative modes owned above (entry
        // eyelids, console focus, device panels, pause). Tick order is fixed
        // here so no script-order race can exist.
        if (_wristBridge != null)
        {
            _wristBridge.Tick(_paused);
        }

        // Pointer ownership is re-asserted every frame: it is cheap, and it
        // guarantees no mode can leave two owners enabled (Phase 07B.2.3).
        ApplyPointerOwnership();

        if (_entryInputLocked)
        {
            // Still behind closed eyelids: no HUD chrome and no interaction.
            if (_hud != null)
            {
                _hud.SetUiSuppressed(true);
            }

            return;
        }

        if (IsAnyModalActive)
        {
            // A focused console, an overlay or the wrist is the only
            // interface; the HUD chrome stays suppressed (SetUiSuppressed
            // handles hiding).
            if (_hud != null)
            {
                _hud.SetUiSuppressed(true);
            }

            // The console VIEW still needs its single "[B] 返回" hint, which is
            // refreshed every frame (the suppressed state only hides the rest of
            // the chrome).
            RefreshHud();
        }
        else
        {
            if (_hud != null)
            {
                _hud.SetUiSuppressed(false);
            }

            RefreshHud();
        }

        if (_wrist != null)
        {
            _wrist.Tick();
        }
    }

    private void PollWristKeys()
    {
        // The pause menu owns the input context: no wrist key may fire while it
        // is open (one press, one action).
        if (_paused)
        {
            return;
        }

        // The physical binding (Xbox Y / PlayStation triangle, or M / I) and the
        // dispatch rule both live in UiInput; this controller only consumes the
        // semantic action.
        if (_isWristOpen)
        {
            // Escape is the GLOBAL pause key and must not double as "close the
            // wrist": the Wrist action toggles it instead.
            if (UiInput.WristPressed())
            {
                SetWristOpen(false);
            }

            return;
        }

        // The Wrist action opens the terminal ONLY from plain ShipExplore. While
        // a console is focused, a transition runs or an overlay is open, it must
        // do nothing (console/wrist/overlay never stack).
        if (IsConsoleFocusActive || IsOverlayOpen)
        {
            return;
        }

        if (UiInput.WristPressed())
        {
            SetWristOpen(true);
        }
    }

    /// <summary>Wrist toggle used by tests and M/I input.</summary>
    public void ToggleWrist()
    {
        if (IsAnyModalActive && !_isWristOpen)
        {
            return;
        }

        SetWristOpen(!_isWristOpen);
    }

    public void SetWristOpen(bool open)
    {
        if (open == _isWristOpen)
        {
            return;
        }

        if (open && (IsConsoleFocusActive || IsOverlayOpen))
        {
            // Phase 07B: never stack Wrist on a focused physical console or
            // on a shipboard overlay.
            return;
        }

        _isWristOpen = open;

        if (_player != null)
        {
            _player.InputEnabled = !open;
        }

        if (_hud != null)
        {
            _hud.SetUiSuppressed(open);
        }

        if (open)
        {
            // Presentation decides WHEN the terminal becomes visible (the left
            // wrist has to rise first), never WHETHER: this state is already
            // authoritative. Without the arms the terminal opens immediately,
            // which is exactly the validated Phase 08 behaviour.
            bool deferred = _wristBridge != null && _wristBridge.BeginWristRaise();
            if (!deferred)
            {
                RevealWristTerminal(true);
            }
        }
        else
        {
            // Close order (contract §31): the existing terminal stops taking
            // input, releases focus and hides FIRST (that is what SetOpen(false)
            // has always done), then the arm comes back down.
            _wristBridge?.BeginWristLower();
            RevealWristTerminal(false);
        }

        ApplyCursorForMode();
        _wristOpenChanged?.Invoke(open);
    }

    /// <summary>
    /// Shows or hides the EXISTING wrist terminal (the unchanged Phase 06/07
    /// document + render texture + display chain). Called immediately on close,
    /// and on open either immediately (no arms) or by
    /// <see cref="WristPresentationBridge"/> once the raise animation has
    /// brought the wrist device into view.
    /// </summary>
    public void RevealWristTerminal(bool visible)
    {
        _wristTerminalRevealed = visible;

        if (_wrist != null)
        {
            _wrist.SetOpen(visible);
        }

        // The cursor/world-pointer ownership depends on the reveal state, so the
        // per-frame cache must be re-evaluated once.
        _ownershipApplied = false;
        ApplyPointerOwnership();
    }

    private void ApplyCursorForMode()
    {
        ApplyPointerOwnership();
    }

    /// <summary>
    /// Single pointer/input ownership arbitration for every ship mode. Called on
    /// every mode change (wrist toggle, overlay open/close, console focus entry
    /// and exit) and once per frame so the mouse can never be processed by two
    /// owners at once. Re-applying an unchanged owner is a no-op, so no cursor
    /// state is written needlessly every frame.
    /// </summary>
    public void ApplyPointerOwnership()
    {
        ShipPointerOwner owner = _paused
            ? ShipPointerOwner.PauseMenu
            : IsConsoleFocusActive
                ? ShipPointerOwner.ConsoleScreen
                : IsOverlayOpen
                    ? ShipPointerOwner.ScreenModal
                    : _isWristOpen
                        ? ShipPointerOwner.Wrist
                        : ShipPointerOwner.None;
        PointerOwner = owner;

        bool inputAllowed = owner == ShipPointerOwner.None && !_entryInputLocked;

        // Re-applying an unchanged owner is a no-op, but the cached state is only
        // valid while the player's input flag already matches it: the entry
        // eyelid lock changes that flag without changing the owner.
        if (_ownershipApplied && owner == _appliedOwner
            && (_player == null || _player.InputEnabled == inputAllowed))
        {
            return;
        }

        _appliedOwner = owner;
        _ownershipApplied = true;

        // Exactly one world-pointer owner, and only for a physical console.
        if (_pointerRouter != null)
        {
            _pointerRouter.enabled = owner == ShipPointerOwner.ConsoleScreen;
        }

        if (_player != null)
        {
            // Movement is only handed back once the entry eyelids are open.
            _player.InputEnabled = inputAllowed;
        }

        switch (owner)
        {
            case ShipPointerOwner.Wrist:
                // While the arms are rising the terminal is not on screen yet:
                // the pointer stays locked and hidden so the reveal is what
                // hands the player the wrist cursor, exactly like the closed
                // wrist never showed it.
                if (_wrist != null && _wristTerminalRevealed)
                {
                    _wrist.EnterWristCursor();
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                break;
            case ShipPointerOwner.ConsoleScreen:
            case ShipPointerOwner.ScreenModal:
            case ShipPointerOwner.PauseMenu:
                // Free, visible OS cursor for the console screen pointer
                // (screen-space projection), the native screen-space modals and
                // the pause document.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
            default:
                ApplyShipExploreMode();
                break;
        }

        if (_hud != null)
        {
            _hud.SetUiSuppressed(owner != ShipPointerOwner.None);
        }

        Debug.Log("[ShipGameController] pointer owner=" + owner
                  + " router=" + (_pointerRouter != null && _pointerRouter.enabled)
                  + " input=" + (_player != null && _player.InputEnabled)
                  + " cursor=" + Cursor.lockState + "/" + Cursor.visible);
    }

    /// <summary>Called by the focus controller / overlay whenever a modal
    /// state changes (entry, focus reached, exit started/finished) so pointer
    /// ownership, HUD and wrist gating stay consistent.</summary>
    public void HandleConsoleFocusChanged()
    {
        ApplyPointerOwnership();
    }

    /// <summary>
    /// Creates (once) and wires the pause menu. The document lives on a child
    /// object so the ship scene stays untouched; it shares the HUD's panel
    /// settings and renders above the HUD and the shipboard modals.
    /// </summary>
    private void EnsurePauseMenu()
    {
        if (_pauseMenu == null)
        {
            _pauseMenu = gameObject.GetComponentInChildren<ShipPauseMenu>(true);
        }

        if (_pauseMenu == null)
        {
            var go = new GameObject("ShipPauseMenu");
            go.transform.SetParent(transform, false);
            _pauseMenu = go.AddComponent<ShipPauseMenu>();
        }

        _pauseMenu.Configure(_hud != null ? _hud.panelSettings : null, this, _focus);
    }

    /// <summary>
    /// Creates (once) the first-person arms instance and its wrist presentation
    /// bridge. Presentation only: nothing here can open/close the wrist, move
    /// the player or take focus - the bridge merely observes this controller's
    /// authoritative state and animates the arms accordingly.
    /// </summary>
    private void EnsureFirstPersonArms()
    {
        if (_player == null || _player.EyeCamera == null)
        {
            Debug.LogError("[ShipGameController] first-person arms need the eye camera");
            return;
        }

        _arms = FirstPersonArmsController.Ensure(_player.EyeCamera);
        if (_arms == null)
        {
            return;
        }

        _arms.Configure(_player);
        // The interior opens behind the entry eyelids, so the arms start hidden
        // and the bridge shows them once first-person control is released.
        _arms.SetVisible(false);

        var bridgeGo = new GameObject("WristPresentationBridge");
        bridgeGo.transform.SetParent(transform, false);
        _wristBridge = bridgeGo.AddComponent<WristPresentationBridge>();
        _wristBridge.Configure(this, _arms, _wrist);

        Debug.Log("[ShipGameController] first-person arms ready renderers="
                  + _arms.RendererCount
                  + " wristBridge=" + (_wristBridge != null));
    }

    /// <summary>
    /// Freezes or releases the whole gameplay input context for the pause menu
    /// (Phase 08 pause work). While paused: no movement, no look, no world
    /// interaction, no wrist keys and no world-console pointer; the underlying
    /// mode (free first person or console view) is preserved untouched so
    /// 继续游戏 resumes exactly where the player was.
    /// </summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;

        if (_wrist == null)
        {
            _wrist = UnityEngine.Object.FindAnyObjectByType<SurfaceWristController>();
        }

        if (_wrist != null)
        {
            if (paused)
            {
                // The wrist keeps its state but stops reacting to input (it is
                // part of the frozen gameplay context).
                _wristEnabledBeforePause = _wrist.enabled;
                _wrist.enabled = false;
            }
            else
            {
                _wrist.enabled = _wristEnabledBeforePause;
            }
        }

        ApplyPointerOwnership();
        RefreshHud();
        Debug.Log("[ShipGameController] paused=" + paused
                  + " owner=" + PointerOwner
                  + " input=" + (_player != null && _player.InputEnabled));
    }

    private void RefreshHud()
    {
        if (_hud == null)
        {
            return;
        }

        if (_player != null)
        {
            (string zh, string meta) zone = ResolveZone(_player.transform.position);
            _hud.SetZoneTag(zone.zh, zone.meta);
        }

        if (_focus != null && _focus.FocusActive)
        {
            // Console view: the screen is the interface, so the gaze prompt is
            // replaced by the single "leave the view" hint the player needs.
            _hud.SetFocusPrompt(InputDeviceTracker.FormatPrompt(UiAction.Back, "返回"));
            _hud.SetCrosshairState(ShipHudController.ShipHudCrosshairState.None);
            return;
        }

        _hud.ClearFocusPrompt();

        if (_interactor != null)
        {
            string prompt = _interactor.ResolvePrompt() ?? "";
            _hud.SetPrompt(prompt);
            _hud.SetCrosshairState(_interactor.HasValidTarget
                ? ShipHudController.ShipHudCrosshairState.ValidTarget
                : (_interactor.HasBlockedTarget
                    ? ShipHudController.ShipHudCrosshairState.BlockedTarget
                    : ShipHudController.ShipHudCrosshairState.None));
        }
        else
        {
            _hud.SetPrompt("");
            _hud.SetCrosshairState(ShipHudController.ShipHudCrosshairState.None);
        }
    }

    /// <summary>ShipExplore mode: OS cursor locked + hidden (mouse look).</summary>
    public void ApplyShipExploreMode()
    {
        if (_wrist != null)
        {
            _wrist.ExitToSurfaceCursor();
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>Deterministic placement on the requested spawn marker.</summary>
    public bool ApplySpawn(string markerId)
    {
        if (_player == null)
        {
            _player = UnityEngine.Object.FindAnyObjectByType<SurfacePlayerController>();
        }

        if (_player == null)
        {
            return false;
        }

        foreach (ShipSpawnMarker marker in UnityEngine.Object.FindObjectsByType<ShipSpawnMarker>())
        {
            if (marker.MarkerId != markerId)
            {
                continue;
            }

            _player.TeleportTo(marker.transform.position);
            _player.SetFacing(marker.transform.eulerAngles.y, 0f);
            return true;
        }

        return false;
    }

    /// <summary>Chinese compartment name at a world position (empty = outside).</summary>
    public static string ResolveZoneZh(Vector3 position)
    {
        return ResolveZone(position).zh;
    }

    private static (string zh, string meta) ResolveZone(Vector3 position)
    {
        for (int i = 0; i < Zones.Length; i++)
        {
            (string zh, string meta, float x0, float z0, float x1, float z1) = Zones[i];
            if (position.x >= x0 && position.x <= x1 && position.z >= z0 && position.z <= z1)
            {
                return (zh, meta);
            }
        }

        return ("", "");
    }
}
