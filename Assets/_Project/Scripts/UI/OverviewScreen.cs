using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen 01 - Overview (Phase 02 main visual; controller + visual pass in
/// Phase 09B PASS 7).
///
/// Reads GameSession and renders ship status exactly once per state change:
/// - survival-critical rows (能源 / 食物) show live bars and numeric values,
/// - resource inventory rows show plain counts,
/// - the destination hero label and the mission objective line both read the
///   planet display name from <see cref="PlanetIdentity"/> (one source of truth,
///   so the page can never show two different planets at once),
/// - the centre planet is the shared rotating 3D preview (or the procedural
///   disc fallback) and is the page's first visual.
///
/// Controller contract (the user's sketch):
///   RS / right stick   rotate the planet: left-right spins it horizontally,
///                      up-down inspects a limited latitude band
///   mouse drag         the same rotation, for a mouse+keyboard player
///   B                  leaves the page's context (host-owned: the Hub page
///                      never owns Back, the ship's console focus layer does)
///
/// The page never owns the rotation state: it forwards the dead-zoned stick to
/// <see cref="OverviewPlanetPreview"/>, which stays the single authority for the
/// planet's orientation. Nothing here can move the camera, the player or another
/// page; <see cref="Tick"/> is only ever called by the host that currently shows
/// this page.
/// </summary>
public class OverviewScreen
{
    /// <summary>Player-facing label of the planet rotation row.</summary>
    public const string RotatePromptLabel = "旋转星球";

    /// <summary>
    /// Authored exploration read-out. Phase 01/02 have no exploration state on
    /// the session (the surface slice tracks harvested nodes, not a survey
    /// percentage), so this page keeps presenting the authored survey figure the
    /// approved reference shows. Gameplay data is untouched by this pass.
    /// </summary>
    public const int ExplorationPercent = 73;

    private readonly VisualElement _root;
    private readonly GameSession _session;

    private Label _energy;
    private Label _food;
    private Label _minerals;
    private Label _fuel;
    private Label _alloy;
    private Label _destination;
    private Label _objectiveSurvey;
    private Label _exploration;

    private VisualElement _energyFill;
    private VisualElement _foodFill;
    private VisualElement _explorationFill;
    private VisualElement _planetDisc;
    private VisualElement _planetStage;
    private Button _expeditionCta;
    private bool _planetReady;
    private RenderTexture _planetPreviewTexture;
    private bool _planetPreviewApplied;

    // ---- Controller (Phase 09B PASS 7) ----
    private ControllerPromptBar _promptBar;
    private bool _promptRowsBuilt;
    private bool _builtBackRow;
    private bool _planetInteractive;
    private bool _dragging;
    private int _dragPointerId = -1;
    private Vector2 _lastDragPosition;

    public OverviewScreen(VisualElement root, GameSession session)
    {
        _root = root;
        _session = session;
    }

    /// <summary>Raised when the player presses Back on this page. Only a host
    /// that really has a step back turns this on (<see cref="OwnsBack"/>).</summary>
    public event Action BackRequested;

    /// <summary>True while this page consumes B / Escape itself.</summary>
    public bool OwnsBack { get; set; }

    /// <summary>
    /// True when the Back key really leaves this page's context - either the
    /// page owns it, or the host's own focus layer does (the ship's console view
    /// leaves through B while the page never steals the press). The [B] 返回 row
    /// is printed only when this is true, so the key line can never advertise a
    /// key that does nothing.
    /// </summary>
    public bool ShowBackRow { get; set; }

    /// <summary>The rotating 3D preview RenderTexture when this overview is
    /// driven by the shared preview rig (null = static procedural disc).</summary>
    public RenderTexture PlanetPreviewTexture => _planetPreviewTexture;

    /// <summary>True while the planet area displays the 3D preview texture.</summary>
    public bool IsPlanetPreviewActive => _planetPreviewApplied;

    /// <summary>True while the fallback static disc is used instead.</summary>
    public bool IsPlanetFallbackActive => _planetReady;

    /// <summary>True while the right stick / mouse really drives the planet.</summary>
    public bool IsPlanetInteractive => _planetInteractive;

    /// <summary>The shared controller prompt bar (probes / tests).</summary>
    public ControllerPromptBar PromptBar => _promptBar;

    /// <summary>
    /// Binds the view.
    ///
    /// <paramref name="showExpeditionCta"/> is the HOST CONTEXT switch
    /// (Phase 07B.2.3): the legacy Hub keeps the 准备远征 CTA because it is the
    /// only way into Preparation there, while the ship's physical Overview
    /// terminal hides it - inside the ship the expedition is launched from the
    /// Airlock equipment locker + deployment station, so the CTA would be both
    /// dead and semantically duplicated.
    ///
    /// <paramref name="planetPreview"/> is the shared rotating 3D planet
    /// RenderTexture (Phase 07B.2.4). Every host reads the SAME texture; when it
    /// is absent the view falls back to the original static procedural disc, so
    /// the overview can never render without a planet.
    /// </summary>
    public void Bind(System.Action navigateToPreparation,
        bool showExpeditionCta = true,
        RenderTexture planetPreview = null)
    {
        _planetPreviewTexture = planetPreview;
        _energy = BindLabel(ElementNames.EnergyValue);
        _food = BindLabel(ElementNames.FoodValue);
        _minerals = BindLabel(ElementNames.MineralsValue);
        _fuel = BindLabel(ElementNames.FuelValue);
        _alloy = BindLabel(ElementNames.AlloyValue);
        _destination = BindLabel(ElementNames.DestinationValue);
        _objectiveSurvey = BindLabel(ElementNames.ObjectiveSurveyText);
        _exploration = BindLabel(ElementNames.ExplorationValue);

        _energyFill = BindElement(ElementNames.EnergyBarFill);
        _foodFill = BindElement(ElementNames.FoodBarFill);
        _explorationFill = BindElement(ElementNames.ExplorationBarFill);
        _planetDisc = BindElement(ElementNames.PlanetDisc);
        if (_planetDisc != null)
        {
            // The planet is a pure visual preview: it must never intercept a
            // pointer that belongs to an overview button.
            _planetDisc.pickingMode = PickingMode.Ignore;
        }

        _planetStage = BindElement(ElementNames.PlanetStage);
        BindPlanetDrag(_planetStage);

        _promptBar = ControllerPromptBar.Attach(
            _root.Q<VisualElement>(ElementNames.OverviewPromptHost),
            UiFontProvider.CreateCjkFont());
        _promptBar?.SetVisible(false);

        _expeditionCta = _root.Q<Button>(ElementNames.OpenPreparationButton);
        if (_expeditionCta == null)
        {
            LogMissing(ElementNames.OpenPreparationButton);
        }
        else
        {
            _expeditionCta.style.display = showExpeditionCta
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _expeditionCta.SetEnabled(showExpeditionCta);
            if (showExpeditionCta && navigateToPreparation != null)
            {
                _expeditionCta.clicked += navigateToPreparation;
            }
        }

        Refresh();
    }

    /// <summary>The 准备远征 CTA (hidden on the ship's physical terminal).</summary>
    public Button ExpeditionCta => _expeditionCta;

    /// <summary>True when the expedition CTA is actually drawn.</summary>
    public bool IsExpeditionCtaVisible => _expeditionCta != null
                                          && _expeditionCta.resolvedStyle.display
                                          != DisplayStyle.None;

    public void Refresh()
    {
        SetText(_energy, _session.Energy);
        SetText(_food, _session.Food);
        SetText(_minerals, _session.Minerals);
        SetText(_fuel, _session.Fuel);
        SetText(_alloy, _session.Alloy);

        SetBarWidth(_energyFill, _session.Energy);
        SetBarWidth(_foodFill, _session.Food);

        // One planet name for the whole page (Phase 09B PASS 7): the hero title
        // and the mission objective both read the shared display name.
        SetLabelText(_destination, PlanetIdentity.DisplayName);
        SetLabelText(_objectiveSurvey, PlanetIdentity.SurveyObjective);

        SetLabelText(_exploration, ExplorationPercent + "%");
        SetBarWidth(_explorationFill, ExplorationPercent);

        EnsurePlanetTexture();
    }

    /// <summary>
    /// Per-frame page tick. The host calls it ONLY while this page is the one on
    /// screen, and <paramref name="controllerEnabled"/> is false whenever the
    /// page is visible but not interactive (an unfocused ship terminal), so a
    /// page nobody is looking at can never rotate the planet or answer a press.
    /// </summary>
    public void Tick(bool visible, bool controllerEnabled)
    {
        bool interactive = visible && controllerEnabled
                           && _planetPreviewTexture != null;

        if (!interactive)
        {
            if (_planetInteractive)
            {
                // Leaving the page hands the planet back to its authored idle
                // spin, keeping the orientation the player left it in.
                OverviewPlanetPreview.Shared?.ResetPlayerControl();
                _planetInteractive = false;
            }

            ReleaseDrag();
            _promptBar?.SetVisible(false);
            return;
        }

        _planetInteractive = true;
        InputDeviceTracker.Poll();
        RefreshPrompt();

        if (_promptBar != null)
        {
            _promptBar.SetVisible(true);
            _promptBar.Tick();
        }

        // RS: already dead-zoned and radial (UiInput.MapCursorVector), forwarded
        // to the ONE shared rig. A centred stick is a real zero, so the planet
        // coasts to a stop instead of drifting.
        OverviewPlanetPreview rig = OverviewPlanetPreview.Shared;
        rig?.ApplyPlayerRotation(UiInput.MapCursorVector(), Time.deltaTime);

        if (OwnsBack && UiInput.BackPressed())
        {
            UiInput.ConsumeBackForFrame();
            BackRequested?.Invoke();
        }
    }

    /// <summary>True when the Back row belongs in this page's key line.</summary>
    private bool BackRowVisible => ShowBackRow || OwnsBack;

    /// <summary>
    /// Publishes the row set. The rows only change when the host switches the
    /// Back row on/off, so the bar is not rebuilt every frame.
    /// </summary>
    private void RefreshPrompt()
    {
        if (_promptBar == null)
        {
            return;
        }

        if (_promptRowsBuilt && _builtBackRow == BackRowVisible)
        {
            return;
        }

        _promptRowsBuilt = true;
        _builtBackRow = BackRowVisible;
        _promptBar.SetRows(PromptRows());
    }

    /// <summary>The row set this page publishes: only keys that work here.</summary>
    private ControllerPromptRow[] PromptRows()
    {
        if (BackRowVisible)
        {
            return new[]
            {
                ControllerPromptRow.Key(RotatePromptLabel, UiAction.MapCursor),
                ControllerPromptRow.Key("返回", UiAction.Back)
            };
        }

        return new[]
        {
            ControllerPromptRow.Key(RotatePromptLabel, UiAction.MapCursor)
        };
    }

    // ---- mouse drag (the keyboard/mouse presentation of the same rotation) ----

    private void BindPlanetDrag(VisualElement stage)
    {
        if (stage == null)
        {
            return;
        }

        stage.RegisterCallback<PointerDownEvent>(OnPlanetPointerDown);
        stage.RegisterCallback<PointerMoveEvent>(OnPlanetPointerMove);
        stage.RegisterCallback<PointerUpEvent>(OnPlanetPointerUp);
    }

    private void OnPlanetPointerDown(PointerDownEvent evt)
    {
        // The [鼠标] presentation must really work, so the stage accepts a drag
        // while the page owns the player's attention and the live rig is up.
        if (!_planetInteractive || _planetPreviewTexture == null)
        {
            return;
        }

        _dragging = true;
        _dragPointerId = evt.pointerId;
        _lastDragPosition = evt.position;
        _planetStage?.CapturePointer(evt.pointerId);
    }

    private void OnPlanetPointerMove(PointerMoveEvent evt)
    {
        if (!_dragging || evt.pointerId != _dragPointerId)
        {
            return;
        }

        // The delta is measured here instead of read from the event: the ship's
        // console router synthesises its own pointer, and one owner of the maths
        // keeps the Hub and the console identical.
        var position = new Vector2(evt.position.x, evt.position.y);
        Vector2 delta = position - _lastDragPosition;
        _lastDragPosition = position;

        if (delta.sqrMagnitude > 0f)
        {
            OverviewPlanetPreview.Shared?.RotateByDrag(delta);
        }
    }

    private void OnPlanetPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId == _dragPointerId)
        {
            ReleaseDrag();
        }
    }

    private void ReleaseDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (_dragPointerId >= 0 && _planetStage != null
            && _planetStage.HasPointerCapture(_dragPointerId))
        {
            _planetStage.ReleasePointer(_dragPointerId);
        }

        _dragPointerId = -1;
    }

    /// <summary>
    /// Assigns the planet visual ONCE.
    ///
    /// With the shared preview rig available this is the live rotating 3D
    /// RenderTexture (Phase 07B.2.4); the procedural disc is only generated as a
    /// fallback, so hosts that show the preview never pay for the 2D texture.
    /// </summary>
    private void EnsurePlanetTexture()
    {
        if (_planetDisc == null)
        {
            return;
        }

        if (_planetPreviewTexture != null)
        {
            if (!_planetPreviewApplied)
            {
                _planetDisc.style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(_planetPreviewTexture));
                _planetPreviewApplied = true;
            }

            return;
        }

        if (_planetReady)
        {
            return;
        }

        Texture2D texture = PlanetTextureFactory.Create();
        if (texture == null)
        {
            LogMissing("planet texture generation failed");
            return;
        }

        _planetDisc.style.backgroundImage = new StyleBackground(texture);
        // Planet disc and texture are both square, so every background scale
        // mode (stretch/contain/cover default) resolves to the exact fill.
        _planetReady = true;
    }

    private static void SetBarWidth(VisualElement fill, int value)
    {
        if (fill == null)
        {
            return;
        }

        // Survival bars display the default resource semantics as 0-100 %.
        fill.style.width = Length.Percent(Mathf.Clamp(value, 0, 100));
    }

    private Label BindLabel(string name)
    {
        Label label = _root.Q<Label>(name);
        if (label == null)
        {
            LogMissing(name);
        }

        return label;
    }

    private VisualElement BindElement(string name)
    {
        VisualElement element = _root.Q<VisualElement>(name);
        if (element == null)
        {
            LogMissing(name);
        }

        return element;
    }

    private static void SetText(Label label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
        }
    }

    private static void SetLabelText(Label label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }

    private static void LogMissing(string name)
    {
        Debug.LogError("OverviewScreen: missing element " + name);
    }
}
