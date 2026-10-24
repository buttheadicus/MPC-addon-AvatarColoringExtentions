using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using IPA.Utilities;
using UnityEngine;

namespace MultiplayerChat.AvatarColoring;

// settings OK destroys menu VCs but leaves static patcher refs. treat Unity-null as gone.
internal static class AvatarColoringLiveObjects
{
    internal static AvatarDataModel? ResolveAvatarDataModel(BeatAvatarEditorViewController? editor)
    {
        if (editor != null)
        {
            try
            {
                var fromEditor = editor.GetField<AvatarDataModel, BeatAvatarEditorViewController>("_avatarDataModel");
                if (fromEditor != null)
                    return fromEditor;
            }
            catch
            {
                // field name may differ
            }
        }

        if (AvatarColoringEditorSession.DataModel != null)
            return AvatarColoringEditorSession.DataModel;

        try
        {
            var container = BeatSaberMarkupLanguage.BeatSaberUI.DiContainer;
            if (container != null && container.HasBinding(typeof(AvatarDataModel)))
                return container.TryResolve<AvatarDataModel>();
        }
        catch
        {
            // menu container may not expose it
        }

        return null;
    }

    internal static BeatAvatarEditorViewController? ResolveBeatAvatarEditor()
    {
        if (AvatarColoringEditorSession.EditorVc != null)
            return AvatarColoringEditorSession.EditorVc;

        var editors = Resources.FindObjectsOfTypeAll<BeatAvatarEditorViewController>();
        for (var i = 0; i < editors.Length; i++)
        {
            if (editors[i] != null)
                return editors[i];
        }

        return null;
    }
}
