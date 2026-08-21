using HarmonyLib;
using nel;
using Polaris.Addons.Runtime;

namespace Polaris.Addons.Adapters
{
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
