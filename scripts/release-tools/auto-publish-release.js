"use strict";

const fs = require("node:fs");

const requiredWorkflows = ["unity-tests.yml", "local-gates.yml", "repo-lint.yml"];
const marker = "<!-- unity-helpers:auto-publish -->";

function releaseCandidate(event, pulls, version, repository) {
  const run = event.workflow_run;
  const branch = event.repository?.default_branch;
  if (
    event.repository?.fork ||
    event.repository?.full_name !== repository ||
    run?.event !== "push" ||
    run?.head_branch !== branch ||
    run?.head_repository?.full_name !== repository ||
    !["success", "skipped"].includes(run?.conclusion) ||
    !/^[a-f0-9]{40}$/.test(run?.head_sha ?? "") ||
    !/^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(version)
  ) {
    return null;
  }
  const candidates = pulls.filter(
    (pull) =>
      pull.merged_at &&
      pull.merge_commit_sha === run.head_sha &&
      pull.base?.ref === branch &&
      pull.base?.repo?.full_name === repository &&
      pull.head?.repo?.full_name === repository &&
      pull.head?.ref === `release/${version}` &&
      pull.title === `release: ${version}` &&
      pull.body?.split(/\r?\n/).includes(marker)
  );
  return candidates.length === 1
    ? { sha: run.head_sha, version, branch, pull: candidates[0].number }
    : null;
}

function validationState(runs, sha, branch, repository) {
  const latest = new Map();
  for (const run of runs) {
    if (
      run.event !== "push" ||
      run.head_sha !== sha ||
      run.head_branch !== branch ||
      run.repository?.full_name !== repository
    ) {
      continue;
    }
    const previous = latest.get(run.path);
    if (!previous || previous.id < run.id) latest.set(run.path, run);
  }
  if (requiredWorkflows.some((file) => !latest.has(`.github/workflows/${file}`))) return "waiting";
  let pending = false;
  for (const run of latest.values()) {
    if (run.status !== "completed") pending = true;
    else if (run.conclusion !== "success" && run.conclusion !== "skipped") return "failed";
  }
  if (
    requiredWorkflows.some(
      (file) => latest.get(`.github/workflows/${file}`).conclusion === "skipped"
    )
  )
    return "failed";
  return pending ? "waiting" : "passed";
}

function runTitle(candidate) {
  return `Release ${candidate.version} from ${candidate.sha}`;
}

async function autoPublish({
  event,
  repository,
  api,
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  polls = 40
}) {
  const sha = event.workflow_run?.head_sha;
  if (
    !/^[a-f0-9]{40}$/.test(sha ?? "") ||
    event.workflow_run?.event !== "push" ||
    !["success", "skipped"].includes(event.workflow_run?.conclusion) ||
    event.repository?.fork ||
    event.repository?.full_name !== repository ||
    event.workflow_run?.head_branch !== event.repository?.default_branch ||
    event.workflow_run?.head_repository?.full_name !== repository
  )
    return { status: "ineligible" };
  const pulls = await api.pages(`commits/${sha}/pulls`);
  const content = await api.get(`contents/package.json?ref=${sha}`);
  if (content?.encoding !== "base64" || typeof content.content !== "string")
    throw new Error("Missing package metadata.");
  const version = JSON.parse(Buffer.from(content.content, "base64").toString("utf8")).version;
  const candidate = releaseCandidate(event, pulls, version, repository);
  if (!candidate) return { status: "ineligible" };
  for (let poll = 0; poll < polls; ++poll) {
    const runs = await api.pages(`actions/runs?event=push&head_sha=${sha}`, "workflow_runs");
    const state = validationState(runs, sha, candidate.branch, repository);
    if (state === "failed") return { status: "validation-failed", ...candidate };
    if (state === "passed") {
      const releases = await api.pages("releases");
      if (releases.some((release) => release.tag_name === version))
        return { status: "already-published", ...candidate };
      const publishRuns = await api.pages(
        "actions/workflows/release.yml/runs?event=workflow_dispatch",
        "workflow_runs"
      );
      const existing = publishRuns.find((run) => run.display_title === runTitle(candidate));
      if (existing) return { status: "already-dispatched", runId: existing.id, ...candidate };
      const claimRef = `refs/tags/release-publish-claims/${version}`;
      try {
        const claim = await api.post("git/refs", { ref: claimRef, sha });
        if (claim?.ref !== claimRef || claim?.object?.sha !== sha)
          throw new Error("Publication claim does not match the validated source.");
      } catch (error) {
        if (error.status !== 409 && error.status !== 422) throw error;
        const claim = await api.get(`git/ref/tags/release-publish-claims/${version}`);
        if (claim?.ref !== claimRef || !/^[a-f0-9]{40}$/.test(claim?.object?.sha ?? ""))
          throw new Error("Invalid existing publication claim.");
        return { status: "already-claimed", claimedSha: claim.object.sha, ...candidate };
      }
      await api.post("actions/workflows/release.yml/dispatches", {
        ref: candidate.branch,
        inputs: { version, source_ref: sha, export_only: "false", allow_tag_recovery: "false" }
      });
      return { status: "dispatched", ...candidate };
    }
    if (poll + 1 < polls) await sleep(15000);
  }
  return { status: "waiting-for-validation", ...candidate };
}

function createApi(repository, token, fetcher = fetch) {
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository ?? "") || !token)
    throw new Error("Repository and token are required.");
  const base = `https://api.github.com/repos/${repository}/`;
  async function request(endpoint, method = "GET", body) {
    const response = await fetcher(base + endpoint, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "Content-Type": "application/json"
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(30000),
      redirect: "error"
    });
    if (!response.ok)
      throw Object.assign(
        new Error(`GitHub ${method} ${endpoint} failed with HTTP ${response.status}.`),
        { status: response.status }
      );
    return response.status === 204 ? null : response.json();
  }
  return {
    get: (endpoint) => request(endpoint),
    post: (endpoint, body) => request(endpoint, "POST", body),
    async pages(endpoint, key) {
      const values = [];
      for (let page = 1; page <= 100; ++page) {
        const result = await request(
          `${endpoint}${endpoint.includes("?") ? "&" : "?"}per_page=100&page=${page}`
        );
        const rows = key ? result[key] : result;
        if (!Array.isArray(rows)) throw new Error("Invalid paginated GitHub response.");
        values.push(...rows);
        if (rows.length < 100) return values;
      }
      throw new Error("GitHub pagination limit reached; refusing partial release evidence.");
    }
  };
}

if (require.main === module) {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, "utf8"));
  const repository = process.env.GITHUB_REPOSITORY;
  autoPublish({ event, repository, api: createApi(repository, process.env.GITHUB_TOKEN) })
    .then((result) => {
      console.log(JSON.stringify(result));
      if (process.env.GITHUB_STEP_SUMMARY)
        fs.appendFileSync(
          process.env.GITHUB_STEP_SUMMARY,
          `Release publication: ${result.status}\n`
        );
    })
    .catch((error) => {
      console.error(error.message);
      process.exitCode = 1;
    });
}

module.exports = {
  autoPublish,
  createApi,
  marker,
  releaseCandidate,
  requiredWorkflows,
  runTitle,
  validationState
};
