/** LEGACY MIGRATION: IndexedDB name from the SnapForge web app. Renaming would drop existing history. */
const DB = "snapforge-web";
const STORE = "history";

export interface HistItem {
  id: number;
  created: number;
  w: number;
  h: number;
  png: Blob;
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB, 1);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE, { keyPath: "id", autoIncrement: true });
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

export async function addHistory(png: Blob, w: number, h: number): Promise<number> {
  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, "readwrite");
    const req = tx.objectStore(STORE).add({ created: Date.now(), w, h, png });
    req.onsuccess = () => resolve(Number(req.result));
    req.onerror = () => reject(req.error);
  });
}

export async function listHistory(limit = 40): Promise<HistItem[]> {
  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, "readonly");
    const req = tx.objectStore(STORE).openCursor(null, "prev");
    const out: HistItem[] = [];
    req.onsuccess = () => {
      const c = req.result;
      if (!c || out.length >= limit) {
        resolve(out);
        return;
      }
      out.push(c.value as HistItem);
      c.continue();
    };
    req.onerror = () => reject(req.error);
  });
}

export async function getHistory(id: number): Promise<HistItem | undefined> {
  const db = await openDb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, "readonly");
    const req = tx.objectStore(STORE).get(id);
    req.onsuccess = () => resolve(req.result as HistItem | undefined);
    req.onerror = () => reject(req.error);
  });
}
