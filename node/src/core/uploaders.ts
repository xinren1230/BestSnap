import fs from "node:fs";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const exec = promisify(execFile);

export async function uploadFile(
  localFile: string,
  remoteName: string,
  target: { kind: string; url: string; user?: string; secret?: string; publicUrlPrefix?: string },
): Promise<string> {
  if (target.kind === "folder") {
    const destDir = target.url;
    fs.mkdirSync(destDir, { recursive: true });
    const dest = path.join(destDir, remoteName);
    fs.copyFileSync(localFile, dest);
    return dest;
  }
  if (target.kind === "ftp" || target.kind === "ftps") {
    const url = target.url.replace(/\/?$/, "/") + remoteName;
    const args = ["-sS", "-T", localFile, url];
    if (target.user) args.unshift("-u", `${target.user}:${target.secret ?? ""}`);
    await exec("curl", args, { timeout: 60000 });
    return target.publicUrlPrefix ? target.publicUrlPrefix.replace(/\/?$/, "/") + remoteName : url;
  }
  if (target.kind === "webdav") {
    const url = target.url.replace(/\/?$/, "/") + remoteName;
    const args = ["-sS", "-T", localFile, url];
    if (target.user) args.unshift("-u", `${target.user}:${target.secret ?? ""}`);
    await exec("curl", args, { timeout: 60000 });
    return target.publicUrlPrefix ? target.publicUrlPrefix.replace(/\/?$/, "/") + remoteName : url;
  }
  throw new Error("unknown upload kind: " + target.kind);
}
