using UnityEngine;

/// <summary>
/// Physical airlock / EVA deployment station (Phase 07A.1 / 07B.2).
///
/// An IFirstPersonInteractable on the aft airlock bulkhead offering the
/// Chinese-first prompt 前往 NEREID-04 地表.
///
/// Phase 07B.2: pressing E no longer deploys immediately. It opens the
/// Deployment Confirmation overlay first; the real ShipInterior -> Surface
/// routing runs only when the player confirms (确认降临). GameSession stays the
/// single authoritative persisted state across the transition.
/// </summary>
public class ShipAirlockInteractable : MonoBehaviour, IFirstPersonInteractable
{
    /// <summary>Chinese-first deployment prompt (surface scene name stays English).</summary>
    public string PromptText => InputDeviceTracker.FormatPrompt("前往 NEREID-04 地表");

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
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        if (overlay != null && overlay.OpenDeploymentConfirm())
        {
            return;
        }

        // Safety fallback (overlay missing on legacy scenes): keep the old
        // direct routing so an unconfigured scene never soft-locks the player.
        Debug.LogWarning("[ShipAirlockInteractable] no overlay available - "
                         + "falling back to direct surface routing");
        SceneRouting.EnterSurface();
    }
}
