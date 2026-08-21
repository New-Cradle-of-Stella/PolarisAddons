using XX;

namespace Polaris.Addons.Adapters
{
    internal static class AdapterText
    {
        internal static string Resolve(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key)) return fallback ?? string.Empty;
            try
            {
                string value = key[0] == '&' ? PolarisAPI.Localization.Text(key) : TX.Get(key);
                return string.IsNullOrEmpty(value) ? fallback ?? key : value;
            }
            catch { return fallback ?? key; }
        }
    }
}
