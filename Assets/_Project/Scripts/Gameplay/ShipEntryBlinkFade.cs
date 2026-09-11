using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// First-person entry eyelids (Phase 08 Revision 2, PASS 4).
///
/// 新游戏 ends on a full black frame and then loads 03_ShipInterior. Instead of
/// a plain fade, the interior now opens through TWO BLACK CURTAINS - a top
/// eyelid hanging from the upper edge and a bottom eyelid rising from the lower
/// edge - that meet on the centre line while the eyes are shut and retract as
/// the player wakes up:
///
///   closed -> Beat 1 (small crack, ~22%) -> closed
///          -> Beat 2 (bigger, ~45%)      -> closed
///          -> Beat 3 (fully open, stays)
///
/// The curtains change their real height (not an opacity trick), the animation
/// runs on unscaled time, movement/look stay locked until the third beat
/// completes, and both elements are removed when it ends so nothing is left to
/// block pointer or input.
///
/// It is production flow, not a test hook: SceneRouting flags the eyelid mode
/// for every 新游戏 (Full) / 继续游戏 (Quick) entry and the ship scene plays it
/// once. A direct -bootShip / airlock entry never animates.
/// </summary>
public class ShipEntryBlinkFade : MonoBehaviour
{
    /// <summary>Automation flag that forces the full eyelids in any ship entry.</summary>
    public const string AutomationFlag = "-blinkEntry";

    // ---- Full (新游戏) timeline, seconds ----
    public const float Beat1OpenEnd = 0.14f;
    public const float Beat1CloseEnd = 0.25f;
    public const float Beat2OpenEnd = 0.42f;
    public const float Beat2CloseEnd = 0.55f;
    public const float Beat3OpenEnd = 1.05f;

    /// <summary>Whole three-beat animation: 1.05 s (inside the 0.8-1.3 s spec).</summary>
    public const float BlinkSeconds = Beat3OpenEnd;

    // ---- Quick (继续游戏) timeline, seconds ----
    public const float QuickHoldEnd = 0.10f;
    public const float QuickOpenEnd = 0.55f;

    /// <summary>How far the eyes open at the first crack (fraction of screen).</summary>
    public const float Beat1Openness = 0.22f;

    /// <summary>How far the eyes open at the second attempt.</summary>
    public const float Beat2Openness = 0.45f;

    private VisualElement _root;
    private VisualElement _topEyelid;
    private VisualElement _bottomEyelid;
    private float _elapsed;
    private bool _playing;
    private bool _finished;

    /// <summary>Which animation this instance plays.</summary>
    public SceneRouting.EntryEyelid Mode { get; private set; } = SceneRouting.EntryEyelid.Full;

    /// <summary>True while the eyelids are animating.</summary>
    public bool IsPlaying => _playing;

    /// <summary>1 = fully open (clear view), 0 = fully shut (black screen).</summary>
    public float Openness { get; private set; }

    /// <summary>How much of the screen each curtain currently covers (0..0.5).</summary>
    public float CoveredFraction { get; private set; } = 0.5f;

    /// <summary>Human readable stage, used by the probes and the tests.</summary>
    public string StageLabel { get; private set; } = "none";

    /// <summary>The top curtain (removed when the animation ends).</summary>
    public VisualElement TopEyelid => _topEyelid;

    /// <summary>The bottom curtain (removed when the animation ends).</summary>
    public VisualElement BottomEyelid => _bottomEyelid;

    /// <summary>True when the ship entry should force the full eyelids.</summary>
    public static bool AutomationRequested
    {
        get
        {
            string[] args = System.Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (string.Equals(arg, AutomationFlag, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Runtime wiring: which animation to play.</summary>
    public void Configure(SceneRouting.EntryEyelid mode)
    {
        Mode = mode == SceneRouting.EntryEyelid.None
            ? SceneRouting.EntryEyelid.Full
            : mode;
    }

    private void Awake()
    {
        // Created during ShipGameController.Start, i.e. before the first frame of
        // this scene renders: the very first visible frame of the interior is
        // already the closed eyelid, so the blackout handed over by the title
        // screen is never broken by a flash of the cockpit.
        _root = ResolvePanelRoot();
        if (_root == null)
        {
            Debug.LogWarning("[ShipEntryBlinkFade] no UI Toolkit panel to draw the eyelids");
            enabled = false;
            return;
        }

        _topEyelid = CreateEyelid("TopEyelid", true);
        _bottomEyelid = CreateEyelid("BottomEyelid", false);
        ApplyCurtains(0f);
    }

    private VisualElement CreateEyelid(string name, bool top)
    {
        var eyelid = new VisualElement { name = name };
        eyelid.style.position = Position.Absolute;
        eyelid.style.left = 0f;
        eyelid.style.right = 0f;
        eyelid.style.backgroundColor = Color.black;
        eyelid.style.height = Length.Percent(50f);
        if (top)
        {
            eyelid.style.top = 0f;
        }
        else
        {
            eyelid.style.bottom = 0f;
        }

        // Pure presentation: it must never swallow a click that belongs to the
        // HUD or to a console modal.
        eyelid.pickingMode = PickingMode.Ignore;
        _root.Add(eyelid);
        return eyelid;
    }

    private void Start()
    {
        Play(Mode);
    }

    /// <summary>Plays the eyelid animation once. Idempotent.</summary>
    public void Play(SceneRouting.EntryEyelid mode)
    {
        if (_playing || _finished || _topEyelid == null)
        {
            return;
        }

        Configure(mode);
        _playing = true;
        _elapsed = 0f;
        Openness = 0f;
        CoveredFraction = 0.5f;
        StageLabel = "closed";
        ApplyCurtains(0f);
    }

    /// <summary>Jumps straight to the open view (automation safety net).</summary>
    public void FinishImmediately()
    {
        Tick(BlinkSeconds);
    }

    private void Update()
    {
        if (!_playing)
        {
            return;
        }

        // Clamped steps: the first frame of a freshly loaded scene can be long
        // (asset streaming), and a single hitch must never skip a whole beat of
        // the animation. The beats are the point - they have to be visible.
        Tick(Mathf.Min(Time.unscaledDeltaTime, 1f / 30f));
    }

    private void Tick(float delta)
    {
        if (!_playing)
        {
            return;
        }

        _elapsed += delta;
        Openness = OpennessAt(_elapsed, Mode);
        CoveredFraction = CoveredFractionFor(Openness);
        StageLabel = StageAt(_elapsed, Mode);
        ApplyCurtains(Openness);

        if (_elapsed < DurationSeconds(Mode))
        {
            return;
        }

        _playing = false;
        _finished = true;
        Openness = 1f;
        CoveredFraction = 0f;

        // The curtains are removed entirely: a finished transition must leave no
        // element behind that could intercept pointer, input or focus.
        _topEyelid?.RemoveFromHierarchy();
        _bottomEyelid?.RemoveFromHierarchy();
        _topEyelid = null;
        _bottomEyelid = null;
        Debug.Log("[ShipEntryBlinkFade] eyelid entry finished (" + Mode + ")");
        Destroy(this);
    }

    /// <summary>Height of ONE curtain for an openness value (0..0.5 of the frame).</summary>
    public static float CoveredFractionFor(float openness)
    {
        return (1f - Mathf.Clamp01(openness)) * 0.5f;
    }

    /// <summary>Total animation length for a mode.</summary>
    public static float DurationSeconds(SceneRouting.EntryEyelid mode)
    {
        return mode == SceneRouting.EntryEyelid.Quick ? QuickOpenEnd : BlinkSeconds;
    }

    /// <summary>
    /// The eyelid curve. It is deliberately NOT a linear fade: the double
    /// movement (small attempt, bigger attempt, full open) is what makes it read
    /// as waking up rather than dissolving.
    /// </summary>
    public static float OpennessAt(float t, SceneRouting.EntryEyelid mode)
    {
        if (t <= 0f)
        {
            return 0f;
        }

        if (mode == SceneRouting.EntryEyelid.Quick)
        {
            if (t <= QuickHoldEnd)
            {
                return 0f;
            }

            if (t >= QuickOpenEnd)
            {
                return 1f;
            }

            return Mathf.SmoothStep(0f, 1f, (t - QuickHoldEnd) / (QuickOpenEnd - QuickHoldEnd));
        }

        if (t < Beat1OpenEnd)
        {
            return Mathf.SmoothStep(0f, 1f, t / Beat1OpenEnd) * Beat1Openness;
        }

        if (t < Beat1CloseEnd)
        {
            return Mathf.Lerp(Beat1Openness, 0f,
                Mathf.SmoothStep(0f, 1f, (t - Beat1OpenEnd) / (Beat1CloseEnd - Beat1OpenEnd)));
        }

        if (t < Beat2OpenEnd)
        {
            return Mathf.SmoothStep(0f, 1f,
                (t - Beat1CloseEnd) / (Beat2OpenEnd - Beat1CloseEnd)) * Beat2Openness;
        }

        if (t < Beat2CloseEnd)
        {
            return Mathf.Lerp(Beat2Openness, 0f,
                Mathf.SmoothStep(0f, 1f, (t - Beat2OpenEnd) / (Beat2CloseEnd - Beat2OpenEnd)));
        }

        if (t < Beat3OpenEnd)
        {
            return Mathf.SmoothStep(0f, 1f,
                (t - Beat2CloseEnd) / (Beat3OpenEnd - Beat2CloseEnd));
        }

        return 1f;
    }

    /// <summary>Stage name for a given moment (probe reports + verification).</summary>
    public static string StageAt(float t, SceneRouting.EntryEyelid mode)
    {
        if (t <= 0f)
        {
            return "closed";
        }

        if (mode == SceneRouting.EntryEyelid.Quick)
        {
            if (t <= QuickHoldEnd)
            {
                return "closed";
            }

            return t >= QuickOpenEnd ? "open" : "quick-open";
        }

        if (t < Beat1OpenEnd)
        {
            return "beat1";
        }

        if (t < Beat1CloseEnd)
        {
            return "beat1-close";
        }

        if (t < Beat2OpenEnd)
        {
            return "beat2";
        }

        if (t < Beat2CloseEnd)
        {
            return "beat2-close";
        }

        if (t < Beat3OpenEnd)
        {
            return "beat3";
        }

        return "open";
    }

    /// <summary>Writes the curtain heights for the current openness.</summary>
    private void ApplyCurtains(float openness)
    {
        float percent = CoveredFractionFor(openness) * 100f;
        if (_topEyelid != null)
        {
            _topEyelid.style.height = Length.Percent(percent);
        }

        if (_bottomEyelid != null)
        {
            _bottomEyelid.style.height = Length.Percent(percent);
        }
    }

    /// <summary>
    /// Prefers the ship HUD panel (the scene's main screen-space UI Toolkit
    /// document); falls back to any other document in the scene.
    /// </summary>
    private static VisualElement ResolvePanelRoot()
    {
        ShipHudController hud = Object.FindAnyObjectByType<ShipHudController>();
        if (hud != null)
        {
            var document = hud.GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
            {
                return document.rootVisualElement;
            }
        }

        foreach (UIDocument candidate in Object.FindObjectsByType<UIDocument>(
                     FindObjectsInactive.Include))
        {
            if (candidate != null && candidate.rootVisualElement != null)
            {
                return candidate.rootVisualElement;
            }
        }

        return null;
    }
}
