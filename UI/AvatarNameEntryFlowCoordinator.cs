using System;
using HMUI;
using MultiplayerChat.AvatarColoring;
using Zenject;

namespace MultiplayerChat.UI;

public sealed class AvatarNameEntryFlowCoordinator : FlowCoordinator
{
    [Inject] private readonly AvatarNameEntryViewController _viewController = null!;

    public HMUI.FlowCoordinator? ParentFlow { get; set; }

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            SetTitle("SAVE AVATAR");
            showBackButton = true;
            ProvideInitialViewControllers(_viewController);
        }

        if (addedToHierarchy)
        {
            _viewController.Committed += OnCommitted;
            _viewController.Cancelled += OnCancelled;
            _viewController.SaveToExisting += OnSaveToExisting;
        }
    }

    protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        if (removedFromHierarchy)
        {
            _viewController.Committed -= OnCommitted;
            _viewController.Cancelled -= OnCancelled;
            _viewController.SaveToExisting -= OnSaveToExisting;
        }
    }

    private void OnCommitted(string name)
    {
        AvatarDatOperations.CopyAvatarDatToPreset(name);
        Dismiss();
    }

    private void OnCancelled() => Dismiss();

    private void OnSaveToExisting()
    {
        var load = AvatarColoringMenuFlows.ResolveLoad();
        if (load == null)
            return;
        load.ParentFlow = this;
        load.BeginSaveOver();
        PresentFlowCoordinator(load);
    }

    protected override void BackButtonWasPressed(ViewController topViewController) => Dismiss();

    internal void Close() => Dismiss();

    private void Dismiss()
    {
        if (ParentFlow != null)
            ParentFlow.DismissFlowCoordinator(this);
        else
            BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
    }
}
