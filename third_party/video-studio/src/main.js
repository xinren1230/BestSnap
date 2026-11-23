import './styles/fonts.js';
import './styles/tokens.css';
import './styles/base.css';
import './styles/editor.css';
import './styles/timeline.css';
import './styles/home.css';
import './styles/responsive.css';
import { h } from './lib/dom.js';
import { icon, appIcon } from './lib/icons.js';
import { applyTheme } from './ui/prefs.js';
import { Library } from './storage/library.js';
import { opfsBackend, opfsAvailable, memoryBackend } from './storage/backends.js';
import { Home } from './ui/home.js';
import { importProjectBundle } from './storage/bundle.js';
import { toast, confirmDialog } from './ui/dialog.js';
import { loadDsp } from './media/dsp.js';
import { confirmLeave, finishUploads } from './ui/leave.js';
import { isBundleName } from './ui/launch.js';
import { fetchOpenFile, isHosted } from './host/bridge.js';

const root = document.getElementById('app');
applyTheme();
loadDsp();

let library = null;
let account = null;
let view = null;
// Set once the user chose to leave the page (signing out, closing the window): no more questions.
let leaving = false;
// The project being opened, if any: files handed over by the host wait for it.
let navigating = Promise.resolve();
// Only the latest navigation may replace the screen; late reads still release their claims.
let navigation = 0;

function navigate(run) {
  const token = ++navigation;
  const job = run(() => navigation === token);
  navigating = job.catch(() => {});
  return job;
}

function loading(text) {
  root.setAttribute('aria-busy', 'true');
  root.replaceChildren(h('div.splash', { role: 'status' }, appIcon(56), h('div.spinner'), h('p', text)));
}

function errorScreen(title, message, retry, retryLabel = 'Try again') {
  root.removeAttribute('aria-busy');
  root.replaceChildren(h('main#main.splash', icon('cloudOff', 32), h('h1.splash-title', title), h('p.splash-sub', message), retry ? h('button.btn.primary', { type: 'button', onclick: retry }, retryLabel) : null));
}

/** A part of the app failed to load: say so, with a reload (which also picks up a new version). */
function cantLoad(err, what) {
  console.error(err);
  const offline = typeof navigator !== 'undefined' && navigator.onLine === false;
  toast(offline ? `Couldn't open ${what} while offline. Connect and try again.` : `Couldn't open ${what}. Reload the page and try again.`, {
    kind: 'error',
    duration: 12000,
    action: { label: 'Reload', onClick: () => location.reload() },
  });
}

function showHome() {
  navigation++;
  view?.dispose?.();
  if (/#(project|review)=/.test(location.hash)) history.replaceState(null, '', `${location.pathname}${location.search}`);
  view = new Home(root, {
    library,
    account,
    onOpen: (id) => openProject(id),
    onCreate: (opts) => createProject(opts),
    onImportFiles: (files) => createProject({ ratio: 'original' }, files),
  });
}

function openProject(id, files) {
  return navigate((current) => openProjectNow(id, files, current));
}

async function openProjectNow(id, files, current) {
  // Asked for while an editor is open (an old "Open" button on a Home message): its last
  // edits are saved first, and the user can stay when they cannot be.
  if (view?.prepareToLeave && !view._disposed && !(await view.prepareToLeave())) return;
  if (!current()) return;
  view?.dispose?.();
  view = null;
  loading('Opening project...');
  let project;
  let claim;
  try {
    // Read once claimed: something written into the file meanwhile stays (see openForEditing).
    ({ project, claim } = await library.openForEditing(id));
  } catch (err) {
    if (!current()) return;
    toast(err?.message || "Couldn't open the project.", { kind: 'error' });
    showHome();
    return;
  }
  if (!current()) {
    claim?.release();
    return;
  }
  if (project.deletedAt) {
    toast(`${project.name} is in the Trash. Restore it to open it.`, { kind: 'error' });
    history.replaceState(null, '', '#trash');
    showHome();
    return;
  }
  // Open in another tab too: edits in both would overwrite each other, so ask first.
  if (!claim.held) {
    const ok = await confirmDialog({
      title: 'Project open in another tab',
      message: `${project.name} is already open in another tab or window. Changes made in both places overwrite each other. Open it here anyway?`,
      confirm: 'Open anyway',
      cancel: 'Go back',
    });
    if (!current()) {
      claim.release();
      return;
    }
    if (!ok) {
      claim.release();
      showHome();
      return;
    }
  }
  try {
    const { Editor } = await import('./ui/editor.js');
    if (!current()) {
      claim.release();
      return;
    }
    view = new Editor(root, {
      library,
      project,
      claim,
      account,
      onExit: () => showHome(),
      onNewProject: () => createProject({ ratio: 'original' }),
      onOpenProject: (other) => openProject(other),
    });
  } catch (err) {
    // The editor's code didn't load (offline, or the app was updated meanwhile) or couldn't start:
    // back to Home rather than "Opening project..." for good, and let another tab open it.
    claim.release();
    if (!current()) return;
    view = null;
    cantLoad(err, 'the editor');
    showHome();
    return;
  }
  history.replaceState(null, '', `#project=${encodeURIComponent(id)}`);
  if (files?.length) {
    const editor = view;
    const { ctx } = editor;
    const assets = await ctx.actions.importFiles(files);
    // A later navigation may have been declined: finish while this editor still owns the screen.
    if (view !== editor || editor._disposed) return;
    // One after another, as one undo step (the first picture or video sets the project's shape).
    ctx.actions.addAssets(assets, { start: ctx.store.duration });
    ctx.store.seek(0);
  }
}

function createProject(opts = {}, files) {
  return navigate((current) => createProjectNow(opts, files, current));
}

async function createProjectNow(opts, files, current) {
  loading('Creating project...');
  try {
    const project = await library.createProject({ name: await library.nextProjectName(), ratio: opts.ratio || 'original' });
    if (!current()) return;
    await openProject(project.id, files);
  } catch (err) {
    if (!current()) return;
    console.error(err);
    toast("Couldn't create a project.", { kind: 'error' });
    showHome();
  }
}

/** Until whatever is being opened (a project, the editor) is on screen. */
async function settled() {
  let current;
  do {
    current = navigating;
    await current;
  } while (current !== navigating);
}

/**
 * Files handed over by the host (a recording just saved). Project files
 * open as projects; media dragged onto an open project goes into its media,
 * otherwise it starts a new project, as files dropped on Home do.
 */
async function openHandedOver(files, { dropped }) {
  await settled();
  const bundles = files.filter((f) => isBundleName(f.name));
  const media = files.filter((f) => !bundles.includes(f));
  const editor = view?.ctx && !view._disposed ? view : null;
  if (!bundles.length && dropped && editor) {
    const assets = await editor.ctx.actions.importFiles(media);
    if (assets.length) toast(assets.length === 1 ? `${assets[0].name} added to the project's media.` : `${assets.length} files added to the project's media.`, { kind: 'info' });
    return;
  }
  // The open project is saved first (with its cover, as leaving it does); the user can stay when it cannot be.
  const open = view;
  if (open?.prepareToLeave && !open._disposed) {
    if (!(await open.prepareToLeave())) return;
    await open.saveCover?.();
    if (open.store?.dirty && !(await open.prepareToLeave())) return;
  }
  // Something else was opened meanwhile: it stays.
  if (view !== open) return;
  view?.dispose?.();
  view = null;
  if (!bundles.length) {
    await createProject({ ratio: 'original' }, media);
    return;
  }
  loading(bundles.length === 1 ? 'Opening project file...' : 'Opening project files...');
  const opened = [];
  for (const file of bundles) {
    try {
      opened.push((await importProjectBundle(library, file)).project);
    } catch (err) {
      console.error(err);
      toast(err?.message || `Couldn't open ${file.name}.`, { kind: 'error' });
    }
  }
  // Both at once: the project files are added, then the media opens in a new project.
  if (media.length) await createProject({ ratio: 'original' }, media);
  else if (opened.length) await openProject(opened[0].id);
  else showHome();
}

function watchWindowClose() {}

/**
 * The page is about to go away: save the open project and wait for media
 * uploads, asking first when something couldn't be stored. Resolves false
 * to stay.
 */
async function leavePage() {
  if (leaving) return true;
  if (view?.prepareToLeave) {
    if (!(await view.prepareToLeave({ waitForUploads: true }))) return false;
  } else {
    const media = await finishUploads(library);
    if (media && !(await confirmLeave(library, { media }))) return false;
  }
  leaving = true;
  if (view) view.pageLeaving = true;
  return true;
}

function startLocal(env) {
  library = new Library(opfsAvailable() ? opfsBackend() : memoryBackend());
  account = {
    user: null,
    env,
    picture: null,
    onSignIn: null,
    onSessionChange: null,
  };
  watchWindowClose();
  route();
}

function route() {
  const m = /#project=([^&]+)/.exec(location.hash);
  if (m) {
    let id;
    try {
      id = decodeURIComponent(m[1]);
    } catch {
      errorScreen('This project link does not work', 'The project address is incomplete or damaged. Open the project from your projects instead.', showHome, 'Back to projects');
      return;
    }
    openProject(id);
  } else showHome();
}

async function boot() {
  startLocal(isHosted() ? 'app' : 'web');
  try {
    const file = await fetchOpenFile();
    if (file) await openHandedOver([file], { dropped: false });
  } catch (err) {
    console.error(err);
    toast(err?.message || 'Could not open the recording.', { kind: 'error' });
  }
}

// Media still uploading (or waiting to be tried again) after leaving the editor: warn before the page closes.
window.addEventListener('beforeunload', (e) => {
  if (leaving || !library?.unsavedMedia) return;
  e.preventDefault();
  e.returnValue = '';
});

window.addEventListener('unhandledrejection', (e) => {
  console.error(e.reason);
});

if (typeof window !== 'undefined' && !window.__VIDEO_STUDIO_TEST__) boot();

// Development hook: drive the app from the console or browser automation.
//   await vs.importDev(['clip-a.mp4', 'music.mp3'], { add: true })
if (import.meta.env.DEV) {
  window.vs = {
    get view() {
      return view;
    },
    get ctx() {
      return view?.ctx;
    },
    // What signing out and closing the the original cloud host window do before the page goes.
    leavePage: () => leavePage(),
    async importDev(names, { add = false } = {}) {
      const files = [];
      for (const n of names) {
        const blob = await (await fetch(`/.dev-media/${n}`)).blob();
        files.push(new File([blob], n, { type: blob.type }));
      }
      return view.ctx.actions.importFiles(files, { addToTimeline: add });
    },
  };
}
