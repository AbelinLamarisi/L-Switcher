const assert = require('node:assert/strict');
const prune = require('../.github/scripts/prune-main-artifacts.js');

const artifact = (id, run, branch = 'master', extra = {}) => ({
  id, name: 'L-Switcher-win-x64', expired: false,
  workflow_run: { id: run, head_branch: branch, repository_id: 42 }, ...extra
});
async function check({ artifacts, ref = 'refs/heads/master', event = 'push', runs = {}, expected = [], fail = false }) {
  const deleted = [];
  const github = {
    paginate: async () => artifacts,
    rest: { actions: {
      listArtifactsForRepo: () => {},
      getWorkflowRun: async ({ run_id }) => ({ data: {
        event: 'push', head_branch: 'master', workflow_id: 1, ...runs[run_id]
      } }),
      deleteArtifact: async ({ artifact_id }) => deleted.push(artifact_id)
    } }
  };
  const task = () => prune({ github, context: {
    repo: { owner: 'test', repo: 'test' }, ref, eventName: event, runId: 200
  }, core: { info: () => {} }, artifactId: 20 });
  if (fail) await assert.rejects(task); else await task();
  assert.deepEqual(deleted, expected);
}
(async () => {
  const current = artifact(20, 200);
  await check({ artifacts: [current, artifact(10, 100)], expected: [10] });
  await check({ artifacts: [current, artifact(10, 100, 'v0.1.0'), artifact(11, 100, 'feature'),
    artifact(12, 100, 'master', { name: 'L-Switcher-test-results' }),
    artifact(21, 201), artifact(22, 200), artifact(9, 100, 'master', { expired: true })] });
  await check({ artifacts: [current, artifact(10, 100)], runs: { 100: { event: 'release' } } });
  await check({ artifacts: [current, artifact(10, 100)], runs: { 100: { workflow_id: 2 } } });
  await check({ artifacts: [artifact(10, 100)], fail: true });
  await check({ artifacts: [current, artifact(10, 100)], event: 'pull_request' });
  await check({ artifacts: [current, artifact(10, 100)], ref: 'refs/tags/v0.1.0' });
  await check({ artifacts: [current, artifact(10, 100)], event: 'workflow_dispatch', expected: [10] });
  await check({ artifacts: [artifact(20, 200, 'master', { expired: true }), artifact(10, 100)], fail: true });
  await check({ artifacts: [current, artifact(10, 100, 'main')], runs: { 100: { head_branch: 'main' } }, expected: [10] });
  console.log('PASS: artifact retention (replacement, protected artifacts, missing upload, events, workflows, concurrency)');
})().catch(error => { console.error(error); process.exitCode = 1; });
