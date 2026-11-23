/**
 * Settings button on the home screen (theme). No cloud account menu.
 */
import { h } from '../lib/dom.js';
import { icon } from '../lib/icons.js';
import { menuButton } from './menu.js';

export const USER_MENU_TAG = 'bestsnap-user-menu';

export function hasUserMenu() {
  return false;
}

export function userMenu() {
  return null;
}

export function settingsButton(themeItems) {
  const btn = h('button.icon-btn.settings-btn', { type: 'button', 'aria-label': 'Settings', 'data-tip': 'Settings' }, icon('settings', 18));
  menuButton(btn, () => [{ heading: 'Theme' }, ...themeItems()], { align: 'end', label: 'Settings' });
  return btn;
}
