namespace RimageGui.Core
{
    /// <summary>Outcome of a validation pass: either OK or a localized failure.</summary>
    public sealed class ValidationResult
    {
        private ValidationResult(string messageKey, string detail)
        {
            MessageKey = messageKey;
            Detail = detail;
        }

        public static readonly ValidationResult Ok = new ValidationResult(null, null);

        public static ValidationResult Fail(string messageKey, string detail = null) =>
            new ValidationResult(messageKey, detail);

        public bool IsValid => MessageKey == null;

        /// <summary>Catalog key for the localized message.</summary>
        public string MessageKey { get; }

        /// <summary>Optional path or value appended after the message.</summary>
        public string Detail { get; }
    }
}
