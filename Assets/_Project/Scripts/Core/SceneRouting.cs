using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single source of truth for cross-scene runtime routing (Phase 07A.1).
///
/// Normal player flow (Phase 08 adds the product cover):
///
///   00_Boot -&gt; 04_MainMenu (product cover / entry)
///   04_MainMenu --进入任务--&gt; 01_Hub (Overview, the existing UI demo flow)
///   01_Hub --确认配置 / 开始远征--&gt; 02_NereidSurface
///   02_NereidSurface --landing E--&gt; 03_ShipInterior (FromSurface spawn)
///   03_ShipInterior --airlock E--&gt; 02_NereidSurface
///
/// The 2D Hub stays the navigation authority for Overview / Preparation /
/// Processing / Outpost, and remains directly loadable for Phase01-06
/// regression and explicit automation.
///
/// No gameplay code hardcodes a scene name anymore: Boot, the Main Menu, the
/// airlock, the landing return and the Hub path all route through this helper.
/// The tiny ship-entry reason (Default / FromSurface) is stored here and
/// consumed once by ShipGameController so spawn selection never depends on
/// Hierarchy order.
/// </summary>
public static class SceneRouting
{
    /// <summary>The Phase 08 product cover scene (loaded by Boot).</summary>
    public const string MainMenuSceneName = "04_MainMenu";

    /// <summary>The boot scene name (also used as the player panel-reset hop).</summary>
    public const string BootSceneName = "00_Boot";

    /// <summary>Which spawn the next ShipInterior entry should use.</summary>
    public enum ShipEntry
    {
        Default,
        FromSurface
    }

    private static ShipEntry _pendingShipEntry = ShipEntry.Default;

    /// <summary>Requests the entry reason for the next ShipInterior load.</summary>
    public static void RequestShipEntry(ShipEntry entry)
    {
        _pendingShipEntry = entry;
    }

    /// <summary>Returns and clears the pending reason (consumed by the ship
    /// scene; afterwards the next entry falls back to Default).</summary>
    public static ShipEntry ConsumeShipEntry()
    {
        ShipEntry entry = _pendingShipEntry;
        _pendingShipEntry = ShipEntry.Default;
        return entry;
    }

    /// <summary>Loads the legacy 2D Hub (the existing UI demo flow: Overview /
    /// Preparation / Processing / Outpost).</summary>
    public static void EnterHub()
    {
        Debug.Log("[SceneRouting] EnterHub -> " + BootController.HubSceneName);
        SceneManager.LoadScene(BootController.HubSceneName);
    }

    /// <summary>Loads the Phase 08 product cover (Main Menu).</summary>
    public static void EnterMainMenu()
    {
        Debug.Log("[SceneRouting] EnterMainMenu -> " + MainMenuSceneName);
        SceneManager.LoadScene(MainMenuSceneName);
    }

    /// <summary>Enters the walkable ship interior with the requested spawn.</summary>
    public static void EnterShipInterior(ShipEntry entry)
    {
        Debug.Log("[SceneRouting] EnterShipInterior(" + entry + ") -> "
                  + GameSession.ShipInteriorSceneName);
        RequestShipEntry(entry);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
    }

    /// <summary>Enters the NEREID-04 surface (from the ship airlock or the Hub
    /// preparation CTA). The surface itself does not gate on the loadout
    /// confirmation flag - confirmation only gates the Hub CTA ceremony.</summary>
    public static void EnterSurface()
    {
        Debug.Log("[SceneRouting] EnterSurface -> " + GameSession.SurfaceSceneName);
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
    }

    public static bool IsSceneActive(string sceneName)
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() && active.name == sceneName;
    }
}
