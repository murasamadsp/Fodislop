#!/usr/bin/env node
"use strict";
const { spawnSync } = require("node:child_process");
const result = spawnSync("dotnet", ["run", "--project", "tools/terrain-crystal-tests/GenerateLava", "--no-restore"], { stdio: "inherit" });
process.exitCode = result.status ?? 1;
