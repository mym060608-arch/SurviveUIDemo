/// <summary>
/// Minimal first-person world-interaction contract (Phase 06 Revision).
///
/// Deliberately small so future world objects (ship doors, physical
/// consoles, wrist triggers) can implement it without a general gameplay
/// framework. Semantics: CanInteract decides whether the interactor offers
/// the action, GetInteractionPrompt supplies player-facing copy, Interact
/// performs the world action.
/// </summary>
public interface IFirstPersonInteractable
{
    /// <summary>True when the object currently accepts an interaction.</summary>
    bool CanInteract();

    /// <summary>Short player-facing prompt (Chinese-first), e.g. E 部署采集器.</summary>
    string GetInteractionPrompt();

    /// <summary>Perform the interaction (authoritative world change).</summary>
    void Interact();
}
