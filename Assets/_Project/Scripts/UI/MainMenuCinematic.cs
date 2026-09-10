using UnityEngine;

/// <summary>
/// Phase 08 Revision start cinematic (PASS 3).
///
/// 进入任务 must never cut straight to another UI screen. This component owns the
/// whole 2.9 s shot and the product flow behind it:
///
///   Idle Menu -&gt; StartPressed -&gt; MenuFadeOut -&gt; EngineBoost
///             -&gt; ShipDeparture -&gt; FadeToBlack -&gt; LoadShipInterior
///
/// The timeline is a single unscaled clock: every observable value (menu alpha,
/// black alpha, engine level, camera push, star parallax) is a pure function of
/// the elapsed time, so the shot is identical in the Editor, in a player build
/// and in an automated run, and a dropped frame can never desynchronise it.
///
/// Ownership: the cinematic drives the menu layer, the hero ship, the starfield
/// and the stage camera, and it is the ONLY place that asks SceneRouting to
/// enter the ship interior. It never creates, resets or mutates GameSession.
/// </summary>
[DefaultExecutionOrder(100)]
public class MainMenuCinematic : MonoBehaviour
{
    /// <summary>Phase names, in playback order (§11.1).</summary>
    public enum Phase
    {
        Idle,
        StartPressed,
        MenuFadeOut,
        EngineBoost,
        ShipDeparture,
        FadeToBlack,
        LoadShipInterior,
        Done
    }

    // ---- Timeline (seconds) ----
    public const float MenuFadeOutSeconds = 0.45f;
    public const float EngineBoostSeconds = 0.55f;
    public const float ShipDepartureSeconds = 1.15f;
    public const float FadeToBlackSeconds = 0.55f;
    public const float BlackHoldSeconds = 0.25f;

    /// <summary>Total shot length: 2.95 s.</summary>
    public const float TotalSeconds = MenuFadeOutSeconds + EngineBoostSeconds
                                      + ShipDepartureSeconds + FadeToBlackSeconds
                                      + BlackHoldSeconds;

    // Segment boundaries. The animation and the reported phase share these
    // numbers, so the ship can never start flying during the boost phase.
    /// <summary>Engine boost starts with the menu fade.</summary>
    public const float EngineBoostStart = MenuFadeOutSeconds;
    /// <summary>The ship starts flying exactly when the departure phase starts.</summary>
    public const float DepartureStart = MenuFadeOutSeconds + EngineBoostSeconds;
    /// <summary>The blackout starts once the ship is gone.</summary>
    public const float BlackoutStart = DepartureStart + ShipDepartureSeconds;

    // ---- Authored references ----
    [SerializeField]
    private MainMenuController menu;

    [SerializeField]
    private MainMenuShipHero hero;

    [SerializeField]
    private MainMenuStarfield starfield;

    [SerializeField]
    private Camera stageCamera;

    /// <summary>Metres the camera creeps forward across the shot.</summary>
    [SerializeField]
    private float cameraPushMetres = 5.5f;

    private float _elapsed;
    private bool _playing;
    private bool _departureStarted;
    private bool _routeRequested;
    private Vector3 _cameraHome;

    /// <summary>True while the shot is playing (or while the scene swap is pending).</summary>
    public bool IsPlaying => _playing;

    /// <summary>Current phase.</summary>
    public Phase CurrentPhase { get; private set; } = Phase.Idle;

    /// <summary>Seconds since 进入任务 was pressed.</summary>
    public float ElapsedSeconds => _elapsed;

    /// <summary>Opacity of the whole menu layer (1 = fully readable).</summary>
    public float MenuAlpha { get; private set; } = 1f;

    /// <summary>Opacity of the black transition overlay.</summary>
    public float BlackAlpha { get; private set; }

    /// <summary>True once the black overlay fully covers the frame.</summary>
    public bool IsBlackoutComplete => BlackAlpha >= 0.999f;

    /// <summary>Runtime wiring (called by the scene builder).</summary>
    public void Configure(MainMenuController menuController, MainMenuShipHero shipHero,
        MainMenuStarfield stars, Camera camera)
    {
        menu = menuController;
        hero = shipHero;
        starfield = stars;
        stageCamera = camera;
    }

    private void Awake()
    {
        if (stageCamera != null)
        {
            _cameraHome = stageCamera.transform.localPosition;
        }
    }

    private void Start()
    {
        if (menu == null)
        {
            menu = Object.FindAnyObjectByType<MainMenuController>();
        }

        if (hero == null)
        {
            hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        }

        if (starfield == null)
        {
            starfield = Object.FindAnyObjectByType<MainMenuStarfield>();
        }

        if (stageCamera == null)
        {
            stageCamera = Camera.main != null
                ? Camera.main
                : Object.FindAnyObjectByType<Camera>();
        }

        if (stageCamera != null)
        {
            _cameraHome = stageCamera.transform.localPosition;
        }
    }

    /// <summary>
    /// Starts the shot. Idempotent: a second 进入任务 (or a test call) while the
    /// shot is playing is ignored, so the timeline can never double-fire.
    /// </summary>
    public void Begin()
    {
        if (_playing)
        {
            return;
        }

        _playing = true;
        _elapsed = 0f;
        _departureStarted = false;
        _routeRequested = false;
        CurrentPhase = Phase.StartPressed;
        MenuAlpha = 1f;
        BlackAlpha = 0f;
        ApplyVisuals();

        if (menu != null)
        {
            menu.SetCinematicBlackoutVisible(true);
        }

        Debug.Log("[MainMenuCinematic] start cinematic (" + TotalSeconds + "s)");
    }

    private void Update()
    {
        if (!_playing || _routeRequested)
        {
            return;
        }

        // The shot runs on unscaled time: a menu must never depend on Time.timeScale.
        _elapsed += Time.unscaledDeltaTime;
        ApplyVisuals();

        if (_elapsed >= TotalSeconds)
        {
            _routeRequested = true;
            CurrentPhase = Phase.LoadShipInterior;
            LoadShipInterior();
        }
    }

    private void ApplyVisuals()
    {
        float t = _elapsed;

        // ---- MenuFadeOut: title, subtitle, CTA, HUD and annotations ----
        float fadeT = Mathf.Clamp01(t / MenuFadeOutSeconds);
        MenuAlpha = 1f - fadeT;
        float engineIdle = Mathf.SmoothStep(0f, 1f, fadeT) * 0.35f;

        // ---- EngineBoost ----
        float boostT = Mathf.Clamp01((t - EngineBoostStart) / EngineBoostSeconds);
        float engineBoost = Mathf.SmoothStep(0f, 1f, boostT);
        float engine = Mathf.Lerp(engineIdle, 1f, engineBoost);

        // ---- ShipDeparture ----
        // The ship starts flying exactly when its phase starts, so the reported
        // phase and the motion can never disagree.
        float departureT = Mathf.Clamp01((t - DepartureStart) / ShipDepartureSeconds);
        if (!_departureStarted && t >= DepartureStart)
        {
            _departureStarted = true;
            hero?.BeginDeparture();
        }

        // ---- FadeToBlack ----
        float blackT = Mathf.Clamp01((t - BlackoutStart) / FadeToBlackSeconds);
        BlackAlpha = Mathf.SmoothStep(0f, 1f, blackT);

        CurrentPhase = PhaseFor(t);
        hero?.SetEngineLevel(engine);
        if (menu != null)
        {
            menu.SetCinematicMenuAlpha(MenuAlpha);
            menu.SetCinematicBlackoutAlpha(BlackAlpha);
        }

        if (starfield != null)
        {
            starfield.SetParallaxBoost(departureT * 0.6f);
        }

        if (stageCamera != null)
        {
            // Camera creep: mostly during the departure, so the frame answers
            // the ship instead of simply cutting.
            float push = Mathf.SmoothStep(0f, 1f, departureT) * cameraPushMetres
                         + Mathf.SmoothStep(0f, 1f, boostT) * cameraPushMetres * 0.25f;
            stageCamera.transform.localPosition = _cameraHome + new Vector3(0f, 0f, push);
        }
    }

    private static Phase PhaseFor(float t)
    {
        if (t < EngineBoostStart)
        {
            return Phase.MenuFadeOut;
        }

        if (t < DepartureStart)
        {
            return Phase.EngineBoost;
        }

        if (t < BlackoutStart)
        {
            return Phase.ShipDeparture;
        }

        return Phase.FadeToBlack;
    }

    /// <summary>
    /// Leaves the menu for the walkable ship interior in first person. The blink
    /// entry request is what makes the ship scene open with the eyelid fade
    /// instead of a hard cut (PASS 4).
    /// </summary>
    private void LoadShipInterior()
    {
        Debug.Log("[MainMenuCinematic] blackout complete -> ship interior (blink entry)");
        SceneRouting.EnterShipInterior(SceneRouting.ShipEntry.FromMainMenu);
    }

    /// <summary>Explicitly finishes the shot (automation / tests).</summary>
    public void CompleteImmediately()
    {
        if (!_playing)
        {
            Begin();
        }

        _elapsed = TotalSeconds;
        ApplyVisuals();
    }
}
