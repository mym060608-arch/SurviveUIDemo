using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Which wrist module an element of the hologram belongs to (Phase 09B PASS 3).
///
/// Focus and pulses are addressed by MODULE, never by pixel: the central map and
/// the four peripheral Parallel-Cap panels each have one identity, so "the
/// equipment edge is brighter" has exactly one meaning for the mesh, the text
/// rows and the controller.
/// </summary>
public enum WristModuleId
{
    None = 0,

    /// <summary>The central circular local map.</summary>
    Map = 1,

    /// <summary>Upper-left panel: 当前装备.</summary>
    Equipment = 2,

    /// <summary>Upper-right panel: 当前状态.</summary>
    State = 3,

    /// <summary>Lower-left panel: 当前导航.</summary>
    Navigation = 4,

    /// <summary>Lower-right panel: 采集状态.</summary>
    Harvest = 5
}

/// <summary>
/// Draws the complete radial geometry for the Phase 06 wrist hologram
/// (Round 9.2 parallel-cap architecture, Phase 09B PASS 3 industrial restyle).
///
/// Round 9.2 rules (still enforced by validation):
/// - No Painter2D.
/// - No tick VisualElements / dot-generated arcs.
/// - No rectangle-card geometry and no one-continuous full outer functional
///   ring. The four surrounding panels are Parallel-Cap Annular Panels:
///   annulus (PanelInnerRadius..PanelOuterRadius) intersected with a
///   horizontal Y band, so every panel has a horizontal top edge and a
///   horizontal bottom edge (top/bottom exactly parallel) plus curved inner
///   and outer edges.
/// - Health and Satiety share one status-track helper whose inner radius is
///   the SAME shared PanelInnerRadius (248), so the status arcs are visually
///   integrated with the panels instead of floating on a distant orbit.
/// - Geometry is produced with triangle meshes only.
///
/// PASS 3 (this revision) changes PRESENTATION only - every constant above is
/// byte-identical, so the frozen Phase 06 mapping, row slots and panel bands are
/// untouched:
/// - the four panels are cool translucent structural plates (no paper white):
///   dark slate fill, light cyan hairlines, a restrained inset rail per panel;
/// - the central map keeps its frosted translucent disc, gains a very light dot
///   lattice and is still the brightest layer of the hologram;
/// - Health / Satiety stay bound to the same 55..125 / 235..305 tracks and gain
///   a slow breathing modulation;
/// - one module at a time can be FOCUSED (edge lift, 120 ms smooth) and PULSED
///   (short confirm/state-change flash);
/// - the navigation module owns a small standby radar (rotating sweep + "?")
///   that turns into a live bearing indicator once a target is set;
/// - harvesting / transmitting slides a few thin dashes along the harvest
///   panel's inset rail (data in transit; no particle spam).
///
/// Fixed design space: 1280 x 800 logical pixels, centre (640, 400).
/// Angle convention: 0 deg = right, 90 = up, 180 = left, 270 = down.
/// UI Toolkit Y grows downward, so Polar() inverts sin for Y.
/// </summary>
public sealed class RadialWristElement : VisualElement
{
    public const float DesignWidth = 1280f;
    public const float DesignHeight = 800f;

    public static readonly Vector2 Center =
        new Vector2(640f, 400f);

    // Map (unchanged: the accepted central circular map stays radius 235).
    public const float MapRadius = 235f;

    // --------------------------------------------------------------------
    // Round 9.2 shared radial geometry (exact constants, validated).
    // The panel inner radius and the status track inner radius are ONE value
    // (248) so Health/Satiety visually join the functional assembly.
    // --------------------------------------------------------------------

    public const float PanelInnerRadius = 248f;
    public const float PanelOuterRadius = 365f;

    public const float StatusInnerRadius = 248f;
    public const float StatusOuterRadius = 262f;
    public const float StatusThickness = 14f; // 262 - 248

    // Parallel-cap panel horizontal Y bands (UI Toolkit y down).
    public const float UpperPanelTopY = 205f;
    public const float UpperPanelBottomY = 345f;
    public const float LowerPanelTopY = 455f;
    public const float LowerPanelBottomY = 595f;

    /// <summary>Radial thickness of the four functional panels (365-248).</summary>
    public const float FunctionalPanelThickness =
        PanelOuterRadius - PanelInnerRadius;

    // Status arc angular spans (degrees, 0 = right / +y-up space).
    public const float HealthArcStartDeg = 55f;
    public const float HealthArcEndDeg = 125f;
    public const float HealthArcSpanDeg = HealthArcEndDeg - HealthArcStartDeg; // 70

    public const float SatietyArcStartDeg = 235f;
    public const float SatietyArcEndDeg = 305f;
    public const float SatietyArcSpanDeg = SatietyArcEndDeg - SatietyArcStartDeg; // 70

    // --------------------------------------------------------------------
    // PASS 3: navigation standby radar (lives in the LOWER-LEFT panel, below
    // the 当前导航 rows, inside the padded slot at that Y).
    // --------------------------------------------------------------------

    public static readonly Vector2 RadarCenter = new Vector2(390f, 572f);
    public const float RadarRadius = 17f;

    /// <summary>Focus transition time (contract: 100-150 ms smooth).</summary>
    public const float FocusTransitionSeconds = 0.12f;

    /// <summary>Confirm pulse length (contract: a very short flash).</summary>
    public const float ConfirmPulseSeconds = 0.18f;

    // --------------------------------------------------------------------
    // Colors - Phase 09B PASS 3 cool industrial palette.
    //
    // The four panels used to be a LIGHT gray wash with dark ink text, which
    // read as paper scraps floating over the world. They are now cool slate
    // plates: the fill is darker than the frosted map disc (so the central map
    // stays the first visual level by construction), the edges are light cyan
    // hairlines and the text is light. No neon: every accent stays below 240
    // alpha and only ONE module is ever lifted at a time.
    // --------------------------------------------------------------------

    private static readonly Color32 Accent =
        new Color32(14, 127, 140, 230);

    private static readonly Color32 AccentWeak =
        new Color32(14, 127, 140, 72);

    private static readonly Color32 Hairline =
        new Color32(105, 122, 132, 105);

    private static readonly Color32 VeryWeakHairline =
        new Color32(105, 122, 132, 42);

    // Central map MUST NOT become a white board: fill alpha stays ~0.10-0.15.
    private static readonly Color32 MapFill =
        new Color32(176, 196, 206, 30);

    // Very light procedural dot lattice inside the map disc (the "非常轻的
    // 点阵/颗粒层次" layer). Alpha 14 keeps it a texture, never a pattern.
    private static readonly Color32 MapGrain =
        new Color32(206, 224, 232, 15);

    // Cool translucent structural plate (35, 48, 58) at ~46% - deliberately
    // darker than the map disc so the panels can never out-shine the map.
    private static readonly Color32 PanelFill =
        new Color32(35, 48, 58, 118);

    // Focus lift: a faint accent wash inside the focused plate.
    private static readonly Color32 PanelFillFocused =
        new Color32(24, 104, 122, 60);

    private static readonly Color32 PanelEdge =
        new Color32(120, 166, 180, 88);

    private static readonly Color32 PanelEdgeOuter =
        new Color32(148, 200, 214, 122);

    private static readonly Color32 PanelEdgeFocused =
        new Color32(146, 224, 238, 232);

    // Inset industrial rail + its corner ticks (少量工业线条).
    private static readonly Color32 RailColor =
        new Color32(140, 188, 202, 54);

    private static readonly Color32 RailColorFocused =
        new Color32(132, 210, 226, 116);

    // Harvest data-flow dashes (only while harvesting / transmitting).
    private static readonly Color32 FlowColor =
        new Color32(122, 214, 232, 205);

    // Navigation radar.
    private static readonly Color32 RadarIdle =
        new Color32(128, 168, 182, 92);

    private static readonly Color32 RadarActiveColor =
        new Color32(122, 208, 226, 210);

    // The same inactive track color is used by BOTH status tracks.
    private static readonly Color32 InactiveArc =
        new Color32(145, 156, 162, 58);

    /// <summary>Health active fill: muted green #5EAD68 (no fluorescent green).</summary>
    public static readonly Color32 HealthActiveColor =
        new Color32(0x5E, 0xAD, 0x68, 235);

    /// <summary>Satiety active fill: dark muted orange #A9632F (no bright orange).</summary>
    public static readonly Color32 SatietyActiveColor =
        new Color32(0xA9, 0x63, 0x2F, 235);

    // --------------------------------------------------------------------

    private float _health01 = 1f;
    private float _satiety01 = 0.82f;

    private readonly float[] _moduleFocus = new float[6];
    private readonly float[] _modulePulse = new float[6];
    private WristModuleId _focusTarget = WristModuleId.None;

    private float _time;
    private bool _harvestFlowing;
    private bool _radarActive;
    private float _radarBearingDeg;

    /// <summary>
    /// Phase 07B.1: when true the central map is a SHIP schematic host - the
    /// generic surface-map decorations (distance rings + reference cross) are
    /// suppressed and ShipSchematicVisual draws the deck plan inside the same
    /// circular frame. The disc fill + boundary stay so the wrist keeps its
    /// round technical interface in both modes.
    /// </summary>
    public bool ShipSchematicMode { get; set; }

    public RadialWristElement()
    {
        pickingMode = PickingMode.Ignore;

        style.position = Position.Absolute;
        style.left = 0f;
        style.top = 0f;
        style.width = DesignWidth;
        style.height = DesignHeight;

        generateVisualContent += GenerateVisual;
    }

    /// <summary>Health arc active fraction 0..1 (higher = healthier).</summary>
    public void SetHealth(float value01)
    {
        _health01 = Mathf.Clamp01(value01);
        MarkDirtyRepaint();
    }

    /// <summary>Satiety arc active fraction 0..1 (higher = more satiated).
    /// The full inactive track is always drawn so both physical track lengths
    /// stay identical.</summary>
    public void SetSatiety(float value01)
    {
        _satiety01 = Mathf.Clamp01(value01);
        MarkDirtyRepaint();
    }

    public float HealthFraction01 => _health01;
    public float SatietyFraction01 => _satiety01;

    // ====================================================================
    // PASS 3 interaction state (focus / pulse / live mini-animations)
    // ====================================================================

    /// <summary>The module that currently owns focus (None = the focus ring is
    /// deliberately not drawn, which is the map cursor mode).</summary>
    public WristModuleId FocusModule => _focusTarget;

    /// <summary>Smoothed focus strength of one module (0..1).</summary>
    public float FocusStrengthOf(WristModuleId module)
    {
        int index = (int)module;
        return index >= 0 && index < _moduleFocus.Length ? _moduleFocus[index] : 0f;
    }

    /// <summary>Smoothed confirmed-pulse strength of one module (0..1).</summary>
    public float PulseStrengthOf(WristModuleId module)
    {
        int index = (int)module;
        return index >= 0 && index < _modulePulse.Length ? _modulePulse[index] : 0f;
    }

    /// <summary>Focuses one module (or None). The visual transition is smoothed
    /// over <see cref="FocusTransitionSeconds"/> by <see cref="Animate"/>.</summary>
    public void SetFocusModule(WristModuleId module)
    {
        _focusTarget = module;
    }

    /// <summary>Fires the short confirm / state-change flash on one module.</summary>
    public void Pulse(WristModuleId module)
    {
        int index = (int)module;
        if (index > 0 && index < _modulePulse.Length)
        {
            _modulePulse[index] = 1f;
        }
    }

    /// <summary>True while the harvest panel slides its data-flow dashes.</summary>
    public bool HarvestFlowing => _harvestFlowing;

    /// <summary>Harvest / transfer line flow ("轻量线路流动").</summary>
    public void SetHarvestFlowing(bool flowing)
    {
        _harvestFlowing = flowing;
    }

    /// <summary>True while the navigation radar shows a live bearing.</summary>
    public bool RadarActive => _radarActive;

    /// <summary>The bearing the active radar points at (degrees, 0 = right).</summary>
    public float RadarBearingDeg => _radarBearingDeg;

    /// <summary>Navigation radar state: standby sweep or a locked bearing.</summary>
    public void SetRadar(bool active, float bearingDeg)
    {
        _radarActive = active;
        _radarBearingDeg = bearingDeg;
    }

    /// <summary>
    /// Advances the light-weight presentation animations (breathing arcs, radar
    /// sweep, data flow, focus smoothing, pulse decay) and repaints. Called every
    /// frame while the wrist hologram is actually presented; a closed terminal
    /// never pays for it.
    /// </summary>
    public void Animate(float deltaSeconds)
    {
        float dt = Mathf.Max(0f, deltaSeconds);
        _time += dt;

        float focusStep = dt / FocusTransitionSeconds;
        float pulseStep = dt / ConfirmPulseSeconds;

        for (int i = 0; i < _moduleFocus.Length; i++)
        {
            float wanted = (int)_focusTarget == i ? 1f : 0f;
            _moduleFocus[i] = Mathf.MoveTowards(_moduleFocus[i], wanted, focusStep);
            _modulePulse[i] = Mathf.Max(0f, _modulePulse[i] - pulseStep);
        }

        MarkDirtyRepaint();
    }

    /// <summary>Slow 0..1 breathing wave shared by the status arcs and the
    /// 当前状态 panel edge (period ~2.8 s, deliberately not a blink).</summary>
    private float Breath01 => 0.5f + 0.5f * Mathf.Sin(_time * (Mathf.PI * 2f / 2.8f));

    /// <summary>Focus/pulse lift of one module (0..1), pulse saturating.</summary>
    private float LiftOf(WristModuleId module)
    {
        int index = (int)module;
        if (index <= 0 || index >= _moduleFocus.Length)
        {
            return 0f;
        }

        return Mathf.Clamp01(_moduleFocus[index] + _modulePulse[index]);
    }

    private static Color32 ScaleAlpha(Color32 color, float scale)
    {
        color.a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * Mathf.Clamp01(scale)), 0, 255);
        return color;
    }

    private static Color32 Blend(Color32 from, Color32 to, float t)
    {
        t = Mathf.Clamp01(t);
        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Lerp(from.r, to.r, t)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(from.g, to.g, t)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(from.b, to.b, t)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(from.a, to.a, t)));
    }

    // ====================================================================
    // Visual generation
    // ====================================================================

    private void GenerateVisual(MeshGenerationContext ctx)
    {
        // ================================================================
        // 1. LOCAL MAP (translucent frosted disc + boundary + decorations)
        // ================================================================

        float mapLift = LiftOf(WristModuleId.Map);

        DrawDisc(ctx, Center, MapRadius, MapFill, 96);

        // Map outside boundary (map edge, not a functional ring).
        DrawRingSector(ctx, Center, MapRadius - 1.5f, MapRadius,
            0f, 360f, Blend(Hairline, PanelEdgeFocused, mapLift), 96);

        if (!ShipSchematicMode)
        {
            // Surface-map decorations only: distance rings + reference cross.
            // Ship mode leaves the disc clean for the deck schematic.
            DrawRingSector(ctx, Center, 78f, 79f, 0f, 360f, VeryWeakHairline, 64);
            DrawRingSector(ctx, Center, 156f, 157f, 0f, 360f, VeryWeakHairline, 96);

            // Very light dot lattice: the "点阵 / 颗粒" texture layer. One mesh
            // for the whole lattice so the layer costs a single allocation.
            DrawDotLattice(ctx, Center, MapRadius - 8f, 184f, 12f, 1.05f,
                new Color32(184, 226, 235, 104));

            // Very subtle map cross reference.
            DrawLine(ctx,
                new Vector2(Center.x - 175f, Center.y),
                new Vector2(Center.x + 175f, Center.y),
                VeryWeakHairline, 1f);

            DrawLine(ctx,
                new Vector2(Center.x, Center.y - 175f),
                new Vector2(Center.x, Center.y + 175f),
                VeryWeakHairline, 1f);

            // Navigation standby radar / live bearing indicator.
            DrawNavigationRadar(ctx);
        }

        // ================================================================
        // 2 + 3. STATUS TRACKS - ONE shared helper at the SAME inner radius
        // as the functional panels (248).
        // ================================================================

        DrawStatusTrack(ctx, HealthArcStartDeg, _health01, HealthActiveColor, 1f);
        DrawStatusTrack(ctx, SatietyArcStartDeg, _satiety01, SatietyActiveColor, 1f);

        // ================================================================
        // 4. FOUR PARALLEL-CAP ANNULAR PANELS (annulus 248..365 ∩ band).
        // Equipment upper-left, State upper-right, Navigation lower-left,
        // Harvest lower-right. They never form one continuous outer ring.
        // ================================================================

        DrawPanel(ctx, WristModuleId.Equipment, true, UpperPanelTopY, UpperPanelBottomY);
        DrawPanel(ctx, WristModuleId.State, false, UpperPanelTopY, UpperPanelBottomY);
        DrawPanel(ctx, WristModuleId.Navigation, true, LowerPanelTopY, LowerPanelBottomY);
        DrawPanel(ctx, WristModuleId.Harvest, false, LowerPanelTopY, LowerPanelBottomY);
        if (!ShipSchematicMode)
        {
            DrawDrillPictogram(ctx);
        }
    }

    // ====================================================================
    // Status arcs
    // ====================================================================

    /// <summary>One mirrored status track (Health upper / Satiety lower).
    /// Geometry is shared so both tracks can never diverge: 70 deg span,
    /// inner 248 == PanelInnerRadius, outer 262, thickness 14. The inactive
    /// track always covers the FULL span; the active fill only runs to the
    /// current value.
    ///
    /// PASS 3: the Health fill breathes very slightly (amplitude ~8%), so the
    /// top anchor is alive without becoming a blinking bar. The geometry is
    /// byte-identical - only the drawn alpha moves.</summary>
    private void DrawStatusTrack(
        MeshGenerationContext ctx,
        float startDeg,
        float value01,
        Color32 activeColor,
        float intensity)
    {
        const float spanDeg = 70f;
        float endDeg = startDeg + spanDeg;

        // Inactive track: ALWAYS the complete 70 deg span (same color both).
        DrawRingSector(ctx, Center, StatusInnerRadius, StatusOuterRadius,
            startDeg, endDeg, InactiveArc, 40);

        // Active progress fill (health 100% / satiety 82%).
        float activeEnd = Mathf.Lerp(startDeg, endDeg, Mathf.Clamp01(value01));
        float breathe = Mathf.Lerp(0.90f, 1f, Breath01);
        DrawRingSector(ctx, Center, StatusInnerRadius, StatusOuterRadius,
            startDeg, activeEnd,
            ScaleAlpha(activeColor, breathe * intensity), 40);

        // A hairline cap closes each end of the ACTIVE fill, so the player can
        // read where the value stops even at a glance (restrained, 1 px).
        Vector2 capInner = Polar(Center, StatusInnerRadius, activeEnd);
        Vector2 capOuter = Polar(Center, StatusOuterRadius, activeEnd);
        DrawLine(ctx, capInner, capOuter,
            ScaleAlpha(activeColor, 0.85f * breathe), 1.4f);
    }

    // ====================================================================
    // Parallel-Cap Annular Panels
    // ====================================================================

    /// <summary>
    /// One module plate: the annulus between PanelInnerRadius and
    /// PanelOuterRadius intersected with the horizontal band [topY..bottomY] on
    /// the chosen side of the centre (top and bottom edges horizontal and
    /// therefore parallel, left/right edges follow the inner and outer arcs).
    ///
    /// PASS 3 additions, all presentation:
    /// - a cool translucent slate fill (never opaque, never white);
    /// - light cyan hairlines: caps + inner arc in PanelEdge, outer arc brighter;
    /// - a restrained inset rail with corner ticks (少量工业线条);
    /// - the focused module lifts its edges and gets a faint accent wash;
    /// - the harvest module slides data-flow dashes along its rail while a node
    ///   is harvesting / transmitting.
    /// </summary>
    private void DrawPanel(
        MeshGenerationContext ctx,
        WristModuleId module,
        bool leftSide,
        float topY,
        float bottomY)
    {
        const int segments = 48;

        float lift = LiftOf(module);

        // ---- Translucent body fill (triangle mesh rows along Y) ----
        BuildPanelRows(leftSide, topY, bottomY, segments, out float[] innerX, out float[] outerX);

        DrawPanelRows(ctx, innerX, outerX, topY, bottomY, segments, PanelFill);
        if (lift > 0.01f)
        {
            DrawPanelRows(ctx, innerX, outerX, topY, bottomY, segments,
                ScaleAlpha(PanelFillFocused, lift));
        }

        // ---- Edges ----
        // The 当前状态 plate breathes very slightly (contract §5: "边缘轻微呼吸"),
        // so the state module reads as alive without blinking. Every other plate
        // is steady, which is what keeps the breathing from becoming decoration.
        float edgeIntensity = module == WristModuleId.State
            ? Mathf.Lerp(0.88f, 1f, Breath01)
            : 1f;

        Color32 edge = ScaleAlpha(Blend(PanelEdge, PanelEdgeFocused, lift), edgeIntensity);
        Color32 outerEdge = ScaleAlpha(Blend(PanelEdgeOuter, PanelEdgeFocused, lift), edgeIntensity);

        // A focused module's edge is slightly thick.
        float capThickness = Mathf.Lerp(1.5f, 2.2f, lift);
        float arcThickness = Mathf.Lerp(1f, 1.6f, lift);

        DrawLine(ctx,
            new Vector2(innerX[0], topY),
            new Vector2(outerX[0], topY),
            edge, capThickness);

        DrawLine(ctx,
            new Vector2(innerX[segments], bottomY),
            new Vector2(outerX[segments], bottomY),
            edge, capThickness);

        for (int i = 0; i < segments; i++)
        {
            float y0 = Mathf.Lerp(topY, bottomY, i / (float)segments);
            float y1 = Mathf.Lerp(topY, bottomY, (i + 1) / (float)segments);
            DrawLine(ctx,
                new Vector2(innerX[i], y0),
                new Vector2(innerX[i + 1], y1),
                edge, arcThickness);
        }

        for (int i = 0; i < segments; i++)
        {
            float y0 = Mathf.Lerp(topY, bottomY, i / (float)segments);
            float y1 = Mathf.Lerp(topY, bottomY, (i + 1) / (float)segments);
            DrawLine(ctx,
                new Vector2(outerX[i], y0),
                new Vector2(outerX[i + 1], y1),
                outerEdge, arcThickness + 0.2f);
        }

        // ---- Inset industrial rail + corner ticks ----
        Color32 rail = Blend(RailColor, RailColorFocused, lift);
        float railTopY = topY + 5f;
        float railBottomY = bottomY - 5f;
        BuildPanelRows(leftSide, railTopY, railBottomY, 2, out float[] railTopInner, out float[] railTopOuter);
        float railInnerTop = railTopInner[0] + 6f;
        float railOuterTop = railTopOuter[0] - 6f;
        float railInnerBottom = railTopInner[2] + 6f;
        float railOuterBottom = railTopOuter[2] - 6f;

        if (railOuterTop > railInnerTop)
        {
            DrawLine(ctx,
                new Vector2(railInnerTop, railTopY),
                new Vector2(railOuterTop, railTopY),
                rail, 1f);

            // Corner ticks at the rail's outer end (top and bottom).
            DrawLine(ctx,
                new Vector2(railOuterTop, railTopY),
                new Vector2(railOuterTop, railTopY + 6f),
                rail, 1f);

            if (railOuterBottom > railInnerBottom)
            {
                DrawLine(ctx,
                    new Vector2(railInnerBottom, railBottomY),
                    new Vector2(railOuterBottom, railBottomY),
                    rail, 1f);

                DrawLine(ctx,
                    new Vector2(railOuterBottom, railBottomY),
                    new Vector2(railOuterBottom, railBottomY - 6f),
                    rail, 1f);
            }
        }

        // ---- Harvest / transfer data flow along the TOP rail ----
        if (module == WristModuleId.Harvest && _harvestFlowing && railOuterTop > railInnerTop)
        {
            DrawFlowDashes(ctx, railInnerTop, railOuterTop, railTopY);
        }

        // Short structural bridge closes the 13 px clearance between the map
        // glass and each wing. It stays outside the map content.
        float bridgeY = (topY + bottomY) * 0.5f;
        float dy = bridgeY - Center.y;
        float mapX = Mathf.Sqrt(MapRadius * MapRadius - dy * dy);
        float panelX = Mathf.Sqrt(PanelInnerRadius * PanelInnerRadius - dy * dy);
        float direction = leftSide ? -1f : 1f;
        DrawLine(ctx, new Vector2(Center.x + direction * mapX, bridgeY),
            new Vector2(Center.x + direction * (panelX + 10f), bridgeY),
            outerEdge, 2f);
        DrawDisc(ctx, new Vector2(Center.x + direction * (panelX + 10f), bridgeY),
            2.2f, outerEdge, 10);

        // ---- Navigation standby residue is drawn by DrawNavigationRadar ----
    }

    /// <summary>Pre-computes the inner/outer row points of one parallel-cap
    /// panel (shared by the fill and every edge so they can never drift).</summary>
    private static void BuildPanelRows(
        bool leftSide,
        float topY,
        float bottomY,
        int segments,
        out float[] innerX,
        out float[] outerX)
    {
        segments = Mathf.Clamp(segments, 1, 128);
        innerX = new float[segments + 1];
        outerX = new float[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float y = Mathf.Lerp(topY, bottomY, t);
            float dy = y - Center.y;

            float innerDx = Mathf.Sqrt(Mathf.Max(0f,
                PanelInnerRadius * PanelInnerRadius - dy * dy));
            float outerDx = Mathf.Sqrt(Mathf.Max(0f,
                PanelOuterRadius * PanelOuterRadius - dy * dy));

            if (leftSide)
            {
                innerX[i] = Center.x - innerDx;
                outerX[i] = Center.x - outerDx;
            }
            else
            {
                innerX[i] = Center.x + innerDx;
                outerX[i] = Center.x + outerDx;
            }
        }
    }

    /// <summary>Fills one pre-computed panel band with a flat translucent color.</summary>
    private static void DrawPanelRows(
        MeshGenerationContext ctx,
        float[] innerX,
        float[] outerX,
        float topY,
        float bottomY,
        int segments,
        Color32 fill)
    {
        int count = innerX.Length - 1;
        int vertexCount = (count + 1) * 2;
        int indexCount = count * 6;
        MeshWriteData mesh = ctx.Allocate(vertexCount, indexCount);

        for (int i = 0; i <= count; i++)
        {
            float y = Mathf.Lerp(topY, bottomY, i / (float)count);
            mesh.SetNextVertex(MakeVertex(new Vector2(innerX[i], y), fill));
            mesh.SetNextVertex(MakeVertex(new Vector2(outerX[i], y), fill));
        }

        for (int i = 0; i < count; i++)
        {
            ushort i0 = (ushort)(i * 2);
            ushort o0 = (ushort)(i * 2 + 1);
            ushort i1 = (ushort)((i + 1) * 2);
            ushort o1 = (ushort)((i + 1) * 2 + 1);

            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o0);
            mesh.SetNextIndex(o1);

            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o1);
            mesh.SetNextIndex(i1);
        }
    }

    /// <summary>Thin dashes sliding outward along the harvest panel's top rail:
    /// data in transit, four short segments, no particles.</summary>
    private void DrawFlowDashes(
        MeshGenerationContext ctx,
        float startX,
        float endX,
        float y)
    {
        const int dashCount = 4;
        const float dashLength = 14f;
        float span = endX - startX;
        if (span <= dashLength)
        {
            return;
        }

        for (int i = 0; i < dashCount; i++)
        {
            float t = Mathf.Repeat(_time * 0.34f + i / (float)dashCount, 1f);
            float x0 = startX + span * t;
            float x1 = Mathf.Min(x0 + dashLength, endX);
            if (x1 <= x0 + 1f)
            {
                continue;
            }

            // Fade in/out at the rail ends so the dashes never pop.
            float edge = Mathf.Min(t, 1f - t);
            float alpha = Mathf.Clamp01(edge * 4f);
            DrawLine(ctx, new Vector2(x0, y), new Vector2(x1, y),
                ScaleAlpha(FlowColor, alpha), 2f);
        }
    }

    // ====================================================================
    // Navigation radar (lower-left panel, below the 当前导航 rows)
    // ====================================================================

    /// <summary>
    /// Standby: a dim ring with one rotating 70 deg sweep segment (低强度扫描).
    /// Active: the ring locks to the live bearing - a line from the centre
    /// toward the navigation target's direction on the map plus an end dot.
    /// The "?" glyph that belongs to the standby state is a UXML label (NavRadarGlyph)
    /// so the symbol stays crisp text, never a mesh approximation.
    /// </summary>
    private void DrawDrillPictogram(MeshGenerationContext ctx)
    {
        // A small line-art extraction rig, anchored below the harvest readout.
        // The animated rail above it carries the existing collection state.
        var ink = new Color32(139, 211, 225, 166);
        float x = 865f;
        float y = 565f;
        DrawLine(ctx, new Vector2(x - 5f, y - 22f), new Vector2(x + 5f, y - 22f), ink, 1.2f);
        DrawLine(ctx, new Vector2(x, y - 22f), new Vector2(x, y - 16f), ink, 1.2f);
        DrawLine(ctx, new Vector2(x - 7f, y - 16f), new Vector2(x + 7f, y - 16f), ink, 1.4f);
        DrawLine(ctx, new Vector2(x - 7f, y - 16f), new Vector2(x - 7f, y + 1f), ink, 1.4f);
        DrawLine(ctx, new Vector2(x + 7f, y - 16f), new Vector2(x + 7f, y + 1f), ink, 1.4f);
        DrawLine(ctx, new Vector2(x - 7f, y + 1f), new Vector2(x + 7f, y + 1f), ink, 1.4f);
        DrawLine(ctx, new Vector2(x - 7f, y + 1f), new Vector2(x - 17f, y + 19f), ink, 1.4f);
        DrawLine(ctx, new Vector2(x + 7f, y + 1f), new Vector2(x + 17f, y + 19f), ink, 1.4f);
        DrawLine(ctx, new Vector2(x - 19f, y + 19f), new Vector2(x + 19f, y + 19f), ink, 1.4f);
        DrawLine(ctx, new Vector2(x, y + 1f), new Vector2(x, y + 17f), ink, 1.2f);
        DrawLine(ctx, new Vector2(x - 3f, y + 9f), new Vector2(x + 3f, y + 9f), ink, 1f);
    }

    private void DrawNavigationRadar(MeshGenerationContext ctx)
    {
        Color32 ring = _radarActive ? RadarActiveColor : RadarIdle;
        // Three fine, broken instrument rings read as a radar instead of a
        // lone question mark. The outer radius stays within the panel slot.
        DrawRingSector(ctx, RadarCenter, RadarRadius - 0.8f, RadarRadius,
            12f, 172f, ring, 24);
        DrawRingSector(ctx, RadarCenter, RadarRadius - 0.8f, RadarRadius,
            190f, 350f, ring, 24);
        DrawRingSector(ctx, RadarCenter, 11.5f, 12.2f,
            0f, 360f, ScaleAlpha(ring, 0.72f), 40);
        DrawRingSector(ctx, RadarCenter, 6.8f, 7.4f,
            30f, 330f, ScaleAlpha(ring, 0.46f), 30);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            DrawLine(ctx, Polar(RadarCenter, 15.2f, angle),
                Polar(RadarCenter, 17f, angle), ring, 1f);
        }

        if (_radarActive)
        {
            // Live bearing: points where the target sits on the map.
            Vector2 tip = Polar(RadarCenter, RadarRadius - 4f, _radarBearingDeg);
            DrawLine(ctx, RadarCenter, tip, RadarActiveColor, 1.6f);
            DrawDisc(ctx, tip, 2.4f, RadarActiveColor, 12);
            DrawDisc(ctx, RadarCenter, 1.8f, RadarActiveColor, 10);
            return;
        }

        // A single slow radial sweep and one echo dot keep standby alive.
        float sweep = Mathf.Repeat(_time * 32f, 360f);
        DrawLine(ctx, RadarCenter, Polar(RadarCenter, 14f, sweep),
            ScaleAlpha(RadarActiveColor, 0.48f), 1f);
        DrawDisc(ctx, Polar(RadarCenter, 12f, sweep - 35f), 1.1f,
            ScaleAlpha(RadarActiveColor, 0.7f), 8);
    }

    // ====================================================================
    // Coordinate helpers
    // ====================================================================

    /// <summary>Polar to pixel (UI Toolkit Y grows downward, so sin is
    /// inverted). 0 deg = right, 90 = up, 180 = left, 270 = down.</summary>
    public static Vector2 Polar(
        Vector2 center,
        float radius,
        float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;

        return new Vector2(
            center.x + Mathf.Cos(rad) * radius,
            center.y - Mathf.Sin(rad) * radius);
    }

    /// <summary>Wrist-pixel angle around the design centre in conventional
    /// +y-up degrees (0 = right, 90 = up). Used by geometry validation and by
    /// the navigation radar bearing.</summary>
    public static float WristAngleDeg(Vector2 pixel)
    {
        Vector2 d = pixel - Center;

        // Restore conventional +Y-up for atan.
        float angle = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
        if (angle < 0f)
        {
            angle += 360f;
        }

        return angle;
    }

    // ====================================================================
    // Round 9.2 authoritative geometry helpers (shared by text layout,
    // panel-cap validation and text-slot/staircase validation)
    // ====================================================================

    /// <summary>Horizontal text range of a parallel-cap panel cross-section at
    /// scanline y. This is the AUTHORITATIVE slot bound for every text row:
    /// text rows must lie between minX and maxX with at least 'padding' from
    /// the panel's real inner/outer circle edges. Returns false when the
    /// panel does not exist at that y.</summary>
    public static bool GetPanelHorizontalRange(
        bool leftSide,
        float y,
        float innerRadius,
        float outerRadius,
        float padding,
        out float minX,
        out float maxX)
    {
        minX = 0f;
        maxX = 0f;

        float dy = y - Center.y;

        float innerSq = innerRadius * innerRadius - dy * dy;
        float outerSq = outerRadius * outerRadius - dy * dy;
        if (outerSq <= 0f)
        {
            return false;
        }

        float innerDx = Mathf.Sqrt(Mathf.Max(0f, innerSq));
        float outerDx = Mathf.Sqrt(outerSq);

        if (leftSide)
        {
            minX = Center.x - outerDx + padding;
            maxX = Center.x - innerDx - padding;
        }
        else
        {
            minX = Center.x + innerDx + padding;
            maxX = Center.x + outerDx - padding;
        }

        return maxX > minX;
    }

    /// <summary>Row centres of one parallel panel across a scanline y: the
    /// middle of the real panel cross-section (after padding). Used by the
    /// staircase layout so the stairs emerge from geometry, never from
    /// hand-added indentation offsets.</summary>
    public static bool TryGetPanelRowCentre(
        bool leftSide,
        float y,
        float padding,
        out float centreX,
        out float minX,
        out float maxX)
    {
        if (!GetPanelHorizontalRange(leftSide, y,
                PanelInnerRadius, PanelOuterRadius, padding,
                out minX, out maxX))
        {
            centreX = 0f;
            return false;
        }

        centreX = (minX + maxX) * 0.5f;
        return true;
    }

    /// <summary>True when the row centre Y falls inside one of the two panel
    /// bands (upper 205..345 or lower 455..595).</summary>
    public static bool YInsidePanelBand(float y)
    {
        return (y >= UpperPanelTopY && y <= UpperPanelBottomY)
               || (y >= LowerPanelTopY && y <= LowerPanelBottomY);
    }

    /// <summary>The un-padded panel cross-section endpoints at scanline y:
    /// innerX (closest to the centre, radius PanelInnerRadius) and outerX
    /// (farthest, radius PanelOuterRadius) on the given side. This mirrors the
    /// exact math the parallel-cap mesh rows use, so validation and drawing
    /// can never drift apart. Returns false when the outer circle does not
    /// reach that y.</summary>
    public static bool GetPanelCrossSection(
        bool leftSide,
        float y,
        out float innerX,
        out float outerX)
    {
        innerX = 0f;
        outerX = 0f;

        float dy = y - Center.y;
        float outerSq = PanelOuterRadius * PanelOuterRadius - dy * dy;
        if (outerSq <= 0f)
        {
            return false;
        }

        float innerDx = Mathf.Sqrt(Mathf.Max(0f,
            PanelInnerRadius * PanelInnerRadius - dy * dy));
        float outerDx = Mathf.Sqrt(outerSq);

        if (leftSide)
        {
            innerX = Center.x - innerDx;
            outerX = Center.x - outerDx;
        }
        else
        {
            innerX = Center.x + innerDx;
            outerX = Center.x + outerDx;
        }

        return true;
    }

    /// <summary>Top and bottom cap endpoints of one parallel-cap panel
    /// (annulus 248..365 x band [topY..bottomY] on one side). The top cap runs
    /// horizontally from (innerX(topY), topY) to (outerX(topY), topY), the
    /// bottom cap at bottomY. Both caps share the exact same Y at both ends,
    /// so top and bottom edges are horizontal and therefore parallel.</summary>
    public static bool GetPanelCapEndpoints(
        bool leftSide,
        float topY,
        float bottomY,
        out Vector2 topInner,
        out Vector2 topOuter,
        out Vector2 bottomInner,
        out Vector2 bottomOuter)
    {
        topInner = Vector2.zero;
        topOuter = Vector2.zero;
        bottomInner = Vector2.zero;
        bottomOuter = Vector2.zero;

        if (!GetPanelCrossSection(leftSide, topY, out float topInX, out float topOutX))
        {
            return false;
        }

        if (!GetPanelCrossSection(leftSide, bottomY, out float bottomInX, out float bottomOutX))
        {
            return false;
        }

        topInner = new Vector2(topInX, topY);
        topOuter = new Vector2(topOutX, topY);
        bottomInner = new Vector2(bottomInX, bottomY);
        bottomOuter = new Vector2(bottomOutX, bottomY);
        return true;
    }

    // ====================================================================
    // Disc / lattice meshes
    // ====================================================================

    private static void DrawDisc(
        MeshGenerationContext ctx,
        Vector2 center,
        float radius,
        Color32 color,
        int segments)
    {
        segments = Mathf.Clamp(segments, 8, 180);

        int vertexCount = segments + 1;
        int indexCount = segments * 3;

        MeshWriteData mesh = ctx.Allocate(vertexCount, indexCount);

        mesh.SetNextVertex(MakeVertex(center, color));

        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;

            mesh.SetNextVertex(
                MakeVertex(Polar(center, radius, angle), color));
        }

        for (int i = 0; i < segments; i++)
        {
            ushort centreIndex = 0;
            ushort current = (ushort)(i + 1);
            ushort next = (ushort)(((i + 1) % segments) + 1);

            mesh.SetNextIndex(centreIndex);
            mesh.SetNextIndex(current);
            mesh.SetNextIndex(next);
        }
    }

    /// <summary>
    /// Very light procedural dot lattice clipped to a disc ("非常轻的点阵").
    /// The whole lattice is ONE mesh allocation, so adding a texture layer to
    /// the map costs a single draw range instead of one per dot.
    /// </summary>
    private static void DrawDotLattice(
        MeshGenerationContext ctx,
        Vector2 center,
        float radius,
        float innerRadius,
        float spacing,
        float dotRadius,
        Color32 color)
    {
        if (spacing <= 0f || radius <= 0f)
        {
            return;
        }

        int steps = Mathf.CeilToInt(radius / spacing);
        var centres = new System.Collections.Generic.List<Vector2>(steps * steps * 2);
        float limit = radius - dotRadius;
        for (int ix = -steps; ix <= steps; ix++)
        {
            for (int iy = -steps; iy <= steps; iy++)
            {
                var candidate = new Vector2(center.x + ix * spacing,
                    center.y + iy * spacing);
                float distanceSq = (candidate - center).sqrMagnitude;
                if (distanceSq <= limit * limit && distanceSq >= innerRadius * innerRadius)
                {
                    centres.Add(candidate);
                }
            }
        }

        if (centres.Count == 0)
        {
            return;
        }

        const int corners = 4;
        MeshWriteData mesh = ctx.Allocate(centres.Count * corners, centres.Count * 6);

        for (int i = 0; i < centres.Count; i++)
        {
            Vector2 c = centres[i];
            mesh.SetNextVertex(MakeVertex(new Vector2(c.x - dotRadius, c.y - dotRadius), color));
            mesh.SetNextVertex(MakeVertex(new Vector2(c.x + dotRadius, c.y - dotRadius), color));
            mesh.SetNextVertex(MakeVertex(new Vector2(c.x + dotRadius, c.y + dotRadius), color));
            mesh.SetNextVertex(MakeVertex(new Vector2(c.x - dotRadius, c.y + dotRadius), color));
        }

        for (int i = 0; i < centres.Count; i++)
        {
            ushort v0 = (ushort)(i * corners);
            mesh.SetNextIndex(v0);
            mesh.SetNextIndex((ushort)(v0 + 1));
            mesh.SetNextIndex((ushort)(v0 + 2));

            mesh.SetNextIndex(v0);
            mesh.SetNextIndex((ushort)(v0 + 2));
            mesh.SetNextIndex((ushort)(v0 + 3));
        }
    }

    // ====================================================================
    // Annular / ring-sector mesh
    // ====================================================================

    private static void DrawRingSector(
        MeshGenerationContext ctx,
        Vector2 center,
        float innerRadius,
        float outerRadius,
        float startDeg,
        float endDeg,
        Color32 color,
        int segments)
    {
        if (outerRadius <= innerRadius)
        {
            return;
        }

        segments = Mathf.Clamp(segments, 1, 180);

        float span = endDeg - startDeg;

        int vertexCount = (segments + 1) * 2;
        int indexCount = segments * 6;

        MeshWriteData mesh = ctx.Allocate(vertexCount, indexCount);

        // Vertices:
        //
        // 0 inner0
        // 1 outer0
        // 2 inner1
        // 3 outer1
        // ...

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = startDeg + span * t;

            Vector2 innerPos = Polar(center, innerRadius, angle);
            Vector2 outerPos = Polar(center, outerRadius, angle);

            mesh.SetNextVertex(MakeVertex(innerPos, color));
            mesh.SetNextVertex(MakeVertex(outerPos, color));
        }

        for (int i = 0; i < segments; i++)
        {
            ushort i0 = (ushort)(i * 2);
            ushort o0 = (ushort)(i * 2 + 1);
            ushort i1 = (ushort)((i + 1) * 2);
            ushort o1 = (ushort)((i + 1) * 2 + 1);

            // Triangle 1
            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o0);
            mesh.SetNextIndex(o1);

            // Triangle 2
            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o1);
            mesh.SetNextIndex(i1);
        }
    }

    // ====================================================================
    // Simple line quad
    // ====================================================================

    private static void DrawLine(
        MeshGenerationContext ctx,
        Vector2 start,
        Vector2 end,
        Color32 color,
        float thickness)
    {
        Vector2 delta = end - start;

        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector2 normal = new Vector2(-delta.y, delta.x).normalized;
        Vector2 offset = normal * (thickness * 0.5f);

        Vector2 v0 = start - offset;
        Vector2 v1 = start + offset;
        Vector2 v2 = end + offset;
        Vector2 v3 = end - offset;

        MeshWriteData mesh = ctx.Allocate(4, 6);

        mesh.SetNextVertex(MakeVertex(v0, color));
        mesh.SetNextVertex(MakeVertex(v1, color));
        mesh.SetNextVertex(MakeVertex(v2, color));
        mesh.SetNextVertex(MakeVertex(v3, color));

        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);

        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    // ====================================================================
    // Vertex helper
    // ====================================================================

    private static Vertex MakeVertex(
        Vector2 position,
        Color32 tint)
    {
        Vertex vertex = new Vertex();

        vertex.position = new Vector3(
            position.x,
            position.y,
            Vertex.nearZ);

        vertex.tint = tint;
        vertex.uv = Vector2.zero;

        return vertex;
    }
}
