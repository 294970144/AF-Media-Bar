// Exercises the embedded lyrics document in Chromium, including row-count changes during rolling transitions.
// The caller supplies Playwright; this script owns and closes its browser and does not contact lyric providers.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const directory = path.join(__dirname, '../src/AFMediaBar/Web/Lyrics');
const read = name => fs.readFileSync(path.join(directory, name), 'utf8');
const document = read('index.html')
  .replace('{{STYLE_CSS}}', read('style.css'))
  .replace('{{APP_JS}}', ['state.js', 'spacing.js', 'presentation.js', 'app.js'].map(read).join('\n'));

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  let failures = 0;
  try {
    const page = await browser.newPage({ viewport: { width: 320, height: 40 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.setContent(document);
    const frame = async (next = '', translation = '', translationMode = false, index = 0, animate = false) => {
      await page.evaluate(({ next, translation, translationMode, index, animate }) => {
        window.taskbarLyrics.receive({ version: 1, type: 'lyrics', payload: {
          current: 'original ' + index, next, progress: 0.3, currentLineIndex: index, trackId: 'test-track',
          isPureMusic: false, isPlaying: true, wordScanProgress: 0.3, currentTranslation: translation,
          nextTranslation: '', translationMode, animateTransition: animate, scene: 'lyrics'
        } });
      }, { next, translation, translationMode, index, animate });
    };
    const geometry = () => page.evaluate(() => {
      const current = document.getElementById('currentLine').getBoundingClientRect();
      const layout = document.getElementById('layout').getBoundingClientRect();
      const secondary = document.getElementById('nextLine');
      return {
        centerError: current.top + current.height / 2 - (layout.top + layout.height / 2),
        secondaryText: document.getElementById('nextLineText').textContent.trim(),
        secondaryOpacity: Number(getComputedStyle(secondary).opacity),
        currentText: document.getElementById('currentLineText').textContent,
        translateTransition: getComputedStyle(document.getElementById('track')).transitionProperty,
        animating: document.getElementById('track').classList.contains('animating')
      };
    });
    const centered = result => {
      assert.ok(Math.abs(result.centerError) <= 0.6, `single line center error: ${result.centerError}px`);
      assert.equal(result.secondaryOpacity, 0, 'empty secondary row must be invisible');
      assert.equal(result.secondaryText, '', 'empty secondary row must not contain a placeholder');
    };
    const check = async (name, run) => {
      try { await run(); console.log('PASS ' + name); }
      catch (error) { failures++; console.error('FAIL ' + name + ': ' + error.message); }
    };
    await check('single line is vertically centered', async () => {
      await frame();
      await page.waitForTimeout(650);
      centered(await geometry());
    });
    await check('missing translation has no placeholder', async () => {
      await frame('', '  ', true);
      await page.waitForTimeout(650);
      centered(await geometry());
    });
    await check('two visible rows stay above and below the center', async () => {
      await frame('', 'translation', true);
      await page.waitForTimeout(650);
      const result = await geometry();
      assert.ok(result.centerError < -3);
      assert.equal(result.secondaryText, 'translation');
      assert.ok(result.secondaryOpacity > 0);
    });
    await check('row-count setting changes animate the centering', async () => {
      await frame('', '', false, 0, true);
      const start = await geometry();
      await page.waitForTimeout(160);
      const middle = await geometry();
      await page.waitForTimeout(650);
      const end = await geometry();
      centered(end);
      assert.ok(start.centerError < middle.centerError && middle.centerError < end.centerError - 0.1,
        'centering should interpolate instead of jumping');
    });
    await check('rolling from a single row to a translation pair', async () => {
      await frame('', 'translation', true, 1, true);
      await page.waitForTimeout(150);
      assert.ok((await geometry()).animating, 'rolling animation should remain active');
      await page.waitForTimeout(850);
      assert.equal((await geometry()).secondaryText, 'translation');
    });
    await check('rolling from a translation pair to a single row', async () => {
      await frame('', '', false, 2, true);
      await page.waitForTimeout(150);
      assert.ok((await geometry()).animating);
      await page.waitForTimeout(850);
      const result = await geometry();
      centered(result);
      assert.equal(result.currentText, 'original 2');
    });
    await check('next-line mode shows its second row', async () => {
      await frame('next lyric');
      await page.waitForTimeout(650);
      const result = await geometry();
      assert.ok(result.centerError < -3);
      assert.equal(result.secondaryText, 'next lyric');
      assert.ok(result.secondaryOpacity > 0);
    });
    await check('translation roll with missing incoming translation ends centered', async () => {
      await frame('', 'translation', true);
      await page.waitForTimeout(650);
      await frame('', '', true, 3, true);
      await page.waitForTimeout(1000);
      centered(await geometry());
    });
    await check('single row remains centered after height, spacing and offset changes', async () => {
      for (const height of [32, 48, 60]) {
        await page.setViewportSize({ width: 320, height });
        await page.evaluate(() => window.taskbarLyrics.receive({ version: 1, type: 'style', payload: {
          lyricsPanePaddingTop: 4, primaryOffsetY: 2, lineGapPercent: 25
        } }));
        await page.waitForTimeout(650);
        centered(await geometry());
      }
    });
    await check('reduced motion changes row count without animation', async () => {
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await frame('', 'translation', true);
      await frame('', '', false, 4, true);
      centered(await geometry());
      assert.equal((await geometry()).translateTransition, 'none');
    });
    assert.deepEqual(errors, [], 'browser must not report script errors');
  } finally {
    await browser.close();
  }
  if (failures) process.exitCode = 1;
})().catch(error => { console.error(error); process.exitCode = 1; });
