using System;
using System.Reflection;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using HarmonyLib;
using HMUI;
using MultiplayerChat.Contracts;
using UnityEngine;

namespace MultiplayerChat.AvatarColoring;

// editor DidActivate does not call base. patch that override once (not CallDidActivate, which also fires with added=false).
internal static class AvatarEditorHarmonyBootstrap
{
    private static readonly Type[] DidActivateArgs = { typeof(bool), typeof(bool), typeof(bool) };

    internal static void Apply(IMpChatHost host)
    {
        var harmony = host.GetAddonHarmony() as Harmony;
        if (harmony == null)
        {
            Debug.LogError("[MPChat][AvatarColoring] GetAddonHarmony was not a Harmony instance");
            host.LogError("[MPChat][AvatarColoring] GetAddonHarmony was not a Harmony instance");
            return;
        }

        Patch(host, harmony,
            AccessTools.DeclaredMethod(typeof(BeatAvatarEditorViewController), "DidActivate", DidActivateArgs),
            postfix: new HarmonyMethod(typeof(AvatarEditorDidActivateHarmonyPatch), nameof(AvatarEditorDidActivateHarmonyPatch.Postfix)),
            label: "BeatAvatarEditorViewController.DidActivate");

        // settings OK rebuilds menu and drops core affinity shims. color VC uses base DidActivate, so patch that.
        Patch(host, harmony,
            AccessTools.DeclaredMethod(typeof(ViewController), "DidActivate", DidActivateArgs),
            postfix: new HarmonyMethod(typeof(AvatarColorEditorDidActivateHarmonyPatch), nameof(AvatarColorEditorDidActivateHarmonyPatch.Postfix)),
            label: "ViewController.DidActivate");

        var editorDeactivate = AccessTools.DeclaredMethod(typeof(BeatAvatarEditorViewController), "DidDeactivate", DidActivateArgs);
        if (editorDeactivate != null)
        {
            Patch(host, harmony,
                editorDeactivate,
                postfix: new HarmonyMethod(typeof(AvatarEditorDidDeactivateHarmonyPatch), nameof(AvatarEditorDidDeactivateHarmonyPatch.Postfix)),
                label: "BeatAvatarEditorViewController.DidDeactivate");
        }

        Patch(host, harmony,
            AccessTools.DeclaredMethod(typeof(ViewController), "DidDeactivate", DidActivateArgs),
            postfix: new HarmonyMethod(typeof(AvatarViewControllerDidDeactivateHarmonyPatch), nameof(AvatarViewControllerDidDeactivateHarmonyPatch.Postfix)),
            label: "ViewController.DidDeactivate");

        Patch(host, harmony,
            AccessTools.DeclaredMethod(typeof(EditAvatarColorViewController), "HandleApplyButtonWasPressed"),
            prefix: new HarmonyMethod(typeof(AvatarColorApplyHarmonyPatch), nameof(AvatarColorApplyHarmonyPatch.Prefix)),
            label: "EditAvatarColorViewController.HandleApplyButtonWasPressed");

        // stock color getter is HSVToRGB, which always has a=1. keep our slider/last-known alpha on the way in and out.
        Patch(host, harmony,
            AccessTools.DeclaredMethod(typeof(EditAvatarColorViewController), "ChangeColor", new[] { typeof(Color) }),
            prefix: new HarmonyMethod(typeof(AvatarColorChangeColorHarmonyPatch), nameof(AvatarColorChangeColorHarmonyPatch.Prefix)),
            postfix: new HarmonyMethod(typeof(AvatarColorChangeColorHarmonyPatch), nameof(AvatarColorChangeColorHarmonyPatch.Postfix)),
            label: "EditAvatarColorViewController.ChangeColor");
    }

    internal static void Reset()
    {
        AvatarColoringEditorPatcher.DropLive();
        AvatarColoringAlphaSliderPatcher.DropLive();
    }

    private static void Patch(
        IMpChatHost host,
        Harmony harmony,
        MethodBase? target,
        string label,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null)
    {
        if (target == null)
        {
            Debug.LogError("[MPChat][AvatarColoring] missing method " + label);
            host.LogError("[MPChat][AvatarColoring] missing method " + label);
            return;
        }

        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            Debug.Log("[MPChat][AvatarColoring] patched " + label);
            host.LogError("[MPChat][AvatarColoring] patched " + label);
        }
        catch (Exception ex)
        {
            Debug.LogError("[MPChat][AvatarColoring] failed to patch " + label + ": " + ex);
            host.LogError("[MPChat][AvatarColoring] failed to patch " + label + ": " + ex.Message);
        }
    }
}

internal static class AvatarEditorDidActivateHarmonyPatch
{
    public static void Postfix(BeatAvatarEditorViewController __instance, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        AvatarColoringEditorPatcher.HandleDidActivate(__instance, firstActivation, addedToHierarchy, screenSystemEnabling);
    }
}

internal static class AvatarEditorDidDeactivateHarmonyPatch
{
    public static void Postfix(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        AvatarColoringEditorPatcher.HandleDidDeactivate(removedFromHierarchy, screenSystemDisabling);
    }
}

// color editor does not override DidActivate, so the base VC call is what we get after a settings reload.
internal static class AvatarColorEditorDidActivateHarmonyPatch
{
    public static void Postfix(ViewController __instance, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (__instance is not EditAvatarColorViewController colorEditor)
            return;
        AvatarColoringAlphaSliderPatcher.HandleDidActivate(colorEditor, firstActivation, addedToHierarchy, screenSystemEnabling);
    }
}

internal static class AvatarViewControllerDidDeactivateHarmonyPatch
{
    public static void Postfix(ViewController __instance, bool removedFromHierarchy, bool screenSystemDisabling)
    {
        if (__instance is BeatAvatarEditorViewController)
            AvatarColoringEditorPatcher.HandleDidDeactivate(removedFromHierarchy, screenSystemDisabling);
        else if (__instance is EditAvatarColorViewController)
            AvatarColoringAlphaSliderPatcher.HandleDidDeactivate(removedFromHierarchy, screenSystemDisabling);
    }
}

// stock Apply invokes didFinish(_colorChanged), which is false if HSV never ran. that used to take the Cancel/revert path.
internal static class AvatarColorApplyHarmonyPatch
{
    public static void Prefix(EditAvatarColorViewController __instance)
    {
        AvatarColorEditorDraft.MarkApplyPressed();
        AvatarColorEditorDraft.CommitPendingChangeColor(__instance);
        AvatarApplyLiveRefresh.Schedule();
        if (AvatarColoringAlphaSliderPatcher.TryGetCommittedEditColor(out var committed))
            Debug.Log("[MPChat][AvatarColoring] color Apply: committed r=" + committed.r + " g=" + committed.g + " b=" + committed.b + " a=" + committed.a);
        else
            Debug.Log("[MPChat][AvatarColoring] color Apply: committed RGBA before didFinish");
    }
}

internal static class AvatarColorChangeColorHarmonyPatch
{
    public static void Prefix(ref Color color)
    {
        // only when our rgba row pushed. hsv wheel color must stay untouched.
        AvatarColoringAlphaSliderPatcher.MergeLiveAlpha(ref color);
    }

    public static void Postfix(Color color)
    {
        AvatarColoringAlphaSliderPatcher.SyncFromColorWheel(color);
    }
}
