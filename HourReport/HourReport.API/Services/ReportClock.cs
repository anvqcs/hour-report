using HourReport.Api.Models;
using Microsoft.Extensions.Options;

namespace HourReport.Api.Services;

/// <summary>Giờ hiện tại theo múi giờ cấu hình (mặc định GMT+7), không phụ thuộc giờ của server.</summary>
public sealed class ReportClock
{
    private readonly TimeZoneInfo _tz;

    public ReportClock(IOptions<HourReportOptions> options, ILogger<ReportClock> logger)
    {
        _tz = Resolve(options.Value.TimeZoneId, logger);
    }

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
    public DateOnly Today => DateOnly.FromDateTime(Now);

    private static TimeZoneInfo Resolve(string id, ILogger logger)
    {
        // Windows dùng "SE Asia Standard Time", Linux dùng "Asia/Ho_Chi_Minh" — thử cả hai.
        foreach (var candidate in new[] { id, "SE Asia Standard Time", "Asia/Ho_Chi_Minh" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        logger.LogWarning("Không tìm thấy time zone '{Id}', dùng giờ local của server.", id);
        return TimeZoneInfo.Local;
    }
}
