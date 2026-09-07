using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Wrist terminal presented as a real "raised wrist" projection
/// (Phase 06 Revision round 3).
///
/// The wrist UXML renders into a fixed 1280x800 RenderTexture which is shown
/// on a tilted world plane anchored camera-relative (lower-left of view), so
/// the terminal reads as floating interface projected from the wrist while
/// the 3D world stays visible around it.
///
/// Real hardware navigation: while WristMode is open this controller reads
/// Mouse.current.leftButton and the real cursor position, casts a ray from
/// the main camera through the cursor onto the wrist display plane, converts
/// the hit to LOCAL MAP coordinates, and hit-tests the node markers before
/// calling the single authoritative TrySelectNavigationTarget(nodeId).
/// TrySelectNavigationTarget remains the state semantic; physical clicks are
/// routed through this same method and still need a human spot-check.
/// </summary>
public class SurfaceWristController : MonoBehaviour
{
    // RenderTexture / logical UI size and LOCAL MAP anchor (fixed pixels).
    public const int UiWidth = 1280;
    public const int UiHeight = 800;
    public const int MapLeft = 390;
    public const int MapTop = 150;
    public const int MapSize = 500;
    public const float MapRadiusPx = 226f;
    public const float NodeHitRadiusPx = 34f;

    private const int RingTickCount = 64;
    private const float RingTickHalfPx = 3f;
    private const float MapMarkerHalfPx = 17f;
    private const float LandingHalfPx = 8f;
    private const float PlayerHalfPx = 8f;
    private const float NavRingHalfPx = 21f;
    private const float CompletionHoldSeconds = 4f;

    [SerializeField]
    public StyleSheet wristStyleSheet;

    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private RenderTexture _renderTexture;
    private VisualElement _root;
    private VisualElement _mapCircle;
    private VisualElement _mapPlayer;
    private VisualElement _mapLanding;
    private VisualElement _navRing;
    private VisualElement _cursor;
    private VisualElement[] _markers;
    private Label _nodeCount;
    private Label _healthValue;
    private Label _satietyValue;
    private Label _actionState;
    private Label _harvesterState;
    private Label _batteryState;
    private Label _droneState;
    private Label _cargoValue;
    private Label _navName;
    private Label _navDistance;
    private Label _activeNodeLabel;
    private Label _activeState;
    private Label _activePercent;

    private VisualElement _satietyRing;
    private VisualElement _healthRing;
    private readonly List<VisualElement> _satietyTicks = new List<VisualElement>();
    private readonly List<VisualElement> _healthTicks = new List<VisualElement>();
    private bool _ticksBuilt;

    private SurfaceGameController _controller;
    private GameSession _session;
    private SurfaceResourceNode _completedNode;
    private float _completedAt;
    private bool _bound;
    private Coroutine _animator;
    private Camera _mainCamera;

    private WristDisplayAnchor _display;
    private int _hoverNodeId = -1;
    private Vector2 _lastPointerPixel = new Vector2(UiWidth * 0.5f, UiHeight * 0.5f);

    /// <summary>Exposed for tests: the wrist document's root element.</summary>
    public VisualElement RootElement => _root;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _renderTexture = new RenderTexture(UiWidth, UiHeight, 0);
        _renderTexture.name = "WristRenderTexture";
        _renderTexture.Create();

        if (_document != null)
        {
            if (_document.panelSettings == null && panelSettings != null)
            {
                _document.panelSettings = panelSettings;
            }

            if (_document.panelSettings != null)
            {
                _document.panelSettings.targetTexture = _renderTexture;
            }
        }
    }

    private void OnDestroy()
    {
        OnDestroyUnsubscribe();
        if (_document != null && _document.panelSettings != null)
        {
            _document.panelSettings.targetTexture = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            _renderTexture = null;
        }
    }

    /// <summary>Binds the wrist view to the authoritative surface controller.</summary>
    public void Bind(SurfaceGameController controller)
    {
        _controller = controller;
        _session = GameSession.Instance;
        _mainCamera = controller.Player != null ? controller.Player.EyeCamera : Camera.main;
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
        SetOpen(false);
    }

    private void OnDestroyUnsubscribe()
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

    private void OnDisable()
    {
        OnDestroyUnsubscribe();
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
        if (wristStyleSheet != null)
        {
            _root.styleSheets.Add(wristStyleSheet);
        }

        ApplyUiCjkFont();

        _mapCircle = _root.Q<VisualElement>("WristMapCircle");
        _mapPlayer = _root.Q<VisualElement>("MapPlayer");
        _mapLanding = _root.Q<VisualElement>("MapLanding");
        _navRing = _root.Q<VisualElement>("MapNavRing");
        _cursor = _root.Q<VisualElement>("WristCursor");
        _satietyRing = _root.Q<VisualElement>("WristSatietyRing");
        _healthRing = _root.Q<VisualElement>("WristHealthRing");
        _nodeCount = _root.Q<Label>("WristNodeCount");
        _healthValue = _root.Q<Label>("WristHealthValue");
        _satietyValue = _root.Q<Label>("WristSatietyValue");
        _actionState = _root.Q<Label>("WristActionState");
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

        BuildRingTicks();
        _bound = _mapCircle != null;
    }

    private static Font _cjkFont;

    private void ApplyUiCjkFont()
    {
        if (_cjkFont == null)
        {
            string[] candidates =
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Noto Sans CJK SC",
                "Source Han Sans SC"
            };

            _cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 16);
        }

        if (_cjkFont == null || _root == null)
        {
            return;
        }

        foreach (TextElement text in _root.Query<TextElement>().ToList())
        {
            text.style.unityFont = _cjkFont;
        }
    }

    /// <summary>Driven every frame by the surface controller.</summary>
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

        if (_document == null || !_document.enabled)
        {
            return;
        }

        RefreshMapGeometry();
        RefreshRingPositions();
        RefreshActiveProgress();
        ProcessHardwarePointer();
    }

    // ---- Wrist mode open / close ----

    public void SetOpen(bool open)
    {
        if (_document == null)
        {
            return;
        }

        if (_animator != null)
        {
            StopCoroutine(_animator);
            _animator = null;
        }

        // The wrist UXML renders only into its own RenderTexture, so it can be
        // kept disabled (and cheap) whenever the terminal is closed.
        _document.enabled = open;
        if (_display == null)
        {
            _display = Object.FindAnyObjectByType<WristDisplayAnchor>();
        }

        if (_display != null)
        {
            _display.Show(open);
            if (open)
            {
                _display.AttachTexture(_renderTexture);
                _animator = StartCoroutine(AnimateOpen());
            }
        }

        if (open)
        {
            RefreshSessionData();
            RefreshNavigation();
            RefreshMapGeometry();
            RefreshRingPositions();
            RefreshRingVisuals();
            RefreshActiveProgress();
        }
    }

    private IEnumerator AnimateOpen()
    {
        if (_display == null)
        {
            yield break;
        }

        float duration = 0.2f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            if (_display != null)
            {
                _display.SetScale(Mathf.Lerp(0.82f, 1f, t));
            }

            yield return null;
        }

        _animator = null;
    }

    private void HandleWristOpenChanged(bool open)
    {
        // SetOpen is applied by the controller already.
    }

    // ---- Live data refresh ----

    private void HandleSessionChanged()
    {
        RefreshSessionData();
        RefreshRingVisuals();
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
        SetText(_satietyValue, _session.PlayerSatiety.ToString());
        SetText(_nodeCount, _session.HarvestedNodeCount + " / " + GameSession.SurfaceNodeCount);

        SurfaceResourceNode active = _controller != null ? _controller.ActiveNode : null;
        SetText(_actionState, active != null
            ? (active.State == SurfaceResourceNode.NodeState.Harvesting ? "采集中" : "传输中")
            : "探索中");

        SetEquipState(_harvesterState, "已装备", false);
        SetEquipState(_batteryState, _session.BatterySelected ? "已选择" : "未选择",
            !_session.BatterySelected);
        SetEquipState(_droneState, _session.DroneSelected ? "已选择" : "未选择",
            !_session.DroneSelected);

        int equipmentCount = 1 + (_session.BatterySelected ? 1 : 0)
                             + (_session.DroneSelected ? 1 : 0);
        SetText(_cargoValue, "设备 " + equipmentCount + " · " + _session.CargoWeight
                            + " / " + GameSession.CargoCapacityKg + " KG");
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

    // ---- Thin ring readouts ----

    private void BuildRingTicks()
    {
        if (_ticksBuilt || _satietyRing == null || _healthRing == null)
        {
            return;
        }

        _satietyTicks.Clear();
        _healthTicks.Clear();
        BuildTicksInto(_satietyRing, _satietyTicks);
        BuildTicksInto(_healthRing, _healthTicks);
        _ticksBuilt = true;
    }

    private static void BuildTicksInto(VisualElement ring, List<VisualElement> output)
    {
        for (int i = 0; i < RingTickCount; i++)
        {
            var tick = new VisualElement();
            tick.AddToClassList("ring-tick");
            ring.Add(tick);
            output.Add(tick);
        }
    }

    private void RefreshRingVisuals()
    {
        if (_session == null)
        {
            return;
        }

        ApplyRingFraction(_healthTicks, _session.PlayerHealth / 100f);
        ApplyRingFraction(_satietyTicks, _session.PlayerSatiety / 100f);
    }

    private static void ApplyRingFraction(List<VisualElement> ticks, float fraction01)
    {
        int active = Mathf.RoundToInt(Mathf.Clamp01(fraction01) * ticks.Count);
        for (int i = 0; i < ticks.Count; i++)
        {
            if (i < active)
            {
                ticks[i].AddToClassList("tick-active");
            }
            else
            {
                ticks[i].RemoveFromClassList("tick-active");
            }
        }
    }

    private void RefreshRingPositions()
    {
        LayoutTicks(_satietyRing, _satietyTicks);
        LayoutTicks(_healthRing, _healthTicks);
    }

    private static void LayoutTicks(VisualElement ring, List<VisualElement> ticks)
    {
        if (ring == null || ticks.Count == 0)
        {
            return;
        }

        float width = ring.resolvedStyle.width > 10f ? ring.resolvedStyle.width : 660f;
        float height = ring.resolvedStyle.height > 10f ? ring.resolvedStyle.height : 660f;
        float centerX = width * 0.5f;
        float centerY = height * 0.5f;
        float radius = Mathf.Min(centerX, centerY) - 12f;
        float stepDegrees = 360f / ticks.Count;

        for (int i = 0; i < ticks.Count; i++)
        {
            float angle = (i * stepDegrees - 90f) * Mathf.Deg2Rad;
            float x = centerX + Mathf.Cos(angle) * radius - RingTickHalfPx;
            float y = centerY + Mathf.Sin(angle) * radius - RingTickHalfPx;
            ticks[i].style.left = x;
            ticks[i].style.top = y;
        }
    }

    // ---- Circular map ----

    private void RefreshMapGeometry()
    {
        if (_controller == null || _mapCircle == null || _controller.Player == null)
        {
            return;
        }

        // The wrist is a fixed 1280x800 logical canvas, so map-local math is
        // deterministic in UI pixels.
        float centerX = MapSize * 0.5f;
        float centerY = MapSize * 0.5f;

        Vector3 playerPos = _controller.Player.transform.position;
        PlaceMapMarker(_mapPlayer, playerPos, PlayerHalfPx, centerX, centerY);

        if (_mapPlayer != null)
        {
            _mapPlayer.style.rotate = new Rotate(new Angle(-_controller.Player.YawDegrees));
        }

        PlaceMapMarker(_mapLanding, Vector3.zero, LandingHalfPx, centerX, centerY);

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

            PlaceMapMarker(_markers[i], node.transform.position, MapMarkerHalfPx, centerX, centerY);
            ApplyMarkerState(_markers[i], node, _controller.NavigationTargetNodeId == i);
        }

        if (_navRing != null && _controller.IsNavigationActive)
        {
            SurfaceResourceNode target = _controller.GetNodeById(_controller.NavigationTargetNodeId);
            if (target != null)
            {
                PlaceMapMarker(_navRing, target.transform.position, NavRingHalfPx, centerX, centerY);
                _navRing.style.rotate = new Rotate(new Angle(45f));
            }
        }
    }

    private static void PlaceMapMarker(VisualElement element, Vector3 world,
        float halfSize, float centerX, float centerY)
    {
        if (element == null)
        {
            return;
        }

        Vector2 fraction = SurfaceGameController.MapFraction(world);
        element.style.left = centerX + fraction.x * MapRadiusPx - halfSize;
        element.style.top = centerY - fraction.y * MapRadiusPx - halfSize;
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

    // ---- Hardware pointer navigation (Mouse.current + display ray) ----

    private void ProcessHardwarePointer()
    {
        if (_controller == null || _mainCamera == null || _display == null || _cursor == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 mousePosition = mouse.position.ReadValue();
        Ray ray = _mainCamera.ScreenPointToRay(mousePosition);
        if (!_display.RaycastToLocal(ray, out Vector2 uv))
        {
            _cursor.style.display = DisplayStyle.None;
            _hoverNodeId = -1;
            return;
        }

        Vector2 wristPixel = UVToWristPixel(uv);
        _lastPointerPixel = wristPixel;
        _cursor.style.display = DisplayStyle.Flex;
        _cursor.style.left = wristPixel.x - 7f;
        _cursor.style.top = wristPixel.y - 7f;

        int hit = HitTestNode(wristPixel);
        SetCursorActive(hit >= 0);
        _hoverNodeId = hit;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            // Real hardware left click -> deterministic hit-test -> single
            // authoritative selector.
            if (hit >= 0)
            {
                _controller.TrySelectNavigationTarget(hit);
            }
        }
    }

    /// <summary>Display UV (0..1) to wrist logical pixel (UI y down).</summary>
    public static Vector2 UVToWristPixel(Vector2 uv)
    {
        return new Vector2(
            Mathf.Clamp01(uv.x) * UiWidth,
            (1f - Mathf.Clamp01(uv.y)) * UiHeight);
    }

    /// <summary>Deterministic LOCAL MAP node hit-test on a wrist pixel.
    /// Returns node id or -1. Rejects nodes outside the circular map.</summary>
    public int HitTestNode(Vector2 wristPixel)
    {
        if (_controller == null)
        {
            return -1;
        }

        float mapLocalX = wristPixel.x - MapLeft;
        float mapLocalY = wristPixel.y - MapTop;
        Vector2 mapCenter = new Vector2(MapSize * 0.5f, MapSize * 0.5f);
        if (Vector2.Distance(new Vector2(mapLocalX, mapLocalY), mapCenter) > MapRadiusPx + 6f)
        {
            return -1;
        }

        int best = -1;
        float bestDistance = NodeHitRadiusPx;
        for (int i = 0; i < _controller.Nodes.Count; i++)
        {
            SurfaceResourceNode node = _controller.GetNodeById(i);
            if (node == null)
            {
                continue;
            }

            Vector2 fraction = SurfaceGameController.MapFraction(node.transform.position);
            Vector2 markerCenter = mapCenter + new Vector2(
                fraction.x * MapRadiusPx, -fraction.y * MapRadiusPx);
            float distance = Vector2.Distance(new Vector2(mapLocalX, mapLocalY), markerCenter);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
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

    // ---- Live active harvest context ----

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
            SetText(_activeNodeLabel, SurfaceGameController.NodeDisplayName(_completedNode.NodeId));
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

    private static void SetText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }
}
