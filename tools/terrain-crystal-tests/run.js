#!/usr/bin/env node
"use strict";
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const project = "tools/terrain-crystal-tests/RunCrystalTests/RunCrystalTests.csproj";
if (!fs.existsSync("tools/terrain-crystal-tests/RunCrystalTests/Program.cs")) {
  console.warn(`Skipping crystal tests: executable source is missing for ${project}`);
  process.exitCode = 0;
} else {
  const result = spawnSync("dotnet", ["run", "--project", "tools/terrain-crystal-tests/RunCrystalTests"], { stdio: "inherit" });
  process.exitCode = result.status ?? 1;
}
