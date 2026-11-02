// Pass this function to an isolated Playwright CLI session's run-code --filename.
// The page must be the local fixture served by render-readme-demos.cjs at 1080x640.
async page => {
  await page.waitForFunction(() => window.demoReady && window.__dragonSkinV1?.status().assetsDecoded);
  const box = await page.getByRole('slider', { name: 'Reasoning effort' }).boundingBox();
  const x = n => box.x + 8 + (box.width - 16) * n / 4;
  const y = box.y + box.height / 2;
  await page.mouse.move(x(0), y);
  await page.waitForTimeout(500);
  await page.mouse.down();
  const seen = [];
  for (const n of [1, 2, 3, 4]) {
    await page.mouse.move(x(n), y, { steps: 12 });
    await page.waitForTimeout(850);
    seen.push(await page.evaluate(() => window.__dragonSkinV1.status()));
  }
  await page.mouse.up();
  await page.waitForTimeout(1200);
  seen.push(await page.evaluate(() => window.__dragonSkinV1.status()));
  await page.getByRole('button', { name: '☾ Dark' }).click();
  await page.waitForTimeout(1000);
  await page.mouse.move(x(4), y);
  await page.mouse.down();
  for (const n of [3, 2, 1, 0]) {
    await page.mouse.move(x(n), y, { steps: 10 });
    await page.waitForTimeout(400);
  }
  await page.mouse.up();
  await page.waitForTimeout(650);
  await page.getByRole('button', { name: '☀ Light' }).click();
  await page.waitForTimeout(550);
  await page.mouse.move(400, 565);
  await page.waitForTimeout(350);
  return seen.map(s => ({ status: s.status, stage: s.stage, source: s.source }));
}
