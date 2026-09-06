using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>
/// Phase 01 structural validator.
///
/// Checks (edit-mode, deterministic):
/// - scenes exist with the correct build order (0 = 00_Boot, 1 = 01_Hub),
/// - MainApp.uxml contains every element the runtime binds to,
/// - 00_Boot keeps its minimal role (BootController, no UI / no authored GameSession),
/// - 01_Hub has exactly one UIDocument + one AppController with valid assets.
///
/// Run via Tools/SurviveUI/Validate Phase 01 Project or
/// -executeMethod SurviveProjectValidator.RunCli -resultPath &lt;file&gt;
/// </summary>
public static class SurviveProjectValidator
{
    private const string BootScenePath = "Assets/_Project/Scenes/00_Boot.unity";
    private const string HubScenePath = "Assets/_Project/Scenes/01_Hub.unity";
    private const string UxmlPath = "Assets/_Project/UI/UXML/MainApp.uxml";
    private const string UssPath = "Assets/_Project/UI/USS/AppTheme.uss";

    [MenuItem("Tools/SurviveUI/Validate Phase 01 Project")]
    public static void ValidateFromMenu()
    {
        RunValidation(null);
    }

    /// <summary>CLI entry used by batch automation.</summary>
    public static void RunCli()
    {
        string resultPath = GetArgValue("-resultPath");
        bool ok = RunValidation(resultPath);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool RunValidation(string resultPath)
    {
        var log = new StringBuilder();
        int failures = 0;

        void Record(bool pass, string label)
        {
            log.AppendLine((pass ? "PASS" : "FAIL") + " :: " + label);
            if (pass)
            {
                Debug.Log("[SurviveProjectValidator] PASS :: " + label);
            }
            else
            {
                failures++;
                Debug.LogError("[SurviveProjectValidator] FAIL :: " + label);
            }
        }

        // ---- Assets ----
        Record(File.Exists(BootScenePath), "scene exists: " + BootScenePath);
        Record(File.Exists(HubScenePath), "scene exists: " + HubScenePath);
        Record(File.Exists(UxmlPath), "uxml exists: " + UxmlPath);
        Record(File.Exists(UssPath), "uss exists: " + UssPath);

        // ---- Build order ----
        var buildScenes = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];
        Record(buildScenes.Length == 2,
            "build contains exactly 2 scenes (found " + buildScenes.Length + ")");
        if (buildScenes.Length >= 1)
        {
            Record(buildScenes[0].path == BootScenePath && buildScenes[0].enabled,
                "build[0] = 00_Boot enabled (actual: " + buildScenes[0].path + ")");
        }

        if (buildScenes.Length >= 2)
        {
            Record(buildScenes[1].path == HubScenePath && buildScenes[1].enabled,
                "build[1] = 01_Hub enabled (actual: " + buildScenes[1].path + ")");
        }

        // ---- MainApp.uxml coverage ----
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        Record(uxml != null, "MainApp.uxml imports as VisualTreeAsset");
        if (uxml != null)
        {
            HashSet<string> present = new HashSet<string>();
            TemplateContainer template = uxml.Instantiate();
            template.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.name))
                {
                    present.Add(element.name);
                }
            });

            foreach (string expected in ElementNames.All)
            {
                Record(present.Contains(expected), "MainApp.uxml contains element: " + expected);
            }
        }

        // ---- 00_Boot scene content ----
        if (File.Exists(BootScenePath))
        {
            EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
            Record(CountInScene<BootController>() == 1,
                "00_Boot has exactly one BootController (found " + CountInScene<BootController>() + ")");
            Record(CountInScene<UIDocument>() == 0,
                "00_Boot contains no UIDocument (found " + CountInScene<UIDocument>() + ")");
            Record(CountInScene<GameSession>() == 0,
                "00_Boot authors no GameSession instance (found " + CountInScene<GameSession>() + ")");
        }

        // ---- 01_Hub scene content ----
        if (File.Exists(HubScenePath))
        {
            EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
            Record(CountInScene<UIDocument>() == 1,
                "01_Hub has exactly one UIDocument (found " + CountInScene<UIDocument>() + ")");
            Record(CountInScene<AppController>() == 1,
                "01_Hub has exactly one AppController (found " + CountInScene<AppController>() + ")");
            Record(CountInScene<GameSession>() == 0,
                "01_Hub authors no GameSession instance (found " + CountInScene<GameSession>() + ")");

            UIDocument document = FirstInScene<UIDocument>();
            if (document != null)
            {
                string sourceName = document.visualTreeAsset != null ? document.visualTreeAsset.name : "<null>";
                Record(document.visualTreeAsset != null && sourceName == "MainApp",
                    "UIDocument bound to MainApp.uxml (actual: " + sourceName + ")");
                Record(document.panelSettings != null,
                    "UIDocument has PanelSettings assigned");
            }
            else
            {
                Record(false, "UIDocument missing (cannot verify binding)");
            }
        }

        bool ok = failures == 0;
        log.AppendLine(ok ? "RESULT: SUCCESS" : "RESULT: FAILED");
        log.AppendLine("CHECKS: " + CountCheckLines(log) + " | FAILURES: " + failures);

        if (!string.IsNullOrEmpty(resultPath))
        {
            try
            {
                File.WriteAllText(resultPath, log.ToString());
                Debug.Log("[SurviveProjectValidator] wrote result: " + resultPath);
            }
            catch (Exception e)
            {
                Debug.LogError("[SurviveProjectValidator] cannot write result file: " + e);
                ok = false;
            }
        }
        else
        {
            Debug.Log("[SurviveProjectValidator] result:\n" + log);
        }

        return ok;
    }

    private static int CountCheckLines(StringBuilder sb)
    {
        int count = 0;
        foreach (string line in sb.ToString().Split('\n'))
        {
            if (line.StartsWith("PASS") || line.StartsWith("FAIL"))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountInScene<T>() where T : Component
    {
        int count = 0;
        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
        {
            count += root.GetComponentsInChildren<T>(true).Length;
        }

        return count;
    }

    private static T FirstInScene<T>() where T : Component
    {
        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }

        return null;
    }

    private static string GetArgValue(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
