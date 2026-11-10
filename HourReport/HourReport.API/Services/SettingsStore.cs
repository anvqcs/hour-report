using System.Text.Json;
using HourReport.Api.Models;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HourReport.Api.Services;

/// <summary>
/// Cài đặt mail + tên nhân viên do người dùng nhập trên giao diện.
/// Giá trị mặc định lấy từ appsettings.json (HourReport / HourReport:Mail), giá trị đã lưu trong
/// Data/settings.json được ưu tiên. Mật khẩu mã hóa bằng Data Protection.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly string[] SecurityValues = Enum.GetNames<SecureSocketOptions>();

    private readonly HourReportOptions _opt;
    private readonly IDataProtector _protector;
    private readonly ILogger<SettingsStore> _logger;
    private readonly string _path;
    private readonly object _lock = new();
    private UserSettings _saved;

    public SettingsStore(IOptions<HourReportOptions> options, IDataProtectionProvider dp,
        IWebHostEnvironment env, ILogger<SettingsStore> logger)
    {
        _opt = options.Value;
        _protector = dp.CreateProtector("HourReport.MailPassword.v1");
        _logger = logger;
        var root = Path.IsPathRooted(_opt.DataFolder) ? _opt.DataFolder : Path.Combine(env.ContentRootPath, _opt.DataFolder);
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "settings.json");
        _saved = Load();
    }

    /// <summary>Tên nhân viên hiệu lực (dùng trong tên file Excel và tiêu đề mail).</summary>
    public string Employee
    {
        get { lock (_lock) return string.IsNullOrWhiteSpace(_saved.Employee) ? _opt.Employee : _saved.Employee; }
    }

    /// <summary>Cấu hình mail hiệu lực, đã giải mã mật khẩu. Trả bản sao, an toàn khi dùng đa luồng.</summary>
    public MailOptions Mail
    {
        get
        {
            UserSettings s;
            lock (_lock) s = _saved;
            return Merge(s, Unprotect(s.PasswordProtected));
        }
    }

    public SettingsView View()
    {
        UserSettings s;
        lock (_lock) s = _saved;
        var m = Merge(s, null);
        return new SettingsView
        {
            Employee = string.IsNullOrWhiteSpace(s.Employee) ? _opt.Employee : s.Employee,
            FromName = m.FromName,
            Host = m.Host,
            Port = m.Port,
            Security = m.Security,
            User = m.User,
            HasPassword = !string.IsNullOrEmpty(s.PasswordProtected) || !string.IsNullOrEmpty(_opt.Mail.Password),
            To = m.To,
            Cc = m.Cc,
        };
    }

    /// <summary>Kiểm tra dữ liệu nhập; trả danh sách lỗi (rỗng = hợp lệ).</summary>
    public static List<string> Validate(SaveSettingsRequest req)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(req.Employee)) errors.Add("Chưa nhập tên nhân viên.");
        else if (req.Employee.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            errors.Add("Tên nhân viên có ký tự không dùng được trong tên file.");
        if (string.IsNullOrWhiteSpace(req.Host)) errors.Add("Chưa nhập máy chủ SMTP.");
        if (req.Port is < 1 or > 65535) errors.Add("Cổng SMTP phải từ 1 đến 65535.");
        if (!SecurityValues.Contains(req.Security, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Kiểu bảo mật không hợp lệ: {req.Security}.");
        if (!string.IsNullOrWhiteSpace(req.User) && !IsEmail(req.User))
            errors.Add($"Email đăng nhập không hợp lệ: {req.User}.");
        if (Clean(req.To).Count == 0) errors.Add("Cần ít nhất 1 email người nhận.");
        foreach (var e in Clean(req.To).Concat(Clean(req.Cc)).Where(e => !IsEmail(e)))
            errors.Add($"Email không hợp lệ: {e}.");
        return errors;
    }

    public SettingsView Save(SaveSettingsRequest req)
    {
        lock (_lock)
        {
            var next = new UserSettings
            {
                Employee = req.Employee.Trim(),
                FromName = req.FromName.Trim(),
                Host = req.Host.Trim(),
                Port = req.Port,
                Security = SecurityValues.First(v => v.Equals(req.Security, StringComparison.OrdinalIgnoreCase)),
                User = req.User.Trim(),
                PasswordProtected = req.ClearPassword ? null
                    : !string.IsNullOrEmpty(req.Password) ? _protector.Protect(req.Password)
                    : _saved.PasswordProtected,
                To = Clean(req.To),
                Cc = Clean(req.Cc),
            };
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(next, JsonOpts));
            File.Move(tmp, _path, overwrite: true);
            _saved = next;
        }
        return View();
    }

    /// <summary>Cấu hình mail dựng từ form (chưa lưu) để thử kết nối. Mật khẩu trống = dùng mật khẩu đã lưu.</summary>
    public MailOptions FromRequest(SaveSettingsRequest req)
    {
        var current = Mail;
        return new MailOptions
        {
            Host = req.Host.Trim(),
            Port = req.Port,
            Security = req.Security,
            User = req.User.Trim(),
            Password = !string.IsNullOrEmpty(req.Password) ? req.Password : current.Password,
            FromName = req.FromName.Trim(),
            FromAddress = req.User.Trim(),
            To = Clean(req.To),
            Cc = Clean(req.Cc),
            SubjectTemplate = current.SubjectTemplate,
            PickupFolder = current.PickupFolder,
        };
    }

    private MailOptions Merge(UserSettings s, string? password)
    {
        var d = _opt.Mail;
        var user = Pick(s.User, d.User);
        return new MailOptions
        {
            Host = Pick(s.Host, d.Host),
            Port = string.IsNullOrWhiteSpace(s.Host) ? d.Port : s.Port,
            Security = string.IsNullOrWhiteSpace(s.Host) ? d.Security : s.Security,
            User = user,
            Password = password ?? d.Password,
            FromName = Pick(s.FromName, d.FromName),
            // Người gửi = email đăng nhập, trừ khi appsettings chỉ định riêng.
            FromAddress = Pick(d.FromAddress, user),
            To = s.To.Count > 0 ? s.To.ToList() : d.To.ToList(),
            Cc = s.To.Count > 0 ? s.Cc.ToList() : d.Cc.ToList(),
            SubjectTemplate = d.SubjectTemplate,
            PickupFolder = d.PickupFolder,
        };
    }

    private UserSettings Load()
    {
        if (!File.Exists(_path)) return new UserSettings();
        try
        {
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), JsonOpts) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không đọc được {Path}, dùng cấu hình mặc định.", _path);
            return new UserSettings();
        }
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return _protector.Unprotect(value); }
        catch (Exception ex)
        {
            // Khóa mã hóa bị mất/đổi máy → phải nhập lại mật khẩu.
            _logger.LogWarning(ex, "Không giải mã được mật khẩu mail, hãy nhập lại trong Cài đặt.");
            return null;
        }
    }

    private static string Pick(string? a, string? b) => !string.IsNullOrWhiteSpace(a) ? a.Trim() : (b ?? "").Trim();

    private static List<string> Clean(IEnumerable<string>? list) =>
        (list ?? Enumerable.Empty<string>())
            .Select(x => x?.Trim() ?? "")
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsEmail(string s) =>
        MailboxAddress.TryParse(s, out var m) && m.Address.Contains('@') && !m.Address.EndsWith('@');
}
