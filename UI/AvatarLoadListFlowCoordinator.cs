using System;
using HMUI;
using MultiplayerChat.AvatarColoring;
using Zenject;

namespace MultiplayerChat.UI;

// lists avatar presets from Avatar Storage and replaces AvatarData.dat when one is picked.
public sealed class AvatarLoadListFlowCoordinator : FlowCoordinator
{
    [Inject] private readonly AvatarLoadListViewController _viewController = null!;

    public HMUI.FlowCoordinator? ParentFlow { get; set; }

    private bool _saveOverMode;

    internal void BeginSaveOver() => _saveOverMode = true;

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        SetTitle(_saveOverMode ? "Save to avatar" : "Load avatar");
        if (firstActivation)
        {
            showBackButton = true;
            ProvideInitialViewControllers(_viewController);
        }

        _viewController.SetSaveOverMode(_saveOverMode);

        if (addedToHierarchy)
            _viewController.PresetSelected += OnPresetSelected;
    }

    protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        if (removedFromHierarchy)
        {
            _viewController.PresetSelected -= OnPresetSelected;
            _saveOverMode = false;
            _viewController.SetSaveOverMode(false);
        }
    }

    private void OnPresetSelected(string presetFileName)
    {
        if (_saveOverMode)
        {
            if (!AvatarDatOperations.CopyAvatarDatToPreset(presetFileName))
                return;
            var parent = ParentFlow;
            Dismiss();
            if (parent is AvatarNameEntryFlowCoordinator nameFlow)
                nameFlow.Close();
            return;
        }

        if (!AvatarDatOperations.ApplyPresetFromStorage(presetFileName))
            return;

        AvatarColoringEditorSession.RefreshAfterAvatarDatChangedOnDisk();
        Dismiss();
    }

    protected override void BackButtonWasPressed(ViewController topViewController) => Dismiss();

    private void Dismiss()
    {
        if (ParentFlow != null)
            ParentFlow.DismissFlowCoordinator(this);
        else
            BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
    }
}
