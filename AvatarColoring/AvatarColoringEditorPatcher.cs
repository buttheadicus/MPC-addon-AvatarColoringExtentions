using System;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using MultiplayerChat.AvatarExtras.Patches.Menu;
using MultiplayerChat.Settings;
using MultiplayerChat.UI;
using MultiplayerChat.Core;
using SiraUtil.Affinity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;

namespace MultiplayerChat.AvatarColoring;

public sealed class AvatarColoringEditorPatcher : IAffinity
{
    public static Vector2 AvatarToolsRowAnchoredPixelOffset = new Vector2(-64f, 7f);

    public static float AvatarToolsRowSpacing = 6f;

    public static float AvatarToolsButtonPreferredWidth = 26f;

    public static float AvatarToolsButtonPreferredHeight = 8f;

    public static float AvatarToolsButtonMinWidth = AvatarToolsButtonPreferredWidth;

    public static float AvatarToolsButtonFontSize = -1f;

    [Inject] private readonly BeatAvatarEditorViewController _beatAvatarEditorViewController = null!;
    [Inject] private readonly AvatarDataModel _avatarDataModel = null!;
    [Inject] private readonly AvatarNameEntryFlowCoordinator _avatarNameEntryFlowCoordinator = null!;
    [Inject] private readonly AvatarLoadListFlowCoordinator _avatarLoadListFlowCoordinator = null!;

    private GameObject? _toolsRowRoot;

    [AffinityPatch(typeof(BeatAvatarEditorViewController), "DidActivate")]
    [AffinityPostfix]
    public void PostfixBeatAvatarEditorDidActivate(bool firstActivation, bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        AvatarDatOperations.TryDeleteAvatarBackup();

        if (!ModSettings.EnableAvatarColoringExtensions)
            return;

        AvatarColoringEditorSession.Attach(_beatAvatarEditorViewController, _avatarDataModel);

        if (_toolsRowRoot != null)
            return;

        try
        {
            BuildToolsRow();
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Error($"[MPChat][AvatarColoring] Editor UI failed: {ex.Message}");
        }
    }

    [AffinityPatch(typeof(BeatAvatarEditorViewController), "DidDeactivate")]
    [AffinityPostfix]
    public void PostfixBeatAvatarEditorDidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        if (!removedFromHierarchy)
            return;

        AvatarColoringEditorSession.Clear();

        if (_toolsRowRoot != null)
        {
            Object.Destroy(_toolsRowRoot);
            _toolsRowRoot = null;
        }
    }

    private void BuildToolsRow()
    {
        var editPanel = EditAvatarViewControllerPatcher.ResolveEditPanel(_beatAvatarEditorViewController);
        if (editPanel == null)
        {
            MultiplayerChat.Plugin.Log?.Warn("[MPChat][AvatarColoring] EditPanel not found; avatar tools row skipped.");
            return;
        }

        var randomizePanel = editPanel.Find("RandomizePanel")
                             ?? EditAvatarViewControllerPatcher.FindChildRecursive(editPanel, "RandomizePanel")
                             ?? EditAvatarViewControllerPatcher.FindChildRecursive(_beatAvatarEditorViewController.transform,
                                 "RandomizePanel");

        var buttonTemplate = ResolveToolsButtonTemplate(editPanel, randomizePanel, _beatAvatarEditorViewController.transform);

        _toolsRowRoot = new GameObject("MPChatAvatarToolsRow", typeof(RectTransform));
        var rt = (RectTransform)_toolsRowRoot.transform;
        rt.SetParent(editPanel, false);

        if (randomizePanel != null)
            rt.SetSiblingIndex(randomizePanel.GetSiblingIndex());
        else
            rt.SetAsLastSibling();

        rt.localScale = Vector3.one;
        rt.anchoredPosition = AvatarToolsRowAnchoredPixelOffset;

        var h = _toolsRowRoot.AddComponent<HorizontalLayoutGroup>();
        h.spacing = AvatarToolsRowSpacing;
        h.padding = new RectOffset(8, 8, 4, 4);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childForceExpandHeight = false;
        h.childForceExpandWidth = false;
        h.childControlHeight = true;
        h.childControlWidth = true;

        var leRow = _toolsRowRoot.AddComponent<LayoutElement>();
        leRow.minHeight = 13f;
        leRow.preferredHeight = 13f;
        leRow.flexibleHeight = 0f;

        AddToolButton(_toolsRowRoot.transform, buttonTemplate, "Randomize", OnRandomizeClicked);
        AddToolButton(_toolsRowRoot.transform, buttonTemplate, "Save", OnSaveClicked);
        AddToolButton(_toolsRowRoot.transform, buttonTemplate, "Load", OnLoadClicked);

        if (editPanel is RectTransform editRt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(editRt);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        rt.anchoredPosition = AvatarToolsRowAnchoredPixelOffset;
    }

    private static Button? ResolveToolsButtonTemplate(Transform editPanel, Transform? randomizePanel, Transform vcRoot)
    {
        var apply = FindFirstButtonWhoseNameContains(vcRoot, "Apply");
        if (apply != null && !IsLikelyUndoOrStepButton(apply.gameObject.name))
            return apply;

        var random = FindFirstButtonWhoseNameContains(randomizePanel, "Random", "Dice", "Shuffle");
        if (random != null)
            return random;

        var best = PickLargestNonUndoButton(randomizePanel);
        if (best != null)
            return best;

        best = PickLargestNonUndoButton(editPanel);
        if (best != null)
            return best;

        return PickLargestNonUndoButton(vcRoot);
    }

    private static Button? FindFirstButtonWhoseNameContains(Transform? scope, params string[] needles)
    {
        if (scope == null)
            return null;
        foreach (var b in scope.GetComponentsInChildren<Button>(true))
        {
            var n = b.gameObject.name;
            foreach (var needle in needles)
            {
                if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return b;
            }
        }

        return null;
    }

    private static Button? PickLargestNonUndoButton(Transform? scope)
    {
        if (scope == null)
            return null;
        Button? best = null;
        var bestArea = 0f;
        foreach (var b in scope.GetComponentsInChildren<Button>(true))
        {
            if (IsLikelyUndoOrStepButton(b.gameObject.name))
                continue;
            var rt = b.transform as RectTransform;
            if (rt == null)
                continue;
            var area = Mathf.Abs(rt.rect.width * rt.rect.height);
            if (!(area > bestArea))
                continue;
            bestArea = area;
            best = b;
        }

        return best;
    }

    private static bool IsLikelyUndoOrStepButton(string gameObjectName)
    {
        var n = gameObjectName.ToLowerInvariant();
        if (n.Contains("undo") || n.Contains("redo"))
            return true;
        if (n.Contains("increment") || n.Contains("decrement"))
            return true;
        if (n.Contains("step") && !n.Contains("random"))
            return true;
        if (n.Contains("prevbutton") || n.Contains("nextbutton"))
            return true;
        // Arrow-only chrome under RandomizePanel
        return n.Contains("<") || n.Contains(">");
    }

    private static void AddToolButton(Transform row, Button? template, string label, UnityEngine.Events.UnityAction onClick)
    {
        if (template != null)
        {
            var go = Object.Instantiate(template.gameObject, row, false);
            go.name = "MPChatTool_" + label;

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(onClick);
                btn.interactable = true;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
            }

            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = true;

            foreach (var tmp in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.text = label;
                tmp.raycastTarget = false;
                if (AvatarToolsButtonFontSize >= 0f)
                    tmp.fontSize = AvatarToolsButtonFontSize;
            }

            var bw = AvatarToolsButtonPreferredWidth;
            var bh = AvatarToolsButtonPreferredHeight;
            var bmin = Mathf.Min(AvatarToolsButtonMinWidth, bw);

            var le = go.GetComponent<LayoutElement>();
            if (le == null)
                le = go.AddComponent<LayoutElement>();
            le.minWidth = bmin;
            le.preferredWidth = bw;
            le.flexibleWidth = 0f;
            le.minHeight = bh;
            le.preferredHeight = bh;
            le.flexibleHeight = 0f;

            if (go.transform is RectTransform crt)
            {
                crt.anchorMin = new Vector2(0f, 0.5f);
                crt.anchorMax = new Vector2(0f, 0.5f);
                crt.pivot = new Vector2(0f, 0.5f);
                crt.anchoredPosition = Vector2.zero;
                crt.sizeDelta = new Vector2(bw, bh);
            }
            return;
        }

        MakeFallbackButton(row, label, onClick);
    }

    private static void MakeFallbackButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var fw = AvatarToolsButtonPreferredWidth;
        var fh = AvatarToolsButtonPreferredHeight;
        var fmin = Mathf.Min(AvatarToolsButtonMinWidth, fw);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = fmin;
        le.preferredWidth = fw;
        le.flexibleWidth = 0f;
        le.minHeight = fh;
        le.preferredHeight = fh;
        le.flexibleHeight = 0f;

        if (go.transform is RectTransform frt)
        {
            frt.anchorMin = new Vector2(0f, 0.5f);
            frt.anchorMax = new Vector2(0f, 0.5f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.sizeDelta = new Vector2(fw, fh);
        }

        var img = go.AddComponent<Image>();
        img.sprite = BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite;
        img.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = true;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(onClick);

        var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(go.transform, false);
        var txtRt = (RectTransform)txtGo.transform;
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
        var tmp = txtGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = AvatarToolsButtonFontSize >= 0f ? AvatarToolsButtonFontSize : 3.4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = label;
        tmp.raycastTarget = false;
    }

    private void OnRandomizeClicked()
    {
        if (!AvatarDatOperations.RandomizeAvatarDatFile())
            return;

        AvatarColoringEditorSession.RefreshAfterAvatarDatChangedOnDisk();
    }

    private void OnSaveClicked()
    {
        var mainFlow = BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator;
        var topFlow = FlowCoordinatorHelper.GetTopFlowCoordinator(mainFlow);
        _avatarNameEntryFlowCoordinator.ParentFlow = topFlow;
        topFlow.PresentFlowCoordinator(_avatarNameEntryFlowCoordinator);
    }

    private void OnLoadClicked()
    {
        var mainFlow = BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator;
        var topFlow = FlowCoordinatorHelper.GetTopFlowCoordinator(mainFlow);
        _avatarLoadListFlowCoordinator.ParentFlow = topFlow;
        topFlow.PresentFlowCoordinator(_avatarLoadListFlowCoordinator);
    }
}
