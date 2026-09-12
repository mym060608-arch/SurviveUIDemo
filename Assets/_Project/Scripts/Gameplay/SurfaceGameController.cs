using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Authoritative coordinator of the NEREID-04 first-person surface slice
/// (Phase 06 + Revision).
///
/// Owns: the node registry, wrist terminal mode (M / I / Esc), scene-owned
/// navigation target state, world input gating, HUD refresh and the legacy
/// semantic helpers. Resource settlement stays in GameSession; gaze
/// interaction happens through FirstPersonInteractor. Wrist map coordinates
/// live in the single authoritative WristMapCoordinates source.
/// </summary>
public class SurfaceGameController : MonoBehaviour, IWristContext, IArmsPresentationContext
{
    // ---- IArmsPresentationContext: the same block-arm presentation as the ship ----

    /// <summary>Player rig the arms hang from.</summary>
    SurfacePlayerController IArmsPresentationContext.Player => _player;

    /// <summary>The surface has no pause menu, so nothing freezes the arms here.</summary>
    bool IArmsPresentationContext.IsPaused => false;

    /// <summary>The surface has no console view or device panels to hide them for.</summary>
    bool IArmsPresentationContext.HidesFirstPersonPresentation => false;

    /// <summary>The planet's own wrist terminal stays the authoritative UI.</summary>
    void IArmsPresentationContext.SetWristTerminalVisible(bool visible)
    {
        if (_wrist != null)
        {
            _wrist.SetOpen(visible);
        }
    }

    /// <summary>Live block-arms presentation instance (tests / probes).</summary>
    public FirstPersonBlockArmsController Arms => _arms;

    /// <summary>Wrist presentation bridge driving raise / hold / lower.</summary>
    public WristPresentationBridge WristPresentation => _wristBridge;

    /// <summary>The planet's existing wrist terminal (the authoritative UI).</summary>
    public SurfaceWristController Wrist => _wrist;

    /// <summary>
    /// True once the surface terminal is actually presented. On open this becomes
    /// true when the raise brings the wrist into view (the same staged reveal the
    /// ship interior uses), never on the input that requested it.
    /// </summary>
    public bool IsWristTerminalRevealed =>
        _wrist != null && _wrist.Display != null && _wrist.Display.Visible;
    // Legacy proximity helper range (semantic tests/probes only).
    public float interactRangeFallback = 3.0f;

    private readonly List<SurfaceResourceNode> _nodes = new List<SurfaceResourceNode>();
    private SurfacePlayerController _player;
    private SurfaceHudController _hud;
    private SurfaceWristController _wrist;
    private FirstPersonBlockArmsController _arms;
    private WristPresentationBridge _wristBridge;
    private FirstPersonInteractor _interactor;
    private SurfaceResourceNode _nearest;
    private SurfaceResourceNode _activeNode;
    private float _resultUntil;
    private bool _ready;

    private bool _isWristOpen;
    private int _navigationTargetNodeId = -1;
    private float _navigationDistance;

    public bool IsReady => _ready;
    public IReadOnlyList<SurfaceResourceNode> Nodes => _nodes;
    public SurfaceResourceNode NearestNode => _nearest;
    public SurfaceResourceNode ActiveNode => _activeNode;
    public FirstPersonInteractor Interactor => _interactor;
    public SurfacePlayerController Player => _player;

    /// <summary>The surface HUD (owns the document the shared prompt bar lives in).</summary>
    public SurfaceHudController Hud => _hud;

    public bool IsWristOpen => _isWristOpen;

    /// <summary>Phase 07B: the surface is the wrist's original return source.</summary>
    public WristReturnMode WristSourceMode => WristReturnMode.SurfaceFirstPerson;

    /// <summary>Scene-owned navigation target (-1 = none). Not persistence
    /// state; it never lives in GameSession.</summary>
    public int NavigationTargetNodeId => _navigationTargetNodeId;

    public bool IsNavigationActive => _navigationTargetNodeId >= 0;

    /// <summary>True when the target node is within deploy range of the
    /// player (the exploration HUD can then say 目标已到达).</summary>
    public bool NavigationReached =>
        IsNavigationActive && _navigationDistance <= 1.6f;

    /// <summary>Real player-to-target distance, refreshed every frame.</summary>
    public float NavigationDistance => _navigationDistance;

    public event System.Action<bool> WristOpenChanged;
    public event System.Action NavigationChanged;

    private void Start()
    {
        _player = Object.FindAnyObjectByType<SurfacePlayerController>();
        _hud = Object.FindAnyObjectByType<SurfaceHudController>();
        _wrist = Object.FindAnyObjectByType<SurfaceWristController>();
        _interactor = Object.FindAnyObjectByType<FirstPersonInteractor>();

        _nodes.Clear();
        foreach (SurfaceResourceNode node in Object.FindObjectsByType<SurfaceResourceNode>())
        {
            _nodes.Add(node);
            node.OnStateChanged += HandleNodeState;
            node.OnSettled += HandleNodeSettled;
        }

        // Deterministic registry: FindObjectsByType order is not stable, so the
        // list is sorted by NodeId. Consumers rely on Nodes[0] == node id 0.
        _nodes.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));

        if (_hud != null)
        {
            _hud.Bind(this);
            _hud.RefreshCore();
        }

        if (_wrist != null)
        {
            _wrist.Bind(this);
        }

        ApplySurfaceFirstPersonMode();
        _ready = true;
        EnsureFirstPersonArms();
    }

    private void OnDestroy()
    {
        foreach (SurfaceResourceNode node in _nodes)
        {
            if (node == null)
            {
                continue;
            }

            node.OnStateChanged -= HandleNodeState;
            node.OnSettled -= HandleNodeSettled;
        }
    }

    private void Update()
    {
        if (!_ready)
        {
            return;
        }

        PollWristKeys();
        if (_wristBridge != null)
        {
            _wristBridge.Tick(false);
        }

        UpdateNearest();
        if (!_isWristOpen)
        {
            PollWorldKeys();
        }

        RefreshNavigationDistance();
        RefreshHudDynamic();
        if (_wrist != null)
        {
            _wrist.Tick();
        }

        // The prompt is presentation: it refreshes AFTER the wrist has ticked, so the
        // row it shows always belongs to the device the player used in THIS frame
        // (the wrist is what polls the device while the map is open).
        RefreshControllerPrompt();
    }

    /// <summary>
    /// Phase 09B: the surface's controller prompt. While the map is actually
    /// presented the bar shows the map rows (cursor / select / zoom / back); in
    /// free exploration the bar stays hidden, because the interaction prompt in
    /// the world layer already names its own key. The map row set is cached so the
    /// bar is only rebuilt when the page really changes it.
    /// </summary>
    private void RefreshControllerPrompt()
    {
        if (_hud == null)
        {
            return;
        }

        bool mapPresented = _isWristOpen && IsWristTerminalRevealed;
        _hud.SetControllerPrompt(mapPresented, MapPromptRows);
    }

    /// <summary>The surface map's prompt rows (built once, device-independent).</summary>
    private static readonly ControllerPromptRow[] MapPromptRows =
        ControllerPromptBar.SurfaceMapRows();

    /// <summary>
    /// The surface's ONE wrist / map authority.
    ///
    /// Phase 09A_4 root cause: this method used to read the raw M and I keys and
    /// never consumed a logical action, so NO gamepad face control reached the
    /// surface at all - Xbox X (buttonWest) could not open the map, and neither
    /// could Xbox Y. It now consumes the two semantic actions instead:
    ///
    ///   Wrist (Xbox Y / M / I)   - the wrist terminal, whose centre IS the surface map
    ///   Map   (Xbox X / M / I)   - the same surface map presentation
    ///
    /// Both route to the single wrist authority, so the two actions can never
    /// stack two interfaces; while it is open either one toggles it closed, and Esc
    /// (this layer's Back, resolved by UiInput so the physical key is never named
    /// here) closes it without reaching the global Pause.
    ///
    /// Gating: opening is allowed ONLY from free surface gameplay. A local layer
    /// above Gameplay (pause menu / modal panel / console view) owns the frame, and
    /// the map must never open behind it.
    /// </summary>
    private void PollWristKeys()
    {
        // Publish this layer's state: with the wrist open, Escape is a LOCAL Back
        // that closes the terminal (UiInput resolves that, so the physical key is
        // never named here).
        UiInput.SetLayerActive(UiInput.UiLayer.Wrist, _isWristOpen);

        if (_isWristOpen)
        {
            if (UiInput.BackPressedForLayer(UiInput.UiLayer.Wrist))
            {
                UiInput.ConsumeBackForFrame();
                SetWristOpen(false);
                return;
            }

            if (UiInput.WristPressed() || UiInput.MapPressed())
            {
                SetWristOpen(false);
            }

            return;
        }

        // Free gameplay only: pause / modal / console focus keep the map closed.
        if (UiInput.ActiveBackLayer != UiInput.UiLayer.Gameplay)
        {
            return;
        }

        if (UiInput.WristPressed() || UiInput.MapPressed())
        {
            SetWristOpen(true);
        }
    }

    private void OnDisable()
    {
        // The scene leaving must not strand the Escape priority board.
        UiInput.SetLayerActive(UiInput.UiLayer.Wrist, false);
    }

    private void PollWorldKeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        // R return-to-ship shortcut stays available in exploration mode.
        if (keyboard.rKey.wasPressedThisFrame)
        {
            ReturnToShip();
        }
    }

    // ---- Wrist terminal mode ----

    /// <summary>Shared toggle semantic used by both M and I bindings.</summary>
    public void ToggleWrist()
    {
        SetWristOpen(!_isWristOpen);
    }

    public void SetWristOpen(bool open)
    {
        if (open == _isWristOpen)
        {
            return;
        }

        _isWristOpen = open;
        UiInput.SetLayerActive(UiInput.UiLayer.Wrist, open);

        // Wrist mode: freeze exploration input, disable world interaction,
        // release the OS cursor (hidden), show the project cursor.
        if (_player != null)
        {
            _player.InputEnabled = !open;
        }

        if (_hud != null)
        {
            _hud.SetWristMode(open);
        }

        if (open)
        {
            // Phase 09A_5: the surface runs the SAME presentation timing as the
            // ship. The terminal is revealed by WristPresentationBridge once the
            // raise has brought the wrist device into view (~0.82 of the raise),
            // never on the key press - showing it here made the hologram pop in
            // one frame while the arm only started moving afterwards.
            //
            // Presentation decides WHEN the terminal becomes visible, never
            // WHETHER: this state is already authoritative. Without the arms the
            // terminal opens immediately, which is the validated Phase 06
            // behaviour and keeps the wrist reachable in every configuration.
            bool deferred = _wristBridge != null && _wristBridge.BeginWristRaise();
            if (!deferred)
            {
                SetWristTerminalVisibleNow(true);
            }
        }
        else
        {
            // Close order (unchanged): the terminal stops taking input, releases
            // focus and hides FIRST, then the arm comes back down.
            _wristBridge?.BeginWristLower();
            SetWristTerminalVisibleNow(false);
        }

        ApplyCursorForMode();
        WristOpenChanged?.Invoke(open);
    }

    /// <summary>The planet's wrist terminal itself (owned here, revealed by the bridge).</summary>
    private void SetWristTerminalVisibleNow(bool visible)
    {
        if (_wrist != null)
        {
            _wrist.SetOpen(visible);
        }
    }

    /// <summary>
    /// Creates the SAME block-arm presentation the ship interior uses: one
    /// environment-independent controller under the player rig plus the wrist
    /// presentation bridge, wired through <see cref="IArmsPresentationContext"/>.
    /// Reuses an existing instance, so a scene transition can never stack a
    /// second set of arms.
    /// </summary>
    private void EnsureFirstPersonArms()
    {
        if (_player == null || _player.EyeCamera == null)
        {
            Debug.LogError("[SurfaceGameController] first-person arms need the eye camera");
            return;
        }

        _arms = FirstPersonBlockArmsController.Ensure(_player);
        if (_arms == null)
        {
            return;
        }

        _arms.Configure(_player);

        var bridgeGo = new GameObject("WristPresentationBridge");
        bridgeGo.transform.SetParent(transform, false);
        _wristBridge = bridgeGo.AddComponent<WristPresentationBridge>();
        _wristBridge.Configure(this, _arms, _wrist);

        Debug.Log("[SurfaceGameController] first-person block arms ready renderers="
                  + _arms.RendererCount + " wristBridge=" + (_wristBridge != null));
    }

    private void ApplyCursorForMode()
    {
        if (_isWristOpen)
        {
            ApplyWristMode();
        }
        else
        {
            ApplySurfaceFirstPersonMode();
        }
    }

    // Explicit input-mode cursor semantics (CodeReference):
    // - SurfaceFirstPersonMode: locked + hidden OS cursor (mouse look);
    // - WristMode: unlocked + visible with the runtime ring cursor assigned
    //   through Cursor.SetCursor by WristCursorService (owned by the wrist
    //   controller; no UI Toolkit cursor overlay anywhere);
    // - HubUiMode (AppController): unlocked + visible for the 2D Hub.

    public void ApplySurfaceFirstPersonMode()
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

    public void ApplyWristMode()
    {
        if (_wrist != null)
        {
            _wrist.EnterWristCursor();
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ---- Navigation (scene-owned, wrist clickable) ----

    /// <summary>Selects a map navigation target. Depleted/out-of-range nodes
    /// are rejected so the player can never navigate to an exhausted node.</summary>
    public bool TrySelectNavigationTarget(int nodeId)
    {
        SurfaceResourceNode node = GetNodeById(nodeId);
        if (node == null || node.State == SurfaceResourceNode.NodeState.Depleted)
        {
            return false;
        }

        if (_navigationTargetNodeId == nodeId)
        {
            return true;
        }

        _navigationTargetNodeId = nodeId;
        NavigationChanged?.Invoke();

        // Phase 07B: remember the choice for the ship wrist (地表目标已记录).
        if (GameSession.Instance != null)
        {
            GameSession.Instance.RecordSurfaceTarget(nodeId);
        }

        return true;
    }

    public void ClearNavigationTarget()
    {
        if (_navigationTargetNodeId < 0)
        {
            return;
        }

        _navigationTargetNodeId = -1;
        NavigationChanged?.Invoke();
    }

    public SurfaceResourceNode GetNodeById(int nodeId)
    {
        if (nodeId < 0 || nodeId >= _nodes.Count)
        {
            return null;
        }

        return _nodes[nodeId];
    }

    /// <summary>Player-facing node name, e.g. 资源节点 02 for node id 1.</summary>
    public static string NodeDisplayName(int nodeId)
    {
        return "资源节点 " + (nodeId + 1).ToString("00");
    }

    private void RefreshNavigationDistance()
    {
        if (!IsNavigationActive)
        {
            _navigationDistance = 0f;
            return;
        }

        SurfaceResourceNode node = GetNodeById(_navigationTargetNodeId);
        SurfaceResourceNode reference = node != null ? node : _nearest;
        if (_player == null || reference == null)
        {
            _navigationDistance = 0f;
            return;
        }

        _navigationDistance = Vector3.Distance(_player.transform.position,
            reference.transform.position);
    }

    // ---- Node events ----

    private void HandleNodeState(SurfaceResourceNode node)
    {
        if (node.State == SurfaceResourceNode.NodeState.Harvesting
            || node.State == SurfaceResourceNode.NodeState.Transmitting)
        {
            _activeNode = node;
        }
        else if (_activeNode == node)
        {
            _activeNode = null;
        }

        // A settled node can no longer be a navigation target: clear cleanly.
        if (node.State == SurfaceResourceNode.NodeState.Depleted
            && node.NodeId == _navigationTargetNodeId)
        {
            ClearNavigationTarget();
        }
    }

    private void HandleNodeSettled(SurfaceResourceNode node, int yield)
    {
        // Keep the result line outside the wrist open (HUD layer), and the
        // wrist's live card reflects the Depleted state through node events.
        if (_hud != null)
        {
            _hud.ShowTransferResult("传输完成  矿物 +" + yield);
        }

        _resultUntil = Time.time + 2.5f;
    }

    // ---- Dynamic HUD refresh ----

    private void RefreshHudDynamic()
    {
        if (_hud == null)
        {
            return;
        }

        if (_isWristOpen)
        {
            return; // the wrist layer is the only interface while open
        }

        // Round 9: real-time compass heading/waypoint refresh (frame fixed).
        _hud.RefreshCompass(this);

        string prompt;
        if (_interactor != null)
        {
            string gazePrompt = _interactor.ResolvePrompt();
            prompt = gazePrompt ?? "搜寻资源节点";
        }
        else
        {
            prompt = "搜寻资源节点";
        }

        _hud.SetPrompt(prompt);

        // Crosshair state comes from the gaze ray, not proximity.
        if (_interactor != null)
        {
            _hud.SetCrosshairState(_interactor.HasValidTarget
                ? SurfaceHudController.HudCrosshairState.ValidTarget
                : (_interactor.HasBlockedTarget
                    ? SurfaceHudController.HudCrosshairState.BlockedTarget
                    : SurfaceHudController.HudCrosshairState.None));
        }
        else
        {
            _hud.SetCrosshairState(SurfaceHudController.HudCrosshairState.None);
        }

        // Navigation readout while exploring.
        if (IsNavigationActive)
        {
            _hud.SetNavigation(NodeDisplayName(_navigationTargetNodeId),
                NavigationReached ? "目标已到达" : _navigationDistance.ToString("0.0") + " m",
                true);
        }
        else
        {
            _hud.SetNavigation("", "", false);
        }

        if (_activeNode != null)
        {
            float progress = 1f - Mathf.Clamp01(_activeNode.RemainingSeconds /
                Mathf.Max(0.001f, _activeNode.StageSeconds));
            string status = _activeNode.State == SurfaceResourceNode.NodeState.Harvesting
                ? "采集中"
                : "无线传输中";
            _hud.SetNodeStatus(status, progress);
        }
        else
        {
            _hud.SetNodeStatus("—", 0f);
        }

        // Transient transfer-result line auto-clears.
        if (_resultUntil > 0f && Time.time >= _resultUntil)
        {
            _resultUntil = 0f;
            if (_hud != null)
            {
                _hud.ShowTransferResult("");
            }
        }
    }

    // ---- Legacy semantic helpers (tests / internal probes) ----

    /// <summary>Deploys the nearest node when the player is close enough.
    /// Kept for tests and automation; player-facing input uses the gaze ray
    /// through FirstPersonInteractor instead.</summary>
    public bool TryDeployNearest()
    {
        if (_isWristOpen || _nearest == null || _player == null)
        {
            return false;
        }

        if (Vector3.Distance(_nearest.transform.position, _player.transform.position)
            > interactRangeFallback)
        {
            return false;
        }

        return _nearest.TryStartHarvest();
    }

    /// <summary>Returns to the walkable ship interior (FromSurface spawn) with
    /// the session untouched. Phase 07A.1: the surface landing return now
    /// leads to the physical ship instead of the legacy 2D Hub.</summary>
    public void ReturnToShip()
    {
        if (_isWristOpen)
        {
            return;
        }

        SceneRouting.EnterShipInterior(SceneRouting.ShipEntry.FromSurface);
    }

    private void UpdateNearest()
    {
        _nearest = null;
        if (_player == null)
        {
            return;
        }

        float best = float.MaxValue;
        Vector3 origin = _player.transform.position;
        foreach (SurfaceResourceNode node in _nodes)
        {
            float distance = Vector3.Distance(node.transform.position, origin);
            if (distance < best)
            {
                best = distance;
                _nearest = node;
            }
        }
    }
}
