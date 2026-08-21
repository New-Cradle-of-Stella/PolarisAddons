using System.Text;

namespace Polaris.Addons.Adapters
{
    internal static class AdapterKey
    {
        internal static string For(string kind, string id)
        {
            var slug = new StringBuilder(64);
            foreach (char character in id)
            {
                if (slug.Length == 64) break;
                slug.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
            }
            return "polaris_" + kind + "_" + slug + "_" + Hash(id);
        }

        private static string Hash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (byte valueByte in Encoding.UTF8.GetBytes(value))
                {
                    hash ^= valueByte;
                    hash *= 16777619;
                }
                return hash.ToString("x8");
            }
        }
    }
}
