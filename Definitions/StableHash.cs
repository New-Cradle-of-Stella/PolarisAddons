using System.Text;

namespace Polaris.Addons.Definitions
{
    /// <summary>
    /// FNV-1a 32 位散列。用于在原版 key 与 Addons id 之间生成稳定后缀：同一输入在任意进程、
    /// 任意版本下都得到同一结果，因此可以进存档和生成代码。
    /// </summary>
    internal static class StableHash
    {
        internal static string Of(string value)
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
