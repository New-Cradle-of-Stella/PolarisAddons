using System;
using Polaris.Addons.Authoring;

namespace Polaris.Addons.Definitions
{
    public interface IPluginDescriptor
    {
        string Id { get; }
        string ItemId { get; }
        ContentOrigin Origin { get; }
        string TitleKey { get; }
        string DescriptionKey { get; }
        string Icon { get; }
        int Cost { get; }
        string NativeKey { get; }
        bool IsReadOnly { get; }
    }

    public interface ISkillDescriptor
    {
        string Id { get; }
        string ItemId { get; }
        ContentOrigin Origin { get; }
        string TitleKey { get; }
        string DescriptionKey { get; }
        string Icon { get; }
        AddonSkillMode Mode { get; }
        AddonSkillUnlockPolicy Unlock { get; }
        double CooldownSeconds { get; }
        string ConcurrencyGroup { get; }
        string NativeKey { get; }
        bool IsReadOnly { get; }
    }

    public sealed class NativePluginDescriptor : IPluginDescriptor
    {
        public NativePluginDescriptor(string id, string itemId, string nativeKey, string title, string description, string icon, int cost)
        {
            Id = NativeFacetId.Require(id, nameof(id));
            ItemId = NativeFacetId.Require(itemId, nameof(itemId));
            NativeKey = nativeKey ?? throw new ArgumentNullException(nameof(nativeKey));
            TitleKey = title ?? string.Empty;
            DescriptionKey = description ?? string.Empty;
            Icon = icon ?? string.Empty;
            Cost = Math.Max(0, cost);
        }
        public string Id { get; }
        public string ItemId { get; }
        public ContentOrigin Origin => ContentOrigin.Native;
        public string TitleKey { get; }
        public string DescriptionKey { get; }
        public string Icon { get; }
        public int Cost { get; }
        public string NativeKey { get; }
        public bool IsReadOnly => true;
    }

    public sealed class NativeSkillDescriptor : ISkillDescriptor
    {
        public NativeSkillDescriptor(string id, string itemId, string nativeKey, string title, string description, string icon)
        {
            Id = NativeFacetId.Require(id, nameof(id));
            ItemId = NativeFacetId.Require(itemId, nameof(itemId));
            NativeKey = nativeKey ?? throw new ArgumentNullException(nameof(nativeKey));
            TitleKey = title ?? string.Empty;
            DescriptionKey = description ?? string.Empty;
            Icon = icon ?? string.Empty;
        }
        public string Id { get; }
        public string ItemId { get; }
        public ContentOrigin Origin => ContentOrigin.Native;
        public string TitleKey { get; }
        public string DescriptionKey { get; }
        public string Icon { get; }
        public AddonSkillMode Mode => AddonSkillMode.Passive;
        public AddonSkillUnlockPolicy Unlock => AddonSkillUnlockPolicy.External;
        public double CooldownSeconds => 0;
        public string ConcurrencyGroup => string.Empty;
        public string NativeKey { get; }
        public bool IsReadOnly => true;
    }

    /// <summary>把原版 key 映射为 Facet 的 Addons id：沿用物品 id 的命名空间，只替换中间的类别段。</summary>
    public static class NativeFacetId
    {
        private const string ItemSegment = "/item/";

        public static string Plugin(string nativeKey) => FromKey("plugin", nativeKey);

        public static string Skill(string nativeKey) => FromKey("skill", nativeKey);

        internal static string Require(string id, string parameter) => AddonIdentifier.IsValidId(id)
            ? id
            : throw new ArgumentException("Invalid Addons id '" + id + "'.", parameter);

        private static string FromKey(string kind, string nativeKey)
        {
            string itemId = NativeItemId.FromKey(nativeKey);
            int marker = itemId.IndexOf(ItemSegment, StringComparison.Ordinal);
            return itemId.Substring(0, marker) + "/" + kind + "/" +
                itemId.Substring(marker + ItemSegment.Length);
        }
    }
}
