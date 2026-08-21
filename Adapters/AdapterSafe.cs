using System;

namespace Polaris.Addons.Adapters
{
    internal static class AdapterSafe
    {
        /// <summary>
        /// 读取原版对象上的字段：文案系统或场景尚未就绪时个别读取会抛异常，
        /// 此时退回到稳定的回退值，不能打断整批投影。
        /// </summary>
        internal static T Read<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
