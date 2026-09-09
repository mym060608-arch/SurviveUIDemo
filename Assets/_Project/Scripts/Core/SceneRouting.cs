using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single source of truth for cross-scene runtime routing (Phase 07A.1).
///
/// Normal player flow:
///
///   00_Boot -&gt; 03_ShipInterior (Default spawn)
///   03_ShipInterior --airlock E--&gt; 02_NereidSurface
///   02_NereidSurface --landing E--&gt; 03_ShipInterior (FromSurface spawn)
///
/// The legacy 01_Hub stays loadable (EnterHub) for Phase01-06 regression and
/// remains the 2D UI scene; it is no longer the human boot destination.
///
/// No gameplay code hardcodes a scene name anymore: Boot, the airlock, the
/// landing return and the Hub path all route through this helper. The tiny
/// ship-entry reason (Default / FromSurface) is stored here and consumed once
/// by ShipGameController so spawn selection never depends on Hierarchy order.
/// </summary>
public static class SceneRouting
{
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

    /// <summary>Loads the legacy 2D Hub (regression / explicit automation).</summary>
    public static void EnterHub()
    {
        SceneManager.LoadScene(BootController.HubSceneName);
    }

    /// <summary>Enters the walkable ship interior with the requested spawn.</summary>
    public static void EnterShipInterior(ShipEntry entry)
    {
        RequestShipEntry(entry);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
    }

    /// <summary>Enters the NEREID-04 surface (from the ship airlock or the Hub
    /// preparation CTA). The surface itself does not gate on the loadout
    /// confirmation flag - confirmation only gates the Hub CTA ceremony.</summary>
    public static void EnterSurface()
    {
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
    }

    public static bool IsSceneActive(string sceneName)
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() && active.name == sceneName;
    }
}
