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
        private readonly object nativeGate = new object();
        private IReadOnlyDictionary<string, NativeItemDescriptor> nativeItems =
            new ReadOnlyDictionary<string, NativeItemDescriptor>(
                new Dictionary<string, NativeItemDescriptor>(StringComparer.Ordinal));
        private IReadOnlyDictionary<string, NativePluginDescriptor> nativePlugins =
            new ReadOnlyDictionary<string, NativePluginDescriptor>(new Dictionary<string, NativePluginDescriptor>(StringComparer.Ordinal));
        private IReadOnlyDictionary<string, NativeSkillDescriptor> nativeSkills =
            new ReadOnlyDictionary<string, NativeSkillDescriptor>(new Dictionary<string, NativeSkillDescriptor>(StringComparer.Ordinal));

        internal AddonCatalog(
            IDictionary<string, ItemRegistration> items,
            IDictionary<string, PluginDefinition> plugins,
            IDictionary<string, SkillDefinition> skills,
            IDictionary<string, ItemOverlay> overlays,
            AddonServiceProvider services)
        {
            this.items = new ReadOnlyDictionary<string, ItemRegistration>(
                new Dictionary<string, ItemRegistration>(items, StringComparer.Ordinal));
            this.plugins = new ReadOnlyDictionary<string, PluginDefinition>(
                new Dictionary<string, PluginDefinition>(plugins, StringComparer.Ordinal));
            this.skills = new ReadOnlyDictionary<string, SkillDefinition>(
                new Dictionary<string, SkillDefinition>(skills, StringComparer.Ordinal));
            this.overlays = new ReadOnlyDictionary<string, ItemOverlay>(
                new Dictionary<string, ItemOverlay>(overlays, StringComparer.Ordinal));
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

        public IReadOnlyCollection<ItemOverlay> ItemOverlays => overlays.Values
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        public IReadOnlyCollection<NativeItemDescriptor> NativeItems
        {
            get
            {
                lock (nativeGate)
                {
                    return nativeItems.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
                }
            }
        }

        public IReadOnlyCollection<IItemDescriptor> AllItems
        {
            get
            {
                IItemDescriptor[] addons = items.Values.Select(x => (IItemDescriptor)x.Item).ToArray();
                lock (nativeGate)
                {
                    return addons.Concat(nativeItems.Values)
                        .OrderBy(x => x.Id, StringComparer.Ordinal)
                        .ToArray();
                }
            }
        }

        public IReadOnlyCollection<IPluginDescriptor> AllPlugins
        {
            get { lock (nativeGate) { return plugins.Values.Cast<IPluginDescriptor>().Concat(nativePlugins.Values).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(); } }
        }

        public IReadOnlyCollection<ISkillDescriptor> AllSkills
        {
            get { lock (nativeGate) { return skills.Values.Cast<ISkillDescriptor>().Concat(nativeSkills.Values).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(); } }
        }

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

            lock (nativeGate)
            {
                return id != null && nativeItems.TryGetValue(id, out NativeItemDescriptor native)
                    ? native
                    : null;
            }
        }

        public IReadOnlyList<ItemOverlay> GetItemOverlays(string targetItemId) => overlays.Values
            .Where(x => string.Equals(x.TargetItemId, targetItemId, StringComparison.Ordinal))
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        public object CreateBehavior(AddonDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return definition.BehaviorType == null ? null : services.Create(definition.BehaviorType);
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

            lock (nativeGate)
            {
                nativeItems = new ReadOnlyDictionary<string, NativeItemDescriptor>(replacement);
            }
        }

        internal void ReplaceNativeFacets(
            IEnumerable<NativePluginDescriptor> pluginDescriptors,
            IEnumerable<NativeSkillDescriptor> skillDescriptors)
        {
            lock (nativeGate)
            {
                nativePlugins = new ReadOnlyDictionary<string, NativePluginDescriptor>(
                    (pluginDescriptors ?? Array.Empty<NativePluginDescriptor>()).ToDictionary(x => x.Id, StringComparer.Ordinal));
                nativeSkills = new ReadOnlyDictionary<string, NativeSkillDescriptor>(
                    (skillDescriptors ?? Array.Empty<NativeSkillDescriptor>()).ToDictionary(x => x.Id, StringComparer.Ordinal));
            }
        }
    }

    internal static class AddonCatalogBuilder
    {
        internal static AddonCatalog Discover(
            IEnumerable<Assembly> assemblies,
            IModifierSink modifiers,
            IAddonStateStore state)
        {
            Assembly[] candidates = assemblies.Where(x => x != null).Distinct().ToArray();
            AddonServiceProvider services = BuildServices(candidates, modifiers, state);

            var items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            var plugins = new Dictionary<string, PluginDefinition>(StringComparer.Ordinal);
            var skills = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
            var overlays = new Dictionary<string, ItemOverlay>(StringComparer.Ordinal);

            foreach (Assembly assembly in candidates)
            {
                foreach (Type type in SafeTypes(assembly))
                {
                    TryRegister(type, typeof(ItemDefinitionProviderAttribute), items);
                    TryRegister(type, typeof(PluginDefinitionProviderAttribute), plugins);
                    TryRegister(type, typeof(SkillDefinitionProviderAttribute), skills);
                    TryRegisterOverlay(type, overlays);
                }
            }

            var registrations = new Dictionary<string, ItemRegistration>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ItemDefinition> entry in items)
            {
                IReadOnlyList<PluginDefinition> ownedPlugins = plugins.Values
                    .Where(x => string.Equals(x.ItemId, entry.Key, StringComparison.Ordinal))
                    .OrderBy(x => x.Id, StringComparer.Ordinal)
                    .ToArray();
                IReadOnlyList<SkillDefinition> ownedSkills = skills.Values
                    .Where(x => string.Equals(x.ItemId, entry.Key, StringComparison.Ordinal))
                    .OrderBy(x => x.Id, StringComparer.Ordinal)
                    .ToArray();
                registrations.Add(entry.Key, new ItemRegistration(entry.Value, ownedPlugins, ownedSkills));
            }

            // 目标物品不存在的 Facet 不进入最终目录。异常已经在这里按单条报告，不影响其它定义。
            foreach (PluginDefinition plugin in plugins.Values.ToArray())
            {
                if (!registrations.ContainsKey(plugin.ItemId))
                {
                    Report(new AddonDefinitionException(
                        "Plugin '" + plugin.Id + "' targets missing item '" + plugin.ItemId + "'."),
                        "linking Addons plugin " + plugin.Id,
                        plugin.ProviderAssembly);
                    plugins.Remove(plugin.Id);
                }
            }

            foreach (SkillDefinition skill in skills.Values.ToArray())
            {
                if (!registrations.ContainsKey(skill.ItemId))
                {
                    Report(new AddonDefinitionException(
                        "Skill '" + skill.Id + "' targets missing item '" + skill.ItemId + "'."),
                        "linking Addons skill " + skill.Id,
                        skill.ProviderAssembly);
                    skills.Remove(skill.Id);
                }
            }

            return new AddonCatalog(registrations, plugins, skills, overlays, services);
        }

        private static void TryRegisterOverlay(
            Type providerType,
            IDictionary<string, ItemOverlay> overlays)
        {
            bool marked;
            try
            {
                marked = providerType.IsClass &&
                    providerType.GetCustomAttribute(typeof(ItemOverlayProviderAttribute), false) != null;
            }
            catch
            {
                return;
            }

            if (!marked)
            {
                return;
            }

            try
            {
                MethodInfo factory = providerType.GetMethod(
                    ItemOverlayProviderAttribute.FactoryMethodName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (factory == null || factory.GetParameters().Length != 0 ||
                    !typeof(ItemOverlay).IsAssignableFrom(factory.ReturnType))
                {
                    throw new AddonDefinitionException(
                        providerType.FullName + " must declare a parameterless static " +
                        ItemOverlayProviderAttribute.FactoryMethodName + "() returning ItemOverlay.");
                }

                var overlay = (ItemOverlay)factory.Invoke(null, null);
                if (overlay == null)
                {
                    throw new AddonDefinitionException(providerType.FullName + " returned a null Overlay.");
                }

                if (overlays.ContainsKey(overlay.Id))
                {
                    throw new AddonDefinitionException("Overlay id '" + overlay.Id + "' is declared twice.");
                }

                overlays.Add(overlay.Id, overlay);
            }
            catch (Exception ex)
            {
                Exception report = ex is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException
                    : ex;
                Report(report, "registering Addons Overlay provider " + providerType.FullName, providerType.Assembly);
            }
        }

        private static AddonServiceProvider BuildServices(
            IEnumerable<Assembly> assemblies,
            IModifierSink modifiers,
            IAddonStateStore state)
        {
            var collection = new AddonServiceCollection();
            collection.AddSingleton(modifiers ?? throw new ArgumentNullException(nameof(modifiers)));
            collection.AddSingleton(state ?? throw new ArgumentNullException(nameof(state)));
            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in SafeTypes(assembly))
                {
                    if (type.IsAbstract || !typeof(IAddonModule).IsAssignableFrom(type)
                        || type.GetCustomAttribute<AddonModuleAttribute>(false) == null)
                    {
                        continue;
                    }

                    try
                    {
                        var module = (IAddonModule)Activator.CreateInstance(type);
                        module.Configure(collection);
                    }
                    catch (Exception ex)
                    {
                        Report(ex, "configuring Addons module " + type.FullName, assembly);
                    }
                }
            }

            return collection.Build();
        }

        private static void TryRegister<TDefinition>(
            Type providerType,
            Type attributeType,
            IDictionary<string, TDefinition> definitions)
            where TDefinition : AddonDefinition
        {
            if (!providerType.IsClass || providerType.GetCustomAttribute(attributeType, false) == null)
            {
                return;
            }

            try
            {
                MethodInfo factory = providerType.GetMethod(
                    "BuildDefinition",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (factory == null || factory.GetParameters().Length != 0
                    || !typeof(TDefinition).IsAssignableFrom(factory.ReturnType))
                {
                    throw new AddonDefinitionException(
                        providerType.FullName + " must declare a parameterless static BuildDefinition() returning " +
                        typeof(TDefinition).Name + ".");
                }

                var definition = (TDefinition)factory.Invoke(null, null);
                if (definition == null)
                {
                    throw new AddonDefinitionException(providerType.FullName + ".BuildDefinition() returned null.");
                }

                if (definitions.TryGetValue(definition.Id, out TDefinition existing))
                {
                    throw new AddonDefinitionException(
                        "Definition id '" + definition.Id + "' is declared twice by " +
                        existing.ProviderAssembly.GetName().Name + " and " +
                        definition.ProviderAssembly.GetName().Name + ".");
                }

                definitions.Add(definition.Id, definition);
            }
            catch (Exception ex)
            {
                Exception report = ex is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException
                    : ex;
                Report(report, "registering Addons provider " + providerType.FullName, providerType.Assembly);
            }
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(x => x != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static void Report(Exception exception, string operation, Assembly owner)
        {
            try
            {
                PolarisAPI.Errors.Report(exception, operation, owner);
            }
            catch
            {
                // Core 诊断尚未就绪时，单条坏定义仍然只应被跳过。
            }
        }
    }
}
