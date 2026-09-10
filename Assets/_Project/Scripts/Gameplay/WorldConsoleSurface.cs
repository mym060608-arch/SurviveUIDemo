using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Physical world-space operation console (Phase 07B / 07B.2).
///
/// One instance per terminal. Owns the console's always-on presentation:
/// - a real RenderTexture (UI native resolution, created once),
/// - a per-console PanelSettings instance bound to that texture,
/// - the UIDocument hosting the console UI: consoles 1/3/4 host the EXISTING
///   MainApp tree with exactly one production screen made visible through
///   ShipConsoleScreenHost; Console02 (仓库总览) hosts the dedicated
///   WarehouseConsole tree through ShipWarehouseScreenHost,
/// - the screen renderer. Phase 07B.2 renders the display surface with an
///   AUTHORED URP/Unlit material asset (never Shader.Find at runtime: a
///   shader that no build asset references is stripped from the Release
///   player and the runtime-created material silently renders nothing), and
///   the per-console RenderTexture is bound through a per-renderer
///   MaterialPropertyBlock so one shared screen material can never cross-wire
///   the four terminals,
/// - the dedicated screen collider used for real pointer routing,
/// - the deterministic ConsoleFocusAnchor camera pose,
/// - a diagnostic override mode (solid magenta / checkerboard) that paints
///   the REAL physical display so the visible chain can be proven end to end
///   in the Release player before restoring the real UI.
///
/// The four terminals keep their screens rendering even while unfocused
/// (always-on contract); ShipConsoleFocusController switches which surface
/// receives pointer input. Rendering never pauses when the player walks away.
/// </summary>
public class WorldConsoleSurface : MonoBehaviour
{
    public const int UiWidth = 1920;
    public const int UiHeight = 1080;

    /// <summary>Camera filter layer for physical display surfaces
    /// (TagManager user layer 6). Base/environment camera never renders it;
    /// the sharp overlay camera renders only this layer.</summary>
    public const int ConsoleScreenLayer = 6;

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

    // ---- Phase 07B.2: Console02 warehouse terminal ----

    [SerializeField]
    private bool _useWarehouseUi;

    [SerializeField]
    private VisualTreeAsset _warehouseUxml;

    [SerializeField]
    private StyleSheet _warehouseStyleSheet;

    // ---- Phase 07B.2: authored unlit screen material (Release-safe) ----

    [SerializeField]
    private Material _screenMaterialAsset;

    private RenderTexture _renderTexture;
    private PanelSettings _panelSettings;
    private UIDocument _document;
    private ShipConsoleScreenHost _screenHost;
    private ShipWarehouseScreenHost _warehouseHost;
    private Material _runtimeFallbackMaterial;
    private Material _screenMaterialInstance;
    private Texture _overrideTexture;

    public int ConsoleId => _consoleId;

    public RenderTexture CurrentRenderTexture => _renderTexture;

    public PanelSettings CurrentPanelSettings => _panelSettings;

    public UIDocument Document => _document;

    public Renderer ScreenRenderer => _screenRenderer;

    /// <summary>The MainApp UI host (consoles 1/3/4; null on the warehouse
    /// terminal).</summary>
    public ShipConsoleScreenHost ScreenHost => _screenHost;

    /// <summary>The dedicated 仓库总览 host (Console02 only).</summary>
    public ShipWarehouseScreenHost WarehouseHost => _warehouseHost;

    public Collider ScreenCollider => _screenCollider;

    /// <summary>The world quad that displays the console RenderTexture.</summary>
    public Transform ScreenQuad => _screenQuad;

    /// <summary>Deterministic focused-camera pose authored by the builder.</summary>
    public Transform FocusAnchor => _focusAnchor;

    /// <summary>The unlit material on the physical screen (authored asset or
    /// runtime fallback). Never null after a successful init.</summary>
    public Material ScreenMaterial =>
        _screenRenderer != null ? _screenRenderer.sharedMaterial : null;

    /// <summary>True once the surface fully initialized its RT + UI chain.</summary>
    public bool IsReady => _renderTexture != null && _renderTexture.IsCreated()
                           && _document != null
                           && (_screenHost != null && _screenHost.IsBound
                               || _warehouseHost != null && _warehouseHost.IsBound);

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

    /// <summary>Builder configuration for Console02's warehouse terminal:
    /// the surface hosts the dedicated WarehouseConsole tree instead of the
    /// shared MainApp tree.</summary>
    public void ConfigureWarehouse(bool enabled, VisualTreeAsset warehouseUxml,
        StyleSheet warehouseStyleSheet)
    {
        _useWarehouseUi = enabled;
        _warehouseUxml = warehouseUxml;
        _warehouseStyleSheet = warehouseStyleSheet;
    }

    /// <summary>Builder configuration: authored URP/Unlit screen material
    /// asset (guarantees the shader survives Release stripping and is shared
    /// by every console; per-console texture binding uses property blocks).</summary>
    public void ConfigureScreenMaterial(Material materialAsset)
    {
        _screenMaterialAsset = materialAsset;
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

        if (_runtimeFallbackMaterial != null)
        {
            Object.Destroy(_runtimeFallbackMaterial);
            _runtimeFallbackMaterial = null;
        }

        if (_screenMaterialInstance != null)
        {
            Object.Destroy(_screenMaterialInstance);
            _screenMaterialInstance = null;
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
        // texel, so the 1920x1080 production UI is never stretched). Prefer a
        // CLONE of the authored Hub panel settings asset - it carries the UI
        // Toolkit runtime theme (themeUss) that a bare runtime-created
        // PanelSettings instance lacks (without it the UI Toolkit renderer
        // never paints the console RenderTexture in the Release player).
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

        // Screen surface hosts the console's UI tree.
        var docGo = new GameObject(_useWarehouseUi ? "WarehouseConsoleUI" : "ConsoleUI");
        docGo.transform.SetParent(transform, false);
        docGo.SetActive(false);
        _document = docGo.AddComponent<UIDocument>();
        if (_useWarehouseUi)
        {
            _document.visualTreeAsset = _warehouseUxml;
            _warehouseHost = docGo.AddComponent<ShipWarehouseScreenHost>();
            _warehouseHost.warehouseStyleSheet = _warehouseStyleSheet;
        }
        else
        {
            _document.visualTreeAsset = _appUxml;
            _screenHost = docGo.AddComponent<ShipConsoleScreenHost>();
            _screenHost.Configure(_consoleId, _themeStyleSheet, _overviewStyleSheet,
                _preparationStyleSheet, _processingStyleSheet, _outpostStyleSheet);
        }

        _document.panelSettings = _panelSettings;
        docGo.SetActive(true);

        // Phase 07B.2: authored unlit monitor material (Release shader-safe).
        // Every console instantiates its OWN material from the authored asset
        // and binds its OWN RenderTexture directly on that instance - no
        // shared-material overwrite, no property-block dependency - so one
        // console can never cross-wire another console's screen.
        Material displayMaterial = null;
        if (_screenMaterialAsset != null)
        {
            _screenMaterialInstance = Object.Instantiate(_screenMaterialAsset);
            _screenMaterialInstance.name = "M_ConsoleScreen_"
                                           + _consoleId.ToString("00");
            displayMaterial = _screenMaterialInstance;
        }

        if (displayMaterial == null)
        {
            // Legacy fallback (authoring only): build a URP Unlit material at
            // runtime. In Release players Shader.Find can return null for
            // stripped shaders - the authored asset above is the safe path.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
            {
                unlit = Shader.Find("Unlit/Texture");
            }

            if (unlit != null)
            {
                _runtimeFallbackMaterial = new Material(unlit)
                {
                    name = "M_ConsoleScreen_Runtime_" + _consoleId.ToString("00")
                };
                displayMaterial = _runtimeFallbackMaterial;
            }
        }

        if (displayMaterial == null)
        {
            Debug.LogError("[WorldConsoleSurface] console " + _consoleId
                           + " no usable screen material (asset missing and "
                           + "runtime shader lookup failed)");
            return;
        }

        _screenRenderer.sharedMaterial = displayMaterial;
        ConsoleUvAuthority.ApplyToMaterial(displayMaterial);
        ApplyScreenTexture();
    }

    // ====================================================================
    // Phase 07B.2.1 UV orientation diagnostic (TL / TR / BL / BR markers)
    // ====================================================================

    public const string UvRefRootName = "UvReferenceMarkers";

    /// <summary>Canonical marker colours: panel top-left RED, top-right GREEN,
    /// bottom-left BLUE, bottom-right YELLOW. The probe samples the projected
    /// screen corners and proves which canonical corner really appears where.</summary>
    public static readonly Color UvRefTopLeftColor = new Color(1f, 0f, 0f, 1f);
    public static readonly Color UvRefTopRightColor = new Color(0f, 1f, 0f, 1f);
    public static readonly Color UvRefBottomLeftColor = new Color(0f, 0f, 1f, 1f);
    public static readonly Color UvRefBottomRightColor = new Color(1f, 1f, 0f, 1f);

    private VisualElement _uvRefRoot;

    /// <summary>Shows/hides the four corner reference markers inside this
    /// console's real UI document (diagnostic only, never part of gameplay).
    /// The markers live in the real UI Toolkit tree, so they exercise the
    /// complete UI -> RenderTexture -> material chain.</summary>
    public bool SetUvReferenceMarkersVisible(bool visible)
    {
        if (_document == null || _document.rootVisualElement == null)
        {
            return false;
        }

        VisualElement root = _document.rootVisualElement;
        if (_uvRefRoot == null || _uvRefRoot.parent != root)
        {
            _uvRefRoot = new VisualElement { name = UvRefRootName };
            _uvRefRoot.pickingMode = PickingMode.Ignore;
            _uvRefRoot.style.position = Position.Absolute;
            _uvRefRoot.style.left = 0f;
            _uvRefRoot.style.top = 0f;
            _uvRefRoot.style.right = 0f;
            _uvRefRoot.style.bottom = 0f;
            AddUvRefMarker(_uvRefRoot, "UvRef_TL", true, true, UvRefTopLeftColor);
            AddUvRefMarker(_uvRefRoot, "UvRef_TR", false, true, UvRefTopRightColor);
            AddUvRefMarker(_uvRefRoot, "UvRef_BL", true, false, UvRefBottomLeftColor);
            AddUvRefMarker(_uvRefRoot, "UvRef_BR", false, false, UvRefBottomRightColor);
            root.Add(_uvRefRoot);
        }

        _uvRefRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        return true;
    }

    private static void AddUvRefMarker(VisualElement parent, string name, bool left,
        bool top, Color color)
    {
        var marker = new VisualElement { name = name };
        marker.pickingMode = PickingMode.Ignore;
        marker.style.position = Position.Absolute;
        marker.style.width = Length.Percent(22f);
        marker.style.height = Length.Percent(20f);
        if (left)
        {
            marker.style.left = 0f;
        }
        else
        {
            marker.style.right = 0f;
        }

        if (top)
        {
            marker.style.top = 0f;
        }
        else
        {
            marker.style.bottom = 0f;
        }

        marker.style.backgroundColor = color;
        parent.Add(marker);
    }

    /// <summary>Applies the effective screen texture (diagnostic override or
    /// the live console RenderTexture) directly on this console's material
    /// instance.</summary>
    private void ApplyScreenTexture()
    {
        Material material = _screenRenderer != null
            ? _screenRenderer.sharedMaterial
            : null;
        if (material == null)
        {
            return;
        }

        Texture effective = _overrideTexture != null ? _overrideTexture : _renderTexture;
        material.SetTexture("_BaseMap", effective);
        material.SetTexture("_MainTex", effective);
    }

    // ====================================================================
    // Phase 07B.2 visible-chain diagnostics (magenta / checkerboard / real UI)
    // ====================================================================

    /// <summary>Paints the REAL physical screen with a solid diagnostic
    /// texture (e.g. magenta). If the Release player shows the color, the
    /// whole RT -> material -> renderer -> camera chain is healthy and any
    /// missing content is an upstream UI problem.</summary>
    public void SetDiagnosticTexture(Texture texture)
    {
        _overrideTexture = texture;
        ApplyScreenTexture();
    }

    /// <summary>Restores the live console RenderTexture after diagnostics.</summary>
    public void ClearDiagnosticTexture()
    {
        _overrideTexture = null;
        ApplyScreenTexture();
    }

    /// <summary>Current texture actually sampled by the physical screen
    /// (null when nothing is bound). Reads this console's material instance.</summary>
    public Texture ReadBoundScreenTexture()
    {
        Material material = _screenRenderer != null
            ? _screenRenderer.sharedMaterial
            : null;
        if (material == null)
        {
            return null;
        }

        Texture texture = material.GetTexture("_BaseMap");
        return texture != null ? texture : material.GetTexture("_MainTex");
    }

    /// <summary>Visible-chain binding report: renderer state, material shader
    /// presence, sampled texture identity. Returns true only when the screen
    /// material is real (non-null shader) and samples this console's RT.</summary>
    public bool VerifyScreenBinding(out string report)
    {
        report = "console " + _consoleId;
        if (_screenRenderer == null)
        {
            report += " renderer=MISSING";
            return false;
        }

        report += " renderer.enabled=" + _screenRenderer.enabled
                  + " layer=" + _screenRenderer.gameObject.layer
                  + " (ConsoleScreen=" + ConsoleScreenLayer + ")";
        Material material = _screenRenderer.sharedMaterial;
        if (material == null)
        {
            report += " material=NULL";
            return false;
        }

        Shader shader = material.shader;
        report += " shader=" + (shader != null ? shader.name : "NULL");
        if (shader == null)
        {
            report += " <== null shader material renders nothing";
            return false;
        }

        Texture bound = ReadBoundScreenTexture();
        report += " boundTexture=" + (bound != null ? bound.name : "NULL")
                  + " expected=" + (_renderTexture != null ? _renderTexture.name : "NULL");
        if (bound == null || _renderTexture == null || bound != (Texture)_renderTexture)
        {
            report += " <== screen does not sample this console RT";
            return false;
        }

        return true;
    }

    // ====================================================================
    // RenderTexture pixel evidence (kept from Phase 07B.1: proves the UI
    // panel painted real content into the RT - upstream half of the chain).
    // ====================================================================

    /// <summary>Samples the console RenderTexture on a regular grid and
    /// reports how much of the surface carries light (UI) pixels. Returns
    /// true (contentVisible) when more than ~35% of samples are light.</summary>
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
                  + " ui=" + (_useWarehouseUi ? "Warehouse" : (ScreenHost != null
                      ? ScreenHost.MappedScreen.ToString() : "?"))
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

    /// <summary>Creates a tiny solid-color Texture2D used by the diagnostic
    /// stage (caller owns and destroys it).</summary>
    public static Texture2D CreateSolidTexture(Color color, string name)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        texture.name = name;
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Repeat;
        return texture;
    }

    /// <summary>Creates a small black/white checkerboard Texture2D used by
    /// the diagnostic stage (caller owns and destroys it).</summary>
    public static Texture2D CreateCheckerTexture(string name)
    {
        const int cell = 4;
        const int width = 32;
        var texture = new Texture2D(width, width, TextureFormat.RGBA32, false);
        texture.name = name;
        var pixels = new Color[width * width];
        for (int y = 0; y < width; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool light = ((x / cell) + (y / cell)) % 2 == 0;
                pixels[y * width + x] = light
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(0f, 0f, 0f, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        return texture;
    }
}
