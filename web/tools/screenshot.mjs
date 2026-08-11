// Screenshot every admin screen through a Chromium-based browser over CDP.
//
// Launch the browser first, in a private window with a throwaway profile:
//   brave.exe --incognito --remote-debugging-port=9222 --user-data-dir=<tmp>
// then: node tools/screenshot.mjs <output-prefix>
import { chromium } from 'playwright-core'

const [, , outPrefix] = process.argv
const base = 'http://localhost:19199'

const browser = await chromium.connectOverCDP('http://127.0.0.1:9222')
const context = browser.contexts()[0]
const page = context.pages()[0] ?? (await context.newPage())

const errors = []
page.on('console', (m) => {
  if (m.type() === 'error') errors.push(m.text())
})
page.on('pageerror', (e) => errors.push(String(e)))

async function shoot(path, name, prep) {
  await page.goto(base + path, { waitUntil: 'networkidle' })
  if (prep) await prep()
  await page.waitForTimeout(400)
  await page.screenshot({ path: `${outPrefix}-${name}.png`, fullPage: true })
  console.log(`shot ${name}: ${await page.title()}`)
}

await page.setViewportSize({ width: 1280, height: 900 })

await shoot('/', 'servers')
await shoot('/settings', 'settings')
await shoot('/logs', 'logs')
await shoot('/about', 'about')
await shoot('/servers/new', 'editor')

// The rendered text proves React mounted rather than just serving HTML.
await page.goto(base + '/', { waitUntil: 'networkidle' })
const bodyText = await page.locator('body').innerText()
console.log('--- rendered text (first 600 chars) ---')
console.log(bodyText.slice(0, 600))
console.log('--- console errors ---')
console.log(errors.length ? errors.join('\n') : '(none)')

await browser.close()
