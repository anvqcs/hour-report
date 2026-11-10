
const endOf = (reportTime) => `${reportTime}:00`;

export function SlotRow({ row, view, sent, busy, locked, canCopyPrev, onChange, onCopyPrev, onSend, onDownload }) {
  const id = `job-${row.reportTime}`;
  const hasContent = row.jobContent.trim().length > 0;

  return (
    <li className={`slot tone-${view.tone}`}>
      <div className="slot-clock">
        <span className="slot-hour">{row.reportTime}</span>
        <span className="slot-range">
          {row.startingTime}–{endOf(row.reportTime)}
        </span>
      </div>

      <div className="slot-rail" aria-hidden="true">
        <span className="slot-dot" />
      </div>

      <div className="slot-body">
        <div className="job-head">
          <label htmlFor={id}>Công việc</label>
          {canCopyPrev && (
            <button type="button" className="link" onClick={onCopyPrev}>
              Giống giờ trước
            </button>
          )}
        </div>
        <textarea
          id={id}
          rows={2}
          value={row.jobContent}
          placeholder="Vd: FW: [YDC] Request for ERP system support"
          onChange={(e) => onChange({ jobContent: e.target.value })}
        />

        <div className="slot-fields">
          <div className="seg" role="group" aria-label="Hoàn thành">
            <button
              type="button"
              aria-pressed={row.completed === "N"}
              onClick={() => onChange({ completed: "N" })}
            >
              Đang làm
            </button>
            <button
              type="button"
              aria-pressed={row.completed === "Y"}
              onClick={() => onChange({ completed: "Y" })}
            >
              Hoàn thành
            </button>
          </div>

          <label className="field field-time">
            <span>Giờ xong</span>
            <input
              type="time"
              value={row.completingTime}
              onChange={(e) => onChange({ completingTime: e.target.value })}
            />
          </label>

          <label className="field field-remark">
            <span>Ghi chú</span>
            <input
              type="text"
              value={row.remark}
              onChange={(e) => onChange({ remark: e.target.value })}
            />
          </label>
        </div>
      </div>

      <div className="slot-side">
        <p className="slot-status">{view.text}</p>
        <div className="slot-actions">
          <button
            type="button"
            className={sent ? "btn" : "btn btn-primary"}
            disabled={busy || locked || !hasContent}
            title={hasContent ? undefined : "Điền nội dung công việc trước khi gửi"}
            onClick={onSend}
          >
            {busy ? "Đang gửi…" : sent ? "Gửi lại" : "Gửi ngay"}
          </button>
          <button type="button" className="btn btn-quiet" onClick={onDownload}>
            Tải Excel
          </button>
        </div>
      </div>
    </li>
  );
}
