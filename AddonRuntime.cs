using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using nel;
using Polaris.Addons.Adapters;
using Polaris.Addons.Catalog;
using Polaris.Addons.Runtime;
using Polaris.API;
using Polaris.Save;

namespace Polaris.Addons
{
    internal static class AddonRuntime
    {
        private static readonly List<IDisposable> callbacks = new List<IDisposable>();
        private static SaveHandle<AddonSaveData> save;
        private static AddonStateStore state;
        private static ModifierEngine modifiers;
        private static FacetRuntime facets;
        private static AliceItemCatalogAdapter items;
        private static AlicePluginAdapter pluginAdapter;
        private static AliceSkillAdapter skillAdapter;
        private static AddonSaveData observedState;

        internal static AddonCatalog Catalog { get; private set; }

        internal static bool IsReady => Catalog != null;

        internal static bool IsGameAdapterInstalled { get; private set; }

        internal static IModifierSink Modifiers => modifiers;

        internal static IAddonStateStore State => state;

        internal static void RegisterPersistence()
        {
            if (save == null)
            {
                save = SaveAPI.Register<AddonSaveData>("polaris.addons/state", 1);
            }
        }

        internal static void Initialize(IEnumerable<Assembly> assemblies)
        {
            if (Catalog != null)
            {
                return;
            }

            RegisterPersistence();
            modifiers = new ModifierEngine();
            state = new AddonStateStore(() => save.Current);
            Catalog = AddonCatalogBuilder.Discover(assemblies ?? Array.Empty<Assembly>(), modifiers, state);
            facets = new FacetRuntime(Catalog, state);
            items = new AliceItemCatalogAdapter(Catalog);
            pluginAdapter = new AlicePluginAdapter(Catalog, facets);
            skillAdapter = new AliceSkillAdapter(Catalog, facets);
            observedState = save.Current;
            RegisterCallbacks();
            TryInstallGameAdapter();
        }

        internal static void Pump()
        {
            if (save != null && !ReferenceEquals(observedState, save.Current))
            {
                observedState = save.Current;
                ApplySavedState();
                return;
            }

            skillAdapter?.ObserveSkills(true);
        }

        internal static void TryInstallGameAdapter()
        {
            if (items == null)
            {
                return;
            }

            try
            {
                IsGameAdapterInstalled = items.TryInstall();
            }
            catch (Exception ex)
            {
                IsGameAdapterInstalled = false;
                AddonDiagnostics.Report(ex, "installing the Addons item adapter");
            }

            InstallEnhancers();
            InstallSkills();
        }

        internal static void InstallEnhancers()
        {
            try
            {
                pluginAdapter?.InstallEnhancers();
            }
            catch (Exception ex)
            {
                AddonDiagnostics.Report(ex, "installing Addons Enhancers");
            }
        }

        internal static void InstallSkills()
        {
            try
            {
                skillAdapter?.InstallSkills();
            }
            catch (Exception ex)
            {
                AddonDiagnostics.Report(ex, "installing Addons skills");
            }
        }

        internal static void ObserveEnhancers(ItemStorage precious, ItemStorage enhancer) =>
            pluginAdapter?.ObserveEnhancers(precious, enhancer, true);

        internal static bool TryExecuteCustomItem(string nativeKey, int grade, out int result)
        {
            result = 0;
            if (items != null && items.TryExecuteCustom(nativeKey, grade, out result))
            {
                return true;
            }

            return skillAdapter != null && skillAdapter.TryUseSkillBook(nativeKey, out result);
        }

        internal static NativeItemUseInvocation BeginNativeItemUse(string nativeKey, int grade) =>
            items?.BeginNative(nativeKey, grade);

        internal static void CompleteNativeItemUse(NativeItemUseInvocation invocation, int nativeResult) =>
            items?.CompleteNative(invocation, nativeResult);

        internal static bool IsCustomNativeItem(string nativeKey) => items != null && items.IsCustom(nativeKey);

        internal static void NotifyOwnerItemUsed(string itemId, ItemUseResult result)
        {
            if (result != ItemUseResult.Succeeded)
            {
                return;
            }

            facets?.UnlockOwnedFacets(itemId, true);
            ApplySavedState();
        }

        internal static string PluginText(ENHA.Enhancer enhancer, bool description) =>
            pluginAdapter?.PluginTitle(enhancer, description);

        internal static string SkillText(PrSkill skill, bool description) =>
            skillAdapter?.SkillTitle(skill, description);

        internal static List<SkillSerializationState> SuppressCustomSkills() =>
            skillAdapter?.SuppressCustomSkills();

        internal static void RestoreCustomSkills(IEnumerable<SkillSerializationState> states) =>
            skillAdapter?.RestoreCustomSkills(states);

        internal static bool SetPluginEnabled(string id, bool enabled) =>
            pluginAdapter != null && pluginAdapter.SetPluginEnabled(id, enabled);

        internal static bool SetPluginObtained(string id, bool obtained) =>
            pluginAdapter != null && pluginAdapter.SetPluginObtained(id, obtained);

        internal static bool SetSkillEnabled(string id, bool enabled) =>
            skillAdapter != null && skillAdapter.SetSkillEnabled(id, enabled);

        internal static bool SetSkillObtained(string id, bool obtained) =>
            skillAdapter != null && skillAdapter.SetSkillObtained(id, obtained);

        internal static ValueTask<SkillExecutionResult> ExecuteSkillAsync(string id, CancellationToken token) =>
            facets == null
                ? new ValueTask<SkillExecutionResult>(SkillExecutionResult.Rejected)
                : facets.ExecuteSkillAsync(id, token);

        internal static void Shutdown()
        {
            for (int index = callbacks.Count - 1; index >= 0; index--)
            {
                callbacks[index].Dispose();
            }

            callbacks.Clear();
            pluginAdapter?.Dispose();
            skillAdapter?.Dispose();
            items?.Dispose();
            facets?.Dispose();
            pluginAdapter = null;
            skillAdapter = null;
            items = null;
            facets = null;
            state = null;
            modifiers = null;
            Catalog = null;
            IsGameAdapterInstalled = false;
        }

        /// <summary>存档换档或物品解锁后，把两侧投影重新对齐到存档状态。</summary>
        private static void ApplySavedState()
        {
            pluginAdapter?.ApplySavedState();
            skillAdapter?.ApplySavedState();
        }

        private static void RegisterCallbacks()
        {
            callbacks.Add(PolarisAPI.Game.Callbacks.Register<MapChangedCallbackData>(
                GameStaticCallbackKind.MapChanged, _ => facets?.CancelExecutions()));
            callbacks.Add(PolarisAPI.Game.Callbacks.Register<ItemObtainedCallbackData>(
                GameStaticCallbackKind.ItemObtained, data =>
                {
                    if (data?.Item != null && items != null && items.TryResolveCustomId(data.Item.Key, out string itemId))
                    {
                        facets?.UnlockOwnedFacets(itemId, false);
                        ApplySavedState();
                    }
                }));
        }
    }
}
