using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Polaris.Addons.Composition;
using Polaris.Addons.Definitions;
using Polaris.Addons.Runtime;
using Polaris.Content;

namespace Polaris.Addons.Catalog
{
    /// <summary>
    /// 反射发现各程序集里的 Provider（静态工厂方法）→ 逐条校验 → 组装成一份不可变的
    /// <see cref="AddonCatalog"/>。单条定义出错只报告并跳过，不影响其它定义。
    /// </summary>
    internal static class AddonCatalogBuilder
    {
        /// <summary>三种定义 Provider 共用同一个工厂方法名，各特性上的 FactoryMethodName 都是它。</summary>
        private const string DefinitionFactoryMethodName = ItemDefinitionProviderAttribute.FactoryMethodName;

        private const BindingFlags FactoryLookup =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static AddonCatalog Discover(
            IEnumerable<Assembly> assemblies,
            IModifierSink modifiers,
            IAddonStateStore state)
        {
            Assembly[] candidates = assemblies.Where(x => x != null).Distinct().ToArray();
            AddonServiceProvider services = BuildServices(candidates, modifiers, state);

            var items = new ContentCatalog<string, ItemDefinition>(StringComparer.Ordinal, ContentConflictPolicy.Aggregate);
            var plugins = new ContentCatalog<string, PluginDefinition>(StringComparer.Ordinal, ContentConflictPolicy.Aggregate);
            var skills = new ContentCatalog<string, SkillDefinition>(StringComparer.Ordinal, ContentConflictPolicy.Aggregate);
            var overlays = new ContentCatalog<string, ItemOverlay>(StringComparer.Ordinal, ContentConflictPolicy.Aggregate);

            foreach (Assembly assembly in candidates)
            {
                foreach (Type type in PolarisAPI.Types.Of(assembly))
                {
                    TryRegister(type, typeof(ItemDefinitionProviderAttribute), items);
                    TryRegister(type, typeof(PluginDefinitionProviderAttribute), plugins);
                    TryRegister(type, typeof(SkillDefinitionProviderAttribute), skills);
                    TryRegisterOverlay(type, overlays);
                }
            }

            Dictionary<string, ItemRegistration> registrations = LinkFacetsToItems(items, plugins, skills);
            RemoveOrphans(plugins, "Plugin", x => x.ItemId, registrations);
            RemoveOrphans(skills, "Skill", x => x.ItemId, registrations);

            return new AddonCatalog(
                registrations,
                plugins.Snapshot,
                skills.Snapshot,
                overlays.Snapshot,
                services);
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
                foreach (Type type in PolarisAPI.Types.Of(assembly))
                {
                    if (type.IsAbstract || !typeof(IAddonModule).IsAssignableFrom(type)
                        || !IsProvider(type, typeof(AddonModuleAttribute)))
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
                        AddonDiagnostics.Report(ex, "configuring Addons module " + type.FullName, assembly);
                    }
                }
            }

            return collection.Build();
        }

        private static void TryRegister<TDefinition>(
            Type providerType,
            Type attributeType,
            ContentCatalog<string, TDefinition> definitions)
            where TDefinition : AddonDefinition
        {
            if (!IsProvider(providerType, attributeType))
            {
                return;
            }

            try
            {
                TDefinition definition = InvokeFactory<TDefinition>(providerType, DefinitionFactoryMethodName);
                if (definitions.TryRegister(definition.Id, definition, definition.ProviderAssembly.FullName))
                {
                    return;
                }

                definitions.TryGet(definition.Id, out TDefinition existing);
                throw new AddonDefinitionException(
                    "Definition id '" + definition.Id + "' is declared twice by " +
                    existing.ProviderAssembly.GetName().Name + " and " +
                    definition.ProviderAssembly.GetName().Name + ".");
            }
            catch (Exception ex)
            {
                AddonDiagnostics.Report(
                    AddonDiagnostics.Unwrap(ex),
                    "registering Addons provider " + providerType.FullName,
                    providerType.Assembly);
            }
        }

        private static void TryRegisterOverlay(Type providerType, ContentCatalog<string, ItemOverlay> overlays)
        {
            if (!IsProvider(providerType, typeof(ItemOverlayProviderAttribute)))
            {
                return;
            }

            try
            {
                ItemOverlay overlay = InvokeFactory<ItemOverlay>(
                    providerType,
                    ItemOverlayProviderAttribute.FactoryMethodName);
                if (!overlays.TryRegister(overlay.Id, overlay, providerType.Assembly.FullName))
                {
                    throw new AddonDefinitionException("Overlay id '" + overlay.Id + "' is declared twice.");
                }
            }
            catch (Exception ex)
            {
                AddonDiagnostics.Report(
                    AddonDiagnostics.Unwrap(ex),
                    "registering Addons Overlay provider " + providerType.FullName,
                    providerType.Assembly);
            }
        }

        /// <summary>第三方 Addon 约定的发现形状：Provider 类上的特性 + 无参静态工厂方法。</summary>
        private static TResult InvokeFactory<TResult>(Type providerType, string methodName)
            where TResult : class
        {
            MethodInfo factory = providerType.GetMethod(methodName, FactoryLookup);
            if (factory == null || factory.GetParameters().Length != 0
                || !typeof(TResult).IsAssignableFrom(factory.ReturnType))
            {
                throw new AddonDefinitionException(
                    providerType.FullName + " must declare a parameterless static " + methodName +
                    "() returning " + typeof(TResult).Name + ".");
            }

            return (TResult)factory.Invoke(null, null)
                ?? throw new AddonDefinitionException(
                    providerType.FullName + "." + methodName + "() returned null.");
        }

        /// <summary>特性本身都加载不了（依赖缺失、类型加载失败）时，只能整体跳过这个类型。</summary>
        private static bool IsProvider(Type type, Type attributeType)
        {
            try
            {
                return type.IsClass && type.GetCustomAttribute(attributeType, false) != null;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, ItemRegistration> LinkFacetsToItems(
            ContentCatalog<string, ItemDefinition> items,
            ContentCatalog<string, PluginDefinition> plugins,
            ContentCatalog<string, SkillDefinition> skills)
        {
            IReadOnlyDictionary<string, PluginDefinition> declaredPlugins = plugins.Snapshot;
            IReadOnlyDictionary<string, SkillDefinition> declaredSkills = skills.Snapshot;
            var registrations = new Dictionary<string, ItemRegistration>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ItemDefinition> entry in items.Snapshot)
            {
                registrations.Add(entry.Key, new ItemRegistration(
                    entry.Value,
                    OwnedBy(declaredPlugins.Values, entry.Key, x => x.ItemId),
                    OwnedBy(declaredSkills.Values, entry.Key, x => x.ItemId)));
            }

            return registrations;
        }

        private static TDefinition[] OwnedBy<TDefinition>(
            IEnumerable<TDefinition> facets,
            string itemId,
            Func<TDefinition, string> ownerItemIdOf)
            where TDefinition : AddonDefinition =>
            facets.Where(x => string.Equals(ownerItemIdOf(x), itemId, StringComparison.Ordinal))
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .ToArray();

        /// <summary>目标物品不存在的 Facet 不进入最终目录；每条单独报告，不影响其它定义。</summary>
        private static void RemoveOrphans<TDefinition>(
            ContentCatalog<string, TDefinition> facets,
            string kind,
            Func<TDefinition, string> ownerItemIdOf,
            IReadOnlyDictionary<string, ItemRegistration> items)
            where TDefinition : AddonDefinition
        {
            foreach (TDefinition facet in facets.Snapshot.Values.ToArray())
            {
                string ownerItemId = ownerItemIdOf(facet);
                if (items.ContainsKey(ownerItemId))
                {
                    continue;
                }

                AddonDiagnostics.Report(
                    new AddonDefinitionException(
                        kind + " '" + facet.Id + "' targets missing item '" + ownerItemId + "'."),
                    "linking Addons " + kind.ToLowerInvariant() + " " + facet.Id,
                    facet.ProviderAssembly);
                facets.Remove(facet.Id);
            }
        }
    }
}
