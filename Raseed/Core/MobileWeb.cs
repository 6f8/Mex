namespace Raseed;

/// <summary>تطبيق الهاتف: صفحة ويب عربية تُفتح من متصفح الهاتف ويمكن إضافتها للشاشة الرئيسية</summary>
public static class MobileWeb
{
    public static string Manifest(string shop) =>
        "{\"name\":\"رصيد — " + (shop ?? "").Replace("\"", "") + "\",\"short_name\":\"رصيد\",\"start_url\":\"/\",\"display\":\"standalone\"," +
        "\"background_color\":\"#f1f5f9\",\"theme_color\":\"#0f2847\",\"dir\":\"rtl\",\"lang\":\"ar\"}";

    public const string Html = """
<!DOCTYPE html>
<html lang="ar" dir="rtl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#0f2847">
<meta name="apple-mobile-web-app-capable" content="yes">
<link rel="manifest" href="/manifest.json">
<title>رصيد</title>
<style>
:root{--p:#0f2847;--a:#0e84d6;--g:#16964a;--r:#d22d2d;--o:#de6e14;--bg:#f1f5f9;--ink:#1e293b;--mut:#64748b}
*{box-sizing:border-box}body{margin:0;font-family:Tahoma,"Segoe UI",sans-serif;background:var(--bg);color:var(--ink);padding-bottom:70px}
header{background:var(--p);color:#fff;padding:14px 16px calc(14px) ;padding-top:calc(14px + env(safe-area-inset-top));display:flex;justify-content:space-between;align-items:center;position:sticky;top:0;z-index:5}
header b{font-size:20px}header small{opacity:.75}
.cards{display:grid;grid-template-columns:1fr 1fr;gap:10px;padding:12px}
.card{background:#fff;border-radius:12px;padding:12px;border-top:4px solid var(--a);box-shadow:0 1px 3px #0001}
.card .t{color:var(--mut);font-size:13px}.card .v{font-size:20px;font-weight:bold;margin-top:6px}
.search{padding:10px 12px}.search input{width:100%;padding:12px;border:1px solid #cbd5e1;border-radius:10px;font-size:16px}
.list{padding:0 12px}.row{background:#fff;border-radius:10px;padding:10px 12px;margin-bottom:8px;box-shadow:0 1px 2px #0001}
.row .n{font-weight:bold}.row .d{color:var(--mut);font-size:13px;margin-top:4px;display:flex;flex-wrap:wrap;gap:10px}
.tag{display:inline-block;padding:2px 8px;border-radius:20px;font-size:12px;background:#e2e8f0}
.pos{color:var(--r)}.neg{color:var(--g)}
nav{position:fixed;bottom:0;left:0;right:0;background:#fff;display:flex;border-top:1px solid #e2e8f0;padding-bottom:env(safe-area-inset-bottom)}
nav button{flex:1;border:0;background:none;padding:10px 2px;font-family:inherit;font-size:12px;color:var(--mut)}
nav button.on{color:var(--a);font-weight:bold}
.empty{text-align:center;color:var(--mut);padding:30px}
#login{padding:30px 20px}#login input{width:100%;padding:12px;font-size:16px;border-radius:10px;border:1px solid #cbd5e1;margin:10px 0}
#login button{width:100%;padding:12px;background:var(--a);color:#fff;border:0;border-radius:10px;font-size:16px}
</style>
</head>
<body>
<header><div><b>رصيد</b><br><small id="shop"></small></div><small id="clock"></small></header>
<div id="login" style="display:none">
  <p>أدخل رمز الربط (Token) الموجود في إعدادات البرنامج على الحاسبة:</p>
  <input id="tok" placeholder="رمز الربط"><button onclick="saveTok()">دخول</button>
</div>
<main id="app"></main>
<nav id="nav">
  <button data-v="home" class="on">الرئيسية</button><button data-v="items">المواد</button>
  <button data-v="parties">الحسابات</button><button data-v="inst">الأقساط</button><button data-v="rep">الصيانة</button>
</nav>
<script>
const qs=new URLSearchParams(location.search);
if(qs.get('token')){localStorage.setItem('raseed_token',qs.get('token'));history.replaceState(null,'','/');}
let TOKEN=localStorage.getItem('raseed_token')||'';
const $=s=>document.querySelector(s), app=$('#app');
const fmt=n=>Number(n||0).toLocaleString('en-US',{maximumFractionDigits:2});
const esc=s=>String(s??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
function saveTok(){TOKEN=$('#tok').value.trim();localStorage.setItem('raseed_token',TOKEN);$('#login').style.display='none';show('home');}
async function api(p,q=''){
  const r=await fetch(`/api/${p}?token=${encodeURIComponent(TOKEN)}&q=${encodeURIComponent(q)}`);
  if(r.status==401){$('#login').style.display='block';app.innerHTML='';throw 'auth';}
  return r.json();
}
function list(rows,fn){return rows.length?`<div class="list">${rows.map(fn).join('')}</div>`:'<div class="empty">لا توجد بيانات</div>';}
function searchBox(v){return `<div class="search"><input id="q" placeholder="بحث..." oninput="clearTimeout(window.t);window.t=setTimeout(()=>show('${v}',this.value),350)"></div><div id="res"></div>`;}
const views={
 async home(){
  const s=await api('summary');$('#shop').textContent=s.shop||'';
  const c=(t,v,col)=>`<div class="card" style="border-color:${col}"><div class="t">${t}</div><div class="v" style="color:${col}">${v}</div></div>`;
  const sales=await api('sales');
  return `<div class="cards">${c('مبيعات اليوم',fmt(s.today_sales),'var(--a)')}${c('النقد في الصناديق',fmt(s.cash),'var(--g)')}
   ${c('ديون لنا',fmt(s.receivables),'var(--o)')}${c('ديون علينا',fmt(s.payables),'var(--r)')}
   ${c('مواد تحت حد الطلب',s.low_stock,'#7c3aed')}${c('صلاحية قريبة',s.expiring,'var(--r)')}
   ${c('أقساط مستحقة',s.due_installments,'var(--o)')}${c('أجهزة بالصيانة',s.repairs_open??'-','var(--a)')}</div>
   <div class="search"><b>فواتير اليوم</b></div>`+list(sales,r=>`<div class="row"><div class="n">#${r.id} — ${esc(r.party)}</div>
   <div class="d"><span>${esc(r.date).slice(11,16)}</span><span>الصافي: ${fmt(r.net)}</span><span>المدفوع: ${fmt(r.paid)}</span></div></div>`);
 },
 async items(q){const rows=await api('items',q);return list(rows,r=>`<div class="row"><div class="n">${esc(r.name)}</div>
   <div class="d"><span class="tag">الرصيد: ${fmt(r.stock)} ${esc(r.unit)}</span><span>مفرد ${fmt(r.price_retail)}</span><span>جملة ${fmt(r.price_wholesale)}</span><span>خاص ${fmt(r.price_special)}</span><span>${esc(r.barcode)}</span></div></div>`);},
 async parties(q){const rows=await api('parties',q);return list(rows,r=>`<div class="row"><div class="n">${esc(r.name)} <span class="tag">${esc(r.kind)}</span></div>
   <div class="d"><a href="tel:${esc(r.phone)}">${esc(r.phone)}</a><b class="${r.balance>0?'pos':'neg'}">${fmt(Math.abs(r.balance))} ${r.balance>0?'(عليه)':r.balance<0?'(له)':''}</b></div></div>`);},
 async inst(q){const rows=await api('installments',q);const today=new Date().toISOString().slice(0,10);
   return list(rows,r=>`<div class="row"><div class="n">${esc(r.name)} — القسط ${r.seq}</div><div class="d"><span class="${r.due_date<=today?'pos':''}">الاستحقاق: ${esc(r.due_date)}</span>
   <span>المتبقي: ${fmt(r.amount-r.paid)}</span><a href="https://wa.me/${esc((r.phone||'').replace(/^0/,'964'))}">واتساب</a></div></div>`);},
 async rep(q){const rows=await api('repairs',q);return list(rows,r=>`<div class="row"><div class="n">#${r.id} — ${esc(r.device)} <span class="tag">${esc(r.status)}</span></div>
   <div class="d"><span>${esc(r.customer)}</span><a href="tel:${esc(r.phone)}">${esc(r.phone)}</a><span>${esc(r.fault)}</span><span>التقديري: ${fmt(r.estimate)}</span><span>المدفوع: ${fmt(r.paid)}</span></div></div>`);}
};
let cur='home';
async function show(v,q){
  cur=v;document.querySelectorAll('nav button').forEach(b=>b.classList.toggle('on',b.dataset.v==v));
  try{
    if(v=='home'){app.innerHTML=await views.home();return;}
    if(q===undefined){app.innerHTML=searchBox(v);}
    $('#res').innerHTML=await views[v](q||'');
  }catch(e){if(e!=='auth')app.innerHTML='<div class="empty">تعذر الاتصال بالبرنامج. تأكد أن الحاسبة تعمل وعلى نفس الشبكة.</div>';}
}
document.querySelectorAll('nav button').forEach(b=>b.onclick=()=>show(b.dataset.v));
setInterval(()=>$('#clock').textContent=new Date().toLocaleTimeString('en-GB',{hour:'2-digit',minute:'2-digit'}),1000);
if(!TOKEN)$('#login').style.display='block';else show('home');
</script>
</body>
</html>
""";
}
