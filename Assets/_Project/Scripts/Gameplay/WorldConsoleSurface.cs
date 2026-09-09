using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Physical world-space operation console (Phase 07B).
///
/// One instance per terminal. Owns the console's always-on presentation:
/// - a real RenderTexture (UI native resolution, created once),
/// - a per-console PanelSettings instance bound to that texture,
/// - the UIDocument hosting the EXISTING MainApp tree with exactly one
///   production screen made visible (Overview / Preparation / Processing /
///   Outpost) through ShipConsoleScreenHost,
/// - the screen renderer's unlit material referencing the RenderTexture,
/// - the dedicated screen collider used for real pointer routing,
/// - the deterministic ConsoleFocusAnchor camera pose.
///
/// The four terminals keep their screens rendering even while unfocused
/// (always-on contract); ShipConsoleFocusController switches which surface
/// receives pointer input. Rendering never pauses when the player walks away.
/// </summary>
public class WorldConsoleSurface : MonoBehaviour
{
    public const int UiWidth = 1920;
    public const int UiHeight = 1080;

    [SerializeField]
    private int _consoleId = 1;

    [SerializeField]
    private Renderer _screenRenderer;

    [SerializeField]
    private Transform _screenQuad;

    [SerializeField]
    private Collider _screenCollider;

    [SerializeField]
    private Transform _focusAnchor;

    [SerializeField]
    private VisualTreeAsset _appUxml;

    [SerializeField]
    private StyleSheet _themeStyleSheet;

    [SerializeField]
    private StyleSheet _overviewStyleSheet;

    [SerializeField]
    private StyleSheet _preparationStyleSheet;

    [SerializeField]
    private StyleSheet _processingStyleSheet;

    [SerializeField]
    private StyleSheet _outpostStyleSheet;

    private RenderTexture _renderTexture;
    private PanelSettings _panelSettings;
    private UIDocument _document;
    private ShipConsoleScreenHost _screenHost;
    private Material _screenMaterial;

    public int ConsoleId => _consoleId;

    public RenderTexture CurrentRenderTexture => _renderTexture;

    public PanelSettings CurrentPanelSettings => _panelSettings;

    public UIDocument Document => _document;

    public Renderer ScreenRenderer => _screenRenderer;

    /// <summary>The UI host that shows exactly one production screen.</summary>
    public ShipConsoleScreenHost ScreenHost => _screenHost;

    public Collider ScreenCollider => _screenCollider;

    /// <summary>The world quad that displays the console RenderTexture.</summary>
    public Transform ScreenQuad => _screenQuad;

    /// <summary>Deterministic focused-camera pose authored by the builder.</summary>
    public Transform FocusAnchor => _focusAnchor;

    /// <summary>True once the surface fully initialized its RT + UI chain.</summary>
    public bool IsReady => _renderTexture != null && _renderTexture.IsCreated()
                           && _document != null && _screenHost != null
                           && _screenHost.IsBound;

    /// <summary>Physical display aspect (width / height), used by the aspect
    /// validation and by pointer pixel mapping.</summary>
    public float PhysicalScreenAspect => _screenQuad != null
        ? Mathf.Abs(_screenQuad.lossyScale.x) / Mathf.Max(0.0001f,
            Mathf.Abs(_screenQuad.lossyScale.y))
        : 0f;

    /// <summary>Builder configuration (scene authoring only).</summary>
    public void Configure(int consoleId, Renderer screenRenderer, Transform screenQuad,
        Collider screenCollider, Transform focusAnchor, VisualTreeAsset appUxml,
        StyleSheet theme, StyleSheet overview, StyleSheet preparation,
        StyleSheet processing, StyleSheet outpost)
    {
        _consoleId = consoleId;
        _screenRenderer = screenRenderer;
        _screenQuad = screenQuad;
        _screenCollider = screenCollider;
        _focusAnchor = focusAnchor;
        _appUxml = appUxml;
        _themeStyleSheet = theme;
        _overviewStyleSheet = overview;
        _preparationStyleSheet = preparation;
        _processingStyleSheet = processing;
        _outpostStyleSheet = outpost;
    }

    private void Awake()
    {
        EnsureRuntimeResources();
    }

    private void OnDestroy()
    {
        if (_document != null && _document.panelSettings != null)
        {
            _document.panelSettings.targetTexture = null;
        }

        if (_panelSettings != null)
        {
            // Runtime-created PanelSettings instance; released with the RT.
            Object.Destroy(_panelSettings);
            _panelSettings = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Object.Destroy(_renderTexture);
            _renderTexture = null;
        }
    }

    private void EnsureRuntimeResources()
    {
        if (_screenRenderer == null)
        {
            Debug.LogError("[WorldConsoleSurface] console " + _consoleId
                           + " missing screen renderer");
            return;
        }

        // Real RenderTexture at the existing UI native resolution.
        _renderTexture = new RenderTexture(UiWidth, UiHeight, 0,
            RenderTextureFormat.ARGB32)
        {
            name = "ConsoleRT_" + _consoleId.ToString("00"),
            filterMode = FilterMode.Bilinear
        };
        _renderTexture.Create();

        // Per-console PanelSettings (ConstantPixelSize = 1 layout px per RT
        // texel, so the 1920x1080 production UI is never stretched).
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.name = "ConsolePanel_" + _consoleId.ToString("00");
        _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        _panelSettings.referenceResolution = new Vector2Int(UiWidth, UiHeight);
        _panelSettings.scale = 1f;
        _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        _panelSettings.match = 0.5f;
        _panelSettings.targetTexture = _renderTexture;
        _panelSettings.clearColor = true;
        // UI canvas tone; any transparent RT region reads as monitor surface.
        _panelSettings.colorClearValue = new Color(0.914f, 0.922f, 0.929f, 1f);

        // Screen surface hosts the existing UI tree (single screen visible).
        var docGo = new GameObject("ConsoleUI");
        docGo.transform.SetParent(transform, false);
        docGo.SetActive(false);
        _document = docGo.AddComponent<UIDocument>();
        _document.visualTreeAsset = _appUxml;
        _document.panelSettings = _panelSettings;
        _screenHost = docGo.AddComponent<ShipConsoleScreenHost>();
        _screenHost.Configure(_consoleId, _themeStyleSheet, _overviewStyleSheet,
            _preparationStyleSheet, _processingStyleSheet, _outpostStyleSheet);
        docGo.SetActive(true);

        // Unlit monitor material so the UI reads faithfully in ship lighting.
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null)
        {
            unlit = Shader.Find("Unlit/Texture");
        }

        _screenMaterial = new Material(unlit)
        {
            name = "M_ConsoleScreen_" + _consoleId.ToString("00")
        };
        _screenMaterial.SetTexture("_BaseMap", _renderTexture);
        _screenMaterial.SetTexture("_MainTex", _renderTexture);
        _screenRenderer.sharedMaterial = _screenMaterial;
    }
}
