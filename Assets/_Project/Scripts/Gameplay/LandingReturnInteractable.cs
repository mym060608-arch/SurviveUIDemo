using UnityEngine;

/// <summary>
/// Landing / return-zone world interaction (Phase 06 Revision round 2,
/// Phase 07A.1 routing).
///
/// The landing pad is a normal IFirstPersonInteractable: looking at the pad
/// area and pressing E calls the existing surface return path, which now
/// routes into the walkable ship interior (FromSurface spawn) instead of the
/// legacy 2D Hub. R remains a secondary shortcut. Standing anywhere at the
/// pad never creates a dead end: return is always offered.
/// </summary>
public class LandingReturnInteractable : MonoBehaviour, IFirstPersonInteractable
{
    /// <summary>Horizontal distance from the pad centre within which the
    /// return action is offered (the interactor ray still decides gaze).</summary>
    public float activationRadius = 2.2f;

    public bool CanInteract()
    {
        Vector3 playerPos = PlayerFeet();
        if (playerPos == Vector3.zero)
        {
            return false;
        }

        Vector3 toPlayer = playerPos - transform.position;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude <= activationRadius * activationRadius;
    }

    public string GetInteractionPrompt()
    {
        return "E  返回舰船";
    }

    public void Interact()
    {
        SurfaceGameController controller =
            Object.FindAnyObjectByType<SurfaceGameController>();
        if (controller != null)
        {
            controller.ReturnToShip();
        }
    }

    private static Vector3 PlayerFeet()
    {
        SurfacePlayerController player =
            Object.FindAnyObjectByType<SurfacePlayerController>();
        if (player == null)
        {
            return Vector3.zero;
        }

        Vector3 pos = player.transform.position;
        pos.y = 0f;
        return pos;
    }
}
