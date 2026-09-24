namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Parents who have no email (they ask for everything on WhatsApp) still get a portal login:
    /// they sign in with their mobile number + PIN. Every account still needs a unique email-shaped
    /// login key, so theirs is an internal one on the reserved <c>.invalid</c> domain
    /// (<see cref="PlaceholderEmail"/>) — never shown to them, never deliverable, and every email
    /// sender skips it (<see cref="IsDeliverable"/>).
    /// </summary>
    public static class ParentLogin
    {
        public const string NoEmailDomain = "no-email.invalid";

        /// <summary>
        /// Digits only, last 10 kept (so "+91 98765 43210", "098765 43210" and "9876543210" all
        /// match). Null when there are too few digits to be a phone number.
        /// </summary>
        public static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length < 7)
            {
                return null;
            }

            return digits.Length > 10 ? digits[^10..] : digits;
        }

        public static string PlaceholderEmail(string normalizedPhone) => $"m{normalizedPhone}@{NoEmailDomain}";

        public static bool IsPlaceholderEmail(string? email) =>
            email is not null && email.Trim().EndsWith("@" + NoEmailDomain, StringComparison.OrdinalIgnoreCase);

        /// <summary>False for a blank address or an internal no-email login key — nothing may be mailed to either.</summary>
        public static bool IsDeliverable(string? email) => !string.IsNullOrWhiteSpace(email) && !IsPlaceholderEmail(email);

        /// <summary>
        /// A parent's email is optional (WhatsApp-only parents), but then a mobile number is a must:
        /// it's how they're reached and how they log in. Returns the lower-cased email or, with none,
        /// the internal login key. Storing that key on the demo booking keeps every email-based
        /// demo↔parent link (portal schedule, feedback prompts, join capture, history grouping)
        /// working unchanged, and no mail is ever sent to it.
        /// </summary>
        public static string ResolveLoginEmail(string? email, string? phone)
        {
            var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0 || IsPlaceholderEmail(normalized))
            {
                var normalizedPhone = NormalizePhone(phone)
                    ?? throw new Exceptions.DomainValidationException("Enter the parent's email or mobile number.");
                return PlaceholderEmail(normalizedPhone);
            }

            if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(normalized))
            {
                throw new Exceptions.DomainValidationException("The parent's email address is not valid.");
            }
            return normalized;
        }

        /// <summary>Looks like a phone number rather than an email (the login box accepts either).</summary>
        public static bool LooksLikePhone(string identifier) =>
            !identifier.Contains('@') && NormalizePhone(identifier) is not null;
    }
}
