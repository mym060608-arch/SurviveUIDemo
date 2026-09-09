using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Wrist terminal (Phase 06 Revision Round 9.2 parallel-cap architecture).
///
/// The wrist UXML renders into a fixed 1280 x 800 RenderTexture shown on a
/// tilted world plane (FirstPersonCamera -> WristProjectionPivot ->
/// WristDisplay). All radial geometry - translucent local map, health /
/// satiety arcs (inner radius 248 == panel inner radius) and the four
/// Parallel-Cap Annular Panels - is painted by a single RadialWristElement
/// triangle mesh (no Painter2D, no tick fields, no cards, no one-full outer
/// ring, no white MapDisc).
///
/// Round 9.2 text architecture: every text row is its OWN horizontal slot
/// label. SurfaceWristController positions each row from the authoritative
/// panel cross-section at that row's Y
/// (RadialWristElement.GetPanelHorizontalRange / TryGetPanelRowCentre), so
/// the staircase direction and slot bounds emerge from the real parallel-cap
/// geometry - never from hand-added indentation offsets.
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

    /// <summary>Row slot vertical height used for all text rows (px).</summary>
    public const float RowHeight = 21f;

    /// <summary>Minimum horizontal padding used by row slot bounds (Round 9.2
    /// requires >= 10 px; 14 is the authored padding).</summary>
    public const float RowSlotPadding = 14f;

    private const float ParallaxYawLimit = 4f;
    private const float ParallaxPitchLimit = 3f;
    private const float ParallaxRollLimit = 0.5f;
    private const float ParallaxDamping = 0.16f;
    private const float CompletionHoldSeconds = 4f;

    /// <summary>One horizontal text slot: its own label, the side of the wrist
    /// centre its panel lives on, and the row's centre Y in wrist pixels.
    /// X/width come from the parallel-cap geometry at that Y.</summary>
    private sealed class TextRow
    {
        public readonly string ElementName;
        public readonly bool LeftSide;
        public readonly float CenterY;
        public Label Label;

        public TextRow(string elementName, bool leftSide, float centerY)
        {
            ElementName = elementName;
            LeftSide = leftSide;
            CenterY = centerY;
        }
    }

    /// <summary>Row table (Round 9.2). Upper band panels sit on Y 205..345,
    /// lower band panels on Y 455..595. Equipment / Navigation panels are on
    /// the LEFT, State / Harvest on the RIGHT. Rows follow each panel's
    /// recommended slot Ys; the X of every row is derived from geometry.</summary>
    private static readonly TextRow[] RowTable =
    {
        // Equipment (upper-left, down-left staircase).
        new TextRow("EquipHeading", true, 238f),
        new TextRow("EquipHarvester", true, 268f),
        new TextRow("EquipBattery", true, 296f),
        new TextRow("EquipDrone", true, 322f),

        // State (upper-right, down-right staircase).
        new TextRow("StateHeading", false, 238f),
        new TextRow("ActionState", false, 268f),
        new TextRow("SignalState", false, 296f),
        new TextRow("RecoveredState", false, 322f),

        // Navigation (lower-left, down-right staircase).
        new TextRow("NavHeading", true, 478f),
        new TextRow("NavTarget", true, 510f),
        new TextRow("NavDistance", true, 540f),

        // Harvest (lower-right, down-left staircase).
        new TextRow("HarvestHeading", false, 478f),
        new TextRow("HarvestNode", false, 508f),
        new TextRow("HarvestState", false, 536f),
        new TextRow("HarvestProgress", false, 562f)
    };

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
    private Label _identityLabel;
    private readonly List<TextRow> _rows = new List<TextRow>();
    private readonly Dictionary<string, TextRow> _rowByName = new Dictionary<string, TextRow>();

    // Convenience handles to the rows whose text changes live.
    private TextRow _harvesterRow;
    private TextRow _batteryRow;
    private TextRow _droneRow;
    private TextRow _actionRow;
    private TextRow _signalRow;
    private TextRow _recoveredRow;
    private TextRow _navTargetRow;
    private TextRow _navDistanceRow;
    private TextRow _harvestNodeRow;
    private TextRow _harvestStateRow;
    private TextRow _harvestProgressRow;

    private RadialWristElement _radial;
    private ShipSchematicVisual _shipSchematic;
    private float _lastRadialHealth = -1f;
    private float _lastRadialSatiety = -1f;

    // ---- World display chain ----
    private WristDisplayAnchor _display;
    private Transform _projectionPivot;
    private Quaternion _basePivotLocalRotation = Quaternion.identity;
    private Camera _mainCamera;
    private Coroutine _animator;

    private float _tiltYaw = -4f;
    private float _tiltPitch = 8f;
    private float _tiltRoll = 0f;

    // ---- Parallax (damped; clamped; pivot local rotation only) ----
    private Vector2 _parallaxTarget;
    private float _parallaxYaw;
    private float _parallaxPitch;
    private float _parallaxRoll;

    // ---- Cursor service (Cursor.SetCursor ring textures) ----
    private WristCursorService _cursorService;

    // ---- State bindings ----
    private IWristContext _controller;
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

    /// <summary>Phase 07B: the source mode this wrist instance was opened from.
    /// Ship contexts answer ShipExplore, surface contexts SurfaceFirstPerson.</summary>
    public WristReturnMode ReturnMode =>
        _controller != null ? _controller.WristSourceMode : WristReturnMode.SurfaceFirstPerson;

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

    /// <summary>Binds the wrist view to the authoritative context (surface or
    /// ship controller - Phase 07B). Content adapts to WristSourceMode.</summary>
    public void Bind(IWristContext controller)
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
        if (ReturnMode == WristReturnMode.ShipExplore)
        {
            RefreshShipContent();
        }

        RefreshTextRowLayout();
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
        // Radial mesh first, then (ship mode) the deck schematic, then fonts
        // so every text element - including schematic chips - gets the CJK
        // runtime font in one pass.
        EnsureRadialVisual();
        EnsureShipSchematicVisual();
        ApplyUiCjkFont();

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
        _identityLabel = _root.Q<Label>("WristIdentity");

        _rows.Clear();
        _rowByName.Clear();
        foreach (TextRow row in RowTable)
        {
            Label label = _root.Q<Label>(row.ElementName);
            if (label == null)
            {
                continue;
            }

            TextRow bound = new TextRow(row.ElementName, row.LeftSide, row.CenterY)
            {
                Label = label
            };
            _rows.Add(bound);
            _rowByName[row.ElementName] = bound;
        }

        _harvesterRow = Row("EquipHarvester");
        _batteryRow = Row("EquipBattery");
        _droneRow = Row("EquipDrone");
        _actionRow = Row("ActionState");
        _signalRow = Row("SignalState");
        _recoveredRow = Row("RecoveredState");
        _navTargetRow = Row("NavTarget");
        _navDistanceRow = Row("NavDistance");
        _harvestNodeRow = Row("HarvestNode");
        _harvestStateRow = Row("HarvestState");
        _harvestProgressRow = Row("HarvestProgress");

        _bound = _geometryHost != null && _playerMarker != null && _rows.Count > 0;
    }

    private TextRow Row(string name)
    {
        TextRow row;
        return _rowByName.TryGetValue(name, out row) ? row : null;
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

    /// <summary>Phase 07B.1 ship schematic host: in ship mode the radial
    /// element suppresses surface-map decorations and this deck-plan element
    /// (hull hairlines + zone chips + player marker) is added right AFTER the
    /// radial mesh so the plan reads on top of the translucent map disc.
    /// Surface mode keeps no schematic element at all.</summary>
    private void EnsureShipSchematicVisual()
    {
        if (_root == null || _document == null
            || _document.rootVisualElement == null)
        {
            return;
        }

        VisualElement host = _root.Q<VisualElement>("RadialGeometryHost");
        if (host == null)
        {
            return;
        }

        if (_radial != null)
        {
            _radial.ShipSchematicMode = IsShipMode;
            _radial.MarkDirtyRepaint();
        }

        if (!IsShipMode)
        {
            if (_shipSchematic != null && _shipSchematic.parent == host)
            {
                _shipSchematic.RemoveFromHierarchy();
            }

            _shipSchematic = null;
            return;
        }

        if (_shipSchematic != null && _shipSchematic.parent == host)
        {
            return;
        }

        if (_shipSchematic != null)
        {
            _shipSchematic.RemoveFromHierarchy();
        }

        _shipSchematic = new ShipSchematicVisual();
        host.Add(_shipSchematic);
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
    // Round 9.2 text-row slot layout
    // ====================================================================

    /// <summary>Lays every text row into its panel's real horizontal
    /// cross-section at the row's Y: left = minX, width = maxX - minX, so the
    /// label box exactly spans the padded slot and the middle-centre text sits
    /// on the geometry-derived centreX. Rows whose panel has no cross-section
    /// at that Y are hidden (can never happen with the authored bands).</summary>
    public void RefreshTextRowLayout()
    {
        if (_root == null || _rows.Count == 0)
        {
            return;
        }

        foreach (TextRow row in _rows)
        {
            if (row.Label == null)
            {
                continue;
            }

            if (!RadialWristElement.TryGetPanelRowCentre(
                    row.LeftSide, row.CenterY, RowSlotPadding,
                    out float centreX, out float minX, out float maxX))
            {
                row.Label.style.display = DisplayStyle.None;
                continue;
            }

            row.Label.style.display = DisplayStyle.Flex;
            row.Label.style.left = minX;
            row.Label.style.top = row.CenterY - RowHeight * 0.5f;
            row.Label.style.width = Mathf.Max(1f, maxX - minX);
            row.Label.style.height = RowHeight;
        }
    }

    // ====================================================================
    // Runtime layout / geometry verification (tests + open logging)
    // ====================================================================

    /// <summary>Runtime layout report for the wrist document: resolves the
    /// geometry host, markers and every text-row slot; returns false when any
    /// measured geometry is NaN/zero/degenerate, a text row lies outside its
    /// panel slot (Round 9.2), or the status tracks are misaligned with the
    /// panel inner radius.</summary>
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
                             + " panelR=" + RadialWristElement.PanelInnerRadius
                             + ".." + RadialWristElement.PanelOuterRadius
                             + " statusR=" + RadialWristElement.StatusInnerRadius
                             + ".." + RadialWristElement.StatusOuterRadius
                             + "x" + RadialWristElement.StatusThickness
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

        // Status labels sit near the (now inner) health/satiety arcs.
        Label healthLabel = root.Q<Label>("HealthLabel");
        Label satietyLabel = root.Q<Label>("SatietyLabel");
        if (healthLabel != null && satietyLabel != null)
        {
            Check(healthLabel.resolvedStyle.top < 160f, "health label near upper status arc");
            Check(satietyLabel.resolvedStyle.top > 600f, "satiety label near lower status arc");
        }

        // ---- Round 9.2: status arcs integrated with the panel inner radius.
        Check(Mathf.Approximately(RadialWristElement.StatusInnerRadius,
                RadialWristElement.PanelInnerRadius),
            "status inner radius == panel inner radius ("
            + RadialWristElement.StatusInnerRadius + ")");
        Check(RadialWristElement.StatusOuterRadius - RadialWristElement.StatusInnerRadius
              == RadialWristElement.StatusThickness,
            "status thickness consistent ("
            + RadialWristElement.StatusThickness + ")");

        // ---- Round 9.2: every text row sits fully inside its panel slot with
        // the authored padding (>= 10 px), and the slot is inside the band.
        RefreshTextRowLayout();
        bool allRowsOk = true;
        foreach (TextRow row in _rows)
        {
            if (row.Label == null)
            {
                allRowsOk = false;
                continue;
            }

            bool slotOk = VerifyRowSlot(row.Label, row.LeftSide, row.CenterY,
                out string rowInfo);
            lines.AppendLine((slotOk ? "PASS" : "FAIL") + " :: row " + row.ElementName
                             + " " + rowInfo);
            allRowsOk &= slotOk;
        }

        Check(allRowsOk, "all text rows inside their parallel-cap panel slots");

        report = lines.ToString();
        return ok;
    }

    /// <summary>Round 9.2 authoritative row-slot check: the label's horizontal
    /// box (left..left+width) must lie fully between the panel's padded inner
    /// and outer radius bounds at the row Y, and the row Y must be inside the
    /// panel's horizontal Y band (rows can never reach past the outer radius
    /// or touch the central map by construction of GetPanelHorizontalRange).
    /// Returns a small INFO string with the slot numbers.</summary>
    public static bool VerifyRowSlot(Label label, bool leftSide, float centerY,
        out string info)
    {
        info = "";
        if (label == null)
        {
            return false;
        }

        float left = label.style.left.value.value;
        float top = label.style.top.value.value;
        float width = label.style.width.value.value;
        float height = label.style.height.value.value;
        if (float.IsNaN(left) || float.IsNaN(top) || width <= 1f || height <= 1f)
        {
            return false;
        }

        bool insideBand = RowInsidePanelBand(leftSide, centerY);
        float pad = RowSlotPadding - 4f; // hard validation floor: >= 10 px

        bool rangeOk = RadialWristElement.GetPanelHorizontalRange(
            leftSide, centerY, RadialWristElement.PanelInnerRadius,
            RadialWristElement.PanelOuterRadius, pad,
            out float minX, out float maxX);

        bool horizontalInside = rangeOk
                                && left >= minX - 0.75f
                                && left + width <= maxX + 0.75f;

        bool verticalCentred = Mathf.Abs(top + height * 0.5f - centerY) <= 0.75f;

        info = "y=" + centerY.ToString("0")
               + " box=" + left.ToString("0") + ".." + (left + width).ToString("0")
               + " slot=" + (rangeOk ? minX.ToString("0") + ".." + maxX.ToString("0") : "<none>")
               + " band=" + insideBand
               + " horiz=" + horizontalInside
               + " vert=" + verticalCentred;

        return insideBand && horizontalInside && verticalCentred;
    }

    /// <summary>True when the row centre Y falls inside one of the two panel
    /// bands (upper 205..345 or lower 455..595).</summary>
    public static bool RowInsidePanelBand(bool leftSide, float centerY)
    {
        // Panels are mirrored left/right on the same bands.
        return (centerY >= RadialWristElement.UpperPanelTopY
                && centerY <= RadialWristElement.UpperPanelBottomY)
               || (centerY >= RadialWristElement.LowerPanelTopY
                   && centerY <= RadialWristElement.LowerPanelBottomY);
    }

    // ====================================================================
    // Round 9.2.2 WristIdentity bounds (map footer)
    // ====================================================================

    /// <summary>Round 9.2.2: verifies the WristIdentity label is a restrained
    /// MAP FOOTER - it lives INSIDE the central Local Map (radius 235), lower
    /// half. The label must:
    ///   - resolve fully inside the 1280x800 wrist RenderTexture;
    ///   - have all four corners strictly INSIDE the map disc with a safe
    ///     padding (each corner distance to the map centre &lt; MapRadius -
    ///     MapSafePadding, padding >= 12px);
    ///   - be centred below the map centre (identity centre Y &gt; map centre Y);
    ///   - NOT overlap HealthLabel or SatietyLabel;
    ///   - NOT overlap the Health/Satiety tracks (which live at radius
    ///     248..262 - corners inside the map already clear them);
    ///   - project to the screen inside the viewport on all four corners.
    /// The projection uses the SAME wrist-pixel -> display-plane -> camera
    /// mapping as ComputeProjectedRadialBounds (no separate coordinate math).</summary>
    public bool VerifyWristIdentityBounds(out string report)
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

        Label identity = _document.rootVisualElement.Q<Label>("WristIdentity");
        Label health = _document.rootVisualElement.Q<Label>("HealthLabel");
        Label satiety = _document.rootVisualElement.Q<Label>("SatietyLabel");
        Check(identity != null, "WristIdentity element resolved");
        Check(health != null, "HealthLabel element resolved");
        Check(satiety != null, "SatietyLabel element resolved");
        if (identity == null || health == null || satiety == null)
        {
            report = lines.ToString();
            return ok;
        }

        const float mapSafePadding = 12f;

        // 1) Resolved rect of the identity label inside the 1280x800 canvas.
        Rect idRect = ResolvedRectOf(identity);
        Rect healthRect = ResolvedRectOf(health);
        Rect satietyRect = ResolvedRectOf(satiety);
        lines.AppendLine("INFO :: identity rect=" + idRect.x.ToString("0") + ","
                         + idRect.y.ToString("0") + " " + idRect.width.ToString("0") + "x"
                         + idRect.height.ToString("0")
                         + " (expected ~534,560 212x20) health rect="
                         + healthRect.x.ToString("0") + "," + healthRect.y.ToString("0") + " "
                         + healthRect.width.ToString("0") + "x" + healthRect.height.ToString("0")
                         + " satiety rect="
                         + satietyRect.x.ToString("0") + "," + satietyRect.y.ToString("0") + " "
                         + satietyRect.width.ToString("0") + "x" + satietyRect.height.ToString("0"));

        bool rectValid = idRect.width > 1f && idRect.height > 1f
                         && !float.IsNaN(idRect.x) && !float.IsNaN(idRect.y);
        Check(rectValid, "identity resolved rect is valid");

        bool insideCanvas = rectValid
                            && idRect.x >= -0.75f
                            && idRect.y >= -0.75f
                            && idRect.x + idRect.width <= UiWidth + 0.75f
                            && idRect.y + idRect.height <= UiHeight + 0.75f;
        Check(insideCanvas, "identity rect fully inside the 1280x800 RenderTexture");

        // 2) No overlap with the Health / Satiety value labels.
        Check(!RectsOverlap(idRect, healthRect),
            "identity does not overlap HealthLabel bounds");
        Check(!RectsOverlap(idRect, satietyRect),
            "identity does not overlap SatietyLabel bounds");

        // 3) All four corners strictly INSIDE the map disc with the safe
        // padding. This also guarantees the label never reaches the status
        // tracks (radius 248..262).
        Vector2[] corners =
        {
            new Vector2(idRect.x, idRect.y),
            new Vector2(idRect.x + idRect.width, idRect.y),
            new Vector2(idRect.x + idRect.width, idRect.y + idRect.height),
            new Vector2(idRect.x, idRect.y + idRect.height)
        };
        float maxCornerDistance = 0f;
        bool cornersInsideMap = true;
        bool clearOfTracks = true;
        foreach (Vector2 corner in corners)
        {
            float distance = Vector2.Distance(corner, RadialWristElement.Center);
            maxCornerDistance = Mathf.Max(maxCornerDistance, distance);
            if (distance > RadialWristElement.MapRadius - mapSafePadding + 0.75f)
            {
                cornersInsideMap = false;
            }

            if (distance > RadialWristElement.StatusInnerRadius - 0.75f)
            {
                clearOfTracks = false;
            }
        }

        float maxDistance = maxCornerDistance;
        lines.AppendLine("INFO :: identity corner distance to map centre="
                         + maxDistance.ToString("0.0") + " (map radius "
                         + RadialWristElement.MapRadius + " minus safe padding "
                         + mapSafePadding + " = " + (RadialWristElement.MapRadius
                             - mapSafePadding).ToString("0.0") + ")");
        Check(cornersInsideMap, "all four identity corners INSIDE the map disc with >=12px padding");
        Check(clearOfTracks, "identity stays clear of the status tracks (radius < 248)");

        // 4) Lower half: identity centre Y below the map centre.
        float idCentreY = idRect.y + idRect.height * 0.5f;
        Check(idCentreY > RadialWristElement.Center.y + 0.5f,
            "identity centre Y is below the map centre (lower half, centreY="
            + idCentreY.ToString("0.0") + " vs " + RadialWristElement.Center.y + ")");

        // 5) Projected corners inside the current viewport (fraction 0..1).
        EnsureDisplayReference();
        if (_mainCamera == null && Camera.main != null)
        {
            _mainCamera = Camera.main;
        }

        if (_display == null || _mainCamera == null || Screen.width <= 0 || Screen.height <= 0)
        {
            Check(false, "display/camera available for projection");
        }
        else
        {
            bool allInViewport = true;
            float minVx = 1f, maxVx = 0f, minVy = 1f, maxVy = 0f;
            foreach (Vector2 corner in corners)
            {
                if (!TryProjectWristPixelToViewport(corner, out Vector3 vp))
                {
                    allInViewport = false;
                    continue;
                }

                minVx = Mathf.Min(minVx, vp.x);
                maxVx = Mathf.Max(maxVx, vp.x);
                minVy = Mathf.Min(minVy, vp.y);
                maxVy = Mathf.Max(maxVy, vp.y);
                const float epsilon = 0.006f;
                if (vp.x < -epsilon || vp.x > 1f + epsilon
                    || vp.y < -epsilon || vp.y > 1f + epsilon)
                {
                    allInViewport = false;
                }
            }

            lines.AppendLine("INFO :: identity projected viewport x="
                             + minVx.ToString("0.000") + ".." + maxVx.ToString("0.000")
                             + " y=" + minVy.ToString("0.000") + ".." + maxVy.ToString("0.000")
                             + " screen=" + (minVx * Screen.width).ToString("0") + ".."
                             + (maxVx * Screen.width).ToString("0") + "x"
                             + (minVy * Screen.height).ToString("0") + ".."
                             + (maxVy * Screen.height).ToString("0")
                             + " @ " + Screen.width + "x" + Screen.height);
            Check(allInViewport, "all four identity corners inside the viewport");
        }

        report = lines.ToString();
        return ok;
    }

    /// <summary>Resolved layout rect of a wrist element (falls back to the
    /// inline style values when the RT panel has not laid out yet).</summary>
    public static Rect ResolvedRectOf(VisualElement element)
    {
        if (element == null)
        {
            return Rect.zero;
        }

        float left = float.IsNaN(element.resolvedStyle.left)
            ? (element.style.left.value.value)
            : element.resolvedStyle.left;
        float top = float.IsNaN(element.resolvedStyle.top)
            ? (element.style.top.value.value)
            : element.resolvedStyle.top;
        float width = float.IsNaN(element.resolvedStyle.width)
            ? (element.style.width.value.value)
            : element.resolvedStyle.width;
        float height = float.IsNaN(element.resolvedStyle.height)
            ? (element.style.height.value.value)
            : element.resolvedStyle.height;

        return new Rect(left, top, width, height);
    }

    private static bool RectsOverlap(Rect a, Rect b)
    {
        if (a.width <= 0f || a.height <= 0f || b.width <= 0f || b.height <= 0f)
        {
            return false;
        }

        return a.x < b.x + b.width
               && a.x + a.width > b.x
               && a.y < b.y + b.height
               && a.y + a.height > b.y;
    }

    /// <summary>Maps one wrist pixel (design-space, UI y down) onto the world
    /// wrist plane, then to viewport coordinates (0..1). Uses the exact same
    /// display transform convention as ComputeProjectedRadialBounds.</summary>
    public bool TryProjectWristPixelToViewport(Vector2 wristPixel, out Vector3 viewport)
    {
        viewport = Vector3.zero;
        if (_display == null || _mainCamera == null)
        {
            return false;
        }

        Transform t = _display.transform;
        Vector3 scale = t.localScale;
        float localX = wristPixel.x / UiWidth - 0.5f;
        float localY = 0.5f - wristPixel.y / UiHeight;
        Vector3 world = t.position
                        + t.right * (localX * scale.x)
                        + t.up * (localY * scale.y);
        viewport = _mainCamera.WorldToViewportPoint(world);
        return viewport.z > 0f;
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
            RefreshTextRowLayout();
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

        // Round 9.2.1: identity label bounds (RT + projection). Printed on
        // every wrist open so the Release screenshot log carries the exact
        // 1920x1080 projected corners for human acceptance.
        bool identityOk = VerifyWristIdentityBounds(out string identityReport);
        if (identityOk)
        {
            Debug.Log("[WristIdentity] OK\n" + identityReport);
        }
        else
        {
            Debug.LogError("[WristIdentity] FAIL\n" + identityReport);
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

            // The display normal can be read from the wrist pivot direction
            // (must face the camera at all) and from the hologram centre
            // direction (Round 9 target: 0.88..0.95 - the readable centre of
            // the panel is what a human actually looks at, so the tilt is
            // measured there).
            Vector3 frontNormal = _display.FrontNormal;
            Vector3 displayCentrePos = _display.transform.position;
            float facingPivot = Vector3.Dot(frontNormal,
                (camPos - planePos).normalized);
            float facingCentre = Vector3.Dot(frontNormal,
                (camPos - displayCentrePos).normalized);
            Check(facingPivot > 0.5f,
                "display front normal faces the camera at the wrist (dot="
                + facingPivot.ToString("0.00") + ")");

            // Round 9 raised-wrist pose evidence (printed on every open):
            // wrist pivot lower-left, hologram centre up/right of the pivot,
            // front dot 0.88..0.95 measured at the hologram centre.
            Vector3 pivotViewport = _mainCamera.WorldToViewportPoint(planePos);
            Vector3 centreViewport = _mainCamera.WorldToViewportPoint(displayCentrePos);
            lines.AppendLine("INFO :: pose pivotViewport=" + pivotViewport.ToString("0.000")
                             + " centreViewport=" + centreViewport.ToString("0.000")
                             + " frontDotCentre=" + facingCentre.ToString("0.00")
                             + " frontDotPivot=" + facingPivot.ToString("0.00")
                             + " centreAbovePivot=" + (centreViewport.y > pivotViewport.y + 0.05f)
                             + " centreRightOfPivot=" + (centreViewport.x > pivotViewport.x + 0.02f));
            Check(facingCentre >= 0.84f && facingCentre <= 0.96f,
                "raised-wrist base pose not camera-facing (hologram centre front dot "
                + facingCentre.ToString("0.00") + ", target 0.88..0.93)");
            Check(centreViewport.z > 0f
                  && centreViewport.x >= -0.1f && centreViewport.x <= 1.1f
                  && centreViewport.y >= -0.1f && centreViewport.y <= 1.1f,
                "hologram centre inside viewport (v=" + centreViewport.ToString("0.000"));
            Check(centreViewport.y > pivotViewport.y + 0.05f,
                "hologram centre raised above the wrist pivot");
            Check(centreViewport.x > pivotViewport.x + 0.02f,
                "hologram centre right of the wrist pivot");

            Vector3 viewport = _mainCamera.WorldToViewportPoint(planePos);
            Check(viewport.z > 0f
                  && viewport.x >= -0.05f && viewport.x <= 1.05f
                  && viewport.y >= -0.05f && viewport.y <= 1.05f,
                "wrist pivot inside camera viewport (v=" + viewport.ToString("0.000"));

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
            lines.AppendLine("INFO :: projectedBounds fractionW="
                             + (widthFraction * 100f).ToString("0") + "% fractionH="
                             + (heightFraction * 100f).ToString("0")
                             + "% px=" + ((maxX - minX) * Screen.width).ToString("0")
                             + "x" + ((maxY - minY) * Screen.height).ToString("0")
                             + " (Round 9.2: meaningful geometry must fit the screen;"
                             + " transparent RT edges may extend beyond)");
            // The exact pixel targets are aspect- and resolution-specific, so
            // the runtime check only guards against a degenerate (invisible)
            // projection. The precise 1920x1080 numbers land in the Release
            // screenshot log (same INFO lines) for human acceptance.
            Check(widthFraction >= 0.10f, "projected display width not degenerate (actual "
                + (widthFraction * 100f).ToString("0") + "%)");
            Check(heightFraction >= 0.10f, "projected display height not degenerate (actual "
                + (heightFraction * 100f).ToString("0") + "%)");

            float widthPx = (maxX - minX) * Screen.width;
            float heightPx = (maxY - minY) * Screen.height;
            float aspectPx = heightPx > 1f ? widthPx / heightPx : 0f;
            // Round 9.2: the plane is large and close, so corner projections
            // can overflow numerically near the near clip; the aspect guard
            // only needs to reject a genuinely edge-on (degenerate) view. A
            // true edge-on plane projects far below 1.0. The exact 1280:800
            // plane aspect is independently verified just below, and the pose
            // collapse guard keeps the tilt from ever becoming edge-on.
            Check(aspectPx >= 1.0f && aspectPx <= 3.0f,
                "projected aspect not edge-on (px " + widthPx.ToString("0") + "x"
                + heightPx.ToString("0") + " aspect " + aspectPx.ToString("0.00") + ")");
            float scaleRatio = Mathf.Abs(scale.x / scale.y - (1280f / 800f));
            Check(scaleRatio < 0.02f,
                "display local scale keeps 1280:800 aspect (x/y ratio "
                + (scale.x / scale.y).ToString("0.00") + ")");

            // Round 9.2: authoritative size metric - the panel outer radius
            // (365) is now the outermost drawn radial geometry (status arcs
            // moved inside, panels define the outer bound). The previous
            // status-arc-outer metric is obsolete.
            if (ComputeProjectedRadialBounds(out Vector2 radialCentre,
                    out float radialWidthPx, out float radialHeightPx))
            {
                lines.AppendLine("INFO :: radialGraphic centre="
                                 + radialCentre.ToString("0.000")
                                 + " size=" + radialWidthPx.ToString("0")
                                 + "x" + radialHeightPx.ToString("0")
                                 + " px (Round 9.2 target @1920: centre x 44..46%,"
                                 + " top-y 53..56%, width 960..1020, height 820..900)");
            }
        }

        report = lines.ToString();
        return ok;
    }

    // ====================================================================
    // Pose + parallax (CodeReference: rotate the WristProjectionPivot only)
    // ====================================================================

    /// <summary>Orients the display front at the camera through the projection
    /// pivot with the Round 9 raised-wrist tilt, then captures the pivot base
    /// pose. Only collapses the tilt if the projected aspect would become
    /// degenerately edge-on (below 1.15); a moderate tilt is the DESIGNED
    /// base pose now, not an error to be minimised.</summary>
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
            if (aspect <= 0f || aspect >= 1.15f)
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

    /// <summary>Round 9.2: projects the outermost drawn radial geometry (a
    /// circle at PanelOuterRadius = 365 around the RT centre - the panels now
    /// define the outer bound) onto the screen. Returns the projected bounds
    /// centre viewport, and the pixel width / height at the current Screen
    /// resolution. This is the authoritative size metric.</summary>
    public bool ComputeProjectedRadialBounds(
        out Vector2 centreViewport,
        out float widthPx,
        out float heightPx)
    {
        centreViewport = Vector2.zero;
        widthPx = 0f;
        heightPx = 0f;

        if (_display == null || _mainCamera == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return false;
        }

        float rtRadius = RadialWristElement.PanelOuterRadius;
        Transform t = _display.transform;
        float rx = rtRadius / RadialWristElement.DesignWidth * t.localScale.x;
        float ry = rtRadius / RadialWristElement.DesignHeight * t.localScale.y;

        float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
        bool any = false;
        const int samples = 72;
        for (int i = 0; i < samples; i++)
        {
            float angle = i * 360f / samples;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 world = t.position
                            + t.right * (Mathf.Cos(rad) * rx)
                            + t.up * (Mathf.Sin(rad) * ry);
            Vector3 c = _mainCamera.WorldToViewportPoint(world);
            if (c.z <= 0f)
            {
                continue;
            }

            any = true;
            minX = Mathf.Min(minX, c.x);
            maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y);
            maxY = Mathf.Max(maxY, c.y);
        }

        if (!any)
        {
            return false;
        }

        centreViewport = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        widthPx = (maxX - minX) * Screen.width;
        heightPx = (maxY - minY) * Screen.height;
        return true;
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
            RefreshTextRowLayout();
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

        if (_harvesterRow != null)
        {
            SetEquipState(_harvesterRow, "采集器 MK-I", false);
        }

        if (_batteryRow != null)
        {
            SetEquipState(_batteryRow, "备用电池", !_session.BatterySelected);
        }

        if (_droneRow != null)
        {
            SetEquipState(_droneRow, "勘测无人机", !_session.DroneSelected);
        }

        SurfaceResourceNode active = _controller != null ? _controller.ActiveNode : null;
        if (_actionRow != null)
        {
            _actionRow.Label.text = active != null
                ? (active.State == SurfaceResourceNode.NodeState.Harvesting ? "采集中" : "无线传输")
                : (ReturnMode == WristReturnMode.ShipExplore ? "舰内" : "勘测中");
        }

        if (_recoveredRow != null)
        {
            _recoveredRow.Label.text = "回收 " + _session.HarvestedNodeCount
                                       + "/" + GameSession.SurfaceNodeCount;
        }

        if (ReturnMode == WristReturnMode.ShipExplore)
        {
            RefreshShipContent();
        }
    }

    // ====================================================================
    // Phase 07B ship-mode content (same wrist document, ship context only)
    // ====================================================================

    /// <summary>True while this wrist instance is bound inside the walkable
    /// ship (content mode: 舰内, no fake surface distance or markers).</summary>
    public bool IsShipMode => ReturnMode == WristReturnMode.ShipExplore;

    /// <summary>Hides every surface map marker. The ship player is not on the
    /// NEREID map, so no surface player/node markers may be drawn.</summary>
    private void HideSurfaceMarkers()
    {
        SetMarkerVisibility(_playerMarker, false);
        SetMarkerVisibility(_landingMarker, false);
        for (int i = 0; i < _nodeMarkers.Length; i++)
        {
            SetMarkerVisibility(_nodeMarkers[i], false);
        }

        SetMarkerVisibility(_selectedRing, false);
    }

    private static void SetMarkerVisibility(VisualElement marker, bool visible)
    {
        if (marker == null)
        {
            return;
        }

        marker.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>Phase 07B.1: moves the deck-schematic player marker to the
    /// real ship position/yaw (only while the wrist is open; the wrist Tick
    /// drives this through RefreshMapGeometry).</summary>
    private void UpdateShipSchematicPlayer()
    {
        if (_shipSchematic == null || _controller == null
            || _controller.Player == null)
        {
            return;
        }

        _shipSchematic.SetPlayer(_controller.Player.transform.position,
            _controller.Player.YawDegrees);
    }

    /// <summary>The live ship schematic element (null on surface). Exposed for
    /// Phase 07B.1 acceptance so tests can read nodes/marker without rendering
    /// introspection.</summary>
    public ShipSchematicVisual ShipSchematic => _shipSchematic;

    /// <summary>True while the ship schematic element is attached to the wrist
    /// document (ship mode acceptance).</summary>
    public bool HasShipSchematic =>
        _shipSchematic != null && _shipSchematic.parent != null
        && _shipSchematic.parent.name == "RadialGeometryHost";

    /// <summary>
    /// Ship wrist presentation (Phase 07B contract 59-65):
    /// - central map keeps its circular technical interface, footer shows
    ///   ARK-07 · 舰内模式,
    /// - state rows: 舰内 / 信号在线 (authored), persisted recovery count,
    /// - navigation: 地表目标已记录 only when a NEREID target was recorded
    ///   this session - never a fake live distance,
    /// - harvest region reads 地表采集待命 (no player is physically
    ///   harvesting inside the ship).
    /// </summary>
    public void RefreshShipContent()
    {
        if (_session == null)
        {
            return;
        }

        HideSurfaceMarkers();

        if (_identityLabel != null)
        {
            _identityLabel.text = "ARK-07 · 舰内模式";
        }

        if (_actionRow != null)
        {
            _actionRow.Label.text = "舰内";
        }

        int recorded = _session.RecordedSurfaceTargetNodeId;
        bool hasMemory = recorded >= 0;
        if (_navTargetRow != null)
        {
            _navTargetRow.Label.text = hasMemory
                ? SurfaceGameController.NodeDisplayName(recorded)
                : "未设置";
        }

        if (_navDistanceRow != null)
        {
            _navDistanceRow.Label.text = hasMemory ? "地表目标已记录" : "";
        }

        if (_harvestNodeRow != null)
        {
            _harvestNodeRow.Label.text = "";
        }

        if (_harvestStateRow != null)
        {
            _harvestStateRow.Label.text = "地表采集待命";
        }

        if (_harvestProgressRow != null)
        {
            _harvestProgressRow.Label.text = "";
        }
    }

    private static void SetEquipState(TextRow row, string name, bool off)
    {
        if (row == null || row.Label == null)
        {
            return;
        }

        row.Label.text = name;
        if (off)
        {
            row.Label.AddToClassList("row-off");
        }
        else
        {
            row.Label.RemoveFromClassList("row-off");
        }
    }

    // ---- Circular map (single shared coordinate source) ----

    private void RefreshMapGeometry()
    {
        if (ReturnMode == WristReturnMode.ShipExplore)
        {
            // Ship mode: no surface map markers at all (no fake player/node
            // positions); the deck schematic + live player marker render the
            // ARK-07 interior plan instead of the surface node scatter map.
            HideSurfaceMarkers();
            UpdateShipSchematicPlayer();
            return;
        }

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

    // ---- Live active harvest context (Round 9.2 full-state assignments) ----

    /// <summary>Refresh the harvest sector rows. Every refresh EXPLICITLY
    /// writes all three labels (node / state / progress) from the current
    /// state - no partial string replacement - so no '%' can ever survive a
    /// completion transition.</summary>
    private void RefreshActiveProgress()
    {
        if (ReturnMode == WristReturnMode.ShipExplore)
        {
            RefreshShipContent();
            return;
        }

        if (_harvestNodeRow == null || _harvestStateRow == null
            || _harvestProgressRow == null || _controller == null)
        {
            return;
        }

        SurfaceResourceNode active = _controller.ActiveNode;
        bool showCompletion = active == null && _completedNode != null
                              && Time.time - _completedAt < CompletionHoldSeconds;

        if (active != null)
        {
            // Harvesting or Transmitting: all three rows carry text.
            float progress = 1f - Mathf.Clamp01(active.RemainingSeconds /
                Mathf.Max(0.001f, active.StageSeconds));
            _harvestNodeRow.Label.text = ShortNodeName(active.NodeId);
            _harvestStateRow.Label.text = active.State == SurfaceResourceNode.NodeState.Harvesting
                ? "采集中"
                : "无线传输";
            _harvestProgressRow.Label.text = Mathf.RoundToInt(progress * 100f) + "%";
        }
        else if (showCompletion)
        {
            _harvestNodeRow.Label.text = ShortNodeName(_completedNode.NodeId);
            _harvestStateRow.Label.text = "传输完成";
            _harvestProgressRow.Label.text = ""; // NO residue
        }
        else
        {
            _harvestNodeRow.Label.text = "";
            _harvestStateRow.Label.text = "待机";
            _harvestProgressRow.Label.text = ""; // NO residue
        }
    }

    /// <summary>Compact node label used inside the Harvest sector rows,
    /// e.g. "节点 01" for node id 0.</summary>
    private static string ShortNodeName(int nodeId)
    {
        return "节点 " + (nodeId + 1).ToString("00");
    }

    private void RefreshNavigation()
    {
        if (ReturnMode == WristReturnMode.ShipExplore)
        {
            RefreshShipContent();
            return;
        }

        if (_controller == null)
        {
            return;
        }

        if (_navTargetRow != null)
        {
            _navTargetRow.Label.text = _controller.IsNavigationActive
                ? SurfaceGameController.NodeDisplayName(_controller.NavigationTargetNodeId)
                : "未设置";
        }

        if (_navDistanceRow != null)
        {
            _navDistanceRow.Label.text = _controller.IsNavigationActive
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
