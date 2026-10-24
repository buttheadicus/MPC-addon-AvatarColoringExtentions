using System;
using System.IO;
using HarmonyLib;

namespace MultiplayerChat.AvatarColoring;

// BGLib FileSystemFileStorage saves via File.Replace(tmp, AvatarData.dat, AvatarData.dat.bak).
// skip that backup so Apply does not leave a bak behind.
[HarmonyPatch]
internal static class AvatarDataBakPreventionPatch
{
    [HarmonyPatch(typeof(File), nameof(File.Replace), new[] { typeof(string), typeof(string), typeof(string) })]
    [HarmonyPrefix]
    private static bool PrefixReplace3(string sourceFileName, string destinationFileName, string destinationBackupFileName)
    {
        return !TryReplaceAvatarDataWithoutBackup(sourceFileName, destinationFileName, destinationBackupFileName);
    }

    [HarmonyPatch(typeof(File), nameof(File.Replace), new[]
    {
        typeof(string), typeof(string), typeof(string), typeof(bool)
    })]
    [HarmonyPrefix]
    private static bool PrefixReplace4(string sourceFileName, string destinationFileName,
        string destinationBackupFileName, bool ignoreMetadataErrors)
    {
        // ignoreMetadataErrors is unused: we never create a bak for AvatarData.dat
        return !TryReplaceAvatarDataWithoutBackup(sourceFileName, destinationFileName, destinationBackupFileName);
    }

    // true when we handled the replace (caller should skip stock File.Replace)
    private static bool TryReplaceAvatarDataWithoutBackup(string sourceFileName, string destinationFileName,
        string destinationBackupFileName)
    {
        // only AvatarData.dat itself. never match a random dest that happens to use .bak
        if (!IsAvatarDataPath(destinationFileName))
            return false;

        try
        {
            if (string.IsNullOrEmpty(sourceFileName) || string.IsNullOrEmpty(destinationFileName))
                return false;
            if (!File.Exists(sourceFileName))
                return false;

            // copy-over then delete temp. do not delete dest first: a failed move used to
            // leave AvatarData.dat missing and stock File.Replace then throws (dest must exist).
            File.Copy(sourceFileName, destinationFileName, overwrite: true);
            try
            {
                File.Delete(sourceFileName);
            }
            catch
            {
                // dest is already the new file; leftover tmp is harmless
            }

            AvatarDatOperations.TryDeleteAvatarBackup();
            if (!string.IsNullOrEmpty(destinationBackupFileName) && File.Exists(destinationBackupFileName))
            {
                try
                {
                    File.Delete(destinationBackupFileName);
                }
                catch
                {
                    // deferred delete is the safety net
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn(
                $"[MPChat][AvatarColoring] bak-less AvatarData replace failed, falling back to stock: {ex.Message}");
            return false;
        }
    }

    private static bool IsAvatarDataPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return string.Equals(Path.GetFileName(path), "AvatarData.dat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAvatarDataBakPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return string.Equals(Path.GetFileName(path), "AvatarData.dat.bak", StringComparison.OrdinalIgnoreCase);
    }
}
