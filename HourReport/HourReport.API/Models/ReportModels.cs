namespace HourReport.Api.Models;

/// <summary>Một dòng trong file Excel mẫu (trừ cột ReportDate, lấy từ DailyReport.Date).</summary>
public sealed class ReportRow
{
    public string ReportTime { get; set; } = "";
    public string StartingTime { get; set; } = "";
    public string JobContent { get; set; } = "";
    /// <summary>"Y" hoặc "N".</summary>
    public string Completed { get; set; } = "N";
    public string CompletingTime { get; set; } = "";
    public string Remark { get; set; } = "";

    public bool HasContent => !string.IsNullOrWhiteSpace(JobContent);
}

public sealed class SlotStatus
{
    public DateTime? SentAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Toàn bộ dữ liệu báo cáo của 1 ngày — lưu thành 1 file JSON.</summary>
public sealed class DailyReport
{
    /// <summary>yyyy-MM-dd</summary>
    public string Date { get; set; } = "";
    public List<ReportRow> Rows { get; set; } = new();
    /// <summary>Key = ReportTime ("09").</summary>
    public Dictionary<string, SlotStatus> Status { get; set; } = new();
}

public sealed class SaveReportRequest
{
    public List<ReportRow> Rows { get; set; } = new();
}

public sealed record SlotMessage(string ReportTime, string Message);

public sealed class SendAllResult
{
    public DailyReport Report { get; set; } = new();
    /// <summary>Các giờ gửi thành công, theo thứ tự.</summary>
    public List<string> Sent { get; set; } = new();
    public List<SlotMessage> Skipped { get; set; } = new();
    /// <summary>Giờ gửi lỗi (dừng tại đây, các giờ sau chưa gửi).</summary>
    public SlotMessage? Failed { get; set; }
}

public sealed class ImportResult
{
    /// <summary>Ngày đọc được trong cột ReportDate (nếu có).</summary>
    public string? Date { get; set; }
    public List<ReportRow> Rows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
