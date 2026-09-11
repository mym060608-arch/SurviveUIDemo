using UnityEngine;

/// <summary>
/// Physical Airlock equipment locker (Phase 07B.2).
///
/// An IFirstPersonInteractable on a wall cabinet in the Airlock room offering
/// the prompt 调整远征装备. Pressing E opens the SAME shared loadout view that
/// the deployment confirmation reaches via 调整装备 - both entry points edit
/// exactly one GameSession loadout authority (no duplicate state).
/// Visual geometry is placeholder-only by contract: replace the cabinet
/// model later without touching this gameplay component.
/// </summary>
public class EquipmentLockerInteractable : MonoBehaviour, IFirstPersonInteractable
{
    /// <summary>Chinese-first equipment-locker prompt.</summary>
    public string PromptText => InputDeviceTracker.FormatPrompt("调整远征装备");

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
        if (overlay == null)
        {
            Debug.LogWarning("[EquipmentLockerInteractable] no ShipOverlayUi "
                             + "in scene - locker ignored E");
            return;
        }

        if (!overlay.OpenLoadoutFromLocker())
        {
            Debug.Log("[EquipmentLockerInteractable] open rejected "
                      + "(another mode active)");
        }
    }
}
