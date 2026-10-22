'use strict';
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const messages = {music:'Press Lyrics. Give the song a little more room.',shelf:'Preview files only. Your own files stay on your PC.',todo:'Tap a task to finish it. Pick a reminder below.',booth:'Same three shots. A fresh frame, every time.',companion:'Try a moment from Kilo’s day.'};
const titles = {music:'Now playing',shelf:'Shelf',todo:'To-do',booth:'Photo Booth',companion:'Hello, Kilo'};
let page = 'music', playing = true, lyricIndex = 1, seconds = 42;
function selectPage(next) {
  page = next;
  $('#notch').dataset.page = next;
  $('#notch').dataset.lyrics = next === 'music' && $('#lyrics-toggle').getAttribute('aria-pressed') === 'true';
  $$('[data-panel]').forEach(p => p.hidden = p.dataset.panel !== next);
  $$('.page-tabs button').forEach(b => b.setAttribute('aria-pressed', b.dataset.page === next));
  $('#notch-title').textContent = titles[next]; $('#demo-message').textContent = messages[next];
}
$$('.page-tabs button').forEach(b => b.addEventListener('click', () => selectPage(b.dataset.page)));
$$('[data-show]').forEach(b => b.addEventListener('click', () => {selectPage(b.dataset.show);$('.playground').scrollIntoView({behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth',block:'center'});}));
$('#lyrics-toggle').addEventListener('click', () => {
  const open = $('#lyrics-toggle').getAttribute('aria-pressed') !== 'true';
  $('#lyrics-toggle').setAttribute('aria-pressed', open); $('#lyrics-wrap').setAttribute('aria-hidden', !open);
  $('#notch').dataset.lyrics = open;
  $('#demo-message').textContent = open ? 'Album on the left. Every word, on the right.' : messages.music;
});
function showLine(index) { lyricIndex = index % 4; $$('.lyrics p').forEach((p,i) => p.classList.toggle('current', i === lyricIndex)); }
$('#next').addEventListener('click', () => showLine(lyricIndex + 1));
$('#restart').addEventListener('click', () => { seconds = 0; showLine(0); $('#elapsed').textContent='0:00';$('.scrubber i').style.width='0%';});
$('#play').addEventListener('click', () => {playing=!playing;$('#play').textContent=playing?'Ⅱ':'▷';$('#play').setAttribute('aria-pressed',playing);$('#play').setAttribute('aria-label',playing?'Pause preview':'Play preview');$('#notch').classList.toggle('paused',!playing);});
setInterval(() => {if (!playing || page!=='music' || document.hidden) return; seconds=(seconds+1)%178;$('#elapsed').textContent=`${Math.floor(seconds/60)}:${String(seconds%60).padStart(2,'0')}`;$('.scrubber i').style.width=`${seconds/178*100}%`;if(seconds%6===0)showLine(lyricIndex+1);},1000);
$$('[data-file]').forEach(b => b.addEventListener('click', () => $('#demo-message').textContent=`${b.dataset.file} selected. In Kilo, click to open or drag it out.`));
$('#shelf-undo').addEventListener('click', () => {const cleared=!$('.shelf-items').hidden;$('.shelf-items').hidden=cleared;$('#shelf-undo').textContent=cleared?'Undo':'Clear';$('#demo-message').textContent=cleared?'Shelf cleared. You can undo that.':messages.shelf;});
$$('.task').forEach(b => b.addEventListener('click', () => {b.setAttribute('aria-pressed',b.getAttribute('aria-pressed')!=='true');const count=$$('.task[aria-pressed=true]').length;$('#task-count').textContent=`${count} of 2 done`;$('.task-fraction').textContent=`${count}/2`;$('#demo-message').textContent=count===2?'Both done. That deserves a little break.':messages.todo;}));
$$('[data-minutes]').forEach(b => b.addEventListener('click', () => {$$('[data-minutes]').forEach(x=>x.setAttribute('aria-pressed',x===b));$('#reminder-time').textContent=`In ${b.dataset.minutes} min`;$('#demo-message').textContent=`Preview reminder set for ${b.dataset.minutes} minutes. Nothing is scheduled on this device.`;}));
function reprint() {const strip=$('#mini-strip');strip.classList.remove('reprint');void strip.offsetWidth;strip.classList.add('reprint');$('#demo-message').textContent=`Reprinted in ${strip.dataset.theme}, with the ${strip.dataset.filter} filter.`;}
$$('.segmented button').forEach(b=>b.addEventListener('click',()=>{$$('.segmented button').forEach(x=>x.setAttribute('aria-pressed',x===b));$('#mini-strip').dataset.theme=b.dataset.theme;reprint();}));
$$('.filter-options button').forEach(b=>b.addEventListener('click',()=>{$$('.filter-options button').forEach(x=>x.setAttribute('aria-pressed',x===b));$('#mini-strip').dataset.filter=b.dataset.filter;reprint();}));
const moods={typing:['What are we working on?','Kilo peeks at key timing. It never reads your text.'],battery:['A little rest, maybe.','Low battery brings a softer glow and quieter animation.'],update:['I’ve got something new!','A verified download. Your custom Kilo installer.']};
$$('.moods button').forEach(b=>b.addEventListener('click',()=>{$$('.moods button').forEach(x=>x.setAttribute('aria-pressed',x===b));$('#mascot').dataset.mood=b.dataset.mood;$('#mood-title').textContent=moods[b.dataset.mood][0];$('#mood-copy').textContent=moods[b.dataset.mood][1];}));
