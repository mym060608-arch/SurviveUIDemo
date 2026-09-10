using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Explicit ship overlay modes (Phase 07B.2):
///   ShipExplore
///   -> DeploymentConfirm        (E on the Airlock deployment station)
///      -> Loadout (from deploy) (调整装备)
///   ShipExplore
///   -> Loadout (from locker)    (E on the Airlock equipment locker)
///      -> ShipExplore           (Esc / 返回)
///
/// Single overlay authority: owns the two page documents, the current mode,
/// input freezing, the unlocked cursor, Esc handling and the shared GameSession
/// loadout flow. Wrist / console focus can never stack on an overlay; the
/// console focus machine (ShipConsoleFocusController) and this overlay are
/// mutually exclusive by gating in ShipGameController.
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
    private LoadoutReturnTarget _loadoutReturn = LoadoutReturnTarget.ShipExplore;
    private bool _ready;
    private bool _pendingSurfaceRouting;

    public OverlayMode Mode => _mode;

    public bool IsOpen => _mode != OverlayMode.None;

    public LoadoutReturnTarget LoadoutReturn => _loadoutReturn;

    public ShipDeployConfirmPage DeployPage => _deployPage;

    public ShipLoadoutPage LoadoutPage => _loadoutPage;

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

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (_mode == OverlayMode.Loadout
                && _loadoutReturn == LoadoutReturnTarget.DeploymentConfirm)
            {
                ShowDeploymentConfirmPage();
            }
            else
            {
                CloseToExplore();
            }
        }
    }

    // ---- Public entry points (interactables / tests) ----

    /// <summary>E on the Airlock deployment station: show the deployment
    /// confirmation (no scene load yet).</summary>
    public bool OpenDeploymentConfirm()
    {
        if (!_ready || IsOpen || (_ship != null && _ship.IsConsoleFocusActive))
        {
            return false;
        }

        _mode = OverlayMode.DeploymentConfirm;
        ShowDeploymentConfirmPage();
        EnterOverlayInput();
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
        if (!_ready || IsOpen || (_ship != null && _ship.IsConsoleFocusActive))
        {
            return false;
        }

        _mode = OverlayMode.Loadout;
        _loadoutReturn = returnTarget;
        ShowLoadoutPage();
        EnterOverlayInput();
        return true;
    }

    /// <summary>Closes every page and restores ShipExplore input/cursor.</summary>
    public void CloseToExplore()
    {
        if (!_ready)
        {
            return;
        }

        _mode = OverlayMode.None;
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

    // ---- Page/action routing ----

    private void OnAdjustGear()
    {
        _mode = OverlayMode.Loadout;
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
        _mode = OverlayMode.DeploymentConfirm;
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
        _mode = OverlayMode.Loadout;
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
