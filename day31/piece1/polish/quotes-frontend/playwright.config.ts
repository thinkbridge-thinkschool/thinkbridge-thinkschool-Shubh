import { defineConfig, devices } from '@playwright/test';

// Day 31 E2E: the frontend (this project, served by `ng serve` on 4210) and the Day 31
// backend (day31/piece1/polish/QuotesApi, run separately against ephemeral SQL Server/Redis
// containers on 5310/16381) are both already-running services this config only points at; it
// does not start them, so the same suite can run against a backend/frontend pair started
// however the caller (a local script, or CI) chooses to start them.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  // The default 30s per-test timeout is tight once several 15s expect() waits can each hit
  // a first-request compile tax in the same test.
  timeout: 90000,
  // 15s (vs. the 5s default) for every expect(): the Angular dev server (Vite) and the .NET
  // backend both JIT-compile lazily on first hit per route/endpoint in a session, which can
  // add real, one-time latency to the very first request against a freshly (re)started pair
  // — this is a slower dev-server/JIT reality, not a flaky test or a slow app in production.
  expect: {
    timeout: 15000,
  },
  use: {
    baseURL: 'http://localhost:4210',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
