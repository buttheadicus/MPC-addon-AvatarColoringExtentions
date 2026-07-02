using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MultiplayerChat.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MultiplayerChat.AvatarColoring;

// Reads/writes Beat Saber AvatarData.dat (JSON) under LocalLow for randomize and save/load presets.
internal static class AvatarDatOperations
{
    private static readonly string[] HeadTopIds =
    {
        "WinterHat", "Wizard", "Windswept", "WetHair", "Untidy", "SweatBand", "Sultan", "Scifi", "Punk",
        "Ponytail", "PoloCap", "OnFire", "Normie", "Nanny", "Magician", "Loose", "LongBangs", "Hippie",
        "Heartbreak", "HalfShaved", "Emo", "DoubleTrouble", "Bob", "BedHead", "None"
    };

    private static readonly string[] GlassesIds = { "None", "Glasses01", "Glasses02" };

    private static readonly string[] FacialHairIds = { "None", "Beard01", "Moustache01", "Moustache02" };

    private static readonly string[] HandsIds = { "None", "Fingerless" };

    private static readonly string[] ClothesIds =
        { "Basket", "Dress", "Hoodie", "Jacket", "Jumpsuit", "Rock", "Tracksuit", "Vest" };

    private static readonly string[] SkinColorIds =
    {
        "Default", "Light", "Mid", "Brown", "DarkBrown", "Black", "Alien", "Smurf", "Zombie", "Purple"
    };

    internal static void EnsureAvatarStorageExists()
    {
        try
        {
            Directory.CreateDirectory(ChatIdFilePaths.AvatarStorageDirectoryPath);
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn($"[MPChat][AvatarColoring] Avatar Storage folder failed: {ex.Message}");
        }
    }

    internal static void TryDeleteAvatarBackup()
    {
        try
        {
            var p = ChatIdFilePaths.AvatarDataBackupFilePath;
            if (File.Exists(p))
                File.Delete(p);
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn($"[MPChat][AvatarColoring] Could not delete AvatarData.dat.bak: {ex.Message}");
        }
    }

    internal static bool RandomizeAvatarDatFile()
    {
        var path = ChatIdFilePaths.AvatarDataFilePath;
        try
        {
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var root = JObject.Parse(json);
            ReplaceIfPresent(root, "headTopId", HeadTopIds);
            ReplaceIfPresent(root, "glassesID", GlassesIds);
            ReplaceIfPresent(root, "glassesId", GlassesIds);
            ReplaceIfPresent(root, "facialHairId", FacialHairIds);
            ReplaceIfPresent(root, "handsId", HandsIds);
            ReplaceIfPresent(root, "clothesId", ClothesIds);
            ReplaceIfPresent(root, "skinColorId", SkinColorIds);

            var eyesPool = Enumerable.Range(1, 11).Select(i => $"Eyes{i}").ToArray();
            ReplaceIfPresent(root, "eyesId", eyesPool);

            RandomizeAllColorObjects(root);

            File.WriteAllText(path, root.ToString(Formatting.Indented));
            // New seed so the next randomize / in-editor random calls are not correlated to this file write.
            UnityEngine.Random.InitState(
                unchecked((int)DateTime.UtcNow.Ticks) ^ Environment.TickCount);
            return true;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Error($"[MPChat][AvatarColoring] Randomize failed: {ex.Message}");
            return false;
        }
    }

    private static void ReplaceIfPresent(JObject root, string propName, IReadOnlyList<string> choices)
    {
        var p = PropCi(root, propName);
        if (p == null)
            return;
        p.Value = choices[UnityEngine.Random.Range(0, choices.Count)];
    }

    private static void RandomizeAllColorObjects(JToken node)
    {
        switch (node.Type)
        {
            case JTokenType.Object:
                var jo = (JObject)node;
                if (LooksLikeColorVector(jo))
                {
                    FillRandomColor(jo);
                    return;
                }

                foreach (var child in jo.Properties().Select(p => p.Value))
                    RandomizeAllColorObjects(child);
                return;
            case JTokenType.Array:
                foreach (var el in (JArray)node)
                    RandomizeAllColorObjects(el);
                break;
        }
    }

    private static bool LooksLikeColorVector(JObject jo)
    {
        return jo["r"] != null && jo["g"] != null && jo["b"] != null && jo["a"] != null;
    }

    private static void FillRandomColor(JObject jo)
    {
        jo["r"] = FormatFloat(UnityEngine.Random.Range(-50f, 50f));
        jo["g"] = FormatFloat(UnityEngine.Random.Range(-50f, 50f));
        jo["b"] = FormatFloat(UnityEngine.Random.Range(-50f, 50f));
        jo["a"] = FormatFloat(UnityEngine.Random.Range(-10f, 10f));
    }

    private static float FormatFloat(float v) =>
        float.Parse(v.ToString("G9", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static JProperty? PropCi(JObject o, string name) =>
        o.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    internal static bool CopyAvatarDatToPreset(string rawName)
    {
        var name = SanitizePresetFileName(rawName);
        if (string.IsNullOrEmpty(name))
            return false;

        EnsureAvatarStorageExists();
        try
        {
            var src = ChatIdFilePaths.AvatarDataFilePath;
            if (!File.Exists(src))
                return false;
            var presetDir = Path.Combine(ChatIdFilePaths.AvatarStorageDirectoryPath, name);
            Directory.CreateDirectory(presetDir);
            var dst = Path.Combine(presetDir, "AvatarData.dat");
            File.Copy(src, dst, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Error($"[MPChat][AvatarColoring] Save preset failed: {ex.Message}");
            return false;
        }
    }

    // Removes a preset folder (nested AvatarData.dat layout) or a legacy flat file under Avatar Storage.
    internal static bool DeletePresetFromStorage(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return false;
        EnsureAvatarStorageExists();
        var root = ChatIdFilePaths.AvatarStorageDirectoryPath;
        try
        {
            if (!Directory.Exists(root))
                return false;

            var dirPath = Path.Combine(root, presetName);
            if (Directory.Exists(dirPath))
            {
                Directory.Delete(dirPath, recursive: true);
                return true;
            }

            var filePath = Path.Combine(root, presetName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Error($"[MPChat][AvatarColoring] Delete preset failed: {ex.Message}");
            return false;
        }
    }

    internal static bool ApplyPresetFromStorage(string presetFileName)
    {
        try
        {
            var root = ChatIdFilePaths.AvatarStorageDirectoryPath;
            var inFolder = Path.Combine(root, presetFileName, "AvatarData.dat");
            var legacyFlat = Path.Combine(root, presetFileName);
            string src;
            if (File.Exists(inFolder))
                src = inFolder;
            else if (File.Exists(legacyFlat))
                src = legacyFlat;
            else
                return false;
            TryDeleteAvatarBackup();
            var dst = ChatIdFilePaths.AvatarDataFilePath;
            File.Copy(src, dst, overwrite: true);
            TryDeleteAvatarBackup();
            return true;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Error($"[MPChat][AvatarColoring] Load preset failed: {ex.Message}");
            return false;
        }
    }

    internal static IReadOnlyList<string> ListPresetNames()
    {
        EnsureAvatarStorageExists();
        try
        {
            if (!Directory.Exists(ChatIdFilePaths.AvatarStorageDirectoryPath))
                return Array.Empty<string>();

            var root = ChatIdFilePaths.AvatarStorageDirectoryPath;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                var fn = Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(fn))
                    continue;
                var nestedDat = Path.Combine(d, "AvatarData.dat");
                if (File.Exists(nestedDat))
                    names.Add(fn);
            }

            foreach (var f in Directory.EnumerateFiles(root))
            {
                var fn = Path.GetFileName(f);
                if (string.IsNullOrEmpty(fn))
                    continue;
                if (string.Equals(fn, "AvatarData.dat", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (names.Contains(fn))
                    continue;
                if (Directory.Exists(Path.Combine(root, fn)))
                    continue;
                names.Add(fn);
            }

            return names.OrderBy(fn => fn, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static string SanitizePresetFileName(string raw)
    {
        var s = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return "";

        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c.ToString(), "");

        return s.Trim();
    }
}
