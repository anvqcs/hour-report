import { useEffect, useState } from "react";

const KEY = "hour-report.theme";
const OPTIONS = [
  {
    value: "system",
    label: "Tự động",
    title: "Theo cài đặt sáng/tối của máy",
    icon: (
      <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
        <rect x="1.75" y="2.75" width="12.5" height="8.5" rx="1.25" />
        <path d="M5.5 13.75h5M8 11.25v2.5" strokeLinecap="round" />
      </svg>
    ),
  },
  {
    value: "light",
    label: "Sáng",
    title: "Giao diện sáng",
    icon: (
      <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
        <circle cx="8" cy="8" r="3" />
        <path d="M8 1.5v1.5M8 13v1.5M1.5 8H3M13 8h1.5M3.4 3.4l1 1M11.6 11.6l1 1M3.4 12.6l1-1M11.6 4.4l1-1" />
      </svg>
    ),
  },
  {
    value: "dark",
    label: "Tối",
    title: "Giao diện tối",
    icon: (
      <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" aria-hidden="true">
        <path d="M13.5 9.6A5.75 5.75 0 0 1 6.4 2.5a5.75 5.75 0 1 0 7.1 7.1Z" />
      </svg>
    ),
  },
];

const read = () => {
  try {
    const v = localStorage.getItem(KEY);
    return v === "light" || v === "dark" ? v : "system";
  } catch {
    return "system";
  }
};

/** Áp theme lên <html data-theme>. "system" = bỏ thuộc tính, CSS tự theo prefers-color-scheme. */
const apply = (theme) => {
  if (theme === "system") document.documentElement.removeAttribute("data-theme");
  else document.documentElement.setAttribute("data-theme", theme);
};

/** Nút chọn giao diện Tự động / Sáng / Tối, nhớ lựa chọn trong trình duyệt. */
export function ThemeToggle() {
  const [theme, setTheme] = useState(read);

  useEffect(() => {
    apply(theme);
    try {
      if (theme === "system") localStorage.removeItem(KEY);
      else localStorage.setItem(KEY, theme);
    } catch {
      /* trình duyệt chặn storage: vẫn đổi được, chỉ không nhớ */
    }
  }, [theme]);

  return (
    <div className="theme" role="group" aria-label="Giao diện">
      {OPTIONS.map((o) => (
        <button
          key={o.value}
          type="button"
          title={o.title}
          aria-label={o.label}
          aria-pressed={theme === o.value}
          onClick={() => setTheme(o.value)}
        >
          {o.icon}
        </button>
      ))}
    </div>
  );
}
