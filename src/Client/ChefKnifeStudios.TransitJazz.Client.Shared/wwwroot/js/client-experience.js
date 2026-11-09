const databaseName = "transitjazz-client-experience-v1";
const storeName = "events";
const maxEvents = 1000;
const maxQueueBytes = 4 * 1024 * 1024;
const maxAgeMs = 7 * 24 * 60 * 60 * 1000;
const maxBatchEvents = 20;
const maxBatchBytes = 64 * 1024;
const memoryQueue = new Map();
const visibilitySubscribers = new Set();
const visibilityById = new Map();
const schedulers = new Map();
let databasePromise;
let senderPromise;
let retryTimer;
let retryCount = 0;
let sequence = 0;
let queuedLosses = new Map();
let visibilityListening = false;
let nextVisibilityId = 1;
let nextSchedulerId = 1;

const meta = (name, fallback = "") => document.querySelector(`meta[name="${name}"]`)?.content ?? fallback;
const experienceEnabled = () => meta("client-experience-enabled", "false").toLowerCase() === "true";
const apiBaseUrl = () => (meta("client-experience-api-base-url", location.origin) || location.origin).replace(/\/$/, "");
const release = () => meta("client-release", "unknown").slice(0, 64) || "unknown";
const uuid = () => crypto.randomUUID();

function openDatabase() {
    if (!("indexedDB" in globalThis)) return Promise.reject(new Error("unavailable"));
    if (databasePromise) return databasePromise;
    databasePromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => {
            const db = request.result;
            if (!db.objectStoreNames.contains(storeName)) {
                const store = db.createObjectStore(storeName, { keyPath: "eventId" });
                store.createIndex("queuedAt", "queuedAt", { unique: false });
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error ?? new Error("unavailable"));
        request.onblocked = () => reject(new Error("blocked"));
    }).catch(error => {
        databasePromise = undefined;
        throw error;
    });
    return databasePromise;
}

function reportLoss(eventKind, reason, count = 1) {
    const key = `${eventKind}|${reason}`;
    queuedLosses.set(key, Math.min(100000, (queuedLosses.get(key) ?? 0) + count));
}

async function allQueued() {
    let durable = [];
    try {
        const db = await openDatabase();
        durable = await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, "readonly");
            const request = tx.objectStore(storeName).getAll();
            request.onsuccess = () => resolve(request.result ?? []);
            request.onerror = () => reject(request.error);
        });
    } catch {
        durable = [];
    }
    const byId = new Map(durable.map(entry => [entry.eventId, entry]));
    for (const [id, entry] of memoryQueue) if (!byId.has(id)) byId.set(id, entry);
    return [...byId.values()];
}

async function removeEvents(ids) {
    if (!ids.length) return;
    ids.forEach(id => memoryQueue.delete(id));
    try {
        const db = await openDatabase();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, "readwrite");
            const store = tx.objectStore(storeName);
            ids.forEach(id => store.delete(id));
            tx.oncomplete = resolve;
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error);
        });
    } catch { /* memory fallback already removed these IDs */ }
}

async function enforceBounds() {
    const entries = (await allQueued()).sort((a, b) => a.queuedAt - b.queuedAt);
    let bytes = entries.reduce((sum, entry) => sum + (entry.byteLength ?? 0), 0);
    const remove = [];
    const now = Date.now();
    for (const entry of entries) {
        const expired = now - entry.queuedAt > maxAgeMs;
        const overCapacity = entries.length - remove.length > maxEvents || bytes > maxQueueBytes;
        if (!expired && !overCapacity) break;
        remove.push(entry.eventId);
        bytes -= entry.byteLength ?? 0;
        reportLoss(entry.eventKind ?? "session_started", expired ? "queue_age" : "queue_capacity");
    }
    if (remove.length) await removeEvents(remove);
}

async function enqueue(event) {
    const json = JSON.stringify(event);
    const entry = { ...event, queuedAt: Date.now(), byteLength: new TextEncoder().encode(json).byteLength };
    if (entry.byteLength > maxBatchBytes) {
        reportLoss(event.eventKind, "oversize_event");
        return;
    }
    try {
        const db = await openDatabase();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, "readwrite");
            tx.objectStore(storeName).put(entry);
            tx.oncomplete = resolve;
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error);
        });
    } catch {
        if (!memoryQueue.has(entry.eventId)) memoryQueue.set(entry.eventId, entry);
        while (memoryQueue.size > 100) {
            const oldest = memoryQueue.keys().next().value;
            const removed = memoryQueue.get(oldest);
            memoryQueue.delete(oldest);
            reportLoss(removed?.eventKind ?? "session_started", "memory_limit");
        }
        reportLoss(event.eventKind, "storage_unavailable");
    }
    await enforceBounds();
    scheduleSend(0);
}

function makeBootstrap() {
    const pageSessionId = uuid();
    const startedAt = performance.now();
    const event = {
        schemaVersion: 1,
        measurementVersion: 1,
        eventKind: "session_started",
        eventId: uuid(),
        pageSessionId,
        cityActivationId: null,
        sequence: 1,
        occurredAtUtc: new Date().toISOString(),
        pageElapsedMs: 0,
        citySlug: "unknown",
        clientRelease: release(),
        bootstrapStartOffsetMs: 0
    };
    sequence = 1;
    return { pageSessionId, startedAt, event, enabled: experienceEnabled() };
}

export async function startClientExperienceBootstrap() {
    if (window.__transitJazzClientExperience) return window.__transitJazzClientExperience;
    const bootstrap = makeBootstrap();
    window.__transitJazzClientExperience = bootstrap;
    if (bootstrap.enabled) await enqueue(bootstrap.event);
    return bootstrap;
}

export async function report(observation) {
    if (!experienceEnabled()) return;
    const bootstrap = window.__transitJazzClientExperience ?? await startClientExperienceBootstrap();
    if (!bootstrap?.enabled || !observation || typeof observation !== "object") return;
    const event = {
        ...observation,
        schemaVersion: 1,
        measurementVersion: 1,
        eventId: uuid(),
        pageSessionId: bootstrap.pageSessionId,
        cityActivationId: observation.cityActivationId ?? null,
        sequence: ++sequence,
        occurredAtUtc: observation.eventKind === "toast_presented" && typeof observation.occurredAtUtc === "string"
            ? observation.occurredAtUtc
            : new Date().toISOString(),
        pageElapsedMs: Math.max(0, Math.floor(performance.now() - bootstrap.startedAt)),
        citySlug: observation.citySlug || "unknown",
        clientRelease: release()
    };
    await enqueue(event);
}

export function bootstrapIdentity() {
    const bootstrap = window.__transitJazzClientExperience;
    return bootstrap ? { pageSessionId: bootstrap.pageSessionId, enabled: bootstrap.enabled } : null;
}

export function isVisible() {
    return document.visibilityState === "visible";
}

export function startScheduler(callback) {
    const id = nextSchedulerId++;
    let pending = false;
    const timer = setInterval(() => {
        if (pending) return;
        pending = true;
        callback.invokeMethodAsync("OnSchedulerTick").catch(() => {}).finally(() => { pending = false; });
    }, 5000);
    schedulers.set(id, timer);
    return id;
}

export function stopScheduler(id) {
    const timer = schedulers.get(id);
    if (timer === undefined) return;
    clearInterval(timer);
    schedulers.delete(id);
}

export function observeVisibility(callback) {
    const id = nextVisibilityId++;
    const subscriber = typeof callback === "function"
        ? callback
        : visible => { callback.invokeMethodAsync("OnVisibilityChanged", visible).catch(() => {}); };
    visibilityById.set(id, subscriber);
    visibilitySubscribers.add(subscriber);
    if (!visibilityListening) {
        document.addEventListener("visibilitychange", dispatchVisibility);
        visibilityListening = true;
    }
    return id;
}

export function removeVisibility(id) {
    const subscriber = visibilityById.get(id);
    if (!subscriber) return;
    visibilityById.delete(id);
    visibilitySubscribers.delete(subscriber);
    if (visibilitySubscribers.size === 0 && visibilityListening) {
        document.removeEventListener("visibilitychange", dispatchVisibility);
        visibilityListening = false;
    }
}

function dispatchVisibility() {
    const visible = document.visibilityState === "visible";
    visibilitySubscribers.forEach(callback => {
        try { callback(visible); } catch { /* telemetry callbacks are isolated */ }
    });
}

function scheduleSend(delayMs) {
    if (retryTimer || senderPromise) return;
    retryTimer = setTimeout(() => {
        retryTimer = undefined;
        senderPromise = sendLoop().finally(() => { senderPromise = undefined; });
    }, delayMs);
}

function takeLossCounts() {
    const values = [...queuedLosses.entries()].slice(0, 20).map(([key, count]) => {
        const [eventKind, reason] = key.split("|");
        return { eventKind, count, reason };
    });
    values.forEach(item => queuedLosses.delete(`${item.eventKind}|${item.reason}`));
    return values;
}

async function nextBatch() {
    const entries = (await allQueued()).sort((a, b) => a.queuedAt - b.queuedAt);
    const selected = [];
    let bytes = 0;
    for (const entry of entries) {
        if (Date.now() - entry.queuedAt > maxAgeMs) {
            await removeEvents([entry.eventId]);
            reportLoss(entry.eventKind, "queue_age");
            continue;
        }
        const size = new TextEncoder().encode(JSON.stringify(entry)).byteLength;
        if (selected.length && (selected.length >= maxBatchEvents || bytes + size > maxBatchBytes)) break;
        if (size > maxBatchBytes) {
            await removeEvents([entry.eventId]);
            reportLoss(entry.eventKind, "oversize_event");
            continue;
        }
        const { queuedAt: _queuedAt, byteLength: _byteLength, ...event } = entry;
        selected.push(event);
        bytes += size;
    }
    return selected;
}

async function sendBatch(events, losses = []) {
    const body = JSON.stringify({ schemaVersion: 1, events, lostCounts: losses });
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 10_000);
    try {
        const response = await fetch(`${apiBaseUrl()}/api/client-experience/batch`, {
            method: "POST", headers: { "Content-Type": "application/json" }, body, signal: controller.signal,
            cache: "no-store", credentials: "omit"
        });
        if (response.status === 413 && events.length > 1) {
            const middle = Math.ceil(events.length / 2);
            await sendBatch(events.slice(0, middle), losses);
            await sendBatch(events.slice(middle), []);
            return "ok";
        }
        if (response.status === 413 && events.length === 1) {
            await removeEvents([events[0].eventId]);
            reportLoss(events[0].eventKind, "oversize_event");
            return "ok";
        }
        if (response.status === 429) {
            const retryAfter = Number(response.headers.get("Retry-After")) || 1;
            return { retryAfterMs: Math.min(300_000, retryAfter * 1000) };
        }
        if (response.status >= 500) return "retry";
        if (!response.ok) {
            // Malformed envelopes and permanent failures are quarantined to avoid a poison loop.
            events.forEach(event => reportLoss(event.eventKind, "invalid_event"));
            await removeEvents(events.map(event => event.eventId));
            return "ok";
        }
        const result = await response.json();
        const acknowledged = new Set(result.acknowledgedEventIds ?? []);
        const permanentlyRejected = new Set((result.rejectedEvents ?? []).map(item => item.eventId));
        await removeEvents([...acknowledged, ...permanentlyRejected]);
        return "ok";
    } catch {
        return "retry";
    } finally {
        clearTimeout(timeout);
    }
}

async function sendLoop() {
    for (let count = 0; count < 5; count++) {
        const events = await nextBatch();
        if (!events.length) return;
        const result = await sendBatch(events, takeLossCounts());
        if (result === "retry" || typeof result === "object") {
            retryCount++;
            const base = Math.min(300_000, 2000 * 2 ** Math.min(retryCount - 1, 7));
            const delay = typeof result === "object" ? result.retryAfterMs : Math.floor(base * (0.75 + Math.random() * 0.5));
            scheduleSend(delay);
            return;
        }
        retryCount = 0;
    }
    scheduleSend(0);
}
