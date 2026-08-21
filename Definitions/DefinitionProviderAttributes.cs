using System;

namespace Polaris.Addons.Definitions
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ItemDefinitionProviderAttribute : Attribute
    {
        public const string FactoryMethodName = "BuildDefinition";
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PluginDefinitionProviderAttribute : Attribute
    {
        public const string FactoryMethodName = "BuildDefinition";
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SkillDefinitionProviderAttribute : Attribute
    {
        public const string FactoryMethodName = "BuildDefinition";
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ItemOverlayProviderAttribute : Attribute
    {
        public const string FactoryMethodName = "BuildOverlay";
    }
}
