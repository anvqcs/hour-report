using HourReport.Api.Models;
using Microsoft.Extensions.Options;

namespace HourReport.Api.Services;

/// <summary>Sinh file Excel cộng dồn của 1 giờ, gửi mail và ghi lại trạng thái.</summary>
public sealed class ReportSendService
{
    private readonly HourReportOptions _opt;
    private readonly ReportStore _store;
    private readonly ExcelService _excel;
    private readonly MailService _mail;
    private readonly ReportClock _clock;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ReportSendService(IOptions<HourReportOptions> options, ReportStore store, ExcelService excel,
        MailService mail, ReportClock clock)
    {
        _opt = options.Value;
        _store = store;
        _excel = excel;
        _mail = mail;
        _clock = clock;
    }

    public bool IsValidSlot(string reportTime) => _opt.Slots.Any(s => s.ReportTime == reportTime);

    public async Task<DailyReport> SendAsync(DateOnly date, string reportTime, CancellationToken ct)
    {
        if (!IsValidSlot(reportTime))
            throw new ArgumentException($"Giờ báo cáo '{reportTime}' không hợp lệ.");

        // Không cho 2 lần gửi chạy song song (vd bấm Gửi 2 lần liên tiếp).
        await _sendLock.WaitAsync(ct);
        try
        {
            var report = await _store.GetAsync(date, ct);
            var row = report.Rows.First(r => r.ReportTime == reportTime);
            if (!row.HasContent)
                throw new InvalidOperationException($"Giờ {reportTime} chưa có Job Content.");

            try
            {
                var bytes = _excel.Build(report, reportTime);
                await _mail.SendReportAsync(date, reportTime, _excel.FileName(date, reportTime), bytes, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _store.UpdateAsync(date, r => Mark(r, reportTime, s =>
                {
                    s.LastAttemptAt = _clock.Now;
                    s.LastError = ex.Message;
                }), ct);
                throw;
            }

            return await _store.UpdateAsync(date, r => Mark(r, reportTime, s =>
            {
                s.LastAttemptAt = _clock.Now;
                s.SentAt = _clock.Now;
                s.LastError = null;
            }), ct);
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>
    /// Gửi lần lượt (09 → 17) mọi giờ chưa gửi và đã có Job Content của 1 ngày; mỗi giờ 1 mail
    /// kèm file cộng dồn như khi bấm Gửi từng dòng. Dừng ở lỗi đầu tiên để không gửi sai thứ tự.
    /// </summary>
    public async Task<SendAllResult> SendAllAsync(DateOnly date, CancellationToken ct)
    {
        var result = new SendAllResult();
        var start = await _store.GetAsync(date, ct);
        foreach (var row in start.Rows.ToList())
        {
            if (start.Status.TryGetValue(row.ReportTime, out var st) && st.SentAt != null)
            {
                result.Skipped.Add(new SlotMessage(row.ReportTime, "đã gửi trước đó"));
                continue;
            }
            if (!row.HasContent)
            {
                result.Skipped.Add(new SlotMessage(row.ReportTime, "chưa có Job Content"));
                continue;
            }
            try
            {
                await SendAsync(date, row.ReportTime, ct);
                result.Sent.Add(row.ReportTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Failed = new SlotMessage(row.ReportTime, ex.Message);
                break;
            }
        }
        result.Report = await _store.GetAsync(date, ct);
        return result;
    }

    private static void Mark(DailyReport report, string reportTime, Action<SlotStatus> change)
    {
        if (!report.Status.TryGetValue(reportTime, out var s))
            report.Status[reportTime] = s = new SlotStatus();
        change(s);
    }
}
