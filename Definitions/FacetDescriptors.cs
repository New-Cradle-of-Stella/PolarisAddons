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
            Id = RequireId(id);
            ItemId = RequireId(itemId);
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

        private static string RequireId(string id) => AddonIdentifier.IsValidId(id)
            ? id
            : throw new ArgumentException("Invalid Addons id '" + id + "'.");
    }

    public sealed class NativeSkillDescriptor : ISkillDescriptor
    {
        public NativeSkillDescriptor(string id, string itemId, string nativeKey, string title, string description, string icon)
        {
            Id = AddonIdentifier.IsValidId(id) ? id : throw new ArgumentException("Invalid Addons id '" + id + "'.");
            ItemId = AddonIdentifier.IsValidId(itemId) ? itemId : throw new ArgumentException("Invalid Addons id '" + itemId + "'.");
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

    public static class NativeFacetId
    {
        public static string Plugin(string nativeKey) => FromKey("plugin", nativeKey);
        public static string Skill(string nativeKey) => FromKey("skill", nativeKey);

        private static string FromKey(string kind, string nativeKey)
        {
            string itemId = NativeItemId.FromKey(nativeKey);
            int marker = itemId.IndexOf("/item/", StringComparison.Ordinal);
            return itemId.Substring(0, marker) + "/" + kind + "/" + itemId.Substring(marker + 6);
        }
    }
}
