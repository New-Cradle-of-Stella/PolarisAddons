using System;
using System.Text;
using Polaris.Addons.Authoring;

namespace Polaris.Addons.Definitions
{
    public enum ContentOrigin
    {
        Addon,
        Native,
    }

    /// <summary>统一物品查询面；自定义定义和原版只读镜像都实现此契约。</summary>
    public interface IItemDescriptor
    {
        string Id { get; }

        ContentOrigin Origin { get; }

        string NameKey { get; }

        string DescriptionKey { get; }

        string Icon { get; }

        int Price { get; }

        int StackLimit { get; }

        string Category { get; }

        string NativeKey { get; }

        bool IsReadOnly { get; }

        bool IsVirtual { get; }
    }

    /// <summary>Adapter 从游戏目录建立的快照；不持有任何原版对象引用。</summary>
    public sealed class NativeItemDescriptor : IItemDescriptor
    {
        public NativeItemDescriptor(
            string id,
            string nativeKey,
            string name,
            string description,
            string icon,
            int price,
            int stackLimit,
            string category,
            bool isVirtual = false)
        {
            if (!AddonIdentifier.IsValidId(id))
            {
                throw new ArgumentException("Invalid native item id '" + id + "'.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(nativeKey))
            {
                throw new ArgumentException("A native item key is required.", nameof(nativeKey));
            }

            Id = id;
            NativeKey = nativeKey;
            NameKey = name ?? string.Empty;
            DescriptionKey = description ?? string.Empty;
            Icon = icon ?? string.Empty;
            Price = Math.Max(0, price);
            StackLimit = Math.Max(1, stackLimit);
            Category = category ?? string.Empty;
            IsVirtual = isVirtual;
        }

        public string Id { get; }

        public ContentOrigin Origin => ContentOrigin.Native;

        public string NameKey { get; }

        public string DescriptionKey { get; }

        public string Icon { get; }

        public int Price { get; }

        public int StackLimit { get; }

        public string Category { get; }

        public string NativeKey { get; }

        public bool IsReadOnly => true;

        public bool IsVirtual { get; }
    }

    /// <summary>把任意原版 key 映射为合法、确定且不依赖数值 ID 的 Addons ID。</summary>
    public static class NativeItemId
    {
        public static string FromKey(string nativeKey, string gameNamespace = "aic")
        {
            if (string.IsNullOrWhiteSpace(nativeKey))
            {
                throw new ArgumentException("A native item key is required.", nameof(nativeKey));
            }

            if (string.IsNullOrWhiteSpace(gameNamespace))
            {
                throw new ArgumentException("A game namespace is required.", nameof(gameNamespace));
            }

            string normalizedNamespace = Normalize(gameNamespace, out bool namespaceChanged);
            string normalizedKey = Normalize(nativeKey, out bool keyChanged);
            if (namespaceChanged || keyChanged)
            {
                // 规范化是有损的，附加散列保证不同的原版 key 不会塌缩成同一个 id。
                normalizedKey += "_" + StableHash.Of(nativeKey);
            }

            return "native." + normalizedNamespace + "/item/" + normalizedKey;
        }

        private static string Normalize(string value, out bool changed)
        {
            var result = new StringBuilder(value.Length);
            changed = false;
            foreach (char source in value)
            {
                char character = char.ToLowerInvariant(source);
                if ((character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9'))
                {
                    result.Append(character);
                }
                else
                {
                    if (result.Length > 0 && result[result.Length - 1] != '_')
                    {
                        result.Append('_');
                    }
                    changed = true;
                }

                if (character != source)
                {
                    changed = true;
                }
            }

            string normalized = result.ToString().Trim('_');
            return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
        }
    }
}
