using Polaris.Components;

namespace Polaris.Addons
{
    /// <summary>自定义物品、插件与技能扩展的组件边界。</summary>
    public sealed class PolarisAddonsComponent : PolarisComponent
    {
        public override string Id => "PolarisAddons";
        public override int Order => 400;
    }
}
