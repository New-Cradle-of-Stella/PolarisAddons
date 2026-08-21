using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Polaris.Addons.Definitions;

namespace Polaris.Addons.Catalog
{
    /// <summary>
    /// 原版内容的只读镜像。Adapter 侧按类别整批替换，查询侧拿到的永远是一份已发布、不再变化的快照，
    /// 所以只有替换与取快照需要持锁，之后的过滤与排序都在锁外完成。
    /// </summary>
    internal sealed class NativeContentMirror
    {
        private readonly object gate = new object();
        private IReadOnlyDictionary<string, NativeItemDescriptor> items = Empty<NativeItemDescriptor>();
        private IReadOnlyDictionary<string, NativePluginDescriptor> plugins = Empty<NativePluginDescriptor>();
        private IReadOnlyDictionary<string, NativeSkillDescriptor> skills = Empty<NativeSkillDescriptor>();

        internal IReadOnlyDictionary<string, NativeItemDescriptor> Items
        {
            get { lock (gate) { return items; } }
        }

        internal IReadOnlyDictionary<string, NativePluginDescriptor> Plugins
        {
            get { lock (gate) { return plugins; } }
        }

        internal IReadOnlyDictionary<string, NativeSkillDescriptor> Skills
        {
            get { lock (gate) { return skills; } }
        }

        internal void ReplaceItems(IDictionary<string, NativeItemDescriptor> replacement)
        {
            var published = new ReadOnlyDictionary<string, NativeItemDescriptor>(replacement);
            lock (gate)
            {
                items = published;
            }
        }

        internal void ReplacePlugins(IEnumerable<NativePluginDescriptor> descriptors)
        {
            IReadOnlyDictionary<string, NativePluginDescriptor> published = Publish(descriptors, x => x.Id);
            lock (gate)
            {
                plugins = published;
            }
        }

        internal void ReplaceSkills(IEnumerable<NativeSkillDescriptor> descriptors)
        {
            IReadOnlyDictionary<string, NativeSkillDescriptor> published = Publish(descriptors, x => x.Id);
            lock (gate)
            {
                skills = published;
            }
        }

        private static IReadOnlyDictionary<string, TDescriptor> Publish<TDescriptor>(
            IEnumerable<TDescriptor> descriptors,
            Func<TDescriptor, string> idOf) =>
            new ReadOnlyDictionary<string, TDescriptor>(
                (descriptors ?? Array.Empty<TDescriptor>()).ToDictionary(idOf, StringComparer.Ordinal));

        private static IReadOnlyDictionary<string, TDescriptor> Empty<TDescriptor>() =>
            new ReadOnlyDictionary<string, TDescriptor>(
                new Dictionary<string, TDescriptor>(StringComparer.Ordinal));
    }
}
