using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using nel;
using Polaris.Addons.Catalog;
using Polaris.Addons.Definitions;
using Polaris.Addons.Runtime;
using XX;

namespace Polaris.Addons.Adapters
{
    /// <summary>Alice in Cradle ver029 的物品目录边界；所有 NelItem 引用止于此文件。</summary>
    internal sealed class AliceItemCatalogAdapter : IDisposable
    {
        private readonly AddonCatalog catalog;
        private ItemExecutionPipeline pipeline;
        private readonly object gate = new object();
        private IReadOnlyDictionary<string, string> customIdsByKey = EmptyMap();
        private IReadOnlyDictionary<string, string> itemIdsByKey = EmptyMap();
        private bool disposed;

        internal AliceItemCatalogAdapter(AddonCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        internal bool TryInstall()
        {
            if (disposed || !NelItem.preparedData())
            {
                return false;
            }

            var expectedCustomIds = catalog.Items.ToDictionary(
                registration => AdapterKey.For("item", registration.Item.Id),
                registration => registration.Item.Id,
                StringComparer.Ordinal);

            var projected = new List<NativeItemDescriptor>();
            var projectedIdsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, NelItem> entry in NelItem.getWholeDictionary().ToArray())
            {
                if (entry.Value == null || expectedCustomIds.ContainsKey(entry.Key))
                {
                    continue;
                }

                try
                {
                    string id = NativeItemId.FromKey(entry.Key);
                    projected.Add(ToDescriptor(id, entry.Key, entry.Value));
                    projectedIdsByKey.Add(entry.Key, id);
                }
                catch (Exception ex)
                {
                    Report(ex, "projecting native item " + entry.Key);
                }
            }

            var installedCustomIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ItemRegistration registration in catalog.Items.OrderBy(x => x.Item.Id, StringComparer.Ordinal))
            {
                try
                {
                    string key = AdapterKey.For("item", registration.Item.Id);
                    NelItem native = NelItem.GetById(key, true);
                    if (native == null)
                    {
                        native = CreateNativeItem(key, registration.Item);
                    }
                    else if (native.id != ushort.MaxValue)
                    {
                        throw new InvalidOperationException(
                            "Generated key '" + key + "' is already owned by native item id " + native.id + ".");
                    }

                    ConfigureNativeItem(native, registration.Item);
                    installedCustomIds.Add(key, registration.Item.Id);
                }
                catch (Exception ex)
                {
                    Report(ex, "installing Addons item " + registration.Item.Id);
                }
            }

            catalog.ReplaceNativeItems(projected);
            pipeline ??= new ItemExecutionPipeline(catalog);
            foreach (KeyValuePair<string, string> custom in installedCustomIds)
            {
                projectedIdsByKey[custom.Key] = custom.Value;
            }

            lock (gate)
            {
                customIdsByKey = new Dictionary<string, string>(installedCustomIds, StringComparer.Ordinal);
                itemIdsByKey = new Dictionary<string, string>(projectedIdsByKey, StringComparer.Ordinal);
            }

            return true;
        }

        internal bool TryExecuteCustom(string nativeKey, int grade, out int nativeResult)
        {
            nativeResult = 0;
            if (!TryGetCustomId(nativeKey, out string itemId))
            {
                return false;
            }

            ItemUseResult result = pipeline?.ExecuteCustom(itemId, grade) ?? ItemUseResult.Rejected;
            AddonRuntime.NotifyOwnerItemUsed(itemId, result);
            nativeResult = result == ItemUseResult.Succeeded ? 1 : 0;
            return true;
        }

        internal NativeItemUseInvocation BeginNative(string nativeKey, int grade)
        {
            if (!TryGetItemId(nativeKey, out string itemId) || TryGetCustomId(nativeKey, out _))
            {
                return null;
            }

            return pipeline?.BeginNative(itemId, grade);
        }

        internal void CompleteNative(NativeItemUseInvocation invocation, int nativeResult) =>
            pipeline?.CompleteNative(invocation, nativeResult);

        internal bool IsCustom(string nativeKey) => TryGetCustomId(nativeKey, out _);

        internal bool TryResolveCustomId(string nativeKey, out string itemId) =>
            TryGetCustomId(nativeKey, out itemId);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pipeline?.Dispose();
            pipeline = null;
            lock (gate)
            {
                customIdsByKey = EmptyMap();
                itemIdsByKey = EmptyMap();
            }
        }

        private bool TryGetCustomId(string key, out string id)
        {
            id = null;
            lock (gate)
            {
                return key != null && customIdsByKey.TryGetValue(key, out id);
            }
        }

        private bool TryGetItemId(string key, out string id)
        {
            id = null;
            lock (gate)
            {
                return key != null && itemIdsByKey.TryGetValue(key, out id);
            }
        }

        private static NelItem CreateNativeItem(string key, ItemDefinition definition)
        {
            var item = new NelItem(key, 0, definition.Price, definition.StackLimit);
            NelItem installed = NelItem.CreateItemEntry(key, item, ushort.MaxValue);
            if (installed == null)
            {
                throw new InvalidOperationException("NelItem rejected Addons key '" + key + "'.");
            }

            return installed;
        }

        private static void ConfigureNativeItem(NelItem item, ItemDefinition definition)
        {
            item.price = definition.Price;
            item.stock = definition.StackLimit;
            item.category = NelItem.calcCateg(definition.Category ?? string.Empty);
            if (int.TryParse(definition.Icon, out int iconId))
            {
                item.specific_icon_id = iconId;
            }

            item.FnGetName = (builder, _, __) =>
                builder.Set(AdapterText.Resolve(definition.NameKey, definition.Id));
            item.FnGetDesc = (builder, _, __) =>
                builder.Set(AdapterText.Resolve(definition.DescriptionKey, string.Empty));
            item.fineNameLocalized();
        }

        private static NativeItemDescriptor ToDescriptor(string id, string key, NelItem item)
        {
            string name = key;
            try
            {
                name = item.getLocalizedName(0) ?? key;
            }
            catch
            {
                // 文案系统尚未就绪时仍保留稳定 key，下一次目录重装会刷新快照。
            }

            string description = string.Empty;
            try
            {
                using (STB builder = TX.PopBld())
                {
                    item.getDescLocalized(builder, null, 0);
                    description = builder.ToString();
                }
            }
            catch
            {
                // 部分原版描述要求场景或存档已就绪；镜像仍可保留其余元数据。
            }

            return new NativeItemDescriptor(
                id,
                key,
                name,
                description,
                item.specific_icon_id.ToString(),
                item.price,
                item.stock,
                item.category.ToString());
        }

        private static IReadOnlyDictionary<string, string> EmptyMap() =>
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static void Report(Exception exception, string operation)
        {
            try
            {
                PolarisAPI.Errors.Report(exception, operation, typeof(AliceItemCatalogAdapter).Assembly);
            }
            catch
            {
                // 目录安装是逐项隔离的；诊断不可用也不能中断其余物品。
            }
        }
    }

    [HarmonyPatch(typeof(NelItem), nameof(NelItem.readItemScript))]
    internal static class Patch_NelItem_ReadItemScript_Addons
    {
        [HarmonyPostfix]
        private static void Postfix() => AddonRuntime.TryInstallGameAdapter();
    }

    [HarmonyPatch(typeof(NelItem), nameof(NelItem.Use))]
    internal static class Patch_NelItem_Use_Addons
    {
        [HarmonyPrefix]
        private static bool Prefix(
            NelItem __instance,
            int grade,
            ref int __result,
            out NativeItemUseInvocation __state)
        {
            __state = null;
            if (__instance == null)
            {
                return true;
            }

            if (AddonRuntime.TryExecuteCustomItem(__instance.key, grade, out int customResult))
            {
                __result = customResult;
                return false;
            }

            __state = AddonRuntime.BeginNativeItemUse(__instance.key, grade);
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(int __result, NativeItemUseInvocation __state) =>
            AddonRuntime.CompleteNativeItemUse(__state, __result);
    }

    [HarmonyPatch(typeof(NelItem), "get_useable")]
    internal static class Patch_NelItem_Useable_Addons
    {
        [HarmonyPostfix]
        private static void Postfix(NelItem __instance, ref bool __result)
        {
            if (!__result && __instance != null && AddonRuntime.IsCustomNativeItem(__instance.key))
            {
                __result = true;
            }
        }
    }
}
