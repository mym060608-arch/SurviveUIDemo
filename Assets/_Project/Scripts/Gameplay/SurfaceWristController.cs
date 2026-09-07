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

    // Light raised-wrist tilt (small, safe). Automatically reduced when the
    // projected aspect would collapse below 1.35 (near edge-on).
    private float _tiltYaw = 5f;
    private float _tiltPitch = 3f;
    private float _tiltRoll = -2f;

    private WristDisplayAnchor _display;
    private int _hoverNodeId = -1;
    private Vector2 _lastPointerPixel = new Vector2(UiWidth * 0.5f, UiHeight * 0.5f);

    /// <summary>Exposed for tests: the wrist document's root element.</summary>
    public VisualElement RootElement => _root;

    /// <summary>The wrist RenderTexture (exposed for visibility assertions).</summary>
    public RenderTexture CurrentRenderTexture => _renderTexture;

    /// <summary>Wrist display anchor (exposed for visibility assertions).</summary>
    public WristDisplayAnchor Display => _display;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _renderTexture = new RenderTexture(UiWidth, UiHeight, 0, RenderTextureFormat.ARGB32);
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

    /// <summary>Adds SurfaceWrist.uss to the wrist document root exactly once
    /// (guarded by Contains so re-opens never duplicate). Exposed diagnostics
    /// report whether the sheet is actually bound.</summary>
    public bool EnsureStyleSheets()
    {
        if (_document == null || _document.rootVisualElement == null || wristStyleSheet == null)
        {
            return false;
        }

        VisualElement root = _document.rootVisualElement;
        if (!root.styleSheets.Contains(wristStyleSheet))
        {
            root.styleSheets.Add(wristStyleSheet);
        }

        _root = root;
        return root.styleSheets.Contains(wristStyleSheet);
    }

    /// <summary>Sheet binding summary for the wrist document (logs/tests).</summary>
    public string BoundStyleSheetNames()
    {
        if (_document == null || _document.rootVisualElement == null)
        {
            return "<no root>";
        }

        bool bound = EnsureStyleSheets();
        return bound ? "SurfaceWrist.uss attached" : "SurfaceWrist.uss MISSING";
    }

    /// <summary>Runtime computed-style/layout report for the wrist document:
    /// returns false when any measured geometry is NaN/zero/degenerate.</summary>
    public bool VerifyWristLayoutGeometry(out string report)
    {
        var lines = new System.Text.StringBuilder();
        bool ok = true;

        void Check(bool pass, string label)
        {
            lines.AppendLine((pass ? "PASS" : "FAIL") + " :: " + label);
            ok &= pass;
        }

        if (_document == null || _document.rootVisualElement == null)
        {
            report = "FAIL :: wrist document root missing\n";
            return false;
        }

        lines.AppendLine("INFO :: visualTreeAsset="
                         + (_document.visualTreeAsset != null ? _document.visualTreeAsset.name : "<null>")
                         + " styles=[" + BoundStyleSheetNames() + "]");

        VisualElement root = _document.rootVisualElement;
        bool sheetBound = EnsureStyleSheets();
        Check(sheetBound, "SurfaceWrist.uss bound on the wrist document root");
        lines.AppendLine("INFO :: root children=" + root.childCount
                         + " resolved=" + RootResolved(root));

        VisualElement map = root.Q<VisualElement>("WristMapCircle");
        Check(map != null, "LocalMap element resolved");
        if (map != null)
        {
            string m = Resolved(map);
            lines.AppendLine("INFO :: LocalMap " + m);
            float w = map.resolvedStyle.width;
            float h = map.resolvedStyle.height;
            Check(!float.IsNaN(w) && !float.IsNaN(h) && w >= 480f && h >= 480f,
                "LocalMap size >= ~500px (got " + w.ToString("0") + "x" + h.ToString("0") + ")");
            Check(!float.IsNaN(w) && !float.IsNaN(h) && w > 0f
                  && Mathf.Abs(w - h) / Mathf.Max(1f, h) < 0.02f,
                "LocalMap stays square/aspect ~1");
        }

        VisualElement healthRing = root.Q<VisualElement>("WristHealthRing");
        VisualElement satietyRing = root.Q<VisualElement>("WristSatietyRing");
        Check(healthRing != null && HealthRingWidth(healthRing) > 300f,
            "Health ring region >300px");
        Check(satietyRing != null && HealthRingWidth(satietyRing) > 300f,
            "Satiety ring region >300px");
        if (map != null && healthRing != null && satietyRing != null)
        {
            Check(!float.IsNaN(healthRing.resolvedStyle.width) && healthRing.resolvedStyle.width > 0f,
                "Health ring not 0x0");
            Check(!float.IsNaN(satietyRing.resolvedStyle.width) && satietyRing.resolvedStyle.width > 0f,
                "Satiety ring not 0x0");
            lines.AppendLine("INFO :: healthRing " + Resolved(healthRing));
            lines.AppendLine("INFO :: satietyRing " + Resolved(satietyRing));
            lines.AppendLine("INFO :: map top=" + map.resolvedStyle.top.ToString("0")
                             + " health top=" + healthRing.resolvedStyle.top.ToString("0")
                             + " satiety top=" + satietyRing.resolvedStyle.top.ToString("0"));
        }

        // Four floating panels at their sketch quadrants (style coordinates).
        string[] panelNames = { "WristEquipHarvester", "WristCargoValue", "WristNavName", "WristActiveState" };
        lines.AppendLine("INFO :: panels resolved: " + Resolved(root.Q<VisualElement>("WristEquipHarvester"))
                         + " | " + Resolved(root.Q<VisualElement>("WristNavName"))
                         + " | " + Resolved(root.Q<VisualElement>("WristCargoValue"))
                         + " | " + Resolved(root.Q<VisualElement>("WristActiveState")));

        report = lines.ToString();
        return ok;
    }

    private static float HealthRingWidth(VisualElement ring)
    {
        return ring == null ? 0f : ring.resolvedStyle.width;
    }

    private static string Resolved(VisualElement e)
    {
        if (e == null)
        {
            return "<null>";
        }

        string pos = "?";
        try
        {
            pos = e.resolvedStyle.left.ToString("0") + ","
                  + e.resolvedStyle.top.ToString("0");
        }
        catch (System.Exception)
        {
            pos = "auto";
        }

        return "resolved=" + e.resolvedStyle.width.ToString("0") + "x"
               + e.resolvedStyle.height.ToString("0") + " pos=" + pos;
    }

    private static string RootResolved(VisualElement root)
    {
        return root.resolvedStyle.width.ToString("0") + "x"
               + root.resolvedStyle.height.ToString("0");
    }

    private void BindElements()
    {
        if (_document == null || _document.rootVisualElement == null)
        {
            return;
        }

        _root = _document.rootVisualElement;
        EnsureStyleSheets();
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

        EnsureRingTicksCurrent();
        RefreshMapGeometry();
        RefreshRingPositions();
        RefreshActiveProgress();
        ProcessHardwarePointer();
    }

    // ---- Runtime pose: front-facing basis + light tilt (round 6) ----

    /// <summary>Points the display front (Quad front = -transform.forward)
    /// directly at the camera, aligned with camera up/right, then applies a
    /// small raised-wrist tilt that is automatically reduced if the projected
    /// aspect would collapse. No hardcoded near-180 guess.</summary>
    public void PoseDisplayFacingCamera()
    {
        if (_display == null)
        {
            _display = Object.FindAnyObjectByType<WristDisplayAnchor>();
        }

        if (_display == null || _mainCamera == null)
        {
            return;
        }

        Transform display = _display.transform;
        Transform camera = _mainCamera.transform;
        Vector3 toCamera = camera.position - display.position;
        if (toCamera.sqrMagnitude < 1e-6f)
        {
            return;
        }

        // transform.forward must point AWAY from the camera so that the Quad
        // front (-forward) faces it, aligned to camera up.
        Quaternion frontFacing = Quaternion.LookRotation(-toCamera.normalized,
            camera.up);
        display.rotation = frontFacing;
        float safeYaw = _tiltYaw;
        float safePitch = _tiltPitch;
        float safeRoll = _tiltRoll;
        int guard = 0;
        while (guard < 3)
        {
            display.rotation = frontFacing
                               * Quaternion.Euler(safePitch, safeYaw, safeRoll);
            float aspect = ComputeProjectedAspectPx();
            if (aspect <= 0f || aspect >= 1.35f)
            {
                break;
            }

            safeYaw *= 0.5f;
            safePitch *= 0.5f;
            safeRoll *= 0.5f;
            guard++;
        }

        _tiltYaw = safeYaw;
        _tiltPitch = safePitch;
        _tiltRoll = safeRoll;
        Debug.Log("[WristVisibility] tilt yaw=" + _tiltYaw + " pitch=" + _tiltPitch
                  + " roll=" + _tiltRoll + " aspect="
                  + ComputeProjectedAspectPx().ToString("0.00"));
    }

    /// <summary>Projected display aspect in screen pixels (width/height),
    /// computed from the four display corners through the main camera.
    /// Returns 0 when the display/camera are unusable.</summary>
    public float ComputeProjectedAspectPx()
    {
        if (_display == null || _mainCamera == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return 0f;
        }

        Transform t = _display.transform;
        Vector3 scale = t.localScale;
        float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
        bool any = false;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = t.position + t.right * (sx * scale.x * 0.5f)
                                 + t.up * (sy * scale.y * 0.5f);
                Vector3 c = _mainCamera.WorldToViewportPoint(corner);
                if (c.z > 0f)
                {
                    any = true;
                    minX = Mathf.Min(minX, c.x);
                    maxX = Mathf.Max(maxX, c.x);
                    minY = Mathf.Min(minY, c.y);
                    maxY = Mathf.Max(maxY, c.y);
                }
            }
        }

        if (!any)
        {
            return 0f;
        }

        float widthPx = (maxX - minX) * Screen.width;
        float heightPx = (maxY - minY) * Screen.height;
        return heightPx > 0f ? widthPx / heightPx : 0f;
    }

    /// <summary>Screen-space node hover for the custom cursor overlay: maps
    /// the real mouse position through the display collider to a node id
    /// (-1 = no node).</summary>
    public int HitTestFromScreenMouse()
    {
        if (_mainCamera == null || _display == null)
        {
            return -1;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return -1;
        }

        Ray ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (!_display.RaycastToLocal(ray, out Vector2 uv))
        {
            return -1;
        }

        return HitTestNode(UVToWristPixel(uv));
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
        if (open)
        {
            // Re-assert the stylesheet binding whenever the document comes up:
            // a document re-enabled after cloning must still carry its USS.
            EnsureStyleSheets();
            BindElements();
        }

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
                PoseDisplayFacingCamera();
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
            StartCoroutine(LogVisibilityAfterOpen());
        }
    }

    // ---- Runtime visibility protection (round 4) ----

    /// <summary>Waits until the wrist display has had a frame to come up, then
    /// logs a full visibility report. Any failing link is logged as an error
    /// so an invisible wrist is never silent.</summary>
    private IEnumerator LogVisibilityAfterOpen()
    {
        yield return new WaitForSecondsRealtime(0.35f);
        if (!IsWristOpenState())
        {
            yield break;
        }

        bool ok = VerifyDisplayVisible(out string report);
        if (ok)
        {
            Debug.Log("[WristVisibility] OK\n" + report + SampleAlphaReport());
        }
        else
        {
            Debug.LogError("[WristVisibility] FAIL\n" + report + SampleAlphaReport());
        }

        bool layoutOk = VerifyWristLayoutGeometry(out string layoutReport);
        if (layoutOk)
        {
            Debug.Log("[WristLayout] OK\n" + layoutReport);
        }
        else
        {
            Debug.LogError("[WristLayout] FAIL\n" + layoutReport);
        }
    }

    /// <summary>Best-effort alpha sampling (Release log only): the empty
    /// display region must be transparent and a real UI pixel opaque. Never
    /// throws; returns a short report.</summary>
    private string SampleAlphaReport()
    {
        if (_renderTexture == null || !_renderTexture.IsCreated())
        {
            return "ALPHA :: unavailable (RT not created)\n";
        }

        try
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            var readback = new Texture2D(UiWidth, UiHeight, TextureFormat.ARGB32, false);
            readback.ReadPixels(new Rect(0, 0, UiWidth, UiHeight), 0, 0, false);
            readback.Apply();
            float clearAlpha = readback.GetPixel(UiWidth - 50, UiHeight - 50).a;
            float uiAlpha = readback.GetPixel(640, 400).a;
            RenderTexture.active = previous;
            Object.Destroy(readback);
            return "ALPHA :: clear-region=" + clearAlpha.ToString("0.00")
                   + " (expect ~0) map-center=" + uiAlpha.ToString("0.00")
                   + " (expect >0)\n";
        }
        catch (System.Exception e)
        {
            return "ALPHA :: sampling failed: " + e.Message + "\n";
        }
    }

    private bool IsWristOpenState()
    {
        return _document != null && _document.enabled;
    }

    /// <summary>Checks every link of the wrist world-space display chain.
    /// Returns false (with a report) when any link is broken.</summary>
    public bool VerifyDisplayVisible(out string report)
    {
        var lines = new System.Text.StringBuilder();
        bool ok = true;

        void Check(bool pass, string label)
        {
            lines.AppendLine((pass ? "PASS" : "FAIL") + " :: " + label);
            ok &= pass;
        }

        Check(_renderTexture != null && _renderTexture.IsCreated()
              && _renderTexture.width > 0 && _renderTexture.height > 0,
            "RenderTexture created at " + (_renderTexture != null
                ? _renderTexture.width + "x" + _renderTexture.height : "<null>"));
        Check(_renderTexture != null && _renderTexture.format == RenderTextureFormat.ARGB32,
            "RenderTexture format carries alpha (actual: "
            + (_renderTexture != null ? _renderTexture.format.ToString() : "<null>") + ")");
        Check(_document != null && _document.enabled, "wrist UIDocument enabled");
        Check(panelSettings != null && panelSettings.targetTexture == _renderTexture,
            "PanelSettings.targetTexture bound to wrist RenderTexture");
        if (_display == null)
        {
            _display = Object.FindAnyObjectByType<WristDisplayAnchor>();
        }

        Check(_display != null, "WristDisplayAnchor present");
        if (_display != null)
        {
            Check(_display.SurfaceRenderer != null && _display.SurfaceRenderer.enabled,
                "wrist renderer enabled");
            Check(_display.HasTexture(_renderTexture),
                "wrist material references the wrist RenderTexture (name: "
                + (_display.SurfaceMaterial != null ? _display.SurfaceMaterial.name : "<null>") + ")");
            Check(_display.SurfaceMaterial != null
                  && (_display.SurfaceMaterial.renderQueue == (int)UnityEngine.Rendering.RenderQueue.Transparent
                      || (_display.SurfaceMaterial.shader != null
                          && _display.SurfaceMaterial.shader.name == "Survive/WristDisplayUnlit")),
                "wrist material uses transparent unlit (queue="
                + (_display.SurfaceMaterial != null
                    ? _display.SurfaceMaterial.renderQueue.ToString() : "<null>") + ")");
        }

        if (_mainCamera == null && Camera.main != null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera == null || _display == null)
        {
            Check(false, "camera available for viewport math");
        }
        else
        {
            Transform t = _display.transform;
            Vector3 camPos = _mainCamera.transform.position;
            Vector3 planePos = t.position;
            Vector3 toCamera = camPos - planePos;
            float distance = toCamera.magnitude;
            Check(distance > _mainCamera.nearClipPlane * 2f
                  && distance < _mainCamera.farClipPlane * 0.9f,
                "display in front of camera within clip (dist=" + distance.ToString("0.00") + "m)");

            // Unity Quad readable front is -transform.forward; it must point
            // back toward the camera so the UI is never mirrored.
            Vector3 frontNormal = _display.FrontNormal;
            float facing = Vector3.Dot(frontNormal, toCamera.normalized);
            Check(facing > 0.5f,
                "display front normal faces the camera (dot=" + facing.ToString("0.00") + ")");

            Vector3 viewport = _mainCamera.WorldToViewportPoint(planePos);
            Check(viewport.z > 0f
                  && viewport.x >= -0.05f && viewport.x <= 1.05f
                  && viewport.y >= -0.05f && viewport.y <= 1.05f,
                "display centre inside camera viewport (v=" + viewport.ToString("0.000"));

            // Projected bounds sanity: the wrist must cover only a moderate
            // share of the screen so the world stays visible around it.
            Vector3 scale = t.localScale;
            float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
            bool anyVisible = false;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector3 corner = planePos + t.right * (sx * scale.x * 0.5f)
                                     + t.up * (sy * scale.y * 0.5f);
                    Vector3 c = _mainCamera.WorldToViewportPoint(corner);
                    if (c.z > 0f && c.x >= 0f && c.x <= 1f && c.y >= 0f && c.y <= 1f)
                    {
                        anyVisible = true;
                    }

                    if (c.z > 0f)
                    {
                        minX = Mathf.Min(minX, c.x);
                        maxX = Mathf.Max(maxX, c.x);
                        minY = Mathf.Min(minY, c.y);
                        maxY = Mathf.Max(maxY, c.y);
                    }
                }
            }

            Check(anyVisible, "at least one projected display corner in view");
            float widthFraction = Mathf.Clamp01(maxX - minX);
            float heightFraction = Mathf.Clamp01(maxY - minY);
            Check(widthFraction > 0.03f && widthFraction <= 0.65f,
                "display width <=65% of screen (actual " + (widthFraction * 100f).ToString("0") + "%)");
            Check(heightFraction > 0.03f && heightFraction <= 0.70f,
                "display height <=70% of screen (actual " + (heightFraction * 100f).ToString("0") + "%)");

            // Round 6 pose guards: projected pixel aspect ~1280/800 = 1.6 and
            // a uniform scale chain (no parent non-uniform distortion).
            float widthPx = (maxX - minX) * Screen.width;
            float heightPx = (maxY - minY) * Screen.height;
            float aspectPx = heightPx > 1f ? widthPx / heightPx : 0f;
            Check(aspectPx >= 1.35f && aspectPx <= 1.85f,
                "projected aspect ~1.6 (px " + widthPx.ToString("0") + "x"
                + heightPx.ToString("0") + " aspect " + aspectPx.ToString("0.00") + ")");
            lines.AppendLine("INFO :: localScale=" + t.localScale.ToString("0.000")
                             + " lossyScale=" + t.lossyScale.ToString("0.000")
                             + " parentLossy="
                             + (t.parent != null ? t.parent.lossyScale.ToString("0.000") : "<root>")
                             + " projectedPx=" + widthPx.ToString("0") + "x"
                             + heightPx.ToString("0"));
            float scaleRatio = Mathf.Abs(scale.x / scale.y - (1280f / 800f));
            Check(scaleRatio < 0.02f,
                "display local scale keeps 1280:800 aspect (x/y ratio "
                + (scale.x / scale.y).ToString("0.00") + ")");
        }

        report = lines.ToString();
        return ok;
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

    /// <summary>Ring segments can be lost when the UIDocument re-instantiates
    /// its tree on enable toggles; reconcile by rebuilding into the current
    /// ring containers whenever they come up empty.</summary>
    private void EnsureRingTicksCurrent()
    {
        if (_satietyRing == null || _healthRing == null)
        {
            BindElements();
        }

        if (_satietyRing == null || _healthRing == null)
        {
            return;
        }

        if (_satietyRing.childCount == 0 && _healthRing.childCount == 0)
        {
            _ticksBuilt = false;
            _satietyTicks.Clear();
            _healthTicks.Clear();
            BuildRingTicks();
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
