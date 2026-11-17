// Delete only superseded executable artifacts from this repository's master branch.
module.exports = async function prune({ github, context, core, artifactId }) {
  if (context.ref !== 'refs/heads/master' ||
      !['push', 'workflow_dispatch'].includes(context.eventName)) return;

  const artifacts = await github.paginate(github.rest.actions.listArtifactsForRepo, {
    ...context.repo, per_page: 100
  });
  const current = artifacts.find(a => a.id === artifactId);
  if (!current || current.expired || current.name !== 'L-Switcher-win-x64' ||
      current.workflow_run?.id !== context.runId ||
      current.workflow_run?.head_branch !== 'master') {
    throw new Error('Current master executable artifact was not verified; retaining older builds.');
  }

  const { data: currentRun } = await github.rest.actions.getWorkflowRun({
    ...context.repo, run_id: context.runId
  });
  for (const artifact of artifacts) {
    if (artifact.id === artifactId || artifact.expired ||
        artifact.name !== current.name || !['master', 'main'].includes(artifact.workflow_run?.head_branch) ||
        artifact.workflow_run?.repository_id !== current.workflow_run.repository_id ||
        !(artifact.workflow_run?.id <= context.runId) || artifact.id > artifactId) continue;
    // Preserve release runs and artifacts produced by other workflows.
    const { data: run } = await github.rest.actions.getWorkflowRun({
      ...context.repo, run_id: artifact.workflow_run.id
    });
    if (!['push', 'workflow_dispatch'].includes(run.event) ||
        !['master', 'main'].includes(run.head_branch) || run.workflow_id !== currentRun.workflow_id) continue;
    await github.rest.actions.deleteArtifact({ ...context.repo, artifact_id: artifact.id });
    core.info(`Deleted superseded master executable artifact ${artifact.id}`);
  }
};
