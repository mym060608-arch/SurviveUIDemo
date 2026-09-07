using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Authoritative coordinator of the NEREID-04 surface slice (Phase 06).
///
/// Owns: player/camera framing, node registry, proximity interaction, the E
/// deploy action, R return shortcut, HUD refresh and semantic automation
/// hooks. Scene transition decisions belong here; resource settlement lives
/// in GameSession only.
/// </summary>
public class SurfaceGameController : MonoBehaviour
{
    public float interactRange = 2.75f;
    public Vector3 cameraOffset = new Vector3(0f, 10f, -10f);

    private readonly List<SurfaceResourceNode> _nodes = new List<SurfaceResourceNode>();
    private SurfacePlayerController _player;
    private SurfaceHudController _hud;
    private Camera _camera;
    private SurfaceResourceNode _nearest;
    private SurfaceResourceNode _activeNode;
    private float _resultUntil;
    private bool _ready;

    public bool IsReady => _ready;
    public IReadOnlyList<SurfaceResourceNode> Nodes => _nodes;
    public SurfaceResourceNode NearestNode => _nearest;
    public SurfaceResourceNode ActiveNode => _activeNode;

    private void Start()
    {
        _player = Object.FindAnyObjectByType<SurfacePlayerController>();
        _hud = Object.FindAnyObjectByType<SurfaceHudController>();
        _camera = Camera.main;

        _nodes.Clear();
        foreach (SurfaceResourceNode node in Object.FindObjectsByType<SurfaceResourceNode>())
        {
            _nodes.Add(node);
            node.OnStateChanged += HandleNodeState;
            node.OnSettled += HandleNodeSettled;
        }

        // Deterministic registry: FindObjectsByType order is not stable, so the
        // list is sorted by NodeId. Consumers may rely on Nodes[0] == node id 0.
        _nodes.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));

        _ready = true;

        if (_hud != null)
        {
            _hud.Bind(this);
            _hud.RefreshCore();
        }
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

        FollowPlayer();
        UpdateNearest();
        HandleGameplayKeys();
        RefreshHudDynamic();
    }

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
    }

    private void HandleNodeSettled(SurfaceResourceNode node, int yield)
    {
        if (_hud != null)
        {
            _hud.ShowTransferResult("传输完成  矿物 +" + yield);
        }

        _resultUntil = Time.time + 2.5f;
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

    private void FollowPlayer()
    {
        if (_player == null || _camera == null)
        {
            return;
        }

        _camera.transform.position = _player.transform.position + cameraOffset;
        _camera.transform.LookAt(_player.transform.position + Vector3.up * 0.9f);
    }

    private void HandleGameplayKeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            TryDeployNearest();
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            ReturnToHub();
        }
    }

    private void RefreshHudDynamic()
    {
        if (_hud == null)
        {
            return;
        }

        string prompt;
        if (_nearest == null)
        {
            prompt = "搜寻资源节点";
        }
        else if (_nearest.State == SurfaceResourceNode.NodeState.Available)
        {
            prompt = "E  部署采集器";
        }
        else if (_nearest.State == SurfaceResourceNode.NodeState.Depleted)
        {
            prompt = "资源节点已回收";
        }
        else
        {
            prompt = _nearest.State == SurfaceResourceNode.NodeState.Harvesting
                ? "采集中"
                : "无线传输中";
        }

        _hud.SetPrompt(prompt);

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
            _hud.ShowTransferResult("");
        }
    }

    /// <summary>Deploys the nearest node when the player is close enough.
    /// Returns false when nothing deployable is in range.</summary>
    public bool TryDeployNearest()
    {
        if (_nearest == null || _player == null)
        {
            return false;
        }

        if (Vector3.Distance(_nearest.transform.position, _player.transform.position) > interactRange)
        {
            return false;
        }

        return _nearest.TryStartHarvest();
    }

    /// <summary>Back to the Hub (no Reset); GameSession stays authoritative.</summary>
    public void ReturnToHub()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(BootController.HubSceneName);
    }
}
