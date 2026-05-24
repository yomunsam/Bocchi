import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

/// <summary>
/// Admin CSS 的独立构建入口。Markdown editor 仍由主 Vite config 产出 IIFE，
/// 这里只把 Tailwind 源 CSS 编译成 wwwroot/app.css，避免生成额外脚本入口。
/// </summary>
function removeCssEntryChunks() {
  return {
    name: "bocchi-remove-css-entry-chunks",
    generateBundle(_, bundle) {
      for (const [fileName, asset] of Object.entries(bundle)) {
        if (asset.type === "chunk") {
          delete bundle[fileName];
        }
      }
    },
  };
}

export default defineConfig({
  plugins: [tailwindcss(), removeCssEntryChunks()],
  build: {
    emptyOutDir: false,
    outDir: "wwwroot",
    rollupOptions: {
      input: "Client/app.css",
      output: {
        assetFileNames: assetInfo => assetInfo.name === "app.css" ? "app.css" : "assets/[name][extname]",
      },
    },
  },
});
