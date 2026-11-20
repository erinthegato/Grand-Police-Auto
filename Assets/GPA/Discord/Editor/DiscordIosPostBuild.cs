#if UNITY_IOS
using System.IO;
using GPA.DiscordIntegration;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// iPhone: after "Export iPhone (Xcode project)", registers the discord-APPID:// URL scheme in
/// Info.plist so Discord can hand the player back to the game after they link their account, and
/// lets the game open the Discord app (LSApplicationQueriesSchemes).
/// </summary>
internal static class DiscordIosPostBuild
{
    [PostProcessBuild(200)]
    private static void OnPostprocessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        var settings = DiscordSettings.Load();
        if (settings == null || !settings.EnableOnIos || !settings.TryGetApplicationId(out ulong appId)) return;

        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        PlistElementDict root = plist.root;

        string scheme = $"discord-{appId}";
        PlistElementArray urlTypes = root["CFBundleURLTypes"] as PlistElementArray ?? root.CreateArray("CFBundleURLTypes");
        bool present = false;
        foreach (var item in urlTypes.values)
            if (item is PlistElementDict d && d["CFBundleURLSchemes"] is PlistElementArray schemes)
                foreach (var s in schemes.values) if (s.AsString() == scheme) present = true;
        if (!present)
        {
            var dict = urlTypes.AddDict();
            dict.SetString("CFBundleURLName", "Discord login");
            dict.CreateArray("CFBundleURLSchemes").AddString(scheme);
        }

        PlistElementArray queries = root["LSApplicationQueriesSchemes"] as PlistElementArray ?? root.CreateArray("LSApplicationQueriesSchemes");
        bool hasDiscord = false;
        foreach (var q in queries.values) if (q.AsString() == "discord") hasDiscord = true;
        if (!hasDiscord) queries.AddString("discord");

        plist.WriteToFile(plistPath);
    }
}
#endif
