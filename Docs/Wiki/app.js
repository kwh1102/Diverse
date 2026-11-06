/* A local catalog snapshot. No network requests, framework, analytics or game runtime. */
(() => {
  'use strict';
  const data = window.DIVERSE_WIKI;
  const $ = id => document.getElementById(id);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const url = path => '../../' + path.replace(/\\/g, '/').split('/').map(encodeURIComponent).join('/');
  if (!data) { $('list').textContent = 'catalog.js가 없습니다. Tools/export_wiki.py로 문서 데이터를 내보내 주세요.'; return; }
  let category = '', query = '', family = '', tier = '';
  const categories = [...new Set(data.entries.map(e => e.category))];
  const thumbs = e => `<span class="thumb">${e.image ? `<img src="${url(e.image)}" alt="" loading="lazy">` : e.tier ? '◇' : e.shape ? '▦' : '✦'}</span>`;
  const nav = () => {
    $('navigation').innerHTML = ['', ...categories].map(c => `<button type="button" data-category="${esc(c)}" class="${category === c ? 'active' : ''}"><span>${esc(c || '전체 목록')}</span><span class="count">${data.entries.filter(e => !c || e.category === c).length}</span></button>`).join('');
  };
  function render() {
    nav();
    $('list-title').textContent = category || '모든 항목';
    const rows = data.entries.filter(e => (!category || e.category === category) && (!family || e.family === family) && (!tier || e.tier === Number(tier)) && (!query || `${e.name} ${e.id} ${e.summary} ${e.family || ''} ${e.source} ${e.raw || ''}`.toLocaleLowerCase().includes(query)));
    $('results').textContent = `${rows.length.toLocaleString()}개 항목 · 항목을 누르면 능력·원본 필드를 확인할 수 있습니다.`;
    $('list').innerHTML = rows.length ? rows.map(e => `<button type="button" class="entry" data-id="${esc(e.id)}">${thumbs(e)}<div class="entry-main"><p class="eyebrow">${esc(e.category)}${e.tier ? `<span class="badge">T${e.tier}</span>` : ''}</p><h3>${esc(e.name)}</h3><p>${esc(e.summary)}</p><code>${esc(e.id)}</code></div></button>`).join('') : '<p class="empty">검색 결과가 없습니다. 필터를 해제하거나 다른 단어로 검색해 주세요.</p>';
  }
  function shapeHtml(shape) {
    const cells = [{x:0,y:0,label:'무기'}, ...shape.skills.map((p,i) => ({...p,label:'QWER'[i]})), ...shape.parts.map(p => ({...p,label:'◇'}))];
    const minX = Math.min(...cells.map(p=>p.x)), maxX=Math.max(...cells.map(p=>p.x));
    const minY = Math.min(...cells.map(p=>p.y)), maxY=Math.max(...cells.map(p=>p.y));
    let html='';
    for(let y=minY;y<=maxY;y++) for(let x=minX;x<=maxX;x++){const p=cells.find(p=>p.x===x&&p.y===y);html+=`<span class="${p?'cell':''} ${p?.label==='무기'?'core':''}">${p ? esc(p.label) : ''}</span>`;}
    return `<div class="shape" style="grid-template-columns:repeat(${maxX-minX+1},25px);--shape-color:${esc(shape.color)}">${html}</div>`;
  }
  const labels={id:'ID',name:'이름',family:'계열',familyA:'주요 계열 A',familyB:'주요 계열 B',tier:'등급',conditionText:'발동 조건',conditionA:'실행 조건 A',conditionB:'실행 조건 B',sprite:'리소스 경로',skillDamage:'액티브 피해',projectileDamage:'투사체 액티브 피해',meleeDamage:'근접 액티브 피해',shieldOnCast:'시전 보호막 / 공격력',healOnHit:'시전당 첫 적중 회복 / 공격력',rangeBonus:'사거리 보너스',controlBonus:'제어 시간 보너스',damage:'진화 피해 보너스',range:'진화 사거리 보너스',control:'진화 제어 보너스',shield:'진화 보호막 / 공격력',heal:'진화 회복 / 공격력',cooldown:'진화 쿨다운 환급',description:'설명'};
  function openDetail(id) {
    const e=data.entries.find(e=>e.id===id);if(!e)return;
    $('detail-content').innerHTML=`<div class="detail-head">${thumbs(e)}<div><p class="eyebrow">${esc(e.category)} ${e.tier?'· T'+e.tier:''}</p><h2>${esc(e.name)}</h2><code class="muted">${esc(e.id)}</code></div></div><p>${esc(e.summary)}</p>${e.art?`<p class="art-status">${esc(e.art)}</p>`:''}${e.shape?shapeHtml(e.shape):''}${e.tier>1?'<p class="muted">이 항목은 계열 조합·등급별 진화 외형과 특성입니다. 실제 개별 레시피의 능력은 전체 T1 재료를 계승합니다. 아래 특성 수치는 해당 두 계열 재료의 합계 비율을 곱합니다.</p>':''}<a class="source" href="${url(e.source)}" target="_blank" rel="noopener">편집 원본 ↗ ${esc(e.source)}</a>${e.data?`<dl class="detail-fields">${Object.entries(e.data).filter(([k])=>!['value','mul','stat','rgba','serializedVersion','m_Script'].includes(k)).map(([k,v])=>`<dt>${esc(labels[k]||k)}</dt><dd>${esc(v)}</dd>`).join('')}</dl>`:''}<details ${e.raw?.length<1500?'open':''}><summary>원본 데이터·문서 전체 보기</summary><pre>${esc(e.raw||'유형 값은 위 원본 소스에서 확인하세요.')}</pre></details>`;
    if(!$('detail').open)$('detail').showModal();
    $('detail').scrollTop=0;
  }
  $('navigation').addEventListener('click',e=>{const b=e.target.closest('[data-category]');if(!b)return;category=b.dataset.category;family='';$('family').value='';render();});
  $('search').addEventListener('input',e=>{query=e.target.value.trim().toLocaleLowerCase();render();});
  $('family').innerHTML+=[...new Set(data.entries.map(e=>e.family).filter(Boolean))].sort().map(f=>`<option>${esc(f)}</option>`).join('');
  $('family').addEventListener('change',e=>{family=e.target.value;render();});
  $('tier').addEventListener('change',e=>{tier=e.target.value;render();});
  $('list').addEventListener('click',e=>{const b=e.target.closest('[data-id]');if(b){history.replaceState(null,'','#'+encodeURIComponent(b.dataset.id));openDetail(b.dataset.id);}});
  document.addEventListener('click',e=>{const b=e.target.closest('[data-close]');if(b)$(b.dataset.close).close();});
  document.addEventListener('keydown',e=>{if(e.key==='/'&&!['INPUT','SELECT','TEXTAREA'].includes(document.activeElement.tagName)&&!$('detail').open&&!$('fusion').open){e.preventDefault();$('search').focus();}});
  $('timestamp').textContent='데이터 추출: '+new Date(data.generated).toLocaleString('ko-KR');
  const parts=data.entries.filter(e=>e.category==='T1 파츠');
  let materials=parts.map(p=>({id:p.id,name:p.name,tier:1,ingredients:{[p.id]:1}}));
  let lastResult=null;
  const ingredientText=m=>Object.entries(m.ingredients).map(([id,count])=>`${data.entries.find(e=>e.id===id).name} ×${count}`).join(' · ');
  function updateMaterials(selectedA,selectedB){
    ['a','b'].forEach((side,i)=>{
      $('material-'+side).innerHTML=materials.map((m,n)=>`<option value="${n}">T${m.tier} · ${esc(m.name)}</option>`).join('');
      $('material-'+side).value=String((i?selectedB:selectedA)??i);
      $('ingredient-'+side).textContent=ingredientText(materials[Number($('material-'+side).value)]);
    });
  }
  ['a','b'].forEach(side=>$('material-'+side).addEventListener('change',()=>{$('ingredient-'+side).textContent=ingredientText(materials[Number($('material-'+side).value)]);}));
  $('fusion-toggle').addEventListener('click',()=>{$('fusion').showModal();});
  $('fuse').addEventListener('click',()=>{
    const a=materials[Number($('material-a').value)],b=materials[Number($('material-b').value)];
    if(a.tier!==b.tier||a.tier>=5){$('fusion-result').textContent='같은 등급의 두 재료만 합성할 수 있으며, T5는 최종 등급입니다.';lastResult=null;return;}
    const ingredients={...a.ingredients};Object.entries(b.ingredients).forEach(([id,count])=>ingredients[id]=(ingredients[id]||0)+count);
    const tier=a.tier+1, units=2**(tier-1), familyCounts={};
    Object.entries(ingredients).forEach(([id,count])=>{const f=data.entries.find(p=>p.id===id).family;familyCounts[f]=(familyCounts[f]||0)+count;});
    // Same ordinal tie-break as the runtime (Korean BMP family names).
    const dominant=Object.entries(familyCounts).sort((a,b)=>b[1]-a[1]||(a[0]<b[0]?-1:1)).slice(0,2).map(x=>x[0]).sort();if(dominant.length===1)dominant.push(dominant[0]);
    const evolution=data.entries.find(e=>e.category==='진화 파츠'&&e.tier===tier&&e.data.familyA===dominant[0]&&e.data.familyB===dominant[1]);
    const power=data.tierPower[tier-1], bonuses={}, mods={};
    for(const [id,count] of Object.entries(ingredients)){
      const p=data.entries.find(e=>e.id===id),k=count/units*power;
      for(const key of ['skillDamage','projectileDamage','meleeDamage','shieldOnCast','healOnHit','rangeBonus','controlBonus'])bonuses[key]=(bonuses[key]||0)+Number(p.data[key]||0)*k;
      for(const m of p.raw.matchAll(/- stat: (\d+)\s+value: ([-.\d]+)\s+mul: (\d)/g)){const key=m[1]+':'+m[3];mods[key]=(mods[key]||0)+Number(m[2])*k;}
    }
    const statNames=['최대 체력','공격력','공격 속도','이동 속도','치명타 확률','치명타 피해','쿨다운 감소','피해 감소','경험치','골드','재생','대시 쿨다운','원소 위력'];
    lastResult={id:'sim:'+Object.keys(ingredients).sort().map(id=>id+'*'+ingredients[id]).join('|'),name:evolution?.name||'진화 파츠',tier,ingredients};
    const lines=Object.entries(bonuses).filter(([,v])=>v).map(([k,v])=>`<span class="pill">${labels[k]} +${(v*100).toFixed(2)}%</span>`);
    Object.entries(mods).forEach(([k,v])=>{const [stat,mul]=k.split(':');lines.push(`<span class="pill">${statNames[Number(stat)]} ${v>0?'+':''}${(v*(Number(stat)===0&&mul==='0'?1:100)).toFixed(2)}${Number(stat)===0&&mul==='0'?'':'%'}</span>`);});
    $('fusion-result').innerHTML=`<p class="eyebrow">T${tier} · T1 재료 ${units}개</p><h3>${esc(lastResult.name)}</h3><p class="muted">${esc(ingredientText(lastResult))}</p>${lines.join('')}<p><b>진화 특성</b> ${esc(evolution?.summary||'정의 없음')}</p><button type="button" id="see-evolution">특성 수치·전용 그림 경로 보기</button><div class="fusion-actions"><button type="button" data-material="a">결과를 재료 A에 넣기</button><button type="button" data-material="b">결과를 재료 B에 넣기</button></div><p class="muted">계승 능력의 합계이며 장착 전체 상한 적용 전입니다. T1 재료를 보존하므로 합성 순서가 달라도 같은 구성의 결과는 동일합니다.</p>`;
    $('see-evolution').addEventListener('click',()=>{if(evolution)openDetail(evolution.id);});
  });
  $('fusion-result').addEventListener('click',e=>{const button=e.target.closest('[data-material]');if(!button||!lastResult)return;let index=materials.findIndex(m=>m.id===lastResult.id);if(index<0){index=materials.length;materials.push(lastResult);}const a=Number($('material-a').value),b=Number($('material-b').value);updateMaterials(button.dataset.material==='a'?index:a,button.dataset.material==='b'?index:b);});
  updateMaterials(0,24);render();
  if(location.hash){try{openDetail(decodeURIComponent(location.hash.slice(1)));}catch{}}
})();
