using System.Globalization;
using HourReport.Api.Models;
using HourReport.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HourReport.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ReportsController : ControllerBase
{
    private readonly HourReportOptions _opt;
    private readonly ReportStore _store;
    private readonly ExcelService _excel;
    private readonly MailService _mail;
    private readonly ReportSendService _sender;
    private readonly ReportClock _clock;
    private readonly SettingsStore _settings;

    public ReportsController(IOptions<HourReportOptions> options, ReportStore store, ExcelService excel,
        MailService mail, ReportSendService sender, ReportClock clock, SettingsStore settings)
    {
        _settings = settings;
        _opt = options.Value;
        _store = store;
        _excel = excel;
        _mail = mail;
        _sender = sender;
        _clock = clock;
    }

    /// <summary>Thông tin hiển thị trên UI (không trả mật khẩu SMTP).</summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var mail = _settings.Mail;
        return Ok(new
        {
        team = _opt.Team,
        employee = _settings.Employee,
        now = _clock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        slots = _opt.Slots,
        mailTo = mail.To,
        mailCc = mail.Cc,
        testMode = !string.IsNullOrWhiteSpace(mail.PickupFolder),
        mailConfigured = mail.To.Count > 0 && !string.IsNullOrWhiteSpace(mail.FromAddress)
            && (!string.IsNullOrWhiteSpace(mail.Host) || !string.IsNullOrWhiteSpace(mail.PickupFolder)),
        });
    }

    [HttpGet("reports/{date}")]
    public async Task<IActionResult> Get(string date, CancellationToken ct)
    {
        if (!TryDate(date, out var d)) return BadDate();
        return Ok(await _store.GetAsync(d, ct));
    }

    [HttpPut("reports/{date}")]
    public async Task<IActionResult> Save(string date, [FromBody] SaveReportRequest req, CancellationToken ct)
    {
        if (!TryDate(date, out var d)) return BadDate();
        var saved = await _store.UpdateAsync(d, r =>
        {
            r.Rows = req.Rows; // Status giữ nguyên
        }, ct);
        return Ok(saved);
    }

    /// <summary>Đọc file Excel mẫu, trả về các dòng để UI ghép vào bảng (chưa lưu).</summary>
    [HttpPost("import")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public IActionResult Import(IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Chưa chọn file." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Chỉ nhận file .xlsx." });

        try
        {
            using var stream = file.OpenReadStream();
            return Ok(_excel.Parse(stream));
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Không đọc được file Excel: {ex.Message}" });
        }
    }

    /// <summary>Tải file Excel cộng dồn đến giờ {hour} (đúng file sẽ đính kèm khi gửi mail).</summary>
    [HttpGet("reports/{date}/excel/{hour}")]
    public async Task<IActionResult> Download(string date, string hour, CancellationToken ct)
    {
        if (!TryDate(date, out var d)) return BadDate();
        if (!_sender.IsValidSlot(hour)) return BadRequest(new { message = $"Giờ '{hour}' không hợp lệ." });

        var report = await _store.GetAsync(d, ct);
        return File(_excel.Build(report, hour),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _excel.FileName(d, hour));
    }

    [HttpPost("reports/{date}/send/{hour}")]
    public async Task<IActionResult> Send(string date, string hour, CancellationToken ct)
    {
        if (!TryDate(date, out var d)) return BadDate();
        try
        {
            var report = await _sender.SendAsync(d, hour, ct);
            return Ok(new { report, subject = _mail.BuildSubject(d, hour) });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(502, new { message = $"Gửi mail thất bại: {ex.Message}" });
        }
    }

    /// <summary>Gửi tất cả các giờ chưa gửi (đã có nội dung) của 1 ngày.</summary>
    [HttpPost("reports/{date}/send-all")]
    public async Task<IActionResult> SendAll(string date, CancellationToken ct)
    {
        if (!TryDate(date, out var d)) return BadDate();
        return Ok(await _sender.SendAllAsync(d, ct));
    }

    private static bool TryDate(string s, out DateOnly d) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    private IActionResult BadDate() => BadRequest(new { message = "Ngày phải có dạng yyyy-MM-dd." });
}
