using System;
using System.Collections.Generic;
using System.Linq;
using nel;
using Polaris.Addons.Catalog;
using Polaris.Addons.Definitions;
using Polaris.Addons.Runtime;

namespace Polaris.Addons.Adapters
{
    /// <summary>
    /// 技能（原版 PrSkill 与技能书物品）的目录与 UI 镜像：只负责把定义投影成原版对象、把原版状态
    /// 同步回 <see cref="FacetRuntime"/>。玩法状态与效果本身归 FacetRuntime。
    /// </summary>
    internal sealed class AliceSkillAdapter : IDisposable
    {
        private const int DefaultSkillBookIcon = 18;

        private readonly AddonCatalog catalog;
        private readonly FacetRuntime runtime;

        private readonly Dictionary<string, SkillBinding> skills =
            new Dictionary<string, SkillBinding>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> skillIdsByBookKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal AliceSkillAdapter(AddonCatalog catalog, FacetRuntime runtime)
        {
            this.catalog = catalog;
            this.runtime = runtime;
        }

        internal void InstallSkills()
        {
            Dictionary<string, PrSkill> dictionary = SkillManager.getSkillDictionary();
            if (dictionary == null)
            {
                return;
            }

            DropStaleBindings(dictionary);

            // 先按当前目录投影原版技能，再安装自定义技能：这样首次安装新建的 PrSkill
            // 不会被当成原版内容镜像进目录。
            NativeSkillProjection projection = ProjectNativeSkills(dictionary);
            InstallCustomSkills(dictionary);

            PublishVirtualBooks(projection.VirtualBooks);
            catalog.ReplaceNativeSkills(projection.Skills);
            ApplySavedState();
        }

        internal void ObserveSkills(bool persist)
        {
            foreach (SkillBinding binding in skills.Values)
            {
                runtime.SyncSkill(binding.Definition.Id, binding.Skill.visible, binding.Skill.enabled, persist);
            }
        }

        internal void ApplySavedState()
        {
            foreach (SkillBinding binding in skills.Values)
            {
                binding.Skill.visible = runtime.IsObtained(binding.Definition.Id);
                binding.Skill.enabled = binding.Skill.visible && runtime.IsEnabled(binding.Definition.Id);
                runtime.SyncSkill(binding.Definition.Id, binding.Skill.visible, binding.Skill.enabled, false);
            }
        }

        internal bool SetSkillEnabled(string id, bool enabled)
        {
            if (!skills.TryGetValue(id, out SkillBinding binding) || !binding.Skill.visible)
            {
                return false;
            }

            binding.Skill.enabled = enabled;
            ObserveSkills(true);
            return true;
        }

        internal bool SetSkillObtained(string id, bool obtained)
        {
            if (!skills.TryGetValue(id, out SkillBinding binding))
            {
                return false;
            }

            if (obtained)
            {
                binding.Skill.Obtain(false);
            }
            else
            {
                binding.Skill.ReleaseObtain();
            }

            ObserveSkills(true);
            return binding.Skill.visible == obtained;
        }

        /// <summary>使用自定义技能书：解锁对应技能，原版返回码固定为 1（已消费）。</summary>
        internal bool TryUseSkillBook(string nativeKey, out int result)
        {
            result = 0;
            if (!skillIdsByBookKey.TryGetValue(nativeKey, out string id))
            {
                return false;
            }

            runtime.SyncSkill(id, true, true, true);
            if (skills.TryGetValue(id, out SkillBinding binding))
            {
                binding.Skill.Obtain(false);
            }

            result = 1;
            return true;
        }

        internal string SkillTitle(PrSkill skill, bool description)
        {
            SkillBinding binding = skills.Values.FirstOrDefault(x => ReferenceEquals(x.Skill, skill));
            if (binding == null)
            {
                return null;
            }

            return AdapterText.Resolve(
                description ? binding.Definition.DescriptionKey : binding.Definition.TitleKey,
                binding.Definition.Id);
        }

        /// <summary>原版存档只认自己的技能表，写盘期间先把自定义技能隐藏起来。</summary>
        internal List<SkillSerializationState> SuppressCustomSkills()
        {
            var states = new List<SkillSerializationState>(skills.Count);
            foreach (SkillBinding binding in skills.Values)
            {
                states.Add(new SkillSerializationState(binding.Skill));
                binding.Skill.visible = false;
                binding.Skill.first_visible = false;
            }

            return states;
        }

        internal void RestoreCustomSkills(IEnumerable<SkillSerializationState> states)
        {
            if (states == null)
            {
                return;
            }

            foreach (SkillSerializationState state in states)
            {
                state.Restore();
            }
        }

        public void Dispose()
        {
            skills.Clear();
            skillIdsByBookKey.Clear();
        }

        /// <summary>原版重跑 initScript 后旧对象作废；重新安装前先丢掉指向它们的绑定。</summary>
        private void DropStaleBindings(IReadOnlyDictionary<string, PrSkill> dictionary)
        {
            string[] stale = skills
                .Where(x => !dictionary.TryGetValue(x.Value.Skill.key, out PrSkill current)
                    || !ReferenceEquals(current, x.Value.Skill)
                    || !ReferenceEquals(NelItem.GetById(x.Value.Book.key, true), x.Value.Book))
                .Select(x => x.Key)
                .ToArray();
            foreach (string id in stale)
            {
                skillIdsByBookKey.Remove(skills[id].Book.key);
                skills.Remove(id);
            }
        }

        /// <summary>没有实体技能书的原版技能仍要有物品身份，因此一并投影出虚拟技能书。</summary>
        private NativeSkillProjection ProjectNativeSkills(Dictionary<string, PrSkill> dictionary)
        {
            var native = new List<NativeSkillDescriptor>();
            var virtualBooks = new List<NativeItemDescriptor>();
            foreach (KeyValuePair<string, PrSkill> entry in dictionary.ToArray())
            {
                if (skills.Values.Any(x => ReferenceEquals(x.Skill, entry.Value)))
                {
                    continue;
                }

                string bookKey = SkillManager.skillbook_item_header + entry.Key;
                NelItem book = NelItem.GetById(bookKey, true);
                string itemId = NativeItemId.FromKey(bookKey);
                string title = AdapterSafe.Read(() => entry.Value.title, entry.Key);
                string description = AdapterSafe.Read(() => entry.Value.descript, string.Empty);
                if (book == null)
                {
                    virtualBooks.Add(new NativeItemDescriptor(
                        itemId, bookKey, title, description, string.Empty, 0, 1, "Virtual", true));
                }

                native.Add(new NativeSkillDescriptor(
                    NativeFacetId.Skill(entry.Key),
                    itemId,
                    entry.Key,
                    title,
                    description,
                    book?.specific_icon_id.ToString() ?? string.Empty));
            }

            return new NativeSkillProjection(native, virtualBooks);
        }

        private void InstallCustomSkills(IDictionary<string, PrSkill> dictionary)
        {
            foreach (SkillDefinition definition in catalog.Skills.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (skills.ContainsKey(definition.Id))
                {
                    continue;
                }

                try
                {
                    string key = AdapterKey.For("skill", definition.Id);
                    string bookKey = SkillManager.skillbook_item_header + key;
                    PrSkill skill = SkillManager.Get(key);
                    if (skill == null)
                    {
                        skill = CreateSkill(key);
                        dictionary[key] = skill;
                    }

                    NelItem book = NelItem.GetById(bookKey, true) ?? CreateSkillBook(bookKey, definition);
                    book.value = ushort.MaxValue;
                    skills.Add(definition.Id, new SkillBinding(definition, skill, book));
                    skillIdsByBookKey[bookKey] = definition.Id;
                }
                catch (Exception ex)
                {
                    AddonDiagnostics.Report(ex, "installing Addons skill " + definition.Id);
                }
            }
        }

        private static PrSkill CreateSkill(string key) => new PrSkill(key, ushort.MaxValue)
        {
            category = SkillManager.SKILL_CTG.SPECIAL,
            desc_key_replace = key,
        };

        private static NelItem CreateSkillBook(string bookKey, SkillDefinition definition) =>
            NelItem.CreateItemEntry(
                bookKey,
                new NelItem(bookKey, 0, 300, 1)
                {
                    category = (NelItem.CATEG)2097153u,
                    FnGetName = NelItem.fnGetNameSkillBook,
                    FnGetDesc = NelItem.fnGetDescSkillBook,
                    FnGetDetail = NelItem.fnGetDetailSkillBook,
                    specific_icon_id = ParseIcon(definition.Icon, DefaultSkillBookIcon),
                },
                ushort.MaxValue);

        private void PublishVirtualBooks(IReadOnlyCollection<NativeItemDescriptor> virtualBooks)
        {
            if (virtualBooks.Count == 0)
            {
                return;
            }

            string[] virtualIds = virtualBooks.Select(x => x.Id).ToArray();
            catalog.ReplaceNativeItems(catalog.NativeItems
                .Where(x => !virtualIds.Contains(x.Id, StringComparer.Ordinal))
                .Concat(virtualBooks));
        }

        private static int ParseIcon(string value, int fallback) =>
            int.TryParse(value, out int result) ? result : fallback;

        /// <summary>一次原版技能投影的结果：技能镜像，以及需要补齐的虚拟技能书物品。</summary>
        private sealed class NativeSkillProjection
        {
            internal NativeSkillProjection(
                List<NativeSkillDescriptor> skills,
                List<NativeItemDescriptor> virtualBooks)
            {
                Skills = skills;
                VirtualBooks = virtualBooks;
            }

            internal List<NativeSkillDescriptor> Skills { get; }

            internal List<NativeItemDescriptor> VirtualBooks { get; }
        }

        private sealed class SkillBinding
        {
            internal SkillBinding(SkillDefinition definition, PrSkill skill, NelItem book)
            {
                Definition = definition;
                Skill = skill;
                Book = book;
            }

            internal SkillDefinition Definition { get; }

            internal PrSkill Skill { get; }

            internal NelItem Book { get; }
        }
    }

    /// <summary>写盘期间被隐藏的自定义技能的原始可见性，用于写盘后原样恢复。</summary>
    internal sealed class SkillSerializationState
    {
        private readonly PrSkill skill;
        private readonly bool visible;
        private readonly bool firstVisible;

        internal SkillSerializationState(PrSkill skill)
        {
            this.skill = skill;
            visible = skill.visible;
            firstVisible = skill.first_visible;
        }

        internal void Restore()
        {
            skill.visible = visible;
            skill.first_visible = firstVisible;
        }
    }
}
