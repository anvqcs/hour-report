import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Dev: gọi API .NET (profile "https" chạy ở https://localhost:7147, chứng chỉ dev tự ký)
    proxy: { "/api": { target: "https://localhost:7147", secure: false, changeOrigin: true } },
  },
  build: {
    // Build thẳng vào wwwroot của backend → publish 1 lần là chạy cả 2.
    outDir: "../HourReport/HourReport.API/wwwroot",
    emptyOutDir: true,
  },
});
