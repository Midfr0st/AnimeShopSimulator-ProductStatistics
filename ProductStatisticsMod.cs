using System.Reflection;
using HarmonyLib;
using Il2CppProject.Code.Core.Saves;
using Il2CppProject.Code.Core.Saves.SessionData;
using Il2CppProject.Code.Core.Services;
using Il2CppProject.Code.Core.Utils.DataDefinition;
using Il2CppProject.Code.Gameplay.AI.Buyer;
using Il2CppProject.Code.Gameplay.Controllers;
using Il2CppProject.Code.Gameplay.Definitions;
using Il2CppProject.Code.Gameplay.Interactions.CashRegister;
using Il2CppProject.Code.Gameplay.Services;
using Il2CppProject.Code.Gameplay.UI.Computer;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using UnityEngine;

[assembly: MelonInfo(typeof(WolfProductStatistics.ProductStatisticsMod), "Anime Shop: Product Statistics", "0.5.2", "WolfMods")]

namespace WolfProductStatistics;

public sealed class ProductStatisticsMod : MelonMod
{
    private const string WolfModId = "wolfmod.product_statistics";
    private const int MaxRegistrationAttempts = 4;
    private const float RetryDelay = 1.5f;
    private float _nextRegistrationAttempt;
    private int _registrationAttempts;
    private bool _runtimeInitialized;
    private bool _dependencyFailed;

    internal static ProductStatisticsMod? Instance { get; private set; }
    internal static bool FeatureEnabled { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;
        FeatureEnabled = false;
        TryInitialize();
    }

    public override void OnUpdate()
    {
        if (!_runtimeInitialized)
        {
            if (!_dependencyFailed && Time.unscaledTime >= _nextRegistrationAttempt)
                TryInitialize();
            return;
        }
        StatisticsStore.Tick();
    }

    public override void OnDeinitializeMelon()
    {
        StatisticsStore.Flush();
        ProductStatisticsWindow.Close();
        WolfModBridge.Unregister(WolfModId);
        Instance = null;
    }

    public override void OnApplicationQuit()
    {
        StatisticsStore.Flush();
    }

    private void TryInitialize()
    {
        _registrationAttempts++;
        if (!WolfModBridge.TryRegister(
                WolfModId,
                "Статистика товаров",
                "0.5.2",
                "Продажи, выручка и расчётная прибыль по каждому товару.",
                SetFeatureEnabled,
                DrawWolfModSettings,
                () => false))
        {
            if (_registrationAttempts < MaxRegistrationAttempts)
                _nextRegistrationAttempt = Time.unscaledTime + RetryDelay;
            else
            {
                _dependencyFailed = true;
                LoggerInstance.Error(
                    $"Product Statistics отключён: WolfCore не найден или несовместим. {WolfModBridge.LastError}");
            }
            return;
        }

        try
        {
            StatisticsSettings.Initialize();
            StatisticsStore.Initialize();
            StatisticsPatches.Install();
            _runtimeInitialized = true;
            LoggerInstance.Msg(
                "Product Statistics 0.5.2 загружен. Терминальная вкладка передана WolfCore.");
        }
        catch
        {
            WolfModBridge.Unregister(WolfModId);
            FeatureEnabled = false;
            throw;
        }
    }

    private static void SetFeatureEnabled(bool enabled)
    {
        FeatureEnabled = enabled;
        if (!enabled)
        {
            StatisticsStore.Flush();
            ProductStatisticsWindow.Close();
        }
    }

    private static void DrawWolfModSettings()
    {
        GUILayout.Label(
            "Общий счёт начинается с момента установки мода: игра не хранит старые чеки по отдельным товарам.");
        GUILayout.Space(10f);
        var showProfit = GUILayout.Toggle(
            StatisticsSettings.ShowEstimatedProfit,
            "Показывать расчётную валовую прибыль");
        if (showProfit != StatisticsSettings.ShowEstimatedProfit)
            StatisticsSettings.SetShowEstimatedProfit(showProfit);
        GUILayout.Label(
            "Расчётная прибыль = фактическая цена продажи − средняя закупочная стоимость товара в момент продажи.");
    }
}

internal sealed class StatisticsSettingsData
{
    public bool ShowEstimatedProfit { get; set; } = true;
}

internal static class StatisticsSettings
{
    private static string _path = string.Empty;
    public static bool ShowEstimatedProfit { get; private set; } = true;

    public static void Initialize()
    {
        _path = Path.Combine(MelonEnvironment.UserDataDirectory, "WolfProductStatistics.settings.json");
        ShowEstimatedProfit = true;
        if (!File.Exists(_path))
        {
            Save();
            return;
        }

        try
        {
            var data = JsonConvert.DeserializeObject<StatisticsSettingsData>(File.ReadAllText(_path));
            if (data != null)
                ShowEstimatedProfit = data.ShowEstimatedProfit;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"Product Statistics: настройки не прочитаны: {exception.Message}");
        }
    }

    public static void SetShowEstimatedProfit(bool show)
    {
        ShowEstimatedProfit = show;
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonConvert.SerializeObject(
                    new StatisticsSettingsData { ShowEstimatedProfit = ShowEstimatedProfit },
                    Formatting.Indented));
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"Product Statistics: настройки не сохранены: {exception}");
        }
    }
}

internal sealed class ProductAggregate
{
    public int DefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Quantity { get; set; }
    public double Revenue { get; set; }
    public double EstimatedProfit { get; set; }

    public void Add(CapturedProduct product)
    {
        DefinitionId = product.DefinitionId;
        if (!string.IsNullOrWhiteSpace(product.Name))
            Name = product.Name;
        Quantity++;
        Revenue += product.SalePrice;
        EstimatedProfit += product.EstimatedProfit;
    }

    public ProductAggregate Clone()
    {
        return new ProductAggregate
        {
            DefinitionId = DefinitionId,
            Name = Name,
            Quantity = Quantity,
            Revenue = Revenue,
            EstimatedProfit = EstimatedProfit
        };
    }
}

internal sealed class SaveSlotStatistics
{
    public int CurrentDay { get; set; }
    public int LastCompletedDay { get; set; }
    public Dictionary<int, ProductAggregate> Total { get; set; } = new();
    public Dictionary<int, ProductAggregate> CurrentDayProducts { get; set; } = new();
    public Dictionary<int, ProductAggregate> LastCompletedDayProducts { get; set; } = new();
    public HashSet<string> ProcessedSales { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class ProductStatisticsData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, SaveSlotStatistics> Slots { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class StatisticsSnapshot
{
    public string SlotKey { get; init; } = string.Empty;
    public int LastCompletedDay { get; init; }
    public Dictionary<int, ProductAggregate> Total { get; init; } = new();
    public Dictionary<int, ProductAggregate> CurrentDay { get; init; } = new();
    public Dictionary<int, ProductAggregate> LastDay { get; init; } = new();
}

internal sealed class CapturedProduct
{
    public int DefinitionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public double SalePrice { get; init; }
    public double EstimatedProfit { get; init; }
}

internal sealed class CapturedSale
{
    public string SaleKey { get; init; } = string.Empty;
    public int ReportedItemsCount { get; init; }
    public List<CapturedProduct> Products { get; } = new();
}

internal static class StatisticsStore
{
    private const float SaveDelaySeconds = 4f;
    private static string _path = string.Empty;
    private static ProductStatisticsData _data = new();
    private static float _lastCompletedAt = -1000f;
    private static bool _dirty;
    private static float _saveAt;

    public static void Initialize()
    {
        _path = Path.Combine(MelonEnvironment.UserDataDirectory, "WolfProductStatistics.json");
        Reload();
        _dirty = false;
        _saveAt = 0f;
    }

    public static void RecordSale(CapturedSale sale)
    {
        if (sale.Products.Count == 0)
            return;

        var slotKey = GetCurrentSlotKey();
        if (string.IsNullOrEmpty(slotKey))
        {
            MelonLogger.Warning("Product Statistics: продажа не записана — активный слот сохранения не определён.");
            return;
        }

        var slot = GetOrCreateSlot(slotKey);
        if (!slot.ProcessedSales.Add(sale.SaleKey))
            return;

        foreach (var product in sale.Products)
        {
            AddTo(slot.Total, product);
            AddTo(slot.CurrentDayProducts, product);
        }

        MarkDirty();
    }

    public static void CompleteDay(int day)
    {
        var slotKey = GetCurrentSlotKey();
        if (string.IsNullOrEmpty(slotKey))
            return;

        var slot = GetOrCreateSlot(slotKey);
        if (slot.LastCompletedDay == day)
            return;

        slot.LastCompletedDay = day;
        slot.LastCompletedDayProducts = slot.CurrentDayProducts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone());
        slot.CurrentDayProducts.Clear();
        slot.ProcessedSales.Clear();
        slot.CurrentDay = day + 1;
        Save();
        _lastCompletedAt = Time.unscaledTime;
        MelonLogger.Msg(
            $"Product Statistics: завершён день {day}, видов проданных товаров {slot.LastCompletedDayProducts.Count}.");
    }

    public static void CompleteDayAtNextDayStart()
    {
        // The results views are not guaranteed to be presented in every solo/coop path.
        // StatisticsService.HandleOnNextDayStart is the authoritative reset point.
        if (Time.unscaledTime - _lastCompletedAt < 5f)
            return;

        var slotKey = GetCurrentSlotKey();
        if (string.IsNullOrEmpty(slotKey))
            return;
        var slot = GetOrCreateSlot(slotKey);
        var completedDay = Math.Max(1, slot.CurrentDay > 0 ? slot.CurrentDay : slot.LastCompletedDay + 1);
        try
        {
            var timeController = UnityEngine.Object.FindObjectOfType<TimeController>();
            if (timeController != null)
            {
                var currentDay = timeController.GetCurrentDayNumber();
                if (currentDay > 1)
                    completedDay = currentDay - 1;
            }
        }
        catch
        {
            // Stored sequence is a safe fallback if the controller is between scenes.
        }
        CompleteDay(completedDay);
    }

    public static StatisticsSnapshot GetSnapshot()
    {
        var slotKey = GetCurrentSlotKey();
        if (string.IsNullOrEmpty(slotKey) || !_data.Slots.TryGetValue(slotKey, out var slot))
            return new StatisticsSnapshot { SlotKey = slotKey };

        return new StatisticsSnapshot
        {
            SlotKey = slotKey,
            LastCompletedDay = slot.LastCompletedDay,
            Total = slot.Total.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
            CurrentDay = slot.CurrentDayProducts.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
            LastDay = slot.LastCompletedDayProducts.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())
        };
    }

    private static SaveSlotStatistics GetOrCreateSlot(string key)
    {
        if (_data.Slots.TryGetValue(key, out var slot))
            return slot;
        slot = new SaveSlotStatistics();
        _data.Slots[key] = slot;
        return slot;
    }

    private static void AddTo(Dictionary<int, ProductAggregate> target, CapturedProduct product)
    {
        if (!target.TryGetValue(product.DefinitionId, out var aggregate))
        {
            aggregate = new ProductAggregate
            {
                DefinitionId = product.DefinitionId,
                Name = product.Name
            };
            target[product.DefinitionId] = aggregate;
        }
        aggregate.Add(product);
    }

    private static string GetCurrentSlotKey()
    {
        try
        {
            var saveService = AllServices.Get<SaveService>();
            if (saveService == null || saveService.SlotNumberActive < 0)
                return string.Empty;
            return $"slot_{saveService.SlotNumberActive}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool Reload()
    {
        if (!File.Exists(_path))
        {
            _data = new ProductStatisticsData();
            return true;
        }

        try
        {
            var loaded = JsonConvert.DeserializeObject<ProductStatisticsData>(File.ReadAllText(_path));
            _data = loaded ?? new ProductStatisticsData();
            _data.Slots ??= new Dictionary<string, SaveSlotStatistics>(StringComparer.Ordinal);
            Normalize();
            return true;
        }
        catch (Exception exception)
        {
            MelonLogger.Error(
                $"Product Statistics: файл данных не прочитан; запись отменена, чтобы не потерять статистику: {exception}");
            return false;
        }
    }

    private static void Normalize()
    {
        foreach (var slot in _data.Slots.Values)
        {
            slot.Total ??= new Dictionary<int, ProductAggregate>();
            slot.CurrentDayProducts ??= new Dictionary<int, ProductAggregate>();
            slot.LastCompletedDayProducts ??= new Dictionary<int, ProductAggregate>();
            slot.ProcessedSales ??= new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public static void Tick()
    {
        if (_dirty && Time.unscaledTime >= _saveAt)
            Save();
    }

    public static void Flush()
    {
        if (_dirty)
            Save();
    }

    private static void MarkDirty()
    {
        if (!_dirty)
            _saveAt = Time.unscaledTime + SaveDelaySeconds;
        _dirty = true;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(_data, Formatting.Indented));
            File.Move(temporaryPath, _path, true);
            _dirty = false;
            _saveAt = 0f;
        }
        catch (Exception exception)
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + SaveDelaySeconds;
            MelonLogger.Error($"Product Statistics: данные не сохранены: {exception}");
        }
    }
}

internal static class SaleCapture
{
    private static bool _emptySaleReported;

    public static CapturedSale? Capture(CashRegister register, int reportedItemsCount)
    {
        if (!ProductStatisticsMod.FeatureEnabled || register == null || reportedItemsCount <= 0)
            return null;

        try
        {
            var transaction = register._serverSaleTransaction;
            var buyerProducts = transaction?.BuyerProducts ?? register.ActiveBuyerProducts;
            var buyer = buyerProducts?.Buyer;
            var source = buyer?.AllProductItems;
            var registerProducts = register.Products;
            var fallback = registerProducts?.TakenProductItemsServer;

            if (fallback != null && fallback.Count == reportedItemsCount &&
                (source == null || source.Count != reportedItemsCount))
                source = fallback;

            if (source == null || source.Count == 0)
            {
                if (!_emptySaleReported)
                {
                    _emptySaleReported = true;
                    MelonLogger.Warning(
                        "Product Statistics: завершённый чек не содержит доступного списка товаров; чек пропущен.");
                }
                return null;
            }

            var saleId = transaction?.SaleId ?? 0;
            var revision = transaction?.Revision ?? 0;
            var fallbackIdentity = buyer != null ? buyer.GetInstanceID() : Time.frameCount;
            var key = saleId != 0
                ? $"{register.ObjectId}:{saleId}:{revision}"
                : $"{register.ObjectId}:fallback:{fallbackIdentity}:{reportedItemsCount}";
            var sale = new CapturedSale
            {
                SaleKey = key,
                ReportedItemsCount = reportedItemsCount
            };

            var productsService = AllServices.Get<ProductsService>();
            for (var index = 0; index < source.Count; index++)
            {
                var item = source[index];
                var info = productsService?.GetProductInfo(item.ProductId);
                var definitionId = item.VariantDefinitionId != 0
                    ? item.VariantDefinitionId
                    : info?.DefinitionId ?? 0;
                if (definitionId == 0)
                    continue;

                var salePrice = item.Price;
                if (salePrice <= 0.0001f)
                    salePrice = info?.CurrentPrice ?? 0f;
                var averageCost = Math.Max(0f, info?.AverageCost ?? 0f);
                sale.Products.Add(new CapturedProduct
                {
                    DefinitionId = definitionId,
                    Name = ProductDefinitionLookup.ResolveName(definitionId),
                    SalePrice = salePrice,
                    EstimatedProfit = salePrice - averageCost
                });
            }

            if (sale.Products.Count != reportedItemsCount)
            {
                MelonLogger.Warning(
                    $"Product Statistics: в чеке заявлено {reportedItemsCount}, распознано {sale.Products.Count} товаров.");
            }
            return sale.Products.Count > 0 ? sale : null;
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"Product Statistics: чек не распознан: {exception}");
            return null;
        }
    }

    public static CapturedSale? CaptureActiveCashRegister(int reportedItemsCount)
    {
        if (!ProductStatisticsMod.FeatureEnabled || reportedItemsCount <= 0)
            return null;

        try
        {
            foreach (var register in UnityEngine.Object.FindObjectsOfType<CashRegister>())
            {
                if (register == null)
                    continue;
                var transaction = register._serverSaleTransaction;
                var buyerProducts = transaction?.BuyerProducts ?? register.ActiveBuyerProducts;
                var source = buyerProducts?.Buyer?.AllProductItems;
                var fallback = register.Products?.TakenProductItemsServer;
                if ((source != null && source.Count == reportedItemsCount) ||
                    (fallback != null && fallback.Count == reportedItemsCount))
                    return Capture(register, reportedItemsCount);
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"Product Statistics: резервный поиск чека не выполнен: {exception.Message}");
        }
        return null;
    }

    public static CapturedSale? Capture(Buyer buyer, int reportedItemsCount, float reportedRevenue, string channel)
    {
        if (!ProductStatisticsMod.FeatureEnabled || buyer == null || reportedItemsCount <= 0)
            return null;

        try
        {
            var source = buyer.AllProductItems;
            if (source == null || source.Count == 0)
                return null;

            var sale = new CapturedSale
            {
                SaleKey = $"{channel}:{buyer.GetInstanceID()}:{Time.frameCount}:{reportedItemsCount}",
                ReportedItemsCount = reportedItemsCount
            };
            var productsService = AllServices.Get<ProductsService>();
            var fallbackPrice = reportedRevenue > 0f ? reportedRevenue / Math.Max(1, source.Count) : 0f;
            for (var index = 0; index < source.Count; index++)
            {
                var item = source[index];
                var info = productsService?.GetProductInfo(item.ProductId);
                var definitionId = item.VariantDefinitionId != 0
                    ? item.VariantDefinitionId
                    : info?.DefinitionId ?? 0;
                if (definitionId == 0)
                    continue;
                var salePrice = item.Price > 0.0001f ? item.Price : fallbackPrice;
                var averageCost = Math.Max(0f, info?.AverageCost ?? 0f);
                sale.Products.Add(new CapturedProduct
                {
                    DefinitionId = definitionId,
                    Name = ProductDefinitionLookup.ResolveName(definitionId),
                    SalePrice = salePrice,
                    EstimatedProfit = salePrice - averageCost
                });
            }
            return sale.Products.Count > 0 ? sale : null;
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"Product Statistics: специальная продажа не распознана: {exception}");
            return null;
        }
    }
}

internal static class ProductDefinitionLookup
{
    public static string ResolveName(int definitionId)
    {
        try
        {
            var definition = DataDefinition<ProductDefinition>.GetWithId(definitionId);
            if (definition != null)
            {
                if (!string.IsNullOrWhiteSpace(definition.FullName))
                    return definition.FullName;
                if (!string.IsNullOrWhiteSpace(definition.Name))
                    return definition.Name;
            }
        }
        catch
        {
            // Definitions may be unavailable during a scene transition.
        }
        return $"Товар #{definitionId}";
    }

    public static Sprite? ResolveSprite(int definitionId)
    {
        try
        {
            return DataDefinition<ProductDefinition>.GetWithId(definitionId)?.Icon;
        }
        catch
        {
            return null;
        }
    }
}

internal static class StatisticsPatches
{
    private static readonly HarmonyLib.Harmony Harmony = new("wolfmod.product_statistics");
    [ThreadStatic]
    private static int _checkoutCaptureDepth;

    private sealed class CheckoutCaptureState
    {
        public CapturedSale? Sale { get; init; }
        public bool Released { get; set; }
    }

    public static void Install()
    {
        PatchCheckout();
        PatchCompletedDays();
    }

    private static void PatchCheckout()
    {
        var original = AccessTools.Method(
            typeof(CashRegister),
            "RecordCheckoutCompleted",
            new[] { typeof(int), typeof(float), typeof(float) });
        if (original == null)
            throw new MissingMethodException(typeof(CashRegister).FullName, "RecordCheckoutCompleted");

        Harmony.Patch(
            original,
            prefix: new HarmonyMethod(typeof(StatisticsPatches), nameof(RecordCheckoutCompletedPrefix)),
            postfix: new HarmonyMethod(typeof(StatisticsPatches), nameof(RecordCheckoutCompletedPostfix)),
            finalizer: new HarmonyMethod(typeof(StatisticsPatches), nameof(RecordCheckoutCompletedFinalizer)));

        PatchSpecialCheckout(typeof(BuyerManga), nameof(BuyerMangaCheckoutPrefix));
        PatchSpecialCheckout(typeof(BuyerCourier), nameof(BuyerCourierCheckoutPrefix));

        var registerSoldProducts = AccessTools.Method(
            typeof(StatisticsService),
            "RegisterSoldProducts",
            new[] { typeof(int) });
        if (registerSoldProducts == null)
            throw new MissingMethodException(typeof(StatisticsService).FullName, "RegisterSoldProducts");
        Harmony.Patch(
            registerSoldProducts,
            prefix: new HarmonyMethod(typeof(StatisticsPatches), nameof(RegisterSoldProductsPrefix)));
        MelonLogger.Msg("Product Statistics: подключён учёт обычных и специальных завершённых продаж.");
    }

    private static void PatchSpecialCheckout(Type buyerType, string prefixName)
    {
        var original = AccessTools.Method(
            buyerType,
            "RecordCheckoutCompleted",
            new[] { typeof(float), typeof(float), typeof(int) });
        if (original == null)
            throw new MissingMethodException(buyerType.FullName, "RecordCheckoutCompleted");
        Harmony.Patch(
            original,
            prefix: new HarmonyMethod(typeof(StatisticsPatches), prefixName),
            postfix: new HarmonyMethod(typeof(StatisticsPatches), nameof(RecordCheckoutCompletedPostfix)),
            finalizer: new HarmonyMethod(typeof(StatisticsPatches), nameof(RecordCheckoutCompletedFinalizer)));
    }

    private static void PatchCompletedDays()
    {
        var parameterTypes = new[]
        {
            typeof(StatisticsService.DayStatistics),
            typeof(int),
            typeof(int)
        };
        PatchDayMethod("ShowEndDayResultsViewForHost", parameterTypes);
        PatchDayMethod("ShowEndDayResultsViewForClient", parameterTypes);

        var resetForNextDay = AccessTools.Method(typeof(StatisticsService), "HandleOnNextDayStart");
        if (resetForNextDay == null)
            throw new MissingMethodException(typeof(StatisticsService).FullName, "HandleOnNextDayStart");
        Harmony.Patch(
            resetForNextDay,
            prefix: new HarmonyMethod(typeof(StatisticsPatches), nameof(NextDayStartingPrefix)));
        MelonLogger.Msg("Product Statistics: подключена резервная фиксация дня перед сбросом игровой статистики.");
    }

    private static void PatchDayMethod(string methodName, Type[] parameterTypes)
    {
        var original = AccessTools.Method(typeof(TimeController), methodName, parameterTypes);
        if (original == null)
            throw new MissingMethodException(typeof(TimeController).FullName, methodName);
        Harmony.Patch(
            original,
            prefix: new HarmonyMethod(typeof(StatisticsPatches), nameof(EndDayResultsPrefix)));
        MelonLogger.Msg($"Product Statistics: подключено завершение дня через {methodName}.");
    }

    private static void RecordCheckoutCompletedPrefix(
        CashRegister __instance,
        int itemsCount,
        out CheckoutCaptureState __state)
    {
        _checkoutCaptureDepth++;
        __state = new CheckoutCaptureState { Sale = SaleCapture.Capture(__instance, itemsCount) };
    }

    private static void RecordCheckoutCompletedPostfix(CheckoutCaptureState? __state)
    {
        ReleaseCheckoutCapture(__state, recordSale: true);
    }

    private static Exception? RecordCheckoutCompletedFinalizer(
        Exception? __exception,
        CheckoutCaptureState? __state)
    {
        ReleaseCheckoutCapture(__state, recordSale: __exception == null);
        return __exception;
    }

    private static void RegisterSoldProductsPrefix(int count)
    {
        // RegisterSoldProducts normally runs inside one of the checkout methods
        // patched above. Its old fallback searched every cash register for every
        // sale; skip that duplicate global search while direct capture is active.
        if (_checkoutCaptureDepth > 0)
            return;
        var sale = SaleCapture.CaptureActiveCashRegister(count);
        if (sale != null)
            StatisticsStore.RecordSale(sale);
    }

    private static void BuyerMangaCheckoutPrefix(
        BuyerManga __instance,
        float revenue,
        int itemsCount,
        out CheckoutCaptureState __state)
    {
        _checkoutCaptureDepth++;
        __state = new CheckoutCaptureState
        {
            Sale = SaleCapture.Capture(__instance._buyer, itemsCount, revenue, "manga")
        };
    }

    private static void BuyerCourierCheckoutPrefix(
        BuyerCourier __instance,
        float revenue,
        int itemsCount,
        out CheckoutCaptureState __state)
    {
        _checkoutCaptureDepth++;
        __state = new CheckoutCaptureState
        {
            Sale = SaleCapture.Capture(__instance._buyer, itemsCount, revenue, "courier")
        };
    }

    private static void ReleaseCheckoutCapture(CheckoutCaptureState? state, bool recordSale)
    {
        if (state == null || state.Released)
            return;
        state.Released = true;
        try
        {
            if (recordSale && state.Sale != null)
                StatisticsStore.RecordSale(state.Sale);
        }
        finally
        {
            if (_checkoutCaptureDepth > 0)
                _checkoutCaptureDepth--;
        }
    }

    private static void EndDayResultsPrefix(int day)
    {
        if (ProductStatisticsMod.FeatureEnabled)
            StatisticsStore.CompleteDay(day);
    }

    private static void NextDayStartingPrefix()
    {
        if (ProductStatisticsMod.FeatureEnabled)
            StatisticsStore.CompleteDayAtNextDayStart();
    }

}

internal static class WolfModBridge
{
    private const string RegistryTypeName = "WolfCore.WolfModRegistry";
    public static bool Registered { get; private set; }
    public static string LastError { get; private set; } = string.Empty;

    public static bool TryRegister(
        string id,
        string displayName,
        string version,
        string description,
        Action<bool> onEnabledChanged,
        Action drawSettings,
        Func<bool> isCapturingInput)
    {
        if (Registered)
            return true;
        var registry = FindRegistryType();
        if (registry == null)
        {
            LastError = "Тип WolfCore.WolfModRegistry пока не найден.";
            return false;
        }

        try
        {
            var register = registry.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                    method.Name == "Register" &&
                    method.GetParameters().Length == 8 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
            var registerTerminal = registry.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                    method.Name == "RegisterTerminalPage" &&
                    method.GetParameters().Length == 8);
            if (register == null || registerTerminal == null)
            {
                LastError = "Установленная версия WolfCore не поддерживает централизованные вкладки терминала.";
                return false;
            }
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("WolfProductStatistics.Assets.statistics-tile.png");
            if (stream == null)
            {
                LastError = "В DLL отсутствует изображение плитки статистики.";
                return false;
            }
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            register.Invoke(null, new object?[]
            {
                id,
                displayName,
                version,
                description,
                onEnabledChanged,
                drawSettings,
                isCapturingInput,
                true
            });
            registerTerminal.Invoke(null, new object?[]
            {
                id,
                "wolfmod.product_statistics.terminal",
                "СТАТИСТИКА",
                memory.ToArray(),
                new Func<ComputerWorldView, bool>(ProductStatisticsWindow.Open),
                new Action(ProductStatisticsWindow.Close),
                new Func<bool>(() => ProductStatisticsWindow.IsOpen),
                new Action(ProductStatisticsWindow.TickInput)
            });
            Registered = true;
            LastError = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            LastError = exception.GetBaseException().Message;
            return false;
        }
    }

    public static void Unregister(string id)
    {
        if (!Registered)
            return;
        try
        {
            FindRegistryType()?.GetMethod(
                "Unregister",
                BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object?[] { id });
        }
        catch
        {
            // The core may already be shutting down.
        }
        Registered = false;
    }

    private static Type? FindRegistryType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, "WolfCore", StringComparison.Ordinal))
                return assembly.GetType(RegistryTypeName, false);
        }
        return null;
    }
}
