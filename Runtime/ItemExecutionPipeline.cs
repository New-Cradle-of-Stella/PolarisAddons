using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polaris.Addons.Catalog;
using Polaris.Addons.Definitions;

namespace Polaris.Addons.Runtime
{
    internal sealed class NativeItemUseInvocation
    {
        internal NativeItemUseInvocation(
            string itemId,
            int grade,
            IReadOnlyList<ItemOverlaySession> overlays)
        {
            ItemId = itemId;
            Grade = grade;
            Overlays = overlays;
        }

        internal string ItemId { get; }

        internal int Grade { get; }

        internal IReadOnlyList<ItemOverlaySession> Overlays { get; }
    }

    internal sealed class ItemOverlaySession : IDisposable
    {
        internal ItemOverlaySession(ItemOverlay definition, IItemOverlayBehavior behavior)
        {
            Definition = definition;
            Behavior = behavior;
            Lifetime = new BehaviorLifetime();
        }

        internal ItemOverlay Definition { get; }

        internal IItemOverlayBehavior Behavior { get; }

        internal BehaviorLifetime Lifetime { get; }

        public void Dispose() => Lifetime.Dispose();
    }

    /// <summary>Before → 原行为 → After。每个 Overlay 独立隔离，顺序由 priority/id 决定。</summary>
    internal sealed class ItemExecutionPipeline : IDisposable
    {
        private readonly AddonCatalog catalog;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ItemOverlaySession>> overlays;
        private bool disposed;

        internal ItemExecutionPipeline(AddonCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            overlays = BuildOverlaySessions(catalog);
        }

        internal ItemUseResult ExecuteCustom(string itemId, int grade)
        {
            ThrowIfDisposed();
            ItemRegistration registration = catalog.GetItem(itemId);
            if (registration == null)
            {
                return ItemUseResult.Rejected;
            }

            IReadOnlyList<ItemOverlaySession> sessions = GetOverlays(itemId);
            InvokeBefore(sessions, itemId, grade, ContentOrigin.Addon);

            ItemUseResult result = ItemUseResult.Rejected;
            try
            {
                if (registration.Item.BehaviorType != null)
                {
                    var behavior = (IItemBehavior)catalog.CreateBehavior(registration.Item);
                    ValueTask<ItemUseResult> pending = behavior.UseAsync(
                        new ItemUseContext(itemId, grade),
                        CancellationToken.None);
                    if (!pending.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "Item behavior '" + registration.Item.BehaviorType.FullName +
                            "' yielded asynchronously. NelItem.Use is synchronous; complete the ValueTask inline.");
                    }

                    result = pending.GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                result = ItemUseResult.Failed;
                Report(ex, "executing Addons item " + itemId, registration.Item.ProviderAssembly);
            }

            InvokeAfter(sessions, itemId, grade, ContentOrigin.Addon, result, 0);
            return result;
        }

        internal NativeItemUseInvocation BeginNative(string itemId, int grade)
        {
            ThrowIfDisposed();
            IReadOnlyList<ItemOverlaySession> sessions = GetOverlays(itemId);
            if (sessions.Count == 0)
            {
                return null;
            }

            InvokeBefore(sessions, itemId, grade, ContentOrigin.Native);
            return new NativeItemUseInvocation(itemId, grade, sessions);
        }

        internal void CompleteNative(NativeItemUseInvocation invocation, int nativeResult)
        {
            if (invocation == null || disposed)
            {
                return;
            }

            ItemUseResult result = nativeResult == 0 ? ItemUseResult.Rejected : ItemUseResult.Succeeded;
            InvokeAfter(
                invocation.Overlays,
                invocation.ItemId,
                invocation.Grade,
                ContentOrigin.Native,
                result,
                nativeResult);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (ItemOverlaySession session in overlays.Values.SelectMany(x => x).Reverse())
            {
                session.Dispose();
            }
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<ItemOverlaySession>> BuildOverlaySessions(
            AddonCatalog catalog)
        {
            var sessions = new Dictionary<string, List<ItemOverlaySession>>(StringComparer.Ordinal);
            foreach (ItemOverlay overlay in catalog.ItemOverlays)
            {
                try
                {
                    if (catalog.GetItemDescriptor(overlay.TargetItemId) == null)
                    {
                        throw new AddonDefinitionException(
                            "Overlay '" + overlay.Id + "' targets unavailable item '" +
                            overlay.TargetItemId + "'.");
                    }

                    var behavior = (IItemOverlayBehavior)catalog.CreateBehavior(overlay.BehaviorType);
                    if (!sessions.TryGetValue(overlay.TargetItemId, out List<ItemOverlaySession> target))
                    {
                        target = new List<ItemOverlaySession>();
                        sessions.Add(overlay.TargetItemId, target);
                    }

                    target.Add(new ItemOverlaySession(overlay, behavior));
                }
                catch (Exception ex)
                {
                    Report(ex, "creating Addons Overlay " + overlay.Id, overlay.ProviderAssembly);
                }
            }

            return sessions.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ItemOverlaySession>)pair.Value.ToArray(),
                StringComparer.Ordinal);
        }

        private IReadOnlyList<ItemOverlaySession> GetOverlays(string itemId) =>
            itemId != null && overlays.TryGetValue(itemId, out IReadOnlyList<ItemOverlaySession> value)
                ? value
                : Array.Empty<ItemOverlaySession>();

        private static void InvokeBefore(
            IEnumerable<ItemOverlaySession> sessions,
            string itemId,
            int grade,
            ContentOrigin origin)
        {
            var context = new ItemOverlayContext(itemId, grade, origin, ItemUseResult.Rejected, 0);
            foreach (ItemOverlaySession session in sessions)
            {
                try
                {
                    session.Behavior.BeforeUse(context, session.Lifetime);
                }
                catch (Exception ex)
                {
                    Report(ex, "running BeforeUse for Overlay " + session.Definition.Id,
                        session.Definition.ProviderAssembly);
                }
            }
        }

        private static void InvokeAfter(
            IEnumerable<ItemOverlaySession> sessions,
            string itemId,
            int grade,
            ContentOrigin origin,
            ItemUseResult result,
            int nativeResult)
        {
            var context = new ItemOverlayContext(itemId, grade, origin, result, nativeResult);
            foreach (ItemOverlaySession session in sessions)
            {
                try
                {
                    session.Behavior.AfterUse(context, session.Lifetime);
                }
                catch (Exception ex)
                {
                    Report(ex, "running AfterUse for Overlay " + session.Definition.Id,
                        session.Definition.ProviderAssembly);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ItemExecutionPipeline));
            }
        }

        private static void Report(Exception exception, string operation, System.Reflection.Assembly owner)
        {
            try
            {
                PolarisAPI.Errors.Report(exception, operation, owner);
            }
            catch
            {
                // Core 诊断未启动时也保持单项隔离。
            }
        }

        private sealed class ItemUseContext : IItemUseContext
        {
            internal ItemUseContext(string itemId, int grade)
            {
                ItemId = itemId;
                Grade = grade;
            }

            public string ItemId { get; }

            public int Grade { get; }
        }
    }
}
