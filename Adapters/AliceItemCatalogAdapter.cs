using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly object gate = new object();
        private ItemExecutionPipeline pipeline;
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

            // 自定义物品占用的 key 不参与原版镜像，否则同一个物品会既是扩展内容又是原版内容。
            var customKeys = new HashSet<string>(
                catalog.Items.Select(x => AdapterKey.For("item", x.Item.Id)),
                StringComparer.Ordinal);

            List<NativeItemDescriptor> projected = ProjectNativeItems(customKeys);
            Dictionary<string, string> installedCustomIds = InstallCustomItems();

            catalog.ReplaceNativeItems(projected);
            pipeline ??= new ItemExecutionPipeline(catalog);

            // 原版 key → Addons id 的整表：先是镜像出来的原版物品，再覆盖上安装成功的自定义物品。
            var idsByKey = projected.ToDictionary(x => x.NativeKey, x => x.Id, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> custom in installedCustomIds)
            {
                idsByKey[custom.Key] = custom.Value;
            }

            lock (gate)
            {
                customIdsByKey = installedCustomIds;
                itemIdsByKey = idsByKey;
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

        /// <summary>把游戏目录里的原版物品抓成只读快照；单个物品投影失败不影响其余物品。</summary>
        private List<NativeItemDescriptor> ProjectNativeItems(HashSet<string> customKeys)
        {
            var projected = new List<NativeItemDescriptor>();
            foreach (KeyValuePair<string, NelItem> entry in NelItem.getWholeDictionary().ToArray())
            {
                if (entry.Value == null || customKeys.Contains(entry.Key))
                {
                    continue;
                }

                try
                {
                    projected.Add(ToDescriptor(NativeItemId.FromKey(entry.Key), entry.Key, entry.Value));
                }
                catch (Exception ex)
                {
                    AddonDiagnostics.Report(ex, "projecting native item " + entry.Key);
                }
            }

            return projected;
        }

        /// <summary>把扩展物品投影成原版 NelItem；返回安装成功的 原版 key → Addons id。</summary>
        private Dictionary<string, string> InstallCustomItems()
        {
            var installed = new Dictionary<string, string>(StringComparer.Ordinal);
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
                    installed.Add(key, registration.Item.Id);
                }
                catch (Exception ex)
                {
                    AddonDiagnostics.Report(ex, "installing Addons item " + registration.Item.Id);
                }
            }

            return installed;
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
            // 文案系统尚未就绪时保留稳定 key，下一次目录重装会刷新快照。
            string name = AdapterSafe.Read(() => item.getLocalizedName(0) ?? key, key);

            // 部分原版描述要求场景或存档已就绪；镜像仍可保留其余元数据。
            string description = AdapterSafe.Read(
                () =>
                {
                    using (STB builder = TX.PopBld())
                    {
                        item.getDescLocalized(builder, null, 0);
                        return builder.ToString();
                    }
                },
                string.Empty);

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
    }
}
