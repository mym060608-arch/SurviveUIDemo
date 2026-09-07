using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Authoritative coordinator of the NEREID-04 first-person surface slice
/// (Phase 06 + Revision).
///
/// Owns: the node registry, wrist terminal mode (M / I / Esc), scene-owned
/// navigation target state, map coordinate math, world input gating, HUD
/// refresh and the legacy semantic helpers. Resource settlement stays in
/// GameSession; gaze interaction happens through FirstPersonInteractor.
/// </summary>
public class SurfaceGameController : MonoBehaviour
{
    // World-space half extent used to normalize XZ into the circular map.
    // Player bounds are +/-16.5, nodes stay well inside +/-17.
    public const float MapHalfExtent = 17f;

    // Legacy proximity helper range (semantic tests/probes only).
    public float interactRangeFallback = 3.0f;

    private readonly List<SurfaceResourceNode> _nodes = new List<SurfaceResourceNode>();
    private SurfacePlayerController _player;
    private SurfaceHudController _hud;
    private SurfaceWristController _wrist;
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

    public bool IsWristOpen => _isWristOpen;

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
        UpdateNearest();
        if (!_isWristOpen)
        {
            PollWorldKeys();
        }

        RefreshNavigationDistance();
        TickWristCursorOverlay();
        RefreshHudDynamic();
        if (_wrist != null)
        {
            _wrist.Tick();
        }
    }

    private bool _cursorOverlayActive;

    /// <summary>Drives the screen-space dot cursor while WristMode is open and
    /// hides it otherwise (never two cursors; OS cursor stays hidden).</summary>
    private void TickWristCursorOverlay()
    {
        if (_hud == null)
        {
            return;
        }

        bool shouldShow = _isWristOpen;
        if (shouldShow != _cursorOverlayActive)
        {
            _cursorOverlayActive = shouldShow;
            _hud.SetWristCursorEnabled(shouldShow);
        }

        if (shouldShow)
        {
            int hover = _wrist != null ? _wrist.HitTestFromScreenMouse() : -1;
            _hud.TickWristCursor(hover >= 0);
        }
    }

    /// <summary>M and I both route to the single ToggleWrist semantic; Esc only
    /// closes while the wrist is open. No second wrist mode exists.</summary>
    private void PollWristKeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.mKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame)
        {
            ToggleWrist();
            return;
        }

        if (_isWristOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetWristOpen(false);
        }
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
            ReturnToHub();
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

        if (_wrist != null)
        {
            _wrist.SetOpen(open);
        }

        ApplyCursorForMode();
        WristOpenChanged?.Invoke(open);
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

    // Explicit input-mode semantics (see round-3 blocker 3):
    // - SurfaceFirstPersonMode: locked + hidden OS cursor, mouse look on;
    // - WristMode: unlocked + hidden OS cursor, dot pointer on the display;
    // - HubUiMode (AppController): unlocked + visible for the 2D Hub.

    public void ApplySurfaceFirstPersonMode()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ApplyWristMode()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = false;
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

    /// <summary>World XZ -> normalized [-1..1] map fractions (deterministic,
    /// shared with the wrist map and its tests).</summary>
    public static Vector2 MapFraction(Vector3 worldPosition)
    {
        return new Vector2(
            Mathf.Clamp(worldPosition.x / MapHalfExtent, -1f, 1f),
            Mathf.Clamp(worldPosition.z / MapHalfExtent, -1f, 1f));
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

    /// <summary>Back to the Hub (no Reset); GameSession stays authoritative.</summary>
    public void ReturnToHub()
    {
        if (_isWristOpen)
        {
            return;
        }

        SceneManager.LoadScene(BootController.HubSceneName);
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
