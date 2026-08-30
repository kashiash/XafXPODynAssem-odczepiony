import { chromium } from 'playwright';
const OUT='/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';
const BASE='https://localhost:5031';
const b=await chromium.launch({headless:true});
const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1500,height:1000}});
const page=await ctx.newPage();
await page.goto(`${BASE}/`,{waitUntil:'networkidle'});
if(page.url().includes('LoginPage')){await page.locator('input[type=text]').first().fill('Admin');await page.locator('button[data-action-name="Zaloguj się"]').first().click();await page.waitForLoadState('networkidle');}
await page.goto(`${BASE}/Faktura_ListView`,{waitUntil:'networkidle'});
await page.waitForTimeout(2500);
await page.locator('tr',{hasText:'FV/2026/08/001'}).first().click();
await page.waitForTimeout(1200);
const html = await page.evaluate(()=>{const e=document.querySelector('[data-action-name="Pokaż na raporcie"]');return e? e.parentElement.parentElement.outerHTML.slice(0,3000):'none';});
console.log(html);
await b.close();
