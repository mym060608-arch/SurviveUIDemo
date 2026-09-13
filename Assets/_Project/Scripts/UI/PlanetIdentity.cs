/// <summary>
/// Single source of truth for the current planet's PLAYER-FACING display name
/// (Phase 09B PASS 7, extended to every screen in the PASS 7 follow-up).
///
/// The Overview hero title, its mission objective line, the Preparation action
/// bar, the deployment sheet, the surface HUD and the wrist identity all name
/// the same planet. Hardcoding "AURELIA-01" in each of them is exactly how one
/// page ends up showing two different planets, so every player-facing node reads
/// its copy from here (or from the same literal in the authored UXML, which the
/// structural validator pins).
///
/// Scope note: this is PRESENTATION copy only. It never touches gameplay IDs -
/// the scene (`02_NereidSurface`), the surface builder, the routing and the
/// GameSession state keep their internal NEREID names.
/// </summary>
public static class PlanetIdentity
{
    /// <summary>Display name of the planet the current expedition targets.</summary>
    public const string DisplayName = "AURELIA-01";

    /// <summary>Objective line that names the planet ("勘测 AURELIA-01").</summary>
    public static string SurveyObjective => "勘测 " + DisplayName;

    /// <summary>Hero subtitle under the planet name (authored, Chinese-first).</summary>
    public const string HeroSubtitle = "未知地表 · 资源候选区";

    /// <summary>Wrist identity on the surface ("AURELIA-01 · 地表勘测").</summary>
    public static string SurfaceIdentity => DisplayName + " · 地表勘测";

    /// <summary>Preparation action-bar context ("AURELIA-01 · 出发准备").</summary>
    public static string ExpeditionContext => DisplayName + " · 出发准备";

    /// <summary>Airlock interaction prompt ("前往 AURELIA-01 地表").</summary>
    public static string SurfacePrompt => "前往 " + DisplayName + " 地表";

    /// <summary>
    /// True when a piece of copy still names the OLD planet. Used by the
    /// structural validator and the scoped PlayMode suite so a stale display
    /// name can never silently come back to any player-facing surface.
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
