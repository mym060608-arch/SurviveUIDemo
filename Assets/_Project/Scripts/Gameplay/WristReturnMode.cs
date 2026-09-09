/// <summary>
/// Source mode the wrist terminal was opened from (Phase 07B).
///
/// The wrist is one shared personal terminal. Whichever first-person
/// exploration mode opened it must get it back exactly on close:
///
///   SurfaceFirstPerson - Phase 06 surface exploration (unchanged behavior)
///   ShipExplore        - Phase 07 walkable ARK-07 interior
///
/// Wrist must never open while a physical console is focused (console and
/// wrist are separate modes; no stacking).
/// </summary>
public enum WristReturnMode
{
    /// <summary>Opened on the NEREID-04 surface (02_NereidSurface).</summary>
    SurfaceFirstPerson,

    /// <summary>Opened inside the walkable ship interior (03_ShipInterior).</summary>
    ShipExplore
}
