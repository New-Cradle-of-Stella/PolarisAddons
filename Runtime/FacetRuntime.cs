using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polaris.Addons.Authoring;
using Polaris.Addons.Catalog;
using Polaris.Addons.Definitions;

namespace Polaris.Addons.Runtime
{
    internal sealed class FacetRuntime : IDisposable
    {
        private readonly AddonCatalog catalog;
        private readonly AddonStateStore state;
        private readonly Dictionary<string, PluginSession> plugins = new Dictionary<string, PluginSession>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillSession> skills = new Dictionary<string, SkillSession>(StringComparer.Ordinal);
        private readonly Dictionary<string, CancellationTokenSource> executions =
            new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> cooldowns =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private bool disposed;

        internal FacetRuntime(AddonCatalog catalog, AddonStateStore state)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>获得与启用状态按 Facet id 存放，插件与技能共用同一份状态表。</summary>
        internal bool IsObtained(string id) => state.IsObtained(id);

        internal bool IsEnabled(string id) => state.IsEnabled(id);

        internal void SyncPlugin(string id, bool obtained, bool enabled, bool persist)
        {
            if (disposed)
            {
                return;
            }

            PluginDefinition definition = catalog.GetPlugin(id);
            if (definition == null)
            {
                return;
            }

            enabled &= obtained;
            if (persist)
            {
                state.SetObtained(id, obtained);
                state.SetEnabled(id, enabled);
            }

            if (enabled && !plugins.ContainsKey(id))
            {
                ActivatePlugin(definition);
            }
            else if (!enabled && plugins.TryGetValue(id, out PluginSession session))
            {
                plugins.Remove(id);
                session.Dispose();
            }
        }

        internal void SyncSkill(string id, bool obtained, bool enabled, bool persist)
        {
            if (disposed)
            {
                return;
            }

            SkillDefinition definition = catalog.GetSkill(id);
            if (definition == null)
            {
                return;
            }

            enabled &= obtained;
            if (persist)
            {
                state.SetObtained(id, obtained);
                state.SetEnabled(id, enabled);
            }

            if (enabled && !skills.ContainsKey(id))
            {
                EnableSkill(definition);
            }
            else if (!enabled && skills.TryGetValue(id, out SkillSession session))
            {
                CancelExecution(session.Definition);
                skills.Remove(id);
                session.Dispose();
            }
        }

        internal async ValueTask<SkillExecutionResult> ExecuteSkillAsync(
            string id,
            CancellationToken cancellationToken)
        {
            if (disposed || !state.IsObtained(id) || !state.IsEnabled(id) ||
                !skills.TryGetValue(id, out SkillSession session) ||
                !(session.Behavior is IActiveSkillBehavior active))
            {
                return SkillExecutionResult.Rejected;
            }

            CancellationTokenSource execution;
            string group = ExecutionGroupOf(session.Definition);
            lock (executions)
            {
                if (executions.ContainsKey(group) ||
                    (cooldowns.TryGetValue(id, out DateTime until) && DateTime.UtcNow < until))
                {
                    return SkillExecutionResult.Rejected;
                }

                execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                executions.Add(group, execution);
                if (session.Definition.CooldownSeconds > 0)
                {
                    cooldowns[id] = DateTime.UtcNow.AddSeconds(session.Definition.CooldownSeconds);
                }
            }

            try
            {
                return await active.ExecuteAsync(session.Context, execution.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (execution.IsCancellationRequested)
            {
                return SkillExecutionResult.Cancelled;
            }
            catch (Exception ex)
            {
                AddonDiagnostics.Report(ex, "executing Addons skill " + id, session.Definition.ProviderAssembly);
                return SkillExecutionResult.Failed;
            }
            finally
            {
                lock (executions)
                {
                    executions.Remove(group);
                }
                execution.Dispose();
            }
        }

        internal void UnlockOwnedFacets(string itemId, bool consumed)
        {
            foreach (PluginDefinition plugin in catalog.Plugins.Where(x =>
                string.Equals(x.ItemId, itemId, StringComparison.Ordinal)))
            {
                state.SetObtained(plugin.Id, true);
            }
            foreach (SkillDefinition skill in catalog.Skills.Where(x =>
                string.Equals(x.ItemId, itemId, StringComparison.Ordinal) &&
                (x.Unlock == AddonSkillUnlockPolicy.OwnItem ||
                 (consumed && x.Unlock == AddonSkillUnlockPolicy.ConsumeOwnerItem))))
            {
                state.SetObtained(skill.Id, true);
                state.SetEnabled(skill.Id, true);
            }
        }

        internal void CancelExecutions()
        {
            CancellationTokenSource[] active;
            lock (executions)
            {
                active = executions.Values.ToArray();
            }
            foreach (CancellationTokenSource execution in active)
            {
                execution.Cancel();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            CancelExecutions();
            foreach (PluginSession session in plugins.Values.Reverse())
            {
                session.Dispose();
            }

            foreach (SkillSession session in skills.Values.Reverse())
            {
                session.Dispose();
            }

            plugins.Clear();
            skills.Clear();
        }

        private void ActivatePlugin(PluginDefinition definition)
        {
            if (definition.BehaviorType == null)
            {
                return;
            }
            PluginSession session = null;
            try
            {
                var behavior = (IPluginBehavior)catalog.CreateBehavior(definition);
                session = new PluginSession(definition, behavior);
                behavior.Activate(session.Context, session.Lifetime);
                plugins.Add(definition.Id, session);
                session = null;
            }
            catch (Exception ex)
            {
                AddonDiagnostics.Report(
                    ex,
                    "activating Addons plugin " + definition.Id,
                    definition.ProviderAssembly);
            }
            finally
            {
                session?.Dispose();
            }
        }

        private void EnableSkill(SkillDefinition definition)
        {
            if (definition.BehaviorType == null)
            {
                return;
            }
            SkillSession session = null;
            try
            {
                var behavior = (ISkillBehavior)catalog.CreateBehavior(definition);
                session = new SkillSession(definition, behavior);
                behavior.Enable(session.Context, session.Lifetime);
                skills.Add(definition.Id, session);
                session = null;
            }
            catch (Exception ex)
            {
                AddonDiagnostics.Report(
                    ex,
                    "enabling Addons skill " + definition.Id,
                    definition.ProviderAssembly);
            }
            finally
            {
                session?.Dispose();
            }
        }

        private void CancelExecution(SkillDefinition definition)
        {
            CancellationTokenSource execution;
            lock (executions)
            {
                executions.TryGetValue(ExecutionGroupOf(definition), out execution);
            }

            execution?.Cancel();
        }

        /// <summary>没有声明互斥组的技能以自身 id 独占一组。</summary>
        private static string ExecutionGroupOf(SkillDefinition definition) =>
            string.IsNullOrEmpty(definition.ConcurrencyGroup) ? definition.Id : definition.ConcurrencyGroup;

        private sealed class PluginSession : IDisposable
        {
            internal PluginSession(PluginDefinition definition, IPluginBehavior behavior)
            {
                Definition = definition;
                Behavior = behavior;
                Lifetime = new BehaviorLifetime();
                Context = new PluginContext(definition.ItemId, definition.Id);
            }
            internal PluginDefinition Definition { get; }
            internal IPluginBehavior Behavior { get; }
            internal BehaviorLifetime Lifetime { get; }
            internal PluginContext Context { get; }
            public void Dispose() => Lifetime.Dispose();
        }

        private sealed class SkillSession : IDisposable
        {
            internal SkillSession(SkillDefinition definition, ISkillBehavior behavior)
            {
                Definition = definition;
                Behavior = behavior;
                Lifetime = new BehaviorLifetime();
                Context = new SkillContext(definition.ItemId, definition.Id);
            }
            internal SkillDefinition Definition { get; }
            internal ISkillBehavior Behavior { get; }
            internal BehaviorLifetime Lifetime { get; }
            internal SkillContext Context { get; }
            public void Dispose() => Lifetime.Dispose();
        }

        private sealed class PluginContext : IPluginContext
        {
            internal PluginContext(string itemId, string pluginId) { ItemId = itemId; PluginId = pluginId; }
            public string ItemId { get; }
            public string PluginId { get; }
        }

        private sealed class SkillContext : ISkillContext
        {
            internal SkillContext(string itemId, string skillId) { ItemId = itemId; SkillId = skillId; }
            public string ItemId { get; }
            public string SkillId { get; }
        }
    }
}
