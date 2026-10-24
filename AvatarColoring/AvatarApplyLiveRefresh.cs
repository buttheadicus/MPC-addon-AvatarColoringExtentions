using System;
using System.Collections;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using IPA.Utilities;
using UnityEngine;

namespace MultiplayerChat.AvatarColoring;

// stock Apply saves AvatarData.dat async and often never pokes live BeatAvatarVisualControllers
// until the next process start. force a refresh after save so the new avatar shows immediately.
internal static class AvatarApplyLiveRefresh
{
    private static GameObject? _host;
    private static Runner? _runner;

    internal static void Schedule()
    {
        var runner = EnsureRunner();
        if (runner == null)
        {
            ForceNow();
            return;
        }

        runner.StopAllCoroutines();
        runner.StartCoroutine(RefreshAfterSaveRoutine());
    }

    internal static void ForceFromDisk() => RefreshLiveFromMemory(applyCommittedColor: false);

    internal static void ForceNow() => RefreshLiveFromMemory(applyCommittedColor: true);

    private static IEnumerator RefreshAfterSaveRoutine()
    {
        // SaveAsync is usually fire-and-forget; do not reload disk until the write has had time
        yield return null;
        RefreshLiveFromMemory(applyCommittedColor: true);
        yield return new WaitForSeconds(0.25f);
        RefreshLiveFromMemory(applyCommittedColor: true);
        yield return new WaitForSeconds(0.75f);
        RefreshLiveFromMemory(applyCommittedColor: true);
        AvatarDatOperations.TryDeleteAvatarBackup();
    }

    private static void RefreshLiveFromMemory(bool applyCommittedColor)
    {
        try
        {
            var model = AvatarColoringEditorSession.DataModel;
            if (model == null)
                model = TryGetAvatarDataModelFromEditor();
            if (model?.avatarData != null)
            {
                // load-from-disk must not stamp the last color-editor RGBA onto a different avatar's hat
                if (applyCommittedColor && AvatarColoringAlphaSliderPatcher.TryGetCommittedEditColor(out var committed))
                    AvatarDataColorResolver.TrySetColor(model.avatarData, AvatarColorEditContext.LastPart, committed);
                model.ReportAvatarChanged();
            }
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Debug($"[MPChat][AvatarColoring] ReportAvatarChanged: {ex.Message}");
        }

        try
        {
            var modelData = AvatarColoringEditorSession.DataModel?.avatarData;
            var visuals = Resources.FindObjectsOfTypeAll<BeatAvatarVisualController>();
            for (var i = 0; i < visuals.Length; i++)
            {
                var visual = visuals[i];
                if (visual == null || !visual.isActiveAndEnabled)
                    continue;

                var data = modelData ?? visual.GetField<AvatarData, BeatAvatarVisualController>("_avatarData");
                if (data == null)
                    continue;

                if (modelData != null)
                {
                    try
                    {
                        visual.SetField<BeatAvatarVisualController, AvatarData>("_avatarData", modelData);
                    }
                    catch
                    {
                        // keep going with UpdateAvatarVisual
                    }
                }

                visual.UpdateAvatarVisual(data);
            }
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Debug($"[MPChat][AvatarColoring] UpdateAvatarVisual: {ex.Message}");
        }
    }

    private static AvatarDataModel? TryGetAvatarDataModelFromEditor()
    {
        var editors = Resources.FindObjectsOfTypeAll<BeatAvatarEditorViewController>();
        for (var i = 0; i < editors.Length; i++)
        {
            var editor = editors[i];
            if (editor == null)
                continue;
            try
            {
                var model = editor.GetField<AvatarDataModel, BeatAvatarEditorViewController>("_avatarDataModel");
                if (model != null)
                    return model;
            }
            catch
            {
                // field name may differ
            }
        }

        return null;
    }

    private static Runner? EnsureRunner()
    {
        if (_runner != null)
            return _runner;

        try
        {
            _host = new GameObject("MPChatAvatarColoringApplyRefresh");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _runner = _host.AddComponent<Runner>();
            return _runner;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Debug($"[MPChat][AvatarColoring] apply refresh host: {ex.Message}");
            return null;
        }
    }

    private sealed class Runner : MonoBehaviour
    {
    }
}
