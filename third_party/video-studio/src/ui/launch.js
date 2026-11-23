/**
 * Host / desktop hand-over helpers. Project files exported from Home are ZIP bundles.
 */
export const isBundleName = (name) => /\.zip$/i.test(name || '');
