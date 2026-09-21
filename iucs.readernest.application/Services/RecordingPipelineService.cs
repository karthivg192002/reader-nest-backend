using System.Globalization;
using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;

namespace iucs.readernest.application.Services
{
    public class RecordingPipelineService : IRecordingPipelineService
    {
        // Fixed path on the Jitsi/Video server (see finalize-recording.sh); never user input.
        private const string Dir = "/root/.jitsi-meet-cfg/storage/jibri/recordings";
        private readonly MonitoringOptions _options;

        public RecordingPipelineService(IOptions<MonitoringOptions> options)
        {
            _options = options.Value;
        }

        public async Task<RecordingPipelineDto?> GetAsync(CancellationToken cancellationToken = default)
        {
            var main = BurstWorkerSsh.FindMain(_options);
            if (main is null)
            {
                return null;
            }

            string raw;
            try
            {
                raw = await BurstWorkerSsh.RunAsync(
                    main,
                    "tail -n 600 " + Dir + "/finalize-recording.log 2>/dev/null; " +
                    "echo @@PENDING@@; find " + Dir + " -maxdepth 2 -name .pending-registration 2>/dev/null | wc -l; " +
                    "echo @@NEWEST@@; find " + Dir + " -maxdepth 2 -name '*.mp4' -mmin -2880 -printf '%T@\\n' 2>/dev/null | sort -n | tail -n 1; " +
                    "echo @@DISK@@; df -B1 --output=size,avail " + Dir + " 2>/dev/null | tail -n 1; " +
                    "echo @@GROWTH@@; find " + Dir + " -maxdepth 2 -name '*.mp4' -mmin -1440 -printf '%s\\n' 2>/dev/null | awk '{s+=$1} END{print s+0}'",
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Runs inside the dashboard's single /summary call: never let it fail the whole page.
                return null;
            }

            var log = Section(raw, null, "@@PENDING@@");
            var pending = Section(raw, "@@PENDING@@", "@@NEWEST@@").Trim();
            var newest = Section(raw, "@@NEWEST@@", "@@DISK@@").Trim();
            var disk = Section(raw, "@@DISK@@", "@@GROWTH@@").Trim();
            var growth = Section(raw, "@@GROWTH@@", null).Trim();

            int.TryParse(pending, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pendingCount);
            long? newestEpoch = double.TryParse(newest, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (long)n : null;
            long.TryParse(growth, NumberStyles.Integer, CultureInfo.InvariantCulture, out var growthBytes);

            return RecordingPipelineParser.Parse(log, pendingCount, newestEpoch, disk, growthBytes, DateTime.UtcNow);
        }

        private static string Section(string raw, string? start, string? end)
        {
            var from = 0;
            if (start is not null)
            {
                from = raw.IndexOf(start, StringComparison.Ordinal);
                if (from < 0) return "";
                from += start.Length;
            }

            var to = end is null ? -1 : raw.IndexOf(end, from, StringComparison.Ordinal);
            return to < 0 ? raw[from..] : raw[from..to];
        }
    }
}
