using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single source of truth for cross-scene runtime routing (Phase 07A.1,
/// product flow corrected in the Phase 08 Main Menu revisions).
///
/// Normal player flow:
///
///   00_Boot -&gt; 04_MainMenu (title screen: 活下去 + 新游戏 / 继续游戏 / 选项 / 退出)
///   04_MainMenu --新游戏--&gt; 03_ShipInterior (three-beat eyelid open, first person)
///   04_MainMenu --继续游戏--&gt; 03_ShipInterior (short eyelid open, session kept)
///   03_ShipInterior --终端 A/E--&gt; the 总览 / Overview terminal UI (in scene)
///   03_ShipInterior --airlock--&gt; 02_NereidSurface
///   02_NereidSurface --landing--&gt; 03_ShipInterior (FromSurface spawn)
///   01_Hub --确认配置 / 开始远征--&gt; 02_NereidSurface
///
/// The title screen does NOT enter the 2D Hub: its options land in the walkable
/// ship, and Overview / Preparation / Processing / Outpost are reached by
/// walking to the ship's physical terminals. The Hub stays fully loadable
/// through this helper for Phase01-06 regression and explicit automation.
///
/// No gameplay code hardcodes a scene name anymore: Boot, the title screen, the
/// airlock, the landing return and the Hub path all route through this helper.
/// The tiny ship-entry reason (Default / FromSurface / FromMainMenu) and the
/// eyelid mode are stored here and consumed once by ShipGameController, so spawn
/// selection and the entry animation never depend on Hierarchy order.
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
        FromSurface,

        /// <summary>The product path: the title screen's 新游戏 / 继续游戏.</summary>
        FromMainMenu
    }

    /// <summary>
    /// Which first-person entry animation the next ShipInterior load should play
    /// (Phase 08 Revision 2 - the eyelids).
    /// </summary>
    public enum EntryEyelid
    {
        /// <summary>No eyelids: an ordinary cut (boot / airlock / regression).</summary>
        None,

        /// <summary>The three-beat eyelid open (新游戏).</summary>
        Full,

        /// <summary>A single shorter eyelid open (继续游戏).</summary>
        Quick
    }

    private static ShipEntry _pendingShipEntry = ShipEntry.Default;
    private static EntryEyelid _pendingEyelid = EntryEyelid.None;

    /// <summary>Requests the entry reason for the next ShipInterior load.</summary>
    public static void RequestShipEntry(ShipEntry entry)
    {
        _pendingShipEntry = entry;
    }

    /// <summary>Requests the entry reason AND the eyelid mode for the next load.</summary>
    public static void RequestShipEntry(ShipEntry entry, EntryEyelid eyelid)
    {
        _pendingShipEntry = entry;
        _pendingEyelid = eyelid;
    }

    /// <summary>Requests only the eyelid mode (keeps the pending entry reason).</summary>
    public static void RequestEntryEyelid(EntryEyelid eyelid)
    {
        _pendingEyelid = eyelid;
    }

    /// <summary>Returns and clears the pending reason (consumed by the ship
    /// scene; afterwards the next entry falls back to Default).</summary>
    public static ShipEntry ConsumeShipEntry()
    {
        ShipEntry entry = _pendingShipEntry;
        _pendingShipEntry = ShipEntry.Default;
        return entry;
    }

    /// <summary>Current pending reason, read-only (automation / verification).</summary>
    public static ShipEntry PendingShipEntry => _pendingShipEntry;

    /// <summary>Pending eyelid mode, read-only (automation / verification).</summary>
    public static EntryEyelid PendingEyelid => _pendingEyelid;

    /// <summary>Returns and clears the pending eyelid mode.</summary>
    public static EntryEyelid ConsumeEntryEyelid()
    {
        EntryEyelid eyelid = _pendingEyelid;
        _pendingEyelid = EntryEyelid.None;
        return eyelid;
    }

    /// <summary>True while a blink entry is pending (kept for existing callers).</summary>
    public static bool BlinkEntryPending => _pendingEyelid != EntryEyelid.None;

    /// <summary>Requests the first-person eyelid fade-in for the next entry.</summary>
    public static void RequestBlinkEntry()
    {
        RequestEntryEyelid(EntryEyelid.Full);
    }

    /// <summary>Legacy boolean consume: true when any eyelid was requested.</summary>
    public static bool ConsumeBlinkEntry()
    {
        return ConsumeEntryEyelid() != EntryEyelid.None;
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

    /// <summary>Enters the walkable ship interior with the requested spawn and
    /// first-person entry animation.</summary>
    public static void EnterShipInterior(ShipEntry entry,
        EntryEyelid eyelid = EntryEyelid.None)
    {
        Debug.Log("[SceneRouting] EnterShipInterior(" + entry + ", " + eyelid + ") -> "
                  + GameSession.ShipInteriorSceneName);
        RequestShipEntry(entry, eyelid);
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
