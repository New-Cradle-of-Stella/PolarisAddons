using System;
using System.Text.RegularExpressions;

namespace Polaris.Addons.Authoring
{
    public static class AddonFormatVersion
    {
        public const int Current = 1;
    }

    public enum AddonDocumentKind
    {
        Item,
        Plugin,
        Skill,
    }

    public enum AddonSkillMode
    {
        Passive,
        Active,
        Toggle,
    }

    public enum AddonSkillUnlockPolicy
    {
        OwnItem,
        ConsumeOwnerItem,
        External,
    }

    public sealed class AddonFormatException : Exception
    {
        public AddonFormatException(string message) : base(message) { }

        public AddonFormatException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>只依赖 BCL，PolarisTools 通过源码链接复用这一份规则。</summary>
    public static class AddonIdentifier
    {
        private static readonly Regex StableId = new Regex(
            "^[a-z][a-z0-9]*(?:[._/-][a-z0-9]+)+$",
            RegexOptions.CultureInvariant);

        private static readonly Regex TypeName = new Regex(
            "^(?:global::)?[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex CSharpName = new Regex(
            "^[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.CultureInvariant);

        public static bool IsValidId(string value) =>
            !string.IsNullOrWhiteSpace(value) && StableId.IsMatch(value);

        public static bool IsValidOptionalTypeName(string value) =>
            string.IsNullOrWhiteSpace(value) || TypeName.IsMatch(value.Trim());

        public static bool IsValidName(string value) =>
            !string.IsNullOrWhiteSpace(value) && CSharpName.IsMatch(value);

        public static bool IsValidNamespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split('.');
            foreach (string part in parts)
            {
                if (!IsValidName(part))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
