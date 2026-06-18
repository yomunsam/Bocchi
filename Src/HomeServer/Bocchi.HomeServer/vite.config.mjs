import { defineConfig } from "vite";

export default defineConfig({
  build: {
    emptyOutDir: false,
    outDir: "wwwroot",
    lib: {
      entry: "Client/bocchi-markdown-editor.js",
      formats: ["iife"],
      name: "BocchiMarkdownEditorBundle",
      fileName: () => "bocchi-markdown-editor.min.js",
    },
    rollupOptions: {
      output: {
        assetFileNames: "assets/[name][extname]",
      },
    },
  },
});
