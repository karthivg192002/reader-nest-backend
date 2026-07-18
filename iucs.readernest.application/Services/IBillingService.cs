using iucs.readernest.application.Dto.Billing;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IBillingService
    {
        Task<IReadOnlyList<PackagePlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

        /// <summary>Department payment accounts with live transaction stats, for the Payment Gateway Mapping screen.</summary>
        Task<IReadOnlyList<PaymentAccountDto>> ListPaymentAccountsAsync(CancellationToken cancellationToken = default);

        /// <summary>Pins a parent's payments to a specific department account (admin override).</summary>
        Task SetParentPaymentAccountAsync(SavePaymentMappingRequest request, CancellationToken cancellationToken = default);

        /// <summary>Admin edit of a department account's name/provider/ref/active flag.</summary>
        Task<PaymentAccountDto> UpdatePaymentAccountAsync(Guid id, UpdatePaymentAccountRequest request, CancellationToken cancellationToken = default);

        Task<PackagePlanDto> CreatePlanAsync(SavePackagePlanRequest request, CancellationToken cancellationToken = default);

        Task<PackagePlanDto> UpdatePlanAsync(Guid id, SavePackagePlanRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<InvoiceDto>> ListInvoicesAsync(
            InvoiceStatus? status,
            Guid? parentProfileId,
            CancellationToken cancellationToken = default);

        Task<InvoiceDto> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a successful payment against an invoice (manual entry now;
        /// gateway webhooks call the same path once accounts are provisioned)
        /// and generates the receipt.
        /// </summary>
        Task<InvoiceDto> RecordPaymentAsync(Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a shareable Pay Now link for an open invoice via the gateway,
        /// routed through the invoice's department payment account.
        /// </summary>
        Task<PaymentLinkDto> CreatePaymentLinkAsync(Guid invoiceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ownership-checked invoice fetch for the parent portal download; returns the
        /// invoice with the billed parent's display name resolved.
        /// </summary>
        Task<(InvoiceDto Invoice, string ParentName)> GetParentInvoiceAsync(
            Guid parentUserId,
            Guid invoiceId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Parent Pay-Now: for "cash", records a pending cash intent for admin confirmation;
        /// for a gateway key, creates a checkout link plus a pending transaction the webhook settles.
        /// The invoice must belong to the calling parent.
        /// </summary>
        Task<ParentPaymentResultDto> InitiateParentPaymentAsync(
            Guid parentUserId,
            Guid invoiceId,
            InitiateParentPaymentRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gateway webhook settlement: marks the pending transaction with this gateway reference
        /// Success/Failed and, on success, applies the payment to the invoice (receipt, status,
        /// suspension auto-lift). Idempotent — an already-settled reference is a no-op.
        /// </summary>
        Task SettleGatewayTransactionAsync(
            string gatewayReference,
            bool succeeded,
            string? gatewayPaymentId,
            string? failureReason,
            CancellationToken cancellationToken = default);

        // Cash confirmation workflow: a parent's Pay Now "cash" choice records a pending
        // intent; billing staff review it here and confirm once the money is collected.
        Task<IReadOnlyList<CashIntentDto>> ListPendingCashIntentsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Confirms a pending cash intent: settles the same transaction row (no duplicate),
        /// generates the receipt and applies the amount to the invoice.
        /// </summary>
        Task<CashIntentDto> ConfirmCashIntentAsync(
            Guid transactionId,
            ConfirmCashIntentRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>Marks a pending cash intent Failed (parent never paid / withdrew).</summary>
        Task RejectCashIntentAsync(
            Guid transactionId,
            RejectCashIntentRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pull-based settlement: polls the gateway for each of the invoice's pending checkout
        /// links and settles the invoice if the gateway reports it paid. This is what lets a
        /// completed payment flip the invoice to Paid without waiting on an inbound webhook
        /// (essential on localhost, a safety net in production). Ownership-checked to the parent;
        /// returns the (possibly updated) invoice.
        /// </summary>
        Task<InvoiceDto> ReconcileInvoicePaymentAsync(
            Guid parentUserId,
            Guid invoiceId,
            CancellationToken cancellationToken = default);

        // Renewal tracking workflow: subscriptions drive recurring billing and renew/lapse explicitly
        Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
            SubscriptionStatus? status,
            CancellationToken cancellationToken = default);

        Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

        /// <summary>Renewal conversion: reactivates a lapsed/cancelled subscription and restarts its billing cycle.</summary>
        Task<SubscriptionDto> RenewSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);

        Task<SubscriptionDto> CancelSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);

        // Fee suspension workflow
        Task<IReadOnlyList<FeeSuspensionDto>> ListSuspensionsAsync(SuspensionStatus? status, CancellationToken cancellationToken = default);

        /// <summary>Manual admin restoration; automatic restoration happens on full payment.</summary>
        Task<FeeSuspensionDto> LiftSuspensionAsync(Guid id, CancellationToken cancellationToken = default);

        // Refund request & tracking workflow
        Task<IReadOnlyList<RefundDto>> ListRefundsAsync(CancellationToken cancellationToken = default);

        Task<RefundDto> RequestRefundAsync(RequestRefundRequest request, CancellationToken cancellationToken = default);

        Task<RefundDto> ReviewRefundAsync(Guid id, ReviewRefundRequest request, CancellationToken cancellationToken = default);
    }
}
