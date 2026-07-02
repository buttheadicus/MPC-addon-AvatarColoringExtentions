using System;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using MultiplayerChat.Settings;
using UnityEngine.UI;

namespace MultiplayerChat.UI;

[ViewDefinition("MultiplayerChat.UI.AvatarColoringExtensionsSettingsView.bsml")]
public sealed class AvatarColoringExtensionsSettingsViewController : BSMLAutomaticViewController
{
    public event Action? SettingsApplied;

    [UIComponent("EnableAvatarColoringToggle")] private ToggleSetting? _enableToggle;

    private bool _draftEnable;

    [UIValue("EnableAvatarColoringDraft")]
    public bool EnableAvatarColoringDraft
    {
        get => _draftEnable;
        set => _draftEnable = value;
    }

    private void ReloadDraftFromSettings() =>
        _draftEnable = ModSettings.EnableAvatarColoringExtensions;

    [UIAction("#post-parse")]
    private void PostParse()
    {
        ReloadDraftFromSettings();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
        _enableToggle?.ReceiveValue();
    }

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        ReloadDraftFromSettings();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
        _enableToggle?.ReceiveValue();
    }

    [UIAction("ApplyClicked")]
    private void OnApplyClicked()
    {
        var enableTgl = _enableToggle?.GetComponentInChildren<Toggle>(true);
        if (enableTgl != null)
            _draftEnable = enableTgl.isOn;

        ModSettings.EnableAvatarColoringExtensions = _draftEnable;
        SettingsApplied?.Invoke();
    }
}
