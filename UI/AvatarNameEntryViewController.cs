using System;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using TMPro;
using UnityEngine.UI;

namespace MultiplayerChat.UI;

[ViewDefinition("MultiplayerChat.UI.AvatarNameEntryView.bsml")]
public sealed class AvatarNameEntryViewController : BSMLAutomaticViewController
{
    public event Action<string>? Committed;
    public event Action? Cancelled;

    [UIComponent("AvatarNameInput")] private StringSetting? _nameInput;

    private TMP_InputField? _tmp;
    private Button? _openKeyboardBtn;

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        TryBindField();
        if (_nameInput != null)
            _nameInput.Text = "";
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
    }

    protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        UnbindField();
        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
    }

    private void TryBindField()
    {
        UnbindField();
        if (_nameInput == null)
            return;
        _openKeyboardBtn = _nameInput.GetComponentInChildren<Button>(true);
        if (_openKeyboardBtn != null)
            _openKeyboardBtn.onClick.AddListener(OnBarPressed);
        _tmp = _nameInput.GetComponentInChildren<TMP_InputField>(true);
        if (_tmp != null)
            _tmp.onSubmit.AddListener(OnTmpSubmit);
    }

    private void UnbindField()
    {
        if (_openKeyboardBtn != null)
        {
            _openKeyboardBtn.onClick.RemoveListener(OnBarPressed);
            _openKeyboardBtn = null;
        }

        if (_tmp != null)
        {
            _tmp.onSubmit.RemoveListener(OnTmpSubmit);
            _tmp = null;
        }
    }

    private void OnBarPressed()
    {
        // opening the keyboard is handled by BSML string-setting; keep hook for parity with chat keyboard pattern.
    }

    private void OnTmpSubmit(string _)
    {
        SaveClicked();
    }

    [UIAction("SaveClicked")]
    private void SaveClicked()
    {
        var raw = (_nameInput?.Text ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
        {
            CancelClicked();
            return;
        }

        Committed?.Invoke(raw);
    }

    [UIAction("CancelClicked")]
    private void CancelClicked() => Cancelled?.Invoke();
}
