import { chromium } from 'playwright';
const OUT='/Users/jacek/Projects/Brekhof/zrzuty-mordeczka';
const BASE='https://mordeczka.fleetman.com.pl';
const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport:{width:1500,height:1100} });
const p = await ctx.newPage();
await p.goto(`${BASE}/`, { waitUntil:'domcontentloaded' });
await p.waitForURL(/LoginPage/,{timeout:30000}).catch(()=>{});
await p.waitForTimeout(2500);
await p.locator('input[type=text]').first().fill('Admin');
await p.locator('button:has-text("Zaloguj")').first().click();
await p.waitForTimeout(8000);

const zakladki = await p.evaluate(() => [...document.querySelectorAll('[role=tab]')].map(t=>t.innerText.trim().replace(/\s+/g,' ')));
console.log('ZAKLADKI:', JSON.stringify(zakladki));
console.log('URL po logowaniu:', p.url());

// klik w zakladke Pulpit
const tab = p.locator('[role=tab]:has-text("Pulpit")').first();
if (await tab.count()) { await tab.click(); await p.waitForTimeout(4000); }
console.log('URL po kliknieciu Pulpit:', p.url());
const s = await p.evaluate(() => ({
  sekcje: [...document.querySelectorAll('.navigation-hub h5')].map(h=>h.innerText.trim().replace(/\s+/g,' ')),
  kafle:  [...document.querySelectorAll('.hub-card')].map(c=>c.innerText.trim().replace(/\s+/g,' ')),
  zepsute:[...document.querySelectorAll('img')].filter(i=>i.naturalWidth===0).length,
}));
console.log('SEKCJE:', JSON.stringify(s.sekcje));
console.log('KAFELKI:', s.kafle.length, JSON.stringify(s.kafle));
console.log('ZEPSUTE OBRAZY:', s.zepsute);
await p.screenshot({ path:`${OUT}/03-pulpit.png`, fullPage:true });
await b.close();
