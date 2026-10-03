using Il2CppInterop.Runtime;
using Il2CppProject.Code.Gameplay.UI.Computer;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WolfProductStatistics;

internal enum StatisticsPeriod
{
    CurrentDay,
    LastDay,
    Total
}

internal enum StatisticsTableSort
{
    Name,
    Quantity,
    Profit
}

internal sealed class StatisticsTableRow
{
    public int DefinitionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public ProductAggregate Value { get; init; } = new();
}

/// <summary>
/// Native Unity UI page hosted by ComputerWorldView._content. Nothing in this
/// class is drawn through IMGUI, so it follows the terminal's world camera,
/// scaling, animation and input exactly like the game's own pages.
/// </summary>
internal static class ProductStatisticsWindow
{
    private const string RootName = "WolfMod_ProductStatisticsSection";
    private const int VisibleRowCount = 6;

    private static readonly Color PageColor = new(0.955f, 0.950f, 0.995f, 0.94f);
    private static readonly Color Navy = new(0.27f, 0.25f, 0.68f, 1f);
    private static readonly Color Muted = new(0.38f, 0.40f, 0.52f, 1f);
    private static readonly Color Green = new(0.07f, 0.55f, 0.22f, 1f);
    private static readonly Color Brown = new(0.57f, 0.36f, 0.16f, 1f);
    private static readonly Color Header = new(0.73f, 0.74f, 0.77f, 1f);
    private static readonly Color RowA = new(0.90f, 0.91f, 0.94f, 1f);
    private static readonly Color RowB = new(0.94f, 0.945f, 0.965f, 1f);
    private static readonly Color ActiveTab = new(0.80f, 0.78f, 0.99f, 1f);

    private static ComputerWorldView? _view;
    private static GameObject? _panelTemplate;
    private static GameObject? _root;
    private static GameObject? _textTemplate;
    private static GameObject? _buttonTemplate;
    private static GameObject? _iconTemplate;
    private static StatisticsSnapshot _snapshot = new();
    private static StatisticsPeriod _period = StatisticsPeriod.CurrentDay;
    private static StatisticsTableSort _sort = StatisticsTableSort.Quantity;
    private static bool _descending = true;
    private static int _scrollOffset;
    private static int _scrollRowCount;
    private static readonly List<UnityAction> Actions = new();

    public static bool IsOpen => _root != null;

    public static bool Open(ComputerWorldView view)
    {
        Close();
        PrepareTemplates(view);
        _snapshot = StatisticsStore.GetSnapshot();

        try
        {
            if (_textTemplate == null || _buttonTemplate == null)
                throw new InvalidOperationException("в нативном терминале не найдены шаблоны текста и кнопки");

            _root = CreateRoot(view);
            _root.SetActive(true);
            Rebuild();
            MelonLogger.Msg("Product Statistics: нативная страница статистики открыта внутри терминала.");
            return true;
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"Product Statistics: нативная страница не создана: {exception}");
            Close();
            return false;
        }
    }

    public static void Close()
    {
        if (_root != null)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
        }
        _root = null;
        Actions.Clear();
    }

    public static void TickInput()
    {
        if (_root == null || _scrollRowCount <= VisibleRowCount)
            return;

        try
        {
            var wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.01f)
                ScrollBy(-1);
            else if (wheel < -0.01f)
                ScrollBy(1);
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"Product Statistics: ввод прокрутки недоступен: {exception.Message}");
        }
    }

    public static void PrepareTemplates(ComputerWorldView view)
    {
        if (_view == view && _textTemplate != null && _buttonTemplate != null)
            return;

        ReleaseTemplates();
        _view = view;
        GameObject? textSource = view._employeeButton?.GetComponentInChildren<TextMeshProUGUI>(true)?.gameObject
                                 ?? view._moneyText?.gameObject;
        GameObject? iconSource = view._employeeButton?.gameObject ?? view._ordersButton?.gameObject;
        GameObject? buttonSource = null;
        GameObject? panelSource = null;

        foreach (var button in view._content.GetComponentsInChildren<Button>(true))
        {
            if (button == null || button.gameObject == null)
                continue;
            if (buttonSource == null || button.name.Contains("Confirm", StringComparison.OrdinalIgnoreCase))
                buttonSource = button.gameObject;
            if (button.name.Contains("Confirm", StringComparison.OrdinalIgnoreCase))
                break;
        }
        buttonSource ??= view._closeButton?.gameObject;

        foreach (var image in view._content.GetComponentsInChildren<Image>(true))
        {
            if (image != null && image.gameObject != null && image.name == "BG")
            {
                panelSource = image.gameObject;
                break;
            }
        }
        panelSource ??= buttonSource;

        if (textSource == null || buttonSource == null || panelSource == null)
            return;

        _textTemplate = CloneHiddenTemplate(textSource, view._content, "WolfMod_TextTemplate");
        _buttonTemplate = CloneHiddenTemplate(buttonSource, view._content, "WolfMod_ButtonTemplate");
        _panelTemplate = CloneHiddenTemplate(panelSource, view._content, "WolfMod_PanelTemplate");
        if (iconSource != null)
            _iconTemplate = CloneHiddenTemplate(iconSource, view._content, "WolfMod_IconTemplate");
    }

    private static GameObject CreateRoot(ComputerWorldView view)
    {
        var template = _panelTemplate ?? _buttonTemplate;
        if (template == null)
            throw new InvalidOperationException("не найден фон нативного раздела");

        var root = UnityEngine.Object.Instantiate(template, view._content, false);
        root.name = RootName;
        root.hideFlags = HideFlags.DontSave;
        root.SetActive(false);
        ClearChildren(root.transform);
        DisableInteractiveComponents(root, keepImage: true);
        var rect = root.GetComponent<RectTransform>() ?? throw new InvalidOperationException("у фона нет RectTransform");
        Stretch(rect, Vector2.zero, Vector2.one);
        var imageComponent = root.GetComponent<Image>();
        if (imageComponent != null)
        {
            // The first-floor terminal can expose only its Back button while the
            // native sections are still unbuilt. In that case our fallback template
            // carries the large arrow sprite; a page background must always be plain.
            imageComponent.sprite = null;
            imageComponent.type = Image.Type.Simple;
            imageComponent.preserveAspect = false;
            imageComponent.color = PageColor;
            imageComponent.raycastTarget = true;
        }
        root.transform.SetAsLastSibling();
        return root;
    }

    private static void Rebuild()
    {
        if (_root == null || _view == null || _textTemplate == null || _buttonTemplate == null)
            return;

        ClearChildren(_root.transform);
        Actions.Clear();
        _snapshot = StatisticsStore.GetSnapshot();

        CreateText(_root.transform, "Title", "СТАТИСТИКА ТОВАРОВ",
            new Vector2(0.22f, 0.825f), new Vector2(0.56f, 0.885f), 32f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, Navy);
        CreateText(_root.transform, "Subtitle", GetSubtitle(),
            new Vector2(0.22f, 0.785f), new Vector2(0.56f, 0.825f), 18f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, Muted);

        CreatePeriodButton(StatisticsPeriod.CurrentDay, "ТЕКУЩИЙ ДЕНЬ", 0.57f, 0.695f);
        CreatePeriodButton(StatisticsPeriod.LastDay, "ПРОШЛЫЙ ДЕНЬ", 0.70f, 0.825f);
        CreatePeriodButton(StatisticsPeriod.Total, "ВСЁ ВРЕМЯ", 0.83f, 0.96f);

        CreateHeaderButton("ТОВАР", StatisticsTableSort.Name, 0.03f, 0.58f);
        CreateHeaderButton("КОЛ-ВО ПРОДАЖ", StatisticsTableSort.Quantity, 0.58f, 0.77f);
        CreateHeaderButton(StatisticsSettings.ShowEstimatedProfit ? "ПРИБЫЛЬ" : "ВЫРУЧКА",
            StatisticsTableSort.Profit, 0.77f, 0.96f);

        CreateManualScrollableRows(BuildRows());
    }

    private static string GetSubtitle()
    {
        return _period switch
        {
            StatisticsPeriod.CurrentDay => "Продажи с начала текущего игрового дня",
            StatisticsPeriod.LastDay when _snapshot.LastCompletedDay > 0 =>
                $"Итоги завершённого дня {_snapshot.LastCompletedDay}",
            StatisticsPeriod.LastDay => "Завершённых дней после установки мода пока нет",
            _ => "Все продажи, записанные после установки мода"
        };
    }

    private static void CreatePeriodButton(StatisticsPeriod period, string caption, float left, float right)
    {
        CreateButton(_root!.transform, $"Period_{period}", caption,
            new Vector2(left, 0.825f), new Vector2(right, 0.875f),
            _period == period ? ActiveTab : Header,
            () =>
            {
                _period = period;
                _scrollOffset = 0;
                Rebuild();
            },
            20f);
    }

    private static void CreateHeaderButton(string caption, StatisticsTableSort sort, float left, float right)
    {
        var arrow = _sort == sort ? (_descending ? "  ▼" : "  ▲") : string.Empty;
        CreateButton(_root!.transform, $"Header_{sort}", caption + arrow,
            new Vector2(left, 0.715f), new Vector2(right, 0.765f), Header,
            () =>
            {
                if (_sort == sort)
                    _descending = !_descending;
                else
                {
                    _sort = sort;
                    _descending = sort != StatisticsTableSort.Name;
                }
                _scrollOffset = 0;
                Rebuild();
            },
            23f);
    }

    private static void CreateManualScrollableRows(List<StatisticsTableRow> rows)
    {
        if (_root == null)
            return;

        _scrollRowCount = rows.Count;
        var maximumOffset = Math.Max(0, rows.Count - VisibleRowCount);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maximumOffset);

        if (rows.Count == 0)
        {
            CreateText(_root.transform, "Empty", "За выбранный период продаж пока нет.",
                new Vector2(0.06f, 0.32f), new Vector2(0.94f, 0.58f), 27f,
                FontStyles.Bold, TextAlignmentOptions.Center, Muted);
            return;
        }

        var visibleCount = Math.Min(VisibleRowCount, rows.Count - _scrollOffset);
        for (var visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
        {
            var rowIndex = _scrollOffset + visibleIndex;
            var top = 0.70f - visibleIndex * 0.096f;
            CreateFallbackRow(_root.transform, rows[rowIndex], rowIndex, top - 0.091f, top);
        }

        if (maximumOffset <= 0)
            return;

        const float listBottom = 0.125f;
        const float listTop = 0.70f;
        const float listHeight = listTop - listBottom;
        var trackObject = CreatePanel(_root.transform, "ManualScrollTrack", new Color(0.74f, 0.75f, 0.80f, 1f));
        Stretch(trackObject.GetComponent<RectTransform>()!,
            new Vector2(0.953f, listBottom), new Vector2(0.961f, listTop));

        var handleHeight = Math.Max(0.075f, listHeight * VisibleRowCount / rows.Count);
        var normalizedOffset = (float)_scrollOffset / maximumOffset;
        var handleTop = listTop - normalizedOffset * (listHeight - handleHeight);
        var handleObject = CreatePanel(_root.transform, "ManualScrollHandle", new Color(0.28f, 0.29f, 0.37f, 1f));
        Stretch(handleObject.GetComponent<RectTransform>()!,
            new Vector2(0.952f, handleTop - handleHeight), new Vector2(0.962f, handleTop));
    }

    private static void ScrollBy(int amount)
    {
        var maximumOffset = Math.Max(0, _scrollRowCount - VisibleRowCount);
        var nextOffset = Math.Clamp(_scrollOffset + amount, 0, maximumOffset);
        if (nextOffset == _scrollOffset)
            return;

        _scrollOffset = nextOffset;
        Rebuild();
    }

    private static void CreateFallbackRow(Transform parent, StatisticsTableRow row, int index, float bottom, float top)
    {
        var rowObject = CreatePanel(parent, $"Row_{row.DefinitionId}", index % 2 == 0 ? RowA : RowB);
        Stretch(rowObject.GetComponent<RectTransform>()!, new Vector2(0.03f, bottom), new Vector2(0.96f, top));
        PopulateRow(rowObject.transform, row);
    }

    private static void PopulateRow(Transform parent, StatisticsTableRow row)
    {
        var sprite = ProductDefinitionLookup.ResolveSprite(row.DefinitionId);
        if (sprite != null)
            CreateIcon(parent, sprite, new Vector2(0.010f, 0.035f), new Vector2(0.105f, 0.965f));

        CreateText(parent, "Name", row.Name,
            new Vector2(0.115f, 0.05f), new Vector2(0.565f, 0.95f), 22f,
            FontStyles.Bold, TextAlignmentOptions.MidlineLeft, Navy);
        CreateText(parent, "Quantity", row.Value.Quantity.ToString(),
            new Vector2(0.58f, 0.05f), new Vector2(0.77f, 0.95f), 28f,
            FontStyles.Bold, TextAlignmentOptions.Center, Green);

        var primary = StatisticsSettings.ShowEstimatedProfit ? row.Value.EstimatedProfit : row.Value.Revenue;
        CreateText(parent, "PrimaryMoney", FormatMoney(primary),
            new Vector2(0.77f, 0.43f), new Vector2(0.99f, 0.96f), 27f,
            FontStyles.Bold, TextAlignmentOptions.Center, Green);
        if (StatisticsSettings.ShowEstimatedProfit)
        {
            CreateText(parent, "Revenue", $"выручка {FormatMoney(row.Value.Revenue)}",
                new Vector2(0.77f, 0.05f), new Vector2(0.99f, 0.48f), 17f,
                FontStyles.Normal, TextAlignmentOptions.Center, Brown);
        }
    }

    private static List<StatisticsTableRow> BuildRows()
    {
        var source = _period switch
        {
            StatisticsPeriod.CurrentDay => _snapshot.CurrentDay,
            StatisticsPeriod.LastDay => _snapshot.LastDay,
            _ => _snapshot.Total
        };

        var rows = new List<StatisticsTableRow>();
        foreach (var pair in source)
        {
            var value = pair.Value ?? new ProductAggregate { DefinitionId = pair.Key };
            rows.Add(new StatisticsTableRow
            {
                DefinitionId = pair.Key,
                Name = !string.IsNullOrWhiteSpace(value.Name)
                    ? value.Name
                    : ProductDefinitionLookup.ResolveName(pair.Key),
                Value = value
            });
        }

        IOrderedEnumerable<StatisticsTableRow> ordered = _sort switch
        {
            StatisticsTableSort.Name => _descending
                ? rows.OrderByDescending(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                : rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            StatisticsTableSort.Profit => _descending
                ? rows.OrderByDescending(row => StatisticsSettings.ShowEstimatedProfit
                    ? row.Value.EstimatedProfit
                    : row.Value.Revenue)
                : rows.OrderBy(row => StatisticsSettings.ShowEstimatedProfit
                    ? row.Value.EstimatedProfit
                    : row.Value.Revenue),
            _ => _descending
                ? rows.OrderByDescending(row => row.Value.Quantity)
                : rows.OrderBy(row => row.Value.Quantity)
        };
        return ordered.ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        if (_buttonTemplate == null)
            throw new InvalidOperationException("шаблон панели отсутствует");
        var panel = UnityEngine.Object.Instantiate(_buttonTemplate, parent, false);
        panel.name = name;
        panel.SetActive(false);
        ClearChildren(panel.transform);
        DisableInteractiveComponents(panel, keepImage: true);
        var image = panel.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = color;
            image.raycastTarget = false;
        }
        panel.SetActive(true);
        return panel;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        string caption,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color,
        Action action,
        float fontSize)
    {
        if (_buttonTemplate == null)
            throw new InvalidOperationException("шаблон кнопки отсутствует");
        var buttonObject = UnityEngine.Object.Instantiate(_buttonTemplate, parent, false);
        buttonObject.name = name;
        buttonObject.SetActive(false);
        ClearChildren(buttonObject.transform);
        var rect = buttonObject.GetComponent<RectTransform>() ?? throw new InvalidOperationException("у кнопки нет RectTransform");
        Stretch(rect, anchorMin, anchorMax, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        var image = buttonObject.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = color;
            image.raycastTarget = true;
        }
        var button = buttonObject.GetComponent<Button>() ?? throw new InvalidOperationException("у шаблона нет Button");
        button.enabled = true;
        button.interactable = true;
        button.targetGraphic = image;
        button.image = image;
        button.onClick = new Button.ButtonClickedEvent();
        var unityAction = DelegateSupport.ConvertDelegate<UnityAction>(action) ??
                          throw new InvalidOperationException("не удалось создать обработчик нативной кнопки");
        Actions.Add(unityAction);
        button.onClick.AddListener(unityAction);
        CreateText(buttonObject.transform, "Label", caption, Vector2.zero, Vector2.one,
            fontSize, FontStyles.Bold, TextAlignmentOptions.Center, Navy);
        buttonObject.SetActive(true);
        return button;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        string value,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions alignment,
        Color color)
    {
        if (_textTemplate == null)
            throw new InvalidOperationException("шаблон текста отсутствует");
        var textObject = UnityEngine.Object.Instantiate(_textTemplate, parent, false);
        textObject.name = name;
        textObject.SetActive(false);
        ClearChildren(textObject.transform);
        var rect = textObject.GetComponent<RectTransform>() ?? throw new InvalidOperationException("у текста нет RectTransform");
        Stretch(rect, anchorMin, anchorMax, new Vector2(4f, 2f), new Vector2(-4f, -2f));
        var text = textObject.GetComponent<TextMeshProUGUI>() ?? throw new InvalidOperationException("у шаблона нет TMP текста");
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.enableAutoSizing = false;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        textObject.SetActive(true);
        return text;
    }

    private static void CreateIcon(Transform parent, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (_iconTemplate == null)
            return;
        var iconObject = UnityEngine.Object.Instantiate(_iconTemplate, parent, false);
        iconObject.name = "ProductIcon";
        iconObject.SetActive(false);
        ClearChildren(iconObject.transform);
        DisableInteractiveComponents(iconObject, keepImage: true);
        var rect = iconObject.GetComponent<RectTransform>();
        if (rect != null)
            Stretch(rect, anchorMin, anchorMax, new Vector2(4f, 4f), new Vector2(-4f, -4f));
        var image = iconObject.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        iconObject.SetActive(true);
    }

    private static void DisableInteractiveComponents(GameObject gameObject, bool keepImage)
    {
        var button = gameObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = false;
            button.enabled = false;
        }
        var scroll = gameObject.GetComponent<ScrollRect>();
        if (scroll != null)
            scroll.enabled = false;
        var image = gameObject.GetComponent<Image>();
        if (image != null && !keepImage)
            image.enabled = false;
    }

    private static void ClearChildren(Transform parent)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            var child = parent.GetChild(index);
            if (child == null || child.gameObject == null)
                continue;
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    private static GameObject CloneHiddenTemplate(GameObject source, Transform parent, string name)
    {
        var clone = UnityEngine.Object.Instantiate(source, parent, false);
        clone.name = name;
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour.GetIl2CppType().Name == "Localize")
                behaviour.enabled = false;
        }
        return clone;
    }

    public static void ReleaseTemplates()
    {
        Close();
        foreach (var template in new[] { _panelTemplate, _textTemplate, _buttonTemplate, _iconTemplate })
        {
            if (template == null)
                continue;
            template.SetActive(false);
            UnityEngine.Object.Destroy(template);
        }
        _panelTemplate = null;
        _textTemplate = null;
        _buttonTemplate = null;
        _iconTemplate = null;
        _view = null;
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2? offsetMin = null,
        Vector2? offsetMax = null)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin ?? Vector2.zero;
        rect.offsetMax = offsetMax ?? Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static string FormatMoney(double value) => $"${value:0.00}";
}
