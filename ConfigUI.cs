using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PriceInsight;

internal class ConfigUI(PriceInsightPlugin plugin) : IDisposable {
    private bool settingsVisible = false;

    public bool SettingsVisible {
        get => settingsVisible;
        set => settingsVisible = value;
    }

    public void Dispose() {
    }

    public void Draw() {
        if (!SettingsVisible) {
            return;
        }

        var conf = plugin.Configuration;
        if (ImGui.Begin("Price Insight Config", ref settingsVisible,
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize)) {
            var configValue = conf.RefreshWithAlt;
            if (ImGui.Checkbox("Tap Alt to refresh prices", ref configValue)) {
                conf.RefreshWithAlt = configValue;
                conf.Save();
            }

            configValue = conf.PrefetchInventory;
            if (ImGui.Checkbox("Prefetch prices for items in inventory", ref configValue)) {
                conf.PrefetchInventory = configValue;
                conf.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Prefetch prices for all items in inventory, chocobo saddlebag and retainer when logging in.");

            configValue = conf.UseCurrentWorld;
            if (ImGui.Checkbox("Use current world as home world", ref configValue)) {
                conf.UseCurrentWorld = configValue;
                conf.Save();
                plugin.ClearCache();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The current world you're on will be considered your \"home world\".\nUseful if you're datacenter travelling and want to see prices there.");

            ImGui.Separator();
            ImGui.PushID(0);

            ImGui.Text("Show cheapest price in:");

            configValue = conf.ShowRegion;
            if (ImGui.Checkbox("Region", ref configValue)) {
                conf.ShowRegion = configValue;
                conf.Save();
            }
            TooltipRegion();

            configValue = conf.ShowDatacenter;
            if (ImGui.Checkbox("Datacenter", ref configValue)) {
                conf.ShowDatacenter = configValue;
                conf.Save();
            }

            configValue = conf.ShowWorld;
            if (ImGui.Checkbox("Home world", ref configValue)) {
                conf.ShowWorld = configValue;
                conf.Save();
            }

            ImGui.PopID();
            ImGui.Separator();
            ImGui.PushID(1);

            ImGui.Text("Show most recent purchase in:");

            configValue = conf.ShowMostRecentPurchaseRegion;
            if (ImGui.Checkbox("Region", ref configValue)) {
                conf.ShowMostRecentPurchaseRegion = configValue;
                conf.Save();
            }
            TooltipRegion();

            configValue = conf.ShowMostRecentPurchase;
            if (ImGui.Checkbox("Datacenter", ref configValue)) {
                conf.ShowMostRecentPurchase = configValue;
                conf.Save();
            }

            configValue = conf.ShowMostRecentPurchaseWorld;
            if (ImGui.Checkbox("Home world", ref configValue)) {
                conf.ShowMostRecentPurchaseWorld = configValue;
                conf.Save();
            }

            ImGui.PopID();
            ImGui.Separator();

            var selectValue = conf.ShowDailySaleVelocityIn;
            var velocityItems = new[] { "Do not show", "World", "Datacenter", "Region" };
            if (ImGui.Combo("Show sales per day", ref selectValue, velocityItems, velocityItems.Length)) {
                conf.ShowDailySaleVelocityIn = selectValue;
                conf.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show the average sales per day based on sales of the last 4 days.");

            selectValue = conf.ShowAverageSalePriceIn;
            var avgPriceItems = new[] { "Do not show", "World", "Datacenter", "Region" };
            if (ImGui.Combo("Show average sale price", ref selectValue, avgPriceItems, avgPriceItems.Length)) {
                conf.ShowAverageSalePriceIn = selectValue;
                conf.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show the average sale price based on sales of the last 4 days.");

            configValue = conf.ShowStackSalePrice;
            if (ImGui.Checkbox("Show stack sale price", ref configValue)) {
                conf.ShowStackSalePrice = configValue;
                conf.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show the price of the hovered stack if sold at the given unit price.");

            configValue = conf.ShowAge;
            if (ImGui.Checkbox("Show age of data", ref configValue)) {
                conf.ShowAge = configValue;
                conf.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show when the price info was last refreshed.\nCan be turned off to reduce tooltip bloat.");

            configValue = conf.ShowDatacenterOnCrossWorlds;
            if (ImGui.Checkbox("Show datacenter for foreign worlds", ref configValue)) {
                conf.ShowDatacenterOnCrossWorlds = configValue;
                conf.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show the datacenter for worlds from other datacenters when displaying prices for the entire region.\nCan be turned off to reduce tooltip bloat.");

            configValue = conf.ShowBothNqAndHq;
            if (ImGui.Checkbox("Always display NQ and HQ prices", ref configValue)) {
                conf.ShowBothNqAndHq = configValue;
                conf.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show the prices for both NQ and HQ of an item.\nWhen turned off will only display price for the current quality (use Ctrl to switch between NQ and HQ).");

            ImGui.Separator();

            // porting-note(api13) MUST RE-APPLY: Universalis added the TC data centre 陸行鳥
            // (worlds 4028-4035) some time after 2026-06-13, so a TC home world now resolves on its
            // own and this warning is a fallback rather than the normal state. The override below
            // stays as the escape hatch. Verified 2026-09-19 against the live /api/v2/worlds and
            // /api/v2/data-centers, and against this client's own World sheet.
            if (plugin.ItemPriceLookup.WorldUnsupported && conf.UniversalisWorldIdOverride == 0) {
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Your world is not tracked by Universalis.");
                ImGui.TextWrapped("Set a world ID override below to view reference prices from another world.");
            }

            ImGui.SetNextItemWidth(120);
            var worldOverride = (int)conf.UniversalisWorldIdOverride;
            if (ImGui.InputInt("Universalis world ID override", ref worldOverride, 0, 0)) {
                conf.UniversalisWorldIdOverride = worldOverride < 0 ? 0u : (uint)worldOverride;
                conf.Save();
                plugin.ClearCache();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "Leave this at 0 to use your own home world — Universalis now tracks the TC\n" +
                    "data centre 陸行鳥: 伊弗利特=4028, 迦樓羅=4029, 利維坦=4030, 鳳凰=4031,\n" +
                    "奧汀=4032, 巴哈姆特=4033, 拉姆=4034, 泰坦=4035.\n" +
                    "Set an override only to read another world's prices as a reference, or if your\n" +
                    "own world ever stops being tracked. Japan examples: Carbuncle=40, Tonberry=68.\n" +
                    "Full list: https://universalis.app/api/v2/worlds");
        }

        ImGui.End();
    }

    private static void TooltipRegion() {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Include all datacenters available via datacenter traveling.");
    }
}