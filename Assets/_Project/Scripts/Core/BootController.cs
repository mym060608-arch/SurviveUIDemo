using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Minimal 00_Boot driver.
///
/// 1. ensures the authoritative GameSession exists (defaults are applied on a
///    fresh launch),
/// 2. loads 01_Hub.
///
/// 01_Hub never creates a second GameSession (AppController reuses Ensure()).
/// </summary>
public class BootController : MonoBehaviour
{
    public const string HubSceneName = "01_Hub";

    private void Awake()
    {
        // Session Awake applies defaults on first creation.
        GameSession.Ensure();
    }

    private void Start()
    {
        SceneManager.LoadScene(HubSceneName);
    }
}
