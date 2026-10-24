using System;
using System.Reflection;
using HMUI;
using MultiplayerChat;
using MultiplayerChat.UI;
using UnityEngine;
using Zenject;

namespace MultiplayerChat.AvatarColoring;

// save/load flows are menu-bound; settings OK builds a new container without those binds.
internal static class AvatarColoringMenuFlows
{
    private static AvatarNameEntryFlowCoordinator? _name;
    private static AvatarLoadListFlowCoordinator? _load;
    private static bool _ownsName;
    private static bool _ownsLoad;

    internal static void Drop()
    {
        DestroyOwned(ref _name, ref _ownsName);
        DestroyOwned(ref _load, ref _ownsLoad);
    }

    internal static AvatarNameEntryFlowCoordinator? ResolveName()
    {
        if (_name != null)
            return _name;
        _name = ResolveOrCreate<AvatarNameEntryFlowCoordinator, AvatarNameEntryViewController>(out _ownsName);
        return _name;
    }

    internal static AvatarLoadListFlowCoordinator? ResolveLoad()
    {
        if (_load != null)
            return _load;
        _load = ResolveOrCreate<AvatarLoadListFlowCoordinator, AvatarLoadListViewController>(out _ownsLoad);
        return _load;
    }

    private static void DestroyOwned<TFlow>(ref TFlow? flow, ref bool owned)
        where TFlow : FlowCoordinator
    {
        if (owned && flow != null)
        {
            try
            {
                var field = typeof(TFlow).GetField("_viewController", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(flow) is ViewController view && view != null)
                    UnityEngine.Object.Destroy(view.gameObject);
                UnityEngine.Object.Destroy(flow.gameObject);
            }
            catch
            {
                // already gone with the menu
            }
        }

        flow = null;
        owned = false;
    }

    private static TFlow? ResolveOrCreate<TFlow, TView>(out bool owned)
        where TFlow : FlowCoordinator
        where TView : ViewController
    {
        owned = false;
        var container = BeatSaberMarkupLanguage.BeatSaberUI.DiContainer;

        if (container != null)
        {
            try
            {
                if (container.HasBinding(typeof(TFlow)))
                {
                    var resolved = container.TryResolve<TFlow>();
                    if (resolved != null)
                        return resolved;
                }
            }
            catch
            {
                // new menu container may not have the bind
            }
        }

        try
        {
            var view = BeatSaberMarkupLanguage.BeatSaberUI.CreateViewController<TView>();
            var flowGo = new GameObject(typeof(TFlow).Name);
            var flow = flowGo.AddComponent<TFlow>();
            BindView(flow, view, container);
            owned = true;
            Debug.Log("[MPChat][AvatarColoring] created " + typeof(TFlow).Name + " after menu reload");
            return flow;
        }
        catch (Exception ex)
        {
            Debug.LogError("[MPChat][AvatarColoring] could not create " + typeof(TFlow).Name + ": " + ex);
            Plugin.Log?.Error("[MPChat][AvatarColoring] could not create " + typeof(TFlow).Name + ": " + ex.Message);
            return null;
        }
    }

    private static void BindView<TFlow, TView>(TFlow flow, TView view, DiContainer? container)
        where TFlow : FlowCoordinator
        where TView : ViewController
    {
        if (container != null)
        {
            try
            {
                container.Inject(flow, new object[] { view });
            }
            catch (Exception ex)
            {
                Debug.Log("[MPChat][AvatarColoring] inject " + typeof(TFlow).Name + " failed, setting field: " + ex.Message);
            }
        }

        var field = typeof(TFlow).GetField("_viewController", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null)
            field.SetValue(flow, view);
    }
}
