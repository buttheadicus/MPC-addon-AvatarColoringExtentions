using HMUI;
using Zenject;

namespace MultiplayerChat.UI;

public sealed class AvatarColoringExtensionsSettingsFlowCoordinator : FlowCoordinator
{
    [Inject] private readonly AvatarColoringExtensionsSettingsViewController _view = null!;

    public HMUI.FlowCoordinator? ParentFlow { get; set; }

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            showBackButton = true;
            SetTitle("Avatar Coloring");
            ProvideInitialViewControllers(_view);
        }

        if (addedToHierarchy)
            _view.SettingsApplied += OnSettingsApplied;
    }

    protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        if (removedFromHierarchy)
            _view.SettingsApplied -= OnSettingsApplied;
    }

    private void OnSettingsApplied() => Dismiss();

    protected override void BackButtonWasPressed(ViewController topViewController) => Dismiss();

    private void Dismiss()
    {
        if (ParentFlow != null)
            ParentFlow.DismissFlowCoordinator(this);
        else
            BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
    }
}
