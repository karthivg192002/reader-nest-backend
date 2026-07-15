namespace iucs.readernest.domain.Enums
{
    public enum PaymentMethod
    {
        Card,
        Upi,
        NetBanking,
        Wallet,
        Other,

        /// <summary>Offline payment collected at the centre; recorded pending until an admin confirms receipt.</summary>
        Cash
    }
}
