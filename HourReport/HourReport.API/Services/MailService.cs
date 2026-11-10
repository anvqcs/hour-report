using System.Text;
using HourReport.Api.Models;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HourReport.Api.Services;

public sealed class MailService
{
    private readonly HourReportOptions _opt;
    private readonly SettingsStore _settings;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MailService> _logger;

    public MailService(IOptions<HourReportOptions> options, SettingsStore settings, IWebHostEnvironment env,
        ILogger<MailService> logger)
    {
        _opt = options.Value;
        _settings = settings;
        _env = env;
        _logger = logger;
    }

    public string BuildSubject(DateOnly date, string reportTime) =>
        _settings.Mail.SubjectTemplate
            .Replace("{Team}", _opt.Team)
            .Replace("{Employee}", _settings.Employee)
            .Replace("{Date}", date.ToString("yyyyMMdd"))
            .Replace("{Hour}", reportTime);

    public async Task SendReportAsync(DateOnly date, string reportTime, string fileName, byte[] attachment,
        CancellationToken ct)
    {
        var m = _settings.Mail; // đọc 1 lần, tránh cài đặt đổi giữa chừng
        if (m.To.Count == 0) throw new InvalidOperationException("Chưa cấu hình người nhận. Mở Cài đặt mail để nhập.");
        if (string.IsNullOrWhiteSpace(m.FromAddress))
            throw new InvalidOperationException("Chưa cấu hình email gửi. Mở Cài đặt mail để nhập.");

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(m.FromName, m.FromAddress));
        foreach (var to in m.To) msg.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in m.Cc) msg.Cc.Add(MailboxAddress.Parse(cc));
        msg.Subject = BuildSubject(date, reportTime);

        // Nội dung mail = tên file đính kèm (không có đuôi .xlsx), vd Hour_Report_DevHCM_VoQuocAn_20261030_17H.
        var body = new BodyBuilder { TextBody = Path.GetFileNameWithoutExtension(fileName) };
        body.Attachments.Add(fileName, attachment,
            new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        msg.Body = body.ToMessageBody();

        // Chế độ test: ghi .eml ra thư mục, không gửi thật.
        if (!string.IsNullOrWhiteSpace(m.PickupFolder))
        {
            var folder = Path.IsPathRooted(m.PickupFolder) ? m.PickupFolder : Path.Combine(_env.ContentRootPath, m.PickupFolder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:HHmmss}.eml");
            await msg.WriteToAsync(path, ct);
            _logger.LogInformation("PickupFolder: đã ghi {Path}", path);
            return;
        }

        if (string.IsNullOrWhiteSpace(m.Host))
            throw new InvalidOperationException("Chưa cấu hình máy chủ SMTP. Mở Cài đặt mail để nhập.");

        using var client = await ConnectAsync(m, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("Đã gửi mail {Subject} tới {To}", msg.Subject, string.Join(", ", m.To));
    }

    /// <summary>Kết nối + đăng nhập SMTP để kiểm tra cài đặt, không gửi mail.</summary>
    public async Task TestConnectionAsync(MailOptions m, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.Host)) throw new InvalidOperationException("Chưa nhập máy chủ SMTP.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var client = await ConnectAsync(m, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }

    private static async Task<SmtpClient> ConnectAsync(MailOptions m, CancellationToken ct)
    {
        // Ghi lại hội thoại SMTP (mật khẩu bị che) để báo lỗi rõ ràng khi máy chủ trả về dữ liệu lạ.
        var log = new MemoryStream();
        var client = new SmtpClient(new ProtocolLogger(log, leaveOpen: true) { RedactSecrets = true }) { Timeout = 30_000 };
        var security = Enum.TryParse<SecureSocketOptions>(m.Security, true, out var s) ? s : SecureSocketOptions.Auto;
        try
        {
            await client.ConnectAsync(m.Host, m.Port, security, ct);
            // Máy chủ Hanbiro (GroupWare) quảng cáo CRAM-MD5 nhưng xử lý sai: trả "Auth Fail" không kèm mã lỗi.
            // Kết nối đã mã hóa SSL/TLS nên dùng PLAIN/LOGIN là an toàn; bỏ các cơ chế challenge-response.
            if (client.IsSecure)
            {
                client.AuthenticationMechanisms.Remove("CRAM-MD5");
                client.AuthenticationMechanisms.Remove("DIGEST-MD5");
            }
            if (!string.IsNullOrEmpty(m.User))
            {
                if (string.IsNullOrEmpty(m.Password))
                    throw new InvalidOperationException("Chưa nhập mật khẩu email.");
                await client.AuthenticateAsync(m.User, m.Password, ct);
            }
            return client;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not InvalidOperationException)
        {
            client.Dispose();
            throw new InvalidOperationException(Explain(ex, m, security, Tail(log)), ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Thông báo lỗi kết nối kèm gợi ý và phần cuối hội thoại với máy chủ.</summary>
    private static string Explain(Exception ex, MailOptions m, SecureSocketOptions security, string trace)
    {
        var hint = "";
        var greeting = trace.Split('\n').FirstOrDefault(l => l.StartsWith("S: "))?[3..] ?? "";
        if (greeting.StartsWith("* OK") || greeting.StartsWith("+OK"))
            hint = $" Cổng {m.Port} là cổng nhận mail (IMAP/POP3), không phải SMTP. GroupWare dùng SMTP cổng 465.";
        else if (greeting.StartsWith("HTTP/") || greeting.StartsWith("<"))
            hint = " Máy chủ trả về trang web thay vì SMTP: kiểm tra lại tên máy chủ, cổng, hoặc proxy/phần mềm diệt virus đang chặn cổng mail.";
        else if (ex is SslHandshakeException)
            hint = security == SecureSocketOptions.SslOnConnect
                ? " Lỗi bắt tay SSL: thử Bảo mật = STARTTLS với cổng 587."
                : " Lỗi bắt tay SSL: thử Bảo mật = SSL/TLS với cổng 465.";
        else if (ex is AuthenticationException || trace.Contains("Auth Fail", StringComparison.OrdinalIgnoreCase))
            hint = " Sai email hoặc mật khẩu, hoặc SMTP/IMAP đang bị tắt trong GroupWare (Mail › Settings › SMTP-POP3-IMAP).";
        else if (ex is SmtpProtocolException && security != SecureSocketOptions.SslOnConnect && m.Port == 465)
            hint = " Cổng 465 cần Bảo mật = SSL/TLS.";

        var detail = string.IsNullOrWhiteSpace(trace) ? "" : $" | Hội thoại với máy chủ: {trace}";
        return $"{ex.Message} ({m.Host}:{m.Port}, {security}).{hint}{detail}";
    }

    private static string Tail(MemoryStream log, int max = 600)
    {
        var text = Encoding.UTF8.GetString(log.ToArray()).Replace("\r", "").Trim();
        return text.Length <= max ? text : "…" + text[^max..];
    }
}
