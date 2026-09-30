import { spawnSync } from "node:child_process";
import { readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const frontendRoot = dirname(fileURLToPath(import.meta.url));
const temporaryOutput = join(tmpdir(), `word-search-book-tailwind-${process.pid}.css`);
const tailwindCli = join(frontendRoot, "node_modules", "tailwindcss", "lib", "cli.js");

try {
  const result = spawnSync(
    process.execPath,
    [tailwindCli, "-i", "./css/input.css", "-o", temporaryOutput, "--minify"],
    { cwd: frontendRoot, stdio: "inherit" }
  );

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }

  const normalize = value => value.replaceAll("\r\n", "\n").trim();
  const expected = normalize(readFileSync(join(frontendRoot, "css", "tailwind.css"), "utf8"));
  const actual = normalize(readFileSync(temporaryOutput, "utf8"));

  if (actual !== expected) {
    console.error("css/tailwind.css is stale. Run npm run build:css and commit the result.");
    process.exitCode = 1;
  }
} finally {
  rmSync(temporaryOutput, { force: true });
}
