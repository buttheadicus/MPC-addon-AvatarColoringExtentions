using System;
using System.Collections;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using MultiplayerChat;
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

    [Inject] private BeatAvatarEditorViewController _beatAvatarEditorViewController = null!;
    [Inject] private AvatarDataModel _avatarDataModel = null!;
    [Inject(Optional = true)] private AvatarNameEntryFlowCoordinator? _avatarNameEntryFlowCoordinator;
    [Inject(Optional = true)] private AvatarLoadListFlowCoordinator? _avatarLoadListFlowCoordinator;

    private GameObject? _toolsRowRoot;
    private static AvatarColoringEditorPatcher? _live;
    private Coroutine? _retry;
    private BeatAvatarEditorViewController? _retryHost;

    internal static void DropLive()
    {
        _live?.StopToolsRowRetry();
        _live = null;
        AvatarColoringMenuFlows.Drop();
    }

    internal static void HandleDidActivate(
        BeatAvatarEditorViewController editor,
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        Debug.Log(
            "[MPChat][AvatarColoring] editor DidActivate first=" + firstActivation +
            " added=" + addedToHierarchy +
            " enabled=" + ModSettings.EnableAvatarColoringExtensions);
        if (!firstActivation && !addedToHierarchy)
            return;
        if (!ModSettings.EnableAvatarColoringExtensions)
            return;

        try
        {
            var patcher = GetOrCreate(editor);
            if (patcher == null)
            {
                Debug.LogError("[MPChat][AvatarColoring] could not construct editor patcher");
                Plugin.Log?.Error("[MPChat][AvatarColoring] could not construct editor patcher");
                if (editor != null)
                    editor.StartCoroutine(RetryAttach(editor));
                return;
            }

            patcher.PostfixBeatAvatarEditorDidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            patcher.StartToolsRowRetry(editor);
        }
        catch (Exception ex)
        {
            Debug.LogError("[MPChat][AvatarColoring] editor UI patch failed: " + ex);
            Plugin.Log?.Error($"[MPChat][AvatarColoring] editor UI patch failed: {ex}");
        }
    }

    internal static void HandleDidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        try
        {
            _live?.PostfixBeatAvatarEditorDidDeactivate(removedFromHierarchy, screenSystemDisabling);
            _live?.StopToolsRowRetry();
        }
        catch (Exception ex)
        {
            Debug.LogError("[MPChat][AvatarColoring] editor DidDeactivate failed: " + ex.Message);
            Plugin.Log?.Error($"[MPChat][AvatarColoring] editor DidDeactivate failed: {ex.Message}");
        }

        if (removedFromHierarchy)
            _live = null;
    }

    private static IEnumerator RetryAttach(BeatAvatarEditorViewController editor)
    {
        var until = Time.unscaledTime + 2.5f;
        while (editor != null && Time.unscaledTime < until)
        {
            var patcher = GetOrCreate(editor);
            if (patcher != null)
            {
                patcher.PostfixBeatAvatarEditorDidActivate(true, true, false);
                patcher.StartToolsRowRetry(editor);
                yield break;
            }

            yield return null;
        }
    }

    private static AvatarColoringEditorPatcher? GetOrCreate(BeatAvatarEditorViewController editor)
    {
        DropIfStale(editor);
        if (_live != null)
            return _live;

        var container = BeatSaberMarkupLanguage.BeatSaberUI.DiContainer;
        if (container != null)
        {
            try
            {
                _live = container.Instantiate<AvatarColoringEditorPatcher>(new object[] { editor });
                _live.BindEditor(editor);
                return _live;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"[MPChat][AvatarColoring] Instantiate with editor arg failed: {ex.Message}");
            }

            try
            {
                _live = container.Instantiate<AvatarColoringEditorPatcher>();
                _live.BindEditor(editor);
                return _live;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MPChat][AvatarColoring] Instantiate editor patcher failed: " + ex);
                Plugin.Log?.Error($"[MPChat][AvatarColoring] Instantiate editor patcher failed: {ex.Message}");
            }
        }

        _live = CreateBare(editor);
        return _live;
    }

    private static void DropIfStale(BeatAvatarEditorViewController editor)
    {
        if (_live == null)
            return;
        var bound = _live._beatAvatarEditorViewController;
        if (bound != null && bound == editor)
            return;

        Debug.Log("[MPChat][AvatarColoring] dropping stale editor patcher after menu reload");
        _live.StopToolsRowRetry();
        _live = null;
        AvatarColoringMenuFlows.Drop();
    }

    private static AvatarColoringEditorPatcher CreateBare(BeatAvatarEditorViewController editor)
    {
        var patcher = new AvatarColoringEditorPatcher();
        patcher.BindEditor(editor);
        Debug.Log("[MPChat][AvatarColoring] built editor patcher without zenject");
        return patcher;
    }

    private void BindEditor(BeatAvatarEditorViewController editor)
    {
        _beatAvatarEditorViewController = editor;
        var model = AvatarColoringLiveObjects.ResolveAvatarDataModel(editor);
        if (model != null)
            _avatarDataModel = model;
        if (_avatarNameEntryFlowCoordinator == null)
            _avatarNameEntryFlowCoordinator = null;
        if (_avatarLoadListFlowCoordinator == null)
            _avatarLoadListFlowCoordinator = null;
    }

    [AffinityPatch(typeof(BeatAvatarEditorViewController), "DidActivate")]
    [AffinityPostfix]
    public void PostfixBeatAvatarEditorDidActivate(bool firstActivation, bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        // stock Apply can leave AvatarData.dat.bak; clear now and again after ~1s
        AvatarDatOperations.ScheduleDeleteAvatarBackup();

        if (!ModSettings.EnableAvatarColoringExtensions)
            return;

        if (_avatarDataModel != null)
            AvatarColoringEditorSession.Attach(_beatAvatarEditorViewController, _avatarDataModel);
        TryEnsureToolsRow();
    }

    [AffinityPatch(typeof(BeatAvatarEditorViewController), "DidDeactivate")]
    [AffinityPostfix]
    public void PostfixBeatAvatarEditorDidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        if (!removedFromHierarchy)
            return;

        AvatarColoringEditorSession.Clear();
        StopToolsRowRetry();

        if (_toolsRowRoot != null)
        {
            Object.Destroy(_toolsRowRoot);
            _toolsRowRoot = null;
        }
    }

    // HandleApplyButtonWasPressed calls AvatarDataModel.SaveAsync -> File.Replace creates .bak
    [AffinityPatch(typeof(BeatAvatarEditorViewController), "HandleApplyButtonWasPressed")]
    [AffinityPostfix]
    public void PostfixBeatAvatarEditorApplyPressed()
    {
        AvatarDatOperations.ScheduleDeleteAvatarBackup();
    }

    private void StartToolsRowRetry(BeatAvatarEditorViewController editor)
    {
        if (_toolsRowRoot != null || editor == null)
            return;
        if (_retry != null && _retryHost != null)
            return;
        StopToolsRowRetry();
        _retryHost = editor;
        _retry = editor.StartCoroutine(RetryToolsRow(editor));
    }

    private void StopToolsRowRetry()
    {
        if (_retry != null && _retryHost != null)
        {
            try
            {
                _retryHost.StopCoroutine(_retry);
            }
            catch
            {
                // editor already gone
            }
        }

        _retry = null;
        _retryHost = null;
    }

    private IEnumerator RetryToolsRow(BeatAvatarEditorViewController editor)
    {
        var until = Time.unscaledTime + 2.5f;
        var loggedMissing = false;
        while (editor != null && Time.unscaledTime < until)
        {
            TryEnsureToolsRow();
            if (_toolsRowRoot != null)
            {
                _retry = null;
                yield break;
            }

            if (!loggedMissing)
            {
                Debug.Log("[MPChat][AvatarColoring] EditPanel not ready, retrying tools row");
                loggedMissing = true;
            }

            yield return null;
        }

        _retry = null;
        if (_toolsRowRoot == null)
        {
            Debug.LogError("[MPChat][AvatarColoring] tools row never built (EditPanel missing)");
            Plugin.Log?.Error("[MPChat][AvatarColoring] tools row never built (EditPanel missing)");
        }
    }

    private void TryEnsureToolsRow()
    {
        if (_toolsRowRoot != null)
            return;
        if (_beatAvatarEditorViewController == null)
            return;
        try
        {
            BuildToolsRow();
        }
        catch (Exception ex)
        {
            Debug.LogError("[MPChat][AvatarColoring] Editor UI failed: " + ex);
            Plugin.Log?.Error($"[MPChat][AvatarColoring] Editor UI failed: {ex}");
        }
    }

    private void BuildToolsRow()
    {
        var editPanel = EditAvatarViewControllerPatcher.ResolveEditPanel(_beatAvatarEditorViewController);
        if (editPanel == null)
            return;

        var existing = editPanel.Find("MPChatAvatarToolsRow");
        if (existing != null)
        {
            _toolsRowRoot = existing.gameObject;
            Debug.Log("[MPChat][AvatarColoring] tools row already on editor");
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
        Debug.Log("[MPChat][AvatarColoring] tools row built");
        Plugin.Log?.Error("[MPChat][AvatarColoring] tools row built");
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
        // arrow-only chrome under RandomizePanel
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
        try
        {
            if (_avatarNameEntryFlowCoordinator == null)
                _avatarNameEntryFlowCoordinator = AvatarColoringMenuFlows.ResolveName();
            var nameFlow = _avatarNameEntryFlowCoordinator;
            if (nameFlow == null)
            {
                Debug.LogError("[MPChat][AvatarColoring] save flow missing after menu reload");
                Plugin.Log?.Error("[MPChat][AvatarColoring] save flow missing after menu reload");
                return;
            }

            var mainFlow = BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator;
            var topFlow = FlowCoordinatorHelper.GetTopFlowCoordinator(mainFlow);
            if (topFlow == null)
            {
                Debug.LogError("[MPChat][AvatarColoring] save: no top flow coordinator");
                return;
            }

            nameFlow.ParentFlow = topFlow;
            topFlow.PresentFlowCoordinator(nameFlow);
        }
        catch (Exception ex)
        {
            Debug.LogError("[MPChat][AvatarColoring] save present failed: " + ex);
            Plugin.Log?.Error("[MPChat][AvatarColoring] save present failed: " + ex.Message);
        }
    }

    private void OnLoadClicked()
    {
        try
        {
            if (_avatarLoadListFlowCoordinator == null)
                _avatarLoadListFlowCoordinator = AvatarColoringMenuFlows.ResolveLoad();
            var loadFlow = _avatarLoadListFlowCoordinator;
            if (loadFlow == null)
            {
                Debug.LogError("[MPChat][AvatarColoring] load flow missing after menu reload");
                Plugin.Log?.Error("[MPChat][AvatarColoring] load flow missing after menu reload");
                return;
            }

            var mainFlow = BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator;
            var topFlow = FlowCoordinatorHelper.GetTopFlowCoordinator(mainFlow);
            if (topFlow == null)
            {
                Debug.LogError("[MPChat][AvatarColoring] load: no top flow coordinator");
                return;
            }

            loadFlow.ParentFlow = topFlow;
            topFlow.PresentFlowCoordinator(loadFlow);
        }
        catch (Exception ex)
        {
            Debug.LogError("[MPChat][AvatarColoring] load present failed: " + ex);
            Plugin.Log?.Error("[MPChat][AvatarColoring] load present failed: " + ex.Message);
        }
    }
}
