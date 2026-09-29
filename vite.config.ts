import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

const webRoot = fileURLToPath(new URL("./src/Jularr.Web", import.meta.url));

export default defineConfig({
  root: webRoot,
  build: {
    emptyOutDir: true,
    manifest: true,
    outDir: "wwwroot/build",
    rollupOptions: {
      input: {
        app: fileURLToPath(new URL("./src/Jularr.Web/frontend/entries/app.ts", import.meta.url))
      }
    }
  }
});
