using System;
using UnityEngine;

/// <summary>
/// Explicit ship overlay modes (Phase 07B.2):
///   ShipExplore
///   -> DeploymentConfirm        (E on the Airlock deployment station)
///      -> Loadout (from deploy) (调整装备)
///   ShipExplore
///   -> Loadout (from locker)    (E on the Airlock equipment locker)
///      -> ShipExplore           (Back / 返回)
///
/// Single overlay authority: owns the two page documents, the current mode,
/// input freezing, the unlocked cursor, the Back action and the shared
/// GameSession loadout flow. Wrist / console focus can never stack on an
/// overlay; the console focus machine (ShipConsoleFocusController) and this
/// overlay are mutually exclusive by gating in ShipGameController.
///
/// Back contract (Phase 08 device-panel hotfix). The 装备整理 (equipment
/// locker) and 出征前准备 (deployment station) panels are exactly the two
/// modes above - there is no second panel implementation and no second input
/// path. Back is the logical action (<see cref="UiInput.BackPressed"/>:
/// Xbox B or Escape), never a raw key read, and it follows the ship dispatch
/// chain:
///
///   pause menu open  >  THIS layer  >  console focus  >  free first person
///
/// so ONE press performs ONE action:
///   - the pause is checked first, because a device panel can stay open
///     underneath it (Menu/Start pauses above the panel, 继续游戏 returns to
///     it) and Back there must only resume the game;
///   - this layer consumes Back whenever it is open: the deployment
///     confirmation closes back to ShipExplore, the locker loadout closes
///     back to ShipExplore, and the loadout opened FROM the deployment
///     confirmation steps back to that confirmation instead of leaving it;
///   - nothing below may see the press, and closing never quits, never
///     returns to the title screen and never resets GameSession.
/// </summary>
public class ShipOverlayUi : MonoBehaviour
{
    public enum OverlayMode
    {
        None,
        DeploymentConfirm,
        Loadout
    }

    /// <summary>Context the shared loadout view returns to when closed.</summary>
    public enum LoadoutReturnTarget
    {
        ShipExplore,      // opened from the equipment locker
        DeploymentConfirm // opened from the deployment confirmation
    }

    private ShipDeployConfirmPage _deployPage;
    private ShipLoadoutPage _loadoutPage;
    private SurfacePlayerController _player;
    private ShipGameController _ship;

    private OverlayMode _mode = OverlayMode.None;

    /// <summary>
    /// The one place that changes the panel mode. It also publishes whether this
    /// layer owns Escape right now (see UiInput.ActiveBackLayer): Modal while a
    /// panel is up, so Escape closes that panel and never stacks a pause on it.
    /// </summary>
    private void SetMode(OverlayMode mode)
    {
        _mode = mode;
        UiInput.SetLayerActive(UiInput.UiLayer.Modal, mode != OverlayMode.None);
    }

    private LoadoutReturnTarget _loadoutReturn = LoadoutReturnTarget.ShipExplore;
    private bool _ready;
    private bool _pendingSurfaceRouting;

    public OverlayMode Mode => _mode;

    public bool IsOpen => _mode != OverlayMode.None;

    public LoadoutReturnTarget LoadoutReturn => _loadoutReturn;

    public ShipDeployConfirmPage DeployPage => _deployPage;

    public ShipLoadoutPage LoadoutPage => _loadoutPage;

    /// <summary>
    /// How many Back presses this authority consumed. Tests use it to pin
    /// "one press = one action": one B must move the chain exactly one step.
    /// </summary>
    public int BackActionCount { get; private set; }

    /// <summary>Device-aware "返回" hint currently shown by the open panel
    /// ("" when nothing is open) - the visible half of the Back contract.</summary>
    public string BackHintText
    {
        get
        {
            if (_mode == OverlayMode.DeploymentConfirm)
            {
                return _deployPage != null ? _deployPage.BackHintText : string.Empty;
            }

            if (_mode == OverlayMode.Loadout)
            {
                return _loadoutPage != null ? _loadoutPage.BackHintText : string.Empty;
            }

            return string.Empty;
        }
    }

    private void Start()
    {
        _ship = UnityEngine.Object.FindAnyObjectByType<ShipGameController>();
        _player = UnityEngine.Object.FindAnyObjectByType<SurfacePlayerController>();
        _deployPage = UnityEngine.Object.FindAnyObjectByType<ShipDeployConfirmPage>();
        _loadoutPage = UnityEngine.Object.FindAnyObjectByType<ShipLoadoutPage>();

        if (_deployPage != null)
        {
            _deployPage.AdjustGearRequested += OnAdjustGear;
            _deployPage.ConfirmLandingRequested += OnConfirmLanding;
            _deployPage.CancelRequested += CloseToExplore;
        }

        if (_loadoutPage != null)
        {
            _loadoutPage.CloseRequested += OnLoadoutCloseRequested;
        }

        _ready = true;
        Debug.Log("[ShipOverlayUi] ready deploy=" + (_deployPage != null)
                  + " loadout=" + (_loadoutPage != null));
    }

    private void OnDestroy()
    {
        if (_deployPage != null)
        {
            _deployPage.AdjustGearRequested -= OnAdjustGear;
            _deployPage.ConfirmLandingRequested -= OnConfirmLanding;
            _deployPage.CancelRequested -= CloseToExplore;
        }

        if (_loadoutPage != null)
        {
            _loadoutPage.CloseRequested -= OnLoadoutCloseRequested;
        }
    }

    private void Update()
    {
        if (!_ready)
        {
            return;
        }

        // Publish this layer's own state every frame (cheap and idempotent).
        // Escape is handed to the highest-priority local layer (pause > this
        // modal > wrist > console view) and that answer must not depend on which
        // MonoBehaviour updates first, so the press below is only taken when this
        // layer still owns Back.
        UiInput.SetLayerActive(UiInput.UiLayer.Modal, IsOpen);

        // The 确认降临 handler defers the scene switch by one frame so the
        // UI Toolkit pointer dispatch (which invoked the button) fully
        // completes before the scene teardown begins.
        if (_pendingSurfaceRouting)
        {
            _pendingSurfaceRouting = false;
            SceneRouting.EnterSurface();
            return;
        }

        if (!IsOpen)
        {
            return;
        }

        // Dispatch priority, step 1: the pause menu is the TOP layer and it owns
        // the input context while it is open. A device panel can stay open
        // underneath it (§14.4), so Back here must NOT close that panel - the
        // press belongs to the pause, which only resumes the game.
        // BackPressedForLayer enforces exactly that, from published state rather
        // than update order.
        if (_ship != null && _ship.IsPaused)
        {
            return;
        }

        // Dispatch priority, step 2: this modal layer consumes Back.
        // UiInput is the only place that names the physical control (Xbox B /
        // Escape); nothing here reads a key directly.
        if (UiInput.BackPressedForLayer(UiInput.UiLayer.Modal))
        {
            HandleBack();
        }
    }

    private void OnDisable()
    {
        // Never leave a layer flagged as open when this component goes away (the
        // ship scene unloading must not strand the Escape priority board).
        UiInput.SetLayerActive(UiInput.UiLayer.Modal, false);
    }

    // ---- Public entry points (interactables / tests) ----

    /// <summary>
    /// The Back action of this modal layer: exactly one step out.
    ///
    ///   DeploymentConfirm                     -> ShipExplore (panel closed)
    ///   Loadout opened from the locker        -> ShipExplore (panel closed)
    ///   Loadout opened from the deployment
    ///   confirmation (调整装备)               -> the deployment confirmation
    ///
    /// Returns true when the press was consumed by this layer. Closing restores
    /// the pre-panel Ship Interior state (movement, look, world interaction,
    /// HUD prompt and cursor): it never quits, never reaches the title screen
    /// and never resets GameSession.
    /// </summary>
    public bool HandleBack()
    {
        if (!_ready || !IsOpen)
        {
            return false;
        }

        if (_ship != null && _ship.IsPaused)
        {
            // Defensive: the pause owns Back while it is open (see Update).
            return false;
        }

        BackActionCount++;

        // The press is consumed HERE for this frame: whichever layer saw it
        // first owns it (see UiInput.ConsumeBackForFrame), so closing this panel
        // can never also resume a pause or open one.
        UiInput.ConsumeBackForFrame();

        if (_mode == OverlayMode.Loadout
            && _loadoutReturn == LoadoutReturnTarget.DeploymentConfirm)
        {
            ShowDeploymentConfirmPage();
            Debug.Log("[OVERLAY] Back -> 出征前准备 (deployment confirmation)");
            return true;
        }

        OverlayMode from = _mode;
        CloseToExplore();
        Debug.Log("[OVERLAY] Back -> ShipExplore (closed " + from + ")");
        return true;
    }

    /// <summary>E on the Airlock deployment station: show the deployment
    /// confirmation (no scene load yet).</summary>
    public bool OpenDeploymentConfirm()
    {
        if (!CanOpen())
        {
            return false;
        }

        SetMode(OverlayMode.DeploymentConfirm);
        ShowDeploymentConfirmPage();
        EnterOverlayInput();
        Debug.Log("[OVERLAY] opened 出征前准备 (deployment confirmation)");
        return true;
    }

    /// <summary>E on the Airlock equipment locker: open the shared loadout
    /// view returning to ShipExplore.</summary>
    public bool OpenLoadoutFromLocker()
    {
        return OpenLoadout(LoadoutReturnTarget.ShipExplore);
    }

    /// <summary>Opens the shared loadout view with the given return context.</summary>
    public bool OpenLoadout(LoadoutReturnTarget returnTarget)
    {
        if (!CanOpen())
        {
            return false;
        }

        SetMode(OverlayMode.Loadout);
        _loadoutReturn = returnTarget;
        ShowLoadoutPage();
        EnterOverlayInput();
        Debug.Log("[OVERLAY] opened 装备整理 (loadout, return=" + returnTarget + ")");
        return true;
    }

    /// <summary>
    /// A page may open only from plain ShipExplore: never on top of an already
    /// open page, a focused console or the pause menu (the pause freezes the
    /// whole input context and must stay the only layer receiving input).
    /// </summary>
    private bool CanOpen()
    {
        return _ready && !IsOpen
               && (_ship == null || (!_ship.IsConsoleFocusActive && !_ship.IsPaused));
    }

    /// <summary>Closes every page and restores ShipExplore input/cursor.</summary>
    public void CloseToExplore()
    {
        if (!_ready)
        {
            return;
        }

        SetMode(OverlayMode.None);
        if (_deployPage != null)
        {
            _deployPage.SetVisible(false);
        }

        if (_loadoutPage != null)
        {
            _loadoutPage.SetVisible(false);
        }

        ExitOverlayInput();
    }

    /// <summary>
    /// Drops every open page and every local UI focus for a scene change
    /// (pause -> 返回主菜单). Nothing of the panel survives into the title
    /// screen: no page stays visible, no document keeps the keyboard focus and
    /// the pointer owner goes back to None, so the menu scene starts clean.
    /// </summary>
    public void CleanupForSceneChange()
    {
        if (!_ready)
        {
            return;
        }

        if (IsOpen)
        {
            Debug.Log("[OVERLAY] cleanup for scene change (was " + _mode + ")");
        }

        CloseToExplore();
    }

    // ---- Page/action routing ----

    private void OnAdjustGear()
    {
        SetMode(OverlayMode.Loadout);
        _loadoutReturn = LoadoutReturnTarget.DeploymentConfirm;
        ShowLoadoutPage();
    }

    private void OnConfirmLanding()
    {
        // Deferred one frame (see Update) so the UI Toolkit pointer dispatch
        // completes before the accepted ShipInterior -> Surface routing runs;
        // GameSession persists (DontDestroyOnLoad), exactly one authoritative
        // session.
        CloseToExplore();
        _pendingSurfaceRouting = true;
    }

    private void OnLoadoutCloseRequested()
    {
        if (_mode != OverlayMode.Loadout)
        {
            return;
        }

        if (_loadoutReturn == LoadoutReturnTarget.DeploymentConfirm)
        {
            ShowDeploymentConfirmPage();
        }
        else
        {
            CloseToExplore();
        }
    }

    private void ShowDeploymentConfirmPage()
    {
        SetMode(OverlayMode.DeploymentConfirm);
        if (_deployPage != null)
        {
            _deployPage.RefreshAll();
            _deployPage.SetVisible(true);
        }

        if (_loadoutPage != null)
        {
            _loadoutPage.SetVisible(false);
        }

        ApplyCursor();
    }

    private void ShowLoadoutPage()
    {
        SetMode(OverlayMode.Loadout);
        if (_loadoutPage != null)
        {
            _loadoutPage.SetVisible(true);
        }

        if (_deployPage != null)
        {
            _deployPage.SetVisible(false);
        }

        ApplyCursor();
    }

    // ---- Input / cursor authority ----
    //
    // Ownership is NOT decided here any more (Phase 07B.2.3): this coordinator
    // only reports its mode to ShipGameController, which arbitrates the single
    // pointer owner (screen-space modal here, console router or wrist
    // elsewhere). The inline fallbacks below exist only when no ship controller
    // is present.

    private void EnterOverlayInput()
    {
        if (_ship != null)
        {
            _ship.ApplyPointerOwnership();
            return;
        }

        if (_player != null)
        {
            _player.InputEnabled = false; // WASD + mouse look OFF
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ExitOverlayInput()
    {
        if (_ship != null)
        {
            _ship.ApplyPointerOwnership();
            return;
        }

        if (_player != null)
        {
            _player.InputEnabled = true;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ApplyCursor()
    {
        if (_ship != null)
        {
            _ship.ApplyPointerOwnership();
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void NotifyShip()
    {
        if (_ship != null)
        {
            _ship.ApplyPointerOwnership();
        }
    }
}
