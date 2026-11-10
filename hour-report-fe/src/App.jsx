import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "./api";
import { SlotRow } from "./components/SlotRow";
import { SheetView } from "./components/SheetView";
import { SettingsPanel } from "./components/SettingsPanel";
import { ThemeToggle } from "./components/ThemeToggle";
import { slotView } from "./status";

const pad = (n) => String(n).padStart(2, "0");
const ymd = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const shiftDay = (date, days) => {
  const d = new Date(`${date}T00:00:00`);
  d.setDate(d.getDate() + days);
  return ymd(d);
};
const weekday = (date) =>
  new Date(`${date}T00:00:00`).toLocaleDateString("vi-VN", { weekday: "long", day: "numeric", month: "numeric", year: "numeric" });

const VIEW_KEY = "hour-report.view";
const readView = () => {
  try {
    return localStorage.getItem(VIEW_KEY) === "timeline" ? "timeline" : "sheet";
  } catch {
    return "sheet";
  }
};

export default function App() {
  const [config, setConfig] = useState(null);
  const [clockOffset, setClockOffset] = useState(0); // giờ server - giờ máy
  const [now, setNow] = useState(() => new Date());
  const [date, setDate] = useState(() => ymd(new Date()));
  const [report, setReport] = useState(null);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [sendingHour, setSendingHour] = useState(null);
  const [sendingAll, setSendingAll] = useState(false);
  const [toast, setToast] = useState(null);
  const [view, setView] = useState(readView); // "sheet" | "timeline"
  const [settingsOpen, setSettingsOpen] = useState(false);
  const fileInput = useRef(null);
  const saveRef = useRef(null);
  const dirtyRef = useRef(dirty);
  dirtyRef.current = dirty;
  const reportRef = useRef(report);
  reportRef.current = report;

  // ---- Config + đồng hồ theo giờ server ----
  const loadConfig = useCallback(async () => {
    const cfg = await api.config();
    setConfig(cfg);
    setClockOffset(new Date(cfg.now).getTime() - Date.now());
    return cfg;
  }, []);

  useEffect(() => {
    loadConfig()
      .then((cfg) => {
        setDate(cfg.now.slice(0, 10));
        if (!cfg.mailConfigured) setSettingsOpen(true); // lần đầu chạy: mở cài đặt mail
      })
      .catch((e) => setToast({ kind: "error", text: `Không kết nối được máy chủ: ${e.message}` }));
  }, [loadConfig]);

  useEffect(() => {
    const t = setInterval(() => setNow(new Date(Date.now() + clockOffset)), 15_000);
    setNow(new Date(Date.now() + clockOffset));
    return () => clearInterval(t);
  }, [clockOffset]);

  // ---- Tải báo cáo theo ngày ----
  const loadReport = useCallback(async (d) => {
    const r = await api.get(d);
    setReport(r);
    setDirty(false);
    return r;
  }, []);

  useEffect(() => {
    // Đã có sẵn dữ liệu của ngày này (vd vừa nhập file và chuyển ngày) → không tải đè.
    if (reportRef.current?.date === date) return;
    loadReport(date).catch((e) => setToast({ kind: "error", text: e.message }));
  }, [date, loadReport]);

  // Mỗi phút lấy lại trạng thái gửi (có thể đã gửi từ tab khác). Đang sửa dở thì giữ nội dung.
  useEffect(() => {
    const t = setInterval(async () => {
      try {
        const r = await api.get(date);
        setReport((cur) => (dirtyRef.current && cur ? { ...cur, status: r.status } : r));
        loadConfig().catch(() => undefined);
      } catch {
        /* bỏ qua lỗi mạng tạm thời */
      }
    }, 60_000);
    return () => clearInterval(t);
  }, [date, loadConfig]);

  // Cảnh báo khi rời trang mà chưa lưu
  useEffect(() => {
    const h = (e) => {
      if (dirtyRef.current) e.preventDefault();
    };
    window.addEventListener("beforeunload", h);
    return () => window.removeEventListener("beforeunload", h);
  }, []);

  useEffect(() => {
    if (!toast || toast.details?.length) return;
    const t = setTimeout(() => setToast(null), toast.undo ? 10000 : 5000);
    return () => clearTimeout(t);
  }, [toast]);

  // ---- Chỉnh sửa ----
  const updateRow = (hour, patch) => {
    setReport((cur) =>
      cur ? { ...cur, rows: cur.rows.map((r) => (r.reportTime === hour ? { ...r, ...patch } : r)) } : cur,
    );
    setDirty(true);
  };

  /** Dán nhiều ô từ Excel: patches = Map(reportTime → patch). */
  const updateRows = (patches) => {
    setReport((cur) =>
      cur ? { ...cur, rows: cur.rows.map((r) => (patches.has(r.reportTime) ? { ...r, ...patches.get(r.reportTime) } : r)) } : cur,
    );
    setDirty(true);
  };

  /** Kéo fill handle trên bảng: chép 1 ô sang nhiều dòng, có Hoàn tác. */
  const fillRows = (patches, column) => {
    const before = report.rows;
    const wasDirty = dirty;
    updateRows(patches);
    setToast({
      kind: "ok",
      text: `Đã điền cột ${column} cho ${patches.size} dòng.`,
      undo: () => {
        setReport((cur) => (cur ? { ...cur, rows: before } : cur));
        setDirty(wasDirty);
        setToast(null);
      },
    });
  };

  const changeView = (v) => {
    setView(v);
    try {
      localStorage.setItem(VIEW_KEY, v);
    } catch {
      /* trình duyệt chặn storage: chỉ không nhớ lựa chọn */
    }
  };

  const onSettingsSaved = async (saved) => {
    setSettingsOpen(false);
    await loadConfig().catch(() => undefined);
    setToast({ kind: "ok", text: `Đã lưu cài đặt mail. Gửi tới ${saved.to.join(", ")}.` });
  };

  const copyPrev = (index) => {
    if (!report || index === 0) return;
    const prev = report.rows[index - 1];
    updateRow(report.rows[index].reportTime, { jobContent: prev.jobContent, completed: "N" });
  };

  const save = async () => {
    if (!report) return null;
    setSaving(true);
    try {
      const saved = await api.save(date, report.rows);
      setReport(saved);
      setDirty(false);
      return saved;
    } catch (e) {
      setToast({ kind: "error", text: `Lưu thất bại: ${e.message}` });
      return null;
    } finally {
      setSaving(false);
    }
  };

  const onSave = async () => {
    if (await save()) setToast({ kind: "ok", text: "Đã lưu thay đổi." });
  };
  saveRef.current = onSave;

  // Ctrl+S / Cmd+S: lưu
  useEffect(() => {
    const h = (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        if (dirtyRef.current) saveRef.current?.();
      }
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, []);

  // ---- Gửi / tải ----
  const onSend = async (row) => {
    if (!report) return;
    const st = report.status[row.reportTime];
    if (st?.sentAt && !window.confirm(`Báo cáo ${row.reportTime}H đã gửi lúc ${st.sentAt.slice(11, 16)}. Gửi lại?`)) return;
    if (dirty && !(await save())) return;

    setSendingHour(row.reportTime);
    try {
      const res = await api.send(date, row.reportTime);
      setReport(res.report);
      setToast({ kind: "ok", text: `Đã gửi: ${res.subject}` });
    } catch (e) {
      setToast({ kind: "error", text: e.message });
      loadReport(date).catch(() => undefined);
    } finally {
      setSendingHour(null);
    }
  };

  /** Xóa nội dung đã nhập của cả ngày để nhập lại (chưa lưu, có Hoàn tác). Trạng thái đã gửi giữ nguyên. */
  const onClear = () => {
    if (!report) return;
    const startOf = Object.fromEntries((config?.slots ?? []).map((s) => [s.reportTime, s.startingTime]));
    const isBlank = (r) =>
      !r.jobContent.trim() && !r.completingTime && !r.remark && r.completed !== "Y" && r.startingTime === (startOf[r.reportTime] ?? r.startingTime);
    if (report.rows.every(isBlank)) {
      setToast({ kind: "ok", text: "Bảng đang trống." });
      return;
    }
    const sent = report.rows.filter((r) => report.status[r.reportTime]?.sentAt).length;
    const ask =
      `Xóa toàn bộ nội dung đã nhập của ngày ${date}?` +
      (sent ? `\n${sent} giờ đã gửi vẫn giữ trạng thái "Đã gửi" (mail đã gửi không bị ảnh hưởng).` : "") +
      `\nThay đổi chỉ được ghi khi bấm Lưu thay đổi.`;
    if (!window.confirm(ask)) return;

    const before = report.rows;
    const wasDirty = dirty;
    setReport((cur) => ({
      ...cur,
      rows: cur.rows.map((r) => ({
        ...r,
        startingTime: startOf[r.reportTime] ?? r.startingTime,
        jobContent: "",
        completed: "N",
        completingTime: "",
        remark: "",
      })),
    }));
    setDirty(true);
    setToast({
      kind: "ok",
      text: "Đã xóa nội dung. Nhập lại rồi bấm Lưu thay đổi.",
      undo: () => {
        setReport((cur) => (cur ? { ...cur, rows: before } : cur));
        setDirty(wasDirty);
        setToast(null);
      },
    });
    requestAnimationFrame(() => document.querySelector('[data-r="0"][data-c="3"], #job-' + before[0]?.reportTime)?.focus());
  };

  /** Gửi lần lượt mọi giờ chưa gửi (đã có nội dung) của ngày đang chọn. */
  const onSendAll = async () => {
    if (!report) return;
    const unsent = report.rows.filter((r) => !report.status[r.reportTime]?.sentAt);
    const ready = unsent.filter((r) => r.jobContent.trim());
    const empty = unsent.filter((r) => !r.jobContent.trim());
    if (ready.length === 0) {
      setToast({ kind: "error", text: unsent.length ? "Các giờ chưa gửi đều chưa có Job Content." : "Đã gửi hết báo cáo của ngày này." });
      return;
    }
    const hours = (rows) => rows.map((r) => `${r.reportTime}H`).join(", ");
    const ask =
      `Gửi ${ready.length} báo cáo ngày ${date}: ${hours(ready)}?\n` +
      `Mỗi giờ 1 mail, kèm file Excel cộng dồn.` +
      (empty.length ? `\n\nBỏ qua vì chưa có Job Content: ${hours(empty)}` : "");
    if (!window.confirm(ask)) return;
    if (dirty && !(await save())) return;

    setSendingAll(true);
    try {
      const res = await api.sendAll(date);
      setReport(res.report);
      const details = [
        ...(res.sent.length ? [`Đã gửi: ${res.sent.map((h) => `${h}H`).join(", ")}`] : []),
        ...res.skipped.filter((s) => s.message !== "đã gửi trước đó").map((s) => `Bỏ qua ${s.reportTime}H: ${s.message}`),
        ...(res.failed ? [`Lỗi ở ${res.failed.reportTime}H, các giờ sau chưa gửi: ${res.failed.message}`] : []),
      ];
      setToast({
        kind: res.failed ? "error" : "ok",
        text: res.failed ? `Gửi được ${res.sent.length}/${ready.length} báo cáo.` : `Đã gửi ${res.sent.length} báo cáo ngày ${date}.`,
        details,
      });
    } catch (e) {
      setToast({ kind: "error", text: e.message });
      loadReport(date).catch(() => undefined);
    } finally {
      setSendingAll(false);
    }
  };

  const onDownload = async (hour) => {
    if (dirty && !(await save())) return;
    const a = document.createElement("a");
    a.href = api.excelUrl(date, hour);
    a.click();
  };

  // ---- Nhập file Excel mẫu ----
  const onImport = async (file) => {
    try {
      const result = await api.importExcel(file);
      let target = report;
      if (result.date && result.date !== date) {
        const go = window.confirm(
          `File là báo cáo ngày ${result.date}, đang chọn ngày ${date}.\n\nOK: chuyển sang ngày ${result.date}\nHủy: nhập vào ngày ${date}`,
        );
        if (go) {
          if (dirty && !window.confirm("Thay đổi chưa lưu của ngày hiện tại sẽ mất. Tiếp tục?")) return;
          target = await loadReport(result.date);
          setDate(result.date);
        }
      }
      if (!target || result.rows.length === 0) {
        setToast({ kind: "error", text: "Không nhập được dòng nào.", details: result.warnings });
        return;
      }
      const byHour = new Map(result.rows.map((r) => [r.reportTime, r]));
      setReport({
        ...target,
        rows: target.rows.map((r) => {
          const imp = byHour.get(r.reportTime);
          return imp ? { ...r, ...imp, startingTime: imp.startingTime || r.startingTime } : r;
        }),
      });
      setDirty(true);
      setToast({
        kind: result.warnings.length ? "error" : "ok",
        text: `Đã nhập ${result.rows.length} dòng từ ${file.name}. Kiểm tra rồi bấm Lưu thay đổi.`,
        details: result.warnings,
      });
    } catch (e) {
      setToast({ kind: "error", text: e.message });
    }
  };

  // ---- Hiển thị ----
  const views = useMemo(() => {
    if (!report || !config) return [];
    return report.rows.map((r) => slotView(report, r, now));
  }, [report, config, now]);

  const sentCount = report ? report.rows.filter((r) => report.status[r.reportTime]?.sentAt).length : 0;
  const total = report?.rows.length ?? 0;

  return (
    <div className="page">
      <header className="top">
        <div className="who">
          <h1>Báo cáo giờ</h1>
          {config && (
            <p>
              {config.team} / {config.employee}
              {config.testMode && <span className="badge-test">Chế độ thử: mail ghi ra file, không gửi thật</span>}
            </p>
          )}
        </div>

        <div className="top-right">
        <ThemeToggle />
        <div className="day">
          <button type="button" className="btn btn-quiet" aria-label="Ngày trước" onClick={() => setDate(shiftDay(date, -1))}>
            ‹
          </button>
          <input type="date" value={date} onChange={(e) => e.target.value && setDate(e.target.value)} aria-label="Ngày báo cáo" />
          <button type="button" className="btn btn-quiet" aria-label="Ngày sau" onClick={() => setDate(shiftDay(date, 1))}>
            ›
          </button>
        </div>
        </div>
      </header>

      <section className="summary">
        <div className="progress" aria-label={`Đã gửi ${sentCount} trên ${total} báo cáo`}>
          <strong>
            {sentCount}/{total}
          </strong>
          <span>báo cáo đã gửi, {weekday(date)}</span>
          <div className="bar">
            {report?.rows.map((r, i) => (
              <i key={r.reportTime} className={`tone-${views[i]?.tone ?? "idle"}`} title={`${r.reportTime}H: ${views[i]?.text ?? ""}`} />
            ))}
          </div>
        </div>

        <div className="tools">
          <input
            ref={fileInput}
            type="file"
            accept=".xlsx"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) onImport(f);
              e.target.value = "";
            }}
          />
          <button type="button" className="btn" onClick={() => fileInput.current?.click()}>
            Nhập file Excel
          </button>
          <button
            type="button"
            className="btn"
            disabled={!report || sendingAll || !!sendingHour}
            title="Xóa toàn bộ nội dung đã nhập của ngày này để nhập lại"
            onClick={onClear}
          >
            Xóa nội dung
          </button>
          <button type="button" className="btn" onClick={() => setSettingsOpen(true)}>
            Cài đặt mail
          </button>
          <button
            type="button"
            className="btn"
            disabled={!report || sendingAll || !!sendingHour || sentCount === total}
            title="Gửi lần lượt mọi giờ chưa gửi của ngày này"
            onClick={onSendAll}
          >
            {sendingAll ? "Đang gửi…" : "Gửi tất cả"}
          </button>
          <button type="button" className="btn btn-primary" disabled={!dirty || saving} onClick={onSave}>
            {saving ? "Đang lưu…" : dirty ? "Lưu thay đổi" : "Đã lưu"}
          </button>
        </div>
      </section>

      {config && !config.mailConfigured && (
        <div className="toast toast-error" role="alert">
          <p>Chưa cài đặt email gửi, mật khẩu hoặc người nhận nên chưa gửi được báo cáo.</p>
          <button type="button" className="link" onClick={() => setSettingsOpen(true)}>
            Cài đặt ngay
          </button>
        </div>
      )}

      <div className="view-bar">
        <div className="tabs" role="tablist" aria-label="Kiểu hiển thị">
          <button type="button" role="tab" aria-selected={view === "sheet"} onClick={() => changeView("sheet")}>
            Bảng tính
          </button>
          <button type="button" role="tab" aria-selected={view === "timeline"} onClick={() => changeView("timeline")}>
            Theo giờ
          </button>
        </div>
        {config && (
          <p className="recipients">
            Gửi tới {config.mailTo.join(", ") || "(chưa cấu hình)"}
            {config.mailCc.length > 0 && <>, CC {config.mailCc.join(", ")}</>}{" "}
            <button type="button" className="link" onClick={() => setSettingsOpen(true)}>
              Sửa
            </button>
          </p>
        )}
      </div>

      {toast && (
        <div className={`toast toast-${toast.kind}`} role="status">
          <p>{toast.text}</p>
          {toast.details && toast.details.length > 0 && (
            <ul>
              {toast.details.map((d) => (
                <li key={d}>{d}</li>
              ))}
            </ul>
          )}
          <div className="toast-actions">
            {toast.undo && (
              <button type="button" className="link" onClick={toast.undo}>
                Hoàn tác
              </button>
            )}
            <button type="button" className="link" onClick={() => setToast(null)}>
              Đóng
            </button>
          </div>
        </div>
      )}

      {report && view === "sheet" ? (
        <SheetView
          report={report}
          views={views}
          now={now}
          sendingHour={sendingHour}
          sendingAll={sendingAll}
          onChangeRow={updateRow}
          onBulkChange={updateRows}
          onFill={fillRows}
          onSend={onSend}
          onDownload={onDownload}
        />
      ) : report ? (
        <ol className="timeline">
          {report.rows.map((row, i) => {
            const prev = report.rows[i - 1];
            const lunch = prev && Number(row.reportTime) - Number(prev.reportTime) > 1;
            return (
              <FragmentWithBreak key={row.reportTime} showBreak={!!lunch} from={prev?.reportTime} to={row.startingTime}>
                <SlotRow
                  row={row}
                  view={views[i] ?? { tone: "idle", text: "" }}
                  sent={!!report.status[row.reportTime]?.sentAt}
                  busy={sendingHour === row.reportTime}
                  locked={sendingAll}
                  canCopyPrev={i > 0 && !!prev?.jobContent.trim()}
                  onChange={(p) => updateRow(row.reportTime, p)}
                  onCopyPrev={() => copyPrev(i)}
                  onSend={() => onSend(row)}
                  onDownload={() => onDownload(row.reportTime)}
                />
              </FragmentWithBreak>
            );
          })}
        </ol>
      ) : (
        <p className="loading">Đang tải báo cáo…</p>
      )}

      <SettingsPanel open={settingsOpen} onClose={() => setSettingsOpen(false)} onSaved={onSettingsSaved} />

      <footer className="foot">
        Mỗi báo cáo gửi kèm file Excel cộng dồn từ đầu ngày đến giờ đó, đúng mẫu Hour_Report.
      </footer>
    </div>
  );
}

function FragmentWithBreak({
  showBreak,
  from,
  to,
  children,
}) {
  return (
    <>
      {showBreak && (
        <li className="break" aria-label="Nghỉ trưa">
          <span>
            Nghỉ trưa {from}:00–{to}
          </span>
        </li>
      )}
      {children}
    </>
  );
}
