using System;
using System.IO;
using System.Threading.Tasks;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using IPA.Utilities;
using MultiplayerChat.AvatarExtras.Networking;
using MultiplayerChat.Settings;
using Newtonsoft.Json;

namespace MultiplayerChat.AvatarColoring;

// Holds active avatar editor instances so save/load flows can refresh disk-backed presets.
internal static class AvatarColoringEditorSession
{
    internal static BeatAvatarEditorViewController? EditorVc { get; private set; }
    internal static AvatarDataModel? DataModel { get; private set; }

    internal static void Attach(BeatAvatarEditorViewController editor, AvatarDataModel model)
    {
        EditorVc = editor;
        DataModel = model;
    }

    internal static void Clear()
    {
        EditorVc = null;
        DataModel = null;
    }

    internal static void RefreshAfterAvatarDatChangedOnDisk()
    {
        TryReloadAvatarDisk(DataModel);
        RefreshBeatAvatarEditor(EditorVc);
        if (DataModel?.avatarData != null)
            PackedExtrasString.SyncSeparateColorsFromPackedWire(DataModel.avatarData);
        AvatarColoringAlphaSliderPatcher.NotifyAvatarDataReloadedWhileColorUiOpen();
    }

    private static void RefreshBeatAvatarEditor(BeatAvatarEditorViewController? vc)
    {
        if (vc == null)
            return;
        try
        {
            vc.InvokeMethod<object, BeatAvatarEditorViewController>("ReportAllChangedAndUpdate");
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Debug($"[MPChat][AvatarColoring] ReportAllChangedAndUpdate: {ex.Message}");
        }

        try
        {
            vc.InvokeMethod<object, BeatAvatarEditorViewController>("RefreshUi");
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn($"[MPChat][AvatarColoring] RefreshUi failed: {ex.Message}");
        }
    }

    // BS 1.40+ AvatarDataModel: deserialize AvatarData.dat and call ReportAvatarChanged.
    private static void TryReloadAvatarDisk(AvatarDataModel? model)
    {
        if (model == null)
            return;

        var path = ChatIdFilePaths.AvatarDataFilePath;
        if (!File.Exists(path))
        {
            MultiplayerChat.Plugin.Log?.Warn("[MPChat][AvatarColoring] AvatarData.dat missing after disk write.");
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<AvatarData>(json,
                new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                });
            if (data != null)
            {
                model.avatarData = data;
                model.ReportAvatarChanged();
                return;
            }
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Debug($"[MPChat][AvatarColoring] JSON reload: {ex.Message}");
        }

        try
        {
            model.LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn(
                "[MPChat][AvatarColoring] Could not reload AvatarDataModel after disk change; reopen the avatar editor if needed. " +
                ex.Message);
        }
    }
}
