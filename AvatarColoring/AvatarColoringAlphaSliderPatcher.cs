we using System;
using System.Collections;
using System.Globalization;
using BeatSaber.BeatAvatarAdapter.AvatarEditor;
using BeatSaber.BeatAvatarSDK;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Tags;
using BeatSaberMarkupLanguage.Tags.Settings;
using HMUI;
using IPA.Utilities;
using MultiplayerChat;
using MultiplayerChat.Settings;
using SiraUtil.Affinity;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Zenject;

namespace MultiplayerChat.AvatarColoring;

// RGBA controls for the stock color editor (not here... but in the coed!!!!!! wowza!!).
public sealed class AvatarColoringAlphaSliderPatcher : IAffinity, IInitializable, IDisposable
{
    private const float SliderRgbNarrowMin = -100f;

    private const float SliderRgbNarrowMax = 100f;

    private const float SliderRgbWideMin = -600f;

    private const float SliderRgbWideMax = 600f;

    private const float SliderAlphaMin = -600f;

    private const float SliderAlphaMax = 600f;

    private const float ColumnPreferredWidth = 64f;

    private const float ColumnMinWidth = 56f;

    private const float SliderVisualScale = 1.1f;

    // whole r/g/b/a column position in anchored pixels after layout parents this rect.
    public static Vector2 RgbaSliderStackAnchoredPixelOffset = new Vector2(24f, 52f);

    // nudge the RGB mode / direct-entry toggle row (horizontal layout) inside the column.
    public static Vector2 RgbOptionsRowAnchoredPixelOffset = new Vector2(-13f, 7f);

    // -1 keeps auto order; otherwise set sibling index on the column under the parent row.
    public static int RgbaStackSiblingIndex = -1;

    // nudge r/g/b/a labels relative to each slider row.
    public static Vector2 RgbaChannelLetterAnchoredPixelNudge = new Vector2(10f, 0f);

    internal static AvatarColoringAlphaSliderPatcher? Instance { get; private set; }

    // hsv get_color is always a=1. keep the last slider/AvatarData alpha across Apply and HSV callbacks.
    private static Color? _lastKnownRgba;

    internal static void RememberLiveColor(Color color) => _lastKnownRgba = color;

    internal static bool IsApplyingFromOurSliders => Instance != null && Instance._applyingSlidersFromCode;

    internal static void SyncFromColorWheel(Color color)
    {
        if (!ModSettings.EnableAvatarColoringExtensions)
            return;
        if (AvatarColorEditorDraft.IsCommitBypassActive)
            return;
        if (IsApplyingFromOurSliders)
            return;

        RememberLiveColor(color);
        Instance?.ApplyChannelDisplayValues(color);
    }

    internal static void MergeLiveAlpha(ref Color color)
    {
        if (!ModSettings.EnableAvatarColoringExtensions)
            return;
        // revert/commit bypass already has the color it wants, including snapshot alpha
        if (AvatarColorEditorDraft.IsCommitBypassActive)
            return;
        // hsv wheel should pass through as-is; only keep our a when we pushed from the rgba row
        if (!IsApplyingFromOurSliders)
            return;
        if (!TryGetLiveAlpha(out var a))
            return;
        color.a = a;
    }

    internal static bool TryGetLiveAlpha(out float a)
    {
        if (TryGetCommittedEditColor(out var c))
        {
            a = c.a;
            return true;
        }

        a = 1f;
        return false;
    }

    internal static bool TryGetCommittedEditColor(out Color color)
    {
        if (Instance != null)
        {
            color = Instance.ReadMergedLiveChannelColor();
            return true;
        }

        if (_lastKnownRgba.HasValue)
        {
            color = _lastKnownRgba.Value;
            return true;
        }

        color = default;
        return false;
    }

    internal static void ClearCommittedColor() => _lastKnownRgba = null;

    [Inject] private BeatAvatarEditorViewController _beatAvatarEditorViewController = null!;

    [Inject] private AvatarDataModel _avatarDataModel = null!;

    [Inject] private EditAvatarColorViewController _editColorViewController = null!;

    private GameObject? _alphaColumnRoot;

    private GameObject? _channelsStackRoot;

    private ToggleSetting? _rgbWideToggleSetting;

    private ToggleSetting? _directEntryToggleSetting;

    private RangeValuesTextSlider? _rHmSlider;

    private RangeValuesTextSlider? _gHmSlider;

    private RangeValuesTextSlider? _bHmSlider;

    private RangeValuesTextSlider? _alphaHmSlider;

    private StringSetting? _rStringSetting;

    private StringSetting? _gStringSetting;

    private StringSetting? _bStringSetting;

    private StringSetting? _aStringSetting;

    private bool _applyingSlidersFromCode;

    private Coroutine? _rgbRowAnchoredNudgeCoroutine;

    private MonoBehaviour? _rgbRowNudgeCoroutineHost;

    public void Initialize()
    {
        if (Instance == this)
            return;
        if (Instance != null && Instance != this)
        {
            try
            {
                Instance.Dispose();
            }
            catch
            {
                // previous menu VC already destroyed
            }
        }

        Instance = this;
        if (_beatAvatarEditorViewController != null)
            _beatAvatarEditorViewController.didRequestColorChangeEvent += HandleDidRequestColorChange;
        if (_editColorViewController != null)
        {
            _editColorViewController.didChangeColorEvent += HandleExternalColorChanged;
            _editColorViewController.didFinishEvent += HandleEditAvatarColorDidFinish;
        }
    }

    internal static void DropLive()
    {
        if (Instance == null)
            return;
        try
        {
            Instance.Dispose();
        }
        catch
        {
            Instance = null;
        }
    }

    internal static void HandleDidActivate(
        EditAvatarColorViewController colorEditor,
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        Debug.Log(
            "[MPChat][AvatarColoring] color editor DidActivate first=" + firstActivation +
            " added=" + addedToHierarchy +
            " enabled=" + ModSettings.EnableAvatarColoringExtensions);
        if (!ModSettings.EnableAvatarColoringExtensions)
            return;
        if (!firstActivation && !addedToHierarchy)
            return;

        try
        {
            var patcher = GetOrCreate(colorEditor);
            if (patcher == null)
            {
                Plugin.Log?.Error("[MPChat][AvatarColoring] could not construct color slider patcher");
                if (colorEditor != null)
                    colorEditor.StartCoroutine(RetryAttach(colorEditor));
                return;
            }

            patcher.PostfixEditColorDidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        }
        catch (Exception ex)
        {
            Plugin.Log?.Error($"[MPChat][AvatarColoring] color editor UI patch failed: {ex}");
        }
    }

    internal static void HandleDidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        try
        {
            Instance?.PostfixEditColorDidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }
        catch (Exception ex)
        {
            Plugin.Log?.Error($"[MPChat][AvatarColoring] color editor DidDeactivate failed: {ex.Message}");
        }
    }

    private static IEnumerator RetryAttach(EditAvatarColorViewController colorEditor)
    {
        var until = Time.unscaledTime + 2.5f;
        while (colorEditor != null && Time.unscaledTime < until)
        {
            var patcher = GetOrCreate(colorEditor);
            if (patcher != null)
            {
                patcher.PostfixEditColorDidActivate(true, true, false);
                yield break;
            }

            yield return null;
        }
    }

    private static AvatarColoringAlphaSliderPatcher? GetOrCreate(EditAvatarColorViewController colorEditor)
    {
        DropIfStale(colorEditor);
        if (Instance != null)
            return Instance;

        var container = BeatSaberMarkupLanguage.BeatSaberUI.DiContainer;
        if (container != null)
        {
            try
            {
                var patcher = container.Instantiate<AvatarColoringAlphaSliderPatcher>(new object[] { colorEditor });
                patcher.BindColorEditor(colorEditor);
                patcher.Initialize();
                return patcher;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"[MPChat][AvatarColoring] Instantiate color patcher with arg failed: {ex.Message}");
            }

            try
            {
                var patcher = container.Instantiate<AvatarColoringAlphaSliderPatcher>();
                patcher.BindColorEditor(colorEditor);
                patcher.Initialize();
                return patcher;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error($"[MPChat][AvatarColoring] Instantiate color patcher failed: {ex.Message}");
                Debug.LogError("[MPChat][AvatarColoring] Instantiate color patcher failed: " + ex);
            }
        }

        var bare = CreateBare(colorEditor);
        bare.Initialize();
        return bare;
    }

    private static void DropIfStale(EditAvatarColorViewController colorEditor)
    {
        if (Instance == null)
            return;
        var bound = Instance._editColorViewController;
        if (bound != null && bound == colorEditor)
            return;

        Debug.Log("[MPChat][AvatarColoring] dropping stale color patcher after menu reload");
        try
        {
            Instance.Dispose();
        }
        catch
        {
            Instance = null;
        }
    }

    private static AvatarColoringAlphaSliderPatcher CreateBare(EditAvatarColorViewController colorEditor)
    {
        var patcher = new AvatarColoringAlphaSliderPatcher();
        patcher.BindColorEditor(colorEditor);
        Debug.Log("[MPChat][AvatarColoring] built color patcher without zenject");
        return patcher;
    }

    private void BindColorEditor(EditAvatarColorViewController colorEditor)
    {
        _editColorViewController = colorEditor;
        var editor = AvatarColoringLiveObjects.ResolveBeatAvatarEditor();
        if (editor != null)
            _beatAvatarEditorViewController = editor;
        var model = AvatarColoringLiveObjects.ResolveAvatarDataModel(editor);
        if (model != null)
            _avatarDataModel = model;
    }

    private void HandleEditAvatarColorDidFinish(bool rawFinishParameter)
    {
        var applied = AvatarColorEditorDraft.InterpretDidFinishAsApplied(rawFinishParameter);
        // color Apply can sit in front of a later disk save; sweep bak either way
        if (applied)
            AvatarDatOperations.ScheduleDeleteAvatarBackup();

        if (!ModSettings.EnableAvatarColoringExtensions)
            return;
        if (_editColorViewController == null || _beatAvatarEditorViewController == null)
            return;

        AvatarColorEditorDraft.HandleDidFinish(_editColorViewController, _beatAvatarEditorViewController, applied);
    }

    public void Dispose()
    {
        if (Instance == this)
            Instance = null;
        try
        {
            if (_beatAvatarEditorViewController != null)
                _beatAvatarEditorViewController.didRequestColorChangeEvent -= HandleDidRequestColorChange;
            if (_editColorViewController != null)
            {
                _editColorViewController.didChangeColorEvent -= HandleExternalColorChanged;
                _editColorViewController.didFinishEvent -= HandleEditAvatarColorDidFinish;
            }
        }
        catch
        {
            // menu reload already destroyed the VCs
        }

        TeardownAlphaUi();
    }

    private void HandleDidRequestColorChange(Action<Color> colorCallback, Color currentColor, AvatarPart editPart,
        int uvSegment)
    {
        if (!ModSettings.EnableAvatarColoringExtensions)
            return;
        AvatarColorEditContext.OnColorEditRequested(currentColor, editPart, uvSegment);
    }

    internal static void NotifyAvatarDataReloadedWhileColorUiOpen()
    {
        Instance?.SyncSlidersFromReloadedAvatarData();
    }

    private void SyncSlidersFromReloadedAvatarData()
    {
        if (!ModSettings.EnableAvatarColoringExtensions
            || _editColorViewController == null
            || !_editColorViewController.gameObject.activeInHierarchy
            || _channelsStackRoot == null)
            return;

        var data = AvatarColoringEditorSession.DataModel?.avatarData;
        if (data == null)
            return;

        if (!AvatarDataColorResolver.TryGetColor(data, AvatarColorEditContext.LastPart, out var c))
            c = _editColorViewController.color;

        _applyingSlidersFromCode = true;
        try
        {
            RememberLiveColor(c);
            _editColorViewController.SetColor(c);
            using (AvatarColorEditorDraft.CommitBypassScope())
                _editColorViewController.InvokeMethod<object, EditAvatarColorViewController>("ChangeColor", c);
            ApplyChannelDisplayValues(c);
        }
        finally
        {
            _applyingSlidersFromCode = false;
        }
    }

    private void HandleExternalColorChanged(Color value)
    {
        SyncFromColorWheel(value);
    }

    private void ApplyChannelDisplayValues(Color value)
    {
        _applyingSlidersFromCode = true;
        try
        {
            SetSliderDisplay(_rHmSlider, value.r);
            SetSliderDisplay(_gHmSlider, value.g);
            SetSliderDisplay(_bHmSlider, value.b);
            SetSliderDisplay(_alphaHmSlider, value.a);

            if (_rStringSetting != null)
                _rStringSetting.Text = FormatDirectEntryScalar(value.r);
            if (_gStringSetting != null)
                _gStringSetting.Text = FormatDirectEntryScalar(value.g);
            if (_bStringSetting != null)
                _bStringSetting.Text = FormatDirectEntryScalar(value.b);
            if (_aStringSetting != null)
                _aStringSetting.Text = FormatDirectEntryScalar(value.a);
        }
        finally
        {
            _applyingSlidersFromCode = false;
        }
    }

    private static void SetSliderDisplay(RangeValuesTextSlider? slider, float v)
    {
        if (slider == null)
            return;

        slider.value = v;
        var shown = FormatDirectEntryScalar(v);
        var root = slider.transform.parent != null ? slider.transform.parent : slider.transform;
        foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
        {
            var s = tmp.text?.Trim() ?? "";
            if (s.Length == 1 && char.IsLetter(s[0]))
                continue;
            tmp.text = shown;
        }
    }

    // round-trip friendly display for Direct # rows (no range clamp in direct mode).
    private static string FormatDirectEntryScalar(float v)
    {
        if (float.IsNaN(v))
            return "NaN";
        if (float.IsPositiveInfinity(v))
            return "Infinity";
        if (float.IsNegativeInfinity(v))
            return "-Infinity";

        return v.ToString("G9", CultureInfo.InvariantCulture);
    }

    [AffinityPatch(typeof(EditAvatarColorViewController), "DidActivate")]
    [AffinityPostfix]
    public void PostfixEditColorDidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        AvatarDatOperations.ScheduleDeleteAvatarBackup();

        if (!ModSettings.EnableAvatarColoringExtensions || !addedToHierarchy)
            return;
        if (_editColorViewController == null)
            return;

        if (_avatarDataModel != null)
            AvatarColorEditorDraft.BeginIfNeeded(_avatarDataModel);
        TeardownAlphaUi();
        BuildAlphaUi();
    }

    [AffinityPatch(typeof(EditAvatarColorViewController), "DidDeactivate")]
    [AffinityPostfix]
    public void PostfixEditColorDidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        // returning to the main avatar editor often deactivates this screen without removing it from the hierarchy.
        // skipping here left the draft active and skipped Abort revert until the whole editor closed.
        StopRgbRowAnchoredNudgeCoroutine();
        if (ModSettings.EnableAvatarColoringExtensions
            && _editColorViewController != null
            && _beatAvatarEditorViewController != null)
            AvatarColorEditorDraft.AbortIfStillActive(_editColorViewController, _beatAvatarEditorViewController);
        TeardownAlphaUi();
    }

    private void BuildAlphaUi()
    {
        var bottom = _editColorViewController.transform.Find("BottomPanel") ?? _editColorViewController.transform;

        Transform? wideRow = null;
        var bestKids = 0;
        for (var i = 0; i < bottom.childCount; i++)
        {
            var row = bottom.GetChild(i);
            if (row.GetComponent<HorizontalLayoutGroup>() == null)
                continue;
            if (row.childCount > bestKids)
            {
                bestKids = row.childCount;
                wideRow = row;
            }
        }

        var parentRow = wideRow ?? bottom;

        for (var i = parentRow.childCount - 1; i >= 0; i--)
        {
            var child = parentRow.GetChild(i);
            if (child != null && child.name == "MPChatAlphaColumn")
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        _alphaColumnRoot = new GameObject("MPChatAlphaColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
        _alphaColumnRoot.transform.SetParent(parentRow, false);
        var colRt = (RectTransform)_alphaColumnRoot.transform;
        colRt.localScale = Vector3.one;
        colRt.anchoredPosition = RgbaSliderStackAnchoredPixelOffset;

        if (RgbaStackSiblingIndex >= 0 && RgbaStackSiblingIndex < parentRow.childCount)
            _alphaColumnRoot.transform.SetSiblingIndex(RgbaStackSiblingIndex);

        var colLe = _alphaColumnRoot.AddComponent<LayoutElement>();
        colLe.preferredWidth = ColumnPreferredWidth;
        colLe.minWidth = ColumnMinWidth;
        colLe.flexibleWidth = 0f;

        var colVlg = _alphaColumnRoot.GetComponent<VerticalLayoutGroup>();
        colVlg.spacing = 1f;
        colVlg.childAlignment = TextAnchor.MiddleCenter;
        colVlg.childForceExpandHeight = false;
        colVlg.childForceExpandWidth = true;

        BuildRgbOptionTogglesRow();

        _channelsStackRoot = new GameObject("MPChatRgbaChannels", typeof(RectTransform), typeof(VerticalLayoutGroup));
        _channelsStackRoot.transform.SetParent(_alphaColumnRoot.transform, false);
        var chRt = (RectTransform)_channelsStackRoot.transform;
        chRt.localScale = Vector3.one;

        var chVlg = _channelsStackRoot.GetComponent<VerticalLayoutGroup>();
        chVlg.spacing = 1f;
        chVlg.childAlignment = TextAnchor.MiddleCenter;
        chVlg.childForceExpandHeight = false;
        chVlg.childForceExpandWidth = true;

        var chLe = _channelsStackRoot.AddComponent<LayoutElement>();
        chLe.flexibleHeight = 0f;

        var c = AvatarColorEditContext.TryConsumePendingInitialColor(out var pending)
            ? pending
            : (_lastKnownRgba ?? new Color(0f, 0f, 0f, 1f));
        RememberLiveColor(c);

        BuildChannelWidgets(c);

        if (parentRow is RectTransform prt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(prt);
        LayoutRebuilder.ForceRebuildLayoutImmediate(colRt);
        colRt.anchoredPosition = RgbaSliderStackAnchoredPixelOffset;
        ScheduleRgbOptionsRowAnchoredNudge();
    }

    private void BuildRgbOptionTogglesRow()
    {
        var row = new GameObject("MPChatRgbOptionsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(_alphaColumnRoot!.transform, false);
        var rt = (RectTransform)row.transform;
        rt.localScale = Vector3.one;

        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 4f;
        h.padding = new RectOffset(0, 0, 2, 2);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childForceExpandHeight = false;
        h.childForceExpandWidth = false;
        h.childControlHeight = true;
        h.childControlWidth = true;

        var rowLe = row.AddComponent<LayoutElement>();
        rowLe.preferredHeight = 14f;
        rowLe.minHeight = 12f;
        rowLe.flexibleHeight = 0f;

        _rgbWideToggleSetting = AddOptionToggle(row.transform, "RGB Max 600", ModSettings.AvatarColorRgbWideRangeEnabled,
            OnRgbWideRangeToggleChanged);
        _directEntryToggleSetting = AddOptionToggle(row.transform, "Direct Number",
            ModSettings.AvatarColorDirectNumberEntryEnabled, OnDirectEntryToggleChanged);

        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    private static ToggleSetting AddOptionToggle(Transform parent, string label, bool initial,
        UnityAction<bool> onChanged)
    {
        var go = new ToggleSettingTag().CreateObject(parent);
        var ts = go.GetComponent<ToggleSetting>();
        ts.Text = label;

        var le = go.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.preferredWidth = 40f;
            le.minWidth = 34f;
            le.flexibleWidth = 0f;
        }

        ts.Value = initial;
        ts.Toggle.onValueChanged.AddListener(onChanged);
        return ts;
    }

    private void OnRgbWideRangeToggleChanged(bool _)
    {
        if (!ModSettings.EnableAvatarColoringExtensions || _rgbWideToggleSetting == null)
            return;
        ModSettings.AvatarColorRgbWideRangeEnabled = _rgbWideToggleSetting.Value;

        if (ModSettings.AvatarColorDirectNumberEntryEnabled)
            ApplyChannelDisplayValues(ReadMergedLiveChannelColor());
        else
            ApplyRgbSliderCapsFromSettings(clampLiveColor: true);
    }

    private void OnDirectEntryToggleChanged(bool _)
    {
        if (!ModSettings.EnableAvatarColoringExtensions || _directEntryToggleSetting == null)
            return;
        ModSettings.AvatarColorDirectNumberEntryEnabled = _directEntryToggleSetting.Value;
        RebuildChannelWidgetsOnly();
    }

    private void RebuildChannelWidgetsOnly()
    {
        if (_channelsStackRoot == null)
            return;

        var merged = ReadMergedLiveChannelColor();

        ClearSliderAndStringRefs();
        DestroyChildren(_channelsStackRoot.transform);

        BuildChannelWidgets(merged);

        if (_channelsStackRoot.transform.parent is RectTransform parentRt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_channelsStackRoot.transform);
        ScheduleRgbOptionsRowAnchoredNudge();
    }

    private void BuildChannelWidgets(Color initialColor)
    {
        if (_channelsStackRoot == null)
            return;

        if (ModSettings.AvatarColorDirectNumberEntryEnabled)
            BuildStringChannels(initialColor);
        else
            BuildSliderChannels(initialColor);
    }

    private void BuildSliderChannels(Color initialColor)
    {
        _rHmSlider = AddChannelSlider("r", initialColor.r, SliderMinForRgb(), SliderMaxForRgb(),
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.r = v;
                PushColor(x);
            });
        _gHmSlider = AddChannelSlider("g", initialColor.g, SliderMinForRgb(), SliderMaxForRgb(),
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.g = v;
                PushColor(x);
            });
        _bHmSlider = AddChannelSlider("b", initialColor.b, SliderMinForRgb(), SliderMaxForRgb(),
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.b = v;
                PushColor(x);
            });
        _alphaHmSlider = AddChannelSlider("a", initialColor.a, SliderAlphaMin, SliderAlphaMax,
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.a = v;
                PushColor(x);
            });

        ApplyRgbSliderCapsFromSettings(clampLiveColor: false);
    }

    private void BuildStringChannels(Color initialColor)
    {
        _rStringSetting = AddChannelStringSetting("r", initialColor.r,
            () => ReadMergedLiveChannelColor().r,
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.r = v;
                PushColor(x);
            });
        _gStringSetting = AddChannelStringSetting("g", initialColor.g,
            () => ReadMergedLiveChannelColor().g,
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.g = v;
                PushColor(x);
            });
        _bStringSetting = AddChannelStringSetting("b", initialColor.b,
            () => ReadMergedLiveChannelColor().b,
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.b = v;
                PushColor(x);
            });
        _aStringSetting = AddChannelStringSetting("a", initialColor.a,
            () => ReadMergedLiveChannelColor().a,
            v =>
            {
                var x = ReadMergedLiveChannelColor();
                x.a = v;
                PushColor(x);
            });
    }

    private static float SliderMinForRgb() =>
        ModSettings.AvatarColorRgbWideRangeEnabled ? SliderRgbWideMin : SliderRgbNarrowMin;

    private static float SliderMaxForRgb() =>
        ModSettings.AvatarColorRgbWideRangeEnabled ? SliderRgbWideMax : SliderRgbNarrowMax;

    private void ApplyRgbSliderCapsFromSettings(bool clampLiveColor)
    {
        if (_rHmSlider != null && _gHmSlider != null && _bHmSlider != null && _alphaHmSlider != null)
        {
            var rgbMin = SliderMinForRgb();
            var rgbMax = SliderMaxForRgb();
            _rHmSlider.minValue = rgbMin;
            _rHmSlider.maxValue = rgbMax;
            _gHmSlider.minValue = rgbMin;
            _gHmSlider.maxValue = rgbMax;
            _bHmSlider.minValue = rgbMin;
            _bHmSlider.maxValue = rgbMax;
        }

        if (clampLiveColor)
            ClampLiveRgbChannelsToSliderBounds();
    }

    // RGB wide/narrow toggle only clamps R G B. Alpha is left untouched (and alpha slider range is not reassigned here so HMUI does not reset its value).
    private void ClampLiveRgbChannelsToSliderBounds()
    {
        var rgbMin = SliderMinForRgb();
        var rgbMax = SliderMaxForRgb();
        var c = ReadMergedLiveChannelColor();
        var before = c;
        c.r = Mathf.Clamp(c.r, rgbMin, rgbMax);
        c.g = Mathf.Clamp(c.g, rgbMin, rgbMax);
        c.b = Mathf.Clamp(c.b, rgbMin, rgbMax);
        if (!ColorsRgbApproxEqual(before, c))
            PushColor(c);

        ApplyChannelDisplayValues(ReadMergedLiveChannelColor());
    }

    // lastKnown is the real color. slider thumbs snap to increment 1, so reading .value after a
    // 0-1 hsv pick would turn apply into 0 0 0 1 and paint the wheel black.
    internal Color ReadMergedLiveChannelColor()
    {
        if (_lastKnownRgba.HasValue)
            return _lastKnownRgba.Value;

        var c = new Color(0f, 0f, 0f, 1f);
        if (_channelsStackRoot == null)
            return c;

        if (_rHmSlider != null)
            c.r = _rHmSlider.value;
        else if (_rStringSetting != null && TryParseChannelScalar(_rStringSetting.Text, out var pr))
            c.r = pr;

        if (_gHmSlider != null)
            c.g = _gHmSlider.value;
        else if (_gStringSetting != null && TryParseChannelScalar(_gStringSetting.Text, out var pg))
            c.g = pg;

        if (_bHmSlider != null)
            c.b = _bHmSlider.value;
        else if (_bStringSetting != null && TryParseChannelScalar(_bStringSetting.Text, out var pb))
            c.b = pb;

        if (_alphaHmSlider != null)
            c.a = _alphaHmSlider.value;
        else if (_aStringSetting != null && TryParseChannelScalar(_aStringSetting.Text, out var pa))
            c.a = pa;

        return c;
    }

    private static bool TryParseChannelScalar(string? text, out float v)
    {
        v = default;
        if (text == null || string.IsNullOrWhiteSpace(text))
            return false;
        return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static bool ColorsRgbApproxEqual(Color a, Color b) =>
        Mathf.Approximately(a.r, b.r)
        && Mathf.Approximately(a.g, b.g)
        && Mathf.Approximately(a.b, b.b);

    // verticalLayoutGroup resets child positions after rebuild; nudge on the next frames after layout runs.
    private void ScheduleRgbOptionsRowAnchoredNudge(RectTransform? rowRt = null)
    {
        rowRt ??= _alphaColumnRoot != null
            ? _alphaColumnRoot.transform.Find("MPChatRgbOptionsRow") as RectTransform
            : null;
        if (rowRt == null)
            return;

        StopRgbRowAnchoredNudgeCoroutine();

        var host = _editColorViewController.gameObject.activeInHierarchy
            ? (MonoBehaviour)(object)_editColorViewController
            : (MonoBehaviour)(object)_beatAvatarEditorViewController;
        if (!host.gameObject.activeInHierarchy)
            return;

        _rgbRowNudgeCoroutineHost = host;
        _rgbRowAnchoredNudgeCoroutine = host.StartCoroutine(RgbOptionsRowAnchoredNudgeRoutine(rowRt));
    }

    private void StopRgbRowAnchoredNudgeCoroutine()
    {
        if (_rgbRowAnchoredNudgeCoroutine == null)
            return;
        if (_rgbRowNudgeCoroutineHost != null)
            _rgbRowNudgeCoroutineHost.StopCoroutine(_rgbRowAnchoredNudgeCoroutine);
        _rgbRowAnchoredNudgeCoroutine = null;
        _rgbRowNudgeCoroutineHost = null;
    }

    private IEnumerator RgbOptionsRowAnchoredNudgeRoutine(RectTransform rowRt)
    {
        var delta = RgbOptionsRowAnchoredPixelOffset;
        if (delta == Vector2.zero)
        {
            _rgbRowAnchoredNudgeCoroutine = null;
            _rgbRowNudgeCoroutineHost = null;
            yield break;
        }

        yield return null;
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();
        if (rowRt == null)
        {
            _rgbRowAnchoredNudgeCoroutine = null;
            _rgbRowNudgeCoroutineHost = null;
            yield break;
        }

        rowRt.anchoredPosition += delta;
        _rgbRowAnchoredNudgeCoroutine = null;
        _rgbRowNudgeCoroutineHost = null;
    }

    [AffinityPrefix]
    [AffinityPatch(typeof(EditAvatarColorViewController), "ChangeColor")]
    public bool PrefixDeferChangeColorUntilApply(EditAvatarColorViewController __instance, Color color)
    {
        if (!AvatarColorEditorDraft.ShouldInterceptChangeColor)
            return true;

        // do not SetColor here: that writes back into the hsv wheel. slider pushes already SetColor themselves.
        using (AvatarColorEditorDraft.CommitBypassScope())
            __instance.InvokeMethod<object, EditAvatarColorViewController>("ChangeColor", color);

        if (_avatarDataModel?.avatarData != null
            && AvatarDataColorResolver.TrySetColor(_avatarDataModel.avatarData, AvatarColorEditContext.LastPart, color))
            _avatarDataModel.ReportAvatarChanged();

        return false;
    }

    // core affinity shim calls this by name; without it SaveColorChange was never deferred.
    internal static bool TryPrefixDeferSaveColorChangeWhileDraft(AvatarPart avatarEditPart)
    {
        return Instance?.PrefixDeferSaveColorChangeWhileDraft(avatarEditPart) ?? true;
    }

    [AffinityPrefix]
    [AffinityPatch(typeof(BeatAvatarEditorViewController), "SaveColorChange")]
    public bool PrefixDeferSaveColorChangeWhileDraft(AvatarPart avatarEditPart)
    {
        if (!ModSettings.EnableAvatarColoringExtensions)
            return true;
        if (_avatarDataModel == null || _editColorViewController == null)
            return true;

        var data = _avatarDataModel.avatarData;
        if (!ShouldDeferSaveColorDuringDraft(avatarEditPart, _editColorViewController.color, data))
            return true;

        return false;
    }

    // when vc.color matches AvatarData, stock is syncing after Cancel; when it differs, defer SaveColorChange
    private static bool ShouldDeferSaveColorDuringDraft(AvatarPart avatarEditPart, Color vcColor, AvatarData? data)
    {
        if (!AvatarColorEditorDraft.ShouldInterceptChangeColor || data == null)
            return false;

        if (avatarEditPart != AvatarColorEditContext.LastPart)
            return false;

        if (!AvatarDataColorResolver.TryGetColor(data, avatarEditPart, out var persisted))
            return true;

        return !ColorsVcMatchesPersisted(vcColor, persisted);
    }

    private static bool ColorsVcMatchesPersisted(Color vc, Color persisted) =>
        Mathf.Approximately(vc.r, persisted.r)
        && Mathf.Approximately(vc.g, persisted.g)
        && Mathf.Approximately(vc.b, persisted.b)
        && Mathf.Approximately(vc.a, persisted.a);

    private void PushColor(Color c)
    {
        RememberLiveColor(c);
        if (_editColorViewController == null)
            return;

        _editColorViewController.SetColor(c);
        _editColorViewController.InvokeMethod<object, EditAvatarColorViewController>("ChangeColor", c);

        if (_avatarDataModel?.avatarData != null
            && AvatarDataColorResolver.TrySetColor(_avatarDataModel.avatarData, AvatarColorEditContext.LastPart, c))
            _avatarDataModel.ReportAvatarChanged();
    }

    private RangeValuesTextSlider AddChannelSlider(string channelLabel, float initial, float min, float max,
        Action<float> apply)
    {
        var sliderHost = new SliderSettingTag().CreateObject(_channelsStackRoot!.transform);
        var sliderRt = sliderHost.GetComponent<RectTransform>();
        if (sliderRt != null)
            sliderRt.localScale = new Vector3(SliderVisualScale, SliderVisualScale, 1f);

        var ss = sliderHost.GetComponent<SliderSetting>();
        ss.Increments = 1f;
        ss.IsInt = false;

        var hmSlider = ss.Slider;
        hmSlider.minValue = min;
        hmSlider.maxValue = max;

        ss.Setup();
        StyleSettingRowLabel(sliderHost, channelLabel);

        hmSlider.value = Mathf.Clamp(initial, min, max);
        StyleSettingRowLabel(sliderHost, channelLabel);

        hmSlider.valueDidChangeEvent += (_, val) =>
        {
            if (!ModSettings.EnableAvatarColoringExtensions || _applyingSlidersFromCode)
                return;
            try
            {
                _applyingSlidersFromCode = true;
                apply(val);
            }
            finally
            {
                _applyingSlidersFromCode = false;
            }
        };

        return hmSlider;
    }

    private StringSetting AddChannelStringSetting(string channelLabel, float initial, Func<float> readCurrent,
        Action<float> apply)
    {
        var host = new StringSettingTag().CreateObject(_channelsStackRoot!.transform);
        var strSetting = host.GetComponent<StringSetting>();
        strSetting.Setup();
        StyleSettingRowLabel(host, channelLabel);
        strSetting.Text = FormatDirectEntryScalar(initial);

        var kb = strSetting.ModalKeyboard?.Keyboard;
        if (kb != null)
            kb.EnterPressed += text =>
            {
                if (!ModSettings.EnableAvatarColoringExtensions || _applyingSlidersFromCode)
                    return;
                if (!float.TryParse(text?.Trim() ?? "", NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var parsed))
                    parsed = readCurrent();

                if (float.IsNaN(parsed))
                    parsed = readCurrent();

                try
                {
                    _applyingSlidersFromCode = true;
                    apply(parsed);
                    strSetting.Text = FormatDirectEntryScalar(parsed);
                }
                finally
                {
                    _applyingSlidersFromCode = false;
                }
            };

        return strSetting;
    }

    private static void StyleSettingRowLabel(GameObject settingHost, string letter)
    {
        var h = settingHost.GetComponentInChildren<HorizontalLayoutGroup>(true);
        if (h != null)
            h.spacing = 0.5f;

        foreach (var tmp in settingHost.GetComponentsInChildren<TMP_Text>(true))
        {
            var s = tmp.text?.Trim() ?? "";
            if (!string.Equals(s, "Default Text", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(s, "Default String", StringComparison.OrdinalIgnoreCase))
                continue;

            tmp.text = letter;
            tmp.fontSize = 3.2f;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;

            var le = tmp.GetComponent<LayoutElement>();
            if (le == null)
                le = tmp.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 5.5f;
            le.minWidth = 4f;
            le.flexibleWidth = 0f;

            var rct = tmp.rectTransform;
            if (rct != null)
                rct.anchoredPosition += RgbaChannelLetterAnchoredPixelNudge;
        }
    }

    private static void DestroyChildren(Transform t)
    {
        for (var i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
    }

    private void ClearSliderAndStringRefs()
    {
        _rHmSlider = null;
        _gHmSlider = null;
        _bHmSlider = null;
        _alphaHmSlider = null;
        _rStringSetting = null;
        _gStringSetting = null;
        _bStringSetting = null;
        _aStringSetting = null;
    }

    private void TeardownAlphaUi()
    {
        StopRgbRowAnchoredNudgeCoroutine();
        ClearSliderAndStringRefs();
        _rgbWideToggleSetting = null;
        _directEntryToggleSetting = null;
        _channelsStackRoot = null;

        if (_alphaColumnRoot != null)
        {
            UnityEngine.Object.Destroy(_alphaColumnRoot);
            _alphaColumnRoot = null;
        }
    }
}
