using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Minimal 00_Boot driver (Phase 07A.1 runtime entry, Phase 08 entry scene).
///
/// 1. ensures the authoritative GameSession exists (defaults are applied on a
///    fresh launch),
/// 2. enters the Phase 08 product cover (04_MainMenu), which is the only human
///    entry into the demo: Main Menu (left hero ship / right menu) ->
///    进入任务 -> start cinematic -> first-person ship interior.
///
/// Boot's job is initialization only - it still owns no UI and authors no
/// GameSession. The 2D Hub (01_Hub) and the walkable ship interior
/// (03_ShipInterior) stay fully loadable through SceneRouting for Phase01-07
/// regression and explicit automation.
/// </summary>
public class BootController : MonoBehaviour
{
    public const string HubSceneName = "01_Hub";

    /// <summary>
    /// Automation hook: with <c>-bootHub</c> the boot scene enters the legacy
    /// four-screen Hub directly instead of the Main Menu cover. Used by
    /// regression automation that needs the Hub without the cover in front of
    /// it; normal play is unaffected.
    /// </summary>
    public const string BootHubFlag = "-bootHub";

    /// <summary>
    /// Automation hook: with <c>-bootShip</c> the boot scene enters the walkable
    /// ship interior directly (the Phase 07A.1 entry) instead of the Main Menu
    /// cover. Used by ship automation that needs the ship without the cover in
    /// front of it; normal play is unaffected.
    /// </summary>
    public const string BootShipFlag = "-bootShip";

    private void Awake()
    {
        // Session Awake applies defaults on first creation.
        GameSession.Ensure();
    }

    private void Start()
    {
        if (HasCommandLineFlag(BootHubFlag))
        {
            Debug.Log("[BootController] " + BootHubFlag + " -> legacy Hub");
            SceneRouting.EnterHub();
            return;
        }

        if (HasCommandLineFlag(BootShipFlag))
        {
            Debug.Log("[BootController] " + BootShipFlag + " -> ship interior");
            SceneRouting.EnterShipInterior(SceneRouting.ShipEntry.Default);
            return;
        }

        SceneRouting.EnterMainMenu();
    }

    private static bool HasCommandLineFlag(string flag)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
