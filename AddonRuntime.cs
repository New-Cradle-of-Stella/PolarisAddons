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
        private static SaveHandle<AddonSaveData> save;
        private static AddonStateStore state;
        private static ModifierEngine modifiers;
        private static FacetRuntime facets;
        private static AliceItemCatalogAdapter items;
        private static AliceFacetAdapter facetAdapter;
        private static AddonSaveData observedState;
        private static readonly List<IDisposable> callbacks = new List<IDisposable>();

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
            if (Catalog != null) return;
            RegisterPersistence();
            modifiers = new ModifierEngine();
            state = new AddonStateStore(() => save.Current);
            Catalog = AddonCatalogBuilder.Discover(assemblies ?? Array.Empty<Assembly>(), modifiers, state);
            facets = new FacetRuntime(Catalog, state);
            items = new AliceItemCatalogAdapter(Catalog);
            facetAdapter = new AliceFacetAdapter(Catalog, facets);
            observedState = save.Current;
            RegisterCallbacks();
            TryInstallGameAdapter();
        }

        internal static void Pump()
        {
            if (save != null && !ReferenceEquals(observedState, save.Current))
            {
                observedState = save.Current;
                facetAdapter?.ApplySavedState();
                return;
            }
            facetAdapter?.ObserveSkills(true);
        }

        internal static void TryInstallGameAdapter()
        {
            if (items == null) return;
            try { IsGameAdapterInstalled = items.TryInstall(); }
            catch (Exception ex) { IsGameAdapterInstalled = false; Report(ex, "installing the Addons item adapter"); }
            InstallEnhancers();
            InstallSkills();
        }

        internal static void InstallEnhancers() { try { facetAdapter?.InstallEnhancers(); } catch (Exception ex) { Report(ex, "installing Addons Enhancers"); } }
        internal static void InstallSkills() { try { facetAdapter?.InstallSkills(); } catch (Exception ex) { Report(ex, "installing Addons skills"); } }
        internal static void ObserveEnhancers(ItemStorage precious, ItemStorage enhancer) => facetAdapter?.ObserveEnhancers(precious, enhancer, true);

        internal static bool TryExecuteCustomItem(string nativeKey, int grade, out int result)
        {
            result = 0;
            if (items != null && items.TryExecuteCustom(nativeKey, grade, out result)) return true;
            return facetAdapter != null && facetAdapter.TryUseFacetItem(nativeKey, out result);
        }

        internal static NativeItemUseInvocation BeginNativeItemUse(string nativeKey, int grade) => items?.BeginNative(nativeKey, grade);
        internal static void CompleteNativeItemUse(NativeItemUseInvocation invocation, int nativeResult) => items?.CompleteNative(invocation, nativeResult);
        internal static bool IsCustomNativeItem(string nativeKey) => items != null && items.IsCustom(nativeKey);

        internal static void NotifyOwnerItemUsed(string itemId, ItemUseResult result)
        {
            if (result != ItemUseResult.Succeeded) return;
            facets?.UnlockOwnedFacets(itemId, true);
            facetAdapter?.ApplySavedState();
        }

        internal static string PluginText(ENHA.Enhancer enhancer, bool description) => facetAdapter?.PluginTitle(enhancer, description);
        internal static string SkillText(PrSkill skill, bool description) => facetAdapter?.SkillTitle(skill, description);
        internal static List<SkillSerializationState> SuppressCustomSkills() => facetAdapter?.SuppressCustomSkills();
        internal static void RestoreCustomSkills(IEnumerable<SkillSerializationState> states) => facetAdapter?.RestoreCustomSkills(states);
        internal static bool SetPluginEnabled(string id, bool enabled) => facetAdapter != null && facetAdapter.SetPluginEnabled(id, enabled);
        internal static bool SetPluginObtained(string id, bool obtained) => facetAdapter != null && facetAdapter.SetPluginObtained(id, obtained);
        internal static bool SetSkillEnabled(string id, bool enabled) => facetAdapter != null && facetAdapter.SetSkillEnabled(id, enabled);
        internal static bool SetSkillObtained(string id, bool obtained) => facetAdapter != null && facetAdapter.SetSkillObtained(id, obtained);
        internal static ValueTask<SkillExecutionResult> ExecuteSkillAsync(string id, CancellationToken token) =>
            facets == null ? new ValueTask<SkillExecutionResult>(SkillExecutionResult.Rejected) : facets.ExecuteSkillAsync(id, token);

        internal static void Shutdown()
        {
            for (int index = callbacks.Count - 1; index >= 0; index--) callbacks[index].Dispose();
            callbacks.Clear();
            facetAdapter?.Dispose();
            items?.Dispose();
            facets?.Dispose();
            facetAdapter = null;
            items = null;
            facets = null;
            state = null;
            modifiers = null;
            Catalog = null;
            IsGameAdapterInstalled = false;
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
                        facetAdapter?.ApplySavedState();
                    }
                }));
        }

        private static void Report(Exception exception, string operation)
        {
            try { PolarisAPI.Errors.Report(exception, operation, typeof(AddonRuntime).Assembly); }
            catch { }
        }
    }
}
