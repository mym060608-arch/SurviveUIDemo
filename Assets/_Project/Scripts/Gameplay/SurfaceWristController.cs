using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// How the gamepad drives the OPEN wrist terminal (Phase 09B PASS 3).
///
/// The wrist is one presentation with two honest interaction modes, and the mode
/// is chosen by the CONTROL the player actually touches - never by a menu:
///
///   MapCursor   the shared pointer (right stick / mouse) owns the interaction:
///               A is the map's left button, LT/RT zoom, B returns. This is the
///               accepted PASS 2 behaviour and the mode the terminal opens in.
///   ModuleFocus the D-Pad / left stick walks the focus ring over the elements
///               that really DO something (equipment toggles, navigation clear,
///               the central map), A confirms the focused element. Pure read-only
///               modules can never take focus.
///
/// The map cursor keeps working in both modes (the cursor is the OS cursor the
/// mouse also uses); only the A button is re-routed, so the frozen PASS 2 map
/// contract cannot be disturbed by the new focus layer.
/// </summary>
public enum WristPadMode
{
    /// <summary>Right stick / mouse drives the shared map cursor (PASS 2).</summary>
    MapCursor = 0,

    /// <summary>D-Pad / left stick walks the module focus ring (PASS 3).</summary>
    ModuleFocus = 1
}

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
/// Phase 09B: the cursor position itself is the SHARED pointer
/// (UiPointerState) - the mouse writes it, the right stick moves it at a steady
/// speed and the OS cursor is warped onto it, so the pad drives the SAME cursor
/// instead of a second, pad-only map. Confirm (Xbox A) is the exact equivalent of
/// the mouse's left button on the map, LT/RT zoom the map content, and the
/// left stick stays unbound because this map has no pan.
///
/// Parallax: the hologram tilts around the WristProjectionPivot (localRotation
/// on a captured base pose), clamped to yaw +-4 / pitch +-3 / roll +-0.5 deg,
/// damped; it never translates around the screen.
///
/// Phase 09B PASS 3 (controller UX + presentation, still presentation-only):
/// - the ordinary wrist UI gets controller FOCUS (D-Pad / left stick, A confirm,
///   B back) through the shared <see cref="UiFocusNavigator"/>; only elements
///   that really act can be focused - 当前导航 clears the target while one is set
///   and the central map re-enters cursor mode. The 当前装备 block is a READ-ONLY
///   list of what the expedition is carrying (an unequipped module is not listed
///   at all), so headings, equipment, status readouts and the harvest readout are
///   pure information and are never focusable;
/// - the map keeps the accepted PASS 2 behaviour byte-for-byte (right stick
///   cursor, A = left button, LT/RT zoom, B back) and is the mode the terminal
///   opens in;
/// - the Controller Prompt follows the ACTIVE mode: map rows in cursor mode,
///   focus rows in module mode;
/// - focus / pulse / breathing / data-flow animation is drawn by
///   RadialWristElement and driven from <see cref="Tick"/> only while the
///   terminal is really presented.
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

    // ---- Phase 09B: controller map cursor + zoom ----

    /// <summary>Zoom rate at full trigger deflection (zoom units per second).</summary>
    public const float ZoomRatePerSecond = 1.1f;

    /// <summary>Zoom step for one mouse wheel notch.</summary>
    public const float ZoomWheelStep = 0.14f;

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

        /// <summary>The AUTHORED slot Y of this row in the frozen Round 9.2 row
        /// table (see <see cref="RowTable"/>).</summary>
        public readonly float CenterY;

        /// <summary>The Y this row is ACTUALLY laid out at. It equals
        /// <see cref="CenterY"/> for every fixed row; the equipment block moves its
        /// rows up so a read-only list of equipped items never leaves a hole where
        /// an unequipped item used to be.</summary>
        public float LayoutCenterY;

        /// <summary>True while the row carries no content and is not drawn. A
        /// hidden row keeps its authored slot so the frozen slot geometry stays
        /// verifiable, but it is never part of the visible staircase.</summary>
        public bool Hidden;

        public Label Label;

        public TextRow(string elementName, bool leftSide, float centerY)
        {
            ElementName = elementName;
            LeftSide = leftSide;
            CenterY = centerY;
            LayoutCenterY = centerY;
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

    // ---- Phase 09B: map view state ----

    /// <summary>Map content zoom (1 = the frozen Phase 06 mapping).</summary>
    private float _mapZoom = 1f;
    private int _lastSelectedByConfirmFrame = -1;

    // ---- Phase 09B PASS 3: pad mode + ordinary-UI focus ----

    /// <summary>Style class the shared focus navigator toggles on the focused row.</summary>
    public const string FocusClass = "wrist-row-focus";

    /// <summary>Element name of the invisible focus anchor over the central map.</summary>
    public const string MapFocusAnchorName = "WristFocusMap";

    private WristPadMode _padMode = WristPadMode.MapCursor;
    private UiFocusNavigator _focus;
    private VisualElement _focusRoot;
    private bool _focusModelReady;
    private VisualElement _mapFocusAnchor;
    private Label _navRadarGlyph;
    private bool _navRadarActive;
    private string _focusSignature = string.Empty;
    private string _lastActionStateText;
    private string _lastRecoveredStateText;

    /// <summary>Which control currently owns the open terminal (diagnostics/tests).</summary>
    public WristPadMode PadMode => _padMode;

    /// <summary>The shared focus navigator of the wrist document.</summary>
    public UiFocusNavigator FocusNavigator => _focus;

    /// <summary>Element name of the focused row ("" when nothing is focused).</summary>
    public string FocusedRowName =>
        _focus != null && _focus.Focused != null ? _focus.Focused.name : string.Empty;

    /// <summary>Number of elements that can take focus right now.</summary>
    public int FocusableCount => _focus != null ? _focus.Count : 0;

    /// <summary>The radial mesh element (tests / probes read the PASS 3 state).</summary>
    public RadialWristElement Radial => _radial;

    /// <summary>Current map content zoom (diagnostics / tests / probes).</summary>
    public float MapZoom => _mapZoom;

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
        _navRadarGlyph = _root.Q<Label>("NavRadarGlyph");

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

        EnsureFocusModel();

        _bound = _geometryHost != null && _playerMarker != null && _rows.Count > 0;
    }

    // ====================================================================
    // Phase 09B PASS 3 - ordinary wrist UI focus model
    // ====================================================================

    /// <summary>
    /// Builds the SHARED focus navigator over the wrist document and registers
    /// exactly the elements that really act:
    ///
    ///   WristFocusMap   the central map      -> re-enter map cursor mode
    ///   NavTarget       当前导航 (only while a target is set) -> clear it
    ///
    /// The 当前装备 block is deliberately NOT registered: it is a read-only list of
    /// what the expedition is carrying, and changing the loadout belongs to the
    /// ship's loadout page, never to the wrist. Together with the headings, the
    /// state read-out and the whole harvest block, it can never show a focus ring
    /// (contract §2: "只有真正可操作的元素才能获得 Focus").
    ///
    /// The map anchor is an invisible element covering the disc: the navigator
    /// moves by geometry, and the visible focus feedback is the module plate edge
    /// drawn by RadialWristElement plus the focused row's ink.
    /// </summary>
    private void EnsureFocusModel()
    {
        if (_root == null)
        {
            return;
        }

        // Built ONCE per document root. It must never be rebuilt on a whim: the
        // navigator owns the player's current focus, and a validator that ran on
        // every wrist open would otherwise clear the ring the player was using.
        if (_focusModelReady && _focusRoot == _root
            && _mapFocusAnchor != null && _mapFocusAnchor.parent == _root)
        {
            return;
        }

        if (_mapFocusAnchor == null || _mapFocusAnchor.parent != _root)
        {
            if (_mapFocusAnchor != null)
            {
                _mapFocusAnchor.RemoveFromHierarchy();
            }

            _mapFocusAnchor = new VisualElement { name = MapFocusAnchorName };
            _mapFocusAnchor.style.position = Position.Absolute;
            _mapFocusAnchor.style.left = RadialWristElement.Center.x
                                         - RadialWristElement.MapRadius;
            _mapFocusAnchor.style.top = RadialWristElement.Center.y
                                        - RadialWristElement.MapRadius;
            _mapFocusAnchor.style.width = RadialWristElement.MapRadius * 2f;
            _mapFocusAnchor.style.height = RadialWristElement.MapRadius * 2f;
            _mapFocusAnchor.style.backgroundColor = Color.clear;
            _root.Add(_mapFocusAnchor);
        }

        if (_focus == null)
        {
            _focus = new UiFocusNavigator(FocusClass)
            {
                Wrap = true,
                Enabled = false
            };
        }

        // The elements ARE the focus anchors: every row is already an
        // absolutely-positioned slot whose rect the layout owns, so the navigator
        // can move by real geometry without a second proxy tree.
        _focus.Clear();
        _focus.Register(_mapFocusAnchor, EnterMapCursorFromFocus);
        if (NavTargetRow() != null)
        {
            _focus.Register(NavTargetRow().Label, ClearNavigationFromWrist);
        }

        _focusRoot = _root;
        _focusModelReady = true;
        RefreshFocusItems(force: true);
    }

    private TextRow NavTargetRow() => Row("NavTarget");

    /// <summary>True while the navigation module has a live target, i.e. while its
    /// row is an OPERABLE element ("已设置" state) instead of a standby read-out.</summary>
    public bool NavigationFocusable =>
        _controller != null && !IsShipMode && _controller.IsNavigationActive;

    /// <summary>
    /// Publishes the CURRENT operable set to the navigator. Cheap to call every
    /// refresh: the membership is compared as a signature, so the navigator is
    /// only rebuilt when the terminal's operable set really changed (a navigation
    /// target appearing / disappearing).
    /// </summary>
    private void RefreshFocusItems(bool force = false)
    {
        if (_focus == null || _root == null)
        {
            return;
        }

        bool nav = NavigationFocusable;

        // The central map is an OPERABLE element only where it is a live surface
        // map; inside the ship it is a deck schematic with nothing to select, so
        // it is not a focus stop there (the ship wrist is a pure ordinary UI).
        bool map = !IsShipMode;
        string signature = (map ? "map" : "ship-readonly")
                           + (nav ? "|nav" : string.Empty);
        if (!force && signature == _focusSignature)
        {
            return;
        }

        _focusSignature = signature;

        var items = new List<VisualElement>();
        if (map && _mapFocusAnchor != null)
        {
            items.Add(_mapFocusAnchor);
        }

        if (nav && NavTargetRow() != null)
        {
            items.Add(NavTargetRow().Label);
        }

        _focus.SetItems(items);
    }

    /// <summary>Module plate that mirrors one focusable element.</summary>
    public WristModuleId ModuleOfFocusedElement()
    {
        string name = FocusedRowName;
        if (string.IsNullOrEmpty(name))
        {
            return WristModuleId.None;
        }

        switch (name)
        {
            case MapFocusAnchorName:
                return WristModuleId.Map;
            case "NavTarget":
                return WristModuleId.Navigation;
            default:
                return WristModuleId.None;
        }
    }

    // ---- The focused elements' actions (all use EXISTING authorities) ----

    /// <summary>A on the central map: hand the interaction back to the cursor.</summary>
    private void EnterMapCursorFromFocus()
    {
        if (_radial != null)
        {
            _radial.Pulse(WristModuleId.Map);
        }

        SetPadMode(WristPadMode.MapCursor);
    }

    /// <summary>A on 当前导航 (available only while a target is set) clears the
    /// navigation target through the owning controller - the wrist never edits
    /// navigation state itself.</summary>
    private void ClearNavigationFromWrist()
    {
        if (_controller == null)
        {
            return;
        }

        if (_controller.TryClearNavigationTarget())
        {
            _radial?.Pulse(WristModuleId.Navigation);
        }

        RefreshNavigation();
    }

    /// <summary>Switches the interaction mode and re-applies every presentation
    /// that depends on it (focus ring, navigator enable, prompt rows).</summary>
    public void SetPadMode(WristPadMode mode)
    {
        if (_padMode == mode)
        {
            ApplyPadMode();
            return;
        }

        _padMode = mode;
        ApplyPadMode();
    }

    private void ApplyPadMode()
    {
        bool moduleFocus = _padMode == WristPadMode.ModuleFocus;
        if (_focus != null)
        {
            _focus.Enabled = moduleFocus;
            if (moduleFocus)
            {
                // Opening / entering the focus mode always lands on a sensible
                // element (contract §3: "打开界面时自动给出合理默认 Focus"),
                // and a page that is reopened keeps the player's place.
                _focus.FocusDefault();
            }
            else
            {
                // Map cursor mode deliberately shows NO focus box (contract §6):
                // the cursor is the interaction there, not a focus ring.
                _focus.SetFocus(null);
            }
        }

        if (_radial != null)
        {
            _radial.SetFocusModule(moduleFocus ? ModuleOfFocusedElement() : WristModuleId.None);
        }
    }

    /// <summary>
    /// Chooses the mode from the control the player used this frame. The right
    /// stick (and a real mouse move) hand the terminal back to the shared cursor;
    /// the D-Pad / left stick take it into module focus.
    /// </summary>
    private void UpdatePadMode()
    {
        // The ship wrist has no map cursor to drive (its centre is a deck
        // schematic): it is an ordinary UI and stays in module focus.
        if (IsShipMode)
        {
            _padMode = WristPadMode.ModuleFocus;
        }
        else if (UiInput.MapCursorVector() != Vector2.zero)
        {
            _padMode = WristPadMode.MapCursor;
        }
        else if (UiInput.NavigationVector() != Vector2.zero)
        {
            _padMode = WristPadMode.ModuleFocus;
        }
        else if (InputDeviceTracker.MouseProducedRealInput())
        {
            _padMode = WristPadMode.MapCursor;
        }

        // Re-applied EVERY frame, not only when the mode flips: the module plate
        // that carries the focus look always mirrors the navigator's CURRENT
        // element, so a page (or a test) that moves the focus directly still gets
        // the matching plate lift on the very next frame.
        ApplyPadMode();
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

            // A hidden row keeps its AUTHORED slot (the frozen Round 9.2 row table
            // stays verifiable); a visible row uses its effective Y, which only the
            // read-only equipment block ever moves.
            float y = row.Hidden ? row.CenterY : row.LayoutCenterY;
            if (!RadialWristElement.TryGetPanelRowCentre(
                    row.LeftSide, y, RowSlotPadding,
                    out float centreX, out float minX, out float maxX))
            {
                row.Label.style.display = DisplayStyle.None;
                continue;
            }

            row.Label.style.display = row.Hidden ? DisplayStyle.None : DisplayStyle.Flex;
            row.Label.style.left = minX;
            row.Label.style.top = y - RowHeight * 0.5f;
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

            bool slotOk = VerifyRowSlot(row.Label, row.LeftSide,
                row.Hidden ? row.CenterY : row.LayoutCenterY,
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

    // ====================================================================
    // Phase 09B PASS 3 - focus model + layout verification
    // ====================================================================

    /// <summary>
    /// Verifies the PASS 3 wrist contract that is checkable without a human:
    ///
    ///   1. the standby radar sits INSIDE the navigation panel's padded slot at
    ///      its own Y (never over the map, never over the plate edge);
    ///   2. the navigator only ever holds elements that really act - the map,
    ///      the two equipment toggles and (only while a target is set) the
    ///      navigation row;
    ///   3. every pure INFORMATION row (headings, state read-out, harvest
    ///      read-out, the always-equipped harvester) is absent from the
    ///      navigator, so no read-out can ever show a focus ring;
    ///   4. the status chips still sit near their arcs and never overlap the map
    ///      footer.
    /// </summary>
    public bool VerifyWristFocusModel(out string report)
    {
        var lines = new StringBuilder();
        bool ok = true;

        void Check(bool pass, string label)
        {
            lines.AppendLine((pass ? "PASS" : "FAIL") + " :: " + label);
            ok &= pass;
        }

        if (_root == null)
        {
            report = "FAIL :: wrist document root missing\n";
            return false;
        }

        EnsureFocusModel();
        RefreshFocusItems(force: true);
        RefreshTextRowLayout();

        // ---- 1. radar inside the navigation panel slot ----
        bool radarInPanel = RadialWristElement.GetPanelHorizontalRange(
            true, RadialWristElement.RadarCenter.y,
            RadialWristElement.PanelInnerRadius, RadialWristElement.PanelOuterRadius,
            0f, out float radarMinX, out float radarMaxX);
        bool radarInside = radarInPanel
                           && RadialWristElement.RadarCenter.x
                           - RadialWristElement.RadarRadius >= radarMinX
                           && RadialWristElement.RadarCenter.x
                           + RadialWristElement.RadarRadius <= radarMaxX
                           && RadialWristElement.RadarCenter.y
                           + RadialWristElement.RadarRadius
                           <= RadialWristElement.LowerPanelBottomY;
        lines.AppendLine("INFO :: radar centre="
                         + RadialWristElement.RadarCenter.x.ToString("0") + ","
                         + RadialWristElement.RadarCenter.y.ToString("0")
                         + " r=" + RadialWristElement.RadarRadius
                         + " slot=" + (radarInPanel
                             ? radarMinX.ToString("0") + ".." + radarMaxX.ToString("0")
                             : "<none>"));
        Check(radarInside,
            "standby radar sits inside the navigation panel slot");

        // ---- 2 + 3. the operable set, and nothing else ----
        // Every read-out row is checked, including the whole read-only equipment
        // block: changing the loadout belongs to the ship's loadout page.
        string[] infoRows =
        {
            "EquipHeading", "EquipHarvester", "EquipBattery", "EquipDrone",
            "StateHeading", "ActionState", "SignalState", "RecoveredState",
            "NavHeading", "NavDistance",
            "HarvestHeading", "HarvestNode", "HarvestState", "HarvestProgress"
        };

        var items = _focus != null ? _focus.ActiveGroup : null;
        Check(items != null, "the wrist focus navigator is built");
        Check(IsShipMode || items.Count > 0,
            "the surface terminal always has at least the central map to focus");

        bool infoExcluded = true;
        foreach (string name in infoRows)
        {
            Label label = _root.Q<Label>(name);
            if (label != null && items != null && items.Contains(label))
            {
                infoExcluded = false;
                lines.AppendLine("FAIL :: information row is focusable: " + name);
            }
        }

        Check(infoExcluded,
            "no pure information row can take focus (" + infoRows.Length + " checked)");

        bool everyItemFocusable = true;
        bool everyItemOperable = true;
        foreach (VisualElement item in items ?? new List<VisualElement>())
        {
            everyItemFocusable &= UiFocusNavigator.IsFocusable(item);
            string itemName = item.name;
            bool operable = itemName == MapFocusAnchorName
                            || (itemName == "NavTarget" && NavigationFocusable);
            if (!operable)
            {
                everyItemOperable = false;
                lines.AppendLine("FAIL :: registered element is not operable: " + itemName);
            }
        }

        Check(everyItemFocusable,
            "every registered focus element is focusable (visible, enabled, laid out)");
        Check(everyItemOperable,
            "every registered focus element really acts (central map / navigation)");

        // The equipment block is read-only: it lists exactly what is equipped and
        // is never a focus stop, no matter what the loadout is.
        bool equipmentExcluded = true;
        foreach (TextRow row in new[] { _harvesterRow, _batteryRow, _droneRow })
        {
            if (row == null || row.Label == null)
            {
                continue;
            }

            if (items != null && items.Contains(row.Label))
            {
                equipmentExcluded = false;
                lines.AppendLine("FAIL :: equipment row is focusable: " + row.ElementName);
            }
        }

        lines.AppendLine("INFO :: equipment listed=["
                         + string.Join(",", ListedEquipmentNames) + "] equipped b/d="
                         + (_session != null && _session.BatterySelected) + "/"
                         + (_session != null && _session.DroneSelected));
        Check(equipmentExcluded,
            "the read-only equipment block is never a focus stop");

        // The navigation row is the ONLY conditional element.
        bool navPresent = items != null && NavTargetRow() != null
                          && items.Contains(NavTargetRow().Label);
        lines.AppendLine("INFO :: focus items=" + (items != null ? items.Count : 0)
                         + " navigationActive=" + NavigationFocusable
                         + " navRowFocusable=" + navPresent);
        Check(navPresent == NavigationFocusable,
            "the navigation row is focusable exactly while a target is set");

        // ---- 4. status chips stay embedded and clear of the map footer ----
        Rect health = ResolvedRectOf(_healthLabel);
        Rect satiety = ResolvedRectOf(_satietyLabel);
        Rect identity = ResolvedRectOf(_identityLabel);
        lines.AppendLine("INFO :: chips health=" + health.x.ToString("0") + ","
                         + health.y.ToString("0") + " " + health.width.ToString("0")
                         + "x" + health.height.ToString("0")
                         + " satiety=" + satiety.x.ToString("0") + ","
                         + satiety.y.ToString("0") + " " + satiety.width.ToString("0")
                         + "x" + satiety.height.ToString("0"));
        Check(health.height <= 24f && satiety.height <= 24f,
            "health / satiety read-outs are small embedded chips (<= 24 px tall)");
        Check(health.top < 160f, "health chip sits at the upper arc");
        Check(satiety.top > 600f, "satiety chip sits at the lower arc");
        Check(!RectsOverlap(health, identity) && !RectsOverlap(satiety, identity),
            "the status chips never overlap the map footer");

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

            // PASS 3: the surface opens in the accepted PASS 2 cursor mode with no
            // focus ring (the map is the first thing the player can use); the ship
            // terminal is an ordinary UI and opens straight into module focus.
            _focus?.SetFocus(null);
            _padMode = IsShipMode ? WristPadMode.ModuleFocus : WristPadMode.MapCursor;
            RefreshFocusItems();
            RefreshSessionData();
            RefreshNavigation();
            RefreshMapGeometry();
            RefreshActiveProgress();
            RefreshTextRowLayout();
            ApplyPadMode();
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

            // Phase 09B: a closed map owns no pointer region and reopens at the
            // authored 1:1 zoom, so every open starts from the same known view.
            UiPointerState.ClearBounds();
            ResetMapZoom();
            _hoverNodeId = -1;

            // PASS 3: a closed terminal holds no focus ring either.
            _focus?.SetFocus(null);
            _radial?.SetFocusModule(WristModuleId.None);
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

        // PASS 3: focus model + embedded status chips, printed on every wrist open
        // so the Release screenshot log carries the checkable half of the
        // controller-UX contract.
        bool focusOk = VerifyWristFocusModel(out string focusReport);
        if (focusOk)
        {
            Debug.Log("[WristFocus] OK\n" + focusReport);
        }
        else
        {
            Debug.LogError("[WristFocus] FAIL\n" + focusReport);
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

    /// <summary>
    /// Reads one wrist-pixel of the terminal's own RenderTexture (3x3 average, so
    /// the map's dot lattice cannot flip a sample). This is the objective half of
    /// the PASS 3 visual contract: it lets a probe measure that the four plates
    /// really are darker than the central map and that the status arcs carry the
    /// right colour families, without a human squinting at a screenshot.
    /// </summary>
    public bool TrySampleWristPixel(Vector2 wristPixel, out Color color)
    {
        color = Color.clear;
        if (_renderTexture == null || !_renderTexture.IsCreated())
        {
            return false;
        }

        int cx = Mathf.Clamp(Mathf.RoundToInt(wristPixel.x), 1, (int)UiWidth - 2);

        // ReadPixels reads from the BOTTOM-left corner of the render texture, while
        // wrist pixels are top-down: the Y is converted exactly once here, so a probe
        // sampling the upper health arc can never be handed the lower satiety arc.
        int cy = Mathf.Clamp((int)UiHeight - 1 - Mathf.RoundToInt(wristPixel.y),
            1, (int)UiHeight - 2);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = _renderTexture;
        var readback = new Texture2D(3, 3, TextureFormat.ARGB32, false);
        readback.ReadPixels(new Rect(cx - 1, cy - 1, 3, 3), 0, 0, false);
        readback.Apply();

        Color sum = Color.clear;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                sum += readback.GetPixel(x, y);
            }
        }

        RenderTexture.active = previous;
        Object.Destroy(readback);
        color = sum / 9f;
        return true;
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

    private void UpdateParallax()
    {
        if (_projectionPivot == null)
        {
            return;
        }

        // The hologram tilt follows the SHARED pointer, so the pad tilts the
        // hologram exactly like the mouse always did. When nothing drives the
        // pointer (an automation run with no mouse device and no stick), the value
        // the caller set is kept - the Phase 06 parallax contract.
        if (UiPointerState.IsDrivenByDevice)
        {
            Vector2 pos = UiPointerState.Position;
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

        // Phase 09B: the open terminal polls the device itself, so the prompt and
        // the cursor follow whichever device the player picked up last (the
        // exploration player controller is frozen while the map is open).
        InputDeviceTracker.Poll();

        // The cursor is the SHARED pointer: the mouse writes it, the right stick
        // moves it, and both go through the same position this frame.
        UiPointerState.Tick(Time.unscaledDeltaTime);
        RefreshPointerBounds();
        RefreshZoom(Time.unscaledDeltaTime);

        // PASS 3: the mode follows the control the player used, then that mode
        // owns the A button for this frame - the shared focus navigator reads
        // Confirm only while it is enabled, so one press can never both confirm a
        // focused element and select a map node.
        UpdatePadMode();
        RefreshFocusItems();
        _focus?.Tick();

        EnsureStyleSheets();
        RefreshMapGeometry();
        RefreshActiveProgress();
        RefreshRadar();
        UpdateParallax();
        ProcessPointer();

        // Light-weight presentation animation (breathing arcs, state plate,
        // radar sweep, harvest data flow, focus/pulse smoothing).
        _radial?.Animate(Time.unscaledDeltaTime);
    }

    /// <summary>
    /// Navigation radar state: standby sweep while NO target is set, a live
    /// bearing line pointing at the target's direction on the map once one is
    /// ("未设置 → ? / 雷达待机; 已设置 → 有效导航状态").
    /// </summary>
    private void RefreshRadar()
    {
        if (_radial == null)
        {
            return;
        }

        bool active = false;
        float bearing = 0f;
        if (NavigationFocusable)
        {
            SurfaceResourceNode target = _controller.GetNodeById(
                _controller.NavigationTargetNodeId);
            if (target != null)
            {
                active = true;
                bearing = RadialWristElement.WristAngleDeg(
                    WristMapCoordinates.WorldToWristMap(target.transform.position, _mapZoom));
            }
        }

        _radial.SetRadar(active, bearing);

        if (_navRadarGlyph == null)
        {
            return;
        }

        // The radar exists only where the surface map is drawn: inside the ship the
        // centre is a deck schematic, so its standby symbol is withdrawn.
        _navRadarGlyph.style.display = IsShipMode
            ? DisplayStyle.None
            : DisplayStyle.Flex;

        if (IsShipMode)
        {
            return;
        }

        if (active != _navRadarActive)
        {
            _navRadarActive = active;
            _navRadarGlyph.text = active ? string.Empty : "?";
            if (active)
            {
                _navRadarGlyph.AddToClassList("radar-active");
            }
            else
            {
                _navRadarGlyph.RemoveFromClassList("radar-active");
            }
        }
    }

    /// <summary>
    /// Declares the region the pad may move the cursor inside: the projection of
    /// the wrist display quad. Together with the shared pointer's clamp this is
    /// what guarantees the cursor can never be pushed off the hologram and lost.
    /// </summary>
    private void RefreshPointerBounds()
    {
        if (_display == null || _mainCamera == null || Screen.width <= 0)
        {
            UiPointerState.ClearBounds();
            return;
        }

        Transform t = _display.transform;
        Vector3 scale = t.localScale;
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = t.position + t.right * (sx * scale.x * 0.5f)
                                 + t.up * (sy * scale.y * 0.5f);
                Vector3 screen = _mainCamera.WorldToScreenPoint(corner);
                if (screen.z <= 0f)
                {
                    continue;
                }

                any = true;
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }
        }

        if (!any)
        {
            UiPointerState.ClearBounds();
            return;
        }

        UiPointerState.SetBounds(new Rect(minX, minY, maxX - minX, maxY - minY));
    }

    /// <summary>
    /// LT zoom out / RT zoom in (and the mouse wheel), integrated at a steady rate
    /// and clamped to the authored zoom range. At the default zoom nothing changes,
    /// so the frozen Phase 06 map mapping is preserved exactly.
    /// </summary>
    private void RefreshZoom(float deltaTime)
    {
        float axis = UiInput.ZoomAxis();
        float wheel = UiInput.ZoomWheelSteps();
        if (Mathf.Approximately(axis, 0f) && Mathf.Approximately(wheel, 0f))
        {
            return;
        }

        SetMapZoom(_mapZoom
                   + axis * ZoomRatePerSecond * Mathf.Max(0f, deltaTime)
                   + wheel * ZoomWheelStep);
    }

    /// <summary>Sets the map content zoom, clamped to the authored range.</summary>
    public void SetMapZoom(float zoom)
    {
        _mapZoom = Mathf.Clamp(zoom, WristMapCoordinates.MinZoom, WristMapCoordinates.MaxZoom);
    }

    /// <summary>Returns the map to its default 1:1 mapping.</summary>
    public void ResetMapZoom()
    {
        _mapZoom = 1f;
    }

    // ---- Shared pointer (mouse OR right stick) -> display ray -> wrist pixel ----

    /// <summary>Screen-space node hover for probes: maps the SHARED pointer
    /// position (mouse or pad, whichever drives it) through the display collider
    /// to a node id (-1 = none).</summary>
    public int HitTestFromPointer()
    {
        if (_mainCamera == null || _display == null)
        {
            return -1;
        }

        Ray ray = _mainCamera.ScreenPointToRay(UiPointerState.Position);
        if (!_display.RaycastToLocal(ray, out Vector2 uv))
        {
            return -1;
        }

        return HitTestNode(UVToWristPixel(uv));
    }

    /// <summary>
    /// The map's pointer frame, driven by the shared pointer authority:
    ///
    ///   * hover      - the dot+ring cursor reacts to the node under the pointer,
    ///                  whether the mouse or the right stick put it there;
    ///   * select     - the Confirm action (Xbox A / E / Enter) is the EXACT
    ///                  equivalent of the mouse's left button on the map
    ///                  ("A：执行鼠标左键等价操作"), and both call the same single
    ///                  selector, so a pad and a mouse can never select differently;
    ///   * no cursor  - the visual cursor stays the existing OS cursor; the pad
    ///                  moves it through UiPointerState's warp. No second, pad-only
    ///                  map and no UI Toolkit cursor overlay exist.
    /// </summary>
    private void ProcessPointer()
    {
        if (_controller == null || _mainCamera == null || _display == null)
        {
            return;
        }

        Ray ray = _mainCamera.ScreenPointToRay(UiPointerState.Position);
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

        if (hit < 0)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;

        // Confirm is the map's left button ONLY in cursor mode. In module focus
        // mode the shared focus navigator owns the press (it is enabled exactly
        // then), so A can never do two things at once.
        bool confirmed = _padMode == WristPadMode.MapCursor && UiInput.ConfirmPressed();

        // Confirm is read once per frame: the surface interactor cannot act while
        // the wrist is open (exploration input is frozen), so the same press can
        // never both select a map target and interact with the world.
        if (clicked || (confirmed && _lastSelectedByConfirmFrame != Time.frameCount))
        {
            _lastSelectedByConfirmFrame = Time.frameCount;
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
    /// or -1. Draw position and hit-test share WristMapCoordinates (and the
    /// current map zoom) so marker and hit geometry can never drift apart;
    /// Depleted nodes are skipped.</summary>
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

            Vector2 nodePixel = WristMapCoordinates.WorldToWristMap(node.transform.position,
                _mapZoom);
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
            _healthLabel.text = _session.PlayerHealth.ToString();
        }

        if (_satietyLabel != null)
        {
            _satietyLabel.text = _session.PlayerSatiety.ToString();
        }

        RefreshEquipmentRows();

        SurfaceResourceNode active = _controller != null ? _controller.ActiveNode : null;
        if (_actionRow != null)
        {
            string actionState = active != null
                ? (active.State == SurfaceResourceNode.NodeState.Harvesting ? "采集中" : "无线传输")
                : (ReturnMode == WristReturnMode.ShipExplore ? "舰内" : "勘测中");

            // PASS 3: a real state CHANGE flashes the 当前状态 plate edge briefly
            // (contract §5 "状态变化时短暂强化"); the steady state only breathes.
            if (_lastActionStateText != null && _lastActionStateText != actionState)
            {
                _radial?.Pulse(WristModuleId.State);
            }

            _lastActionStateText = actionState;
            _actionRow.Label.text = actionState;
        }

        if (_recoveredRow != null)
        {
            string recovered = "回收 " + _session.HarvestedNodeCount
                               + "/" + GameSession.SurfaceNodeCount;

            // The recovery counter IS the state module's live value: when it
            // moves, the plate flashes once (contract §5 "状态变化时短暂强化").
            if (_lastRecoveredStateText != null && _lastRecoveredStateText != recovered)
            {
                _radial?.Pulse(WristModuleId.State);
            }

            _lastRecoveredStateText = recovered;
            _recoveredRow.Label.text = recovered;
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

    /// <summary>
    /// The 当前装备 block is a READ-ONLY read-out of what the expedition is
    /// actually carrying (Phase 09B PASS 3 revision):
    ///
    ///   * 采集器 MK-I is mission-required and always listed;
    ///   * 备用电池 / 勘测无人机 are listed ONLY while they are really equipped;
    ///   * an unequipped module is not drawn at all - there is no muted "not
    ///     equipped" line, no fake inventory and nothing to toggle here (the
    ///     loadout authority is the ship's loadout page, not the wrist);
    ///   * the listed rows are packed into the panel's first slots, so a shorter
    ///     loadout can never leave a hole where a module used to be.
    /// </summary>
    private void RefreshEquipmentRows()
    {
        var equipped = new List<TextRow>();
        if (_session != null && _session.HarvesterSelected && _harvesterRow != null)
        {
            equipped.Add(_harvesterRow);
        }

        if (_session != null && _session.BatterySelected && _batteryRow != null)
        {
            equipped.Add(_batteryRow);
        }

        if (_session != null && _session.DroneSelected && _droneRow != null)
        {
            equipped.Add(_droneRow);
        }

        var all = new[] { _harvesterRow, _batteryRow, _droneRow };
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
            {
                continue;
            }

            int slot = equipped.IndexOf(all[i]);
            bool listed = slot >= 0;
            all[i].Hidden = !listed;
            all[i].Label.text = listed ? EquipmentName(all[i]) : string.Empty;
            if (listed)
            {
                // Packed into the block's own slots (268 / 296 / 322).
                all[i].LayoutCenterY = EquipmentSlotY(slot);
            }
        }

        // The packing changes where the listed rows sit, so the slot layout is
        // re-applied immediately (a shortened list must never keep a stale hole).
        RefreshTextRowLayout();
    }

    /// <summary>Authored Y of equipment slot 0..2 in the upper-left panel band.</summary>
    public static float EquipmentSlotY(int slot)
    {
        return slot <= 0 ? 268f : (slot == 1 ? 296f : 322f);
    }

    private static string EquipmentName(TextRow row)
    {
        switch (row.ElementName)
        {
            case "EquipHarvester":
                return "采集器 MK-I";
            case "EquipBattery":
                return "备用电池";
            case "EquipDrone":
                return "勘测无人机";
            default:
                return row.ElementName;
        }
    }

    /// <summary>Element names of the equipment rows the terminal is listing right
    /// now (in panel order). Diagnostics / tests / evidence.</summary>
    public string[] ListedEquipmentNames
    {
        get
        {
            var names = new List<string>();
            var all = new[] { _harvesterRow, _batteryRow, _droneRow };
            foreach (TextRow row in all)
            {
                if (row != null && !row.Hidden)
                {
                    names.Add(row.ElementName);
                }
            }

            return names.ToArray();
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
            SetMarkerPosition(_playerMarker,
                WristMapCoordinates.WorldToWristMap(playerPos, _mapZoom));
            _playerMarker.style.rotate = new Rotate(new Angle(-_controller.Player.YawDegrees));
        }

        if (_landingMarker != null)
        {
            SetMarkerPosition(_landingMarker,
                WristMapCoordinates.WorldToWristMap(Vector3.zero, _mapZoom));
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

            SetMarkerPosition(_nodeMarkers[i],
                WristMapCoordinates.WorldToWristMap(node.transform.position, _mapZoom));
            ApplyMarkerState(_nodeMarkers[i], node, _controller.NavigationTargetNodeId == i);
        }

        if (_selectedRing != null)
        {
            if (_controller.IsNavigationActive)
            {
                SurfaceResourceNode target = _controller.GetNodeById(_controller.NavigationTargetNodeId);
                if (target != null)
                {
                    SetMarkerPosition(_selectedRing,
                        WristMapCoordinates.WorldToWristMap(target.transform.position, _mapZoom));
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
            // No player is physically harvesting inside the ship: the line is static.
            _radial?.SetHarvestFlowing(false);
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

        // PASS 3: thin dashes slide along the harvest plate's rail ONLY while
        // data is really moving (harvesting / transmitting); idle = static line.
        _radial?.SetHarvestFlowing(active != null);

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

        // PASS 3: the navigation module only becomes an OPERABLE element while a
        // target is set (unset = a standby radar read-out, never a focus stop),
        // and the radar switches from sweep to live bearing.
        RefreshFocusItems();
        RefreshRadar();
        ApplyPadMode();
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
