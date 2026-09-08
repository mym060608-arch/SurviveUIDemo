using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Lightweight Chinese-first first-person HUD for the NEREID-04 surface.
///
/// Normal exploration shows: mission context, real resource readouts, the
/// gaze interaction prompt, a small center crosshair, the navigation readout,
/// temporary harvest/transfer feedback and the return action. When the wrist
/// terminal opens (M/I/Esc) the whole normal layer hides and SurfaceWrist-
/// Controller owns the screen instead; the world keeps running underneath.
/// </summary>
public class SurfaceHudController : MonoBehaviour
{
    public enum HudCrosshairState
    {
        None,
        ValidTarget,
        BlockedTarget
    }

    [SerializeField]
    public StyleSheet hudStyleSheet;

    [SerializeField]
    public StyleSheet wristStyleSheet;

    // Panel settings wired at runtime: UIDocument.panelSettings does not
    // serialize reliably from the deterministic scene builder, so the builder
    // stores the asset on this controller instead.
    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _hudLayer;
    private SurfaceGameController _controller;
    private GameSession _session;

    private Label _minerals;
    private Label _efficiency;
    private Label _nodes;
    private Label _prompt;
    private Label _status;
    private Label _statusPercent;
    private Label _result;
    private Button _returnButton;
    private VisualElement _statusCard;

    private VisualElement _crosshairDot;
    private VisualElement _crosshairRing;
    private VisualElement _navBlock;
    private Label _navName;
    private Label _navDistance;

    // ---- Round 9 compass HUD (screen-space surface layer) ----
    private CompassTapeElement _compassTape;
    private VisualElement _compassHost;
    private VisualElement _compassWaypoint;
    private VisualElement _compassFocusDot;
    private Label _compassEdgeLeft;
    private Label _compassEdgeRight;
    private VisualElement _compassInfo;
    private Label _compassInfoText;
    private bool _compassReady;

    private float _lastCompassYaw = float.NaN;
    private float _lastCompassRelative = float.NaN;
    private bool _lastCompassAligned;
    private bool _lastCompassHasTarget;

    private bool _bound;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        if (_document != null)
        {
            if (_document.panelSettings == null && panelSettings != null)
            {
                _document.panelSettings = panelSettings;
            }

            if (_document.rootVisualElement != null)
            {
                BindElements(_document.rootVisualElement);
            }
        }

        _session = GameSession.Instance;
        if (_session != null)
        {
            _session.OnStateChanged += HandleSessionChanged;
        }
    }

    private void OnDisable()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    /// <summary>Called once the surface controller is ready (Start order).</summary>
    public void Bind(SurfaceGameController controller)
    {
        _controller = controller;
        if (_document != null && _document.rootVisualElement != null)
        {
            BindElements(_document.rootVisualElement);
        }
    }

    private void BindElements(VisualElement root)
    {
        _root = root;
        if (!_bound && hudStyleSheet != null)
        {
            root.styleSheets.Add(hudStyleSheet);
            if (wristStyleSheet != null)
            {
                root.styleSheets.Add(wristStyleSheet);
            }

            _bound = true;
        }

        ApplyUiCjkFont();

        _hudLayer = root.Q<VisualElement>("HudLayer");
        _minerals = root.Q<Label>("HudMineralsValue");
        _efficiency = root.Q<Label>("HudEfficiencyValue");
        _nodes = root.Q<Label>("HudNodesValue");
        _prompt = root.Q<Label>("HudPrompt");
        _status = root.Q<Label>("HudStatusText");
        _statusPercent = root.Q<Label>("HudStatusPercent");
        _result = root.Q<Label>("HudResult");
        _statusCard = root.Q<VisualElement>("HudStatusCard");
        _returnButton = root.Q<Button>("HudReturnButton");
        _crosshairDot = root.Q<VisualElement>("HudCrosshairDot");
        _crosshairRing = root.Q<VisualElement>("HudCrosshairRing");
        _navBlock = root.Q<VisualElement>("HudNavBlock");
        _navName = root.Q<Label>("HudNavName");
        _navDistance = root.Q<Label>("HudNavDistance");

        BindCompass(root);

        if (_returnButton != null && _controller != null)
        {
            _returnButton.clicked += _controller.ReturnToHub;
        }
    }

    // ---- Round 9 compass bind + refresh ----

    /// <summary>True once the compass elements resolved (tests/readouts).</summary>
    public bool CompassReady => _compassReady;

    /// <summary>Relative bearing of the last compass refresh (deg).</summary>
    public float LastCompassRelativeDeg => _lastCompassRelative;

    /// <summary>True when the last refresh had the target aligned (+-2 deg).</summary>
    public bool LastCompassAligned => _lastCompassAligned;

    public bool LastCompassHasTarget => _lastCompassHasTarget;

    public CompassTapeElement CompassTape => _compassTape;

    /// <summary>Finds the fixed compass elements and injects the
    /// CompassTapeElement exactly once (like the wrist radial element).</summary>
    private void BindCompass(VisualElement root)
    {
        if (_compassReady)
        {
            return;
        }

        _compassHost = root.Q<VisualElement>("CompassTicksHost");
        _compassWaypoint = root.Q<VisualElement>("CompassWaypoint");
        _compassFocusDot = root.Q<VisualElement>("CompassFocusDot");
        _compassEdgeLeft = root.Q<Label>("CompassEdgeLeft");
        _compassEdgeRight = root.Q<Label>("CompassEdgeRight");
        _compassInfo = root.Q<VisualElement>("CompassInfo");
        _compassInfoText = root.Q<Label>("CompassInfoText");

        if (_compassHost == null || _compassFocusDot == null || _compassEdgeLeft == null
            || _compassEdgeRight == null)
        {
            return;
        }

        _compassHost.Clear();
        _compassTape = new CompassTapeElement();
        _compassHost.Add(_compassTape);
        ApplyCompassFont();
        _compassReady = true;
    }

    private void ApplyCompassFont()
    {
        if (_cjkFont == null || _compassHost == null)
        {
            return;
        }

        foreach (TextElement text in _compassHost.Query<TextElement>().ToList())
        {
            text.style.unityFont = _cjkFont;
        }
    }

    /// <summary>Drives the whole compass from the real camera yaw and the
    /// scene-owned navigation target (frame, focus dot and edge cues never
    /// move; the tape + waypoint scroll). Called every frame while exploring.</summary>
    public void RefreshCompass(SurfaceGameController controller)
    {
        if (!_compassReady || controller == null || controller.Player == null)
        {
            return;
        }

        SurfacePlayerController player = controller.Player;
        float cameraYaw = player.YawDegrees;
        bool hasTarget = controller.IsNavigationActive
                         && controller.GetNodeById(controller.NavigationTargetNodeId) != null;
        float rel = 0f;
        float targetBearing = 0f;

        if (hasTarget)
        {
            SurfaceResourceNode node = controller.GetNodeById(controller.NavigationTargetNodeId);
            targetBearing = CompassTapeElement.TargetBearing(
                player.transform.position, node.transform.position);
            rel = CompassTapeElement.RelativeBearing(cameraYaw, targetBearing);
        }

        bool aligned = hasTarget && Mathf.Abs(rel) <= CompassAlignedDegrees;
        _lastCompassYaw = cameraYaw;
        _lastCompassRelative = rel;
        _lastCompassAligned = aligned;
        _lastCompassHasTarget = hasTarget;

        // Tape follows the camera yaw (wraps 359/0 cleanly).
        if (_compassTape != null && !float.IsNaN(_lastCompassYaw))
        {
            _compassTape.SetCameraYaw(cameraYaw);
        }

        // Fixed centre light dot (never moves) + restrained glow on align.
        if (_compassFocusDot != null)
        {
            if (aligned)
            {
                _compassFocusDot.AddToClassList("compass-aligned");
            }
            else
            {
                _compassFocusDot.RemoveFromClassList("compass-aligned");
            }
        }

        // Waypoint / edge cues from the signed relative bearing.
        bool inView = hasTarget && Mathf.Abs(rel) <= CompassTapeElement.VisibleHalfDegrees;
        if (_compassWaypoint != null)
        {
            _compassWaypoint.style.display = inView ? DisplayStyle.Flex : DisplayStyle.None;
            if (inView)
            {
                float x = CompassFrameCentreX + rel * CompassPixelsPerDegree();
                _compassWaypoint.style.left = x - 6f;
            }
        }

        if (_compassEdgeLeft != null && _compassEdgeRight != null)
        {
            bool leftEdge = hasTarget && rel < -CompassTapeElement.VisibleHalfDegrees;
            bool rightEdge = hasTarget && rel > CompassTapeElement.VisibleHalfDegrees;
            _compassEdgeLeft.style.display = leftEdge ? DisplayStyle.Flex : DisplayStyle.None;
            _compassEdgeRight.style.display = rightEdge ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Minimal restrained nav summary (never duplicates the wrist).
        if (_compassInfo != null && _compassInfoText != null)
        {
            bool show = hasTarget;
            _compassInfo.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                string distance = controller.NavigationReached
                    ? "已到达"
                    : controller.NavigationDistance.ToString("0.0") + " m";
                _compassInfoText.text = "◆ "
                    + SurfaceGameController.NodeDisplayName(controller.NavigationTargetNodeId)
                    + "   " + distance;
            }
        }
    }

    /// <summary>Alignment window in degrees (contract 1.5..2).</summary>
    public const float CompassAlignedDegrees = 2f;

    /// <summary>Compass frame centre x in frame coordinates (design 680px
    /// wide frame -> centre 340).</summary>
    private const float CompassFrameCentreX = 340f;

    private float CompassPixelsPerDegree()
    {
        if (_compassTape == null)
        {
            return 0f;
        }

        float ppd = _compassTape.PixelsPerDegree;
        return ppd > 0f ? ppd : 0f;
    }

    private static Font _cjkFont;

    /// <summary>Phase 06 Chinese-first HUD: OS runtime CJK font, same policy
    /// as AppController (never copied into the project).</summary>
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

    private void HandleSessionChanged()
    {
        RefreshCore();
    }

    /// <summary>Refreshes real GameSession-derived numbers (event driven).</summary>
    public void RefreshCore()
    {
        if (_root == null || _session == null)
        {
            return;
        }

        SetText(_minerals, _session.Minerals.ToString());
        SetText(_efficiency, _session.HarvesterEfficiencyPercent + "%");
        SetText(_nodes, _session.HarvestedNodeCount + " / " + GameSession.SurfaceNodeCount);

        bool canReturn = _session.HarvestedNodeCount > 0;
        if (_returnButton != null)
        {
            _returnButton.style.display = canReturn ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Wrist terminal mode: the whole normal exploration layer hides
    /// so the world + wrist stay uncluttered.</summary>
    public void SetWristMode(bool wristOpen)
    {
        if (_hudLayer != null)
        {
            _hudLayer.style.display = wristOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    // ---- Wrist-mode cursor policy (CodeReference) ----
    //
    // The wrist-mode pointer is NOT a UI Toolkit overlay: SurfaceWristController
    // owns WristCursorService which assigns a runtime ring cursor through
    // Cursor.SetCursor. The HUD only hides its normal layer while the wrist is
    // open.

    public void SetPrompt(string text)
    {
        if (_prompt != null)
        {
            _prompt.text = text;
        }
    }

    public void SetNodeStatus(string statusText, float progress01)
    {
        bool active = statusText != "—";
        if (_statusCard != null)
        {
            _statusCard.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_status != null)
        {
            _status.text = statusText;
        }

        if (_statusPercent != null)
        {
            _statusPercent.text = active
                ? Mathf.RoundToInt(progress01 * 100f) + "%"
                : "";
        }
    }

    /// <summary>Crosshair feedback for the gaze interaction state.</summary>
    public void SetCrosshairState(HudCrosshairState state)
    {
        if (_crosshairDot == null || _crosshairRing == null)
        {
            return;
        }

        _crosshairDot.RemoveFromClassList("dot-valid");
        _crosshairDot.RemoveFromClassList("dot-blocked");
        _crosshairRing.RemoveFromClassList("ring-visible");
        _crosshairRing.RemoveFromClassList("ring-valid");
        _crosshairRing.RemoveFromClassList("ring-blocked");

        switch (state)
        {
            case HudCrosshairState.ValidTarget:
                _crosshairDot.AddToClassList("dot-valid");
                _crosshairRing.AddToClassList("ring-visible");
                _crosshairRing.AddToClassList("ring-valid");
                break;
            case HudCrosshairState.BlockedTarget:
                _crosshairDot.AddToClassList("dot-blocked");
                _crosshairRing.AddToClassList("ring-visible");
                _crosshairRing.AddToClassList("ring-blocked");
                break;
        }
    }

    /// <summary>Restrained exploration navigation readout.</summary>
    public void SetNavigation(string nodeName, string distanceText, bool visible)
    {
        if (_navBlock == null)
        {
            return;
        }

        _navBlock.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (visible)
        {
            SetText(_navName, "◆ " + nodeName);
            SetText(_navDistance, distanceText);
        }
    }

    /// <summary>Transient transfer result shown next to the prompt area.
    /// Hidden again when cleared (no empty pill over the world).</summary>
    public void ShowTransferResult(string text)
    {
        if (_result == null)
        {
            return;
        }

        _result.text = text;
        _result.style.display = string.IsNullOrEmpty(text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    private static void SetText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }
}
