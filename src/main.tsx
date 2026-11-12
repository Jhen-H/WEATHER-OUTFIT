import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./styles.css";

// Service Worker 只在安全環境註冊；正式 HTTPS 與本機 loopback 都可測試 PWA。
if (window.isSecureContext && "serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register(`${import.meta.env.BASE_URL}service-worker.js`).catch((error) => {
      console.warn("PWA 離線功能註冊失敗", error);
    });
  });
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
