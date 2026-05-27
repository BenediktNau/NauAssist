// NauAssist Service Worker — minimal: Push-Empfang + Click-Handler.
// Keine Caching-Strategie (PWA-Offline-Caching ist explizit nicht Teil von Phase 4).

self.addEventListener("install", (event) => {
  event.waitUntil(self.skipWaiting());
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
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
