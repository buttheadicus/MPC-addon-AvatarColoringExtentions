using System;
using System.Collections.Generic;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using IPA.Utilities;
using MultiplayerChat.AvatarExtras.Networking;
using MultiplayerChat.Settings;
using Newtonsoft.Json;

namespace MultiplayerChat.AvatarColoring;

// Buffers avatar writes during the stock color screen: ChangeColor is deferred until Apply; Cancel restores the snapshot.
internal static class AvatarColorEditorDraft
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Converters = new List<JsonConverter> { new UnityColorJsonConverter() }
    };

    private static bool _active;

    private static string? _snapshotJson;

    private static AvatarDataModel? _model;

    private static int _commitBypassDepth;

    // If stock Apply triggers didFinish with false (or Cancel with true), flip this constant.
    internal static bool DidFinishParameterTrueMeansAppliedChanges = true;

    internal static bool InterpretDidFinishAsApplied(bool rawParameter) =>
        DidFinishParameterTrueMeansAppliedChanges ? rawParameter : !rawParameter;

    internal static bool ShouldInterceptChangeColor =>
        ModSettings.EnableAvatarColoringExtensions && _active && _commitBypassDepth <= 0;

    internal static IDisposable CommitBypassScope()
    {
        _commitBypassDepth++;
        return new BypassScope();
    }

    internal static void BeginIfNeeded(AvatarDataModel model)
    {
        if (!ModSettings.EnableAvatarColoringExtensions || model?.avatarData == null)
            return;

        // Drop any stale snapshot (e.g. Cancel kept payload until DidDeactivate; never orphan across sessions).
        Clear();

        _model = model;
        try
        {
            _snapshotJson = JsonConvert.SerializeObject(model.avatarData, Formatting.None, SerializerSettings);
            _active = true;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn($"[MPChat][AvatarColoring] Draft snapshot failed: {ex.Message}");
            Clear();
        }
    }

    // BeatGames wires Apply/Cancel to didFinishEvent: parameter true when the color edit was accepted (Apply).
    internal static void HandleDidFinish(EditAvatarColorViewController vc, BeatAvatarEditorViewController beatEditor,
        bool appliedChanges)
    {
        if (!_active || vc == null || beatEditor == null || _model == null || string.IsNullOrEmpty(_snapshotJson))
        {
            Clear();
            return;
        }

        if (InterpretDidFinishAsApplied(appliedChanges))
        {
            try
            {
                CommitPendingChangeColor(vc);
            }
            finally
            {
                Clear();
            }

            return;
        }

        try
        {
            RevertVisualAndModel(vc, beatEditor);
        }
        finally
        {
            // Cancel path: keep snapshot + model until DidDeactivate runs AbortIfStillActive (second revert is idempotent).
            _active = false;
            _commitBypassDepth = 0;
        }
    }

    internal static void AbortIfStillActive(EditAvatarColorViewController vc, BeatAvatarEditorViewController beatEditor)
    {
        if (vc == null || beatEditor == null)
            return;

        if (_model == null || string.IsNullOrEmpty(_snapshotJson))
            return;

        try
        {
            RevertVisualAndModel(vc, beatEditor);
        }
        finally
        {
            Clear();
        }
    }

    internal static void CommitPendingChangeColor(EditAvatarColorViewController vc)
    {
        var color = vc.color;
        if (AvatarColoringAlphaSliderPatcher.TryGetCommittedEditColor(out var merged))
            color = merged;

        vc.SetColor(color);
        using (CommitBypassScope())
            vc.InvokeMethod<object, EditAvatarColorViewController>("ChangeColor", color);

        // Stock ChangeColor / HSV paths often persist RGB only (alpha becomes 1). Write full RGBA from our controls.
        if (_model?.avatarData != null
            && AvatarDataColorResolver.TrySetColor(_model.avatarData, AvatarColorEditContext.LastPart, color))
        {
            _model.ReportAvatarChanged();
        }
    }

    private static void RevertVisualAndModel(EditAvatarColorViewController vc,
        BeatAvatarEditorViewController beatEditor)
    {
        try
        {
            using (CommitBypassScope())
            {
                var dto = JsonConvert.DeserializeObject<AvatarData>(_snapshotJson!, SerializerSettings);
                if (dto == null || _model == null)
                    return;

                _model.avatarData = dto;
                _model.ReportAvatarChanged();
                PackedExtrasString.SyncSeparateColorsFromPackedWire(_model.avatarData);

                try
                {
                    beatEditor.InvokeMethod<object, BeatAvatarEditorViewController>("ReportAllChangedAndUpdate");
                }
                catch (Exception ex)
                {
                    MultiplayerChat.Plugin.Log?.Debug($"[MPChat][AvatarColoring] ReportAllChangedAndUpdate on revert: {ex.Message}");
                }

                beatEditor.InvokeMethod<object, BeatAvatarEditorViewController>("RefreshUi");

                if (AvatarDataColorResolver.TryGetColor(_model.avatarData, AvatarColorEditContext.LastPart, out var c))
                {
                    vc.SetColor(c);
                    vc.InvokeMethod<object, EditAvatarColorViewController>("ChangeColor", c);
                }

                AvatarColoringAlphaSliderPatcher.NotifyAvatarDataReloadedWhileColorUiOpen();
            }
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn($"[MPChat][AvatarColoring] Draft revert failed: {ex.Message}");
        }
    }

    private static void Clear()
    {
        _active = false;
        _snapshotJson = null;
        _model = null;
        _commitBypassDepth = 0;
    }

    private sealed class BypassScope : IDisposable
    {
        public void Dispose()
        {
            _commitBypassDepth = Math.Max(0, _commitBypassDepth - 1);
        }
    }
}
