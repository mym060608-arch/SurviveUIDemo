using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Minimal 00_Boot driver (Phase 07A.1 runtime entry).
///
/// 1. ensures the authoritative GameSession exists (defaults are applied on a
///    fresh launch),
/// 2. enters the walkable ship interior (03_ShipInterior, Default spawn).
///
/// The legacy 2D Hub (01_Hub) is no longer the human boot destination but
/// stays fully loadable through SceneRouting.EnterHub for Phase01-06
/// regression and explicit automation.
/// </summary>
public class BootController : MonoBehaviour
{
    public const string HubSceneName = "01_Hub";

    private void Awake()
    {
        // Session Awake applies defaults on first creation.
        GameSession.Ensure();
    }

    private void Start()
    {
        SceneRouting.EnterShipInterior(SceneRouting.ShipEntry.Default);
    }
}
