using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Common
{
    public sealed record EmailTemplateSeed(
        string Key,
        string Name,
        string Description,
        NotificationType Category,
        string Subject,
        string HtmlBody,
        IReadOnlyList<string> Placeholders);

    /// <summary>
    /// Canonical Email Template Master catalog — the Subject/HtmlBody every automated
    /// system email starts from. Consumed by DatabaseInitializer (production/dev seeding)
    /// and by tests, so both exercise identical rendered content.
    /// </summary>
    public static class EmailTemplateSeedData
    {
        public static IReadOnlyList<EmailTemplateSeed> All { get; } = Build();

        private static string Wrap(string bodyHtml) => $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:560px;margin:0 auto;">
              <div style="background:#4F46E5;padding:20px 28px;border-radius:8px 8px 0 0;">
                <span style="color:#ffffff;font-size:18px;font-weight:700;">The Reader Nest</span>
              </div>
              <div style="padding:28px;border:1px solid #E5E7EB;border-top:none;border-radius:0 0 8px 8px;color:#1F2937;font-size:14px;line-height:1.6;">
                {bodyHtml}
              </div>
              <p style="color:#9CA3AF;font-size:12px;text-align:center;margin-top:16px;">
                The Reader Nest &middot; Read &middot; Write &middot; Speak
              </p>
            </div>
            """;

        private static IReadOnlyList<EmailTemplateSeed> Build()
        {
            EmailTemplateSeed New(
                string key, string name, string description, NotificationType category,
                string subject, string bodyHtml, params string[] placeholders) =>
                new(key, name, description, category, subject, Wrap(bodyHtml), placeholders);

            return
            [
                New("welcome-credentials", "Welcome & Login Credentials",
                    "Sent when an account is created, and whenever an admin resends login credentials.",
                    NotificationType.General, "Your Reader Nest account is ready",
                    """
                    <p>Hello {{FirstName}},</p>
                    <p>Your Reader Nest account is ready.</p>
                    <table style="width:100%;background:#F9FAFB;border-radius:6px;margin:16px 0;">
                      <tr><td style="padding:12px 16px;color:#6B7280;">Login</td><td style="padding:12px 16px;font-weight:600;">{{Email}}</td></tr>
                      <tr><td style="padding:12px 16px;color:#6B7280;">Temporary password</td><td style="padding:12px 16px;font-weight:600;">{{TemporaryPassword}}</td></tr>
                    </table>
                    <p>Please sign in and change your password.</p>
                    """,
                    "FirstName", "Email", "TemporaryPassword"),

                New("class-scheduled", "Class Scheduled (Teacher)",
                    "Sent to the teacher when a new session is scheduled for them.",
                    NotificationType.BookingConfirmation, "New class scheduled: {{SessionType}}",
                    """
                    <p>Hi {{TeacherFirstName}},</p>
                    <p>A {{SessionType}} session was scheduled for you:</p>
                    <p style="font-weight:600;">{{StartAtLocal}} &ndash; {{EndAtLocal}}</p>
                    """,
                    "TeacherFirstName", "SessionType", "StartAtLocal", "EndAtLocal"),

                New("demo-confirmed", "Demo Class Confirmation",
                    "Sent to the parent and any additional invitees when a demo class is booked.",
                    NotificationType.BookingConfirmation, "Your demo class is confirmed",
                    """
                    <p>Your demo class for <strong>{{ChildName}}</strong> is confirmed for:</p>
                    <p style="font-weight:600;">{{WhenLocal}}</p>
                    <p>A join link follows before the session.</p>
                    """,
                    "ChildName", "WhenLocal"),

                New("session-reminder-teacher", "Class Reminder (Teacher)",
                    "Sent to the teacher one hour before their class starts.",
                    NotificationType.SessionReminder, "Your class starts in 1 hour",
                    """
                    <p>Hi {{TeacherFirstName}},</p>
                    <p>Your {{SessionType}} session starts at <strong>{{StartLocal}}</strong>.</p>
                    """,
                    "TeacherFirstName", "SessionType", "StartLocal"),

                New("session-reminder-parent", "Class Reminder (Parent)",
                    "Sent to the parent one hour before their child's class starts.",
                    NotificationType.SessionReminder, "Class starts in 1 hour",
                    """
                    <p>Your child's class starts at <strong>{{StartLocal}}</strong>.</p>
                    <p>Join from the parent dashboard.</p>
                    """,
                    "StartLocal"),

                New("delayed-session-alert", "Delayed Session Alert (Admin)",
                    "Sent to Admins when a scheduled session's start time passes without it starting.",
                    NotificationType.DelayedSessionAlert, "Session has not started",
                    """
                    <p>The session scheduled at <strong>{{StartLocal}}</strong> (teacher {{TeacherName}}) has not started.</p>
                    """,
                    "StartLocal", "TeacherName"),

                New("teacher-noshow-alert", "Teacher No-Show Alert (Admin)",
                    "Sent to Admins when a teacher no-show is reported for a session.",
                    NotificationType.NoShowAlert, "Teacher no-show reported",
                    """
                    <p>The teacher did not attend the session scheduled at <strong>{{StartAtLocal}}</strong>.</p>
                    <p>A deduction was applied and the session was carried forward.</p>
                    """,
                    "StartAtLocal"),

                New("class-summary", "Class Summary (Parent)",
                    "Sent to the parent after the teacher writes a class summary for a completed session.",
                    NotificationType.PerformanceSummary, "Class summary — {{SessionDate}}",
                    """
                    <p>Today's class notes for <strong>{{ChildName}}</strong>:</p>
                    <p style="background:#F9FAFB;border-radius:6px;padding:16px;">{{Summary}}</p>
                    """,
                    "ChildName", "SessionDate", "Summary"),

                New("payout-statement", "Payout Statement (Finalized)",
                    "Sent to the teacher when their monthly payout is finalized.",
                    NotificationType.PayoutStatement, "Your payout statement for {{Period}}",
                    """
                    <p>Hi {{TeacherFirstName}},</p>
                    <p>Your payout for <strong>{{Period}}</strong> has been finalized.</p>
                    <pre style="background:#F9FAFB;border-radius:6px;padding:16px;white-space:pre-wrap;font-family:inherit;">{{LinesText}}</pre>
                    <p style="font-weight:600;">Total: {{Total}}</p>
                    """,
                    "TeacherFirstName", "Period", "LinesText", "Total"),

                New("salary-slip", "Salary Slip (Paid)",
                    "Sent to the teacher when their finalized payout is marked as paid.",
                    NotificationType.PayoutStatement, "Salary slip — {{Period}} (paid)",
                    """
                    <p>Hi {{TeacherFirstName}},</p>
                    <p>Here is your salary slip for <strong>{{Period}}</strong>:</p>
                    <pre style="background:#F9FAFB;border-radius:6px;padding:16px;white-space:pre-wrap;font-family:inherit;">{{SlipBody}}</pre>
                    <p style="font-weight:600;">Net paid: {{Total}}</p>
                    <p style="color:#6B7280;font-size:12px;">This is a system-generated salary slip. Please contact the admin team for any discrepancy.</p>
                    """,
                    "TeacherFirstName", "Period", "SlipBody", "Total"),

                New("batch-assignment", "Child Assigned to Batch",
                    "Sent to the parent when their child is assigned to a batch.",
                    NotificationType.General, "{{ChildFirstName}} has been assigned to a batch",
                    """
                    <p><strong>{{ChildFullName}}</strong> has been placed in "{{BatchName}}".</p>
                    <p>Their upcoming classes will now appear on your Schedule.</p>
                    """,
                    "ChildFirstName", "ChildFullName", "BatchName"),

                New("leave-status-teacher", "Leave Application Status (Teacher)",
                    "Sent to the teacher when Admin approves or rejects their leave application.",
                    NotificationType.LeaveStatusUpdate, "Leave application {{Status}}",
                    """
                    <p>Hi {{TeacherFirstName}},</p>
                    <p>Your leave for <strong>{{StartAtLocal}} &ndash; {{EndAtLocal}}</strong> was <strong>{{Status}}</strong>.</p>
                    <p>{{ReviewNote}}</p>
                    """,
                    "TeacherFirstName", "StartAtLocal", "EndAtLocal", "Status", "ReviewNote"),

                New("leave-notify-core-team", "Teacher on Leave (Core Team)",
                    "Sent to Admin/Sub Admin when a teacher's leave is approved.",
                    NotificationType.LeaveStatusUpdate, "Teacher on leave: {{TeacherName}}",
                    """
                    <p><strong>{{TeacherName}}</strong> is on approved leave {{Window}}.</p>
                    <p>Their batch classes in this window may need rescheduling or a substitute.</p>
                    """,
                    "TeacherName", "Window"),

                New("leave-notify-parent", "Teacher on Leave (Parent)",
                    "Sent to affected parents when their child's teacher's leave is approved.",
                    NotificationType.LeaveStatusUpdate, "Class update: {{TeacherName}} is on leave",
                    """
                    <p>Your child's teacher <strong>{{TeacherName}}</strong> is on approved leave {{Window}}.</p>
                    <p>Any affected classes will be rescheduled — the new slots will appear on your schedule.</p>
                    """,
                    "TeacherName", "Window"),

                New("leave-submitted-admin-alert", "Leave Application Submitted (Admin)",
                    "Sent to Admins when a teacher submits a new leave application.",
                    NotificationType.General, "Teacher leave application",
                    """
                    <p><strong>{{TeacherName}}</strong> applied for leave <strong>{{StartAtLocal}} &ndash; {{EndAtLocal}}</strong>
                    ({{AffectedSessions}} scheduled session(s) affected).</p>
                    <p>Reason: {{Reason}}</p>
                    """,
                    "TeacherName", "StartAtLocal", "EndAtLocal", "AffectedSessions", "Reason"),

                New("attendance-absent", "Child Marked Absent (Parent)",
                    "Sent to the parent when their child is marked absent for a session.",
                    NotificationType.AttendanceUpdate, "{{ChildFirstName}} was marked absent today",
                    """
                    <p>{{ChildFirstName}} missed today's class.</p>
                    <p>If this was unplanned, please reach out so the session can be carried forward.</p>
                    """,
                    "ChildFirstName"),

                New("invoice-issued", "Invoice Issued (Parent)",
                    "Sent to the parent the moment an invoice is issued (manual or recurring billing).",
                    NotificationType.PaymentReminder, "Invoice {{InvoiceNumber}} — {{Amount}} {{Currency}} due {{DueDate}}",
                    """
                    <p>Hi {{ParentFirstName}},</p>
                    <table style="width:100%;background:#F9FAFB;border-radius:6px;margin:16px 0;">
                      <tr><td style="padding:12px 16px;color:#6B7280;">Invoice no</td><td style="padding:12px 16px;font-weight:600;">{{InvoiceNumber}}</td></tr>
                      <tr><td style="padding:12px 16px;color:#6B7280;">Department</td><td style="padding:12px 16px;font-weight:600;">{{Department}}</td></tr>
                      <tr><td style="padding:12px 16px;color:#6B7280;">Amount due</td><td style="padding:12px 16px;font-weight:600;">{{Amount}} {{Currency}}</td></tr>
                      <tr><td style="padding:12px 16px;color:#6B7280;">Due date</td><td style="padding:12px 16px;font-weight:600;">{{DueDate}}</td></tr>
                    </table>
                    <p>You can pay securely from the parent portal (Payments &amp; Billing &rarr; Pay Now) or download this invoice there.
                    Please ignore this email if you have already paid.</p>
                    """,
                    "ParentFirstName", "InvoiceNumber", "Department", "Amount", "Currency", "DueDate"),

                New("payment-reminder-due", "Payment Reminder (Due Soon)",
                    "Sent to the parent a few days before an invoice's due date.",
                    NotificationType.PaymentReminder, "Payment reminder: invoice {{InvoiceNumber}} due {{DueDate}}",
                    """
                    <p><strong>{{Outstanding}} {{Currency}}</strong> is outstanding on invoice {{InvoiceNumber}} (due {{DueDate}}).</p>
                    <p>Use Pay Now on your dashboard to settle it and keep classes uninterrupted.</p>
                    """,
                    "InvoiceNumber", "DueDate", "Outstanding", "Currency"),

                New("payment-reminder-overdue", "Payment Reminder (Overdue)",
                    "Sent to the parent once an invoice is past its due date.",
                    NotificationType.PaymentReminder, "Overdue: invoice {{InvoiceNumber}}",
                    """
                    <p><strong>{{Outstanding}} {{Currency}}</strong> is outstanding on invoice {{InvoiceNumber}} (was due {{DueDate}}).</p>
                    <p>Use Pay Now on your dashboard to settle it and keep classes uninterrupted.</p>
                    """,
                    "InvoiceNumber", "DueDate", "Outstanding", "Currency"),

                New("payment-received-admin", "Payment Received (Admin Alert)",
                    "Sent to Admins whenever a payment settles against an invoice (manual or gateway).",
                    NotificationType.PaymentReceived, "Payment received",
                    """
                    <p>Payment of <strong>{{Amount}} {{Currency}}</strong> recorded against invoice {{InvoiceNumber}} ({{Status}}).</p>
                    """,
                    "Amount", "Currency", "InvoiceNumber", "Status"),

                New("cash-intent-billing-staff", "Cash Payment Intent (Billing Staff)",
                    "Sent to Admin/Admission Team when a parent chooses to pay in cash.",
                    NotificationType.PaymentReceived, "Cash payment intent",
                    """
                    <p>A parent chose to pay <strong>{{Amount}} {{Currency}}</strong> in cash for invoice {{InvoiceNumber}}.</p>
                    <p>Record the payment once collected.</p>
                    """,
                    "Amount", "Currency", "InvoiceNumber"),

                New("cash-payment-confirmed-parent", "Cash Payment Confirmed (Parent)",
                    "Sent to the parent once billing staff confirm their cash payment.",
                    NotificationType.PaymentReceived, "Cash payment received — invoice {{InvoiceNumber}}",
                    """
                    <p>We have received your cash payment of <strong>{{Amount}} {{Currency}}</strong> for invoice {{InvoiceNumber}}.</p>
                    <p>Receipt no: {{ReceiptNumber}}. Thank you.</p>
                    """,
                    "Amount", "Currency", "InvoiceNumber", "ReceiptNumber"),

                New("weekly-kpi-digest", "Weekly KPI Digest (Admin)",
                    "Sent to Admins every Monday with the week's headline KPI numbers.",
                    NotificationType.General, "Weekly KPI digest",
                    """
                    <p>Weekly Reader Nest KPI digest:</p>
                    <table style="width:100%;background:#F9FAFB;border-radius:6px;margin:16px 0;">
                      <tr><td style="padding:10px 16px;color:#6B7280;">Students</td><td style="padding:10px 16px;font-weight:600;">{{TotalStudents}} total / {{ActiveStudents}} active</td></tr>
                      <tr><td style="padding:10px 16px;color:#6B7280;">Revenue</td><td style="padding:10px 16px;font-weight:600;">{{RevenueCollected}} collected, {{RevenuePending}} pending</td></tr>
                      <tr><td style="padding:10px 16px;color:#6B7280;">Enrollments</td><td style="padding:10px 16px;font-weight:600;">{{TotalEnrollments}}</td></tr>
                      <tr><td style="padding:10px 16px;color:#6B7280;">Batches</td><td style="padding:10px 16px;font-weight:600;">{{ActiveBatches}} active / {{DormantBatches}} dormant ({{OccupancyPercent}}% occupancy)</td></tr>
                      <tr><td style="padding:10px 16px;color:#6B7280;">Conversion / Refund</td><td style="padding:10px 16px;font-weight:600;">{{ConversionRate}}% / {{RefundRate}}%</td></tr>
                      <tr><td style="padding:10px 16px;color:#6B7280;">Teacher utilization</td><td style="padding:10px 16px;font-weight:600;">{{Utilization}} sessions/teacher (30d)</td></tr>
                    </table>
                    """,
                    "TotalStudents", "ActiveStudents", "RevenueCollected", "RevenuePending", "TotalEnrollments",
                    "ActiveBatches", "DormantBatches", "OccupancyPercent", "ConversionRate", "RefundRate", "Utilization"),
            ];
        }
    }
}
