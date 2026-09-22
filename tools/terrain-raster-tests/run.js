#!/usr/bin/env node
"use strict";
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const project = "tools/terrain-raster-tests/RunRasterTests/RunRasterTests.csproj";
if (!fs.existsSync("tools/terrain-raster-tests/RunRasterTests/Program.cs")) {
  console.warn(`Skipping raster tests: executable source is missing for ${project}`);
  process.exitCode = 0;
} else {
  const result = spawnSync("dotnet", ["run", "--project", "tools/terrain-raster-tests/RunRasterTests"], { stdio: "inherit" });
  process.exitCode = result.status ?? 1;
}
