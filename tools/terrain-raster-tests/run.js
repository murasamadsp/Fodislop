#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const root = path.resolve(__dirname, "../..");
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), "utf8");

const shim = read("tools/lighting-tests/NativeTransportShim.cpp");
const loader = read("Assets/Shaders/TerrainCellData.hlsl");
const terrainContour = read("Assets/Shaders/TerrainContour.hlsl");
const terrainShader = read("Assets/Shaders/Terrain.shader");
const scenario = read("tools/terrain-raster-tests/scenario.cpp");
const aoShim = read("tools/terrain-raster-tests/ao-pyramid.cpp");
const ao = read("Assets/Shaders/TerrainAmbientOcclusion.hlsl");
const lighting = read("Assets/Shaders/TerrainLightingData.hlsl");

if (terrainShader.includes("TerrainReliefRimRaw(") || terrainShader.includes("TerrainReliefRim(surface")) {
  throw new Error("Terrain.shader must not apply the retired relief rim to visible albedo");
}
if ((terrainShader.match(/BuildTerrainSurfaceInputs\(/g) ?? []).length !== 2) {
  throw new Error("Expected exactly one surface parse per terrain pass");
}
if ((terrainShader.match(/finalUV = PixelArtSampleUV\(finalUV, atlasTexelSize\.zw\);/g) ?? []).length !== 2) {
  throw new Error("Visible and lighting terrain passes must apply the same pixel-grid UV correction");
}
const debugAt = terrainShader.indexOf("KernTerrainDebugActive()");
const clipAt = terrainShader.indexOf("clip(cellCoverage - 0.5)");
if (debugAt < 0 || clipAt < 0 || debugAt > clipAt) {
  throw new Error("The terrain debug view must be returned before clip()");
}

const rim = terrainContour.slice(
  terrainContour.indexOf("// Выключатель каймы."),
  terrainContour.indexOf("// The visible terrain"),
);
const contour = terrainContour.slice(0, terrainContour.indexOf("float2 QuantizeTerrainFaceUV"));
const quantizeUv = `
float2 QuantizeTerrainFaceUV(float2 uv)
{
    float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
    return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
}
`;
const extra = `
float2 round(float2 a) { return {std::round(a.x), std::round(a.y)}; }
float lerp(float a, float b, float t) { return a + (b-a)*t; }
float2 lerp(float2 a, float2 b, float2 t) { return a + (b-a)*t; }
float4 make_float4(float a, float2 b, float c) { return {a,b.x,b.y,c}; }
`;

function translate(source) {
  return source
    .replace(/^#.*$/gm, "")
    .replaceAll("Texture2D<float4>", "Texture")
    .replaceAll("(TerrainCellVertex)0", "TerrainCellVertex{}")
    .replace(/\(int2\)round\(([^)]+)\)/g, "make_int2(round($1))")
    .replace(/\b(float[234]|int[23]|uint[23])\(/g, "make_$1(");
}

const mutations = [
  null,
  "polygon-carrier",
  "ao-wide-mip",
  "ao-to-black",
];
const temporaryDirectory = fs.mkdtempSync(path.join(os.tmpdir(), "kern-terrain-raster-"));

try {
  const cppPath = path.join(temporaryDirectory, "test.cpp");
  const executablePath = path.join(temporaryDirectory, "test");

  for (const mutation of mutations) {
    let candidateLoader = loader;
    let candidateAo = ao;

    if (mutation === "polygon-carrier") {
      candidateLoader = candidateLoader.replace(
        "carrierCorner = lerp(boundsMin, boundsMax, cornerBase);",
        "carrierCorner = float2(geometryX[corner], geometryY[corner]);",
      );
      if (candidateLoader === loader) throw new Error("polygon-carrier mutation is stale");
    }
    if (mutation === "ao-wide-mip") {
      candidateAo = candidateAo.replace(
        "max(log2(texelsPerCell) - 1.0, 0.0)",
        "1.5 + log2(texelsPerCell)",
      );
      if (candidateAo === ao) throw new Error("ao-wide-mip mutation is stale");
    }
    if (mutation === "ao-to-black") {
      candidateAo = candidateAo.replace(
        "1.0 - (occlusion * (1.0 - _TerrainAmbientOcclusionFloor))",
        "1.0 - occlusion",
      );
      if (candidateAo === ao) throw new Error("ao-to-black mutation is stale");
    }
    candidateAo = candidateAo.replaceAll("Texture2D<float4>", "AoTexture").replaceAll("SamplerState", "int");
    fs.writeFileSync(
      cppPath,
      shim + extra + aoShim +
        translate(candidateLoader + contour + lighting + quantizeUv + rim + candidateAo) +
        scenario,
    );

    const compile = spawnSync(
      "clang++",
      ["-std=c++20", "-O2", "-ffp-contract=off", cppPath, "-o", executablePath],
      { stdio: "inherit" },
    );
    if (compile.status !== 0) process.exit(compile.status ?? 1);

    const result = spawnSync(executablePath, { encoding: "utf8" });
    if (mutation === null) {
      process.stdout.write(result.stdout);
      if (result.status !== 0) throw new Error(result.stderr);
    } else {
      if (result.status === 0) throw new Error(`Regression check accepted mutation: ${mutation}`);
      console.log(`Mutation ${mutation} rejected: ${result.stderr.trim()}`);
    }
  }
} finally {
  fs.rmSync(temporaryDirectory, { recursive: true, force: true });
}
