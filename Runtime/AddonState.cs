using System;
using System.Collections.Generic;
using Polaris.Save;

namespace Polaris.Addons.Runtime
{
    public interface IAddonStateStore
    {
        bool IsObtained(string facetId);
        bool IsEnabled(string facetId);
        byte[] ReadPayload(string ownerId, out int schemaVersion);
        void WritePayload(string ownerId, int schemaVersion, byte[] payload);
        byte[] MigratePayload(string ownerId, int targetSchemaVersion, Func<int, byte[], byte[]> migrate);
    }

    internal sealed class AddonSaveData : IPolarisSaveData
    {
        internal Dictionary<string, bool> Obtained = new Dictionary<string, bool>(StringComparer.Ordinal);
        internal Dictionary<string, bool> Enabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        internal Dictionary<string, int> Schemas = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Dictionary<string, byte[]> Payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public void Serialize(SaveArchive archive)
        {
            archive.ValueMap("obtained", ref Obtained);
            archive.ValueMap("enabled", ref Enabled);
            archive.ValueMap("schemas", ref Schemas);
            archive.ValueMap("payloads", ref Payloads);

            if (archive.Mode == SaveArchiveMode.AfterLoad)
            {
                Obtained ??= new Dictionary<string, bool>(StringComparer.Ordinal);
                Enabled ??= new Dictionary<string, bool>(StringComparer.Ordinal);
                Schemas ??= new Dictionary<string, int>(StringComparer.Ordinal);
                Payloads ??= new Dictionary<string, byte[]>(StringComparer.Ordinal);
            }
        }
    }

    internal sealed class AddonStateStore : IAddonStateStore
    {
        private readonly Func<AddonSaveData> current;

        internal AddonStateStore(Func<AddonSaveData> current) =>
            this.current = current ?? throw new ArgumentNullException(nameof(current));

        public bool IsObtained(string facetId) => Read(current().Obtained, facetId);
        public bool IsEnabled(string facetId) => Read(current().Enabled, facetId);
        internal void SetObtained(string facetId, bool value) => current().Obtained[facetId] = value;
        internal void SetEnabled(string facetId, bool value) => current().Enabled[facetId] = value;

        public byte[] ReadPayload(string ownerId, out int schemaVersion)
        {
            ValidateOwner(ownerId);
            AddonSaveData state = current();
            schemaVersion = state.Schemas.TryGetValue(ownerId, out int stored) ? stored : 0;
            return state.Payloads.TryGetValue(ownerId, out byte[] payload) && payload != null
                ? (byte[])payload.Clone()
                : null;
        }

        public void WritePayload(string ownerId, int schemaVersion, byte[] payload)
        {
            ValidateOwner(ownerId);
            if (schemaVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            AddonSaveData state = current();
            state.Schemas[ownerId] = schemaVersion;
            state.Payloads[ownerId] = payload == null ? null : (byte[])payload.Clone();
        }

        public byte[] MigratePayload(string ownerId, int targetSchemaVersion, Func<int, byte[], byte[]> migrate)
        {
            if (migrate == null) throw new ArgumentNullException(nameof(migrate));
            if (targetSchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(targetSchemaVersion));
            byte[] original = ReadPayload(ownerId, out int storedVersion);
            if (storedVersion >= targetSchemaVersion) return original;

            // 只有迁移完整成功后才提交；异常时原 payload 与版本保持不变，可只读恢复。
            byte[] migrated = migrate(storedVersion, original == null ? null : (byte[])original.Clone());
            WritePayload(ownerId, targetSchemaVersion, migrated);
            return migrated == null ? null : (byte[])migrated.Clone();
        }

        private static bool Read(IReadOnlyDictionary<string, bool> values, string id) =>
            id != null && values.TryGetValue(id, out bool value) && value;

        private static void ValidateOwner(string ownerId)
        {
            if (!Authoring.AddonIdentifier.IsValidId(ownerId))
            {
                throw new ArgumentException("Invalid Addons owner id '" + ownerId + "'.", nameof(ownerId));
            }
        }
    }
}
