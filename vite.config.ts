import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const runtime = globalThis as typeof globalThis & {
  process?: { env?: Record<string, string | undefined> };
};
const githubRepository = runtime.process?.env?.GITHUB_REPOSITORY;
const isGitHubActions = runtime.process?.env?.GITHUB_ACTIONS === "true";

export default defineConfig({
  // GitHub Pages 專案網址會是 /repository-name/；本機與一般主網域則維持根路徑。
  base: isGitHubActions ? `/${githubRepository?.split("/")[1] ?? "weather-outfit-pwa"}/` : "/",
  plugins: [react()],
});
