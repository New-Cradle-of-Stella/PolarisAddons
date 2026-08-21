using System;
using System.Collections.Generic;
using System.Linq;
using nel;
using PixelLiner;
using Polaris.Addons.Catalog;
using Polaris.Addons.Definitions;
using Polaris.Addons.Runtime;

namespace Polaris.Addons.Adapters
{
    /// <summary>
    /// 插件（原版 Enhancer）的目录与 UI 镜像：只负责把定义投影成原版对象、把原版状态同步回
    /// <see cref="FacetRuntime"/>。玩法状态与效果本身归 FacetRuntime。
    /// </summary>
    internal sealed class AlicePluginAdapter : IDisposable
    {
        private readonly AddonCatalog catalog;
        private readonly FacetRuntime runtime;

        private readonly Dictionary<string, PluginBinding> plugins =
            new Dictionary<string, PluginBinding>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> pluginIdsByKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private ItemStorage enhancerStorage;
        private ItemStorage preciousStorage;

        internal AlicePluginAdapter(AddonCatalog catalog, FacetRuntime runtime)
        {
            this.catalog = catalog;
            this.runtime = runtime;
        }

        internal void InstallEnhancers()
        {
            if (ENHA.AEh == null)
            {
                return;
            }

            DropStaleBindings();

            // 先按当前目录投影原版 Enhancer，再安装自定义插件：这样首次安装新建的 Enhancer
            // 不会被当成原版内容镜像进目录。
            List<NativePluginDescriptor> native = ProjectNativeEnhancers();
            InstallCustomPlugins();
            catalog.ReplaceNativePlugins(native);

            if (enhancerStorage != null)
            {
                ApplySavedPlugins(enhancerStorage);
            }
        }

        internal void ObserveEnhancers(ItemStorage precious, ItemStorage storage, bool persist)
        {
            if (storage == null)
            {
                return;
            }

            if (precious != null)
            {
                preciousStorage = precious;
            }

            enhancerStorage = storage;
            foreach (PluginBinding binding in plugins.Values)
            {
                ItemStorage.ObtainInfo info = FindInfo(storage, binding.Item);
                bool obtained = info != null && storage.getCount(binding.Item) > 0;
                bool active = obtained && (info.top_grade & 2) == 2;
                runtime.SyncPlugin(binding.Definition.Id, obtained, active, persist);
            }
        }

        internal void ApplySavedState()
        {
            if (enhancerStorage != null)
            {
                ApplySavedPlugins(enhancerStorage);
            }
        }

        internal bool SetPluginEnabled(string id, bool enabled)
        {
            if (!plugins.TryGetValue(id, out PluginBinding binding) || enhancerStorage == null)
            {
                return false;
            }

            ItemStorage.ObtainInfo info = FindInfo(enhancerStorage, binding.Item);
            if (info == null)
            {
                return false;
            }

            info.changeGradeForPrecious(WithActiveBit(info.top_grade, enabled));
            if (preciousStorage != null)
            {
                ENHA.fineEnhancerStorage(preciousStorage, enhancerStorage);
            }
            else
            {
                M2PrSkill.resetSkillConnectionWhole();
            }

            ObserveEnhancers(preciousStorage, enhancerStorage, true);
            return true;
        }

        internal bool SetPluginObtained(string id, bool obtained)
        {
            if (!plugins.TryGetValue(id, out PluginBinding binding) || enhancerStorage == null)
            {
                return false;
            }

            int count = enhancerStorage.getCount(binding.Item, -1);
            if (obtained && count == 0)
            {
                enhancerStorage.Add(binding.Item, 1, 0, true, true);
            }

            if (!obtained && count > 0)
            {
                enhancerStorage.Reduce(binding.Item, count, -1, true);
            }

            ObserveEnhancers(preciousStorage, enhancerStorage, true);
            return obtained == (enhancerStorage.getCount(binding.Item, -1) > 0);
        }

        internal string PluginTitle(ENHA.Enhancer enhancer, bool description)
        {
            if (enhancer == null || !pluginIdsByKey.TryGetValue(enhancer.key, out string id)
                || !plugins.TryGetValue(id, out PluginBinding binding))
            {
                return null;
            }

            return AdapterText.Resolve(
                description ? binding.Definition.DescriptionKey : binding.Definition.TitleKey,
                id);
        }

        public void Dispose()
        {
            plugins.Clear();
            pluginIdsByKey.Clear();
        }

        /// <summary>原版重跑 initScript 后旧对象作废；重新安装前先丢掉指向它们的绑定。</summary>
        private void DropStaleBindings()
        {
            string[] stale = plugins
                .Where(x => !ReferenceEquals(ENHA.Get(x.Value.Enhancer.key), x.Value.Enhancer)
                    || !ReferenceEquals(NelItem.GetById(x.Value.Item.key, true), x.Value.Item))
                .Select(x => x.Key)
                .ToArray();
            foreach (string id in stale)
            {
                pluginIdsByKey.Remove(plugins[id].Enhancer.key);
                plugins.Remove(id);
            }
        }

        private List<NativePluginDescriptor> ProjectNativeEnhancers()
        {
            var native = new List<NativePluginDescriptor>();
            foreach (ENHA.Enhancer enhancer in ENHA.AEh.ToArray())
            {
                if (enhancer == null || pluginIdsByKey.ContainsKey(enhancer.key))
                {
                    continue;
                }

                string itemKey = ENHA.enhancer_item_header + enhancer.key;
                native.Add(new NativePluginDescriptor(
                    NativeFacetId.Plugin(enhancer.key),
                    NativeItemId.FromKey(itemKey),
                    enhancer.key,
                    AdapterSafe.Read(() => enhancer.title, enhancer.key),
                    AdapterSafe.Read(() => enhancer.descript, string.Empty),
                    enhancer.key,
                    enhancer.cost));
            }

            return native;
        }

        private void InstallCustomPlugins()
        {
            foreach (PluginDefinition definition in catalog.Plugins.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (plugins.ContainsKey(definition.Id))
                {
                    continue;
                }

                try
                {
                    string key = AdapterKey.For("plugin", definition.Id);
                    string itemKey = ENHA.enhancer_item_header + key;
                    ENHA.Enhancer enhancer = ENHA.Get(key) ?? CreateEnhancer(key, definition);
                    NelItem item = NelItem.GetById(itemKey, true) ?? CreateEnhancerItem(itemKey);
                    item.value = definition.Cost;
                    plugins.Add(definition.Id, new PluginBinding(definition, enhancer, item));
                    pluginIdsByKey[key] = definition.Id;
                }
                catch (Exception ex)
                {
                    AddonDiagnostics.Report(ex, "installing Addons plugin " + definition.Id);
                }
            }
        }

        private static ENHA.Enhancer CreateEnhancer(string key, PluginDefinition definition)
        {
            var enhancer = new ENHA.Enhancer(key, ResolveEnhancerFrame(definition.Icon))
            {
                cost = definition.Cost,
                ehbit = 0,
                tx_suffix = key,
            };
            ENHA.AEh.Add(enhancer);
            return enhancer;
        }

        private static NelItem CreateEnhancerItem(string itemKey) => NelItem.CreateItemEntry(
            itemKey,
            new NelItem(itemKey, 0, 600, 1)
            {
                category = (NelItem.CATEG)10485761u,
                FnGetName = NelItem.fnGetNameEnhancer,
                FnGetDesc = NelItem.fnGetDescEnhancer,
                FnGetDetail = NelItem.fnGetDetailEnhancer,
            },
            ushort.MaxValue);

        private void ApplySavedPlugins(ItemStorage storage)
        {
            foreach (PluginBinding binding in plugins.Values)
            {
                ItemStorage.ObtainInfo info = FindInfo(storage, binding.Item);
                if (runtime.IsObtained(binding.Definition.Id) && info == null)
                {
                    storage.Add(binding.Item, 1, 0, true, true);
                    info = FindInfo(storage, binding.Item);
                }

                if (info != null)
                {
                    info.changeGradeForPrecious(
                        WithActiveBit(info.top_grade, runtime.IsEnabled(binding.Definition.Id)));
                }
            }

            ObserveEnhancers(preciousStorage, storage, false);
        }

        /// <summary>原版用 top_grade 的第 2 位表示"已装配"。</summary>
        private static int WithActiveBit(int grade, bool active) => active ? grade | 2 : grade & ~2;

        private static ItemStorage.ObtainInfo FindInfo(ItemStorage storage, NelItem item) =>
            storage.getWholeInfoDictionary().TryGetValue(item, out ItemStorage.ObtainInfo info) ? info : null;

        private static PxlFrame ResolveEnhancerFrame(string icon)
        {
            if (ENHA.SqImgIcon == null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(icon)
                ? ENHA.SqImgIcon.getFrame(0)
                : ENHA.SqImgIcon.getFrameByName(icon) ?? ENHA.SqImgIcon.getFrame(0);
        }

        private sealed class PluginBinding
        {
            internal PluginBinding(PluginDefinition definition, ENHA.Enhancer enhancer, NelItem item)
            {
                Definition = definition;
                Enhancer = enhancer;
                Item = item;
            }

            internal PluginDefinition Definition { get; }

            internal ENHA.Enhancer Enhancer { get; }

            internal NelItem Item { get; }
        }
    }
}
