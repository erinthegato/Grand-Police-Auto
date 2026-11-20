using System;
using System.Collections.Generic;
using System.Linq;
using GPA.DiscordIntegration;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Turns the Discord code on when the Discord Social SDK package is in the project (adds the
/// DISCORD_SOCIAL_SDK define for PC and iOS) and off again if it's removed, so the game always builds.
/// Tools > GPA > Discord > Settings opens the settings asset (application ID, art keys).
/// </summary>
[InitializeOnLoad]
internal static class DiscordSdkSetup
{
    private const string Define = "DISCORD_SOCIAL_SDK";
    private const string SettingsPath = "Assets/GPA/Resources/DiscordSettings.asset";
    private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Standalone, NamedBuildTarget.iOS };

    static DiscordSdkSetup() => EditorApplication.delayCall += SyncDefine;

#pragma warning disable UAC0005   // only reads type names, at editor load
    private static bool SdkInstalled() =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("Discord.Sdk.Client", false) != null);
#pragma warning restore UAC0005

    [MenuItem("Tools/GPA/Discord/Re-check SDK")]
    private static void SyncDefine()
    {
        bool want = SdkInstalled();
        foreach (var target in Targets)
        {
            var defines = new List<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            bool has = defines.Contains(Define);
            if (has == want) continue;
            if (want) defines.Add(Define); else defines.Remove(Define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.Log(want ? $"[Discord] Discord Social SDK found: Rich Presence turned on for {target.TargetName}."
                           : $"[Discord] Discord Social SDK not found: Rich Presence turned off for {target.TargetName}.");
        }
    }

    [MenuItem("Tools/GPA/Discord/Settings")]
    private static void OpenSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<DiscordSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<DiscordSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
        }
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }
}
