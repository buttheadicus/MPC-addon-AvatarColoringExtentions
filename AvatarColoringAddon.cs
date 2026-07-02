using System;
using MultiplayerChat.Contracts;

namespace MultiplayerChat.Addon.AvatarColoring;

[MpChatAddon(AddonIds.AvatarColoring)]
public sealed class AvatarColoringAddon : IMpChatAddon, IMpChatSettingsPage
{
    private IMpChatHost? _host;

    public string Id => AddonIds.AvatarColoring;

    public string DisplayName => "Avatar Coloring Extensions";

    public Version Version => new(1, 0, 0);

    string IMpChatSettingsPage.AddonId => Id;

    public string PageTitle => "Avatar Coloring";

    public string SettingsCategory => "Addons";

    public void OnLoad(IMpChatHost host)
    {
        _host = host;
        host.RegisterSettingsPage(this);
        host.RegisterSettingsPresenter(
            Id,
            typeof(MultiplayerChat.UI.AvatarColoringExtensionsSettingsFlowCoordinator),
            "Avatar Coloring Extensions");
        host.SetCapability(AddonCapability.AvatarColoring, true);
        host.PatchAllHarmony(Id, typeof(AvatarColoringAddon).Assembly);
    }

    public void OnUnload()
    {
        _host?.UnpatchHarmony(Id);
        _host?.UnregisterSettingsPresenter(Id);
        _host?.UnregisterSettingsPage(this);
        _host?.SetCapability(AddonCapability.AvatarColoring, false);
        _host = null;
    }
}
