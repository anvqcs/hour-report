namespace HourReport.Api.Models;

/// <summary>Cấu hình đọc từ section "HourReport" trong appsettings.json.</summary>
public sealed class HourReportOptions
{
    public string Team { get; set; } = "DevHCM";
    public string Employee { get; set; } = "";
    public string TimeZoneId { get; set; } = "SE Asia Standard Time";
    public string DataFolder { get; set; } = "Data";
    public List<SlotOptions> Slots { get; set; } = new();
    public MailOptions Mail { get; set; } = new();
}

public sealed class SlotOptions
{
    /// <summary>Giờ báo cáo, 2 chữ số: "09".."17".</summary>
    public string ReportTime { get; set; } = "";
    /// <summary>Giờ bắt đầu công việc của khung giờ: "08:00".</summary>
    public string StartingTime { get; set; } = "";

    public int Hour => int.Parse(ReportTime);
}

public sealed class MailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    /// <summary>None | Auto | SslOnConnect | StartTls | StartTlsWhenAvailable</summary>
    public string Security { get; set; } = "StartTls";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    /// <summary>Token: {Team} {Employee} {Date} (yyyyMMdd) {Hour} (09..17)</summary>
    public string SubjectTemplate { get; set; } = "[Hour Report] {Team}_{Employee}_{Date}_{Hour}H";
    /// <summary>Nếu có giá trị: không gửi SMTP mà ghi file .eml vào thư mục này (dùng khi test).</summary>
    public string PickupFolder { get; set; } = "";
}
