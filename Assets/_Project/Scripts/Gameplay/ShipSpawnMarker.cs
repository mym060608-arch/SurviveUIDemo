using UnityEngine;

/// <summary>
/// Deterministic ship spawn marker (Phase 07 Checkpoint A).
///
/// The scene builder authors two markers - ShipSpawn_Default and
/// ShipSpawn_FromSurface - and ShipGameController applies the chosen one to
/// the first-person player at scene entry. Type lookup (not string find)
/// keeps the wiring deterministic and testable.
/// </summary>
public class ShipSpawnMarker : MonoBehaviour
{
    /// <summary>Marker identity: "Default" or "FromSurface".</summary>
    [SerializeField]
    private string _markerId = "Default";

    public const string DefaultId = "Default";
    public const string FromSurfaceId = "FromSurface";

    public string MarkerId => _markerId;

    public void Configure(string markerId)
    {
        _markerId = markerId;
    }
}
