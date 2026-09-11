using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Ship Interior pause menu (Phase 08 pause work).
///
/// The minimum complete pause loop the product was missing:
///
///     gameplay (free first person OR console view)
///         -- Menu/Start, Escape -->  Pause
///     暂停: 继续游戏 / 返回主菜单 / 退出游戏
///
/// While it is open the pause UI owns the input context: Time.timeScale is 0,
/// movement / look / world interaction / wrist / world-console pointer are all
/// frozen (see ShipGameController.SetPaused), the cursor is free and visible, and
/// every animation here runs on UNSCALED time. The GameSession is never touched.
///
/// Semantic vs physical input: this class only asks
/// <see cref="UiInput"/> for Confirm / Back / Pause / navigation. The physical
/// mapping (Xbox A / B / Menu, keys) lives in UiInput and nowhere else.
///
/// Ownership: it is created by ShipGameController at runtime (the validated
/// Phase 07 ship scene is not regenerated), which is the same pattern the entry
/// eyelids use. The document loads its UXML/USS from Resources and renders at a
/// high sorting order, above the ship HUD and the shipboard modals.
/// </summary>
public class ShipPauseMenu : MonoBehaviour
{
    public const string UxmlResourcePath = "ShipPauseMenu";
    public const string UssResourcePath = "ShipPauseMenu";

    /// <summary>Draws above the ship HUD (1) and the shipboard modals (30/31).</summary>
    public const int SortingOrder = 60;

    /// <summary>Seconds before a held stick/D-Pad starts repeating.</summary>
    private const float NavigationRepeatDelay = 0.32f;

    /// <summary>Seconds between repeats while the direction is held.</summary>
    private const float NavigationRepeatRate = 0.16f;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _overlay;
    private Label _title;
    private Label _hint;
    private Button _resume;
    private Button _mainMenu;
    private Button _quit;

    private readonly List<Button> _items = new List<Button>();
    private int _focusIndex;
    private bool _open;
    private bool _built;
    private float _previousTimeScale = 1f;
    private int _lastNavDirection;
    private float _nextNavTime;

    private ShipGameController _ship;
    private ShipConsoleFocusController _focus;

    /// <summary>True while the pause menu is showing.</summary>
    public bool IsOpen => _open;

    /// <summary>The pause headline (verification).</summary>
    public string TitleText => _title != null ? _title.text : string.Empty;

    /// <summary>Focused option index (0 = 继续游戏).</summary>
    public int FocusIndex => _focusIndex;

    /// <summary>The three options, in navigation order.</summary>
    public IReadOnlyList<Button> Items => _items;

    public Button ResumeButton => _resume;
    public Button MainMenuButton => _mainMenu;
    public Button QuitButton => _quit;

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

            if (focused == _resume)
            {
                return "Resume";
            }

            if (focused == _mainMenu)
            {
                return "ReturnToMainMenu";
            }

            return focused == _quit ? "QuitGame" : focused.name;
        }
    }

    /// <summary>The focused option.</summary>
    public Button FocusedButton =>
        _focusIndex >= 0 && _focusIndex < _items.Count ? _items[_focusIndex] : null;

    /// <summary>The timeScale that will be restored when the pause closes.</summary>
    public float PreviousTimeScale => _previousTimeScale;

    /// <summary>True once the UI tree is bound and the menu can be opened.</summary>
    public bool IsReady => _built;

    /// <summary>The hint line currently shown (device aware).</summary>
    public string HintText => _hint != null ? _hint.text : string.Empty;

    /// <summary>Runtime wiring + document creation (called by ShipGameController).</summary>
    public void Configure(PanelSettings panelSettings, ShipGameController ship,
        ShipConsoleFocusController focus)
    {
        _ship = ship;
        _focus = focus;

        if (_built)
        {
            return;
        }

        if (panelSettings == null)
        {
            Debug.LogError("[ShipPauseMenu] no PanelSettings: the pause menu cannot be built");
            return;
        }

        var visualTree = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
        var styleSheet = Resources.Load<StyleSheet>(UssResourcePath);
        if (visualTree == null)
        {
            Debug.LogError("[ShipPauseMenu] missing Resources/" + UxmlResourcePath + ".uxml");
            return;
        }

        _document = GetComponent<UIDocument>();
        if (_document == null)
        {
            _document = gameObject.AddComponent<UIDocument>();
        }

        _document.panelSettings = panelSettings;
        _document.visualTreeAsset = visualTree;
        _document.sortingOrder = SortingOrder;

        _root = _document.rootVisualElement;
        if (_root == null)
        {
            Debug.LogError("[ShipPauseMenu] UIDocument root is null");
            return;
        }

        if (styleSheet != null)
        {
            _root.styleSheets.Add(styleSheet);
        }

        UiFontProvider.ApplyTo(_root);

        _overlay = _root.Q<VisualElement>("PauseRoot");
        _title = _root.Q<Label>("PauseTitle");
        _hint = _root.Q<Label>("PauseHint");
        _resume = _root.Q<Button>("PauseResumeButton");
        _mainMenu = _root.Q<Button>("PauseMainMenuButton");
        _quit = _root.Q<Button>("PauseQuitButton");

        _items.Clear();
        TrackItem(_resume, ResumeGame);
        TrackItem(_mainMenu, ReturnToMainMenu);
        TrackItem(_quit, QuitGame);

        if (_overlay == null || _items.Count != 3)
        {
            Debug.LogError("[ShipPauseMenu] pause UI is incomplete (root="
                           + (_overlay != null) + " items=" + _items.Count + ")");
            return;
        }

        _overlay.style.display = DisplayStyle.None;
        _overlay.pickingMode = PickingMode.Ignore;
        _built = true;
        UpdateHint();
        Debug.Log("[ShipPauseMenu] ready items=" + _items.Count);
    }

    private void TrackItem(Button item, System.Action action)
    {
        if (item == null)
        {
            return;
        }

        _items.Add(item);

        // Same rule as the title screen: Unity's runtime UI synthesises a pointer
        // click from a gamepad confirm, and that click must never activate an item
        // (one press, one action).
        item.focusable = false;
        item.clicked += () =>
        {
            if (IsGamepadSynthesisedClick())
            {
                Debug.Log("[PAUSE] ignored a gamepad-synthesised click on " + item.name);
                return;
            }

            SetFocus(_items.IndexOf(item));
            action();
        };

        item.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (IsRealMousePointer())
            {
                SetFocus(_items.IndexOf(item));
            }
        });
    }

    private static bool IsGamepadSynthesisedClick()
    {
        return UiInput.GamepadConfirmPressedThisFrame();
    }

    private static bool IsRealMousePointer()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return Gamepad.current == null;
        }

        return mouse.delta.ReadValue().sqrMagnitude > 0.01f || mouse.leftButton.isPressed;
    }

    private void Update()
    {
        if (!_built)
        {
            return;
        }

        InputDeviceTracker.Poll();
        UpdateHint();

        if (!_open)
        {
            // Escape means different things per layer: inside an open device
            // panel it is that panel's Back key (the panel closes itself), while
            // in gameplay it is the global pause. Menu/Start pauses above an open
            // panel and leaves it as the frozen underlying state.
            bool overlayOpen = _ship != null && _ship.IsOverlayOpen;
            UiInput.UiLayer layer = overlayOpen ? UiInput.UiLayer.Modal : UiInput.UiLayer.Gameplay;

            if (UiInput.PausePressed(layer))
            {
                Debug.Log("[PAUSE] pause input seen: canPause=" + CanPause()
                          + " layer=" + layer
                          + " overlayOpen=" + overlayOpen
                          + " device=" + (Gamepad.current != null ? Gamepad.current.name : "-")
                          + "/" + (Keyboard.current != null ? Keyboard.current.name : "-"));
                if (CanPause())
                {
                    Open();
                }
            }

            return;
        }

        // Pause owns the input context: nothing else may act this frame.
        // A held direction repeats on a delay so one press can never step twice.
        int direction = UiInput.NavigationDirection();
        if (direction == 0)
        {
            _lastNavDirection = 0;
        }
        else
        {
            float now = Time.unscaledTime;
            if (direction != _lastNavDirection)
            {
                _lastNavDirection = direction;
                _nextNavTime = now + NavigationRepeatDelay;
                Step(direction);
            }
            else if (now >= _nextNavTime)
            {
                _nextNavTime = now + NavigationRepeatRate;
                Step(direction);
            }
        }

        if (UiInput.ConfirmPressed())
        {
            ConfirmFocused();
            return;
        }

        // The pause is the TOP layer: while it is open, a Back press belongs to
        // it (resume) and never to the device panel that may be open underneath.
        // Consuming the press makes that independent of update order.
        bool backPressed = UiInput.BackPressed();
        if (backPressed || UiInput.PausePressed())
        {
            if (backPressed)
            {
                UiInput.ConsumeBackForFrame();
            }

            ResumeGame();
        }
    }

    /// <summary>
    /// Menu/Start and Escape are GLOBAL pause actions: they work in free first
    /// person, inside the console view and above an OPEN device panel (the panel
    /// stays as the frozen underlying state, so 继续游戏 returns straight to it).
    /// Escape is resolved by layer in Update: inside a device panel it is that
    /// panel's Back key instead.
    /// </summary>
    private bool CanPause()
    {
        return _built && !_open;
    }

    // ---- State ----

    /// <summary>Opens the pause menu: freezes gameplay, focuses 继续游戏.</summary>
    public void Open()
    {
        if (!_built || _open)
        {
            return;
        }

        _open = true;
        _previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        _overlay.style.display = DisplayStyle.Flex;
        _overlay.pickingMode = PickingMode.Position;
        SetFocus(0);

        if (_ship != null)
        {
            _ship.SetPaused(true);
        }

        Debug.Log("[PAUSE] opened (timeScale=0, focus=" + FocusedItemId + ")");
    }

    /// <summary>继续游戏: closes the pause and hands control back exactly as it was.</summary>
    public void ResumeGame()
    {
        if (!_open)
        {
            return;
        }

        Close();
        Debug.Log("[PAUSE] Execute = ContinueGame");
    }

    /// <summary>
    /// 返回主菜单: real product loop. The session is NOT reset, so the title
    /// screen's 继续游戏 becomes available and returns to this same ship state.
    /// Any device panel / loadout page that was open (possibly underneath this
    /// pause) is cleaned first, so no panel visibility or UI focus survives into
    /// the title screen.
    /// </summary>
    public void ReturnToMainMenu()
    {
        Debug.Log("[PAUSE] Execute = ReturnToMainMenu");
        Close();

        // The pause state, the console view and the device panels must not
        // survive the scene change.
        _focus?.CleanupForSceneChange();
        _ship?.Overlay?.CleanupForSceneChange();
        SceneRouting.EnterMainMenu();
    }

    /// <summary>退出游戏: the one path in the pause menu that may terminate.</summary>
    public void QuitGame()
    {
        Debug.Log("[PAUSE] Execute = QuitGame");
        AppQuit.Request("Pause.QuitGame");
    }

    private void Close()
    {
        _open = false;
        Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;

        if (_overlay != null)
        {
            _overlay.style.display = DisplayStyle.None;
            _overlay.pickingMode = PickingMode.Ignore;
        }

        if (_ship != null)
        {
            _ship.SetPaused(false);
        }
    }

    /// <summary>Hands control back to the pre-pause state (used by tests/probes).</summary>
    public void CloseIfOpen()
    {
        if (_open)
        {
            ResumeGame();
        }
    }

    // ---- Focus / activation ----

    /// <summary>Moves the focus to an index (all pause options are enabled).</summary>
    public void SetFocus(int index)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _focusIndex = Mathf.Clamp(index, 0, _items.Count - 1);
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i] == null)
            {
                continue;
            }

            if (i == _focusIndex)
            {
                _items[i].AddToClassList("pause-item-focused");
            }
            else
            {
                _items[i].RemoveFromClassList("pause-item-focused");
            }
        }
    }

    /// <summary>Moves the focus by one option (wraps).</summary>
    public void Step(int direction)
    {
        if (_items.Count == 0 || direction == 0)
        {
            return;
        }

        int next = ((_focusIndex + direction) % _items.Count + _items.Count) % _items.Count;
        SetFocus(next);
    }

    /// <summary>Executes the focused option (Confirm).</summary>
    public bool ConfirmFocused()
    {
        Button focused = FocusedButton;
        if (focused == null || !_open)
        {
            return false;
        }

        if (focused == _resume)
        {
            ResumeGame();
        }
        else if (focused == _mainMenu)
        {
            ReturnToMainMenu();
        }
        else if (focused == _quit)
        {
            QuitGame();
        }

        return true;
    }

    private void UpdateHint()
    {
        if (_hint == null)
        {
            return;
        }

        // The hint is presentation only; the bindings stay in UiInput.
        string hint = InputDeviceTracker.PauseHint;
        if (_hint.text != hint)
        {
            _hint.text = hint;
        }
    }
}
