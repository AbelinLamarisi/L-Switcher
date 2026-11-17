// Delete only superseded executable artifacts from this repository's main branch.
module.exports = async function prune({ github, context, core, artifactId }) {
  if (context.ref !== 'refs/heads/main' ||
      !['push', 'workflow_dispatch'].includes(context.eventName)) return;

  const artifacts = await github.paginate(github.rest.actions.listArtifactsForRepo, {
    ...context.repo, per_page: 100
  });
  const current = artifacts.find(a => a.id === artifactId);
  if (!current || current.expired || current.name !== 'L-Switcher-win-x64' ||
      current.workflow_run?.id !== context.runId ||
      current.workflow_run?.head_branch !== 'main') {
    throw new Error('Current main executable artifact was not verified; retaining older builds.');
  }

  const { data: currentRun } = await github.rest.actions.getWorkflowRun({
    ...context.repo, run_id: context.runId
  });
  for (const artifact of artifacts) {
    if (artifact.id === artifactId || artifact.expired ||
        artifact.name !== current.name || artifact.workflow_run?.head_branch !== 'main' ||
        artifact.workflow_run?.repository_id !== current.workflow_run.repository_id ||
        !(artifact.workflow_run?.id <= context.runId) || artifact.id > artifactId) continue;
    // Preserve release runs and artifacts produced by other workflows.
    const { data: run } = await github.rest.actions.getWorkflowRun({
      ...context.repo, run_id: artifact.workflow_run.id
    });
    if (!['push', 'workflow_dispatch'].includes(run.event) ||
        run.head_branch !== 'main' || run.workflow_id !== currentRun.workflow_id) continue;
    await github.rest.actions.deleteArtifact({ ...context.repo, artifact_id: artifact.id });
    core.info(`Deleted superseded main executable artifact ${artifact.id}`);
  }
};
