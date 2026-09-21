namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Reversible, encrypted storage for the PINs the SYSTEM issues (a temporary PIN generated on
    /// account creation, credential resend or admin reset), so an admin can look one up again
    /// instead of resetting it. The login check still uses the bcrypt hash only; a PIN a user
    /// chooses themselves is never put in the vault.
    /// </summary>
    public interface IPinVault
    {
        string Protect(string pin);

        /// <summary>The original PIN, or null when the value can't be decrypted (wrong key, tampered, unknown format).</summary>
        string? TryReveal(string protectedPin);
    }
}
