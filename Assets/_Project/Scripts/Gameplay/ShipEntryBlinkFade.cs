using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// First-person "eyelid" entry fade for the ship interior (Phase 08 Revision,
/// PASS 4).
///
/// The Main Menu's 进入任务 cinematic ends on a full black frame and then loads
/// 03_ShipInterior. Without this component the player would simply appear in the
/// cockpit; with it the interior opens through a short double-blink, which is
/// what makes the hand-over read as "I woke up inside my ship".
///
/// It is production flow, not a test hook: <see cref="SceneRouting"/> flags the
/// blink for every 进入任务 entry (SceneRouting.ShipEntry.FromMainMenu) and the
/// ship scene plays it once. A direct -bootShip / airlock entry never blinks.
/// Automation may force it with -blinkEntry so a player build can be validated
/// on the same presentation.
///
/// The overlay is a plain UI Toolkit element appended to the existing ship HUD
/// panel: no canvas, no second camera, no scene authoring change, and it removes
/// itself completely (element + component) when the blink finishes.
/// </summary>
public class ShipEntryBlinkFade : MonoBehaviour
{
    /// <summary>Automation flag that forces the blink in any ship entry.</summary>
    public const string AutomationFlag = "-blinkEntry";

    /// <summary>Eyes fully shut before anything opens.</summary>
    public const float ClosedSeconds = 0.22f;

    /// <summary>First crack open.</summary>
    public const float OpenSeconds = 0.10f;

    /// <summary>Involuntary re-close.</summary>
    public const float RecloseSeconds = 0.10f;

    /// <summary>Final full opening.</summary>
    public const float SettleSeconds = 0.55f;

    /// <summary>Whole blink duration: 0.97 s.</summary>
    public const float BlinkSeconds = ClosedSeconds + OpenSeconds + RecloseSeconds
                                      + SettleSeconds;

    /// <summary>Black level the eyelids reach at the first crack open.</summary>
    public const float FirstOpenAlpha = 0.42f;

    /// <summary>Black level of the involuntary re-close.</summary>
    public const float RecloseAlpha = 0.90f;

    private VisualElement _root;
    private VisualElement _overlay;
    private float _elapsed;
    private bool _playing;
    private bool _finished;

    /// <summary>True while the blink is being played.</summary>
    public bool IsPlaying => _playing;

    /// <summary>0 = fully open (clear view), 1 = fully shut (black).</summary>
    public float Alpha { get; private set; } = 1f;

    /// <summary>Human readable stage, used by the standalone probe report.</summary>
    public string StageLabel { get; private set; } = "none";

    /// <summary>True when the automation flag asks for a forced blink.</summary>
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

    private void Awake()
    {
        // Created during ShipGameController.Start, i.e. before the first frame of
        // this scene is rendered: the very first visible frame of the interior is
        // already the closed eyelid, so the blackout handed over by the menu is
        // never broken by a flash of the cockpit.
        _root = ResolvePanelRoot();
        if (_root == null)
        {
            Debug.LogWarning("[ShipEntryBlinkFade] no UI Toolkit panel to fade");
            enabled = false;
            return;
        }

        _overlay = new VisualElement { name = "ShipEntryBlink" };
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = 0f;
        _overlay.style.top = 0f;
        _overlay.style.right = 0f;
        _overlay.style.bottom = 0f;
        _overlay.style.backgroundColor = Color.black;
        _overlay.style.opacity = 1f;
        // A pure presentation layer: it must never swallow a click that belongs
        // to the HUD or to a console modal.
        _overlay.pickingMode = PickingMode.Ignore;
        _root.Add(_overlay);
    }

    private void Start()
    {
        PlayBlink();
    }

    /// <summary>Plays the blink once. Idempotent.</summary>
    public void PlayBlink()
    {
        if (_playing || _finished || _overlay == null)
        {
            return;
        }

        _playing = true;
        _elapsed = 0f;
        Alpha = 1f;
        StageLabel = "closed";
        _overlay.style.opacity = 1f;
    }

    /// <summary>Skips straight to the open view (automation safety net).</summary>
    public void FinishImmediately()
    {
        _elapsed = BlinkSeconds;
        Tick(0f);
    }

    private void Update()
    {
        if (!_playing)
        {
            return;
        }

        Tick(Time.unscaledDeltaTime);
    }

    private void Tick(float delta)
    {
        if (!_playing)
        {
            return;
        }

        _elapsed += delta;
        Alpha = AlphaAt(_elapsed);
        StageLabel = StageAt(_elapsed);

        if (_overlay != null)
        {
            _overlay.style.opacity = Alpha;
        }

        if (_elapsed < BlinkSeconds)
        {
            return;
        }

        _playing = false;
        _finished = true;
        Alpha = 0f;

        // The overlay is removed entirely: a finished transition must leave no
        // element behind that could intercept input or invalidate the panel.
        _overlay?.RemoveFromHierarchy();
        _overlay = null;
        Debug.Log("[ShipEntryBlinkFade] entry blink finished");
        Destroy(this);
    }

    /// <summary>Stage name for a given moment (report + verification).</summary>
    public static string StageAt(float t)
    {
        if (t <= 0f)
        {
            return "closed";
        }

        if (t < ClosedSeconds)
        {
            return "closed";
        }

        if (t < ClosedSeconds + OpenSeconds)
        {
            return "opening";
        }

        if (t < ClosedSeconds + OpenSeconds + RecloseSeconds)
        {
            return "reclosing";
        }

        if (t < BlinkSeconds)
        {
            return "settling";
        }

        return "open";
    }

    /// <summary>
    /// The eyelid curve: shut, crack open, reflexive re-close, then a slow full
    /// open. It is deliberately NOT a linear fade - the double movement is what
    /// makes it read as a blink instead of a scene dissolve.
    /// </summary>
    public static float AlphaAt(float t)
    {
        if (t <= 0f)
        {
            return 1f;
        }

        if (t < ClosedSeconds)
        {
            return 1f;
        }

        float openEnd = ClosedSeconds + OpenSeconds;
        if (t < openEnd)
        {
            return Mathf.Lerp(1f, FirstOpenAlpha,
                Mathf.SmoothStep(0f, 1f, (t - ClosedSeconds) / OpenSeconds));
        }

        float recloseEnd = openEnd + RecloseSeconds;
        if (t < recloseEnd)
        {
            return Mathf.Lerp(FirstOpenAlpha, RecloseAlpha,
                Mathf.SmoothStep(0f, 1f, (t - openEnd) / RecloseSeconds));
        }

        if (t < BlinkSeconds)
        {
            return Mathf.Lerp(RecloseAlpha, 0f,
                Mathf.SmoothStep(0f, 1f, (t - recloseEnd) / SettleSeconds));
        }

        return 0f;
    }

    /// <summary>
    /// Prefers the ship HUD panel (it is the scene's main screen-space UI
    /// Toolkit document); falls back to any other document in the scene.
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
