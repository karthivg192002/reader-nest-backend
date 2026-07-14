namespace iucs.readernest.application.Dto.Billing
{
    /// <summary>A selectable checkout method — an enabled payment-gateway integration (key = integration key), for the parent Pay Now popup.</summary>
    public class PaymentMethodOptionDto
    {
        public string Key { get; set; } = null!;

        public string Name { get; set; } = null!;
    }
}
