import { useEffect, useRef, useState } from "react";
import { api } from "../api";

const PRESETS = [
  { id: "ksystem", label: "Ksystem GroupWare", host: "gw.ksystem.vn", port: 465, security: "SslOnConnect" },
  { id: "office365", label: "Microsoft 365 / Outlook", host: "smtp.office365.com", port: 587, security: "StartTls" },
  { id: "gmail", label: "Gmail / Google Workspace", host: "smtp.gmail.com", port: 587, security: "StartTls" },
  { id: "custom", label: "Máy chủ khác" },
];
const SECURITY = [
  { value: "StartTls", label: "STARTTLS (thường dùng cổng 587)" },
  { value: "SslOnConnect", label: "SSL/TLS (thường dùng cổng 465)" },
  { value: "StartTlsWhenAvailable", label: "STARTTLS nếu máy chủ hỗ trợ" },
  { value: "Auto", label: "Tự động" },
  { value: "None", label: "Không mã hóa" },
];

const splitEmails = (s) =>
  s
    .split(/[\n,;]+/)
    .map((x) => x.trim())
    .filter(Boolean);

const presetOf = (host) => PRESETS.find((p) => p.host && p.host === host)?.id ?? "custom";

/** Hộp thoại cài đặt mail: email + mật khẩu đăng nhập SMTP, người nhận, CC. */
export function SettingsPanel({ open, onClose, onSaved }) {
  const dialogRef = useRef(null);
  const [form, setForm] = useState(null);
  const [hasPassword, setHasPassword] = useState(false);
  const [showPass, setShowPass] = useState(false);
  const [busy, setBusy] = useState(null); // "save" | "test"
  const [msg, setMsg] = useState(null); // { kind, text, details }

  useEffect(() => {
    const d = dialogRef.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);

  useEffect(() => {
    if (!open) return;
    setMsg(null);
    setShowPass(false);
    api
      .settings()
      .then((s) => {
        setHasPassword(s.hasPassword);
        setForm({
          employee: s.employee,
          fromName: s.fromName,
          user: s.user,
          password: "",
          clearPassword: false,
          preset: s.host ? presetOf(s.host) : PRESETS[0].id,
          host: s.host || PRESETS[0].host,
          port: s.host ? s.port : PRESETS[0].port,
          security: s.host ? s.security : PRESETS[0].security,
          to: s.to.join("\n"),
          cc: s.cc.join("\n"),
        });
      })
      .catch((e) => setMsg({ kind: "error", text: `Không tải được cài đặt: ${e.message}` }));
  }, [open]);

  const set = (patch) => setForm((f) => ({ ...f, ...patch }));

  const choosePreset = (id) => {
    const p = PRESETS.find((x) => x.id === id);
    set(p.host ? { preset: id, host: p.host, port: p.port, security: p.security } : { preset: id });
  };

  const body = () => ({
    employee: form.employee,
    fromName: form.fromName,
    host: form.host,
    port: Number(form.port) || 0,
    security: form.security,
    user: form.user,
    password: form.password || null,
    clearPassword: form.clearPassword,
    to: splitEmails(form.to),
    cc: splitEmails(form.cc),
  });

  const run = async (kind, fn) => {
    setBusy(kind);
    setMsg(null);
    try {
      await fn();
    } catch (e) {
      setMsg({ kind: "error", text: e.message, details: e.details });
    } finally {
      setBusy(null);
    }
  };

  const onTest = () =>
    run("test", async () => {
      const res = await api.testSettings(body());
      setMsg({ kind: "ok", text: res.message });
    });

  const onSubmit = (e) => {
    e.preventDefault();
    run("save", async () => {
      const saved = await api.saveSettings(body());
      setHasPassword(saved.hasPassword);
      onSaved(saved);
    });
  };

  const isGmail = form?.host === "smtp.gmail.com";
  const isKsystem = form?.host === "gw.ksystem.vn";

  return (
    <dialog ref={dialogRef} className="dialog" onClose={onClose} onCancel={onClose} aria-labelledby="settings-title">
      <form onSubmit={onSubmit} className="dialog-body">
        <header className="dialog-head">
          <h2 id="settings-title">Cài đặt mail</h2>
          <button type="button" className="btn btn-quiet" aria-label="Đóng" onClick={onClose}>
            ✕
          </button>
        </header>

        {!form ? (
          <p className="loading">{msg ? msg.text : "Đang tải…"}</p>
        ) : (
          <>
            <fieldset>
              <legend>Người gửi</legend>
              <div className="grid-2">
                <label className="field">
                  <span>Tên nhân viên (dùng trong tên file)</span>
                  <input required value={form.employee} placeholder="VoQuocAn" onChange={(e) => set({ employee: e.target.value })} />
                </label>
                <label className="field">
                  <span>Tên hiển thị khi gửi</span>
                  <input value={form.fromName} placeholder="Võ Quốc An" onChange={(e) => set({ fromName: e.target.value })} />
                </label>
                <label className="field">
                  <span>Email đăng nhập</span>
                  <input
                    type="email"
                    required
                    autoComplete="username"
                    value={form.user}
                    placeholder="ten@ksystem.vn"
                    onChange={(e) => set({ user: e.target.value })}
                  />
                </label>
                <div className="field">
                  <label htmlFor="smtp-pass">Mật khẩu</label>
                  <div className="pass">
                    <input
                      id="smtp-pass"
                      type={showPass ? "text" : "password"}
                      autoComplete="current-password"
                      value={form.password}
                      disabled={form.clearPassword}
                      placeholder={hasPassword ? "Đã lưu, để trống nếu không đổi" : "Nhập mật khẩu"}
                      onChange={(e) => set({ password: e.target.value })}
                    />
                    <button type="button" className="link" onClick={() => setShowPass((v) => !v)}>
                      {showPass ? "Ẩn" : "Hiện"}
                    </button>
                  </div>
                  {hasPassword && (
                    <label className="check">
                      <input
                        type="checkbox"
                        checked={form.clearPassword}
                        onChange={(e) => set({ clearPassword: e.target.checked, password: "" })}
                      />
                      Xóa mật khẩu đã lưu
                    </label>
                  )}
                </div>
              </div>
              {isKsystem && (
                <p className="hint">
                  Mật khẩu là mật khẩu đăng nhập GroupWare/webmail (hoặc App password nếu đã tạo). Cần bật{" "}
                  <b>Mail › Settings › SMTP-POP3-IMAP › Use SMTP/IMAP = Enable</b>; không đăng nhập quá 32 ngày sẽ bị tự tắt.
                </p>
              )}
              {isGmail && (
                <p className="hint">Gmail cần dùng App Password (Mật khẩu ứng dụng), không dùng mật khẩu đăng nhập thường.</p>
              )}
            </fieldset>

            <fieldset>
              <legend>Người nhận</legend>
              <div className="grid-2">
                <label className="field">
                  <span>Gửi tới (mỗi dòng 1 email)</span>
                  <textarea rows={3} required value={form.to} placeholder="manager@ksystem.vn" onChange={(e) => set({ to: e.target.value })} />
                </label>
                <label className="field">
                  <span>CC (không bắt buộc)</span>
                  <textarea rows={3} value={form.cc} onChange={(e) => set({ cc: e.target.value })} />
                </label>
              </div>
            </fieldset>

            <fieldset>
              <legend>Máy chủ gửi mail (SMTP)</legend>
              <div className="grid-2">
                <label className="field">
                  <span>Loại tài khoản</span>
                  <select value={form.preset} onChange={(e) => choosePreset(e.target.value)}>
                    {PRESETS.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.label}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="field">
                  <span>Máy chủ</span>
                  <input required value={form.host} onChange={(e) => set({ host: e.target.value, preset: presetOf(e.target.value) })} />
                </label>
                <label className="field">
                  <span>Cổng</span>
                  <input type="number" min="1" max="65535" required value={form.port} onChange={(e) => set({ port: e.target.value })} />
                </label>
                <label className="field">
                  <span>Bảo mật</span>
                  <select value={form.security} onChange={(e) => set({ security: e.target.value })}>
                    {SECURITY.map((s) => (
                      <option key={s.value} value={s.value}>
                        {s.label}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
            </fieldset>

            <p className="hint">Mật khẩu được mã hóa khi lưu trên máy chủ và không hiển thị lại trên trang này.</p>

            {msg && (
              <div className={`toast toast-${msg.kind}`} role="status">
                <p>{msg.text}</p>
                {msg.details?.length > 0 && (
                  <ul>
                    {msg.details.map((d) => (
                      <li key={d}>{d}</li>
                    ))}
                  </ul>
                )}
              </div>
            )}

            <footer className="dialog-foot">
              <button type="button" className="btn" disabled={!!busy} onClick={onTest}>
                {busy === "test" ? "Đang thử…" : "Thử đăng nhập"}
              </button>
              <span className="spacer" />
              <button type="button" className="btn btn-quiet" onClick={onClose}>
                Hủy
              </button>
              <button type="submit" className="btn btn-primary" disabled={!!busy}>
                {busy === "save" ? "Đang lưu…" : "Lưu cài đặt"}
              </button>
            </footer>
          </>
        )}
      </form>
    </dialog>
  );
}
