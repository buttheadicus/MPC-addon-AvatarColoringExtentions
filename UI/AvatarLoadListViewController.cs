using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using MultiplayerChat.AvatarColoring;
using MultiplayerChat.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerChat.UI;

[ViewDefinition("MultiplayerChat.UI.AvatarLoadListView.bsml")]
public sealed class AvatarLoadListViewController : BSMLAutomaticViewController
{
    private const int SlotsPerRow = 7;

    public event Action<string>? PresetSelected;

    [UIComponent("PresetListRoot")] private RectTransform? _presetRoot;
    [UIComponent("DeleteModeButton")] private Button? _deleteModeButton;

    private bool _deleteMode;

    [UIAction("#post-parse")]
    private void PostParse()
    {
        ResolvePresetRoot();
        RefreshDeleteModeUi();
        RebuildList();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
    }

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        if (addedToHierarchy)
            _deleteMode = false;
        ResolvePresetRoot();
        RefreshDeleteModeUi();
        RebuildList();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
    }

    [UIAction("DeleteModeToggleClicked")]
    private void DeleteModeToggleClicked()
    {
        _deleteMode = !_deleteMode;
        RefreshDeleteModeUi();
        RebuildList();
    }

    private void RefreshDeleteModeUi()
    {
        ApplyDeleteButtonColors();
        RefreshDeleteModeButtonLabel();
    }

    private void ApplyDeleteButtonColors()
    {
        if (_deleteModeButton == null)
            return;
        var colors = _deleteModeButton.colors;
        if (_deleteMode)
        {
            var b = new Color(0.2f, 0.45f, 0.95f, 1f);
            colors.normalColor = b;
            colors.highlightedColor = new Color(0.35f, 0.58f, 1f, 1f);
            colors.pressedColor = new Color(0.12f, 0.32f, 0.78f, 1f);
            colors.selectedColor = b;
        }
        else
        {
            var k = new Color(0.06f, 0.06f, 0.06f, 1f);
            colors.normalColor = k;
            colors.highlightedColor = new Color(0.12f, 0.12f, 0.12f, 1f);
            colors.pressedColor = new Color(0.04f, 0.04f, 0.04f, 1f);
            colors.selectedColor = k;
        }

        colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.06f;
        _deleteModeButton.colors = colors;
    }

    private void RefreshDeleteModeButtonLabel()
    {
        if (_deleteModeButton == null)
            return;
        var tmp = _deleteModeButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp == null)
            return;
        if (_deleteMode)
        {
            tmp.text =
                "DELETE MODE ON. TAP ON AVATAR NAME TO DELETE THE AVATAR. TAP ON THIS BUTTON AGAIN TO TURN DELETE MODE OFF.";
            tmp.fontSize = 2.35f;
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            tmp.text = "Delete mode is off. Tap on this button to turn it on!";
            tmp.fontSize = 3.2f;
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.Center;
        }
    }

    private void ResolvePresetRoot()
    {
        if (_presetRoot != null)
            return;
        var found = transform.Find("PresetListRoot");
        if (found is RectTransform rt)
            _presetRoot = rt;
    }

    private void RebuildList()
    {
        ResolvePresetRoot();
        if (_presetRoot == null)
        {
            MultiplayerChat.Plugin.Log?.Warn("[MPChat][AvatarLoad] PresetListRoot not resolved; load list is empty in UI.");
            return;
        }

        for (var i = _presetRoot.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(_presetRoot.GetChild(i).gameObject);

        var names = new List<string>(AvatarDatOperations.ListPresetNames());
        MultiplayerChat.Plugin.Log?.Info(
            $"[MPChat][AvatarLoad] {names.Count} presets under {ChatIdFilePaths.AvatarStorageDirectoryPath}");

        var deleteMode = _deleteMode;
        for (var i = 0; i < names.Count; i += SlotsPerRow)
        {
            var row = new GameObject("PresetRow", typeof(RectTransform));
            row.transform.SetParent(_presetRoot, false);
            var rowRt = (RectTransform)row.transform;
            rowRt.localScale = Vector3.one;

            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 10f;
            rowLe.preferredHeight = 10f;
            rowLe.flexibleHeight = 0f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 0.5f;
            h.padding = new RectOffset(0, 0, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;
            h.childControlHeight = true;
            h.childControlWidth = true;

            var end = Math.Min(i + SlotsPerRow, names.Count);
            for (var j = i; j < end; j++)
            {
                var name = names[j];
                AddPresetSlot(row.transform, name, deleteMode, OnPresetSlotClicked);
            }
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_presetRoot);
        var p = _presetRoot.parent as RectTransform;
        while (p != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(p);
            p = p.parent as RectTransform;
        }
    }

    private void OnPresetSlotClicked(string presetFileName, bool asDelete)
    {
        if (asDelete)
        {
            if (!AvatarDatOperations.DeletePresetFromStorage(presetFileName))
                return;
            MultiplayerChat.Plugin.Log?.Info($"[MPChat][AvatarLoad] Deleted preset: {presetFileName}");
            RebuildList();
            return;
        }

        MultiplayerChat.Plugin.Log?.Debug($"[MPChat][AvatarLoad] Selected preset: {presetFileName}");
        PresetSelected?.Invoke(presetFileName);
    }

    private static void AddPresetSlot(Transform row, string presetFileName, bool deleteMode, Action<string, bool> onClick)
    {
        var go = new GameObject($"Preset_{presetFileName}", typeof(RectTransform));
        go.transform.SetParent(row, false);

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 0f;
        le.minWidth = 14f;
        le.preferredWidth = 22f;
        le.minHeight = 9f;
        le.preferredHeight = 9f;

        var img = go.AddComponent<Image>();
        img.sprite = BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite;
        img.color = new Color(0.12f, 0.12f, 0.18f, 0.92f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var capture = presetFileName;
        btn.onClick.AddListener(() => onClick(capture, deleteMode));

        var txtGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(go.transform, false);
        var txtRt = (RectTransform)txtGo.transform;
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(3f, 1f);
        txtRt.offsetMax = new Vector2(-3f, -1f);
        var tmp = txtGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 2.8f;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.text = presetFileName;
        tmp.raycastTarget = false;
    }
}
