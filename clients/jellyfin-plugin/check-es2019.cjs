// Checks that the scripts in the plugin's pages are plain ES2019, which the webOS 6 web engine (Chromium 79 era) runs.
// Usage (from the repository root, after `yarn install`): node clients/jellyfin-plugin/check-es2019.cjs
const fs = require('fs');
const path = require('path');
const acorn = require(require.resolve('acorn', { paths: [path.join(__dirname, '..', '..')] }));

const root = path.join(__dirname, 'src', 'Jellyfin.Plugin.Cinnabarr');
const files = [];

(function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory() && entry.name !== 'bin' && entry.name !== 'obj') walk(full);
    else if (/\.(html|js)$/.test(entry.name)) files.push(full);
  }
})(root);

let failed = 0;

for (const file of files) {
  const text = fs.readFileSync(file, 'utf8');
  const scripts = file.endsWith('.js')
    ? [text]
    : [...text.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)].map(m => m[1]);

  scripts.forEach((code, i) => {
    try {
      acorn.parse(code, { ecmaVersion: 2019, sourceType: 'script' });
    } catch (e) {
      failed++;
      console.error(`${path.relative(process.cwd(), file)} script ${i + 1}: ${e.message}`);
    }
  });
}

if (failed) {
  process.exit(1);
}

console.log(`ES2019 check passed (${files.length} files).`);
