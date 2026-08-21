using System.Collections.Generic;
using System.Reflection;
using Polaris.Components;

namespace Polaris.Addons
{
    /// <summary>自定义物品、插件与技能扩展的组件边界。</summary>
    public sealed class PolarisAddonsComponent : PolarisComponent
    {
        public override string Id => "PolarisAddons";
        public override int Order => 400;

        public override void Bootstrap() => AddonRuntime.RegisterPersistence();

        public override void Start()
        {
            AddonRuntime.Initialize(CandidateAssemblies());
        }

        public override void Shutdown() => AddonRuntime.Shutdown();

        public override void Update() => AddonRuntime.Pump();

        private static IEnumerable<Assembly> CandidateAssemblies()
        {
            var seen = new HashSet<Assembly>();

            foreach (Assembly assembly in PolarisAPI.Modules.ComponentAssemblies)
            {
                if (seen.Add(assembly))
                {
                    yield return assembly;
                }
            }

            foreach (Assembly assembly in PolarisAPI.Modules.PluginAssemblies)
            {
                if (seen.Add(assembly))
                {
                    yield return assembly;
                }
            }
        }
    }
}
