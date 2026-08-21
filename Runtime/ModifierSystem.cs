using System;
using System.Collections.Generic;
using System.Linq;
using Polaris.Addons.Authoring;

namespace Polaris.Addons.Runtime
{
    public enum ModifierOperation
    {
        Add,
        Multiply,
        Override,
    }

    public sealed class ModifierContribution
    {
        internal ModifierContribution(
            string sourceId,
            string statId,
            ModifierOperation operation,
            double value,
            int priority)
        {
            SourceId = sourceId;
            StatId = statId;
            Operation = operation;
            Value = value;
            Priority = priority;
        }

        public string SourceId { get; }
        public string StatId { get; }
        public ModifierOperation Operation { get; }
        public double Value { get; }
        public int Priority { get; }
    }

    public interface IModifierSink
    {
        IDisposable Contribute(
            string sourceId,
            string statId,
            ModifierOperation operation,
            double value,
            int priority = 0);

        double Evaluate(string statId, double baseValue);

        IReadOnlyList<ModifierContribution> GetContributions(string statId);

        void RemoveSource(string sourceId);
    }

    internal sealed class ModifierEngine : IModifierSink
    {
        private readonly object gate = new object();
        private readonly Dictionary<long, ModifierContribution> contributions =
            new Dictionary<long, ModifierContribution>();
        private long nextId;

        public IDisposable Contribute(
            string sourceId,
            string statId,
            ModifierOperation operation,
            double value,
            int priority = 0)
        {
            ValidateId(sourceId, nameof(sourceId));
            ValidateId(statId, nameof(statId));
            long id;
            lock (gate)
            {
                id = ++nextId;
                contributions.Add(id, new ModifierContribution(sourceId, statId, operation, value, priority));
            }

            return new Removal(this, id);
        }

        public double Evaluate(string statId, double baseValue)
        {
            ModifierContribution[] snapshot = Snapshot(statId);
            double result = baseValue;
            foreach (ModifierContribution contribution in snapshot.Where(x => x.Operation == ModifierOperation.Add))
            {
                result += contribution.Value;
            }
            foreach (ModifierContribution contribution in snapshot.Where(x => x.Operation == ModifierOperation.Multiply))
            {
                result *= contribution.Value;
            }
            foreach (ModifierContribution contribution in snapshot.Where(x => x.Operation == ModifierOperation.Override))
            {
                result = contribution.Value;
            }
            return result;
        }

        public IReadOnlyList<ModifierContribution> GetContributions(string statId) => Snapshot(statId);

        public void RemoveSource(string sourceId)
        {
            if (sourceId == null)
            {
                return;
            }

            lock (gate)
            {
                foreach (long id in contributions
                    .Where(x => string.Equals(x.Value.SourceId, sourceId, StringComparison.Ordinal))
                    .Select(x => x.Key)
                    .ToArray())
                {
                    contributions.Remove(id);
                }
            }
        }

        private ModifierContribution[] Snapshot(string statId)
        {
            ValidateId(statId, nameof(statId));
            lock (gate)
            {
                return contributions.Values
                    .Where(x => string.Equals(x.StatId, statId, StringComparison.Ordinal))
                    .OrderBy(x => x.Priority)
                    .ThenBy(x => x.SourceId, StringComparer.Ordinal)
                    .ThenBy(x => x.Operation)
                    .ToArray();
            }
        }

        private void Remove(long id)
        {
            lock (gate)
            {
                contributions.Remove(id);
            }
        }

        private static void ValidateId(string value, string parameter)
        {
            if (!AddonIdentifier.IsValidId(value))
            {
                throw new ArgumentException("Invalid Addons id '" + value + "'.", parameter);
            }
        }

        private sealed class Removal : IDisposable
        {
            private ModifierEngine owner;
            private readonly long id;

            internal Removal(ModifierEngine owner, long id)
            {
                this.owner = owner;
                this.id = id;
            }

            public void Dispose()
            {
                ModifierEngine current = System.Threading.Interlocked.Exchange(ref owner, null);
                current?.Remove(id);
            }
        }
    }
}
