using System;
using System.Collections.Generic;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using IPA.Utilities;
using MultiplayerChat.AvatarExtras.Networking;
using MultiplayerChat.Settings;
using Newtonsoft.Json;

namespace MultiplayerChat.AvatarColoring;

// buffers avatar writes during the stock color screen: ChangeColor is deferred until Apply; Cancel restores the snapshot.
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

    private static bool _applyPressed;

    // if stock Apply triggers didFinish with false (or Cancel with true), flip this constant.
    internal static bool DidFinishParameterTrueMeansAppliedChanges = true;

    internal static bool InterpretDidFinishAsApplied(bool rawParameter) =>
        DidFinishParameterTrueMeansAppliedChanges ? rawParameter : !rawParameter;

    internal static bool ShouldInterceptChangeColor =>
        ModSettings.EnableAvatarColoringExtensions && _active && _commitBypassDepth <= 0;

    internal static bool IsCommitBypassActive => _commitBypassDepth > 0;

    internal static void MarkApplyPressed() => _applyPressed = true;

    internal static IDisposable CommitBypassScope()
    {
        _commitBypassDepth++;
        return new BypassScope();
    }

    internal static void BeginIfNeeded(AvatarDataModel model)
    {
        if (!ModSettings.EnableAvatarColoringExtensions || model?.avatarData == null)
            return;

        if (_active && ReferenceEquals(_model, model) && !string.IsNullOrEmpty(_snapshotJson))
            return;

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

        // Apply button always commits. stock didFinish(_colorChanged) is false when only RGBA sliders moved.
        if (_applyPressed || InterpretDidFinishAsApplied(appliedChanges))
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
            // cancel path: keep snapshot + model until DidDeactivate runs AbortIfStillActive (second revert is idempotent).
            _active = false;
            _commitBypassDepth = 0;
        }
    }

    internal static void AbortIfStillActive(EditAvatarColorViewController vc, BeatAvatarEditorViewController beatEditor)
    {
        if (vc == null || beatEditor == null)
            return;

        if (_applyPressed)
        {
            Clear();
            return;
        }

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
        AvatarColoringAlphaSliderPatcher.MergeLiveAlpha(ref color);
        AvatarColoringAlphaSliderPatcher.RememberLiveColor(color);

        vc.SetColor(color);
        using (CommitBypassScope())
            vc.InvokeMethod<object, EditAvatarColorViewController>("ChangeColor", color);

        // stock HSV get_color / ChangeColor is RGB with a=1. write full RGBA from our controls after that.
        if (_model?.avatarData != null
            && AvatarDataColorResolver.TrySetColor(_model.avatarData, AvatarColorEditContext.LastPart, color))
        {
            _model.ReportAvatarChanged();
        }

        var editor = AvatarColoringEditorSession.EditorVc;
        if (editor != null)
        {
            using (CommitBypassScope())
                editor.SaveColorChange(AvatarColorEditContext.LastPart);
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
        _applyPressed = false;
    }

    private sealed class BypassScope : IDisposable
    {
        public void Dispose()
        {
            _commitBypassDepth = Math.Max(0, _commitBypassDepth - 1);
        }
    }
}
