using SephiriaSkins.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace SephiriaSkins.Plugin;

// The game owns modal focus, button feedback, font styling and closing behaviour.
internal sealed class NativeSelector : IDisposable
{
    private UI_MessageBox_YesNo? window;
    private PackEntry? chosen;
    private Button? apply;
    private TextMeshProUGUI? error;
    private Image? preview;
    private RectTransform? content;
    private Button? rowTemplate;
    private readonly List<Button> rows = new();
    private readonly List<UI_MessageBox_YesNo> detailWindows = new();
    private Button? restoreButton, refreshButton;
    private readonly List<Object> owned = new();
    private readonly Dictionary<string, Sprite> previewCache = new();
    private RuntimeTheme? previewTheme;
    private IReadOnlyList<PackEntry> entries = Array.Empty<PackEntry>();
    private Action<PackEntry> select = null!;
    private Action reload = null!, export = null!;
    private Func<RuntimeTheme?> current = null!;
    public bool IsOpen => window && window!.IsOpened;

    public bool Open(IReadOnlyList<PackEntry> packs, Func<RuntimeTheme?> theme, Action<PackEntry> onApply,
        Action onRestore, Action onReload, Action onExport, Action onClose)
    {
        if (!UIManager.Instance) return false;
        var holder = UIManager.Instance.GetElement<UI_MessageBoxHolder>();
        if (!holder || !holder.yesNoPrefab) return false;
        entries = packs; current = theme; select = onApply; reload = onReload; export = onExport;
        if (theme() is RuntimeTheme active) chosen = packs.FirstOrDefault(p => p.Pack?.Manifest.Id == active.Pack.Manifest.Id);
        window = (UI_MessageBox_YesNo)holder.OpenYesNo("스킨", () => { if (chosen?.Error == null && chosen?.Pack != null) select(chosen); }, () => { });
        window.gameObject.AddComponent<SkinSelectorMarker>();
        window.canCloseControlWithESC = true;
        foreach (var nativeLayout in window.GetComponentsInChildren<LayoutGroup>(true)) nativeLayout.enabled = false;
        foreach (var sizing in window.GetComponentsInChildren<ContentSizeFitter>(true)) sizing.enabled = false;
        window.onClosed += onClose;
        apply = window.yesButton; SetLabel(apply, "적용"); SetLabel(window.noButton, "닫기");
        apply.onClick = new Button.ButtonClickedEvent(); apply.onClick.AddListener(() => { if (chosen?.Pack != null && chosen.Error == null) select(chosen); });
        rowTemplate = holder.yesNoPrefab.yesButton;
        var canvas = window.GetComponentInParent<Canvas>();
        var available = ((RectTransform)canvas.transform).rect.size;
        var root = window.rectTransform;
        var width = Mathf.Min(560, available.x - 32); var height = Mathf.Min(440, available.y - 32);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f); root.sizeDelta = new Vector2(width, height); root.anchoredPosition = Vector2.zero;
        foreach (var image in window.GetComponentsInChildren<Image>(true))
        {
            if (image.GetComponentInParent<Button>()) continue;
            var rect = image.rectTransform;
            if (rect != root && rect.rect.width > 100 && rect.rect.height > 40)
            { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        }
        // Existing text/buttons are repositioned; their materials and native colours stay intact.
        Place(window.text.rectTransform, 16, -12, width - 32, 28);
        window.text.transform.SetParent(root, false); Place(window.text.rectTransform, 16, -12, width - 32, 28);
        window.text.text = "스킨";
        var body = Node("Skins", root);
        body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = new Vector2(16, 88); body.offsetMax = new Vector2(-16, -48);
        var viewport = Node("Viewport", body); viewport.anchorMin = Vector2.zero; viewport.anchorMax = new Vector2(.72f, 1); viewport.offsetMin = Vector2.zero; viewport.offsetMax = new Vector2(-8, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        content = Node("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = body.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        body.gameObject.AddComponent<SelectorScrollFocus>().Scroll = scroll;
        var previewRect = Node("Preview", body); previewRect.anchorMin = new Vector2(.75f, .3f); previewRect.anchorMax = new Vector2(1, .85f); previewRect.offsetMin = previewRect.offsetMax = Vector2.zero;
        preview = previewRect.gameObject.AddComponent<Image>(); preview.preserveAspect = true; preview.raycastTarget = false;
        error = CloneText(window.text, root, "Error"); Place(error.rectTransform, 16, -height + 84, width - 32, 28); error.fontSize = window.text.fontSize; error.gameObject.SetActive(false);
        var buttons = Node("Actions", root); Place(buttons, 16, -height + 48, width - 32, 32);
        var actions = buttons.gameObject.AddComponent<HorizontalLayoutGroup>(); actions.spacing = 8; actions.childControlWidth = true; actions.childControlHeight = true; actions.childForceExpandWidth = true;
        var restore = restoreButton = CloneButton(rowTemplate, buttons, "원본", onRestore);
        var refresh = refreshButton = CloneButton(rowTemplate, buttons, "새로고침", () => reload());
        var details = CloneButton(rowTemplate, buttons, "상세", ShowDetails);
        apply.transform.SetParent(buttons, false); window.noButton.transform.SetParent(buttons, false);
        foreach (var button in new[] { restore, refresh, details, apply, window.noButton })
        { button.gameObject.AddComponent<LayoutElement>().preferredWidth = 80; button.navigation = new Navigation { mode = Navigation.Mode.Automatic }; }
        chosen = packs.FirstOrDefault(p => p.Pack?.Manifest.Id == current()?.Pack.Manifest.Id && p.Error == null) ?? packs.FirstOrDefault(p => p.Error == null) ?? packs.FirstOrDefault();
        Populate();
        return true;
    }
    public void Update(IReadOnlyList<PackEntry> packs, bool loading, string status, bool unsupported)
    {
        if (!IsOpen) return;
        if (!ReferenceEquals(entries, packs)) { entries = packs; Populate(); }
        if (previewTheme != current()) { previewTheme = current(); if (chosen != null) Choose(chosen); }
        apply!.interactable = !loading && !unsupported && chosen?.Pack != null && chosen.Error == null;
        restoreButton!.interactable = refreshButton!.interactable = !loading;
        var message = unsupported ? "지원하지 않는 게임 버전" : chosen?.Error != null ? "사용할 수 없는 팩" : status.Contains("실패") ? "불러오기 실패" : loading ? "불러오는 중…" : "";
        error!.gameObject.SetActive(message.Length > 0); error.text = message;
    }
    private void Populate()
    {
        if (!content || !rowTemplate) return;
        var id = chosen?.Pack?.Manifest.Id;
        foreach (var row in rows) if (row) Object.Destroy(row.gameObject); rows.Clear();
        chosen = entries.FirstOrDefault(p => p.Pack?.Manifest.Id == id) ?? entries.FirstOrDefault(p => p.Error == null) ?? entries.FirstOrDefault();
        foreach (var pack in entries)
        {
            var row = CloneButton(rowTemplate!, content!, pack.Name, () => Choose(pack));
            var label = row.GetComponentInChildren<TextMeshProUGUI>(true);
            label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
            var size = row.gameObject.AddComponent<LayoutElement>(); size.preferredHeight = 40;
            rows.Add(row);
        }
        if (chosen != null) Choose(chosen);
        if (rows.Count > 0) rows[0].Select();
    }
    private void Choose(PackEntry pack)
    {
        chosen = pack;
        for (var i = 0; i < rows.Count; i++) SetLabel(rows[i], (entries[i] == pack ? "› " : "") + entries[i].Name);
        preview!.sprite = Preview(pack); preview.enabled = preview.sprite;
    }
    private Sprite? Preview(PackEntry pack)
    {
        if (pack.Pack?.Manifest.Preview == null) return null;
        var id = pack.Pack.Manifest.Preview;
        if (current()?.Pack.Manifest.Id == pack.Pack.Manifest.Id && current()!.Assets.TryGetValue(id, out var native)) return native as Sprite;
        var r = pack.Pack.Manifest.Resources[id]; if (r.Bundle != null) return null;
        var key = pack.Source + ":" + Keys.Hash(pack.Pack.Files[r.File]);
        if (previewCache.TryGetValue(key, out var cached)) return cached;
        var texture = new Texture2D(2, 2) { filterMode = FilterMode.Point }; owned.Add(texture);
        if (!ImageConversion.LoadImage(texture, pack.Pack.Files[r.File], true)) return null;
        var rect = r.Rect == null ? new Rect(0, 0, texture.width, texture.height) : new Rect(r.Rect[0], r.Rect[1], r.Rect[2], r.Rect[3]);
        var sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), r.PixelsPerUnit); owned.Add(sprite); previewCache[key] = sprite; return sprite;
    }
    private void ShowDetails()
    {
        if (!window || chosen == null) return;
        var pack = chosen.Pack?.Manifest;
        var text = pack == null ? chosen.Error ?? chosen.Name : pack.Name + "\n" + pack.Author + " · " + pack.Version + "\n" + pack.Description + (chosen.Error == null ? "" : "\n" + chosen.Error);
        var holder = UIManager.Instance.GetElement<UI_MessageBoxHolder>();
        var details = holder.OpenYesNo(text, export, () => { });
        details.gameObject.AddComponent<SkinSelectorMarker>();
        details.canCloseControlWithESC = true;
        var box = (UI_MessageBox_YesNo)details; SetLabel(box.yesButton, "템플릿 저장"); SetLabel(box.noButton, "닫기");
        detailWindows.Add(box); box.onClosed += () => detailWindows.Remove(box);
    }
    private static RectTransform Node(string name, Transform parent)
    {
        var node = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)node.transform; rect.SetParent(parent, false); return rect;
    }
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }
    private static TextMeshProUGUI CloneText(TextMeshProUGUI source, Transform parent, string name)
    {
        var clone = Object.Instantiate(source.gameObject, parent); clone.name = name;
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(behaviour is Graphic) && !(behaviour is TMP_SubMeshUI)) { behaviour.enabled = false; Object.Destroy(behaviour); }
        return clone.GetComponent<TextMeshProUGUI>();
    }
    private static Button CloneButton(Button source, Transform parent, string label, Action action)
    {
        var staging = new GameObject("ButtonStaging"); staging.SetActive(false); staging.transform.SetParent(parent, false);
        var clone = Object.Instantiate(source.gameObject, staging.transform); clone.name = label;
        foreach (var sizing in clone.GetComponentsInChildren<ContentSizeFitter>(true)) sizing.enabled = false;
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(behaviour is Graphic) && !(behaviour is Button) && !(behaviour is TMP_SubMeshUI) && !(behaviour is LayoutGroup) && !(behaviour is ContentSizeFitter) && !(behaviour is LayoutElement))
            { behaviour.enabled = false; Object.DestroyImmediate(behaviour); }
        var button = clone.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(() => action());
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        var rect = (RectTransform)clone.transform; rect.SetParent(parent, false); rect.localScale = Vector3.one;
        var nativeHeight = Mathf.Max(32, source.GetComponent<RectTransform>().rect.height);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, nativeHeight);
        clone.SetActive(true); Object.Destroy(staging); SetLabel(button, label); return button;
    }
    private static void SetLabel(Button button, string label)
    {
        var text = button.GetComponentInChildren<TMP_Text>(true);
        if (text) { text.text = label; text.overflowMode = TextOverflowModes.Ellipsis; text.textWrappingMode = TextWrappingModes.NoWrap; }
    }
    public void Close()
    {
        var active = window; window = null;
        foreach (var details in detailWindows.ToArray()) if (details && details.IsOpened) details.Close();
        detailWindows.Clear();
        if (active && active!.IsOpened) active.Close();
        foreach (var asset in owned) if (asset) Object.Destroy(asset); owned.Clear(); previewCache.Clear(); rows.Clear();
    }
    public void Dispose()
    {
        Close(); foreach (var asset in owned) if (asset) Object.Destroy(asset); owned.Clear(); rows.Clear();
    }
}
internal sealed class SelectorScrollFocus : MonoBehaviour
{
    public ScrollRect Scroll = null!;
    private GameObject? previous;
    private void LateUpdate()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (!selected || selected == previous || !selected!.transform.IsChildOf(Scroll.content)) return;
        previous = selected;
        Canvas.ForceUpdateCanvases();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(Scroll.viewport,selected.transform);
        var visible = Scroll.viewport.rect;
        var shift = bounds.min.y < visible.yMin ? visible.yMin - bounds.min.y : bounds.max.y > visible.yMax ? visible.yMax - bounds.max.y : 0;
        Scroll.content.anchoredPosition += new Vector2(0,shift);
    }
}
