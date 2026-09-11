using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Title screen controller (Phase 08, Revision 2).
///
/// The cover is now a MINIMAL console title screen: the headline 活下去 and the
/// four options 新游戏 / 继续游戏 / 选项 / 退出, floating over the deep-space
/// shot of the hero ship. Everything else the previous cover carried is gone
/// (system bars, telemetry, mission/sector codes, HUD brackets, brand wordmark,
/// 制作信息).
///
/// Responsibilities:
/// - bind the four real actions (new game / continue / options / quit),
/// - own the focus state (Focus is the primary state; hover follows it),
/// - provide the basic gamepad navigation this screen must already support
///   (D-Pad or left stick up/down, A confirm, B back) without pretending to be
///   the full Phase 09 console UX,
/// - show a light, device-aware input hint,
/// - own the 选项 modal,
/// - expose the layer alphas the start cinematic drives.
///
/// It is deliberately NOT a session or navigation authority: 新游戏 resets the
/// ONE authoritative GameSession through its existing reset contract, then hands
/// the shot to <see cref="MainMenuCinematic"/>; routing stays in SceneRouting.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    [SerializeField]
    private StyleSheet themeStyleSheet;

    [SerializeField]
    private StyleSheet mainMenuStyleSheet;

    /// <summary>Seconds before a held stick/D-Pad starts repeating.</summary>
    private const float NavigationRepeatDelay = 0.32f;

    /// <summary>Seconds between repeats while the direction is held.</summary>
    private const float NavigationRepeatRate = 0.14f;

    /// <summary>Stick magnitude that counts as a direction press.</summary>
    private const float NavigateStickThreshold = 0.55f;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _menuLayer;
    private MainMenuCinematic _cinematic;

    private VisualElement _optionsModal;
    private VisualElement _optionsDim;
    private VisualElement _optionsPanel;
    private VisualElement _transitionOverlay;
    private Label _inputHint;

    private Label _title;
    private Button _newGame;
    private Button _continue;
    private Button _options;
    private Button _exit;
    private Button _optionsClose;
    private Button _motionOn;
    private Button _motionOff;
    private Button _hintsShow;
    private Button _hintsHide;

    private readonly System.Collections.Generic.List<Button> _items =
        new System.Collections.Generic.List<Button>();

    private int _focusIndex;
    private bool _optionsOpen;
    private float _optionsAlpha;
    private bool _optionsInteractive;

    private bool _transitioning;
    private bool _sceneLoadRequested;
    private float _transitionAlpha;

    private float _navigateHeld;
    private int _lastNavigateDirection;
    private float _nextNavigateTime;
    private int _lastGamepadConfirmFrame = -100;

    /// <summary>True once Start() finished binding and the menu is usable.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Root of the bound UI tree (valid after IsReady).</summary>
    public VisualElement UiRoot => _root;

    /// <summary>The menu's UIDocument.</summary>
    public UIDocument Document => _document;

    /// <summary>The fadeable menu subtree (everything except the modal).</summary>
    public VisualElement MenuLayer => _menuLayer;

    /// <summary>The only headline on the screen.</summary>
    public Label Title => _title;

    /// <summary>The four options, in navigation order.</summary>
    public System.Collections.Generic.IReadOnlyList<Button> MenuItems => _items;

    public Button NewGameButton => _newGame;
    public Button ContinueButton => _continue;
    public Button OptionsButton => _options;
    public Button ExitButton => _exit;

    /// <summary>Index of the focused option inside <see cref="MenuItems"/>.</summary>
    public int FocusIndex => _focusIndex;

    /// <summary>The focused option (never a disabled one).</summary>
    public Button FocusedButton =>
        _focusIndex >= 0 && _focusIndex < _items.Count ? _items[_focusIndex] : null;

    /// <summary>The start cinematic that owns 新游戏 / 继续游戏.</summary>
    public MainMenuCinematic Cinematic
    {
        get
        {
            if (_cinematic == null)
            {
                _cinematic = Object.FindAnyObjectByType<MainMenuCinematic>();
            }

            return _cinematic;
        }
    }

    /// <summary>
    /// The title screen's AUTHORITATIVE stage camera.
    ///
    /// Never resolve "any camera" here: persistent helper rigs (the shared
    /// Overview planet preview) survive scene loads with their own camera, so a
    /// generic lookup can hand back a camera that has nothing to do with this
    /// composition.
    /// </summary>
    public Camera StageCamera
    {
        get
        {
            MainMenuCinematic cinematic = Cinematic;
            return cinematic != null ? cinematic.StageCamera : null;
        }
    }

    /// <summary>Logical options-modal state.</summary>
    public bool IsOptionsOpen => _optionsOpen;

    /// <summary>Number of modals currently shown (never above one).</summary>
    public int OpenModalCount
    {
        get
        {
            return _optionsModal != null
                   && _optionsModal.resolvedStyle.display == DisplayStyle.Flex
                ? 1
                : 0;
        }
    }

    /// <summary>True while a modal is open (the menu behind it is blocked).</summary>
    public bool IsAnyModalOpen => _optionsOpen;

    /// <summary>True when 继续游戏 can resume a run started in this process.</summary>
    public bool IsContinueAvailable => GameSession.Instance != null
                                       && GameSession.Instance.HasStartedRun;

    /// <summary>True while the current input device is a gamepad.</summary>
    public bool IsGamepadHintVisible => _inputHint != null
                                        && !string.IsNullOrEmpty(_inputHint.text);

    /// <summary>The controller hint currently shown (empty when hidden).</summary>
    public string InputHintText => _inputHint != null ? _inputHint.text : string.Empty;

    /// <summary>True while the start cinematic runs (or the fallback fade does).</summary>
    public bool IsTransitioning
    {
        get
        {
            MainMenuCinematic cinematic = Cinematic;
            return _transitioning || (cinematic != null && cinematic.IsPlaying);
        }
    }

    /// <summary>Assigns the style sheets; called once by scene authoring tooling.</summary>
    public void Configure(StyleSheet theme, StyleSheet menu)
    {
        themeStyleSheet = theme;
        mainMenuStyleSheet = menu;
    }

    private void Awake()
    {
        _document = GetComponent<UIDocument>();

        // The title screen is a 2D UI screen: keep the OS cursor usable
        // (returning from the wrist/surface modes leaves it locked and hidden).
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private void Start()
    {
        _root = _document != null ? _document.rootVisualElement : null;
        if (_root == null)
        {
            Debug.LogError("MainMenuController: UIDocument root is null.");
            return;
        }

        if (themeStyleSheet != null)
        {
            _root.styleSheets.Add(themeStyleSheet);
        }

        if (mainMenuStyleSheet != null)
        {
            _root.styleSheets.Add(mainMenuStyleSheet);
        }

        UiFontProvider.ApplyTo(_root);

        // A player build runs Resources.UnloadUnusedAssets on every scene load:
        // the panel settings of the scene being replaced must not be released
        // while UI Toolkit still holds its panel state.
        if (!Application.isEditor && _document != null && _document.panelSettings != null)
        {
            _document.panelSettings.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }

        BindElements();
        BindActions();
        BindOptions();
        ApplySettings();
        RefreshContinueAvailability();
        SetFocus(0);
        IsReady = true;

        Debug.Log("[MainMenuController] title screen ready: items=" + _items.Count
                  + " continue=" + IsContinueAvailable
                  + " cinematic=" + (Cinematic != null));
    }

    private void Update()
    {
        // Device tracking first: everything below reacts to the same sample.
        InputDeviceTracker.Poll();
        TrackGamepadPresses();

        UpdateNavigation();
        HandleEscapeAndBack();
        PollConfirm();
        UpdateInputHint();
        UpdateOptionsAnimation();
        UpdateTransition();
    }

    /// <summary>
    /// Remembers the frame a gamepad confirm was pressed. Unity's runtime UI
    /// turns that same press into a synthetic pointer click at the virtual
    /// pointer position (the left stick drives it), and that click must never
    /// activate a menu item - otherwise one A press performs TWO actions, which
    /// is how "A on 新游戏" ended up quitting the game.
    /// </summary>
    private void TrackGamepadPresses()
    {
        if (UiInput.GamepadConfirmPressedThisFrame())
        {
            _lastGamepadConfirmFrame = Time.frameCount;
        }
    }

    /// <summary>
    /// True when the pointer event currently being processed was synthesised by
    /// the gamepad (same frame as the confirm press, or the frame after).
    /// </summary>
    private bool IsGamepadSynthesisedUiInput()
    {
        return Time.frameCount - _lastGamepadConfirmFrame <= 1;
    }

    /// <summary>
    /// True when a pointer event came from a real mouse (movement or a pressed
    /// button) rather than from the gamepad's virtual pointer.
    /// </summary>
    private static bool IsRealMousePointer()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            // Automation dispatches pointer events with no mouse device at all.
            return Gamepad.current == null;
        }

        return mouse.delta.ReadValue().sqrMagnitude > 0.01f || mouse.leftButton.isPressed;
    }

    /// <summary>ESC closes 选项 and never exits the application.</summary>
    public void HandleEscapeKey()
    {
        HandleBackInput();
    }

    /// <summary>
    /// The one "go back" action: ESC, gamepad B and the modal's 返回 button all
    /// end up here, so the behaviour cannot drift between devices.
    /// </summary>
    public void HandleBackInput()
    {
        CloseOptions();
    }

    private void HandleEscapeAndBack()
    {
        // One logical Back action (Xbox B / Escape) for the whole screen; the
        // physical mapping lives in UiInput.
        if (UiInput.BackPressed())
        {
            // The title screen owns Back while it is showing: consume it so no
            // other layer (and not the Escape-as-Pause path) sees the same press.
            UiInput.ConsumeBackForFrame();

            if (InputDeviceTracker.GamepadProducedInput())
            {
                InputDeviceTracker.NotifyGamepad();
            }

            HandleBackInput();
        }
    }

    // ---- Binding ----

    private void BindElements()
    {
        _menuLayer = BindElement(MainMenuNames.Layer, false);
        _title = BindLabel(MainMenuNames.Title);

        _newGame = BindButton(MainMenuNames.NewGameButton);
        _continue = BindButton(MainMenuNames.ContinueButton);
        _options = BindButton(MainMenuNames.OptionsButton);
        _exit = BindButton(MainMenuNames.ExitButton);

        _items.Clear();
        TrackItem(_newGame);
        TrackItem(_continue);
        TrackItem(_options);
        TrackItem(_exit);

        _inputHint = BindLabel(MainMenuNames.InputHintLabel, false);
        _transitionOverlay = BindElement(MainMenuNames.TransitionOverlay, false);

        if (_transitionOverlay != null)
        {
            _transitionOverlay.style.display = DisplayStyle.None;
            _transitionOverlay.style.opacity = 0f;
        }
    }

    private void TrackItem(Button item)
    {
        if (item == null)
        {
            return;
        }

        int index = _items.Count;
        _items.Add(item);

        // The gamepad must never be able to activate a row through Unity's
        // built-in focus/submit path: this controller dispatches from its ONE
        // authoritative focus state. Mouse activation still works through the
        // guarded click below.
        item.focusable = false;

        item.RegisterCallback<PointerEnterEvent>(_ =>
        {
            // Hover feeds the focus, but only for a real mouse: the gamepad's
            // virtual pointer would otherwise fight the stick navigation.
            if (IsRealMousePointer() && !IsGamepadSynthesisedUiInput())
            {
                SetFocus(index);
            }
        });
    }

    private void BindActions()
    {
        // Every row is routed through the guarded binder: a click that Unity's
        // runtime UI synthesised from the gamepad is dropped, so the only
        // gamepad path to an action is PollConfirm -> ConfirmFocused.
        BindGuarded(_newGame, "NewGame", () =>
        {
            SetFocus(0);
            RequestNewGame();
        });
        BindGuarded(_continue, "Continue", () =>
        {
            SetFocus(1);
            RequestContinue();
        });
        BindGuarded(_options, "Options", () =>
        {
            SetFocus(2);
            ShowOptions();
        });
        BindGuarded(_exit, "Exit", () =>
        {
            SetFocus(3);
            OnExitClicked();
        });
    }

    /// <summary>
    /// Subscribes a button while refusing the pointer click that Unity's runtime
    /// UI synthesises from a gamepad button press (see
    /// <see cref="IsGamepadSynthesisedUiInput"/>).
    /// </summary>
    private void BindGuarded(Button button, string id, System.Action action)
    {
        if (button == null)
        {
            return;
        }

        button.clicked += () =>
        {
            if (IsGamepadSynthesisedUiInput())
            {
                Debug.Log("[MENU] ignored a gamepad-synthesised UI click on " + id
                          + " (focus=" + FocusedItemId + ")");
                return;
            }

            Debug.Log("[MENU] Click = " + id + " (focus=" + FocusedItemId + ")");
            action();
        };
    }

    private void OnExitClicked()
    {
        Debug.Log("[MENU] Execute = QuitGame (退出 clicked)");
        RequestQuit();
    }

    private void BindOptions()
    {
        _optionsModal = BindElement(MainMenuNames.OptionsModal, false);
        _optionsDim = BindElement(MainMenuNames.OptionsDim, false);
        _optionsPanel = BindElement(MainMenuNames.OptionsPanel, false);
        _optionsClose = BindButton(MainMenuNames.OptionsCloseButton, false);

        BindGuarded(_optionsClose, "OptionsClose", CloseOptions);

        _motionOn = BindButton(MainMenuNames.MotionOnButton, false);
        _motionOff = BindButton(MainMenuNames.MotionOffButton, false);
        _hintsShow = BindButton(MainMenuNames.HintsShowButton, false);
        _hintsHide = BindButton(MainMenuNames.HintsHideButton, false);

        BindGuarded(_motionOn, "MotionOn", () => SetMotion(true));
        BindGuarded(_motionOff, "MotionOff", () => SetMotion(false));
        BindGuarded(_hintsShow, "HintsShow", () => SetHints(true));
        BindGuarded(_hintsHide, "HintsHide", () => SetHints(false));

        SetModalVisible(_optionsModal, false);
        ApplyPickingMode(_optionsModal, false);
        _optionsOpen = false;
        _optionsAlpha = 0f;
        _optionsInteractive = false;
    }

    // ---- Focus / navigation ----

    /// <summary>Moves the focus to an index, skipping disabled options.</summary>
    public void SetFocus(int index)
    {
        if (_items.Count == 0)
        {
            return;
        }

        index = Mathf.Clamp(index, 0, _items.Count - 1);
        if (IsItemEnabled(index))
        {
            _focusIndex = index;
        }
        else
        {
            // Never leave the focus on an unavailable option: walk forward,
            // then backward, and stay put when nothing is selectable.
            int found = -1;
            for (int step = 1; step <= _items.Count; step++)
            {
                int candidate = (index + step) % _items.Count;
                if (IsItemEnabled(candidate))
                {
                    found = candidate;
                    break;
                }
            }

            _focusIndex = found >= 0 ? found : index;
        }

        ApplyFocusClasses();
    }

    private bool IsItemEnabled(int index)
    {
        return _items[index] != null && _items[index].enabledSelf;
    }

    private void ApplyFocusClasses()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            Button item = _items[i];
            if (item == null)
            {
                continue;
            }

            if (i == _focusIndex)
            {
                item.AddToClassList("menu-item-focused");
            }
            else
            {
                item.RemoveFromClassList("menu-item-focused");
            }
        }
    }

    /// <summary>Moves the focus to the next enabled option (wraps).</summary>
    public void FocusNext()
    {
        Step(1);
    }

    /// <summary>Moves the focus to the previous enabled option (wraps).</summary>
    public void FocusPrevious()
    {
        Step(-1);
    }

    private void Step(int direction)
    {
        if (_items.Count == 0)
        {
            return;
        }

        for (int step = 1; step <= _items.Count; step++)
        {
            int candidate = ((_focusIndex + direction * step) % _items.Count
                             + _items.Count) % _items.Count;
            if (IsItemEnabled(candidate))
            {
                SetFocus(candidate);
                return;
            }
        }
    }

    /// <summary>Activates the focused option (A / Confirm).</summary>
    public bool ConfirmFocused()
    {
        Button focused = FocusedButton;
        if (focused == null || !focused.enabledSelf || IsAnyModalOpen || IsTransitioning)
        {
            return false;
        }

        Activate(focused);
        return true;
    }

    private void Activate(Button item)
    {
        if (item == _newGame)
        {
            RequestNewGame();
        }
        else if (item == _continue)
        {
            RequestContinue();
        }
        else if (item == _options)
        {
            ShowOptions();
        }
        else if (item == _exit)
        {
            RequestQuit();
        }
    }

    /// <summary>
    /// D-Pad / left stick / arrow keys move the focus; confirm activates. Repeat
    /// uses a short delay then a steady rate so a held stick cannot race the menu.
    /// The physical mapping lives in <see cref="UiInput"/>.
    /// </summary>
    private void UpdateNavigation()
    {
        if (IsAnyModalOpen || IsTransitioning)
        {
            return;
        }

        int direction = UiInput.NavigationDirection();
        if (direction == 0)
        {
            _navigateHeld = 0f;
            _lastNavigateDirection = 0;
            return;
        }

        float now = Time.unscaledTime;
        if (direction != _lastNavigateDirection)
        {
            _lastNavigateDirection = direction;
            _navigateHeld = 0f;
            _nextNavigateTime = now + NavigationRepeatDelay;
            Step(direction);
            return;
        }

        if (now >= _nextNavigateTime)
        {
            _nextNavigateTime = now + NavigationRepeatRate;
            Step(direction);
        }
    }

    /// <summary>Gamepad / keyboard confirmation, polled here so the title screen
    /// works without any authored EventSystem.</summary>
    private void PollConfirm()
    {
        if (!UiInput.ConfirmPressed())
        {
            return;
        }

        Debug.Log("[MENU] Confirm pressed - focus=" + FocusedItemId);
        if (InputDeviceTracker.GamepadProducedInput())
        {
            InputDeviceTracker.NotifyGamepad();
        }

        if (_optionsOpen)
        {
            return;
        }

        ConfirmFocused();
    }

    /// <summary>Stable id of the focused option (diagnostics / automation).</summary>
    public string FocusedItemId
    {
        get
        {
            Button focused = FocusedButton;
            if (focused == null)
            {
                return "<none>";
            }

            if (focused == _newGame)
            {
                return "NewGame";
            }

            if (focused == _continue)
            {
                return "Continue";
            }

            if (focused == _options)
            {
                return "Options";
            }

            return focused == _exit ? "Exit" : focused.name;
        }
    }

    private void UpdateInputHint()
    {
        if (_inputHint == null)
        {
            return;
        }

        if (!InputDeviceTracker.GamepadActive)
        {
            // Mouse/keyboard players get no controller chrome at all.
            if (_inputHint.text.Length > 0)
            {
                _inputHint.text = string.Empty;
                _inputHint.style.display = DisplayStyle.None;
            }

            return;
        }

        string hint = _optionsOpen ? "(A) 确认    (B) 返回" : "(A) 确认";
        if (_inputHint.text != hint)
        {
            _inputHint.text = hint;
        }

        _inputHint.style.display = DisplayStyle.Flex;
    }

    // ---- Options ----

    /// <summary>Opens 选项.</summary>
    public void ShowOptions()
    {
        if (_optionsOpen || IsTransitioning)
        {
            return;
        }

        Debug.Log("[MENU] Execute = OpenOptions");
        _optionsOpen = true;
        SetModalVisible(_optionsModal, true);
    }

    /// <summary>Closes 选项 (ESC / B / 返回).</summary>
    public void CloseOptions()
    {
        if (!_optionsOpen && _optionsAlpha <= 0.001f)
        {
            return;
        }

        Debug.Log("[MENU] Execute = CloseOptions");
        _optionsOpen = false;
    }

    // ---- The four actions ----

    /// <summary>
    /// 新游戏: resets the ONE authoritative session through its existing reset
    /// contract, marks the run as started (which is what enables 继续游戏 for the
    /// rest of this process) and plays the start cinematic. It never creates a
    /// second session object.
    /// </summary>
    public void RequestNewGame()
    {
        if (IsTransitioning || IsAnyModalOpen)
        {
            return;
        }

        Debug.Log("[MENU] Execute = StartNewGame");
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        session.BeginRun();

        _transitioning = true;
        _transitionAlpha = 0f;
        RefreshContinueAvailability();

        MainMenuCinematic cinematic = Cinematic;
        if (cinematic != null)
        {
            cinematic.BeginNewGame();
            return;
        }

        Debug.LogWarning("MainMenuController: no MainMenuCinematic in the scene - "
                         + "falling back to a plain fade + route");
        SetCinematicBlackoutVisible(true);
    }

    /// <summary>
    /// 继续游戏: resumes the run already started in this process WITHOUT touching
    /// the session, through a short blackout and a shorter eyelid open.
    /// </summary>
    public void RequestContinue()
    {
        if (IsTransitioning || IsAnyModalOpen || !IsContinueAvailable)
        {
            return;
        }

        Debug.Log("[MENU] Execute = ContinueGame");
        _transitioning = true;
        _transitionAlpha = 0f;

        MainMenuCinematic cinematic = Cinematic;
        if (cinematic != null)
        {
            cinematic.BeginContinue();
            return;
        }

        SetCinematicBlackoutVisible(true);
    }

    /// <summary>Reflects the resumable state into 继续游戏 (disabled when the
    /// process has no started run yet).</summary>
    public void RefreshContinueAvailability()
    {
        bool available = IsContinueAvailable;
        if (_continue != null)
        {
            _continue.SetEnabled(available);
            if (available)
            {
                _continue.RemoveFromClassList("menu-item-disabled");
            }
            else
            {
                _continue.AddToClassList("menu-item-disabled");
            }
        }

        if (!available && _focusIndex == _items.IndexOf(_continue))
        {
            SetFocus(0);
        }
    }

    /// <summary>
    /// The 退出 action: reachable only from the Exit option (confirm while it is
    /// focused, or a real click on that row). The termination itself lives in the
    /// single authoritative <see cref="AppQuit"/> path, shared with the pause
    /// menu's 退出游戏.
    /// </summary>
    public static void RequestQuit()
    {
        AppQuit.Request("MainMenu.Exit");
    }

    /// <summary>How many times the quit action was requested (automation only).</summary>
    public static int QuitRequestCount => AppQuit.RequestCount;

    /// <summary>
    /// Automation seam: while true the quit action only records itself. Tests
    /// must reset it in teardown.
    /// </summary>
    public static bool SuppressQuitForAutomation
    {
        get => AppQuit.SuppressForAutomation;
        set => AppQuit.SuppressForAutomation = value;
    }

    /// <summary>Clears the quit bookkeeping (automation only).</summary>
    public static void ResetQuitBookkeepingForAutomation()
    {
        AppQuit.ResetBookkeepingForAutomation();
    }

    // ---- Cinematic hooks (driven by MainMenuCinematic) ----

    /// <summary>Fades the whole menu subtree (title, options, hint).</summary>
    public void SetCinematicMenuAlpha(float alpha)
    {
        if (_menuLayer != null)
        {
            _menuLayer.style.opacity = Mathf.Clamp01(alpha);
        }
    }

    /// <summary>Shows/hides the black start-cinematic overlay.</summary>
    public void SetCinematicBlackoutVisible(bool visible)
    {
        if (_transitionOverlay == null)
        {
            return;
        }

        _transitionOverlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        _transitionOverlay.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
    }

    /// <summary>Sets the black overlay opacity (0 = clear, 1 = full blackout).</summary>
    public void SetCinematicBlackoutAlpha(float alpha)
    {
        if (_transitionOverlay != null)
        {
            _transitionOverlay.style.opacity = Mathf.Clamp01(alpha);
        }
    }

    /// <summary>Current black overlay opacity (verification).</summary>
    public float BlackoutAlpha => _transitionOverlay != null
        ? _transitionOverlay.resolvedStyle.opacity
        : 0f;

    // ---- Per-frame presentation ----

    private void UpdateOptionsAnimation()
    {
        // Steady state: no style writes at all (a per-frame layout write keeps
        // the panel invalidated forever and makes its teardown spin).
        float target = _optionsOpen ? 1f : 0f;
        if (_optionsAlpha == target)
        {
            return;
        }

        _optionsAlpha = Mathf.MoveTowards(_optionsAlpha, target,
            MotionEnabledStep(MainMenuSettings.ModalSeconds));

        if (_optionsDim != null)
        {
            _optionsDim.style.opacity = _optionsAlpha;
        }

        if (_optionsPanel != null)
        {
            _optionsPanel.style.opacity = _optionsAlpha;
            _optionsPanel.style.translate = new Translate(0f,
                Mathf.Lerp(16f, 0f, _optionsAlpha));
        }

        bool visible = _optionsOpen || _optionsAlpha > 0.001f;
        DisplayStyle wanted = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (_optionsModal != null && _optionsModal.resolvedStyle.display != wanted)
        {
            _optionsModal.style.display = wanted;
        }

        if (_optionsInteractive != _optionsOpen)
        {
            _optionsInteractive = _optionsOpen;
            ApplyPickingMode(_optionsModal, _optionsOpen);
        }
    }

    /// <summary>
    /// Fallback transition used only when the scene has no cinematic: fade to
    /// black, then take the product route (title screen -> ship interior).
    /// </summary>
    private void UpdateTransition()
    {
        if (!_transitioning || _sceneLoadRequested || Cinematic != null)
        {
            return;
        }

        _transitionAlpha = Mathf.MoveTowards(_transitionAlpha, 1f,
            MotionEnabledStep(MainMenuSettings.TransitionSeconds));
        SetCinematicBlackoutAlpha(_transitionAlpha);
        SetCinematicMenuAlpha(1f - _transitionAlpha);

        if (_transitionAlpha < 1f)
        {
            return;
        }

        _sceneLoadRequested = true;
        SceneRouting.EnterShipInterior(SceneRouting.ShipEntry.FromMainMenu,
            SceneRouting.EntryEyelid.Full);
    }

    /// <summary>Per-frame alpha step; motion disabled resolves instantly.</summary>
    private static float MotionEnabledStep(float seconds)
    {
        if (!MainMenuSettings.MotionEnabled || seconds <= 0f)
        {
            return 1f;
        }

        return Time.unscaledDeltaTime / seconds;
    }

    // ---- Settings application ----

    /// <summary>动态效果: applies the preference and refreshes the switch UI.</summary>
    public void SetMotion(bool enabled)
    {
        MainMenuSettings.SetMotion(enabled);
        ApplySettings();
    }

    /// <summary>界面提示: applies the preference and refreshes the switch UI.</summary>
    public void SetHints(bool visible)
    {
        MainMenuSettings.SetHints(visible);
        ApplySettings();
    }

    /// <summary>Reflects <see cref="MainMenuSettings"/> into the tree.</summary>
    public void ApplySettings()
    {
        SetToggleState(_motionOn, MainMenuSettings.MotionEnabled);
        SetToggleState(_motionOff, !MainMenuSettings.MotionEnabled);
        SetToggleState(_hintsShow, MainMenuSettings.HintsVisible);
        SetToggleState(_hintsHide, !MainMenuSettings.HintsVisible);
    }

    private static void SetToggleState(Button button, bool active)
    {
        if (button == null)
        {
            return;
        }

        if (active)
        {
            button.AddToClassList("segment-active");
        }
        else
        {
            button.RemoveFromClassList("segment-active");
        }
    }

    private static void SetModalVisible(VisualElement modal, bool visible)
    {
        if (modal != null)
        {
            modal.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static void ApplyPickingMode(VisualElement element, bool interactive)
    {
        if (element == null)
        {
            return;
        }

        element.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
        foreach (VisualElement child in element.Children())
        {
            ApplyPickingMode(child, interactive);
        }
    }

    // ---- Binding helpers ----

    private Button BindButton(string name, bool required = true)
    {
        Button button = _root.Q<Button>(name);
        if (button == null && required)
        {
            LogMissing(name);
        }

        return button;
    }

    private Label BindLabel(string name, bool required = true)
    {
        Label label = _root.Q<Label>(name);
        if (label == null && required)
        {
            LogMissing(name);
        }

        return label;
    }

    private VisualElement BindElement(string name, bool required = true)
    {
        VisualElement element = _root.Q<VisualElement>(name);
        if (element == null && required)
        {
            LogMissing(name);
        }

        return element;
    }

    private static void LogMissing(string name)
    {
        Debug.LogError("MainMenuController: missing UI element " + name);
    }
}
