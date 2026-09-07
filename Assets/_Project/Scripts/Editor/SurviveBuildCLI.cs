using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Phase 01 standalone build entry (Windows x86_64, Development).
///
/// Run via -executeMethod SurviveBuildCLI.BuildPlayer -buildPath &lt;dir&gt; -resultPath &lt;file&gt;
/// </summary>
public static class SurviveBuildCLI
{
    public static void BuildPlayer()
    {
        string buildPath = GetArgValue("-buildPath");
        string resultPath = GetArgValue("-resultPath");

        bool ok = false;
        var reportText = new System.Text.StringBuilder();

        try
        {
            if (string.IsNullOrEmpty(buildPath))
            {
                throw new Exception("-buildPath argument missing");
            }

            buildPath = Path.GetFullPath(buildPath).Replace('\\', '/');
            Directory.CreateDirectory(buildPath);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));

            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length != 3)
            {
                throw new Exception("build settings must contain exactly 3 scenes");
            }

            string[] scenePaths =
            {
                scenes[0].path,
                scenes[1].path,
                scenes[2].path
            };

            string exePath = buildPath + "/SurviveUIDemo.exe";

            // Development builds add a visible "Development Build" watermark,
            // so the review screenshot pipeline uses a Release player while
            // automated runs keep using Development (default) when asked.
            string devArg = GetArgValue("-development");
            bool development = string.IsNullOrEmpty(devArg) || !string.Equals(devArg, "0");
            BuildOptions buildOptions = development ? BuildOptions.Development : BuildOptions.None;

            var options = new BuildPlayerOptions
            {
                scenes = scenePaths,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = buildOptions
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            ok = summary.result == BuildResult.Succeeded;

            reportText.AppendLine(ok ? "BUILD SUCCESS" : "BUILD FAILED");
            reportText.AppendLine("path: " + exePath);
            reportText.AppendLine("mode: " + (development ? "Development" : "Release"));
            reportText.AppendLine("result: " + summary.result);
            reportText.AppendLine("sizeBytes: " + summary.totalSize);
            reportText.AppendLine("warnings: " + summary.totalWarnings);
            reportText.AppendLine("errors: " + summary.totalErrors);

            foreach (var step in report.steps)
            {
                reportText.AppendLine("step: " + step.name + " (" + step.duration + "s)");
            }

            if (!ok)
            {
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                        {
                            reportText.AppendLine("error: " + message.content);
                        }
                    }
                }
            }

            Debug.Log("[SurviveBuildCLI] " + (ok ? "BUILD SUCCESS" : "BUILD FAILED") + " -> " + exePath);
        }
        catch (Exception e)
        {
            ok = false;
            reportText.AppendLine("BUILD FAILED");
            reportText.AppendLine("exception: " + e);
            Debug.LogError("[SurviveBuildCLI] " + e);
        }

        try
        {
            if (!string.IsNullOrEmpty(resultPath))
            {
                File.WriteAllText(resultPath, reportText.ToString());
                Debug.Log("[SurviveBuildCLI] wrote result: " + resultPath);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[SurviveBuildCLI] cannot write result file: " + e);
            ok = false;
        }

        EditorApplication.Exit(ok ? 0 : 1);
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
