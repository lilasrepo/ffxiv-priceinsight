using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EasyCaching.InMemory;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace PriceInsight;

public class ItemPriceLookup : IDisposable {
    private readonly InMemoryCaching cache = new("prices", new InMemoryCachingOptions { EnableReadDeepClone = false });
    private readonly ConcurrentQueue<uint> requestedItems = new();
    private readonly ConcurrentDictionary<uint, (Task Task, CancellationTokenSource Token)> activeTasks = new();
    private readonly PriceInsightPlugin plugin;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private uint? homeWorldId;
    private bool worldUnsupported = false;
    public bool WorldUnsupported => worldUnsupported;

    public ItemPriceLookup(PriceInsightPlugin plugin) {
        this.plugin = plugin;
        Task.Run(ProcessQueue, cancellationTokenSource.Token)
            // Silently ignore cancel
            .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
    }

    public bool CheckReady() {
        var lp = Service.ClientState.LocalPlayer;
        if (lp == null) return false;
        if (plugin.Configuration.UseCurrentWorld) {
            homeWorldId ??= lp.CurrentWorld.RowId;
        } else {
            homeWorldId ??= lp.HomeWorld.RowId;
        }

        return homeWorldId != null;
    }

    public (MarketBoardData? MarketBoardData, LookupState State) Get(ulong fullItemId, bool refresh) {
        if (!ToMarketableItemId(fullItemId, out var itemId))
            return (null, LookupState.NonMarketable);
        if (worldUnsupported && plugin.Configuration.UniversalisWorldIdOverride == 0)
            return (null, LookupState.NonMarketable);

        if (refresh) {
            cache.Remove(itemId.ToString());
            if (activeTasks.TryRemove(itemId, out var t))
                t.Token.Cancel();
        } else {
            if (cache.Get<MarketBoardData>(itemId.ToString()) is { IsNull: false, Value: var mbData })
                return (mbData, LookupState.Marketable);
            if (activeTasks.TryGetValue(itemId, out var t))
                return (null, t.Task.IsFaulted ? LookupState.Faulted : LookupState.Marketable);
        }

        requestedItems.Enqueue(itemId);

        return (null, LookupState.Marketable);
    }

    private static bool ToMarketableItemId(ulong fullItemId, out uint itemId, ExcelSheet<Item>? sheet = null) {
        itemId = (uint)(fullItemId % 500000);
        if (fullItemId is >= 2000000 or >= 500000 and < 1000000)
            return false;
        sheet ??= Service.DataManager.Excel.GetSheet<Item>();
        return sheet.GetRowOrDefault(itemId) is not null and not { ItemSearchCategory.RowId: 0 };
    }

    public void Fetch(IEnumerable<uint> items) {
        if (worldUnsupported && plugin.Configuration.UniversalisWorldIdOverride == 0) return;
        var itemSheet = Service.DataManager.Excel.GetSheet<Item>();
        foreach (var id in items) {
            if (!ToMarketableItemId(id, out var itemId, itemSheet))
                continue;
            if (cache.Get(itemId.ToString()) != null || (activeTasks.TryGetValue(itemId, out var t) && !t.Task.IsFaulted))
                continue;
            if (!requestedItems.Contains(itemId))
                requestedItems.Enqueue(itemId);
        }
    }

    private async Task ProcessQueue() {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(cancellationTokenSource.Token)) {
            if (requestedItems.IsEmpty)
                continue;
            var items = new HashSet<uint>();
            while (items.Count < 50 && requestedItems.TryDequeue(out var item))
                items.Add(item);
            await FetchInternal(items);
        }

        timer.Dispose();
    }

    private Task<Dictionary<uint, MarketBoardData>?> FetchInternal(ICollection<uint> itemIds) {
        var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);
        var itemTask = FetchItemTask();

        foreach (var id in itemIds) {
            var task = Task.Run(async () => {
                var items = await itemTask;
                if (items != null && items.TryGetValue(id, out var value))
                    cache.Set(id.ToString(), value, TimeSpan.FromMinutes(90));
                activeTasks.TryRemove(id, out _);
            }, token.Token);
            task.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
            activeTasks[id] = (task, token);
        }

        return itemTask;

        async Task<Dictionary<uint, MarketBoardData>?> FetchItemTask() {
            if (!homeWorldId.HasValue)
                return null;
            var worldId = plugin.Configuration.UniversalisWorldIdOverride > 0
                ? plugin.Configuration.UniversalisWorldIdOverride
                : homeWorldId.Value;
            var fetchStart = DateTime.Now;
            Dictionary<uint, MarketBoardData>? result;
            try {
                result = await plugin.UniversalisClientV2.GetMarketBoardDataList(worldId, itemIds, token.Token);
            } catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest) {
                if (!worldUnsupported) {
                    Service.PluginLog.Warning(
                        "World {0} is not tracked by Universalis (HTTP 400). Price data unavailable. " +
                        "Set a world ID override in /priceinsight settings (e.g. 40 = Carbuncle JP).", worldId);
                    worldUnsupported = true;
                }
                return null; // silent — no FetchFailed tooltip, no re-request
            }
            if (result != null)
                plugin.ItemPriceTooltip.Refresh(result);
            else
                plugin.ItemPriceTooltip.FetchFailed(itemIds);
            Service.PluginLog.Debug($"Fetching {itemIds.Count} items took {(DateTime.Now - fetchStart).TotalMilliseconds:F0}ms");
            return result;
        }
    }

    public void Dispose() {
        cancellationTokenSource.Cancel();
    }
}
