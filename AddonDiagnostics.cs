using System;
using System.Reflection;

namespace Polaris.Addons
{
    /// <summary>单条内容出错只应被跳过，因此上报本身永远不能抛出。</summary>
    internal static class AddonDiagnostics
    {
        internal static void Report(Exception exception, string operation) =>
            Report(exception, operation, typeof(AddonDiagnostics).Assembly);

        internal static void Report(Exception exception, string operation, Assembly owner)
        {
            try
            {
                PolarisAPI.Errors.Report(exception, operation, owner);
            }
            catch
            {
                // Core 诊断尚未就绪（或已关闭）时，调用方已经隔离了这一条内容，静默即可。
            }
        }

        /// <summary>反射调用失败时，真正有价值的是内层异常。</summary>
        internal static Exception Unwrap(Exception exception) =>
            exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
    }
}
