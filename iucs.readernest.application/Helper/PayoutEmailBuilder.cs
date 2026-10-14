using System.Globalization;
using System.Net;
using System.Text;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Helper
{
    /// <summary>
    /// The breakdown block shared by the payout-statement and salary-slip emails: a summary,
    /// then the session, addition and deduction lines as plain two/three-column tables. Inline
    /// styles and table layout only, so it reads the same in Gmail/Outlook on a phone or a
    /// desktop. Every value that came from a person (notes, batch names) is HTML-encoded here,
    /// because the template inserts this block unescaped (see EmailTemplateService's "Html" tokens).
    /// </summary>
    public static class PayoutEmailBuilder
    {
        private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

        private const string CellStyle = "padding:8px 10px;border-bottom:1px solid #E5E7EB;vertical-align:top;";
        private const string HeadStyle = "padding:8px 10px;border-bottom:2px solid #E5E7EB;text-align:left;font-size:12px;color:#6B7280;";

        public static string Money(decimal amount) =>
            (amount < 0 ? "-" : string.Empty) + "₹" + Math.Abs(amount).ToString("N2", Inr);

        public static string PeriodLabel(int year, int month) =>
            new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        public static string BuildBreakdownHtml(IReadOnlyCollection<PayoutItem> items, decimal total, string? timeZoneId)
        {
            var ordered = items.OrderBy(i => i.ClassSession?.ScheduledStartAtUtc ?? i.CreatedAtUtc).ToList();
            var sessions = ordered.Where(i => i.Type == PayoutItemType.SessionEarning).ToList();
            var additions = ordered.Where(i => i.Type != PayoutItemType.SessionEarning && i.Amount > 0m).ToList();
            var deductions = ordered.Where(i => i.Type != PayoutItemType.SessionEarning && i.Amount < 0m).ToList();

            var html = new StringBuilder();

            html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:16px 0;font-size:14px;\">");
            SummaryRow(html, "Sessions conducted", sessions.Count.ToString(Inr));
            SummaryRow(html, "Session earnings", Money(sessions.Sum(i => i.Amount)));
            SummaryRow(html, "Additions", Money(additions.Sum(i => i.Amount)));
            SummaryRow(html, "Deductions", Money(deductions.Sum(i => i.Amount)));
            html.Append("<tr><td style=\"padding:12px 10px;background:#EEF2FF;font-weight:700;\">Net payable</td>")
                .Append("<td style=\"padding:12px 10px;background:#EEF2FF;font-weight:700;font-size:18px;text-align:right;white-space:nowrap;\">")
                .Append(Money(total)).Append("</td></tr></table>");

            Section(html, "Session details", sessions, timeZoneId);
            Section(html, "Additions", additions, timeZoneId);
            Section(html, "Deductions", deductions, timeZoneId);

            return html.ToString();
        }

        private static void SummaryRow(StringBuilder html, string label, string value)
        {
            html.Append("<tr><td style=\"").Append(CellStyle).Append("\">").Append(label).Append("</td>")
                .Append("<td style=\"").Append(CellStyle).Append("text-align:right;white-space:nowrap;\">").Append(value).Append("</td></tr>");
        }

        private static void Section(StringBuilder html, string title, List<PayoutItem> lines, string? timeZoneId)
        {
            if (lines.Count == 0)
            {
                return;
            }

            html.Append("<p style=\"margin:24px 0 6px;font-weight:700;\">").Append(title).Append("</p>")
                .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px;\">")
                .Append("<tr><th style=\"").Append(HeadStyle).Append("\">Details</th>")
                .Append("<th style=\"").Append(HeadStyle).Append("text-align:right;\">Amount</th></tr>");

            foreach (var line in lines)
            {
                html.Append("<tr><td style=\"").Append(CellStyle).Append("\">")
                    .Append(WebUtility.HtmlEncode(Describe(line, timeZoneId)));
                if (!string.IsNullOrWhiteSpace(line.Note))
                {
                    html.Append("<br/><span style=\"color:#6B7280;font-size:12px;\">")
                        .Append(WebUtility.HtmlEncode(line.Note)).Append("</span>");
                }

                html.Append("</td><td style=\"").Append(CellStyle).Append("text-align:right;white-space:nowrap;\">")
                    .Append(Money(line.Amount)).Append("</td></tr>");
            }

            html.Append("</table>");
        }

        private static string Describe(PayoutItem line, string? timeZoneId)
        {
            var kind = line.Type switch
            {
                PayoutItemType.SessionEarning => "Class",
                PayoutItemType.StudentNoShowWaiting => "Student no-show waiting amount",
                PayoutItemType.TeacherNoShowDeduction => "Teacher no-show deduction",
                PayoutItemType.Penalty => "Deduction",
                PayoutItemType.Bonus => "Addition",
                _ => "Adjustment",
            };
            if (line.ClassSession is not { } session)
            {
                return kind;
            }

            var when = LocalTime(session.ScheduledStartAtUtc, timeZoneId).ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);
            var name = session.Batch?.Name;
            return line.Type == PayoutItemType.SessionEarning
                ? (string.IsNullOrEmpty(name) ? when : $"{when} · {name}")
                : (string.IsNullOrEmpty(name) ? $"{kind} · {when}" : $"{kind} · {when} · {name}");
        }

        private static DateTime LocalTime(DateTime utc, string? timeZoneId)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId ?? DateTimeDisplay.DefaultTimeZoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            }
            catch (TimeZoneNotFoundException)
            {
                return utc;
            }
        }
    }
}
