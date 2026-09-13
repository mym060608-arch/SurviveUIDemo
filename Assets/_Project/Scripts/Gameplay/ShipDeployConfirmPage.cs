using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Landing Confirmation / 最终确认 page (Phase 07B.2; rebuilt in Phase 09B PASS 8).
///
/// Shown by the Airlock deployment station BEFORE any ShipInterior -> Surface
/// routing: the destination (shared <see cref="PlanetIdentity"/> display name),
/// the CURRENT expedition equipment summary (live GameSession read-only, text
/// only - no pictograms on this page) and THREE action cards that carry their
/// own key glyphs:
///
///   [X] 出发准备 / 调整装备  -> ContextAction -> the SAME shared loadout view
///   [A] 确认降临 / 长按确认  -> Confirm (HELD) -> the real surface routing
///   [B] 返回 / 取消操作      -> Back          -> back to ShipExplore
///
/// There is deliberately NO second prompt row: the glyph on each card IS the
/// key line, so the page can never print a key twice.
///
/// Hold Confirm (the point of this pass): a plain tap of Confirm NEVER lands.
/// Holding the logical Confirm action (or the mouse on the A card) fills the
/// progress bar INSIDE that card from 0 to 100%; only a completed hold raises
/// <see cref="ConfirmLandingRequested"/>. Releasing early rolls the bar back to
/// zero and keeps the player on this page - a half press can never deploy.
/// The hold only starts from a FRESH press: the press that opened the page (and
/// its synthetic pointer echo) is ignored until Confirm has been released once,
/// which is what makes "不能自动开始 Hold" true.
///
/// Business logic only ever names the platform-neutral actions
/// (<see cref="UiAction.Confirm"/> / <see cref="UiAction.Back"/> /
/// <see cref="UiAction.ContextAction"/>); the Xbox / PlayStation / keyboard
/// glyphs come from <see cref="InputDeviceTracker.PromptGlyph"/>, so no
/// AButton / BButton / XButton name exists anywhere in the page.
///
/// The GameObject/UIDocument stays active; visibility is driven by the root
/// display style so the panel tree binds once and never re-instantiates.
/// </summary>
public class ShipDeployConfirmPage : MonoBehaviour
{
    public const string DestinationName = "DeployDestination";
    public const string HarvesterStateName = "DeployHarvesterState";
    public const string BatteryStateName = "DeployBatteryState";
    public const string DroneStateName = "DeployDroneState";
    public const string CargoValueName = "DeployCargoValue";
    public const string AdjustGearButtonName = "DeployAdjustGearButton";
    public const string ConfirmButtonName = "DeployConfirmButton";
    public const string CancelButtonName = "DeployCancelButton";

    // Phase 09B PASS 8: the key glyphs live inside the cards, and the hold
    // progress bar lives inside the confirm card.
    public const string AdjustKeyName = "DeployAdjustKey";
    public const string ConfirmKeyName = "DeployConfirmKey";
    public const string CancelKeyName = "DeployCancelKey";
    public const string HoldFillName = "DeployHoldFill";

    /// <summary>The page's own Back verb. The device glyph is added by
    /// <see cref="InputDeviceTracker"/>, so the card reads "返回" and the hint
    /// string composed from it is "[B] 返回" / "[Esc] 返回".</summary>
    public const string BackVerb = "返回";

    /// <summary>Verb on the context-action card (X / □ / M).</summary>
    public const string AdjustVerb = "出发准备";

    /// <summary>Verb on the primary card.</summary>
    public const string ConfirmVerb = "确认降临";

    /// <summary>
    /// How long the player must hold Confirm to land. This is the SINGLE
    /// authored hold duration for this page (there is no other hold constant
    /// anywhere), tuned to feel deliberate without dragging.
    /// </summary>
    public const float HoldSeconds = 1.10f;

    /// <summary>
    /// Rollback speed when the player releases early, as a multiple of the fill
    /// rate: the bar empties noticeably faster than it filled, so a cancelled
    /// attempt reads as "reset", never as "almost kept it".
    /// </summary>
    public const float HoldReleaseRate = 2.6f;

    /// <summary>Short confirmation beat between 100% and the real routing.</summary>
    public const float HoldCompleteFeedbackSeconds = 0.20f;

    private const string CardHoldClass = "landing-card-holding";
    private const string CardCompleteClass = "landing-card-complete";
    private const string CardFocusClass = "landing-card-focused";

    [SerializeField]
    public StyleSheet modalStyleSheet;

    // Panel settings assigned at runtime because UIDocument.panelSettings does
    // not serialize reliably from the deterministic scene builder (same
    // convention as the ship HUD / console hosts).
    [SerializeField]
    public PanelSettings panelSettings;

    private UIDocument _document;
    private VisualElement _root;
    private GameSession _session;
    private bool _bound;
    private bool _visible;

    private Label _harvesterState;
    private Label _batteryState;
    private Label _droneState;
    private Label _cargoValue;

    private Label _adjustKey;
    private Label _confirmKey;
    private Label _cancelKey;
    private Label _cancelVerb;
    private Label _adjustVerb;
    private Label _confirmVerb;

    private Button _adjustButton;
    private Button _confirmButton;
    private Button _cancelButton;
    private VisualElement _holdFill;

    private int _shownFrame = -1000;

    // ---- Hold Confirm state (Phase 09B PASS 8) ----
    private float _holdProgress01;
    private bool _holdArmed;
    private bool _pointerHolding;
    private bool _completed;
    private float _completeElapsed;
    private VisualElement _focusedCard;

    /// <summary>User asked to edit the shared loadout (X or the 出发准备 card).</summary>
    public event Action AdjustGearRequested;

    /// <summary>User completed the hold: perform the real routing.</summary>
    public event Action ConfirmLandingRequested;

    /// <summary>User cancelled: back to ShipExplore.</summary>
    public event Action CancelRequested;

    public bool IsBound => _bound;

    public VisualElement Root => _root;

    public UIDocument Document => _document;

    public bool IsVisible => _visible;

    /// <summary>Hold progress 0..1 (tests / probes).</summary>
    public float HoldProgress01 => _holdProgress01;

    /// <summary>True while the player is really holding Confirm.</summary>
    public bool IsHolding { get; private set; }

    /// <summary>True during the short "completed" feedback beat.</summary>
    public bool IsHoldComplete => _completed;

    /// <summary>How many times a COMPLETED hold asked for the real landing
    /// (a cancelled or partial hold must never increase this).</summary>
    public int ConfirmRequestCount { get; private set; }

    /// <summary>The card that currently carries the focus frame.</summary>
    public VisualElement FocusedCard => _focusedCard;

    /// <summary>The device-aware Back hint this page shows, composed from the
    /// B card itself ("[B] 返回" on a controller, "[Esc] 返回" on the keyboard).
    /// The page has no separate hint row any more.</summary>
    public string BackHintText => ComposeHint(_cancelKey, _cancelVerb);

    /// <summary>Device-aware hint of the context-action card ("[X] 出发准备").</summary>
    public string AdjustHintText => ComposeHint(_adjustKey, _adjustVerb);

    /// <summary>Device-aware hint of the primary card ("[A] 确认降临").</summary>
    public string ConfirmHintText => ComposeHint(_confirmKey, _confirmVerb);

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        if (_document != null && _document.panelSettings == null && panelSettings != null)
        {
            _document.panelSettings = panelSettings;
        }
    }

    private void Update()
    {
        if (!_bound && _document != null && _document.rootVisualElement != null
            && _document.rootVisualElement.childCount > 0)
        {
            EnsureBound();
        }

        // The card glyphs name the device the player is actually holding, so
        // they are refreshed while the page is on screen (gamepad <-> keyboard).
        if (_bound && _visible)
        {
            InputDeviceTracker.Poll();
            RefreshGlyphs();
        }
    }

    private void OnDestroy()
    {
        if (_session != null)
        {
            _session.OnStateChanged -= HandleSessionChanged;
        }
    }

    private void EnsureBound()
    {
        if (_bound || _document == null || _document.rootVisualElement == null
            || _document.rootVisualElement.childCount == 0)
        {
            return;
        }

        _root = _document.rootVisualElement;
        if (modalStyleSheet != null && !_root.styleSheets.Contains(modalStyleSheet))
        {
            _root.styleSheets.Add(modalStyleSheet);
        }

        ApplyUiCjkFont();

        _harvesterState = BindLabel(HarvesterStateName);
        _batteryState = BindLabel(BatteryStateName);
        _droneState = BindLabel(DroneStateName);
        _cargoValue = BindLabel(CargoValueName);

        _adjustKey = BindLabel(AdjustKeyName);
        _confirmKey = BindLabel(ConfirmKeyName);
        _cancelKey = BindLabel(CancelKeyName);
        _holdFill = BindElement(HoldFillName);

        _adjustButton = BindButton(AdjustGearButtonName, () => AdjustGearRequested?.Invoke());
        _cancelButton = BindButton(CancelButtonName, () => CancelRequested?.Invoke());

        // The primary card has NO click handler: landing is reachable ONLY
        // through a completed hold (see TickLandingInput).
        _confirmButton = BindButton(ConfirmButtonName, null);
        BindConfirmCard(_confirmButton);

        _adjustVerb = VerbLabelOf(_adjustButton);
        _confirmVerb = VerbLabelOf(_confirmButton);
        _cancelVerb = VerbLabelOf(_cancelButton);

        MountDotMatrices();
        BindCardFocus();

        // The gamepad confirm press that OPENS this panel is also turned into a
        // synthetic pointer click by Unity's runtime UI (the same echo that once
        // activated 退出 on the title screen). On the frame the panel appears
        // that click IS the opening press, not a second action, so it must not
        // activate anything inside the panel - otherwise one press would open
        // and immediately confirm/cancel the deployment.
        _root.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (IsOpeningPressEcho())
            {
                Debug.Log("[OVERLAY] ignored the click produced by the press "
                          + "that opened 最终确认");
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);

        _session = GameSession.Ensure();
        _session.OnStateChanged += HandleSessionChanged;
        RefreshAll();
        RefreshGlyphs();
        SetFocusCard(_confirmButton);
        _bound = true;
        _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>Mounts the two code-drawn cyan dot fields once.</summary>
    private void MountDotMatrices()
    {
        MountDotMatrix("LandingDotsLeft");
        MountDotMatrix("LandingDotsRight");
    }

    private void MountDotMatrix(string hostName)
    {
        VisualElement host = _root.Q<VisualElement>(hostName);
        if (host == null)
        {
            Debug.LogError("[ShipDeployConfirmPage] missing dot host " + hostName);
            return;
        }

        if (host.Q<LandingDotMatrix>(null) != null)
        {
            return;
        }

        host.Add(new LandingDotMatrix());
    }

    /// <summary>
    /// True while a click can only be the echo of the press that opened the
    /// panel (the opening frame, plus one frame of delivery slack).
    /// </summary>
    private bool IsOpeningPressEcho()
    {
        return Time.frameCount <= _shownFrame + 1
               && UiInput.GamepadConfirmPressedThisFrame();
    }

    // ---- Hold Confirm (Phase 09B PASS 8) ----

    private void BindConfirmCard(Button card)
    {
        if (card == null)
        {
            return;
        }

        // Mouse / pointer hold: pressing the card IS holding Confirm, so the
        // same progress path serves every device. The callbacks run in the
        // TRICKLE-DOWN phase: a Button's own Clickable handles the press first
        // in the bubble phase and stops it there, so a bubble-phase listener
        // would never see the hold.
        card.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                _pointerHolding = true;
            }
        }, TrickleDown.TrickleDown);
        card.RegisterCallback<PointerUpEvent>(_ => _pointerHolding = false,
            TrickleDown.TrickleDown);
        card.RegisterCallback<PointerCancelEvent>(_ => _pointerHolding = false,
            TrickleDown.TrickleDown);
    }

    private void BindCardFocus()
    {
        FocusOnHover(_adjustButton);
        FocusOnHover(_confirmButton);
        FocusOnHover(_cancelButton);
    }

    private void FocusOnHover(Button card)
    {
        if (card == null)
        {
            return;
        }

        card.RegisterCallback<PointerEnterEvent>(_ => SetFocusCard(card));
        card.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            // The primary CTA is the authored default focus.
            if (ReferenceEquals(_focusedCard, card))
            {
                SetFocusCard(_confirmButton);
            }
        });
    }

    private void SetFocusCard(VisualElement card)
    {
        _focusedCard = card;
        ApplyClass(_adjustButton, CardFocusClass, ReferenceEquals(card, _adjustButton));
        ApplyClass(_confirmButton, CardFocusClass, ReferenceEquals(card, _confirmButton));
        ApplyClass(_cancelButton, CardFocusClass, ReferenceEquals(card, _cancelButton));
    }

    /// <summary>
    /// Per-frame hold logic. The host calls it ONLY while this page is the
    /// overlay in charge, and <paramref name="interactive"/> is false while the
    /// game is paused, so a frozen or hidden page can never accumulate progress.
    /// </summary>
    public void TickLandingInput(bool interactive)
    {
        if (!_bound)
        {
            return;
        }

        if (!interactive || !_visible)
        {
            CancelHold();
            return;
        }

        InputDeviceTracker.Poll();
        RefreshGlyphs();

        // X / □ / M: the context action opens the SAME shared loadout view the
        // card opens (one code path, no second equipment flow).
        if (UiInput.ContextActionPressed())
        {
            AdjustGearRequested?.Invoke();
            return;
        }

        if (_completed)
        {
            // The hold is already complete: show the confirmation beat, then run
            // the REAL routing exactly once. The beat is measured in game time,
            // so a frozen game (pause, or an automation freeze for a screenshot)
            // holds the 100% frame instead of racing through it.
            _completeElapsed += Time.deltaTime;
            if (_completeElapsed >= HoldCompleteFeedbackSeconds)
            {
                FinishCompletedHold();
            }

            return;
        }

        bool confirmHeld = UiInput.ConfirmHeld();
        if (!confirmHeld)
        {
            // A fresh press is required: the press that opened this page (and the
            // one that closed the loadout) can never start a hold by itself.
            _holdArmed = true;
        }

        bool holding = (confirmHeld && _holdArmed) || _pointerHolding;
        IsHolding = holding;

        if (holding)
        {
            _holdProgress01 = Mathf.Clamp01(_holdProgress01 + Time.deltaTime / HoldSeconds);
            if (_holdProgress01 >= 1f)
            {
                CompleteHold();
                return;
            }
        }
        else if (_holdProgress01 > 0f)
        {
            // Released early: roll the bar back, never land.
            _holdProgress01 = Mathf.Max(0f,
                _holdProgress01 - Time.deltaTime * HoldReleaseRate / HoldSeconds);
        }

        RefreshHoldPresentation();
    }

    private void CompleteHold()
    {
        _holdProgress01 = 1f;
        _completed = true;
        _completeElapsed = 0f;
        IsHolding = false;
        _pointerHolding = false;
        ApplyClass(_confirmButton, CardCompleteClass, true);
        ApplyClass(_confirmButton, CardHoldClass, false);
        RefreshHoldPresentation();
        Debug.Log("[LANDING] hold complete (100%) - awaiting the confirmation beat");
    }

    private void FinishCompletedHold()
    {
        _completed = false;
        ConfirmRequestCount++;
        ApplyClass(_confirmButton, CardCompleteClass, false);
        _holdProgress01 = 0f;
        RefreshHoldPresentation();
        Debug.Log("[LANDING] hold confirmed - running the real deployment routing");
        ConfirmLandingRequested?.Invoke();
    }

    /// <summary>Cancels a partial hold and resets every hold visual.</summary>
    public void CancelHold()
    {
        _pointerHolding = false;
        IsHolding = false;
        _completed = false;
        _holdArmed = false;
        if (_holdProgress01 != 0f)
        {
            _holdProgress01 = 0f;
        }

        ApplyClass(_confirmButton, CardHoldClass, false);
        ApplyClass(_confirmButton, CardCompleteClass, false);
        RefreshHoldPresentation();
    }

    private void RefreshHoldPresentation()
    {
        if (_holdFill != null)
        {
            _holdFill.style.width = Length.Percent(Mathf.Clamp01(_holdProgress01) * 100f);
        }

        bool holding = IsHolding && !_completed;
        ApplyClass(_confirmButton, CardHoldClass, holding);
    }

    // ---- Device presentation ----

    /// <summary>
    /// Fills the three card glyphs for the device in hand. Presentation only:
    /// the logical actions never change, only the printed glyph
    /// (X / A / B, □ / ✕ / ○ or M / E / Esc).
    /// </summary>
    private void RefreshGlyphs()
    {
        SetText(_adjustKey, InputDeviceTracker.PromptGlyph(UiAction.ContextAction));
        SetText(_confirmKey, InputDeviceTracker.PromptGlyph(UiAction.Confirm));
        SetText(_cancelKey, InputDeviceTracker.PromptGlyph(UiAction.Back));
    }

    private static string ComposeHint(Label key, Label verb)
    {
        if (key == null || verb == null || string.IsNullOrEmpty(key.text))
        {
            return string.Empty;
        }

        return "[" + key.text + "] " + verb.text;
    }

    private static Label VerbLabelOf(VisualElement card)
    {
        return card != null ? card.Q<Label>(null, "landing-card-verb") : null;
    }

    private static void ApplyClass(VisualElement element, string className, bool on)
    {
        if (element == null)
        {
            return;
        }

        if (on)
        {
            element.AddToClassList(className);
        }
        else
        {
            element.RemoveFromClassList(className);
        }
    }

    private void HandleSessionChanged()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_session == null)
        {
            _session = GameSession.Ensure();
        }

        SetState(_harvesterState, "必需", true);
        SetState(_batteryState, _session.BatterySelected ? "已装备" : "未选择",
            _session.BatterySelected);
        SetState(_droneState, _session.DroneSelected ? "已装备" : "未选择",
            _session.DroneSelected);
        if (_cargoValue != null)
        {
            _cargoValue.text = _session.CargoWeight + " / "
                               + GameSession.CargoCapacityKg + " KG";
        }
    }

    private static void SetState(Label label, string text, bool on)
    {
        if (label == null)
        {
            return;
        }

        label.text = text;
        if (on)
        {
            label.AddToClassList("modal-state-required");
        }
        else
        {
            label.RemoveFromClassList("modal-state-required");
        }
    }

    private static void SetText(Label label, string text)
    {
        if (label != null && label.text != text)
        {
            label.text = text;
        }
    }

    private Label BindLabel(string name)
    {
        Label label = _root != null ? _root.Q<Label>(name) : null;
        if (label == null)
        {
            Debug.LogError("[ShipDeployConfirmPage] missing label " + name);
        }

        return label;
    }

    private VisualElement BindElement(string name)
    {
        VisualElement element = _root != null ? _root.Q<VisualElement>(name) : null;
        if (element == null)
        {
            Debug.LogError("[ShipDeployConfirmPage] missing element " + name);
        }

        return element;
    }

    private Button BindButton(string name, Action onClick)
    {
        Button button = _root != null ? _root.Q<Button>(name) : null;
        if (button == null)
        {
            Debug.LogError("[ShipDeployConfirmPage] missing button " + name);
            return null;
        }

        if (onClick != null)
        {
            button.clicked += onClick;
        }

        return button;
    }

    /// <summary>Show / hide the page (display style; tree stays bound).</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (visible)
        {
            _shownFrame = Time.frameCount;
            RefreshGlyphs();
            RefreshAll();
            SetFocusCard(_confirmButton);
        }
        else
        {
            ReleaseFocus();
        }

        // Never leave a half-filled hold behind a hidden page.
        CancelHold();

        if (_root != null)
        {
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>
    /// Drops any UI Toolkit focus this page still holds, so a closed panel can
    /// never keep swallowing input (Phase 08 device-panel hotfix: no hidden UI
    /// may own the keyboard focus after Back closed it).
    /// </summary>
    private void ReleaseFocus()
    {
        if (_root == null || _root.focusController == null)
        {
            return;
        }

        Focusable focused = _root.focusController.focusedElement;
        if (focused == null)
        {
            return;
        }

        var element = focused as VisualElement;
        if (element != null && _root.Contains(element))
        {
            focused.Blur();
        }
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
}
