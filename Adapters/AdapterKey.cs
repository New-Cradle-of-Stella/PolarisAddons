using System.Text;
using Polaris.Addons.Definitions;

namespace Polaris.Addons.Adapters
{
    /// <summary>Addons id → 原版字符串 key。可读的 slug 便于排查，尾部散列保证唯一且稳定。</summary>
    internal static class AdapterKey
    {
        private const int MaxSlugLength = 64;

        internal static string For(string kind, string id) =>
            "polaris_" + kind + "_" + Slug(id) + "_" + StableHash.Of(id);

        private static string Slug(string id)
        {
            var slug = new StringBuilder(MaxSlugLength);
            foreach (char character in id)
            {
                if (slug.Length == MaxSlugLength)
                {
                    break;
                }

                slug.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
            }

            return slug.ToString();
        }
    }
}
