using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Polaris.Addons.Composition;
using Polaris.Addons.Definitions;
using Polaris.Addons.Runtime;

namespace Polaris.Addons.Catalog
{
    public sealed class ItemRegistration
    {
        internal ItemRegistration(
            ItemDefinition item,
            IReadOnlyList<PluginDefinition> plugins,
            IReadOnlyList<SkillDefinition> skills)
        {
            Item = item;
            Plugins = plugins;
            Skills = skills;
        }

        public ItemDefinition Item { get; }

        public IReadOnlyList<PluginDefinition> Plugins { get; }

        public IReadOnlyList<SkillDefinition> Skills { get; }
    }

    /// <summary>启动时构建一次的不可变目录；插件和技能通过 ItemId 反向挂回物品。</summary>
    public sealed class AddonCatalog
    {
        private readonly IReadOnlyDictionary<string, ItemRegistration> items;
        private readonly IReadOnlyDictionary<string, PluginDefinition> plugins;
        private readonly IReadOnlyDictionary<string, SkillDefinition> skills;
        private readonly IReadOnlyDictionary<string, ItemOverlay> overlays;
        private readonly AddonServiceProvider services;
        private readonly NativeContentMirror native = new NativeContentMirror();

        internal AddonCatalog(
            IReadOnlyDictionary<string, ItemRegistration> items,
            IReadOnlyDictionary<string, PluginDefinition> plugins,
            IReadOnlyDictionary<string, SkillDefinition> skills,
            IReadOnlyDictionary<string, ItemOverlay> overlays,
            AddonServiceProvider services)
        {
            this.items = Freeze(items);
            this.plugins = Freeze(plugins);
            this.skills = Freeze(skills);
            this.overlays = Freeze(overlays);
            this.services = services;
        }

        /// <summary>供工具和非游戏进程构建同一份目录。</summary>
        public static AddonCatalog Discover(IEnumerable<Assembly> assemblies)
        {
            var saved = new AddonSaveData();
            return AddonCatalogBuilder.Discover(
                assemblies ?? Array.Empty<Assembly>(),
                new ModifierEngine(),
                new AddonStateStore(() => saved));
        }

        public IReadOnlyCollection<ItemRegistration> Items => items.Values.ToArray();

        public IReadOnlyCollection<PluginDefinition> Plugins => plugins.Values.ToArray();

        public IReadOnlyCollection<SkillDefinition> Skills => skills.Values.ToArray();

        public IReadOnlyCollection<ItemOverlay> ItemOverlays => InExecutionOrder(overlays.Values);

        public IReadOnlyCollection<NativeItemDescriptor> NativeItems =>
            native.Items.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

        public IReadOnlyCollection<IItemDescriptor> AllItems => items.Values
            .Select(x => (IItemDescriptor)x.Item)
            .Concat(native.Items.Values)
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        public IReadOnlyCollection<IPluginDescriptor> AllPlugins => plugins.Values
            .Cast<IPluginDescriptor>()
            .Concat(native.Plugins.Values)
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        public IReadOnlyCollection<ISkillDescriptor> AllSkills => skills.Values
            .Cast<ISkillDescriptor>()
            .Concat(native.Skills.Values)
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        public ItemRegistration GetItem(string id) =>
            id != null && items.TryGetValue(id, out ItemRegistration value) ? value : null;

        public PluginDefinition GetPlugin(string id) =>
            id != null && plugins.TryGetValue(id, out PluginDefinition value) ? value : null;

        public SkillDefinition GetSkill(string id) =>
            id != null && skills.TryGetValue(id, out SkillDefinition value) ? value : null;

        public IItemDescriptor GetItemDescriptor(string id)
        {
            ItemRegistration addon = GetItem(id);
            if (addon != null)
            {
                return addon.Item;
            }

            return id != null && native.Items.TryGetValue(id, out NativeItemDescriptor descriptor)
                ? descriptor
                : null;
        }

        public IReadOnlyList<ItemOverlay> GetItemOverlays(string targetItemId) => InExecutionOrder(
            overlays.Values.Where(x => string.Equals(x.TargetItemId, targetItemId, StringComparison.Ordinal)));

        public object CreateBehavior(AddonDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return CreateBehavior(definition.BehaviorType);
        }

        internal object CreateBehavior(Type behaviorType) =>
            behaviorType == null ? null : services.Create(behaviorType);

        internal void ReplaceNativeItems(IEnumerable<NativeItemDescriptor> descriptors)
        {
            var replacement = new Dictionary<string, NativeItemDescriptor>(StringComparer.Ordinal);
            foreach (NativeItemDescriptor descriptor in descriptors ?? Array.Empty<NativeItemDescriptor>())
            {
                if (descriptor == null)
                {
                    continue;
                }

                if (items.ContainsKey(descriptor.Id))
                {
                    throw new AddonDefinitionException(
                        "Native item id '" + descriptor.Id + "' conflicts with an Addons item.");
                }

                if (replacement.ContainsKey(descriptor.Id))
                {
                    throw new AddonDefinitionException(
                        "Native item id '" + descriptor.Id + "' is projected more than once.");
                }

                replacement.Add(descriptor.Id, descriptor);
            }

            native.ReplaceItems(replacement);
        }

        internal void ReplaceNativePlugins(IEnumerable<NativePluginDescriptor> descriptors) =>
            native.ReplacePlugins(descriptors);

        internal void ReplaceNativeSkills(IEnumerable<NativeSkillDescriptor> descriptors) =>
            native.ReplaceSkills(descriptors);

        private static ItemOverlay[] InExecutionOrder(IEnumerable<ItemOverlay> candidates) => candidates
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        private static IReadOnlyDictionary<string, TValue> Freeze<TValue>(
            IEnumerable<KeyValuePair<string, TValue>> source) =>
            new ReadOnlyDictionary<string, TValue>(
                new Dictionary<string, TValue>(source, StringComparer.Ordinal));
    }
}
