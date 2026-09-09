using UnityEngine;

/// <summary>
/// Physical airlock / EVA deployment station placeholder (Phase 07 Checkpoint A).
///
/// An IFirstPersonInteractable on the aft airlock bulkhead offering the
/// Chinese-first prompt 前往 NEREID-04 地表. In 07A it is a placeholder only:
/// pressing E records a notice. The real airlock -&gt; surface scene transition
/// is checkpoint 07C scope and uses the existing mission/loadout authority
/// (GameSession) without duplicating preparation rules.
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
        // Phase 07A contract: airlock is an interactable placeholder. The
        // real airlock -> surface transition arrives in checkpoint 07C.
        Debug.Log("[ShipAirlockInteractable] deployment placeholder - "
                  + "airlock -> NEREID-04 transition arrives in 07C");
    }
}
