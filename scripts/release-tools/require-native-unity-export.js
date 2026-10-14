"use strict";

const workflowPath = ".github/workflows/unity-tests.yml";
const jobName = "Unity package export smoke";
const requiredSteps = [
  "Acquire organization Unity lock",
  "Export Unity package smoke artifact",
  "Return Unity license",
  "Classify Unity cleanup evidence",
  "Release organization Unity lock",
  "Require confirmed Unity cleanup"
];
const pageSize = 100;
const maximumPages = 20;

async function collectPages(request, endpoint, key) {
  const items = [];
  for (let page = 1; page <= maximumPages; ++page) {
    const response = await request(`${endpoint}&per_page=${pageSize}&page=${page}`);
    const values = response?.[key];
    if (!Array.isArray(values)) {
      throw new Error(`Native Unity validation returned no ${key} array.`);
    }
    items.push(...values);
    if (values.length < pageSize) {
      return items;
    }
  }
  throw new Error("Native Unity validation exceeded its pagination limit.");
}

async function requireNativeExport(repository, sourceSha, request) {
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository ?? "")) {
    throw new Error("A repository in owner/name form is required.");
  }
  if (!/^[a-f0-9]{40}$/.test(sourceSha ?? "")) {
    throw new Error("A complete lowercase source commit SHA is required.");
  }
  const runs = await collectPages(
    request,
    `/repos/${repository}/actions/workflows/unity-tests.yml/runs?head_sha=${sourceSha}`,
    "workflow_runs"
  );
  const candidates = runs.filter(
    (run) =>
      run.head_sha === sourceSha &&
      run.repository?.full_name === repository &&
      run.head_repository?.full_name === repository &&
      run.path === workflowPath &&
      Number.isSafeInteger(run.id) &&
      run.id > 0 &&
      Number.isSafeInteger(run.run_attempt) &&
      run.run_attempt > 0
  );
  candidates.sort((left, right) => right.id - left.id);
  const run = candidates[0];
  if (!run) {
    throw new Error("No trusted Unity Tests run validates the selected source commit.");
  }
  const jobs = await collectPages(
    request,
    `/repos/${repository}/actions/runs/${run.id}/jobs?filter=latest`,
    "jobs"
  );
  const matches = jobs.filter((job) => job.name === jobName);
  if (matches.length !== 1) {
    throw new Error("Exactly one native Unity export job is required in the latest run attempt.");
  }
  const job = matches[0];
  if (
    !Number.isSafeInteger(job.id) ||
    job.id <= 0 ||
    job.run_id !== run.id ||
    job.run_attempt !== run.run_attempt ||
    job.status !== "completed" ||
    job.conclusion !== "success" ||
    !Array.isArray(job.steps)
  ) {
    throw new Error(
      "Native Unity export has not completed successfully in the latest run attempt."
    );
  }
  let previousNumber = 0;
  for (const name of requiredSteps) {
    const steps = job.steps.filter((step) => step.name === name);
    const step = steps[0];
    if (
      steps.length !== 1 ||
      step.status !== "completed" ||
      step.conclusion !== "success" ||
      !Number.isSafeInteger(step.number) ||
      step.number <= previousNumber
    ) {
      throw new Error(`Native Unity export requires a successful ordered '${name}' step.`);
    }
    previousNumber = step.number;
  }
  return { runId: run.id, jobId: job.id, sourceSha };
}

async function requestJson(endpoint, token) {
  const response = await fetch(`https://api.github.com${endpoint}`, {
    headers: {
      Accept: "application/vnd.github+json",
      Authorization: `Bearer ${token}`,
      "X-GitHub-Api-Version": "2022-11-28"
    },
    redirect: "error",
    signal: AbortSignal.timeout(30000)
  });
  if (!response.ok) {
    throw new Error(`Native Unity validation API returned HTTP ${response.status}.`);
  }
  return response.json();
}

async function main() {
  const token = process.env.GH_TOKEN;
  if (!token) {
    throw new Error("GH_TOKEN is required to read native Unity validation metadata.");
  }
  const result = await requireNativeExport(
    process.env.GITHUB_REPOSITORY,
    process.env.SOURCE_SHA,
    (endpoint) => requestJson(endpoint, token)
  );
  console.log(
    `Confirmed native Unity export and cleanup for ${result.sourceSha}: run ${result.runId}, job ${result.jobId}.`
  );
}

if (require.main === module) {
  main().catch((error) => {
    console.error(`::error::${error.message}`);
    process.exitCode = 1;
  });
}

module.exports = { requireNativeExport };
