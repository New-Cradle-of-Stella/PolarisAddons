using System.Threading;
using System.Threading.Tasks;
using Polaris.Addons.Catalog;
using Polaris.Addons.Runtime;

namespace Polaris.Addons
{
    public static class PolarisAddonsAPI
    {
        public static bool IsReady => AddonRuntime.IsReady;

        public static bool IsGameAdapterInstalled => AddonRuntime.IsGameAdapterInstalled;

        public static AddonCatalog Catalog => AddonRuntime.Catalog;

        public static IModifierSink Modifiers => AddonRuntime.Modifiers;

        public static IAddonStateStore State => AddonRuntime.State;

        public static bool SetPluginEnabled(string pluginId, bool enabled) =>
            AddonRuntime.SetPluginEnabled(pluginId, enabled);

        public static bool SetPluginObtained(string pluginId, bool obtained) =>
            AddonRuntime.SetPluginObtained(pluginId, obtained);

        public static bool SetSkillEnabled(string skillId, bool enabled) =>
            AddonRuntime.SetSkillEnabled(skillId, enabled);

        public static bool SetSkillObtained(string skillId, bool obtained) =>
            AddonRuntime.SetSkillObtained(skillId, obtained);

        public static ValueTask<SkillExecutionResult> ExecuteSkillAsync(
            string skillId,
            CancellationToken cancellationToken = default) =>
            AddonRuntime.ExecuteSkillAsync(skillId, cancellationToken);
    }
}
