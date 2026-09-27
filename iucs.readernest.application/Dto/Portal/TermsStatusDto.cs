namespace iucs.readernest.application.Dto.Portal
{
    /// <summary>Whether the signed-in parent still has to accept the Terms and Conditions before paying.</summary>
    public class TermsStatusDto
    {
        /// <summary>True only until their first payment; once accepted it is never asked again.</summary>
        public bool Required { get; set; }
    }
}
