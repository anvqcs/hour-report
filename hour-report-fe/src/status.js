// tone: "sent" | "wait" | "late" | "idle" | "error"

const hm = (iso) => (iso ? iso.slice(11, 16) : "");

/** Sau mốc giờ bao nhiêu phút mà chưa gửi thì báo đỏ "Chưa gửi". */
const LATE_MINUTES = 60;

/** Mốc giờ gửi của 1 dòng, vd "2026-10-30" + "09" → 2026-10-30 09:00 (giờ địa phương). */
export const dueOf = (date, reportTime) => new Date(`${date}T${reportTime}:00:00`);

/** Trạng thái hiển thị của 1 khung giờ: { tone, text }. Báo cáo chỉ gửi tay. */
export function slotView(report, row, now) {
  const st = report.status[row.reportTime];
  if (st?.sentAt) return { tone: "sent", text: `Đã gửi lúc ${hm(st.sentAt)}` };
  if (st?.lastError) return { tone: "error", text: `Gửi lỗi: ${st.lastError}` };

  const due = dueOf(report.date, row.reportTime);
  const lateAt = new Date(due.getTime() + LATE_MINUTES * 60_000);
  const hasContent = row.jobContent.trim().length > 0;

  if (now >= lateAt) return { tone: "late", text: "Chưa gửi" };
  if (now >= due) return hasContent ? { tone: "wait", text: "Đến giờ gửi" } : { tone: "late", text: "Thiếu nội dung" };
  return { tone: "idle", text: `Gửi lúc ${row.reportTime}:00` };
}
