using System.Globalization;
using ClosedXML.Excel;
using HourReport.Api.Models;
using Microsoft.Extensions.Options;

namespace HourReport.Api.Services;

/// <summary>
/// Sinh và đọc file Excel theo đúng mẫu Hour_Report_{Team}_{Employee}_{yyyyMMdd}_{HH}H.xlsx:
/// 1 sheet "Sheet 1", 7 cột, mọi giá trị lưu dạng Text (giống file mẫu).
/// </summary>
public sealed class ExcelService
{
    public static readonly string[] Headers =
    {
        "ReportDate", "Report time", "Starting time", "Job Content",
        "Completed Y/N", "Completing time", "Remark"
    };

    // Độ rộng cột lấy đúng từ file mẫu (ClosedXML chỉ còn dùng để đọc file nhập).
    private static readonly double[] Widths = { 12.5, 12.5, 12.109375, 101.5625, 15.625, 15.625, 58.59375 };

    private readonly HourReportOptions _opt;
    private readonly SettingsStore _settings;

    public ExcelService(IOptions<HourReportOptions> options, SettingsStore settings)
    {
        _opt = options.Value;
        _settings = settings;
    }

    public string FileName(DateOnly date, string reportTime) =>
        $"Hour_Report_{_opt.Team}_{_settings.Employee}_{date:yyyyMMdd}_{reportTime}H.xlsx";

    /// <summary>
    /// Các dòng có trong báo cáo của giờ <paramref name="reportTime"/>: báo cáo cộng dồn,
    /// file 09H chỉ có dòng 09, file 17H có đủ 8 dòng.
    /// </summary>
    public List<ReportRow> RowsUpTo(DailyReport report, string reportTime)
    {
        var hour = int.Parse(reportTime);
        return report.Rows.Where(r => int.Parse(r.ReportTime) <= hour).ToList();
    }

    /// <summary>
    /// File Excel cộng dồn đến giờ <paramref name="reportTime"/>, cấu trúc + định dạng giống file mẫu
    /// (ghi bằng <see cref="XlsxTemplateWriter"/> để GroupWare xem trước được).
    /// </summary>
    public byte[] Build(DailyReport report, string reportTime)
    {
        const int C = XlsxTemplateWriter.StyleCenter, L = XlsxTemplateWriter.StyleLeft;
        var rows = new List<IReadOnlyList<(string, int)>>
        {
            Headers.Select(h => (h, C)).ToList(),
        };
        foreach (var row in RowsUpTo(report, reportTime))
        {
            // Tiêu đề + ReportDate / Report time / Starting time / Completed: căn giữa; còn lại căn trái.
            rows.Add(new List<(string, int)>
            {
                (report.Date, C), (row.ReportTime, C), (row.StartingTime, C), (row.JobContent, L),
                (row.Completed, C), (row.CompletingTime, L), (row.Remark, L),
            });
        }
        return XlsxTemplateWriter.Write("Sheet 1", Widths, rows, DateTime.UtcNow);
    }

    /// <summary>
    /// Đọc file Excel người dùng nhập. Tìm dòng tiêu đề theo tên cột (không phụ thuộc vị trí),
    /// chấp nhận ô kiểu Text, Number, Date/Time.
    /// </summary>
    public ImportResult Parse(Stream stream)
    {
        var result = new ImportResult();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // 1. Tìm dòng tiêu đề (trong 10 dòng đầu) chứa "Report time".
        int headerRow = 0;
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r <= Math.Min(lastRow, 10) && headerRow == 0; r++)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c <= lastCol; c++)
            {
                var text = ws.Cell(r, c).GetString().Trim();
                if (text.Length > 0) map.TryAdd(Key(text), c);
            }
            if (map.ContainsKey(Key("Report time")))
            {
                headerRow = r;
                colMap = map;
            }
        }

        if (headerRow == 0)
        {
            result.Warnings.Add("Không tìm thấy dòng tiêu đề có cột 'Report time'. Hãy dùng đúng file mẫu.");
            return result;
        }

        int Col(string header) => colMap.TryGetValue(Key(header), out var c) ? c : 0;
        var cDate = Col("ReportDate");
        var cTime = Col("Report time");
        var cStart = Col("Starting time");
        var cJob = Col("Job Content");
        var cDone = Col("Completed Y/N");
        var cDoneTime = Col("Completing time");
        var cRemark = Col("Remark");

        var validTimes = _opt.Slots.Select(s => s.ReportTime).ToHashSet();
        var dates = new HashSet<string>();

        // 2. Đọc từng dòng dữ liệu.
        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            var rawTime = ReadText(ws, r, cTime);
            if (rawTime.Length == 0) continue;

            var digits = new string(rawTime.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, out var hour))
            {
                result.Warnings.Add($"Dòng {r}: Report time '{rawTime}' không hợp lệ, bỏ qua.");
                continue;
            }
            var reportTime = hour.ToString("00");
            if (!validTimes.Contains(reportTime))
            {
                result.Warnings.Add($"Dòng {r}: giờ {reportTime} không nằm trong khung giờ báo cáo, bỏ qua.");
                continue;
            }

            var date = ReadDate(ws, r, cDate);
            if (date.Length > 0) dates.Add(date);

            var done = ReadText(ws, r, cDone).ToUpperInvariant();
            result.Rows.Add(new ReportRow
            {
                ReportTime = reportTime,
                StartingTime = ReadTime(ws, r, cStart),
                JobContent = ReadText(ws, r, cJob),
                Completed = done.StartsWith("Y") ? "Y" : "N",
                CompletingTime = ReadTime(ws, r, cDoneTime),
                Remark = ReadText(ws, r, cRemark),
            });
        }

        // Trùng giờ → lấy dòng cuối cùng.
        result.Rows = result.Rows.GroupBy(x => x.ReportTime).Select(g => g.Last())
            .OrderBy(x => x.ReportTime).ToList();

        if (dates.Count == 1) result.Date = dates.First();
        else if (dates.Count > 1)
            result.Warnings.Add($"File có nhiều ngày khác nhau: {string.Join(", ", dates)}.");

        if (result.Rows.Count == 0)
            result.Warnings.Add("File không có dòng dữ liệu hợp lệ nào.");

        return result;
    }

    private static string Key(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string ReadText(IXLWorksheet ws, int r, int c)
    {
        if (c == 0) return "";
        var v = ws.Cell(r, c).Value;
        if (v.IsBlank) return "";
        if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        return ws.Cell(r, c).GetString().Trim();
    }

    private static string ReadDate(IXLWorksheet ws, int r, int c)
    {
        if (c == 0) return "";
        var v = ws.Cell(r, c).Value;
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd");
        if (v.IsNumber) return DateTime.FromOADate(v.GetNumber()).ToString("yyyy-MM-dd");
        var text = ws.Cell(r, c).GetString().Trim();
        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "yyyyMMdd" };
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd")
            : text;
    }

    private static string ReadTime(IXLWorksheet ws, int r, int c)
    {
        if (c == 0) return "";
        var v = ws.Cell(r, c).Value;
        if (v.IsTimeSpan) return Hm(v.GetTimeSpan());
        if (v.IsDateTime) return v.GetDateTime().ToString("HH:mm");
        if (v.IsNumber)
        {
            var n = v.GetNumber();
            // Excel lưu giờ là phần lẻ của ngày (0.375 = 09:00); làm tròn theo phút để tránh 08:59.
            return n < 1 ? Hm(TimeSpan.FromMinutes(Math.Round(n * 1440))) : n.ToString(CultureInfo.InvariantCulture);
        }
        var text = ws.Cell(r, c).GetString().Trim();
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var t) && text.Contains(':')
            ? Hm(t)
            : text;
    }

    private static string Hm(TimeSpan t) => $"{(int)t.TotalHours % 24:00}:{t.Minutes:00}";
}
