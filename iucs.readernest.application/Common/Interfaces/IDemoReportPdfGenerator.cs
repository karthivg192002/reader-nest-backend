using iucs.readernest.application.Dto.Admission;

namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>Renders the month-end demo report as a PDF (QuestPDF, in the API layer — see DemoReportPdfGenerator).</summary>
    public interface IDemoReportPdfGenerator
    {
        byte[] Generate(DemoMonthlyReportDto report);
    }
}
