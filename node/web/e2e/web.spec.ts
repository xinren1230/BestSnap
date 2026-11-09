import { expect, test } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const fixtures = path.resolve(dir, "../../tests/fixtures");

test.describe.configure({ mode: "serial" });

test("upload OCR fixture → Chinese/English text", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("video")).toBeVisible();
  await page.setInputFiles('[data-testid="file"]', path.join(fixtures, "ocr.png"));
  await expect(page.locator("#cv")).toBeVisible({ timeout: 15_000 });
  await page.locator("#more").click();
  await page.getByTestId("ocr").click();
  await expect(page.getByTestId("result")).toContainText(/Windows/, { timeout: 180_000 });
  await expect(page.getByTestId("result")).toContainText(/11|升级|系统|Hello|World|SnapForge/);
});

test("upload formula fixture → LaTeX", async ({ page }) => {
  await page.goto("/");
  await page.setInputFiles('[data-testid="file"]', path.join(fixtures, "formula.png"));
  await expect(page.locator("#cv")).toBeVisible({ timeout: 15_000 });
  await page.locator("#more").click();
  await page.getByTestId("latex-btn").click();
  await expect(page.getByTestId("latex")).toContainText(/frac|sqrt|pm|[xX]|b/, { timeout: 180_000 });
});

test("upload QR fixture → example.com URL", async ({ page }) => {
  await page.goto("/");
  await page.setInputFiles('[data-testid="file"]', path.join(fixtures, "qr.png"));
  await expect(page.locator("#cv")).toBeVisible({ timeout: 15_000 });
  await page.locator("#more").click();
  await page.getByTestId("qr").click();
  await expect(page.getByTestId("result")).toContainText(/example.com\/snapforge/, { timeout: 30_000 });
  await page.screenshot({ path: "/opt/cursor/artifacts/snapforge-web.png", fullPage: true });
  await page.screenshot({ path: "test-results/snapforge-web.png", fullPage: true });
});
