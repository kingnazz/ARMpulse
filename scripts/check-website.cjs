"use strict";

// Exercise the real site under the same /ARMpulse/ path used by GitHub Pages.
// npm install --prefix /tmp/website-check playwright@1.61.1
// NODE_PATH=/tmp/website-check/node_modules node scripts/check-website.cjs
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const { chromium } = require("playwright");

const root = path.resolve(__dirname, "../website");
const types = { ".html": "text/html", ".css": "text/css", ".js": "text/javascript", ".png": "image/png", ".svg": "image/svg+xml" };
const server = http.createServer(async (req, res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, "http://localhost").pathname);
    if (!pathname.startsWith("/ARMpulse/")) throw new Error("Outside project path");
    const file = path.resolve(root, pathname.slice("/ARMpulse/".length) || "index.html");
    if (!file.startsWith(root + path.sep)) throw new Error("Outside website");
    const body = await fs.readFile(file);
    res.writeHead(200, { "Content-Type": types[path.extname(file)] || "application/octet-stream" });
    res.end(body);
  } catch {
    res.writeHead(404);
    res.end("Not found");
  }
});

(async () => {
  let browser;
  try {
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    const base = `http://127.0.0.1:${server.address().port}/ARMpulse/`;
    browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: "reduce" });
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
    page.on("response", response => { if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`); });
    await page.goto(base, { waitUntil: "networkidle" });
    assert.match(await page.title(), /ARMpulse/);

    const references = await page.locator("[href], [src]").evaluateAll(elements => elements.flatMap(element =>
      [element.getAttribute("href"), element.getAttribute("src")].filter(Boolean)));
    for (const reference of new Set(references)) {
      const url = new URL(reference, base);
      if (url.origin !== new URL(base).origin) continue;
      if (url.hash) assert.equal(await page.locator(`[id="${url.hash.slice(1)}"]`).count(), 1, `Missing anchor: ${reference}`);
      assert.ok((await page.request.get(url.href)).ok(), `Missing local file: ${reference}`);
    }
    const downloads = await page.locator('a[href*="releases/download/"]').evaluateAll(links => links.map(link => link.href));
    assert.equal(downloads.filter(url => url.endsWith("/ARMpulse-win-arm64.zip")).length, 2);
    assert.equal(downloads.filter(url => url.endsWith("/ARMpulse-win-x64.zip")).length, 1);

    for (const name of ["processes", "memory", "history", "settings", "overview"]) {
      await page.locator(`#tab-${name}`).click();
      await page.waitForFunction(() => document.getElementById("app-screenshot").complete && document.getElementById("app-screenshot").naturalWidth > 0);
      assert.equal(await page.locator("#app-screenshot").getAttribute("src"), `assets/${name}.png`);
      assert.equal(await page.locator(`#tab-${name}`).getAttribute("aria-selected"), "true");
      assert.equal(await page.locator('[role="tab"][aria-selected="true"]').count(), 1);
    }
    await page.locator("#tab-overview").focus();
    for (const [key, expected] of [["ArrowLeft", "settings"], ["ArrowRight", "overview"], ["End", "settings"], ["Home", "overview"]]) {
      await page.keyboard.press(key);
      assert.ok(await page.locator(`#tab-${expected}`).evaluate(element => document.activeElement === element));
      assert.equal(await page.locator(`#tab-${expected}`).getAttribute("aria-selected"), "true");
    }
    for (const summary of await page.locator("summary").all()) {
      await summary.focus();
      await page.keyboard.press("Enter");
      assert.ok(await summary.evaluate(element => element.parentElement.open));
      await page.keyboard.press("Enter");
    }
    for (const width of [320, 360, 390, 768, 1024, 1440, 1920]) {
      await page.setViewportSize({ width, height: 1000 });
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Horizontal overflow at ${width}px`);
      if ([390, 1440].includes(width)) {
        await page.screenshot({ path: path.join(process.env.RUNNER_TEMP || os.tmpdir(), `armpulse-site-${width}.png`), fullPage: true });
      }
    }
    // Links remain usable with JavaScript disabled, including all five screenshots.
    const fallback = await browser.newPage({ javaScriptEnabled: false });
    await fallback.goto(base);
    await fallback.locator("#tab-processes").click();
    assert.ok(fallback.url().endsWith("/assets/processes.png"));
    await page.goto(`${base}404.html`, { waitUntil: "networkidle" });
    await page.getByRole("link", { name: "Go to ARMpulse" }).click();
    assert.equal(page.url(), base);
    assert.deepEqual(errors, []);
    console.log("Website passed: local links, downloads, 5 screenshot tabs, keyboard controls, FAQs, 7 responsive widths, no-JS fallback, 404 page, and browser errors.");
  } finally {
    if (browser) await browser.close();
    server.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
