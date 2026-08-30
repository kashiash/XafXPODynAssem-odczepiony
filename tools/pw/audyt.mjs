// Audyt aplikacji: logowanie, przeglad pulpitu, wychwycenie bledow konsoli i sieci.
import { chromium } from 'playwright';

const OUT  = process.env.PW_OUT  || '/Users/jacek/Projects/Brekhof/zrzuty-mordeczka';
const BASE = process.env.PW_BASE || 'https://mordeczka.fleetman.com.pl';

const bledyKonsoli = [];
const bledySieci = [];

const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const page = await ctx.newPage();

page.on('console', m => { if (m.type() === 'error') bledyKonsoli.push(m.text().slice(0, 200)); });
page.on('pageerror', e => bledyKonsoli.push('PAGEERROR: ' + String(e).slice(0, 200)));
page.on('response', r => { if (r.status() >= 400) bledySieci.push(`${r.status()} ${r.url().slice(0, 120)}`); });

await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
console.log('URL startowy:', page.url());
console.log('tytul:', await page.title());
await page.screenshot({ path: `${OUT}/a1-start.png` });

if (/login/i.test(page.url())) {
  await page.locator('input[type=text]').first().fill('Admin');
  const btn = page.locator('button[data-action-name="Zaloguj się"], button:has-text("Zaloguj")').first();
  await btn.click();
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(4000);
}
console.log('po logowaniu:', page.url(), '|', await page.title());
await page.screenshot({ path: `${OUT}/a2-po-logowaniu.png`, fullPage: true });

const stan = await page.evaluate(() => {
  const kafle = [...document.querySelectorAll('.hub-card')].map(c => c.innerText.trim().replace(/\s+/g, ' '));
  const naglowki = [...document.querySelectorAll('.navigation-hub h5')].map(h => h.innerText.trim());
  const obrazy = [...document.querySelectorAll('img')].map(i => ({ src: (i.src||'').slice(-60), ok: i.naturalWidth > 0 }));
  const nav = [...document.querySelectorAll('[role=treeitem], .dxbl-navigation a')].map(a => a.innerText.trim()).filter(Boolean).slice(0, 20);
  return { kafle, naglowki, zepsuteObrazy: obrazy.filter(o => !o.ok), liczbaObrazow: obrazy.length, nav };
});
console.log('sekcje:', JSON.stringify(stan.naglowki));
console.log('kafelkow:', stan.kafle.length, JSON.stringify(stan.kafle));
console.log('obrazow:', stan.liczbaObrazow, '| zepsutych:', stan.zepsuteObrazy.length, JSON.stringify(stan.zepsuteObrazy));
console.log('nawigacja:', JSON.stringify(stan.nav));

console.log('--- bledy konsoli:', bledyKonsoli.length);
bledyKonsoli.slice(0, 12).forEach(e => console.log('   ', e));
console.log('--- odpowiedzi >=400:', bledySieci.length);
[...new Set(bledySieci)].slice(0, 12).forEach(e => console.log('   ', e));

await b.close();
