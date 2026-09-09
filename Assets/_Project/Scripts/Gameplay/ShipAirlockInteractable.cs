using UnityEngine;

/// <summary>
/// Physical airlock / EVA deployment station (Phase 07A.1).
///
/// An IFirstPersonInteractable on the aft airlock bulkhead offering the
/// Chinese-first prompt 前往 NEREID-04 地表. Pressing E routes through the
/// single SceneRouting helper into the existing NEREID-04 surface scene and
/// its authoritative GameSession state - no second expedition/loadout state
/// is created. The surface scene itself does not gate on the loadout
/// confirmation flag (confirmation only gates the legacy Hub CTA ceremony;
/// physical confirmation arrives with the interactive consoles in 07B).
/// </summary>
public class ShipAirlockInteractable : MonoBehaviour, IFirstPersonInteractable
{
    /// <summary>Chinese-first deployment prompt (surface scene name stays English).</summary>
    public string PromptText => "E  前往 NEREID-04 地表";

    public bool CanInteract()
    {
        return true;
    }

    public string GetInteractionPrompt()
    {
        return PromptText;
    }

    public void Interact()
    {
        SceneRouting.EnterSurface();
    }
}
