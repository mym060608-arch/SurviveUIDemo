using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Wrist terminal (Phase 06 Revision CodeReference architecture).
///
/// The wrist UXML renders into a fixed 1280 x 800 RenderTexture shown on a
/// tilted world plane (FirstPersonCamera -> WristProjectionPivot ->
/// WristDisplay). All radial geometry - translucent local map, thin health /
/// satiety arcs and the four thick functional annular sectors - is painted by
/// a single RadialWristElement triangle mesh (no Painter2D, no tick fields,
/// no cards, no one-full outer functional ring, no white MapDisc).
///
/// Coordinates:
/// - World -> map: single authoritative WristMapCoordinates.WorldToWristMap()
///   returns absolute wrist pixels shared by draw, hover and click.
/// - Display UV -> wrist pixel: single authoritative UVToWristPixel() (one
///   Y inversion only).
///
/// Cursor: WristCursorService assigns runtime ring textures through
/// Cursor.SetCursor while WristMode is open. There is deliberately NO
/// UI Toolkit cursor overlay / trail anywhere.
///
/// Parallax: the hologram tilts around the WristProjectionPivot (localRotation
/// on a captured base pose), clamped to yaw +-4 / pitch +-3 / roll +-0.5 deg,
/// damped; it never translates around the screen.
/// </summary>
public class SurfaceWristController : MonoBehaviour
{
    // Fixed logical design space (CodeReference). ConstantPixelSize panel.
    public const float UiWidth = RadialWristElement.DesignWidth;
    public const float UiHeight = RadialWristElement.DesignHeight;

    /// <summary>Hit radius around a node marker used by HitTestNode (CodeReference).</summary>
    public const float NodeHitRadiusPx = 28f;

    private const float ParallaxYawLimit = 4f;
    private const float ParallaxPitchLimit = 3f;
    private const float ParallaxRollLimit = 0.5f;
    private const float ParallaxDamping = 0.16f;
    private const float CompletionHoldSeconds = 4f;

    [SerializeField]
    public StyleSheet wristStyleSheet;

    [SerializeField]
    public PanelSettings panelSettings;

    // ---- RenderTexture document ----
    private UIDocument _document;
    private RenderTexture _renderTexture;
    private VisualElement _root;

    // ---- Bound elements (CodeReference names) ----
    private VisualElement _geometryHost;
    private Label _playerMarker;
    private VisualElement _landingMarker;
    private readonly VisualElement[] _nodeMarkers = new VisualElement[GameSession.SurfaceNodeCount];
    private VisualElement _selectedRing;

    private Label _healthLabel;
    private Label _satietyLabel;
    private Label _equipHarvester;
    private Label _equipBattery;
    private Label _equipDrone;
    private Label _actionState;
    private Label _recoveredState;
    private Label _navTarget;
    private Label _navDistance;
    private Label _harvestNode;
    private Label _harvestState;

    private RadialWristElement _radial;
    private float _lastRadialHealth = -1f;
    private float _lastRadialSatiety = -1f;

    // ---- World display chain ----
    private WristDisplayAnchor _display;
    private Transform _projectionPivot;
    private Quaternion _basePivotLocalRotation = Quaternion.identity;
    private Camera _mainCamera;
    private Coroutine _animator;

    private float _tiltYaw = 5f;
    private float _tiltPitch = 3f;
    private float _tiltRoll = -2f;

    // ---- Parallax (damped; clamped; pivot local rotation only) ----
    private Vector2 _parallaxTarget;
    private float _parallaxYaw;
    private float _parallaxPitch;
    private float _parallaxRoll;

    // ---- Cursor service (Cursor.SetCursor ring textures) ----
    private WristCursorService _cursorService;

    // ---- State bindings ----
    private SurfaceGameController _controller;
    private GameSession _session;
    private SurfaceResourceNode _completedNode;
    private float _completedAt;
    private bool _bound;
    private int _hoverNodeId = -1;

    /// <summary>Exposed for tests: the wrist document's root element.</summary>
    public VisualElement RootElement => _root;

    /// <summary>The wrist RenderTexture (exposed for visibility assertions).</summary>
    public RenderTexture CurrentRenderTexture => _renderTexture;

    /// <summary>Wrist display anchor (exposed for visibility assertions).</summary>
    public WristDisplayAnchor Display => _display;

    /// <summary>WristProjectionPivot under the first-person camera; parallax
    /// rotates this transform only (CodeReference hierarchy).</summary>
    public Transform ProjectionPivot => _projectionPivot;

    /// <summary>Cursor authority while the wrist terminal is open.</summary>
    public WristCursorService CursorService => _cursorService;

    public float CurrentParallaxYaw => _parallaxYaw;
    public float CurrentParallaxPitch => _parallaxPitch;
    public float CurrentParallaxRoll => _parallaxRoll;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _cursorService = new WristCursorService();
        _renderTexture = new RenderTexture((int)UiWidth, (int)UiHeight, 0, RenderTextureFormat.ARGB32);
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
        RestoreDefaultCursor();
        if (_cursorService != null)
        {
            _cursorService.Dispose();
            _cursorService = null;
        }

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
        RefreshNavigation();
        SetOpen(false);
    }

    private void OnDisable()
    {
        OnDestroyUnsubscribe();
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

    // ====================================================================
    // Style sheet / binding helpers
    // ====================================================================

    /// <summary>Adds SurfaceWrist.uss to the wrist document root exactly once.</summary>
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

    private void BindElements()
    {
        if (_document == null || _document.rootVisualElement == null)
        {
            return;
        }

        _root = _document.rootVisualElement;
        EnsureStyleSheets();
        ApplyUiCjkFont();
        EnsureRadialVisual();

        _geometryHost = _root.Q<VisualElement>("RadialGeometryHost");
        _playerMarker = _root.Q<Label>("MapPlayer");
        _landingMarker = _root.Q<VisualElement>("MapLanding");
        _selectedRing = _root.Q<VisualElement>("MapSelectedRing");
        for (int i = 0; i < _nodeMarkers.Length; i++)
        {
            _nodeMarkers[i] = _root.Q<VisualElement>("MapNode" + i);
        }

        _healthLabel = _root.Q<Label>("HealthLabel");
        _satietyLabel = _root.Q<Label>("SatietyLabel");
        _equipHarvester = _root.Q<Label>("EquipHarvester");
        _equipBattery = _root.Q<Label>("EquipBattery");
        _equipDrone = _root.Q<Label>("EquipDrone");
        _actionState = _root.Q<Label>("ActionState");
        _recoveredState = _root.Q<Label>("RecoveredState");
        _navTarget = _root.Q<Label>("NavTarget");
        _navDistance = _root.Q<Label>("NavDistance");
        _harvestNode = _root.Q<Label>("HarvestNode");
        _harvestState = _root.Q<Label>("HarvestState");

        _bound = _geometryHost != null && _playerMarker != null;
    }

    /// <summary>Inserts the single RadialWristElement mesh into the geometry
    /// host (CodeReference: never rely on a custom UXML factory).</summary>
    private void EnsureRadialVisual()
    {
        if (_root == null)
        {
            return;
        }

        VisualElement host = _root.Q<VisualElement>("RadialGeometryHost");
        if (host == null)
        {
            Debug.LogError("[Wrist] Missing RadialGeometryHost.");
            return;
        }

        if (_radial != null && _radial.parent == host)
        {
            return;
        }

        host.Clear();

        _radial = new RadialWristElement();
        host.Add(_radial);
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

    // ====================================================================
    // Runtime layout / geometry verification (tests + open logging)
    // ====================================================================

    /// <summary>Runtime layout report for the wrist document: resolves the
    /// geometry host, markers and sector text geometry; returns false when any
    /// measured geometry is NaN/zero/degenerate or text sits outside its
    /// intended radial sector (CodeReference section 26).</summary>
    public bool VerifyWristLayoutGeometry(out string report)
    {
        var lines = new StringBuilder();
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

        VisualElement host = root.Q<VisualElement>("RadialGeometryHost");
        Check(host != null, "RadialGeometryHost element resolved");
        EnsureRadialVisual();
        Check(_radial != null && _radial.parent == host, "RadialWristElement present in geometry host");
        if (_radial != null)
        {
            lines.AppendLine("INFO :: radial mapR=" + RadialWristElement.MapRadius
                             + " thinArcR=" + RadialWristElement.ThinArcRadius
                             + " sectorR=" + RadialWristElement.SectorInnerRadius
                             + ".." + RadialWristElement.SectorOuterRadius
                             + " health=" + (_radial.HealthFraction01 * 100f).ToString("0")
                             + "% satiety=" + (_radial.SatietyFraction01 * 100f).ToString("0")
                             + "% (center " + RadialWristElement.Center.x + ","
                             + RadialWristElement.Center.y + ")");
            Check(_radial.HealthFraction01 > 0f, "Health arc fraction driven");
            Check(_radial.SatietyFraction01 > 0f, "Satiety arc fraction driven");
        }

        VisualElement markerLayer = root.Q<VisualElement>("MapMarkerLayer");
        Check(markerLayer != null, "MapMarkerLayer element resolved");
        Check(root.Q<Label>("MapPlayer") != null, "MapPlayer marker resolved");
        Check(root.Q<VisualElement>("MapLanding") != null, "MapLanding marker resolved");
        Check(root.Q<VisualElement>("MapNode0") != null, "MapNode0 marker resolved");
        Check(root.Q<VisualElement>("MapNode1") != null, "MapNode1 marker resolved");
        Check(root.Q<VisualElement>("MapNode2") != null, "MapNode2 marker resolved");
        Check(root.Q<VisualElement>("MapSelectedRing") != null, "MapSelectedRing marker resolved");

        // Status labels sit near the thin upper/lower arcs.
        Label healthLabel = root.Q<Label>("HealthLabel");
        Label satietyLabel = root.Q<Label>("SatietyLabel");
        if (healthLabel != null && satietyLabel != null)
        {
            Check(healthLabel.resolvedStyle.top < 180f, "health label near upper arc");
            Check(satietyLabel.resolvedStyle.top > 560f, "satiety label near lower arc");
        }

        // Sector text geometry: each content centre must lie inside its
        // intended thick annular sector (CodeReference section 26).
        Check(TextInsideSector(root.Q<VisualElement>("EquipContent"),
            RadialWristElement.EquipmentSectorStartDeg, RadialWristElement.EquipmentSectorEndDeg),
            "equipment text centre inside Equipment sector (135..190)");
        Check(TextInsideSector(root.Q<VisualElement>("NavContent"),
            RadialWristElement.NavigationSectorStartDeg, RadialWristElement.NavigationSectorEndDeg),
            "navigation text centre inside Navigation sector (195..235)");
        Check(TextInsideSector(root.Q<VisualElement>("StateContent"),
            RadialWristElement.StateSectorStartDeg, RadialWristElement.StateSectorEndDeg),
            "state text centre inside State sector (350..405)");
        Check(TextInsideSector(root.Q<VisualElement>("HarvestContent"),
            RadialWristElement.HarvestSectorStartDeg, RadialWristElement.HarvestSectorEndDeg),
            "harvest text centre inside Harvest sector (305..345)");

        report = lines.ToString();
        return ok;
    }

    /// <summary>True when the element's resolved centre lies within the radial
    /// band [SectorInnerRadius .. SectorOuterRadius] and inside the given
    /// angular range (with tolerance for the element extent).</summary>
    private static bool TextInsideSector(VisualElement content, float startDeg, float endDeg)
    {
        if (content == null)
        {
            return false;
        }

        float left = content.resolvedStyle.left;
        float top = content.resolvedStyle.top;
        float width = content.resolvedStyle.width;
        float height = content.resolvedStyle.height;
        if (float.IsNaN(left) || float.IsNaN(top) || width <= 1f || height <= 1f)
        {
            return false;
        }

        Vector2 centre = new Vector2(left + width * 0.5f, top + height * 0.5f);
        Vector2 d = centre - RadialWristElement.Center;
        float radius = d.magnitude;
        float angle = RadialWristElement.WristAngleDeg(centre);

        float tolerance = 14f;
        float radiusTolerance = 40f;

        if (radius < RadialWristElement.SectorInnerRadius - radiusTolerance
            || radius > RadialWristElement.SectorOuterRadius + radiusTolerance)
        {
            return false;
        }

        float span = endDeg - startDeg;
        float angleOffset = Mathf.Repeat(angle - startDeg, 360f);
        return angleOffset <= span + tolerance;
    }

    private static string RootResolved(VisualElement root)
    {
        return root.resolvedStyle.width.ToString("0") + "x"
               + root.resolvedStyle.height.ToString("0");
    }

    // ====================================================================
    // Wrist mode open / close
    // ====================================================================

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
            EnsureStyleSheets();
            BindElements();
            EnsureDisplayReference();
            if (_display != null)
            {
                _display.Show(true);
                _display.AttachTexture(_renderTexture);
                PoseDisplayFacingCamera();
                _animator = StartCoroutine(AnimateOpen());
            }

            RefreshSessionData();
            RefreshNavigation();
            RefreshMapGeometry();
            RefreshActiveProgress();
            StartCoroutine(LogVisibilityAfterOpen());
        }
        else
        {
            EnsureDisplayReference();
            if (_display != null)
            {
                _display.Show(false);
            }

            if (_projectionPivot != null)
            {
                _projectionPivot.localRotation = _basePivotLocalRotation;
            }

            _parallaxYaw = 0f;
            _parallaxPitch = 0f;
            _parallaxRoll = 0f;
            _parallaxTarget = Vector2.zero;
            if (_cursorService != null)
            {
                _cursorService.SetHover(false);
            }
        }
    }

    /// <summary>Enter WristMode cursor state (Cursor.SetCursor ring).</summary>
    public void EnterWristCursor()
    {
        if (_cursorService != null)
        {
            _cursorService.EnterWristMode();
        }
    }

    /// <summary>Exit to the first-person surface cursor state (locked + hidden
    /// OS cursor, default cursor restored internally).</summary>
    public void ExitToSurfaceCursor()
    {
        if (_cursorService != null)
        {
            _cursorService.ExitToSurface();
        }
    }

    private void RestoreDefaultCursor()
    {
        UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        _hoverNodeId = -1;
    }

    private void EnsureDisplayReference()
    {
        if (_display == null)
        {
            _display = Object.FindAnyObjectByType<WristDisplayAnchor>();
        }

        if (_display != null && _projectionPivot == null)
        {
            _projectionPivot = _display.transform.parent;
        }
    }

    // ---- Runtime visibility protection (kept from round 4) ----

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
            var readback = new Texture2D((int)UiWidth, (int)UiHeight, TextureFormat.ARGB32, false);
            readback.ReadPixels(new Rect(0, 0, UiWidth, UiHeight), 0, 0, false);
            readback.Apply();
            float clearAlpha = readback.GetPixel((int)UiWidth - 50, (int)UiHeight - 50).a;
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

    /// <summary>Checks every link of the wrist world-space display chain.</summary>
    public bool VerifyDisplayVisible(out string report)
    {
        var lines = new StringBuilder();
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

        EnsureDisplayReference();

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

        Check(_projectionPivot != null, "WristProjectionPivot present above the display");
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
            Transform t = _projectionPivot != null ? _projectionPivot : _display.transform;
            Vector3 camPos = _mainCamera.transform.position;
            Vector3 planePos = t.position;
            Vector3 toCamera = camPos - planePos;
            float distance = toCamera.magnitude;
            Check(distance > _mainCamera.nearClipPlane * 2f
                  && distance < _mainCamera.farClipPlane * 0.9f,
                "display in front of camera within clip (dist=" + distance.ToString("0.00") + "m)");

            Vector3 frontNormal = _display.FrontNormal;
            float facing = Vector3.Dot(frontNormal, toCamera.normalized);
            Check(facing > 0.5f,
                "display front normal faces the camera (dot=" + facing.ToString("0.00") + ")");

            Vector3 viewport = _mainCamera.WorldToViewportPoint(planePos);
            Check(viewport.z > 0f
                  && viewport.x >= -0.05f && viewport.x <= 1.05f
                  && viewport.y >= -0.05f && viewport.y <= 1.05f,
                "display centre inside camera viewport (v=" + viewport.ToString("0.000"));

            Transform displayTransform = _display.transform;
            Vector3 scale = displayTransform.localScale;
            float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
            bool anyVisible = false;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector3 corner = planePos + displayTransform.right * (sx * scale.x * 0.5f)
                                     + displayTransform.up * (sy * scale.y * 0.5f);
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

            float widthPx = (maxX - minX) * Screen.width;
            float heightPx = (maxY - minY) * Screen.height;
            float aspectPx = heightPx > 1f ? widthPx / heightPx : 0f;
            Check(aspectPx >= 1.35f && aspectPx <= 1.85f,
                "projected aspect ~1.6 (px " + widthPx.ToString("0") + "x"
                + heightPx.ToString("0") + " aspect " + aspectPx.ToString("0.00") + ")");
            float scaleRatio = Mathf.Abs(scale.x / scale.y - (1280f / 800f));
            Check(scaleRatio < 0.02f,
                "display local scale keeps 1280:800 aspect (x/y ratio "
                + (scale.x / scale.y).ToString("0.00") + ")");
        }

        report = lines.ToString();
        return ok;
    }

    // ====================================================================
    // Pose + parallax (CodeReference: rotate the WristProjectionPivot only)
    // ====================================================================

    /// <summary>Orients the display front at the camera through the projection
    /// pivot with a small raised-wrist tilt, then captures the pivot base pose.
    /// Automatically reduces the tilt if the projected aspect would collapse.</summary>
    public void PoseDisplayFacingCamera()
    {
        EnsureDisplayReference();
        if (_display == null || _projectionPivot == null || _mainCamera == null)
        {
            return;
        }

        Transform pivot = _projectionPivot;
        Transform camera = _mainCamera.transform;
        Vector3 toCamera = camera.position - pivot.position;
        if (toCamera.sqrMagnitude < 1e-6f)
        {
            return;
        }

        // pivot.forward must point AWAY from the camera so the Quad front
        // (-forward) faces it, aligned to camera up.
        Quaternion frontFacing = Quaternion.LookRotation(-toCamera.normalized, camera.up);
        float safeYaw = _tiltYaw;
        float safePitch = _tiltPitch;
        float safeRoll = _tiltRoll;
        int guard = 0;
        while (guard < 3)
        {
            pivot.rotation = frontFacing * Quaternion.Euler(safePitch, safeYaw, safeRoll);
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
        _basePivotLocalRotation = pivot.localRotation;
        _parallaxYaw = 0f;
        _parallaxPitch = 0f;
        _parallaxRoll = 0f;
        _parallaxTarget = Vector2.zero;
        Debug.Log("[WristVisibility] tilt yaw=" + _tiltYaw + " pitch=" + _tiltPitch
                  + " roll=" + _tiltRoll + " aspect="
                  + ComputeProjectedAspectPx().ToString("0.00"));
    }

    /// <summary>Projected display aspect in screen pixels (width/height).</summary>
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

    /// <summary>Sets the normalized pointer (-1..1) that drives the small
    /// hologram tilt around the wrist pivot; tests call it directly (no
    /// hardware mouse involved). +x = right, +y = top.</summary>
    public void SetParallaxNormalized(float x, float y)
    {
        _parallaxTarget = new Vector2(Mathf.Clamp(x, -1f, 1f),
            Mathf.Clamp(y, -1f, 1f));
    }

    private void UpdateParallaxFromMouse()
    {
        if (_projectionPivot == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 pos = mouse.position.ReadValue();
            float nx = Mathf.Clamp((pos.x / Mathf.Max(1, Screen.width) - 0.5f) * 2f, -1f, 1f);
            float ny = Mathf.Clamp((pos.y / Mathf.Max(1, Screen.height) - 0.5f) * 2f, -1f, 1f);
            SetParallaxNormalized(nx, ny);
        }

        float t = Mathf.Clamp01(Time.deltaTime / ParallaxDamping);
        float targetYaw = _parallaxTarget.x * ParallaxYawLimit;
        float targetPitch = _parallaxTarget.y * ParallaxPitchLimit;
        float targetRoll = _parallaxTarget.x * ParallaxRollLimit;
        _parallaxYaw = Mathf.Lerp(_parallaxYaw, targetYaw, t);
        _parallaxPitch = Mathf.Lerp(_parallaxPitch, targetPitch, t);
        _parallaxRoll = Mathf.Lerp(_parallaxRoll, targetRoll, t);

        Quaternion offset = Quaternion.Euler(_parallaxPitch, _parallaxYaw, _parallaxRoll);
        _projectionPivot.localRotation = _basePivotLocalRotation * offset;
    }

    // ====================================================================
    // Per-frame tick (driven by SurfaceGameController)
    // ====================================================================

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
            RefreshNavigation();
        }

        if (_document == null || !_document.enabled)
        {
            return;
        }

        EnsureStyleSheets();
        RefreshMapGeometry();
        RefreshActiveProgress();
        UpdateParallaxFromMouse();
        ProcessHardwarePointer();
    }

    // ---- Hardware pointer (real mouse -> display ray -> wrist pixel) ----

    /// <summary>Screen-space node hover for tests/probes: maps the real mouse
    /// position through the display collider to a node id (-1 = none).</summary>
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

    private void ProcessHardwarePointer()
    {
        if (_controller == null || _mainCamera == null || _display == null)
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
            _hoverNodeId = -1;
            if (_cursorService != null)
            {
                _cursorService.SetHover(false);
            }

            return;
        }

        Vector2 wristPixel = UVToWristPixel(uv);
        int hit = HitTestNode(wristPixel);
        _hoverNodeId = hit;
        if (_cursorService != null)
        {
            _cursorService.SetHover(hit >= 0);
        }

        if (hit >= 0 && mouse.leftButton.wasPressedThisFrame)
        {
            // Real hardware left click -> deterministic hit-test -> single
            // authoritative selector.
            _controller.TrySelectNavigationTarget(hit);
        }
    }

    /// <summary>Display UV (0..1, bottom-left origin) to wrist logical pixel
    /// (UI y down). One authoritative Y inversion only.</summary>
    public static Vector2 UVToWristPixel(Vector2 uv)
    {
        return new Vector2(
            Mathf.Clamp01(uv.x) * UiWidth,
            (1f - Mathf.Clamp01(uv.y)) * UiHeight);
    }

    /// <summary>Deterministic wrist-pixel node hit-test. Returns the node id
    /// or -1. Draw position and hit-test share WristMapCoordinates so marker
    /// and hit geometry can never drift apart; Depleted nodes are skipped.</summary>
    public int HitTestNode(Vector2 wristPixel)
    {
        if (_controller == null)
        {
            return -1;
        }

        float hitRadiusSq = NodeHitRadiusPx * NodeHitRadiusPx;
        for (int i = 0; i < _controller.Nodes.Count; i++)
        {
            SurfaceResourceNode node = _controller.GetNodeById(i);
            if (node == null || node.State == SurfaceResourceNode.NodeState.Depleted)
            {
                continue;
            }

            Vector2 nodePixel = WristMapCoordinates.WorldToWristMap(node.transform.position);
            if ((wristPixel - nodePixel).sqrMagnitude <= hitRadiusSq)
            {
                return i;
            }
        }

        return -1;
    }

    // ====================================================================
    // Live data refresh
    // ====================================================================

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

        if (!IsWristOpenState())
        {
            return;
        }

        RefreshMapGeometry();
        RefreshActiveProgress();
    }

    private void HandleNodeSettled(SurfaceResourceNode node, int yield)
    {
        _completedNode = node;
        _completedAt = Time.time;
        if (IsWristOpenState())
        {
            RefreshActiveProgress();
        }
    }

    private void HandleWristOpenChanged(bool open)
    {
        // SetOpen is applied by the controller already.
    }

    private void RefreshRadialStatus()
    {
        if (_radial == null || _session == null)
        {
            return;
        }

        float health = Mathf.Clamp01(_session.PlayerHealth / 100f);
        float satiety = Mathf.Clamp01(_session.PlayerSatiety / 100f);
        if (Mathf.Abs(health - _lastRadialHealth) < 0.001f
            && Mathf.Abs(satiety - _lastRadialSatiety) < 0.001f)
        {
            return;
        }

        _lastRadialHealth = health;
        _lastRadialSatiety = satiety;
        _radial.SetHealth(health);
        _radial.SetSatiety(satiety);
    }

    private void RefreshSessionData()
    {
        if (_session == null)
        {
            return;
        }

        RefreshRadialStatus();

        if (_healthLabel != null)
        {
            _healthLabel.text = "健康度 " + _session.PlayerHealth;
        }

        if (_satietyLabel != null)
        {
            _satietyLabel.text = "饱食度 " + _session.PlayerSatiety;
        }

        if (_equipHarvester != null)
        {
            SetEquipState(_equipHarvester, "采集器 MK-I", false);
        }

        if (_equipBattery != null)
        {
            SetEquipState(_equipBattery, "备用电池组", !_session.BatterySelected);
        }

        if (_equipDrone != null)
        {
            SetEquipState(_equipDrone, "勘测无人机", !_session.DroneSelected);
        }

        SurfaceResourceNode active = _controller != null ? _controller.ActiveNode : null;
        if (_actionState != null)
        {
            _actionState.text = active != null
                ? (active.State == SurfaceResourceNode.NodeState.Harvesting ? "采集中" : "无线传输")
                : "勘测中";
        }

        if (_recoveredState != null)
        {
            _recoveredState.text = "回收节点 " + _session.HarvestedNodeCount
                                   + " / " + GameSession.SurfaceNodeCount;
        }
    }

    private static void SetEquipState(Label label, string name, bool off)
    {
        label.text = name;
        if (off)
        {
            label.AddToClassList("sector-off");
        }
        else
        {
            label.RemoveFromClassList("sector-off");
        }
    }

    // ---- Circular map (single shared coordinate source) ----

    private void RefreshMapGeometry()
    {
        if (_controller == null || _controller.Player == null || _root == null)
        {
            return;
        }

        if (_playerMarker != null)
        {
            Vector3 playerPos = _controller.Player.transform.position;
            SetMarkerPosition(_playerMarker, WristMapCoordinates.WorldToWristMap(playerPos));
            _playerMarker.style.rotate = new Rotate(new Angle(-_controller.Player.YawDegrees));
        }

        if (_landingMarker != null)
        {
            SetMarkerPosition(_landingMarker, WristMapCoordinates.WorldToWristMap(Vector3.zero));
        }

        for (int i = 0; i < _nodeMarkers.Length; i++)
        {
            if (_nodeMarkers[i] == null)
            {
                continue;
            }

            SurfaceResourceNode node = _controller.GetNodeById(i);
            if (node == null)
            {
                continue;
            }

            SetMarkerPosition(_nodeMarkers[i], WristMapCoordinates.WorldToWristMap(node.transform.position));
            ApplyMarkerState(_nodeMarkers[i], node, _controller.NavigationTargetNodeId == i);
        }

        if (_selectedRing != null)
        {
            if (_controller.IsNavigationActive)
            {
                SurfaceResourceNode target = _controller.GetNodeById(_controller.NavigationTargetNodeId);
                if (target != null)
                {
                    SetMarkerPosition(_selectedRing, WristMapCoordinates.WorldToWristMap(target.transform.position));
                    _selectedRing.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _selectedRing.style.display = DisplayStyle.None;
                }
            }
            else
            {
                _selectedRing.style.display = DisplayStyle.None;
            }
        }
    }

    private static void SetMarkerPosition(VisualElement marker, Vector2 wristPixel)
    {
        if (marker == null)
        {
            return;
        }

        marker.style.left = wristPixel.x;
        marker.style.top = wristPixel.y;
    }

    private static void ApplyMarkerState(VisualElement marker,
        SurfaceResourceNode node, bool selected)
    {
        marker.RemoveFromClassList("node-depleted");
        marker.RemoveFromClassList("node-active");
        marker.RemoveFromClassList("node-transmitting");
        marker.RemoveFromClassList("node-selected");

        switch (node.State)
        {
            case SurfaceResourceNode.NodeState.Harvesting:
                marker.AddToClassList("node-active");
                break;
            case SurfaceResourceNode.NodeState.Transmitting:
                marker.AddToClassList("node-transmitting");
                marker.AddToClassList("node-active");
                break;
            case SurfaceResourceNode.NodeState.Depleted:
                marker.AddToClassList("node-depleted");
                break;
        }

        if (selected && node.State != SurfaceResourceNode.NodeState.Depleted)
        {
            marker.AddToClassList("node-selected");
        }
    }

    // ---- Live active harvest context ----

    private void RefreshActiveProgress()
    {
        if (_harvestNode == null || _harvestState == null || _controller == null)
        {
            return;
        }

        SurfaceResourceNode active = _controller.ActiveNode;
        bool showCompletion = active == null && _completedNode != null
                              && Time.time - _completedAt < CompletionHoldSeconds;
        if (active != null)
        {
            _harvestNode.text = SurfaceGameController.NodeDisplayName(active.NodeId);
            float progress = 1f - Mathf.Clamp01(active.RemainingSeconds /
                Mathf.Max(0.001f, active.StageSeconds));
            string status = active.State == SurfaceResourceNode.NodeState.Harvesting
                ? "采集中 " + Mathf.RoundToInt(progress * 100f) + "%"
                : "无线传输 " + Mathf.RoundToInt(progress * 100f) + "%";
            _harvestState.text = status;
        }
        else if (showCompletion)
        {
            _harvestNode.text = SurfaceGameController.NodeDisplayName(_completedNode.NodeId);
            _harvestState.text = "传输完成";
        }
        else
        {
            _harvestNode.text = "—";
            _harvestState.text = "待机";
        }
    }

    private void RefreshNavigation()
    {
        if (_controller == null)
        {
            return;
        }

        if (_navTarget != null)
        {
            _navTarget.text = _controller.IsNavigationActive
                ? SurfaceGameController.NodeDisplayName(_controller.NavigationTargetNodeId)
                : "未设置";
        }

        if (_navDistance != null)
        {
            _navDistance.text = _controller.IsNavigationActive
                ? (_controller.NavigationReached
                    ? "目标已到达"
                    : _controller.NavigationDistance.ToString("0.0") + " m")
                : "";
        }

        if (_selectedRing != null)
        {
            _selectedRing.style.display = _controller.IsNavigationActive
                ? DisplayStyle.Flex
                : DisplayStyle.None;
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
}
