import { useEffect, useRef, useState } from "react";

/**
 * Bảng nhập liệu kiểu Excel, đúng 7 cột của file mẫu Hour_Report.
 * - Enter / ↓ / ↑ (Shift+Enter: lên): sang ô dưới / trên (Alt+Enter: xuống dòng trong ô Job Content).
 * - Tab / Shift+Tab: sang ô phải / trái.
 * - Dán (Ctrl+V) một vùng copy từ Excel: tự điền nhiều ô; nếu vùng có cột "Report time" thì khớp theo giờ.
 * - Ô Completed: gõ Y / N hoặc Space để đổi.
 * - Kéo ô vuông nhỏ ở góc dưới phải ô đang chọn (fill handle) để chép nội dung sang các dòng khác;
 *   nhấp đúp ô vuông: điền xuống tới dòng cuối. Ctrl+D: lấy nội dung ô ngay phía trên.
 */
const COLS = [
  { key: "reportDate", title: "ReportDate", readOnly: true, cls: "c-date" },
  { key: "reportTime", title: "Report time", readOnly: true, cls: "c-hour" },
  { key: "startingTime", title: "Starting time", cls: "c-time", time: true },
  { key: "jobContent", title: "Job Content", cls: "c-job" },
  { key: "completed", title: "Completed Y/N", cls: "c-done" },
  { key: "completingTime", title: "Completing time", cls: "c-time", time: true },
  { key: "remark", title: "Remark", cls: "c-remark" },
];
const FIRST_EDIT = 2;
const LAST_EDIT = COLS.length - 1;

/** "830" → "08:30", "9" → "09:00", "9:5" → "09:05"; giá trị không hiểu được giữ nguyên. */
export function normalizeTime(v) {
  const s = String(v ?? "").trim();
  if (!s) return "";
  let h;
  let m;
  const colon = s.match(/^(\d{1,2})[:hH.](\d{1,2})?$/);
  if (colon) {
    h = +colon[1];
    m = colon[2] ? +colon[2] : 0;
  } else if (/^\d{1,4}$/.test(s)) {
    if (s.length <= 2) {
      h = +s;
      m = 0;
    } else {
      h = +s.slice(0, s.length - 2);
      m = +s.slice(-2);
    }
  } else {
    return s;
  }
  if (h > 23 || m > 59) return s;
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
}

/** Tách dữ liệu clipboard dạng TSV của Excel (ô có xuống dòng được bọc trong dấu "). */
function parseTsv(text) {
  const rows = [];
  let row = [];
  let cell = "";
  let quoted = false;
  const t = text.replace(/\r\n?/g, "\n");
  for (let i = 0; i < t.length; i++) {
    const ch = t[i];
    if (quoted) {
      if (ch === '"' && t[i + 1] === '"') {
        cell += '"';
        i++;
      } else if (ch === '"') quoted = false;
      else cell += ch;
    } else if (ch === '"' && cell === "") quoted = true;
    else if (ch === "\t") {
      row.push(cell);
      cell = "";
    } else if (ch === "\n") {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
    } else cell += ch;
  }
  if (cell !== "" || row.length) {
    row.push(cell);
    rows.push(row);
  }
  return rows;
}

const hourKey = (v) => {
  const d = String(v ?? "").trim().match(/^(\d{1,2})/);
  return d ? String(+d[1]).padStart(2, "0") : null;
};

function cellValue(key, raw) {
  const v = String(raw ?? "").trim();
  if (key === "completed") return /^y/i.test(v) ? "Y" : "N";
  if (key === "startingTime" || key === "completingTime") return normalizeTime(v);
  return key === "jobContent" || key === "remark" ? String(raw ?? "").trim() : v;
}

export function SheetView({ report, views, now, sendingHour, sendingAll, onChangeRow, onFill, onSend, onDownload, onBulkChange }) {
  const tableRef = useRef(null);
  const [active, setActive] = useState(null); // ô đang chọn { r, c }
  const [fill, setFill] = useState(null); // vùng đang kéo { r0, r1, c }
  const reportRef = useRef(report);
  reportRef.current = report;

  // ---- Fill handle: kéo để chép nội dung 1 ô sang các dòng khác cùng cột ----
  const rowAtY = (y) => {
    const trs = [...(tableRef.current?.tBodies[0]?.rows ?? [])];
    if (!trs.length) return 0;
    const i = trs.findIndex((tr) => y < tr.getBoundingClientRect().bottom);
    return i === -1 ? trs.length - 1 : i;
  };

  const applyFill = ({ r0, r1, c }) => {
    if (r0 === r1) return;
    const rows = reportRef.current.rows;
    const key = COLS[c].key;
    const value = rows[r0][key];
    const patches = new Map();
    for (let r = Math.min(r0, r1); r <= Math.max(r0, r1); r++) {
      if (r !== r0 && rows[r][key] !== value) patches.set(rows[r].reportTime, { [key]: value });
    }
    if (patches.size) onFill(patches, COLS[c].title);
  };

  const startFill = (e, r, c) => {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    let current = { r0: r, r1: r, c };
    setFill(current);
    document.body.classList.add("is-filling");
    const move = (ev) => {
      const r1 = rowAtY(ev.clientY);
      if (r1 !== current.r1) setFill((current = { ...current, r1 }));
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      document.body.classList.remove("is-filling");
      setFill(null);
      applyFill(current);
      focusCell(current.r1, c);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  };

  const inFill = (r, c) => fill && fill.c === c && r >= Math.min(fill.r0, fill.r1) && r <= Math.max(fill.r0, fill.r1);

  const cellClass = (r, c, base) =>
    [base, active?.r === r && active?.c === c ? "is-active" : "", inFill(r, c) ? "in-fill" : ""].filter(Boolean).join(" ") || undefined;

  const handle = (r, c) =>
    active?.r === r && active?.c === c ? (
      <span
        className="fill-handle"
        title="Kéo để chép xuống/lên các dòng khác. Nhấp đúp: điền tới dòng cuối."
        onPointerDown={(e) => startFill(e, r, c)}
        onDoubleClick={() => applyFill({ r0: r, r1: report.rows.length - 1, c })}
      />
    ) : null;

  const focusCell = (r, c) => {
    const el = tableRef.current?.querySelector(`[data-r="${r}"][data-c="${c}"]`);
    if (!el) return;
    el.focus();
    if (el.select) el.select();
  };

  // Tự giãn chiều cao ô Job Content theo nội dung.
  useEffect(() => {
    tableRef.current?.querySelectorAll("textarea").forEach(grow);
  }, [report.rows]);

  const onKeyDown = (e, r, c) => {
    const el = e.currentTarget;
    const isText = el.tagName === "TEXTAREA";
    if (e.key === "Enter" && isText && e.altKey) {
      // Alt+Enter: xuống dòng trong ô, giống Excel.
      e.preventDefault();
      const { selectionStart: a, selectionEnd: b, value } = el;
      onChangeRow(report.rows[r].reportTime, { jobContent: `${value.slice(0, a)}\n${value.slice(b)}` });
      requestAnimationFrame(() => el.setSelectionRange(a + 1, a + 1));
      return;
    }
    const multiline = isText && el.value.includes("\n");
    if (e.key === "Enter" || (e.key === "ArrowDown" && !multiline)) {
      e.preventDefault();
      const up = e.key === "Enter" && e.shiftKey; // Shift+Enter: lên trên
      focusCell(up ? Math.max(r - 1, 0) : Math.min(r + 1, report.rows.length - 1), c);
    } else if (e.key === "ArrowUp" && !multiline) {
      e.preventDefault();
      focusCell(Math.max(r - 1, 0), c);
    } else if (e.key === "Escape") {
      el.blur();
    } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "d" && r > 0) {
      // Ctrl+D: lấy nội dung ô ngay phía trên (giống Excel).
      e.preventDefault();
      const key = COLS[c].key;
      onChangeRow(report.rows[r].reportTime, { [key]: report.rows[r - 1][key] });
    }
  };

  const onDoneKey = (e, r, c) => {
    const hour = report.rows[r].reportTime;
    const k = e.key.toLowerCase();
    if (k === "y" || k === "n") {
      e.preventDefault();
      onChangeRow(hour, { completed: k.toUpperCase() });
    } else if (k === " ") {
      e.preventDefault();
      onChangeRow(hour, { completed: report.rows[r].completed === "Y" ? "N" : "Y" });
    } else {
      onKeyDown(e, r, c);
    }
  };

  const onPaste = (e, r, c) => {
    const text = e.clipboardData.getData("text/plain");
    if (!text || (!text.includes("\t") && !text.includes("\n"))) return; // dán 1 ô: để trình duyệt xử lý
    let grid = parseTsv(text.replace(/\n$/, ""));
    if (grid.length === 0) return;
    e.preventDefault();

    // Bỏ dòng tiêu đề nếu copy cả header.
    if (grid[0].some((x) => /report\s*time|job\s*content/i.test(x))) grid = grid.slice(1);

    // Nhận diện vùng copy nguyên dòng từ file Excel mẫu:
    //   [ReportDate, Report time, ...] hoặc [Report time, Starting time, ...] → khớp theo giờ, không theo vị trí.
    const isHour = (v) => report.rows.some((x) => x.reportTime === hourKey(v) && /^\s*\d{1,2}\s*h?\s*$/i.test(v));
    const isDate = (v) => /^\s*(\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{4}|\d{8})\s*$/.test(v);
    let hourCol = -1; // vị trí cột "Report time" trong vùng dán
    let startCol = c; // cột trong bảng ứng với cột đầu của vùng dán
    if (grid.every((row) => row.length >= 3 && isDate(row[0]) && isHour(row[1]))) {
      hourCol = 1;
      startCol = 0;
    } else if (grid.every((row) => row.length >= 2 && isHour(row[0]))) {
      hourCol = 0;
      startCol = 1;
    }

    const patches = new Map(); // reportTime → patch
    grid.forEach((cells, i) => {
      const target = hourCol >= 0 ? report.rows.find((x) => x.reportTime === hourKey(cells[hourCol])) : report.rows[r + i];
      if (!target) return;
      const patch = patches.get(target.reportTime) ?? {};
      cells.forEach((raw, j) => {
        const col = COLS[startCol + j];
        if (!col || col.readOnly) return;
        patch[col.key] = cellValue(col.key, raw);
      });
      patches.set(target.reportTime, patch);
    });
    if (patches.size) onBulkChange(patches);
  };

  return (
    <div className="sheet-wrap">
      <table className="sheet" ref={tableRef}>
        <colgroup>
          {COLS.map((col) => (
            <col key={col.key} className={col.cls} />
          ))}
          <col className="c-status" />
          <col className="c-actions" />
        </colgroup>
        <thead>
          <tr>
            {COLS.map((col) => (
              <th key={col.key} scope="col">
                {col.title}
              </th>
            ))}
            <th scope="col">Trạng thái</th>
            <th scope="col">
              <span className="sr-only">Thao tác</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {report.rows.map((row, r) => {
            const view = views[r] ?? { tone: "idle", text: "" };
            const sent = !!report.status[row.reportTime]?.sentAt;
            const hasContent = row.jobContent.trim().length > 0;
            const busy = sendingHour === row.reportTime;
            const current = isCurrent(report.date, row, now);
            return (
              <tr key={row.reportTime} className={`tone-${view.tone}${current ? " is-now" : ""}`}>
                <td className="ro">{report.date}</td>
                <td className="ro hour">{row.reportTime}</td>
                {COLS.slice(FIRST_EDIT).map((col, j) => {
                  const c = FIRST_EDIT + j;
                  const common = {
                    "data-r": r,
                    "data-c": c,
                    "aria-label": `${col.title} ${row.reportTime}H`,
                    onPaste: (e) => onPaste(e, r, c),
                    onFocus: () => setActive({ r, c }),
                  };
                  if (col.key === "completed") {
                    return (
                      <td key={col.key} className={cellClass(r, c, "done")}>
                        <button
                          type="button"
                          {...common}
                          className={`yn yn-${row.completed}`}
                          onClick={() => onChangeRow(row.reportTime, { completed: row.completed === "Y" ? "N" : "Y" })}
                          onKeyDown={(e) => onDoneKey(e, r, c)}
                        >
                          {row.completed}
                        </button>
                        {handle(r, c)}
                      </td>
                    );
                  }
                  if (col.key === "jobContent") {
                    return (
                      <td key={col.key} className={cellClass(r, c)}>
                        <textarea
                          {...common}
                          rows={1}
                          value={row.jobContent}
                          placeholder={r === 0 ? "Nhập công việc, hoặc dán từ Excel…" : ""}
                          onChange={(e) => {
                            grow(e.target);
                            onChangeRow(row.reportTime, { jobContent: e.target.value });
                          }}
                          onKeyDown={(e) => onKeyDown(e, r, c)}
                        />
                        {handle(r, c)}
                      </td>
                    );
                  }
                  return (
                    <td key={col.key} className={cellClass(r, c, col.time ? "time" : "")}>
                      <input
                        {...common}
                        type="text"
                        inputMode={col.time ? "numeric" : undefined}
                        placeholder={col.time ? "hh:mm" : ""}
                        value={row[col.key]}
                        onChange={(e) => onChangeRow(row.reportTime, { [col.key]: e.target.value })}
                        onBlur={
                          col.time
                            ? (e) => {
                                const v = normalizeTime(e.target.value);
                                if (v !== row[col.key]) onChangeRow(row.reportTime, { [col.key]: v });
                              }
                            : undefined
                        }
                        onKeyDown={(e) => onKeyDown(e, r, c)}
                      />
                      {handle(r, c)}
                    </td>
                  );
                })}
                <td className="status" title={view.text}>
                  <span>{view.text}</span>
                </td>
                <td className="actions">
                  <button
                    type="button"
                    className={sent ? "btn btn-sm" : "btn btn-sm btn-primary"}
                    disabled={busy || sendingAll || !hasContent}
                    title={hasContent ? undefined : "Điền Job Content trước khi gửi"}
                    onClick={() => onSend(row)}
                  >
                    {busy ? "Đang gửi…" : sent ? "Gửi lại" : "Gửi"}
                  </button>
                  <button
                    type="button"
                    className="btn btn-sm btn-quiet"
                    title={`Tải file Excel ${row.reportTime}H`}
                    onClick={() => onDownload(row.reportTime)}
                  >
                    Excel
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <p className="sheet-hint">
        Enter / ↑ ↓ để di chuyển, Alt+Enter để xuống dòng trong ô, Ctrl+S để lưu. Kéo ô vuông ở góc ô đang chọn để chép xuống
        nhiều dòng (nhấp đúp: tới dòng cuối), Ctrl+D lấy nội dung ô phía trên. Có thể copy nhiều ô hoặc cả dòng trong Excel rồi
        dán vào bảng.
      </p>
    </div>
  );
}

function grow(el) {
  el.style.height = "auto";
  el.style.height = `${el.scrollHeight}px`;
}

/** Dòng ứng với khung giờ đang làm việc (StartingTime ≤ giờ hiện tại < ReportTime:00). */
function isCurrent(date, row, now) {
  const start = new Date(`${date}T${normalizeTime(row.startingTime) || "00:00"}:00`);
  const end = new Date(`${date}T${row.reportTime}:00:00`);
  return now >= start && now < end;
}
