// NauAssist Service Worker
// - Push-Empfang + Click-Handler (Web Push)
// - Minimaler fetch-Handler mit App-Shell-Caching: NÖTIG, damit Chrome die App
//   als installierbare PWA erkennt ("App installieren" statt nur "Verknüpfung").
//   Liefert zugleich einen Offline-Fallback der App-Shell.

const CACHE_VERSION = "nauassist-v1";
const APP_SHELL = ["/", "/manifest.webmanifest", "/icon-192.png", "/icon-512.png"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE_VERSION);
      // addAll soll die Installation nicht scheitern lassen, falls eine Ressource
      // (z.B. hinter Auth) gerade nicht abrufbar ist.
      await Promise.allSettled(APP_SHELL.map((url) => cache.add(url)));
      await self.skipWaiting();
    })(),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      const keys = await caches.keys();
      await Promise.all(keys.filter((k) => k !== CACHE_VERSION).map((k) => caches.delete(k)));
      await self.clients.claim();
    })(),
  );
});

self.addEventListener("fetch", (event) => {
  const { request } = event;

  // Nur GET cachen; alles andere unverändert ans Netz.
  if (request.method !== "GET") return;

  const url = new URL(request.url);

  // Fremde Origins (z.B. Google Fonts) nicht abfangen.
  if (url.origin !== self.location.origin) return;

  // API- und Auth-Routen niemals cachen — immer frisch ans Netz.
  if (url.pathname.startsWith("/api/") || url.pathname.startsWith("/auth")) return;

  // Navigations-Requests (HTML): network-first mit Offline-Fallback auf App-Shell.
  if (request.mode === "navigate") {
    event.respondWith(
      (async () => {
        try {
          return await fetch(request);
        } catch {
          const cache = await caches.open(CACHE_VERSION);
          return (await cache.match("/")) || Response.error();
        }
      })(),
    );
    return;
  }

  // Statische Assets: stale-while-revalidate.
  event.respondWith(
    (async () => {
      const cache = await caches.open(CACHE_VERSION);
      const cached = await cache.match(request);
      const network = fetch(request)
        .then((response) => {
          if (response && response.ok) cache.put(request, response.clone());
          return response;
        })
        .catch(() => undefined);
      return cached || (await network) || Response.error();
    })(),
  );
});

self.addEventListener("push", (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch (e) {
    payload = { title: "NauAssist", body: event.data ? event.data.text() : "" };
  }

  const title = payload.title || "NauAssist";
  const options = {
    body: payload.body || "",
    tag: payload.tag || undefined,
    data: { url: payload.url || "/" },
    icon: "/icon-192.png",
    badge: "/icon-192.png",
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const targetUrl = (event.notification.data && event.notification.data.url) || "/";

  event.waitUntil(
    (async () => {
      const clientsList = await self.clients.matchAll({
        type: "window",
        includeUncontrolled: true,
      });
      for (const client of clientsList) {
        const url = new URL(client.url);
        if (url.origin === self.location.origin) {
          await client.focus();
          if ("navigate" in client) {
            try {
              await client.navigate(targetUrl);
            } catch (e) {
              // ignore — manche Browser blocken navigate()
            }
          }
          return;
        }
      }
      await self.clients.openWindow(targetUrl);
    })(),
  );
});
