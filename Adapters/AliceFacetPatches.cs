using System;
using System.Collections.Generic;
using HarmonyLib;
using nel;

namespace Polaris.Addons.Adapters
{
    [HarmonyPatch(typeof(ENHA), nameof(ENHA.initScript))]
    internal static class Patch_ENHA_InitScript_Addons
    {
        [HarmonyPostfix]
        private static void Postfix() => AddonRuntime.InstallEnhancers();
    }

    [HarmonyPatch(typeof(SkillManager), nameof(SkillManager.initScript))]
    internal static class Patch_SkillManager_InitScript_Addons
    {
        [HarmonyPostfix]
        private static void Postfix() => AddonRuntime.InstallSkills();
    }

    [HarmonyPatch(typeof(ENHA), nameof(ENHA.fineEnhancerStorage))]
    internal static class Patch_ENHA_Fine_Addons
    {
        [HarmonyPostfix]
        private static void Postfix(ItemStorage StPrecious, ItemStorage StEnhancer) =>
            AddonRuntime.ObserveEnhancers(StPrecious, StEnhancer);
    }

    [HarmonyPatch(typeof(ENHA), nameof(ENHA.attachEnhancer))]
    internal static class Patch_ENHA_Attach_Addons
    {
        [HarmonyPostfix]
        private static void Postfix(ItemStorage StEnhancer) => AddonRuntime.ObserveEnhancers(null, StEnhancer);
    }

    [HarmonyPatch(typeof(ENHA.Enhancer), "get_title")]
    internal static class Patch_Enhancer_Title_Addons
    {
        [HarmonyPostfix]
        private static void Postfix(ENHA.Enhancer __instance, ref string __result) =>
            __result = AddonRuntime.PluginText(__instance, false) ?? __result;
    }

    [HarmonyPatch(typeof(ENHA.Enhancer), "get_descript")]
    internal static class Patch_Enhancer_Description_Addons
    {
        [HarmonyPostfix]
        private static void Postfix(ENHA.Enhancer __instance, ref string __result) =>
            __result = AddonRuntime.PluginText(__instance, true) ?? __result;
    }

    [HarmonyPatch(typeof(PrSkill), "get_title")]
    internal static class Patch_Skill_Title_Addons
    {
        [HarmonyPostfix]
        private static void Postfix(PrSkill __instance, ref string __result) =>
            __result = AddonRuntime.SkillText(__instance, false) ?? __result;
    }

    [HarmonyPatch(typeof(PrSkill), "get_descript")]
    internal static class Patch_Skill_Description_Addons
    {
        [HarmonyPostfix]
        private static void Postfix(PrSkill __instance, ref string __result) =>
            __result = AddonRuntime.SkillText(__instance, true) ?? __result;
    }

    [HarmonyPatch(typeof(SkillManager), nameof(SkillManager.writeBinaryTo))]
    internal static class Patch_SkillManager_Write_Addons
    {
        [HarmonyPrefix]
        private static void Prefix(out List<SkillSerializationState> __state) =>
            __state = AddonRuntime.SuppressCustomSkills();

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, List<SkillSerializationState> __state)
        {
            AddonRuntime.RestoreCustomSkills(__state);
            return __exception;
        }
    }
}
