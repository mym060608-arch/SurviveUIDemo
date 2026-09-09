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
    private PanelSettings _panelSettingsBase;

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
        PanelSettings panelSettingsBase, StyleSheet theme, StyleSheet overview,
        StyleSheet preparation, StyleSheet processing, StyleSheet outpost)
    {
        _consoleId = consoleId;
        _screenRenderer = screenRenderer;
        _screenQuad = screenQuad;
        _screenCollider = screenCollider;
        _focusAnchor = focusAnchor;
        _appUxml = appUxml;
        _panelSettingsBase = panelSettingsBase;
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
        // Phase 07B.1 fix: prefer a CLONE of the authored Hub panel settings
        // asset - it carries the UI Toolkit runtime theme (themeUss) that a
        // bare runtime-created PanelSettings instance lacks. Without the
        // theme the UI Toolkit renderer logs "No Theme Style Sheet set ... UI
        // will not render properly" and never paints the console RenderTexture
        // in the Release player (black screens while structural tests still
        // pass). The clone is per-console and only its targetTexture/clear
        // settings differ per terminal.
        _panelSettings = _panelSettingsBase != null
            ? Object.Instantiate(_panelSettingsBase)
            : ScriptableObject.CreateInstance<PanelSettings>();
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

    // ====================================================================
    // Phase 07B.1 pixel evidence: reads the console RenderTexture directly so
    // "the UI is on the terminal" is proven by real pixels, not just by the
    // bound visual tree (structural tests previously passed while the Release
    // player showed black screens).
    // ====================================================================

    /// <summary>Samples the console RenderTexture on a regular grid and
    /// reports how much of the surface carries light (UI) pixels. Returns
    /// true (contentVisible) when more than ~35% of samples are light, which
    /// only happens once the UI Toolkit panel actually painted the light
    /// console background + content into the texture.</summary>
    public bool SampleScreenContent(out float lightRatio, out string summary)
    {
        lightRatio = 0f;
        summary = "";
        if (_renderTexture == null || !_renderTexture.IsCreated())
        {
            summary = "console " + _consoleId + " RT missing/not created";
            return false;
        }

        if (_document == null || _document.rootVisualElement == null)
        {
            summary = "console " + _consoleId + " document root missing";
            return false;
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = _renderTexture;
        Texture2D readback = new Texture2D(UiWidth, UiHeight,
            TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0f, 0f, UiWidth, UiHeight), 0, 0, false);
        readback.Apply();

        int lightCount = 0;
        int darkCount = 0;
        int total = 0;
        float sumLum = 0f;
        float minLum = 1f;
        float maxLum = 0f;
        for (int y = 40; y < UiHeight; y += 110)
        {
            for (int x = 40; x < UiWidth; x += 140)
            {
                Color c = readback.GetPixel(x, y);
                float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                total++;
                sumLum += lum;
                minLum = Mathf.Min(minLum, lum);
                maxLum = Mathf.Max(maxLum, lum);
                if (lum > 0.45f)
                {
                    lightCount++;
                }
                else if (lum < 0.10f)
                {
                    darkCount++;
                }
            }
        }

        Color centre = readback.GetPixel(UiWidth / 2, UiHeight / 2);
        Object.Destroy(readback);
        RenderTexture.active = previous;

        lightRatio = total > 0 ? lightCount / (float)total : 0f;
        summary = "console " + _consoleId
                  + " rt=" + _renderTexture.width + "x" + _renderTexture.height
                  + " alive=" + _renderTexture.IsCreated()
                  + " screen=" + ScreenHost.MappedScreen
                  + " visibleScreens=" + ScreenHost.CountVisibleScreens()
                  + " light=" + lightCount + "/" + total
                  + " (" + (lightRatio * 100f).ToString("0") + "%)"
                  + " dark=" + darkCount
                  + " avgLum=" + (total > 0 ? (sumLum / total).ToString("0.00") : "-")
                  + " minLum=" + minLum.ToString("0.00")
                  + " maxLum=" + maxLum.ToString("0.00")
                  + " centre=" + centre.r.ToString("0.00") + ","
                  + centre.g.ToString("0.00") + "," + centre.b.ToString("0.00");
        return lightRatio > 0.35f;
    }
}
