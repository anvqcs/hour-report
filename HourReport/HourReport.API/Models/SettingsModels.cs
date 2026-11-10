namespace HourReport.Api.Models;

/// <summary>
/// Cài đặt người dùng nhập trên giao diện, lưu ở Data/settings.json.
/// Mật khẩu được mã hóa (ASP.NET Data Protection), không bao giờ trả ngược về giao diện.
/// </summary>
public sealed class UserSettings
{
    public string Employee { get; set; } = "";
    public string FromName { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    /// <summary>Email đăng nhập SMTP, đồng thời là địa chỉ người gửi.</summary>
    public string User { get; set; } = "";
    /// <summary>Mật khẩu đã mã hóa.</summary>
    public string? PasswordProtected { get; set; }
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
}

/// <summary>Dữ liệu trả về cho giao diện (không có mật khẩu).</summary>
public sealed class SettingsView
{
    public string Employee { get; set; } = "";
    public string FromName { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Security { get; set; } = "";
    public string User { get; set; } = "";
    public bool HasPassword { get; set; }
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
}

public sealed class SaveSettingsRequest
{
    public string Employee { get; set; } = "";
    public string FromName { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string User { get; set; } = "";
    /// <summary>Để trống = giữ mật khẩu đang lưu.</summary>
    public string? Password { get; set; }
    public bool ClearPassword { get; set; }
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
}
