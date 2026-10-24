using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using HarmonyLib;

namespace MultiplayerChat.AvatarColoring;

// affinity on the addon assembly does not run HandleApply (core shim never forwarded it).
// Harmony.PatchAll does, so Apply can delete bak and refresh live avatars without a core bump.
[HarmonyPatch(typeof(BeatAvatarEditorViewController), "HandleApplyButtonWasPressed")]
internal static class AvatarEditorApplyHarmonyPatch
{
    private static void Postfix()
    {
        AvatarDatOperations.ScheduleDeleteAvatarBackup();
        AvatarApplyLiveRefresh.Schedule();
    }
}
