using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Polaris.Addons.Authoring
{
    public abstract class AddonDefinitionDocument
    {
        public int Version { get; set; } = AddonFormatVersion.Current;

        public string Id { get; set; } = string.Empty;

        public string BehaviorType { get; set; } = string.Empty;

        public abstract AddonDocumentKind Kind { get; }

        protected static XElement ParseRoot(string xml, string expectedName)
        {
            try
            {
                XDocument document = XDocument.Parse(xml ?? string.Empty, LoadOptions.None);
                XElement root = document.Root;
                if (root == null || !string.Equals(root.Name.LocalName, expectedName, StringComparison.Ordinal))
                {
                    throw new AddonFormatException("Expected <" + expectedName + "> as the document root.");
                }

                int version = IntAttribute(root, "Version", required: true, fallback: 0);
                if (version < 1 || version > AddonFormatVersion.Current)
                {
                    throw new AddonFormatException(
                        "Unsupported " + expectedName + " format version " + version + ".");
                }

                return root;
            }
            catch (AddonFormatException)
            {
                throw;
            }
            catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
            {
                throw new AddonFormatException("Invalid " + expectedName + " XML: " + ex.Message, ex);
            }
        }

        protected static string Attribute(XElement element, string name, string fallback = "") =>
            element.Attribute(name)?.Value ?? fallback;

        protected static int IntAttribute(
            XElement element,
            string name,
            bool required,
            int fallback)
        {
            XAttribute attribute = element.Attribute(name);
            if (attribute == null)
            {
                if (required)
                {
                    throw new AddonFormatException(
                        "Missing attribute '" + name + "' on <" + element.Name.LocalName + ">.");
                }

                return fallback;
            }

            if (!int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new AddonFormatException(
                    "Attribute '" + name + "' on <" + element.Name.LocalName + "> must be an integer.");
            }

            return value;
        }

        protected static double DoubleAttribute(XElement element, string name, double fallback)
        {
            string text = Attribute(element, name, string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new AddonFormatException(
                    "Attribute '" + name + "' on <" + element.Name.LocalName + "> must be a number.");
            }

            return value;
        }

        protected static TEnum EnumAttribute<TEnum>(XElement element, string name, TEnum fallback)
            where TEnum : struct
        {
            string text = Attribute(element, name, string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            if (!Enum.TryParse(text, ignoreCase: false, out TEnum value) || !Enum.IsDefined(typeof(TEnum), value))
            {
                throw new AddonFormatException(
                    "Attribute '" + name + "' on <" + element.Name.LocalName + "> has an unknown value '" + text + "'.");
            }

            return value;
        }

        protected static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var text = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&': text.Append("&amp;"); break;
                    case '<': text.Append("&lt;"); break;
                    case '>': text.Append("&gt;"); break;
                    case '"': text.Append("&quot;"); break;
                    case '\r': text.Append("&#xD;"); break;
                    case '\n': text.Append("&#xA;"); break;
                    case '\t': text.Append("&#x9;"); break;
                    default: text.Append(c); break;
                }
            }

            return text.ToString();
        }

        protected static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        protected static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// 单元素文档：按给定顺序写出属性。三种定义文档的差别只有根元素名和属性表，
        /// 所以拼接规则只在这里实现一次。
        /// </summary>
        protected static string SingleElementDocument(
            string rootElementName,
            IEnumerable<KeyValuePair<string, string>> attributes)
        {
            var text = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<");
            text.Append(rootElementName);
            foreach (KeyValuePair<string, string> attribute in attributes)
            {
                text.Append(' ').Append(attribute.Key).Append("=\"").Append(Escape(attribute.Value)).Append('"');
            }

            return text.Append(" />\r\n").ToString();
        }

        protected static KeyValuePair<string, string> Attr(string name, string value) =>
            new KeyValuePair<string, string>(name, value);
    }

    public sealed class ItemDefinitionDocument : AddonDefinitionDocument
    {
        public const string RootElementName = "PItem";

        public override AddonDocumentKind Kind => AddonDocumentKind.Item;

        public string NameKey { get; set; } = string.Empty;

        public string DescriptionKey { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;

        public int Price { get; set; }

        public int StackLimit { get; set; } = 1;

        public string Category { get; set; } = "Other";

        public static ItemDefinitionDocument CreateTemplate() => new ItemDefinitionDocument
        {
            Id = "local.item",
            NameKey = "item.local.name",
            DescriptionKey = "item.local.description",
        };

        public static ItemDefinitionDocument Parse(string xml)
        {
            XElement root = ParseRoot(xml, RootElementName);
            return new ItemDefinitionDocument
            {
                Version = IntAttribute(root, "Version", true, 1),
                Id = Attribute(root, "Id"),
                NameKey = Attribute(root, "NameKey"),
                DescriptionKey = Attribute(root, "DescriptionKey"),
                Icon = Attribute(root, "Icon"),
                Price = IntAttribute(root, "Price", false, 0),
                StackLimit = IntAttribute(root, "StackLimit", false, 1),
                Category = Attribute(root, "Category", "Other"),
                BehaviorType = Attribute(root, "BehaviorType"),
            };
        }

        public string ToXml() => SingleElementDocument(RootElementName, new[]
        {
            Attr("Version", Int(Version)),
            Attr("Id", Id),
            Attr("NameKey", NameKey),
            Attr("DescriptionKey", DescriptionKey),
            Attr("Icon", Icon),
            Attr("Price", Int(Price)),
            Attr("StackLimit", Int(StackLimit)),
            Attr("Category", Category),
            Attr("BehaviorType", BehaviorType),
        });
    }

    public sealed class PluginDefinitionDocument : AddonDefinitionDocument
    {
        public const string RootElementName = "PPlugin";

        public override AddonDocumentKind Kind => AddonDocumentKind.Plugin;

        public string ItemId { get; set; } = string.Empty;

        public string TitleKey { get; set; } = string.Empty;

        public string DescriptionKey { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;

        public int Cost { get; set; } = 1;

        public static PluginDefinitionDocument CreateTemplate() => new PluginDefinitionDocument
        {
            Id = "local.plugin",
            ItemId = "local.item",
            TitleKey = "plugin.local.title",
            DescriptionKey = "plugin.local.description",
        };

        public static PluginDefinitionDocument Parse(string xml)
        {
            XElement root = ParseRoot(xml, RootElementName);
            return new PluginDefinitionDocument
            {
                Version = IntAttribute(root, "Version", true, 1),
                Id = Attribute(root, "Id"),
                ItemId = Attribute(root, "ItemId"),
                TitleKey = Attribute(root, "TitleKey"),
                DescriptionKey = Attribute(root, "DescriptionKey"),
                Icon = Attribute(root, "Icon"),
                Cost = IntAttribute(root, "Cost", false, 1),
                BehaviorType = Attribute(root, "BehaviorType"),
            };
        }

        public string ToXml() => SingleElementDocument(RootElementName, new[]
        {
            Attr("Version", Int(Version)),
            Attr("Id", Id),
            Attr("ItemId", ItemId),
            Attr("TitleKey", TitleKey),
            Attr("DescriptionKey", DescriptionKey),
            Attr("Icon", Icon),
            Attr("Cost", Int(Cost)),
            Attr("BehaviorType", BehaviorType),
        });
    }

    public sealed class SkillDefinitionDocument : AddonDefinitionDocument
    {
        public const string RootElementName = "PSkill";

        public override AddonDocumentKind Kind => AddonDocumentKind.Skill;

        public string ItemId { get; set; } = string.Empty;

        public string TitleKey { get; set; } = string.Empty;

        public string DescriptionKey { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;

        public AddonSkillMode Mode { get; set; } = AddonSkillMode.Passive;

        public AddonSkillUnlockPolicy Unlock { get; set; } = AddonSkillUnlockPolicy.ConsumeOwnerItem;

        public double CooldownSeconds { get; set; }

        public string ConcurrencyGroup { get; set; } = string.Empty;

        public static SkillDefinitionDocument CreateTemplate() => new SkillDefinitionDocument
        {
            Id = "local.skill",
            ItemId = "local.item",
            TitleKey = "skill.local.title",
            DescriptionKey = "skill.local.description",
        };

        public static SkillDefinitionDocument Parse(string xml)
        {
            XElement root = ParseRoot(xml, RootElementName);
            return new SkillDefinitionDocument
            {
                Version = IntAttribute(root, "Version", true, 1),
                Id = Attribute(root, "Id"),
                ItemId = Attribute(root, "ItemId"),
                TitleKey = Attribute(root, "TitleKey"),
                DescriptionKey = Attribute(root, "DescriptionKey"),
                Icon = Attribute(root, "Icon"),
                Mode = EnumAttribute(root, "Mode", AddonSkillMode.Passive),
                Unlock = EnumAttribute(root, "Unlock", AddonSkillUnlockPolicy.ConsumeOwnerItem),
                CooldownSeconds = DoubleAttribute(root, "CooldownSeconds", 0),
                ConcurrencyGroup = Attribute(root, "ConcurrencyGroup"),
                BehaviorType = Attribute(root, "BehaviorType"),
            };
        }

        public string ToXml() => SingleElementDocument(RootElementName, new[]
        {
            Attr("Version", Int(Version)),
            Attr("Id", Id),
            Attr("ItemId", ItemId),
            Attr("TitleKey", TitleKey),
            Attr("DescriptionKey", DescriptionKey),
            Attr("Icon", Icon),
            Attr("Mode", Mode.ToString()),
            Attr("Unlock", Unlock.ToString()),
            Attr("CooldownSeconds", Number(CooldownSeconds)),
            Attr("ConcurrencyGroup", ConcurrencyGroup),
            Attr("BehaviorType", BehaviorType),
        });
    }
}
