using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07 Checkpoint A Play Mode validation - walkable ARK-07 ship shell.
///
/// Reuses the exact first-person stack Phase 06 validated (SurfacePlayer-
/// Controller / FirstPersonInteractor / IFirstPersonInteractable). The suite
/// covers:
/// - structural scene contract (registries, spawn markers, first-person rig,
///   HUD document, no authored GameSession),
/// - real movement through every compartment doorway (semantic WASD via the
///   public MoveByInput API, exactly like the surface tests drive it),
/// - gaze prompts on the four physical consoles and the airlock,
/// - the sliding door state machine through the real E interaction path and
///   its trap-safety rules.
/// </summary>
public class SurvivePhase07Tests
{
    private const int MaxWaitFrames = 1800;

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    /// <summary>Real-time bounded wait (the editor test player runs thousands
    /// of frames per second, so door animation is best waited on wall time).</summary>
    private static IEnumerator WaitUntilState(ShipDoorInteractable door,
        ShipDoorInteractable.ShipDoorState target, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (door.State != target && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static string ActiveSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    /// <summary>Loads the ship scene by name and waits for the active scene.</summary>
    private static IEnumerator LoadShipScene()
    {
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.ShipInteriorSceneName);
    }

    private static IEnumerator WaitForController(System.Action<ShipGameController> onReady)
    {
        ShipGameController controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            return controller != null && controller.IsReady;
        });
        onReady(controller);
    }

    private static void Walk(SurfacePlayerController player, Vector3 start, float yawDeg, int steps)
    {
        player.TeleportTo(start);
        player.SetFacing(yawDeg, 0f);
        for (int i = 0; i < steps; i++)
        {
            player.MoveByInput(Vector2.up, 0.02f);
        }
    }

    [UnityTest]
    public IEnumerator ShipInterior_StructureAndRegistries()
    {
        GameSession.Ensure().ResetSession();

        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForController(value => controller = value);
        Assert.IsNotNull(controller, "ShipGameController ready");
        yield return null;

        // Four physical console placeholders with the four production ids.
        Assert.AreEqual(4, controller.Consoles.Count, "four consoles");
        for (int i = 0; i < controller.Consoles.Count; i++)
        {
            Assert.AreEqual(i + 1, controller.Consoles[i].ConsoleId, "console id order");
        }

        ShipConsoleInteractable c1 = controller.Consoles[0];
        Assert.AreEqual("总览", c1.ChineseName, "console 01 identity");
        StringAssert.Contains("总览终端", c1.PromptText, "console 01 prompt");
        StringAssert.Contains("使用", c1.PromptText, "console prompt verb");

        // Three sliding doors with unique ids.
        Assert.AreEqual(3, controller.Doors.Count, "three doors");
        Assert.AreEqual(1, controller.Doors[0].DoorId, "door 1");
        Assert.AreEqual(2, controller.Doors[1].DoorId, "door 2");
        Assert.AreEqual(3, controller.Doors[2].DoorId, "door 3");

        // Airlock station exists with the Chinese-first deployment prompt.
        Assert.IsNotNull(controller.Airlock, "airlock interactable exists");
        StringAssert.Contains("前往 NEREID-04", controller.Airlock.PromptText, "airlock prompt");
        StringAssert.Contains("E", controller.Airlock.PromptText, "airlock prompt key");

        // Spawn markers are deterministic and present.
        ShipSpawnMarker defaultMarker = null;
        ShipSpawnMarker surfaceMarker = null;
        foreach (ShipSpawnMarker marker in Object.FindObjectsByType<ShipSpawnMarker>())
        {
            if (marker.MarkerId == ShipSpawnMarker.DefaultId)
            {
                defaultMarker = marker;
            }
            else if (marker.MarkerId == ShipSpawnMarker.FromSurfaceId)
            {
                surfaceMarker = marker;
            }
        }

        Assert.IsNotNull(defaultMarker, "ShipSpawn_Default exists");
        Assert.IsNotNull(surfaceMarker, "ShipSpawn_FromSurface exists");

        // Spawn applied: player stands on the default marker.
        SurfacePlayerController player = controller.Player;
        Assert.IsNotNull(player, "player exists");
        AssertNearly(defaultMarker.transform.position.x, player.transform.position.x, "spawn x");
        AssertNearly(defaultMarker.transform.position.z, player.transform.position.z, "spawn z");

        // First-person rig: CharacterController + child eye camera at 1.65.
        Assert.IsNotNull(player.Controller, "CharacterController present");
        Camera eye = player.EyeCamera;
        Assert.IsNotNull(eye, "eye camera present");
        Assert.IsTrue(eye.transform.localPosition.y > 1.4f
                      && eye.transform.localPosition.y < 1.9f, "eye height ~1.65");
        Assert.AreEqual("MainCamera", eye.tag, "eye camera tagged MainCamera");

        // No authored GameSession in the ship scene (session is persistent).
        Scene active = SceneManager.GetActiveScene();
        int authoredSessions = 0;
        foreach (GameObject root in active.GetRootGameObjects())
        {
            authoredSessions += root.GetComponentsInChildren<GameSession>(true).Length;
        }

        Assert.AreEqual(0, authoredSessions, "ship scene authors no GameSession");

        // HUD document exists, bound to ShipHUD, with the bound elements.
        UIDocument doc = null;
        foreach (UIDocument candidate in Object.FindObjectsByType<UIDocument>())
        {
            if (candidate.visualTreeAsset != null
                && candidate.visualTreeAsset.name == "ShipHUD")
            {
                doc = candidate;
            }
        }

        Assert.IsNotNull(doc, "ShipHUD UIDocument exists");
        VisualElement hudRoot = doc.rootVisualElement;
        Assert.IsNotNull(hudRoot, "HUD root ready");
        Assert.IsNotNull(hudRoot.Q<Label>("ShipPrompt"), "ShipPrompt element");
        Assert.IsNotNull(hudRoot.Q<Label>("ShipZoneName"), "ShipZoneName element");
        Assert.IsNotNull(hudRoot.Q<VisualElement>("ShipCrosshairDot"), "crosshair dot element");

        // ShipExplore cursor semantics are player-runtime validated (the
        // editor Test Runner owns the OS cursor, so lock state is not asserted
        // here; the standalone screenshot player verifies it instead).

        // Zone resolver agrees with the authored layout.
        Assert.AreEqual("中央通道", ShipGameController.ResolveZoneZh(new Vector3(0f, 0f, 3.6f)),
            "spawn zone is corridor");
        Assert.AreEqual("舰桥", ShipGameController.ResolveZoneZh(new Vector3(0f, 0f, 8.0f)),
            "bridge zone");
        Assert.AreEqual("气闸舱", ShipGameController.ResolveZoneZh(new Vector3(0f, 0f, -11.8f)),
            "airlock zone");

        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipInterior_WalkableCompartments()
    {
        GameSession.Ensure().ResetSession();

        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForController(value => controller = value);
        SurfacePlayerController player = controller.Player;
        Assert.IsNotNull(player, "player ready");
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        // Bridge: from the spine through the open bridge door (1.8 m gap).
        Walk(player, new Vector3(0f, 0f, 5.2f), 0f, 34);
        Assert.IsTrue(player.transform.position.z > 6.7f,
            "player crossed into the bridge (z=" + player.transform.position.z + ")");
        Assert.AreEqual("舰桥", ShipGameController.ResolveZoneZh(player.transform.position),
            "zone is bridge after crossing");

        // Preparation bay: corridor -> west side room through the 1.4 m gap.
        Walk(player, new Vector3(0f, 0f, 3.4f), 270f, 40);
        Assert.IsTrue(player.transform.position.x < -1.4f,
            "player crossed into preparation (x=" + player.transform.position.x + ")");
        Assert.AreEqual("出发准备", ShipGameController.ResolveZoneZh(player.transform.position),
            "zone is preparation after crossing");

        // Fabrication bay: corridor -> east side room through the 1.4 m gap.
        Walk(player, new Vector3(0f, 0f, 3.4f), 90f, 40);
        Assert.IsTrue(player.transform.position.x > 1.4f,
            "player crossed into fabrication (x=" + player.transform.position.x + ")");
        Assert.AreEqual("资源加工", ShipGameController.ResolveZoneZh(player.transform.position),
            "zone is fabrication after crossing");

        // Outpost: corridor mouth -> aft outpost bay.
        Walk(player, new Vector3(0f, 0f, 0.8f), 180f, 24);
        Assert.IsTrue(player.transform.position.z < -0.5f,
            "player crossed into outpost (z=" + player.transform.position.z + ")");
        Assert.AreEqual("据点", ShipGameController.ResolveZoneZh(player.transform.position),
            "zone is outpost after crossing");

        // Airlock: outpost centre -> airlock chamber through the 1.6 m gap.
        Walk(player, new Vector3(0f, 0f, -7.6f), 180f, 34);
        Assert.IsTrue(player.transform.position.z < -9.8f,
            "player crossed into the airlock (z=" + player.transform.position.z + ")");
        Assert.AreEqual("气闸舱", ShipGameController.ResolveZoneZh(player.transform.position),
            "zone is airlock after crossing");

        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipInterior_ConsoleGazePrompts()
    {
        GameSession.Ensure().ResetSession();

        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForController(value => controller = value);
        SurfacePlayerController player = controller.Player;
        FirstPersonInteractor interactor = controller.Interactor;
        Assert.IsNotNull(player, "player ready");
        Assert.IsNotNull(interactor, "interactor ready");
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        // Console approach convention: stand ~1.7 m in front of the screen
        // plane, outside the interaction trigger, facing the screen so the
        // standard gaze ray resolves the console (dir = console screen normal).
        Vector3[] consolePositions =
        {
            new Vector3(0f, 0f, 10.05f),
            new Vector3(-4.95f, 0f, 3.4f),
            new Vector3(4.95f, 0f, 3.4f),
            new Vector3(-2.2f, 0f, -8.85f)
        };

        Vector3[] screenDirs =
        {
            new Vector3(0f, 0f, -1f),
            new Vector3(1f, 0f, 0f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 0f, 1f)
        };

        for (int c = 0; c < 4; c++)
        {
            ShipConsoleInteractable console = controller.Consoles[c];
            Vector3 approach = consolePositions[c] + screenDirs[c] * 1.7f;
            player.TeleportTo(approach);
            float yaw = Mathf.Atan2(-screenDirs[c].x, -screenDirs[c].z) * Mathf.Rad2Deg;
            player.SetFacing(yaw, 0f);
            yield return null;
            yield return null;

            interactor.RefreshTarget();
            Assert.IsTrue(ReferenceEquals(interactor.CurrentTarget, console),
                "console " + console.ConsoleId + " is the gaze target");
            Assert.IsTrue(interactor.HasValidTarget, "console " + console.ConsoleId + " interactable");
            string prompt = interactor.ResolvePrompt();
            Assert.IsNotNull(prompt, "console " + console.ConsoleId + " prompt not null");
            StringAssert.Contains(console.ChineseName, prompt, "prompt carries zh identity");
            Assert.AreEqual(console.PromptText, prompt, "prompt matches console prompt");
        }

        // The E placeholder path is safe to call (07A: no console mode yet).
        controller.Consoles[0].Interact();
        GameSession.Ensure().ResetSession();
    }

    [UnityTest]
    public IEnumerator ShipDoor_RealInteractionAndSafety()
    {
        GameSession.Ensure().ResetSession();

        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForController(value => controller = value);
        SurfacePlayerController player = controller.Player;
        FirstPersonInteractor interactor = controller.Interactor;
        ShipDoorInteractable door = controller.Doors[0]; // bridge door, gap x -0.9..0.9 @ z 6.3
        Assert.IsNotNull(player, "player ready");
        Assert.IsNotNull(door, "door 1 exists");
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        // Ensure the door is open and gaze the doorway from the corridor.
        if (!door.IsOpen)
        {
            door.RequestOpen();
        }

        yield return WaitUntilState(door, ShipDoorInteractable.ShipDoorState.Open, 4f);
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Open, door.State, "door starts open");

        player.TeleportTo(new Vector3(0f, 0f, 5.1f));
        player.SetFacing(0f, 0f);
        yield return null;
        yield return null;
        yield return WaitUntil(() => ReferenceEquals(interactor.CurrentTarget, door));
        Assert.IsTrue(ReferenceEquals(interactor.CurrentTarget, door), "gaze resolves the bridge door");
        Assert.IsTrue(interactor.HasValidTarget, "door is a valid target");
        StringAssert.Contains("关门", interactor.ResolvePrompt(), "open door prompt asks to close");

        // E closes the door through the exact world-interaction path.
        Assert.IsTrue(interactor.TryInteract(), "E closes the open door");
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Closing, door.State, "door closing");
        yield return WaitUntilState(door, ShipDoorInteractable.ShipDoorState.Closed, 6f);
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Closed, door.State, "door closed after E");
        Assert.IsTrue(Mathf.Abs(door.OpenAmount) < 0.001f, "door amount zero when closed");
        StringAssert.Contains("开门", interactor.ResolvePrompt(), "closed door prompt asks to open");

        // E reopens it.
        Assert.IsTrue(interactor.TryInteract(), "E opens the closed door");
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Opening, door.State, "door opening");
        yield return WaitUntilState(door, ShipDoorInteractable.ShipDoorState.Open, 6f);
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Open, door.State, "door open again");

        // Safety: a player standing in the doorway blocks closing.
        player.TeleportTo(new Vector3(0f, 0f, 6.3f)); // doorway centre
        yield return null;
        yield return WaitUntil(() => door.IsOccupied);
        Assert.IsTrue(door.IsOccupied, "doorway occupancy detected");
        door.RequestClose();
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Open, door.State,
            "door refuses to close through the player");

        // Safety: a closing door reopens when the player steps into it.
        player.TeleportTo(new Vector3(0f, 0f, 4.9f));
        yield return null;
        yield return WaitUntil(() => !door.IsOccupied);
        door.RequestClose();
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Closing, door.State, "door closing again");
        player.TeleportTo(new Vector3(0f, 0f, 6.3f));
        yield return WaitUntilState(door, ShipDoorInteractable.ShipDoorState.Open, 4f);
        Assert.AreEqual(ShipDoorInteractable.ShipDoorState.Open, door.State,
            "closing door reopens when the player enters the doorway");

        GameSession.Ensure().ResetSession();
    }

    private static void AssertNearly(float expected, float actual, string label)
    {
        Assert.IsTrue(Mathf.Abs(expected - actual) < 0.001f,
            label + " expected " + expected + " but was " + actual);
    }
}
