using System.Text.Json;
using HourReport.Api.Models;
using Microsoft.Extensions.Options;

namespace HourReport.Api.Services;

/// <summary>
/// Lưu báo cáo mỗi ngày thành 1 file JSON: Data/reports/yyyyMMdd.json.
/// Dữ liệu nhỏ, 1 người dùng → không cần database.
/// </summary>
public sealed class ReportStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HourReportOptions _opt;
    private readonly string _folder;

    public ReportStore(IOptions<HourReportOptions> options, IWebHostEnvironment env)
    {
        _opt = options.Value;
        var root = Path.IsPathRooted(_opt.DataFolder)
            ? _opt.DataFolder
            : Path.Combine(env.ContentRootPath, _opt.DataFolder);
        _folder = Path.Combine(root, "reports");
        Directory.CreateDirectory(_folder);
    }

    public async Task<DailyReport> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await ReadUnlockedAsync(date, ct); }
        finally { _lock.Release(); }
    }

    /// <summary>Đọc - sửa - ghi trong cùng 1 lock (tránh đè dữ liệu khi lưu và gửi cùng lúc).</summary>
    public async Task<DailyReport> UpdateAsync(DateOnly date, Action<DailyReport> mutate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var report = await ReadUnlockedAsync(date, ct);
            mutate(report);
            Normalize(report);
            var path = PathOf(date);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(report, JsonOpts), ct);
            File.Move(tmp, path, overwrite: true);
            return report;
        }
        finally { _lock.Release(); }
    }

    private async Task<DailyReport> ReadUnlockedAsync(DateOnly date, CancellationToken ct)
    {
        var path = PathOf(date);
        DailyReport report;
        if (File.Exists(path))
        {
            await using var fs = File.OpenRead(path);
            report = await JsonSerializer.DeserializeAsync<DailyReport>(fs, JsonOpts, ct) ?? new DailyReport();
        }
        else
        {
            report = new DailyReport();
        }
        report.Date = date.ToString("yyyy-MM-dd");
        Normalize(report);
        return report;
    }

    /// <summary>Đảm bảo luôn đủ 8 dòng theo đúng thứ tự Slots trong cấu hình.</summary>
    private void Normalize(DailyReport report)
    {
        var byTime = report.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ReportTime))
            .GroupBy(r => r.ReportTime)
            .ToDictionary(g => g.Key, g => g.Last());

        report.Rows = _opt.Slots.Select(s =>
        {
            var row = byTime.TryGetValue(s.ReportTime, out var r) ? r : new ReportRow();
            row.ReportTime = s.ReportTime;
            if (string.IsNullOrWhiteSpace(row.StartingTime)) row.StartingTime = s.StartingTime;
            row.Completed = string.Equals(row.Completed?.Trim(), "Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "N";
            row.JobContent ??= "";
            row.CompletingTime ??= "";
            row.Remark ??= "";
            return row;
        }).ToList();
    }

    private string PathOf(DateOnly date) => Path.Combine(_folder, $"{date:yyyyMMdd}.json");
}
