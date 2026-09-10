using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Single CJK font policy for every runtime UI document (Phase 04 policy,
/// extracted in Phase 08 so the Hub and the Main Menu share ONE implementation
/// instead of duplicating the candidate list).
///
/// The font is referenced from the OS at runtime (never copied into the
/// project, never redistributed), so Editor, PlayMode, Standalone and the clean
/// screenshots all render identical glyphs on this machine.
/// See Docs/UI_LANGUAGE_STYLE.md §32 for the temporary font strategy.
/// </summary>
public static class UiFontProvider
{
    private static readonly string[] Candidates =
    {
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "SimHei",
        "Noto Sans CJK SC",
        "Source Han Sans SC"
    };

    /// <summary>
    /// Creates a dynamic OS CJK font for one UI document.
    ///
    /// A dynamic font is a runtime object with its own glyph atlas. Reusing a
    /// single instance across two different panels is not safe in a player build
    /// (the text engine can hang while measuring against a font created for a
    /// panel that has already been destroyed), so every document gets its own.
    /// The font is referenced from the OS at runtime - no font file is copied
    /// into the project or redistributed.
    /// </summary>
    public static Font CreateCjkFont()
    {
        Font font = Font.CreateDynamicFontFromOSFont(Candidates, 16);
        if (font != null)
        {
            font.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }

        return font;
    }

    /// <summary>Applies a dedicated CJK font to every text element in the tree.</summary>
    public static void ApplyTo(VisualElement root)
    {
        if (root == null)
        {
            return;
        }

        Font font = CreateCjkFont();
        if (font == null)
        {
            return;
        }

        foreach (TextElement text in root.Query<TextElement>().ToList())
        {
            text.style.unityFont = font;
        }
    }
}
