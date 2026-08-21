using System;
using System.Reflection;
using Polaris.Addons.Authoring;
using Polaris.Addons.Runtime;

namespace Polaris.Addons.Definitions
{
    public abstract class AddonDefinition
    {
        protected AddonDefinition(string id, Type behaviorType, Assembly providerAssembly)
        {
            Id = id;
            BehaviorType = behaviorType;
            ProviderAssembly = providerAssembly;
        }

        public string Id { get; }

        public Type BehaviorType { get; }

        public Assembly ProviderAssembly { get; }
    }

    public sealed class ItemDefinition : AddonDefinition, IItemDescriptor
    {
        internal ItemDefinition(
            string id,
            string nameKey,
            string descriptionKey,
            string icon,
            int price,
            int stackLimit,
            string category,
            Type behaviorType,
            Assembly providerAssembly)
            : base(id, behaviorType, providerAssembly)
        {
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Icon = icon;
            Price = price;
            StackLimit = stackLimit;
            Category = category;
        }

        public string NameKey { get; }

        public string DescriptionKey { get; }

        public string Icon { get; }

        public int Price { get; }

        public int StackLimit { get; }

        public string Category { get; }

        public ContentOrigin Origin => ContentOrigin.Addon;

        public string NativeKey => null;

        public bool IsReadOnly => false;

        public bool IsVirtual => false;
    }

    public sealed class PluginDefinition : AddonDefinition, IPluginDescriptor
    {
        internal PluginDefinition(
            string id,
            string itemId,
            string titleKey,
            string descriptionKey,
            string icon,
            int cost,
            Type behaviorType,
            Assembly providerAssembly)
            : base(id, behaviorType, providerAssembly)
        {
            ItemId = itemId;
            TitleKey = titleKey;
            DescriptionKey = descriptionKey;
            Icon = icon;
            Cost = cost;
        }

        public string ItemId { get; }

        public string TitleKey { get; }

        public string DescriptionKey { get; }

        public string Icon { get; }

        public int Cost { get; }

        public ContentOrigin Origin => ContentOrigin.Addon;
        public string NativeKey => null;
        public bool IsReadOnly => false;
    }

    public sealed class SkillDefinition : AddonDefinition, ISkillDescriptor
    {
        internal SkillDefinition(
            string id,
            string itemId,
            string titleKey,
            string descriptionKey,
            string icon,
            AddonSkillMode mode,
            AddonSkillUnlockPolicy unlock,
            double cooldownSeconds,
            string concurrencyGroup,
            Type behaviorType,
            Assembly providerAssembly)
            : base(id, behaviorType, providerAssembly)
        {
            ItemId = itemId;
            TitleKey = titleKey;
            DescriptionKey = descriptionKey;
            Icon = icon;
            Mode = mode;
            Unlock = unlock;
            CooldownSeconds = cooldownSeconds;
            ConcurrencyGroup = concurrencyGroup;
        }

        public string ItemId { get; }

        public string TitleKey { get; }

        public string DescriptionKey { get; }

        public string Icon { get; }

        public AddonSkillMode Mode { get; }

        public AddonSkillUnlockPolicy Unlock { get; }

        public double CooldownSeconds { get; }

        public string ConcurrencyGroup { get; }

        public ContentOrigin Origin => ContentOrigin.Addon;
        public string NativeKey => null;
        public bool IsReadOnly => false;
    }

    public sealed class ItemDefinitionBuilder
    {
        private readonly string id;
        private string nameKey = string.Empty;
        private string descriptionKey = string.Empty;
        private string icon = string.Empty;
        private int price;
        private int stackLimit = 1;
        private string category = "Other";
        private Type behaviorType;
        private Assembly providerAssembly;

        public ItemDefinitionBuilder(string id) => this.id = id;

        public ItemDefinitionBuilder SetProviderAssembly(Assembly assembly)
        {
            providerAssembly = assembly;
            return this;
        }

        public ItemDefinitionBuilder SetPresentation(string nameKey, string descriptionKey, string icon)
        {
            this.nameKey = nameKey ?? string.Empty;
            this.descriptionKey = descriptionKey ?? string.Empty;
            this.icon = icon ?? string.Empty;
            return this;
        }

        public ItemDefinitionBuilder SetInventory(int price, int stackLimit, string category)
        {
            this.price = price;
            this.stackLimit = stackLimit;
            this.category = category ?? string.Empty;
            return this;
        }

        public ItemDefinitionBuilder SetBehavior(Type type)
        {
            behaviorType = type;
            return this;
        }

        public ItemDefinition Build()
        {
            AddonDefinitionGuard.Validate(id, providerAssembly, behaviorType, typeof(IItemBehavior));
            if (price < 0 || stackLimit < 1)
            {
                throw new AddonDefinitionException("Item '" + id + "' has invalid inventory values.");
            }

            return new ItemDefinition(
                id, nameKey, descriptionKey, icon, price, stackLimit, category, behaviorType, providerAssembly);
        }
    }

    /// <summary>三种定义 Builder 共用的校验：id、来源程序集，以及 Behavior 是否实现了对应契约。</summary>
    internal static class AddonDefinitionGuard
    {
        internal static void Validate(
            string id,
            Assembly providerAssembly,
            Type behaviorType,
            Type behaviorContract)
        {
            if (!AddonIdentifier.IsValidId(id))
            {
                throw new AddonDefinitionException("'" + id + "' is not a valid Addons id.");
            }

            if (providerAssembly == null)
            {
                throw new AddonDefinitionException("Definition '" + id + "' has no provider assembly.");
            }

            if (behaviorType != null && !behaviorContract.IsAssignableFrom(behaviorType))
            {
                throw new AddonDefinitionException(
                    "Behavior '" + behaviorType.FullName + "' for '" + id + "' must implement " +
                    behaviorContract.FullName + ".");
            }
        }
    }

    public sealed class PluginDefinitionBuilder
    {
        private readonly string id;
        private string itemId = string.Empty;
        private string titleKey = string.Empty;
        private string descriptionKey = string.Empty;
        private string icon = string.Empty;
        private int cost = 1;
        private Type behaviorType;
        private Assembly providerAssembly;

        public PluginDefinitionBuilder(string id) => this.id = id;

        public PluginDefinitionBuilder SetProviderAssembly(Assembly assembly)
        {
            providerAssembly = assembly;
            return this;
        }

        public PluginDefinitionBuilder SetOwnerItem(string value)
        {
            itemId = value ?? string.Empty;
            return this;
        }

        public PluginDefinitionBuilder SetPresentation(string title, string description, string iconValue)
        {
            titleKey = title ?? string.Empty;
            descriptionKey = description ?? string.Empty;
            icon = iconValue ?? string.Empty;
            return this;
        }

        public PluginDefinitionBuilder SetCost(int value)
        {
            cost = value;
            return this;
        }

        public PluginDefinitionBuilder SetBehavior(Type type)
        {
            behaviorType = type;
            return this;
        }

        public PluginDefinition Build()
        {
            AddonDefinitionGuard.Validate(id, providerAssembly, behaviorType, typeof(IPluginBehavior));
            if (!AddonIdentifier.IsValidId(itemId))
            {
                throw new AddonDefinitionException("Plugin '" + id + "' has an invalid owner item id.");
            }

            if (cost < 0)
            {
                throw new AddonDefinitionException("Plugin '" + id + "' has a negative slot cost.");
            }

            return new PluginDefinition(
                id, itemId, titleKey, descriptionKey, icon, cost, behaviorType, providerAssembly);
        }
    }

    public sealed class SkillDefinitionBuilder
    {
        private readonly string id;
        private string itemId = string.Empty;
        private string titleKey = string.Empty;
        private string descriptionKey = string.Empty;
        private string icon = string.Empty;
        private AddonSkillMode mode;
        private AddonSkillUnlockPolicy unlock = AddonSkillUnlockPolicy.ConsumeOwnerItem;
        private double cooldownSeconds;
        private string concurrencyGroup = string.Empty;
        private Type behaviorType;
        private Assembly providerAssembly;

        public SkillDefinitionBuilder(string id) => this.id = id;

        public SkillDefinitionBuilder SetProviderAssembly(Assembly assembly)
        {
            providerAssembly = assembly;
            return this;
        }

        public SkillDefinitionBuilder SetOwnerItem(string value)
        {
            itemId = value ?? string.Empty;
            return this;
        }

        public SkillDefinitionBuilder SetPresentation(string title, string description, string iconValue)
        {
            titleKey = title ?? string.Empty;
            descriptionKey = description ?? string.Empty;
            icon = iconValue ?? string.Empty;
            return this;
        }

        public SkillDefinitionBuilder SetPolicy(AddonSkillMode value, AddonSkillUnlockPolicy unlockPolicy)
        {
            mode = value;
            unlock = unlockPolicy;
            return this;
        }

        public SkillDefinitionBuilder SetBehavior(Type type)
        {
            behaviorType = type;
            return this;
        }

        public SkillDefinitionBuilder SetExecutionPolicy(double cooldown, string group = null)
        {
            cooldownSeconds = cooldown;
            concurrencyGroup = group ?? string.Empty;
            return this;
        }

        public SkillDefinition Build()
        {
            Type contract = mode == AddonSkillMode.Active ? typeof(IActiveSkillBehavior) : typeof(ISkillBehavior);
            AddonDefinitionGuard.Validate(id, providerAssembly, behaviorType, contract);
            if (!AddonIdentifier.IsValidId(itemId))
            {
                throw new AddonDefinitionException("Skill '" + id + "' has an invalid owner item id.");
            }

            if (cooldownSeconds < 0 || double.IsNaN(cooldownSeconds) || double.IsInfinity(cooldownSeconds))
            {
                throw new AddonDefinitionException("Skill '" + id + "' has an invalid cooldown.");
            }
            if (!string.IsNullOrEmpty(concurrencyGroup) && !AddonIdentifier.IsValidId(concurrencyGroup))
            {
                throw new AddonDefinitionException("Skill '" + id + "' has an invalid concurrency group.");
            }

            return new SkillDefinition(
                id, itemId, titleKey, descriptionKey, icon, mode, unlock,
                cooldownSeconds, concurrencyGroup, behaviorType, providerAssembly);
        }
    }

    public sealed class AddonDefinitionException : Exception
    {
        public AddonDefinitionException(string message) : base(message) { }
    }
}
