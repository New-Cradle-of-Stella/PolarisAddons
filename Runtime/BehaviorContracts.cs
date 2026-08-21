using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polaris.Addons.Runtime
{
    public enum ItemUseResult
    {
        Rejected,
        Succeeded,
        Failed,
    }

    public enum SkillExecutionResult
    {
        Rejected,
        Succeeded,
        Failed,
        Cancelled,
    }

    public interface IItemUseContext
    {
        string ItemId { get; }

        int Grade { get; }
    }

    public interface IPluginContext
    {
        string ItemId { get; }

        string PluginId { get; }
    }

    public interface ISkillContext
    {
        string ItemId { get; }

        string SkillId { get; }
    }

    public interface IBehaviorLifetime : IDisposable
    {
        bool IsDisposed { get; }

        T Track<T>(T resource) where T : IDisposable;
    }

    public interface IItemBehavior
    {
        ValueTask<ItemUseResult> UseAsync(
            IItemUseContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>游戏内插件（Enhancer）的玩法代码。实例默认按当前存档会话创建。</summary>
    public interface IPluginBehavior
    {
        void Activate(IPluginContext context, IBehaviorLifetime lifetime);
    }

    public interface ISkillBehavior
    {
        void Enable(ISkillContext context, IBehaviorLifetime lifetime);
    }

    public interface IActiveSkillBehavior : ISkillBehavior
    {
        ValueTask<SkillExecutionResult> ExecuteAsync(
            ISkillContext context,
            CancellationToken cancellationToken);
    }

    public sealed class BehaviorLifetime : IBehaviorLifetime
    {
        private readonly object gate = new object();
        private readonly List<IDisposable> resources = new List<IDisposable>();

        public bool IsDisposed { get; private set; }

        public T Track<T>(T resource) where T : IDisposable
        {
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            lock (gate)
            {
                if (IsDisposed)
                {
                    resource.Dispose();
                    throw new ObjectDisposedException(nameof(BehaviorLifetime));
                }

                resources.Add(resource);
                return resource;
            }
        }

        public void Dispose()
        {
            IDisposable[] snapshot;
            lock (gate)
            {
                if (IsDisposed)
                {
                    return;
                }

                IsDisposed = true;
                snapshot = resources.ToArray();
                resources.Clear();
            }

            for (int index = snapshot.Length - 1; index >= 0; index--)
            {
                try
                {
                    snapshot[index].Dispose();
                }
                catch
                {
                    // 一个清理器失败不能阻止其余订阅和贡献释放。
                }
            }
        }
    }
}
