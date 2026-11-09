import { contextBridge, ipcRenderer } from "electron";

contextBridge.exposeInMainWorld("sf", {
  invoke: (channel: string, ...args: unknown[]) => ipcRenderer.invoke(channel, ...args),
  on: (channel: string, fn: (...args: unknown[]) => void) => {
    const listener = (_: unknown, ...args: unknown[]) => fn(...args);
    ipcRenderer.on(channel, listener);
    return () => ipcRenderer.removeListener(channel, listener);
  },
});
