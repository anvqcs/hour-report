async function request(url, init) {
  const res = await fetch(url, init);
  if (!res.ok) {
    let message = `Lỗi ${res.status}`;
    let details = [];
    try {
      const body = await res.json();
      if (body?.message) message = body.message;
      if (Array.isArray(body?.errors)) details = body.errors;
      else if (body?.errors) details = Object.values(body.errors).flat(); // lỗi validate của ASP.NET
    } catch {
      /* body không phải JSON */
    }
    const err = new Error(message);
    err.details = details;
    throw err;
  }
  return res.json();
}

export const api = {
  config: () => request("/api/config"),

  get: (date) => request(`/api/reports/${date}`),

  save: (date, rows) =>
    request(`/api/reports/${date}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ rows }),
    }),

  send: (date, hour) => request(`/api/reports/${date}/send/${hour}`, { method: "POST" }),

  sendAll: (date) => request(`/api/reports/${date}/send-all`, { method: "POST" }),

  importExcel: (file) => {
    const form = new FormData();
    form.append("file", file);
    return request("/api/import", { method: "POST", body: form });
  },

  excelUrl: (date, hour) => `/api/reports/${date}/excel/${hour}`,

  settings: () => request("/api/settings"),

  saveSettings: (body) =>
    request("/api/settings", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }),

  testSettings: (body) =>
    request("/api/settings/test", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }),
};
