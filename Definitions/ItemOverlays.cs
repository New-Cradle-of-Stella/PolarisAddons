using System;
using System.Reflection;
using Polaris.Addons.Authoring;
using Polaris.Addons.Runtime;

namespace Polaris.Addons.Definitions
{
    /// <summary>只追加前后行为；普通 Overlay 没有阻止或替换原版行为的能力。</summary>
    public interface IItemOverlayBehavior
    {
        void BeforeUse(ItemOverlayContext context, IBehaviorLifetime lifetime);

        void AfterUse(ItemOverlayContext context, IBehaviorLifetime lifetime);
    }

    public sealed class ItemOverlayContext : IItemUseContext
    {
        internal ItemOverlayContext(
            string itemId,
            int grade,
            ContentOrigin origin,
            ItemUseResult result,
            int nativeResult)
        {
            ItemId = itemId;
            Grade = grade;
            Origin = origin;
            Result = result;
            NativeResult = nativeResult;
        }

        public string ItemId { get; }

        public int Grade { get; }

        public ContentOrigin Origin { get; }

        public ItemUseResult Result { get; }

        /// <summary>原版返回码；自定义物品恒为 0。</summary>
        public int NativeResult { get; }
    }

    public sealed class ItemOverlay
    {
        internal ItemOverlay(
            string id,
            string targetItemId,
            int priority,
            Type behaviorType,
            Assembly providerAssembly)
        {
            Id = id;
            TargetItemId = targetItemId;
            Priority = priority;
            BehaviorType = behaviorType;
            ProviderAssembly = providerAssembly;
        }

        public string Id { get; }

        public string TargetItemId { get; }

        public int Priority { get; }

        public Type BehaviorType { get; }

        public Assembly ProviderAssembly { get; }
    }

    public sealed class ItemOverlayBuilder
    {
        private readonly string id;
        private string targetItemId;
        private int priority;
        private Type behaviorType;
        private Assembly providerAssembly;

        public ItemOverlayBuilder(string id) => this.id = id;

        public ItemOverlayBuilder SetProviderAssembly(Assembly assembly)
        {
            providerAssembly = assembly;
            return this;
        }

        public ItemOverlayBuilder SetTarget(string itemId)
        {
            targetItemId = itemId;
            return this;
        }

        public ItemOverlayBuilder SetPriority(int value)
        {
            priority = value;
            return this;
        }

        public ItemOverlayBuilder SetBehavior<TBehavior>() where TBehavior : IItemOverlayBehavior =>
            SetBehavior(typeof(TBehavior));

        public ItemOverlayBuilder SetBehavior(Type type)
        {
            behaviorType = type;
            return this;
        }

        public ItemOverlay Build()
        {
            if (!AddonIdentifier.IsValidId(id))
            {
                throw new AddonDefinitionException("'" + id + "' is not a valid Overlay id.");
            }

            if (!AddonIdentifier.IsValidId(targetItemId))
            {
                throw new AddonDefinitionException("Overlay '" + id + "' has an invalid target item id.");
            }

            if (providerAssembly == null)
            {
                throw new AddonDefinitionException("Overlay '" + id + "' has no provider assembly.");
            }

            if (behaviorType == null || !typeof(IItemOverlayBehavior).IsAssignableFrom(behaviorType))
            {
                throw new AddonDefinitionException(
                    "Overlay behavior for '" + id + "' must implement " +
                    typeof(IItemOverlayBehavior).FullName + ".");
            }

            return new ItemOverlay(id, targetItemId, priority, behaviorType, providerAssembly);
        }
    }
}
