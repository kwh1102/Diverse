"""Import imagegen outputs only; never author gameplay data or launch Unity."""
from pathlib import Path
import csv
import hashlib
import json
import re
import shutil
import sys
import uuid
from PIL import Image, ImageDraw
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'ArtSource/Parts'
JOBS = ART / 'imagegen_jobs.json'
REPORT = ART / 'imagegen_results.json'
BASELINE = ART / 'imagegen_baseline.json'
RULES = ROOT / 'Assets/_Project/Data/AbilityRules.asset'

T1 = {
 'focus_lens': 'Short brass-mounted blue crystal focusing lens. Concentric alignment grooves converge at one center point; stationary concentration, first manual active hit.',
 'sword_knot': 'One tightly tied crimson sword-hilt knot with short braided tassels and a little metal end weight. Three braids join a stronger final knot, suggesting the last basic-combo hit. No sword blade.',
 'ember_crucible': 'One small dark ceramic crucible in a copper cradle holding a single orange ember. Contained ember, no flame jet; adds a brief burn to the player\'s elemental active hit.',
 'prism_core': 'One squat faceted prism core held in a brass claw socket. Two differently colored bands intersect INSIDE the glass, representing alternating active elements. No external light rays.',
 'moon_charm': 'One silver crescent moon pendant on a short violet cord, its inner rear face catching a pale blue highlight. Back-angle precision charm, no attack scene.',
 'lucky_die': 'One polished obsidian die with the five-pip face clearly visible, four dull pips and one ruby-colored central pip; fifth landed basic hit. Pips are inlays, no numerals.',
 'razor_gem': 'One thin sharp-edged pale ruby crystal with a chipped tip and a single deep fracture, in a small silver collar. Finishing weakened targets, no target shown.',
 'ruby_pin': 'One short ornamental silver pin with a ruby reservoir head held in two protective prongs. A contained bright ruby facet stores critical-hit energy for the next manual active.',
 'clock_spring': 'One closed compact brass pocket-clock mechanism with a visible relaxed winding spring and a small still escapement. No numbers or hands shaped as letters. Rest then reuse.',
 'silver_gear': 'One silver three-lobed interlocking gear, three broad prominent teeth, a small brass hub. Three landed basic hits prepare the next active cooldown reduction.',
 'echo_bell': 'Exactly ONE small silver bell, ONE bell mouth and ONE clapper. A backward-curled brass clock spring is physically mounted INSIDE its top crown, one end returning into the earlier notch on the bell shoulder. Time returns to the PREVIOUS skill slot when a different active follows. Return-time motif built into a single bell, not a diagram. No second bell, no ghost copy, no floating arrow, no sound waves, no rapid-fire motif.',
 'hourglass_bead': 'One rounded glass bead enclosing a tiny hourglass with golden sand collected back in the upper chamber. Silver retaining cap and loop. Refund time after a missed finished active; no extra clock.',
 'arrow_fletching': 'One detached arrow-tail tool with a short shaft and two stiff hawk feathers in a neat straight axis; a slim silver collar pierced with a single opening suggests extra penetration. No arrowhead or flying arrow.',
 'falcon_sight': 'One bronze short tubular sight with a hawk-eye-shaped blue glass lens and gently curving integral guide fins; slight homing correction. No target, no projectile.',
 'rune_scope': 'One compact silver rune scope: two nested engraved alignment rings are centered on the EXACT SAME single reticle point in its violet glass, the rear ring subtly brighter. Follow-up projectile hits on the SAME target; NOT homing, NOT two targets. No readable runic letters, only geometric grooves.',
 'spiral_nozzle': 'One short copper launch-nozzle attachment viewed obliquely so its open bore and clearly cut HELICAL SPIRAL GROOVES inside the bore are visible. A single unified spiral-rifled nozzle, no projectile and no firing.',
 'trail_compass': 'One brass trail compass with a long blue needle pointing to a tiny distant-point inset on the rim. No cardinal letters, no numbers. Far-aimed active range motif.',
 'star_map': 'One small folded indigo star chart in a brass corner clamp, two tiny inlaid star dots connected by a short line and one subtly emphasized remembered point. A map tool, no text or UI frame; remembers the first active impact location for nearby re-aim.',
 'long_reed': 'One notably LONG hollow golden-green reed tube, diagonal elongated silhouette, natural joints, small open ends and a thin brass reinforcing cuff. A passive flight-length extension attachment, long sustained flight distance, NOT a blowgun firing, NOT a flute, no holes or musical notes.',
 'amber_mirror': 'One small oval amber-glass mirror with a copper handle, a dark shallow scratch held INSIDE the amber and one pale reflective facet. Stores received hurt for a subsequent manual first hit, no opponent.',
 'frost_shard': 'One chunky ice-blue frost crystal with a broad central frozen facet and tiny silver root clamp. Briefly locks a controlled target, no surrounding snow, particles or enemy.',
 'anchor_hook': 'One short heavy iron anchor hook with two inward-curved blunt flukes and a small attached chain link. Weight and grip suggest slowing an already displaced target. No grappling attack.',
 'thunder_pin': 'One small silver needle tool with a yellow glass head, a simple diamond-shaped inset mark directly under its tip housing. Contained yellow charge, no lightning rays. Precision mark-triggered brief stun.',
 'gravity_stone': 'One dense dark violet stone in an inward-curving iron cage. Its carved grooves and all cage prongs curl INWARD to the central core; two small chips physically held against its inner rim reinforce inward attraction. No outward blast, no floating particle halo, no pushing.',
 'ward_buckle': 'One stout blue-silver belt buckle whose interlocking plate opens into a compact shield shape, a short brown strap stub. Perfect-dodge protection, no energy bubble or shield UI.',
 'vital_vial': 'One little round glass healing vial, partially filled with muted red liquid, cork stopper and green leaf-shaped clasp. Visible empty space over liquid suggests restoring missing health, no health icon or text.',
 'iron_plate': 'One thick iron armor fragment with three overlapping blue-gray lamellae, a stout brass rivet and stable squared lower edge. Bracing while still, no full armor or character.',
 'heart_lock': 'One closed iron heart-shaped padlock, rounded burgundy enamel core held safely by thick steel halves and a small keyhole. Emergency heart protection, no heart UI symbol floating outside.',
 'wind_boot': 'One small soft leather ankle boot with a curved teal cuff and three linked brass studs along the sole. Walking stores momentum for a basic hit; no external wind trails.',
 'silk_grip': 'One short tool handle tightly wrapped in pale lilac silk, three overlapping turns snug around the dark wooden grip. Cloth texture in chunky pixels, no blade; stationary repeated-hit rhythm.',
 'feather_ring': 'One small silver ring with an integrated pale feather curving around its band and a small return notch. Perfect dodge restores movement charge, no flying feather particles.',
 'breeze_ribbon': 'One long soft turquoise ribbon tied into a compact loose bow, two asymmetric cloth tails curling in the same direction. Cloth folds suggest speed continuing after movement ends; no speed lines.'
}

EVOLUTION = {
 'power_power': 'condensed crystal heart in a single crucible socket; thick faceted central core, inward compression ribs',
 'power_critical': 'precision focusing lens ground from ruby crystal, one crescent-shaped silver optical housing',
 'power_reuse': 'ignition winding mechanism, a condensed ember crystal IS the axle of its silver clock spring',
 'power_projectile': 'crystal pressure nozzle with spiral-cut bore and integral feather-shaped cooling fins',
 'power_reach': 'horizon survey crystal, amber prism cut into a compass-like sighting instrument',
 'power_control': 'binding crucible whose frost-lined iron anchor ribs close inward around a dense crystal core',
 'power_guard': 'protective focusing lens set directly into one thick shield-buckle shaped lens mount',
 'power_movement': 'streamlined wind core, condensed crystal socket with integral feather ribs and cloth tie',
 'critical_critical': 'fate moon wheel, one finely sharpened ruby crescent embedded within a silver circular tool',
 'critical_reuse': 'opportunity gear with crescent-shaped precision teeth and ruby-bearing clockwork axle',
 'critical_projectile': 'hawk-fang sighting nozzle, one ruby taper shaped into the upper wall of a feather-ribbed spiral bore',
 'critical_reach': 'star-piercing survey pin, ruby needle and small amber compass disk fused into one calibration tool',
 'critical_control': 'frost-edged calibration blade crystal, ruby and ice facets in one inward-hooked silver root fitting; a held tool attachment, not an attacking sword',
 'critical_guard': 'unyielding ruby, a ruby core structurally embedded inside one interlocking shield-lock buckle',
 'critical_movement': 'moonshadow ring, silver crescent band with integrated ruby eye and feather-shaped flowing rim',
 'reuse_reuse': 'reverberating spring engine, nested silver clockwork rings with one backward-return escapement and small bell-shaped housing',
 'reuse_projectile': 'repeat-cast mechanism, spiral nozzle housing whose silver gear sleeve winds a passive feather-finned guide; no gun or ammunition',
 'reuse_reach': 'orbital clock, one amber-faced navigation clock with an integrated compass needle and silver orbital gear; no numerals',
 'reuse_control': 'binding pendulum, one dense violet frost-stone pendulum seated in a geared iron hook bracket',
 'reuse_guard': 'guardian bell, one silver bell whose thick outer wall forms an interlocking shield and clock-spring crown',
 'reuse_movement': 'cycle wing, one feather-profile silver clock-spring clasp with short silk flight vanes integrated into its frame',
 'projectile_projectile': 'hawk spine guide, one narrow articulated feather-ribbed launch guide with a clearly spiral-grooved terminal bore; no separate arrows',
 'projectile_reach': 'horizon scope, elongated amber optical tube with integral compass adjustment dial and feather-shaped stabilizer ribs',
 'projectile_control': 'anchor guide tube, one spiral-rifled iron tube whose inward-curled frost-lined anchor flukes are its structural supports',
 'projectile_guard': 'bastion fletching, one shield-profile silver fletching collar with protective feather plates around a short guide bore',
 'projectile_movement': 'meteor feather, one streamlined teal feather-shaped guide nozzle with integral silk tail tie and spiral groove',
 'reach_reach': 'horizon compass, one broad amber compass mirror with concentric distant-sighting facets and long internal needle',
 'reach_control': 'attraction celestial sphere, one amber navigation orb in an inward-curved frosted iron meridian cage; inward attraction, no outward rays',
 'reach_guard': 'sanctuary mirror, one amber mirror whose thick silver backing IS a shield-shaped interlocking buckle',
 'reach_movement': 'wind-route map, one small indigo cloth survey map woven directly into a feather-shaped wind vane clasp; two route star inlays, no writing',
 'control_control': 'binding plummet, one heavy frost-violet gravity weight whose dense core is enclosed by inward-hooked anchor iron',
 'control_guard': 'frost seal, one dense ice-blue crystal lock with inward-curved iron shield plates closed around the seal core',
 'control_movement': 'storm hook, one swept teal feather-shaped iron hook with a frost-violet weighted root and short silk grip wrap',
 'guard_guard': 'guardian chalice, one stout silver protective cup with shield-shaped interlocking walls and red glass life reservoir',
 'guard_movement': 'breeze shield, one lightweight feather-ribbed silver buckle shield with an integrated teal silk strap flowing from the handle',
 'movement_movement': 'gale wing, one aerodynamic feather-shaped movement harness clasp with overlapping pale vanes and woven teal silk spine'
}

STYLE = ('Use case: stylized-concept. Asset type: Diverse top-down pixel RPG inventory part. '
 'Cute cohesive game pixel art, dark purple #30213d outline, large readable square pixel clusters, '
 '3–4 discrete flat tones per material, moderately refined metal, glass and cloth. '
 'One centered isolated PASSIVE TOOL, top-down three-quarter item view. '
 'Actual transparent alpha PNG with generous empty margin. '
 'STRICT no background, checkerboard pattern, text, letters, numerals, UI border, badge, '
 'character, enemy, projectile, firing, autonomous attack, particle, aura, excessive glow, gradient, '
 'cast shadow, soft shading or antialiased outlines. ')

def read_rules():
    text = RULES.read_text(encoding='utf-8')
    def section(a, b):
        return {re.search(r'- id: "([^"]+)"', x)[1]: x for x in re.split(r'(?=^    - id:)', text.split(a, 1)[1].split(b, 1)[0], flags=re.M) if '- id:' in x}
    return text, section('    parts:\n', '    evolutions:'), section('    evolutions:\n', '    weaponShapes:')

def field(block, name):
    m = re.search(r'^      '+name+r': "(.*)"$', block, re.M)
    return m[1] if m else ''

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def prepare():
    rows = list(csv.DictReader((ART/'image_manifest.csv').open(encoding='utf-8-sig', newline='')))
    text, parts, evos = read_rules()
    assert len(rows) == 176 and len(parts) == 32 and len(evos) == 144
    jobs = []
    for r in rows:
        tier = int(r['tier']); block = parts[r['id']] if tier == 1 else evos[r['id']]
        p = STYLE + f"ID: {r['id']}. Name: {r['name']}. Manifest motif: {r['art_brief']}. "
        if tier == 1:
            p += f"Subject: {T1[r['id']]} Current unique ability (semantics only, NEVER draw text): {field(block, 'traitText')}. "
            p += 'Native target 32x32 pixels: object fits within 28x28 logical pixels; crisp square nearest-neighbor enlarged pixel art on a 32x32 logical grid.'
        else:
            pair = r['id'].rsplit('_t', 1)[0]
            p += f"Unified tool design: {EVOLUTION[pair]}. Both family motifs are functional parts of ONE newly fused instrument, never two separate objects, never a collage. Current manual-cast condition (do not draw text): {field(block, 'conditionText')}. "
            growth = {
                2: 'T2 foundational form: compact simple single body, bronze and iron fittings, one small glass/crystal insert, 2 broad structural ribs, silhouette about 30 logical pixels wide.',
                3: 'T3 awakened form: recognizable same core tool but longer silhouette, a forked integrated crown/fin, polished silver replacing bronze, larger cut-glass/crystal insert, 3 broad ribs, about 35 logical pixels wide.',
                4: 'T4 radiant form: expanded lobed silhouette, double-layer integral wing or crown, silver with restrained gold edge inlays, sharply faceted glass/crystal and reinforced jointwork, 4 broad ribs, about 39 logical pixels wide; radiance is polished material, NOT light effects.',
                5: 'T5 perfected form: grand fully developed architectural silhouette with sweeping integrated outer fins/crown and closed reinforced central housing, polished pale alloy, restrained gold hardware and jewel-cut glass/crystal, 5 broad structural ribs, about 43 logical pixels wide; unmistakably more developed than T4, no aura.'
            }[tier]
            p += growth + ' Keep the design lineage and family palette across all tiers. Native target 48x48 pixels; fit the whole silhouette within 44x44 logical pixels, crisp square nearest-neighbor enlarged pixel art on a 48x48 logical grid.'
        jobs.append(dict(**r, prompt=p))
    JOBS.write_text(json.dumps(jobs, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    if not BASELINE.exists():
        metas = {str(p.relative_to(ROOT)).replace('\\','/'): re.search(r'^guid: (.+)$', p.read_text(), re.M)[1] for p in (ROOT/'Assets/_Project/Resources/Sprites/Parts').rglob('*.meta')}
        BASELINE.write_text(json.dumps(dict(guids=metas, gameplay=re.sub(r'^      sprite: .*$', '', text, flags=re.M), scripts={str(p.relative_to(ROOT)).replace('\\','/'): digest(p) for p in (ROOT/'Tools').glob('author_part*.py')}), ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(dict(jobs=len(jobs), existing_guids=len(json.loads(BASELINE.read_text(encoding='utf-8'))['guids']))))

def install(id, source):
    job = next(x for x in json.loads(JOBS.read_text(encoding='utf-8')) if x['id'] == id)
    size = 64 if int(job['tier']) == 1 else 96
    original = Image.open(source).convert('RGBA')
    alpha = original.getchannel('A')
    if alpha.getextrema()[0] != 0 or alpha.getextrema()[1] < 200:
        raise ValueError('Generated output has no actual transparent background')
    from refine_readability import convert
    final = convert(original, int(job['tier']))
    dest = ROOT/job['final_png']; dest.parent.mkdir(parents=True, exist_ok=True)
    final.save(dest)
    meta = Path(str(dest)+'.meta')
    if meta.exists():
        m = meta.read_text(encoding='utf-8')
    else:
        m = (ROOT/'Assets/_Project/Resources/Sprites/Parts/focus_lens.png.meta').read_text(encoding='utf-8')
        m = re.sub(r'^guid: .*$', 'guid: '+uuid.uuid4().hex, m, flags=re.M)
        m = re.sub(r'^(    spriteID: ).*$', lambda x:x[1]+uuid.uuid4().hex, m, flags=re.M)
    for key, value in dict(isReadable=1,enableMipMap=0,filterMode=0,nPOTScale=0,spriteMode=1,spritePixelsToUnits=16,textureType=8,alphaUsage=1,alphaIsTransparency=1,textureCompression=0,crunchedCompression=0).items():
        m = re.sub(r'^(\s*'+key+r': )[^\r\n]*', lambda x:x[1]+str(value), m, flags=re.M)
    with meta.open('w', encoding='utf-8', newline='\r\n') as f: f.write(m)
    results = json.loads(REPORT.read_text(encoding='utf-8')) if REPORT.exists() else {}
    results[id] = dict(source=str(source), final_png=job['final_png'], size=size, sha256=digest(dest), tool='built-in image_gen', prompt=job['prompt'])
    REPORT.write_text(json.dumps(results, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(dict(installed=id, size=size, completed=len(results)), ensure_ascii=False))

def contact(start, count):
    jobs = json.loads(JOBS.read_text(encoding='utf-8'))[start:start+count]
    sheet = Image.new('RGB', (len(jobs)*240, 270), '#a799b6')
    d = ImageDraw.Draw(sheet)
    for i,j in enumerate(jobs):
        path = ROOT/j['final_png']
        if not path.exists(): continue
        obj = Image.open(path).convert('RGBA')
        obj = obj.resize((192,192), Image.Resampling.NEAREST)
        sheet.paste(obj, (i*240+24, 16), obj)
        d.text((i*240+8, 220), j['id'], fill='#21172c')
    out = ART/'imagegen_preview.png'; sheet.save(out); print(str(out))

def prepare_sheets():
    jobs = json.loads(JOBS.read_text(encoding='utf-8'))
    groups = [jobs[20:28], jobs[28:32]+[jobs[10]]] + [jobs[i:i+8] for i in range(32,176,8)]
    sheets=[]
    for index, group in enumerate(groups):
        p = ('Use case: stylized-concept. Diverse game production sprite sheet, actual ALPHA TRANSPARENT PNG. '
             'Canvas landscape 2:1 aspect, 4 equally sized columns and 2 equally sized rows, EIGHT equal invisible cells. '
             'Do not draw a grid, separators, borders or labels. Every object is centered exactly in its own cell; '
             'each cell has a large clear transparent margin. No object crosses a cell boundary. '
             'Cute top-down three-quarter pixel RPG item art, readable large square pixel clusters, dark purple #30213d outlines, '
             'STRICT 3–4 flat tones per material; refined but chunky metal, glass and cloth, crisp pixel staircase edges. '
             'No background, checkerboard pattern, words, letters, numerals, UI, badges, particles, glow, gradients, smooth shading, '
             'cast shadows, character, enemy, attack, firing, floating arrows or external motion marks. '
             'All objects are passive equipment; every slot contains ONE unified physical tool, no collage or separate props. ')
        evolution = int(group[0]['tier']) > 1
        if evolution:
            p += ('Each ROW is ONE design lineage across T2,T3,T4,T5 left to right. '
                  'Keep the core architecture, orientation, color identity and tool identity consistent WITHIN each row. '
                  'Grow its actual silhouette, structure and craftsmanship across columns, never merely recolor or add a border. '
                  'Column1 T2 compact bronze/iron single housing with 2 ribs and small inset; '
                  'column2 T3 elongated form with 3 ribs, a forked integral crown and polished silver/larger crystal; '
                  'column3 T4 broad lobed silhouette, 4 ribs, double-layer crown, silver with subtle gold and faceted glass; '
                  'column4 T5 sweeping fully developed fins/crown, 5 ribs, reinforced jewel-cut core in pale alloy and restrained gold. '
                  'T2,T3,T4,T5 objects use respectively 46%,54%,62%,70% of their cell width/height. '
                  'MANDATORY leave at least 15% of EACH CELL empty on EVERY side, INCLUDING the T5 crown and fins. '
                  'Nothing touches any cell boundary or the outer sheet edges. Complete unclipped silhouettes are essential. '
                  'Each sprite must read at NATIVE 48x48; create each cell as enlarged crisp 48x48 logical pixel art. ')
        else:
            p += 'Each object must read at NATIVE 32x32; create each cell as enlarged crisp 32x32 logical pixel art, object occupies 78% of cell at most. '
        for i, j in enumerate(group):
            row,col=divmod(i,4)
            if int(j['tier'])==1:
                subject=T1[j['id']]
            else:
                subject=EVOLUTION[j['id'].rsplit('_t',1)[0]]
            p += f"\nCELL row {row+1} column {col+1}, {j['name']} (T{j['tier']}): {subject}. Manifest: {j['art_brief']}. "
        for i in range(len(group),8):
            row,col=divmod(i,4);p+=f'\nCELL row {row+1} column {col+1}: EMPTY, entirely alpha transparent. '
        sheets.append(dict(id=f'sheet_{index+1:02}', ids=[j['id'] for j in group], prompt=p))
    (ART/'imagegen_sheets.json').write_text(json.dumps(sheets,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(sheets=len(sheets),asset_slots=sum(len(x['ids']) for x in sheets))))

def install_sheet(id, source):
    sheet=next(x for x in json.loads((ART/'imagegen_sheets.json').read_text(encoding='utf-8')) if x['id']==id)
    directory=ART/'Sheets';directory.mkdir(exist_ok=True)
    raw=directory/(id+'.png')
    if Path(source).resolve()!=raw.resolve():shutil.copy2(source,raw)
    im=Image.open(raw).convert('RGBA')
    alpha_min,alpha_max=im.getchannel('A').getextrema()
    if alpha_min != 0 or alpha_max < 200:raise ValueError('Sheet is not alpha transparent')
    fragments=ART/'Crops';fragments.mkdir(exist_ok=True)
    objects=connected_objects(im,len(sheet['ids']))
    for i,asset in enumerate(sheet['ids']):
        component=objects[i];bounds=component['bbox']
        crop=im.crop(bounds)
        # Retain the selected physical silhouette only, never a neighboring cell or guide.
        mask=Image.new('L',crop.size)
        draw=ImageDraw.Draw(mask)
        for y,x0,x1 in component['runs']:
            draw.line((x0-bounds[0],y-bounds[1],x1-1-bounds[0],y-bounds[1]),fill=255)
        crop.putalpha(mask)
        out=fragments/(asset+'.png');crop.save(out)
        install(asset,out)
    results=json.loads(REPORT.read_text(encoding='utf-8'))
    for asset in sheet['ids']:
        results[asset].update(source=str(raw.relative_to(ROOT)).replace('\\','/'),prompt=sheet['prompt'],sheet=id)
    REPORT.write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(sheet=id,source_size=im.size,completed=len(results))))

def connected_objects(im,count):
    """Run-length connected components preserve whole silhouettes across nominal cells."""
    mask=np.asarray(im.getchannel('A'))>=128
    parent=[];runs=[];previous=[]
    def find(i):
        while parent[i]!=i:
            parent[i]=parent[parent[i]];i=parent[i]
        return i
    for y in range(im.height):
        changes=np.diff(np.pad(mask[y].astype(np.int8),(1,1)))
        starts=np.flatnonzero(changes==1);ends=np.flatnonzero(changes==-1);current=[]
        k=0
        for x0,x1 in zip(starts.tolist(),ends.tolist()):
            idx=len(parent);parent.append(idx);runs.append((y,x0,x1));current.append((x0,x1,idx))
            while k<len(previous) and previous[k][1]<x0:k+=1
            q=k
            while q<len(previous) and previous[q][0]<=x1:
                parent[find(idx)]=find(previous[q][2]);q+=1
        previous=current
    comps={}
    for i,(y,x0,x1) in enumerate(runs):
        root=find(i)
        c=comps.setdefault(root,dict(pixels=0,bbox=[im.width,im.height,0,0],runs=[]))
        c['pixels']+=x1-x0;c['runs'].append((y,x0,x1));b=c['bbox']
        b[0]=min(b[0],x0);b[1]=min(b[1],y);b[2]=max(b[2],x1);b[3]=max(b[3],y+1)
    candidates=sorted(comps.values(),key=lambda c:c['pixels'],reverse=True)[:count]
    if len(candidates)!=count or any(c['pixels']<1000 for c in candidates):raise ValueError('Could not identify all sheet objects')
    candidates.sort(key=lambda c:(c['bbox'][1]+c['bbox'][3])/2)
    ordered=[]
    for row in [candidates[:4],candidates[4:]]:
        ordered.extend(sorted(row,key=lambda c:(c['bbox'][0]+c['bbox'][2])/2))
    return ordered

def reimport_sheets():
    sheets=json.loads((ART/'imagegen_sheets.json').read_text(encoding='utf-8'))
    for s in sheets:
        install_sheet(s['id'],ART/'Sheets'/(s['id']+'.png'))
    print('Reimported all 20 sheets by actual connected alpha silhouettes.')

def refresh_previews():
    for s in json.loads((ART/'imagegen_sheets.json').read_text(encoding='utf-8')):
        sheet_preview(s['id'])

def sheet_preview(id):
    sheet=next(x for x in json.loads((ART/'imagegen_sheets.json').read_text(encoding='utf-8')) if x['id']==id)
    jobs={x['id']:x for x in json.loads(JOBS.read_text(encoding='utf-8'))}
    out=Image.new('RGB',(960,520),'#a799b6');d=ImageDraw.Draw(out)
    for i,asset in enumerate(sheet['ids']):
        row,col=divmod(i,4);im=Image.open(ROOT/jobs[asset]['final_png']).convert('RGBA')
        im=im.resize((192,192),Image.Resampling.NEAREST)
        out.paste(im,(col*240+24,row*260+12),im)
        d.text((col*240+8,row*260+220),asset,fill='#21172c')
    target=ART/(id+'_preview.png');out.save(target);print(str(target))

def inspect_sources():
    sheets=json.loads((ART/'imagegen_sheets.json').read_text(encoding='utf-8'))
    issues=[]
    for s in sheets:
        path=ART/'Sheets'/(s['id']+'.png')
        if not path.exists():continue
        im=Image.open(path).convert('RGBA')
        objects=connected_objects(im,len(s['ids']))
        for i,asset in enumerate(s['ids']):
            b=objects[i]['bbox']
            margins=(b[0],b[1],im.width-b[2],im.height-b[3])
            if min(margins)<2:issues.append(dict(id=asset,sheet=s['id'],margins=margins))
    print(json.dumps(issues,ensure_ascii=False,indent=2))

def clean_and_catalog():
    jobs=json.loads(JOBS.read_text(encoding='utf-8'));results=json.loads(REPORT.read_text(encoding='utf-8'))
    removed=[]
    for j in jobs:
        p=ROOT/j['final_png'];im=Image.open(p).convert('RGBA');pixels=im.load();visited=set();components=[]
        for y in range(im.height):
            for x in range(im.width):
                if (x,y) in visited or pixels[x,y][3]==0:continue
                q=[(x,y)];visited.add((x,y));component=[]
                while q:
                    px,py=q.pop();component.append((px,py))
                    for dx in (-1,0,1):
                        for dy in (-1,0,1):
                            nx,ny=px+dx,py+dy
                            if 0<=nx<im.width and 0<=ny<im.height and (nx,ny) not in visited and pixels[nx,ny][3]:
                                visited.add((nx,ny));q.append((nx,ny))
                components.append(component)
        dust=[c for c in components if len(c)<=2]
        if dust:
            for c in dust:
                for x,y in c:pixels[x,y]=(0,0,0,0)
            im.save(p);removed.append(dict(id=j['id'],pixels=sum(map(len,dust))))
        results[j['id']]['sha256']=digest(p)
    REPORT.write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    # Two overview pages retain row-wise T2->T5 comparison at 3x native size.
    for page,group in enumerate([jobs[:32],jobs[32:104],jobs[104:]],1):
        cols=4 if page>1 else 8;cell=208 if page>1 else 144
        rows=(len(group)+cols-1)//cols
        out=Image.new('RGB',(cols*cell,rows*(cell+24)),'#a799b6');d=ImageDraw.Draw(out)
        for i,j in enumerate(group):
            row,col=divmod(i,cols);im=Image.open(ROOT/j['final_png']).convert('RGBA');factor=2
            sprite=im.resize((im.width*factor,im.height*factor),Image.Resampling.NEAREST)
            out.paste(sprite,(col*cell+(cell-sprite.width)//2,row*(cell+24)+(cell-sprite.height)//2),sprite)
            d.text((col*cell+4,row*(cell+24)+cell),j['id'],fill='#21172c')
        out.save(ART/f'catalog_generated_{page}.png')
    print(json.dumps(dict(dust_removed=removed,overview_pages=3),ensure_ascii=False))

def finalize():
    jobs = json.loads(JOBS.read_text(encoding='utf-8')); results = json.loads(REPORT.read_text(encoding='utf-8'))
    assert set(results) == {j['id'] for j in jobs}, f'Incomplete: {len(results)}/176'
    baseline = json.loads(BASELINE.read_text(encoding='utf-8'))
    for id in ['focus_lens','long_reed']:
        prompt_file=ART/(id+'_prompt.txt')
        if prompt_file.exists():results[id]['prompt']=prompt_file.read_text(encoding='utf-8').strip()
    sheet_file=ART/'imagegen_sheets.json'
    sheets=json.loads(sheet_file.read_text(encoding='utf-8'))
    revised_growth=('T2,T3,T4,T5 objects use respectively 46%,54%,62%,70% of their cell width/height. '
                    'MANDATORY leave at least 15% of EACH CELL empty on EVERY side, INCLUDING the T5 crown and fins. '
                    'Nothing touches any cell boundary or the outer sheet edges. Complete unclipped silhouettes are essential. ')
    original_growth='T2,T3,T4,T5 objects use respectively 60%,68%,76%,84% of their cell width/height, all with transparent margins. '
    # First six selected sheets predate the margin revision; record actual prompts.
    for s in sheets[:6]:s['prompt']=s['prompt'].replace(revised_growth,original_growth)
    sheet_file.write_text(json.dumps(sheets,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    sheet_by_id={s['id']:s for s in sheets}
    for r in results.values():
        if 'sheet' in r:r['prompt']=sheet_by_id[r['sheet']]['prompt']
    REPORT.write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    text, parts, _ = read_rules()
    for id, block in parts.items():
        text = text.replace(block, re.sub(r'^      sprite: .*$', f'      sprite: "Sprites/Parts/{id}"', block, flags=re.M))
    assert re.sub(r'^      sprite: .*$', '', text, flags=re.M) == baseline['gameplay']
    with RULES.open('w', encoding='utf-8', newline='\r\n') as f: f.write(text)
    for path, guid in baseline['guids'].items():
        assert re.search(r'^guid: (.+)$', (ROOT/path).read_text(), re.M)[1] == guid
    for path, sha in baseline['scripts'].items(): assert digest(ROOT/path) == sha
    for j in jobs:
        im = Image.open(ROOT/j['final_png']); size = 64 if j['tier']=='1' else 96
        assert im.size == (size,size) and im.mode == 'RGBA' and im.getchannel('A').getextrema() == (0,255)
        m = Path(str(ROOT/j['final_png'])+'.meta').read_text()
        for key,value in dict(isReadable=1,enableMipMap=0,filterMode=0,spriteMode=1,spritePixelsToUnits=16,textureType=8,alphaIsTransparency=1,textureCompression=0).items():
            assert re.findall(r'^\s*'+key+r': (.+)$',m,re.M) and all(x==str(value) for x in re.findall(r'^\s*'+key+r': (.+)$',m,re.M))
    manifest = ART/'image_manifest.csv'
    with manifest.open('w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['id','name','tier','families','final_png','art_brief','status']);w.writeheader()
        for j in jobs:w.writerow({k:('제작 완료' if k=='status' else j[k]) for k in w.fieldnames})
    print('176 PNG imports complete; alpha, sizes, importer settings, old GUIDs, gameplay and authoring scripts inspected. No game, compile or tests run.')

if __name__ == '__main__':
    action = sys.argv[1]
    if action == 'prepare': prepare()
    elif action == 'install': install(sys.argv[2], sys.argv[3])
    elif action == 'contact': contact(int(sys.argv[2]), int(sys.argv[3]))
    elif action == 'prepare_sheets': prepare_sheets()
    elif action == 'install_sheet': install_sheet(sys.argv[2],sys.argv[3])
    elif action == 'sheet_preview': sheet_preview(sys.argv[2])
    elif action == 'inspect_sources': inspect_sources()
    elif action == 'clean_and_catalog': clean_and_catalog()
    elif action == 'reimport_sheets': reimport_sheets()
    elif action == 'refresh_previews': refresh_previews()
    elif action == 'finalize': finalize()
