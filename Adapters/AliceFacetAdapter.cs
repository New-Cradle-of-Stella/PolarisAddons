using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using nel;
using PixelLiner;
using Polaris.Addons.Catalog;
using Polaris.Addons.Definitions;
using Polaris.Addons.Runtime;

namespace Polaris.Addons.Adapters
{
    /// <summary>Enhancer/Skill 只负责原版目录和 UI 镜像；玩法状态与效果归 FacetRuntime。</summary>
    internal sealed class AliceFacetAdapter : IDisposable
    {
        private readonly AddonCatalog catalog;
        private readonly FacetRuntime runtime;
        private readonly Dictionary<string, PluginBinding> plugins = new Dictionary<string, PluginBinding>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillBinding> skills = new Dictionary<string, SkillBinding>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> pluginIdsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> skillIdsByBookKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private ItemStorage enhancerStorage;
        private ItemStorage preciousStorage;

        internal AliceFacetAdapter(AddonCatalog catalog, FacetRuntime runtime)
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

            foreach (string id in plugins.Where(x =>
                !ReferenceEquals(ENHA.Get(x.Value.Enhancer.key), x.Value.Enhancer) ||
                !ReferenceEquals(NelItem.GetById(x.Value.Item.key, true), x.Value.Item))
                .Select(x => x.Key).ToArray())
            {
                pluginIdsByKey.Remove(plugins[id].Enhancer.key);
                plugins.Remove(id);
            }

            var native = new List<NativePluginDescriptor>();
            foreach (ENHA.Enhancer enhancer in ENHA.AEh.ToArray())
            {
                if (enhancer == null || pluginIdsByKey.ContainsKey(enhancer.key)) continue;
                string itemKey = ENHA.enhancer_item_header + enhancer.key;
                native.Add(new NativePluginDescriptor(
                    NativeFacetId.Plugin(enhancer.key), NativeItemId.FromKey(itemKey), enhancer.key,
                    Safe(() => enhancer.title, enhancer.key), Safe(() => enhancer.descript, string.Empty),
                    enhancer.key, enhancer.cost));
            }

            foreach (PluginDefinition definition in catalog.Plugins.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (plugins.ContainsKey(definition.Id)) continue;
                try
                {
                    string key = AdapterKey.For("plugin", definition.Id);
                    ENHA.Enhancer enhancer = ENHA.Get(key);
                    if (enhancer == null)
                    {
                        enhancer = new ENHA.Enhancer(key, ResolveEnhancerFrame(definition.Icon))
                        {
                            cost = definition.Cost,
                            ehbit = 0,
                            tx_suffix = key,
                        };
                        ENHA.AEh.Add(enhancer);
                    }

                    string itemKey = ENHA.enhancer_item_header + key;
                    NelItem item = NelItem.GetById(itemKey, true) ?? NelItem.CreateItemEntry(
                        itemKey,
                        new NelItem(itemKey, 0, 600, 1)
                        {
                            category = (NelItem.CATEG)10485761u,
                            FnGetName = NelItem.fnGetNameEnhancer,
                            FnGetDesc = NelItem.fnGetDescEnhancer,
                            FnGetDetail = NelItem.fnGetDetailEnhancer,
                        },
                        ushort.MaxValue);
                    item.value = definition.Cost;
                    plugins.Add(definition.Id, new PluginBinding(definition, enhancer, item));
                    pluginIdsByKey[key] = definition.Id;
                }
                catch (Exception ex) { Report(ex, "installing Addons plugin " + definition.Id); }
            }

            catalog.ReplaceNativeFacets(native, catalog.AllSkills.OfType<NativeSkillDescriptor>());
            if (enhancerStorage != null) ApplySavedPlugins(enhancerStorage);
        }

        internal void InstallSkills()
        {
            var dictionary = SkillManager.getSkillDictionary();
            if (dictionary == null) return;

            foreach (string id in skills.Where(x =>
                !dictionary.TryGetValue(x.Value.Skill.key, out PrSkill current) ||
                !ReferenceEquals(current, x.Value.Skill) ||
                !ReferenceEquals(NelItem.GetById(x.Value.Book.key, true), x.Value.Book))
                .Select(x => x.Key).ToArray())
            {
                skillIdsByBookKey.Remove(skills[id].Book.key);
                skills.Remove(id);
            }

            var native = new List<NativeSkillDescriptor>();
            var virtualItems = new List<NativeItemDescriptor>();
            foreach (KeyValuePair<string, PrSkill> entry in dictionary.ToArray())
            {
                if (skills.Values.Any(x => ReferenceEquals(x.Skill, entry.Value))) continue;
                string bookKey = SkillManager.skillbook_item_header + entry.Key;
                NelItem book = NelItem.GetById(bookKey, true);
                string itemId = NativeItemId.FromKey(bookKey);
                if (book == null)
                {
                    virtualItems.Add(new NativeItemDescriptor(
                        itemId, bookKey, Safe(() => entry.Value.title, entry.Key),
                        Safe(() => entry.Value.descript, string.Empty), string.Empty, 0, 1, "Virtual", true));
                }
                native.Add(new NativeSkillDescriptor(
                    NativeFacetId.Skill(entry.Key), itemId, entry.Key,
                    Safe(() => entry.Value.title, entry.Key), Safe(() => entry.Value.descript, string.Empty),
                    book?.specific_icon_id.ToString() ?? string.Empty));
            }

            foreach (SkillDefinition definition in catalog.Skills.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (skills.ContainsKey(definition.Id)) continue;
                try
                {
                    string key = AdapterKey.For("skill", definition.Id);
                    PrSkill skill = SkillManager.Get(key);
                    if (skill == null)
                    {
                        skill = new PrSkill(key, ushort.MaxValue)
                        {
                            category = SkillManager.SKILL_CTG.SPECIAL,
                            desc_key_replace = key,
                        };
                        dictionary[key] = skill;
                    }

                    string bookKey = SkillManager.skillbook_item_header + key;
                    NelItem book = NelItem.GetById(bookKey, true) ?? NelItem.CreateItemEntry(
                        bookKey,
                        new NelItem(bookKey, 0, 300, 1)
                        {
                            category = (NelItem.CATEG)2097153u,
                            FnGetName = NelItem.fnGetNameSkillBook,
                            FnGetDesc = NelItem.fnGetDescSkillBook,
                            FnGetDetail = NelItem.fnGetDetailSkillBook,
                            specific_icon_id = ParseIcon(definition.Icon, 18),
                        },
                        ushort.MaxValue);
                    book.value = ushort.MaxValue;
                    skills.Add(definition.Id, new SkillBinding(definition, skill, book));
                    skillIdsByBookKey[bookKey] = definition.Id;
                }
                catch (Exception ex) { Report(ex, "installing Addons skill " + definition.Id); }
            }

            if (virtualItems.Count != 0)
            {
                string[] virtualIds = virtualItems.Select(x => x.Id).ToArray();
                catalog.ReplaceNativeItems(catalog.NativeItems
                    .Where(x => !virtualIds.Contains(x.Id, StringComparer.Ordinal))
                    .Concat(virtualItems));
            }
            catalog.ReplaceNativeFacets(catalog.AllPlugins.OfType<NativePluginDescriptor>(), native);
            ApplySavedSkills();
        }

        internal void ObserveEnhancers(ItemStorage precious, ItemStorage storage, bool persist)
        {
            if (storage == null) return;
            if (precious != null) preciousStorage = precious;
            enhancerStorage = storage;
            foreach (PluginBinding binding in plugins.Values)
            {
                ItemStorage.ObtainInfo info = FindInfo(storage, binding.Item);
                bool obtained = info != null && storage.getCount(binding.Item) > 0;
                bool active = obtained && (info.top_grade & 2) == 2;
                runtime.SyncPlugin(binding.Definition.Id, obtained, active, persist);
            }
        }

        internal void ObserveSkills(bool persist)
        {
            foreach (SkillBinding binding in skills.Values)
            {
                runtime.SyncSkill(binding.Definition.Id, binding.Skill.visible, binding.Skill.enabled, persist);
            }
        }

        internal void ApplySavedState()
        {
            if (enhancerStorage != null) ApplySavedPlugins(enhancerStorage);
            ApplySavedSkills();
        }

        internal bool SetPluginEnabled(string id, bool enabled)
        {
            if (!plugins.TryGetValue(id, out PluginBinding binding) || enhancerStorage == null) return false;
            ItemStorage.ObtainInfo info = FindInfo(enhancerStorage, binding.Item);
            if (info == null) return false;
            int grade = info.top_grade;
            info.changeGradeForPrecious(enabled ? grade | 2 : grade & ~2);
            if (preciousStorage != null) ENHA.fineEnhancerStorage(preciousStorage, enhancerStorage);
            else M2PrSkill.resetSkillConnectionWhole();
            ObserveEnhancers(preciousStorage, enhancerStorage, true);
            return true;
        }

        internal bool SetPluginObtained(string id, bool obtained)
        {
            if (!plugins.TryGetValue(id, out PluginBinding binding) || enhancerStorage == null) return false;
            int count = enhancerStorage.getCount(binding.Item, -1);
            if (obtained && count == 0) enhancerStorage.Add(binding.Item, 1, 0, true, true);
            if (!obtained && count > 0) enhancerStorage.Reduce(binding.Item, count, -1, true);
            ObserveEnhancers(preciousStorage, enhancerStorage, true);
            return obtained == (enhancerStorage.getCount(binding.Item, -1) > 0);
        }

        internal bool SetSkillEnabled(string id, bool enabled)
        {
            if (!skills.TryGetValue(id, out SkillBinding binding) || !binding.Skill.visible) return false;
            binding.Skill.enabled = enabled;
            ObserveSkills(true);
            return true;
        }

        internal bool SetSkillObtained(string id, bool obtained)
        {
            if (!skills.TryGetValue(id, out SkillBinding binding)) return false;
            if (obtained) binding.Skill.Obtain(false);
            else binding.Skill.ReleaseObtain();
            ObserveSkills(true);
            return binding.Skill.visible == obtained;
        }

        internal bool TryUseFacetItem(string nativeKey, out int result)
        {
            result = 0;
            if (!skillIdsByBookKey.TryGetValue(nativeKey, out string id)) return false;
            runtime.SyncSkill(id, true, true, true);
            if (skills.TryGetValue(id, out SkillBinding binding))
            {
                binding.Skill.Obtain(false);
            }
            result = 1;
            return true;
        }

        internal string PluginTitle(ENHA.Enhancer enhancer, bool description)
        {
            if (enhancer == null || !pluginIdsByKey.TryGetValue(enhancer.key, out string id) || !plugins.TryGetValue(id, out PluginBinding binding)) return null;
            return AdapterText.Resolve(description ? binding.Definition.DescriptionKey : binding.Definition.TitleKey, id);
        }

        internal string SkillTitle(PrSkill skill, bool description)
        {
            SkillBinding binding = skills.Values.FirstOrDefault(x => ReferenceEquals(x.Skill, skill));
            return binding == null ? null : AdapterText.Resolve(description ? binding.Definition.DescriptionKey : binding.Definition.TitleKey, binding.Definition.Id);
        }

        internal List<SkillSerializationState> SuppressCustomSkills()
        {
            var states = new List<SkillSerializationState>(skills.Count);
            foreach (SkillBinding binding in skills.Values)
            {
                states.Add(new SkillSerializationState(binding.Skill));
                binding.Skill.visible = false;
                binding.Skill.first_visible = false;
            }
            return states;
        }

        internal void RestoreCustomSkills(IEnumerable<SkillSerializationState> states)
        {
            if (states == null) return;
            foreach (SkillSerializationState state in states) state.Restore();
        }

        public void Dispose() { plugins.Clear(); skills.Clear(); pluginIdsByKey.Clear(); skillIdsByBookKey.Clear(); }

        private void ApplySavedPlugins(ItemStorage storage)
        {
            foreach (PluginBinding binding in plugins.Values)
            {
                ItemStorage.ObtainInfo info = FindInfo(storage, binding.Item);
                if (runtime.IsPluginObtained(binding.Definition.Id) && info == null)
                {
                    storage.Add(binding.Item, 1, 0, true, true);
                    info = FindInfo(storage, binding.Item);
                }
                if (info != null)
                {
                    int grade = info.top_grade;
                    info.changeGradeForPrecious(runtime.IsPluginEnabled(binding.Definition.Id) ? grade | 2 : grade & ~2);
                }
            }
            ObserveEnhancers(preciousStorage, storage, false);
        }

        private void ApplySavedSkills()
        {
            foreach (SkillBinding binding in skills.Values)
            {
                binding.Skill.visible = runtime.IsSkillObtained(binding.Definition.Id);
                binding.Skill.enabled = binding.Skill.visible && runtime.IsSkillEnabled(binding.Definition.Id);
                runtime.SyncSkill(binding.Definition.Id, binding.Skill.visible, binding.Skill.enabled, false);
            }
        }

        private static ItemStorage.ObtainInfo FindInfo(ItemStorage storage, NelItem item) =>
            storage.getWholeInfoDictionary().TryGetValue(item, out ItemStorage.ObtainInfo info) ? info : null;

        private static PxlFrame ResolveEnhancerFrame(string icon)
        {
            if (ENHA.SqImgIcon == null) return null;
            return string.IsNullOrWhiteSpace(icon) ? ENHA.SqImgIcon.getFrame(0) : ENHA.SqImgIcon.getFrameByName(icon) ?? ENHA.SqImgIcon.getFrame(0);
        }

        private static int ParseIcon(string value, int fallback) => int.TryParse(value, out int result) ? result : fallback;
        private static T Safe<T>(Func<T> read, T fallback) { try { return read(); } catch { return fallback; } }
        private static void Report(Exception ex, string operation) { try { PolarisAPI.Errors.Report(ex, operation, typeof(AliceFacetAdapter).Assembly); } catch { } }

        private sealed class PluginBinding
        {
            internal PluginBinding(PluginDefinition definition, ENHA.Enhancer enhancer, NelItem item) { Definition = definition; Enhancer = enhancer; Item = item; }
            internal PluginDefinition Definition { get; }
            internal ENHA.Enhancer Enhancer { get; }
            internal NelItem Item { get; }
        }

        private sealed class SkillBinding
        {
            internal SkillBinding(SkillDefinition definition, PrSkill skill, NelItem book) { Definition = definition; Skill = skill; Book = book; }
            internal SkillDefinition Definition { get; }
            internal PrSkill Skill { get; }
            internal NelItem Book { get; }
        }
    }

    internal sealed class SkillSerializationState
    {
        private readonly PrSkill skill;
        private readonly bool visible;
        private readonly bool firstVisible;
        internal SkillSerializationState(PrSkill skill) { this.skill = skill; visible = skill.visible; firstVisible = skill.first_visible; }
        internal void Restore() { skill.visible = visible; skill.first_visible = firstVisible; }
    }

    [HarmonyPatch(typeof(ENHA), nameof(ENHA.initScript))]
    internal static class Patch_ENHA_InitScript_Addons { [HarmonyPostfix] private static void Postfix() => AddonRuntime.InstallEnhancers(); }

    [HarmonyPatch(typeof(SkillManager), nameof(SkillManager.initScript))]
    internal static class Patch_SkillManager_InitScript_Addons { [HarmonyPostfix] private static void Postfix() => AddonRuntime.InstallSkills(); }

    [HarmonyPatch(typeof(ENHA), nameof(ENHA.fineEnhancerStorage))]
    internal static class Patch_ENHA_Fine_Addons { [HarmonyPostfix] private static void Postfix(ItemStorage StPrecious, ItemStorage StEnhancer) => AddonRuntime.ObserveEnhancers(StPrecious, StEnhancer); }

    [HarmonyPatch(typeof(ENHA), nameof(ENHA.attachEnhancer))]
    internal static class Patch_ENHA_Attach_Addons { [HarmonyPostfix] private static void Postfix(ItemStorage StEnhancer) => AddonRuntime.ObserveEnhancers(null, StEnhancer); }

    [HarmonyPatch(typeof(ENHA.Enhancer), "get_title")]
    internal static class Patch_Enhancer_Title_Addons { [HarmonyPostfix] private static void Postfix(ENHA.Enhancer __instance, ref string __result) { __result = AddonRuntime.PluginText(__instance, false) ?? __result; } }

    [HarmonyPatch(typeof(ENHA.Enhancer), "get_descript")]
    internal static class Patch_Enhancer_Description_Addons { [HarmonyPostfix] private static void Postfix(ENHA.Enhancer __instance, ref string __result) { __result = AddonRuntime.PluginText(__instance, true) ?? __result; } }

    [HarmonyPatch(typeof(PrSkill), "get_title")]
    internal static class Patch_Skill_Title_Addons { [HarmonyPostfix] private static void Postfix(PrSkill __instance, ref string __result) { __result = AddonRuntime.SkillText(__instance, false) ?? __result; } }

    [HarmonyPatch(typeof(PrSkill), "get_descript")]
    internal static class Patch_Skill_Description_Addons { [HarmonyPostfix] private static void Postfix(PrSkill __instance, ref string __result) { __result = AddonRuntime.SkillText(__instance, true) ?? __result; } }

    [HarmonyPatch(typeof(SkillManager), nameof(SkillManager.writeBinaryTo))]
    internal static class Patch_SkillManager_Write_Addons
    {
        [HarmonyPrefix] private static void Prefix(out List<SkillSerializationState> __state) => __state = AddonRuntime.SuppressCustomSkills();
        [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, List<SkillSerializationState> __state) { AddonRuntime.RestoreCustomSkills(__state); return __exception; }
    }
}
