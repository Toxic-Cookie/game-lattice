import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Build output lands in ../wwwroot so the ASP.NET host serves it as static
// files. In dev (`npm run dev`), proxy /api to the running Studio backend.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/api": "http://127.0.0.1:5210",
    },
  },
});
