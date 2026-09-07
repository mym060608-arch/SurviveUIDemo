using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Wrist terminal view for the NEREID-04 surface (Phase 06 Revision).
///
/// Pure presentation: SurfaceGameController owns wrist mode and navigation
/// state, GameSession owns personal status / loadout / cargo, and the nodes
/// own their harvesting states. This controller binds those live sources into
/// the wrist UXML: circular LOCAL MAP, personal status, equipment, cargo,
/// navigation readout and the active harvest context. The world keeps running
/// while the wrist is open.
/// </summary>
public class SurfaceWristController : MonoBehaviour
{
    private const float MapMarkerRadiusPx = 12f;
    private const float LandingHalfPx = 8f;
    private const float PlayerHalfPx = 8f;
    private const float NavRingHalfPx = 17f;
    private const float CompletionHoldSeconds = 4f;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _wristLayer;
    private VisualElement _wristPanel;
    private VisualElement _mapCircle;
    private VisualElement _mapPlayer;
    private VisualElement _mapLanding;
    private VisualElement _navRing;
    private VisualElement _cursor;
    private VisualElement[] _markers;
    private Label _nodeCount;
    private Label _healthValue;
    private Label _hungerValue;
    private Label _harvesterState;
    private Label _batteryState;
    private Label _droneState;
    private Label _cargoValue;
    private Label _navName;
    private Label _navDistance;
    private Label _activeNodeLabel;
    private Label _activeState;
    private Label _activePercent;

    private SurfaceGameController _controller;
    private GameSession _session;
    private SurfaceResourceNode _completedNode;
    private float _completedAt;
    private bool _bound;
    private Coroutine _animator;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    /// <summary>Binds the wrist view to the authoritative surface controller
    /// (called once the surface Start order is done).</summary>
    public void Bind(SurfaceGameController controller)
    {
        _controller = controller;
        _session = GameSession.Instance;
        BindElements();
        SubscribeNodes();

        if (_session != null)
        {
            _session.OnStateChanged += HandleSessionChanged;
        }

        if (_controller != null)
        {
            _controller.WristOpenChanged += HandleWristOpenChanged;
            _controller.NavigationChanged += RefreshNavigation;
        }

        RefreshSessionData();
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }

        if (_controller != null)
        {
            _controller.WristOpenChanged -= HandleWristOpenChanged;
            _controller.NavigationChanged -= RefreshNavigation;
        }

        if (_controller != null)
        {
            foreach (SurfaceResourceNode node in _controller.Nodes)
            {
                if (node == null)
                {
                    continue;
                }

                node.OnStateChanged -= HandleNodeState;
                node.OnSettled -= HandleNodeSettled;
            }
        }
    }

    private void SubscribeNodes()
    {
        if (_controller == null)
        {
            return;
        }

        foreach (SurfaceResourceNode node in _controller.Nodes)
        {
            node.OnStateChanged += HandleNodeState;
            node.OnSettled += HandleNodeSettled;
        }
    }

    private void BindElements()
    {
        if (_document == null || _document.rootVisualElement == null)
        {
            return;
        }

        _root = _document.rootVisualElement;
        _wristLayer = _root.Q<VisualElement>("WristLayer");
        _wristPanel = _root.Q<VisualElement>("WristPanel");
        _mapCircle = _root.Q<VisualElement>("WristMapCircle");
        _mapPlayer = _root.Q<VisualElement>("MapPlayer");
        _mapLanding = _root.Q<VisualElement>("MapLanding");
        _navRing = _root.Q<VisualElement>("MapNavRing");
        _cursor = _root.Q<VisualElement>("WristCursor");
        _nodeCount = _root.Q<Label>("WristNodeCount");
        _healthValue = _root.Q<Label>("WristHealthValue");
        _hungerValue = _root.Q<Label>("WristHungerValue");
        _harvesterState = _root.Q<Label>("WristEquipHarvesterState");
        _batteryState = _root.Q<Label>("WristEquipBatteryState");
        _droneState = _root.Q<Label>("WristEquipDroneState");
        _cargoValue = _root.Q<Label>("WristCargoValue");
        _navName = _root.Q<Label>("WristNavName");
        _navDistance = _root.Q<Label>("WristNavDistance");
        _activeNodeLabel = _root.Q<Label>("WristActiveNode");
        _activeState = _root.Q<Label>("WristActiveState");
        _activePercent = _root.Q<Label>("WristActivePercent");
        _markers = new VisualElement[GameSession.SurfaceNodeCount];
        for (int i = 0; i < _markers.Length; i++)
        {
            _markers[i] = _root.Q<VisualElement>("MapNode" + i);
        }

        if (_cursor != null && _wristLayer != null)
        {
            // Project-native dot cursor follows the real pointer.
            _wristLayer.RegisterCallback<PointerMoveEvent>(OnPointerMoved);
        }

        for (int i = 0; i < _markers.Length; i++)
        {
            int captured = i;
            VisualElement marker = _markers[i];
            if (marker == null)
            {
                continue;
            }

            marker.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_controller != null)
                {
                    _controller.TrySelectNavigationTarget(captured);
                }
            });

            marker.RegisterCallback<PointerOverEvent>(evt => SetCursorActive(true));
            marker.RegisterCallback<PointerOutEvent>(evt => SetCursorActive(false));
        }

        _bound = _wristLayer != null && _mapCircle != null;
    }

    /// <summary>Driven every frame by the surface controller so the map stays
    /// live even while the wrist is open.</summary>
    public void Tick()
    {
        if (!_bound)
        {
            BindElements();
            if (!_bound)
            {
                return;
            }

            SubscribeNodes();
            RefreshSessionData();
        }

        if (_wristLayer == null || _wristLayer.style.display == DisplayStyle.None)
        {
            return;
        }

        RefreshMapGeometry();
        RefreshActiveProgress();
    }

    // ---- Wrist mode open / close (with light raise animation) ----

    public void SetOpen(bool open)
    {
        if (_wristLayer == null)
        {
            return;
        }

        if (_animator != null)
        {
            StopCoroutine(_animator);
            _animator = null;
        }

        _wristLayer.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        if (_cursor != null)
        {
            _cursor.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (open && _wristPanel != null)
        {
            _animator = StartCoroutine(AnimateOpen());
        }
        else
        {
            if (_wristPanel != null)
            {
                _wristPanel.style.opacity = 1f;
                _wristPanel.style.scale = new Scale(Vector3.one);
                _wristPanel.style.translate = new Translate(0f, 0f, 0f);
            }

            if (_cursor != null)
            {
                _cursor.style.display = DisplayStyle.None;
            }
        }

        if (open)
        {
            RefreshSessionData();
            RefreshNavigation();
            RefreshMapGeometry();
            RefreshActiveProgress();
        }
    }

    private IEnumerator AnimateOpen()
    {
        // Small "raise the wrist" feel: fade in, scale up, lift into place.
        float duration = 0.22f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t); // smoothstep
            if (_wristPanel != null)
            {
                _wristPanel.style.opacity = t;
                _wristPanel.style.scale = new Scale(new Vector3(0.92f + 0.08f * t,
                    0.92f + 0.08f * t, 1f));
                _wristPanel.style.translate = new Translate(0f, (1f - t) * 40f, 0f);
            }

            yield return null;
        }

        _animator = null;
    }

    private void HandleWristOpenChanged(bool open)
    {
        // State is already applied through SetOpen by the controller.
    }

    // ---- Live data refresh (event driven + per-frame) ----

    private void HandleSessionChanged()
    {
        RefreshSessionData();
    }

    private void HandleNodeState(SurfaceResourceNode node)
    {
        if (node.State == SurfaceResourceNode.NodeState.Depleted && _completedNode == node)
        {
            _completedNode = null;
        }

        RefreshMapGeometry();
        RefreshActiveProgress();
    }

    private void HandleNodeSettled(SurfaceResourceNode node, int yield)
    {
        _completedNode = node;
        _completedAt = Time.time;
        RefreshActiveProgress();
    }

    private void RefreshSessionData()
    {
        if (_session == null)
        {
            return;
        }

        SetText(_healthValue, _session.PlayerHealth.ToString());
        SetText(_hungerValue, _session.PlayerHunger.ToString());
        SetText(_nodeCount, "回收节点 " + _session.HarvestedNodeCount + " / "
                            + GameSession.SurfaceNodeCount);

        SetEquipState(_harvesterState, "已装备", false);
        SetEquipState(_batteryState, _session.BatterySelected ? "已选择" : "未选择",
            !_session.BatterySelected);
        SetEquipState(_droneState, _session.DroneSelected ? "已选择" : "未选择",
            !_session.DroneSelected);

        int equipmentCount = 1 + (_session.BatterySelected ? 1 : 0)
                             + (_session.DroneSelected ? 1 : 0);
        SetText(_cargoValue, "设备 " + equipmentCount + " · 载荷 "
                            + _session.CargoWeight + " / "
                            + GameSession.CargoCapacityKg + " KG");
    }

    private static void SetEquipState(Label state, string text, bool off)
    {
        if (state == null)
        {
            return;
        }

        state.text = text;
        if (off)
        {
            state.AddToClassList("state-off");
        }
        else
        {
            state.RemoveFromClassList("state-off");
        }
    }

    private void RefreshNavigation()
    {
        if (_controller == null)
        {
            return;
        }

        if (_controller.IsNavigationActive)
        {
            SetText(_navName, SurfaceGameController.NodeDisplayName(
                _controller.NavigationTargetNodeId));
            SetText(_navDistance, _controller.NavigationReached
                ? "目标已到达"
                : _controller.NavigationDistance.ToString("0.0") + " m");
        }
        else
        {
            SetText(_navName, "未设置");
            SetText(_navDistance, "");
        }

        if (_navRing != null)
        {
            _navRing.style.display = _controller.IsNavigationActive
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    // ---- Circular map ----

    /// <summary>Places every world marker on the circular map from live world
    /// positions. North-up: world +X -> right, world +Z -> up.</summary>
    private void RefreshMapGeometry()
    {
        if (_controller == null || _mapCircle == null || _controller.Player == null)
        {
            return;
        }

        float width = _mapCircle.resolvedStyle.width > 10f
            ? _mapCircle.resolvedStyle.width
            : 440f;
        float height = _mapCircle.resolvedStyle.height > 10f
            ? _mapCircle.resolvedStyle.height
            : 440f;
        float centerX = width * 0.5f;
        float centerY = height * 0.5f;
        float radius = Mathf.Min(centerX, centerY) - 18f;

        Vector3 playerPos = _controller.Player.transform.position;
        PlaceMapMarker(_mapPlayer, playerPos, PlayerHalfPx, centerX, centerY, radius);

        // Player marker facing: yaw 0 faces +Z (up). UI rotation positive is
        // clockwise, so the marker angle is -yaw.
        if (_mapPlayer != null)
        {
            _mapPlayer.style.rotate = new Rotate(new Angle(
                -_controller.Player.YawDegrees));
        }

        // Landing / return zone sits at the world origin.
        PlaceMapMarker(_mapLanding, Vector3.zero, LandingHalfPx, centerX, centerY, radius);

        // Three resource nodes with live state classes.
        for (int i = 0; i < _markers.Length; i++)
        {
            if (_markers[i] == null)
            {
                continue;
            }

            SurfaceResourceNode node = _controller.GetNodeById(i);
            if (node == null)
            {
                continue;
            }

            PlaceMapMarker(_markers[i], node.transform.position, MapMarkerRadiusPx,
                centerX, centerY, radius);
            ApplyMarkerState(_markers[i], node,
                _controller.NavigationTargetNodeId == i);
        }

        // Selected navigation ring over its node.
        if (_navRing != null && _controller.IsNavigationActive)
        {
            SurfaceResourceNode target = _controller.GetNodeById(
                _controller.NavigationTargetNodeId);
            if (target != null)
            {
                PlaceMapMarker(_navRing, target.transform.position, NavRingHalfPx,
                    centerX, centerY, radius);
                _navRing.style.rotate = new Rotate(new Angle(45f));
            }
        }
    }

    private static void PlaceMapMarker(VisualElement element, Vector3 world,
        float halfSize, float centerX, float centerY, float radius)
    {
        if (element == null)
        {
            return;
        }

        Vector2 fraction = SurfaceGameController.MapFraction(world);
        element.style.left = centerX + fraction.x * radius - halfSize;
        element.style.top = centerY - fraction.y * radius - halfSize;
    }

    private static void ApplyMarkerState(VisualElement marker,
        SurfaceResourceNode node, bool selected)
    {
        marker.RemoveFromClassList("marker-harvesting");
        marker.RemoveFromClassList("marker-transmitting");
        marker.RemoveFromClassList("marker-depleted");
        marker.RemoveFromClassList("marker-selected");

        switch (node.State)
        {
            case SurfaceResourceNode.NodeState.Harvesting:
                marker.AddToClassList("marker-harvesting");
                break;
            case SurfaceResourceNode.NodeState.Transmitting:
                marker.AddToClassList("marker-transmitting");
                break;
            case SurfaceResourceNode.NodeState.Depleted:
                marker.AddToClassList("marker-depleted");
                break;
        }

        if (selected && node.State != SurfaceResourceNode.NodeState.Depleted)
        {
            marker.AddToClassList("marker-selected");
        }
    }

    // ---- Live active harvest context (wrist never pauses the world) ----

    private void RefreshActiveProgress()
    {
        if (_controller == null)
        {
            return;
        }

        SurfaceResourceNode active = _controller.ActiveNode;
        bool showCompletion = active == null && _completedNode != null
                              && Time.time - _completedAt < CompletionHoldSeconds;
        if (active != null)
        {
            SetText(_activeNodeLabel, SurfaceGameController.NodeDisplayName(active.NodeId));
            float progress = 1f - Mathf.Clamp01(active.RemainingSeconds /
                Mathf.Max(0.001f, active.StageSeconds));
            string status = active.State == SurfaceResourceNode.NodeState.Harvesting
                ? "采集中"
                : "无线传输中";
            SetText(_activeState, status);
            SetText(_activePercent, Mathf.RoundToInt(progress * 100f) + "%");
        }
        else if (showCompletion)
        {
            SetText(_activeNodeLabel, SurfaceGameController.NodeDisplayName(
                _completedNode.NodeId));
            SetText(_activeState, "传输完成");
            SetText(_activePercent, "矿物 +5");
        }
        else
        {
            SetText(_activeNodeLabel, "—");
            SetText(_activeState, "待机");
            SetText(_activePercent, "");
        }
    }

    // ---- Custom dot cursor ----

    private void OnPointerMoved(PointerMoveEvent evt)
    {
        if (_cursor == null)
        {
            return;
        }

        _cursor.style.left = evt.localPosition.x - 6f;
        _cursor.style.top = evt.localPosition.y - 6f;
    }

    private void SetCursorActive(bool active)
    {
        if (_cursor == null)
        {
            return;
        }

        if (active)
        {
            _cursor.AddToClassList("cursor-active");
        }
        else
        {
            _cursor.RemoveFromClassList("cursor-active");
        }
    }

    private static void SetText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }
}
