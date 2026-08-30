import { chromium } from 'playwright';
const OUT  = '/Users/jacek/Projects/Brekhof/zrzuty-mordeczka';
const BASE = process.env.PW_BASE || 'https://mordeczka.fleetman.com.pl';
const bledy = [], siec = [];

const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const p = await ctx.newPage();
p.on('console', m => { if (m.type() === 'error') bledy.push(m.text().slice(0,180)); });
p.on('pageerror', e => bledy.push('PAGEERROR ' + String(e).slice(0,180)));
p.on('response', r => { if (r.status() >= 400) siec.push(`${r.status()} ${r.url().slice(0,110)}`); });

await p.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
await p.waitForURL(/LoginPage/, { timeout: 30000 }).catch(()=>{});
await p.waitForTimeout(3000);
await p.screenshot({ path: `${OUT}/01-logowanie.png` });

await p.locator('input[type=text]').first().fill('Admin');
await p.locator('button:has-text("Zaloguj")').first().click();
await p.waitForTimeout(9000);
console.log('PO LOGOWANIU URL:', p.url());
console.log('TYTUL:', await p.title());
await p.screenshot({ path: `${OUT}/02-po-logowaniu.png`, fullPage: true });

const s = await p.evaluate(() => {
  const kafle = [...document.querySelectorAll('.hub-card')].map(c => c.innerText.trim().replace(/\s+/g,' '));
  const sekcje = [...document.querySelectorAll('.navigation-hub h5')].map(h => h.innerText.trim().replace(/\s+/g,' '));
  const img = [...document.querySelectorAll('img')];
  const nav = [...document.querySelectorAll('[role=treeitem]')].map(a=>a.innerText.trim()).filter(Boolean).slice(0,25);
  return { sekcje, kafle, obrazy: img.length, zepsute: img.filter(i=>i.naturalWidth===0).map(i=>i.src.slice(-55)), nav,
           tekst: document.body.innerText.slice(0,300).replace(/\n+/g,' | ') };
});
console.log('SEKCJE:', JSON.stringify(s.sekcje));
console.log('KAFELKI:', s.kafle.length, JSON.stringify(s.kafle));
console.log('OBRAZY:', s.obrazy, '| ZEPSUTE:', s.zepsute.length, JSON.stringify(s.zepsute));
console.log('NAWIGACJA:', JSON.stringify(s.nav));
console.log('TEKST:', s.tekst);
console.log('BLEDY KONSOLI:', bledy.length); bledy.slice(0,10).forEach(e=>console.log('  ',e));
console.log('HTTP>=400:', siec.length); [...new Set(siec)].slice(0,10).forEach(e=>console.log('  ',e));
await b.close();
