// Version releases retain their binary; master replaces its single development build.
module.exports = async function publish({ github, context, binary }) {
  if (!Buffer.isBuffer(binary) || binary.length === 0) throw new Error('A nonempty tested executable is required.');
  if (!['push', 'workflow_dispatch', 'release'].includes(context.eventName)) return;
  const rolling = context.ref === 'refs/heads/master' && context.eventName !== 'release';
  const tag = rolling ? 'master' : context.ref.startsWith('refs/tags/v') ? context.ref.slice(10) : null;
  if (!tag) return;
  const repo = context.repo;
  const api = github.rest;
  let release;
  try { release = (await api.repos.getReleaseByTag({ ...repo, tag })).data; }
  catch (error) { if (error.status !== 404) throw error; }

  if (!release) {
    release = (await api.repos.createRelease({ ...repo, tag_name: tag,
      target_commitish: context.sha, name: rolling ? 'master — latest build' : tag,
      body: rolling ? `Latest tested master build. Commit: ${context.sha}` : `Tested Windows 11 x64 build for ${tag}.`,
      draft: true, prerelease: rolling || tag.includes('-'), make_latest: rolling ? 'false' : 'legacy'
    })).data;
  }
  const assets = await github.paginate(api.repos.listReleaseAssets, { ...repo, release_id: release.id, per_page: 100 });
  if (rolling || !assets.some(asset => asset.name === 'L-Switcher.exe')) {
    // Upload first: if transferring the replacement fails, the previous EXE remains available.
    const name = rolling ? `L-Switcher-${context.runId}-${context.runAttempt || 1}.exe` : 'L-Switcher.exe';
    // Recover a retry of the same run/attempt without creating a duplicate pending asset.
    const pending = assets.find(asset => asset.name === name && rolling);
    if (pending) await api.repos.deleteReleaseAsset({ ...repo, asset_id: pending.id });
    const uploaded = (await api.repos.uploadReleaseAsset({ ...repo, release_id: release.id,
      name, data: binary, headers: { 'content-type': 'application/octet-stream' }
    })).data;
    if (rolling) {
      for (const asset of assets) {
        if (asset.id !== pending?.id && /^L-Switcher(?:-\d+-\d+)?\.exe$/.test(asset.name)) {
          await api.repos.deleteReleaseAsset({ ...repo, asset_id: asset.id });
        }
      }
      await api.repos.updateReleaseAsset({ ...repo, asset_id: uploaded.id, name: 'L-Switcher.exe' });
    }
  }
  if (rolling) {
    // Update only the floating tag. Version tags are never moved.
    try {
      await api.git.getRef({ ...repo, ref: 'tags/master' });
      await api.git.updateRef({ ...repo, ref: 'tags/master', sha: context.sha, force: true });
    } catch (error) {
      if (error.status !== 404) throw error;
      await api.git.createRef({ ...repo, ref: 'refs/tags/master', sha: context.sha });
    }
  }
  if (rolling || release.draft) {
    await api.repos.updateRelease({ ...repo, release_id: release.id, draft: false,
      ...(rolling ? { name: 'master — latest build', prerelease: true, make_latest: 'false',
        body: `Latest tested master build. Commit: ${context.sha}` } : {})
    });
  }
};
