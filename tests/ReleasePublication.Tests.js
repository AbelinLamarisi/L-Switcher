const assert = require('node:assert/strict');
const publish = require('../.github/scripts/publish-release.js');
async function scenario({ ref = 'refs/heads/master', eventName = 'push', assets = [], exists = true, failUpload = false }) {
  const calls = [];
  const methods = {};
  for (const name of ['getReleaseByTag', 'createRelease', 'listReleaseAssets', 'deleteReleaseAsset',
    'uploadReleaseAsset', 'updateReleaseAsset', 'updateRelease', 'getRef', 'updateRef', 'createRef']) {
    methods[name] = async args => {
      calls.push({ name, args });
      if (name === 'getReleaseByTag' && !exists) throw Object.assign(new Error('missing'), { status: 404 });
      if (name === 'uploadReleaseAsset' && failUpload) throw new Error('Upload failed');
      return { data: { id: name === 'uploadReleaseAsset' ? 999 : 1, draft: !exists } };
    };
  }
  const task = () => publish({ github: { rest: { repos: methods, git: methods }, paginate: async () => assets },
    context: { repo: { owner: 'test', repo: 'test' }, ref, eventName, sha: 'abc123', runId: 20, runAttempt: 1 },
    binary: Buffer.from('tested executable') });
  if (failUpload) await assert.rejects(task); else await task();
  return calls;
}
(async () => {
  let calls = await scenario({ assets: [{ id: 3, name: 'L-Switcher.exe' }] });
  assert(calls.find(c => c.name === 'updateRef' && c.args.ref === 'tags/master'));
  assert(calls.findIndex(c => c.name === 'uploadReleaseAsset') < calls.findIndex(c => c.name === 'deleteReleaseAsset'));
  assert(calls.some(c => c.name === 'updateRelease' && c.args.prerelease && c.args.make_latest === 'false'));
  calls = await scenario({ assets: [{ id: 3, name: 'L-Switcher.exe' }], failUpload: true });
  assert(!calls.some(c => c.name === 'deleteReleaseAsset'));
  calls = await scenario({ ref: 'refs/tags/v0.2.0', exists: false });
  assert(calls.some(c => c.name === 'createRelease' && c.args.tag_name === 'v0.2.0' && !c.args.prerelease));
  assert(calls.some(c => c.name === 'uploadReleaseAsset' && c.args.name === 'L-Switcher.exe'));
  assert(!calls.some(c => c.name === 'updateRef'));
  calls = await scenario({ ref: 'refs/tags/v0.2.0', assets: [{ id: 3, name: 'L-Switcher.exe' }] });
  assert(!calls.some(c => /upload|delete|update/.test(c.name)));
  assert.deepEqual(await scenario({ eventName: 'pull_request' }), []);
  assert.deepEqual(await scenario({ ref: 'refs/heads/feature', eventName: 'workflow_dispatch' }), []);
  calls = await scenario({ ref: 'refs/tags/v0.2.0-beta.1', exists: false, eventName: 'release' });
  assert(calls.some(c => c.name === 'createRelease' && c.args.prerelease));
  console.log('PASS: release publication (rolling replacement, upload failure, immutable versions, PR/branch filters, prereleases)');
})().catch(error => { console.error(error); process.exitCode = 1; });
