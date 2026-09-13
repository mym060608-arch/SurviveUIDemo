/// <summary>
/// Single source of truth for the current planet's PLAYER-FACING display name
/// (Phase 09B PASS 7).
///
/// The Overview page names the current planet in more than one node (the hero
/// title and the mission objective line). Hardcoding "AURELIA-01" in each of
/// them is exactly how a page ends up showing two different planets at once, so
/// every Overview node reads its copy from here instead.
///
/// Scope note: this is PRESENTATION copy only. It never touches gameplay IDs -
/// the surface scene, the resource nodes, the routing and the GameSession state
/// keep their internal NEREID names; other screens (Preparation, the airlock
/// prompt, the wrist terminal, the surface HUD) still carry their own authored
/// copy and are deliberately NOT rewritten by the Overview pass.
/// </summary>
public static class PlanetIdentity
{
    /// <summary>Display name of the planet the current expedition targets.</summary>
    public const string DisplayName = "AURELIA-01";

    /// <summary>Objective line that names the planet ("勘测 AURELIA-01").</summary>
    public static string SurveyObjective => "勘测 " + DisplayName;

    /// <summary>Hero subtitle under the planet name (authored, Chinese-first).</summary>
    public const string HeroSubtitle = "未知地表 · 资源候选区";

    /// <summary>
    /// True when a piece of copy names a planet that is NOT the current display
    /// name. Used by the structural validator and the scoped PlayMode suite so a
    /// stale name can never silently come back to this page.
    /// </summary>
    public static bool NamesAnotherPlanet(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.Contains("NEREID") && !text.Contains(DisplayName);
    }
}
